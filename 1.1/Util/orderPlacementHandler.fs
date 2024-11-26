module Controller.orderPlacementHandler

open System
open FSharp.Data
open System.IO
open Suave
open System.Net.Http
open Suave.RequestErrors
open System.Text.Json
open System.Text
open Newtonsoft.Json
open Util
open Util.DAC

let httpClient = new HttpClient()

type CryptoQuoteOnTransact ={
    Exchange: string;
    Pair: string;
    Size: float
    Prices: float
}

type OrderError =
    | NewWorkError of string * string
    | ParsingError of string * string
    | DbError

let rawTrading = File.ReadAllText("Datas/tradingWebsites.json")
let tradingWebsite = JsonValue.Parse(rawTrading)

let parseSubmitURL (url:string) taskName exchangeName =
    match exchangeName with
    | "BitStamp" -> url.Replace("{market_symbol}", exchangeName.Replace("-","").ToLower())
    | _ -> url
    
    
let parseRetrieveURL (url:string) (cryptoQuote: CryptoQuoteOnTransact) (orderID:string) =
    match cryptoQuote.Exchange with 
    | "Bitfinex" -> url.Replace("{symbol}", ("t" + cryptoQuote.Pair).Split "-" |> String.concat "").Replace("{id}",orderID)
    | _ -> url
    
    
let constructBitfinexPostBody (cryptoQuote: CryptoQuoteOnTransact) =
    System.Text.Json.JsonSerializer.Serialize({|
        ``type`` = "EXCHANGE LIMIT" 
        price = cryptoQuote.Prices 
        symbol = ("t" + cryptoQuote.Pair).Split "-" |> String.concat "" 
        amount = cryptoQuote.Size
    |})

let constructKrakenPostBody (cryptoQuote: CryptoQuoteOnTransact) (taskName: string) =
    System.Text.Json.JsonSerializer.Serialize({|
        nouce = DateTime.Now.ToString()
        ordertype = "limit"
        ``type`` = taskName
        pair = cryptoQuote.Pair.Replace("-","")
        volume = cryptoQuote.Size
        price = cryptoQuote.Prices
    |})

let constructBitstampPostBody (cryptoQuote: CryptoQuoteOnTransact)  =
        System.Text.Json.JsonSerializer.Serialize({|
        price = cryptoQuote.Prices
        amount = cryptoQuote.Size                      
        |})
        

let constructPostBody (cryptoQuote: CryptoQuoteOnTransact) (taskName: string) =
    match cryptoQuote.Exchange with
    | "Bitfinex" -> Ok(constructBitfinexPostBody cryptoQuote)
    | "Kraken" -> Ok(constructKrakenPostBody cryptoQuote taskName)
    | "BitStamp" -> Ok(constructBitstampPostBody cryptoQuote)
    | _ -> Error (ParsingError(taskName, cryptoQuote.Exchange))

let extractID (response:JsonValue) exchangeName=
    match exchangeName with
    | "Bitfinex" -> response.[3].AsString()
    | "Kraken" -> response.["txid"].AsString()
    | "Bitstamp" -> response.["id"].AsString()
    
// Infrastructure submit
let submitOrder (cryptoQuote: CryptoQuoteOnTransact) (taskName: string) saver=
    async{
        try
            let apiDest = tradingWebsite.GetProperty(cryptoQuote.Exchange).GetProperty(taskName).AsString()
            let url = parseSubmitURL apiDest taskName cryptoQuote.Exchange
            let body = constructPostBody cryptoQuote taskName
            match body with
            | Ok bodyResult -> use content = new StringContent(bodyResult, Encoding.UTF8, "application/json")
                               let! response = httpClient.PostAsync(url, content) |> Async.AwaitTask
                               match response.IsSuccessStatusCode with
                               | false -> return Error (NewWorkError(taskName, cryptoQuote.Exchange))
                               | true ->
                                        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                                        let data = JsonValue.Parse(content)
                                        match saver content (extractID data cryptoQuote.Exchange)|>Async.RunSynchronously with
                                        | Ok _ -> return Ok data
                                        | Error _ -> return Error DbError

            | Error bodyResult -> return Error (ParsingError(taskName, cryptoQuote.Exchange))
        with
        | ex -> return Error (ParsingError(taskName, cryptoQuote.Exchange))
    }

// Infrastructure - Retrieve
let submitRetrieval (cryptoQuote: CryptoQuoteOnTransact) (buySellData: JsonValue) saver=
    async{
        try
            let orderId =
                match cryptoQuote.Exchange with
                | "Bitfinex" -> buySellData.[3].ToString()
                | "Kraken" -> buySellData.["txid"].ToString()
                | "Bitstamp" -> buySellData.["id"].ToString()
                
              
            let apiDest = tradingWebsite.GetProperty(cryptoQuote.Exchange).["retrieve"].AsString()
            let url = parseRetrieveURL apiDest cryptoQuote orderId
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            match response.IsSuccessStatusCode with
                   | false -> return Error (NewWorkError("retrieve", cryptoQuote.Exchange))
                   | true ->
                            let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                            let data = JsonValue.Parse(content)
                            match saver content (extractID data cryptoQuote.Exchange)|>Async.RunSynchronously with
                            | Ok _ -> return Ok orderId
                            | Error _ -> return Error DbError
        with
        | ex -> return Error (ParsingError("retrieve", cryptoQuote.Exchange))

    }    
//To add data saver
let generateOrderPlacement (cryptoQuotermation: CryptoQuoteOnTransact) (taskName: string) saver =
    async{
        let! orderPlacement = submitOrder cryptoQuotermation taskName saver
        match orderPlacement with
        | Ok result ->  
            do! Async.Sleep(5000)
            return! submitRetrieval cryptoQuotermation result saver
        | Error err -> 
            return Error err
    }


let genOrderErrorMessage (e: OrderError) =
    match e with
    | NewWorkError(task, exchange) -> sprintf "Network error when %s at %s" task exchange
    | ParsingError(task, exchange) -> sprintf "Parsing error when %s at %s" task exchange
    | _ -> "Error"
    
let orderHandler (buycryptoQuotermation : CryptoQuoteOnTransact) (sellcryptoQuotermation: CryptoQuoteOnTransact)=
    async {
            let a = """{"name":"Tom"}"""
            let saver = DAC.UpsertTableString "transactionHistory" 
            let! results = 
                            [
                                generateOrderPlacement buycryptoQuotermation "buy" saver
                                generateOrderPlacement sellcryptoQuotermation "sell" saver
                            ] 
                            |> Async.Parallel
            let finalResults = results |> Array.map (fun rst ->
                                                        match rst with
                                                        | Ok data -> [true, data]
                                                        | Error e -> [false, genOrderErrorMessage e])
            
            return finalResults

}