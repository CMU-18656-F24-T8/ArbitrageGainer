module Controller.orderController

open System
open FSharp.Data
open System.IO
open Suave
open System.Net.Http
open Suave.RequestErrors
open System.Text.Json
open System.Text
open Newtonsoft.Json

let httpClient = new HttpClient()

type TransactionType =
    | Buy
    | Sell

type orderProcessingType =
    | Transaction of TransactionType
    | Retrieve
    
type orderInformation ={
    exchangeName: string;
    currencyPair: string;
    amount: float
    price: float
}

type OrderError =
    | NewWorkError of string * string
    | ParsingError of string * string


let rawTrading = File.ReadAllText("Datas/tradingWebsites.json")
let tradingWebsite = JsonValue.Parse(rawTrading)

let parseSubmitURL (url:string) taskName exchangeName =
    match exchangeName with
    | "BitStamp" -> url.Replace("{market_symbol}", exchangeName.Replace("-","").ToLower())
    | _ -> url
    
    
let parseRetrieveURL (url:string) (orderInfo: orderInformation) (orderID:string) =
    match orderInfo.exchangeName with 
    | "Bitfinex" -> url.Replace("{symbol}", ("t" + orderInfo.currencyPair).Split "-" |> String.concat "").Replace("{id}",orderID)
    | _ -> url
    
    
let constructBitfinexPostBody (orderInfo: orderInformation) =
    System.Text.Json.JsonSerializer.Serialize({|
        ``type`` = "EXCHANGE LIMIT" 
        price = orderInfo.price 
        symbol = ("t" + orderInfo.currencyPair).Split "-" |> String.concat "" 
        amount = orderInfo.amount
    |})

let constructKrakenPostBody (orderInfo: orderInformation) (taskName: string) =
    System.Text.Json.JsonSerializer.Serialize({|
        nouce = DateTime.Now.ToString()
        ordertype = "limit"
        ``type`` = taskName
        pair = orderInfo.currencyPair.Replace("-","")
        volume = orderInfo.amount
        price = orderInfo.price
    |})

let constructBitstampPostBody (orderInfo: orderInformation)  =
        System.Text.Json.JsonSerializer.Serialize({|
        price = orderInfo.price
        amount = orderInfo.amount                      
        |})
        

let constructPostBody (orderInfo: orderInformation) (taskName: string) =
    match orderInfo.exchangeName with
    | "Bitfinex" -> Ok(constructBitfinexPostBody orderInfo)
    | "Kraken" -> Ok(constructKrakenPostBody orderInfo taskName)
    | "BitStamp" -> Ok(constructBitstampPostBody orderInfo)
    | _ -> Error (ParsingError(taskName, orderInfo.exchangeName))

// Infrastructure submit
let submitOrder (orderInfo: orderInformation) (taskName: string)=
    async{
        try
            let apiDest = tradingWebsite.GetProperty(orderInfo.exchangeName).GetProperty(taskName).AsString()
            let url = parseSubmitURL apiDest taskName orderInfo.exchangeName
            let body = constructPostBody orderInfo taskName
            match body with
            | Ok bodyResult -> use content = new StringContent(bodyResult, Encoding.UTF8, "application/json")
                               let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
                               match response.IsSuccessStatusCode with
                               | false -> return Error (NewWorkError(taskName, orderInfo.exchangeName))
                               | true ->
                                        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                                        let data = JsonValue.Parse(content)
                                        return Ok data

            | Error bodyResult -> return Error (ParsingError(taskName, orderInfo.exchangeName))
        with
        | ex -> return Error (ParsingError(taskName, orderInfo.exchangeName))
    }

// Infrastructure - Retrieve
let submitRetrieval (orderInfo: orderInformation) (buySellData: JsonValue)=
    async{
        try
            let orderId =
                match orderInfo.exchangeName with
                | "Bitfinex" -> buySellData.[3].ToString()
                | "Kraken" -> buySellData.["txid"].ToString()
                | "Bitstamp" -> buySellData.["id"].ToString()
                
              
            let apiDest = tradingWebsite.GetProperty(orderInfo.exchangeName).["retrieve"].AsString()
            let url = parseRetrieveURL apiDest orderInfo orderId
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            match response.IsSuccessStatusCode with
                   | false -> return Error (NewWorkError("retrieve", orderInfo.exchangeName))
                   | true ->
                            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            let data = JsonValue.Parse(content)
                            return Ok orderId
        with
        | ex -> return Error (ParsingError("retrieve", orderInfo.exchangeName))

    }    
//To add data saver
let generateOrderPlacement (orderInformation : orderInformation) (taskName: string)=
    async{
        let! orderPlacement = submitOrder orderInformation taskName
        match orderPlacement with
        | Ok result ->  
            do! Async.Sleep(5000)
            return! submitRetrieval orderInformation result
        | Error err -> 
            return Error err
    }


let genOrderErrorMessage (e: OrderError) =
    match e with
    | NewWorkError(task, exchange) -> sprintf "Network error when %s at %s" task exchange
    | ParsingError(task, exchange) -> sprintf "Parsing error when %s at %s" task exchange
    | _ -> "Error"
    
let orderHandler (buyOrderInformation : orderInformation) (sellOrderInformation: orderInformation) (ctx: HttpContext)=
    async {
            
            let! results = 
                            [
                                generateOrderPlacement buyOrderInformation "buy"
                                generateOrderPlacement sellOrderInformation "sell"
                            ] 
                            |> Async.Parallel
            let finalResults = results |> Array.map (fun rst ->
                                                        match rst with
                                                        | Ok data -> [true, data]
                                                        | Error e -> [false, genOrderErrorMessage e])
            
            let out = JsonConvert.SerializeObject(finalResults)
            
            let ctx =
                { ctx with
                    response =
                        { ctx.response with
                            status = HTTP_200.status
                            content = Bytes (System.Text.Encoding.UTF8.GetBytes out)
                            headers = ("Content-Type", "application/json") :: ctx.response.headers
                        }
                }
            return Some ctx
    }

