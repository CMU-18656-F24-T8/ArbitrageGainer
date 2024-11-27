﻿module Controller.orderPlacementHandler

open System
open System.Collections.Generic
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

type FinishType =
| Full
| Partial

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
let submitOrder (cryptoQuote: CryptoQuoteOnTransact) (taskName: string)=
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
                                        return Ok data

            | Error bodyResult -> return Error (ParsingError(taskName, cryptoQuote.Exchange))
        with
        | ex -> return Error (ParsingError(taskName, cryptoQuote.Exchange))
    }

// Infrastructure - Retrieve
let submitRetrieval (cryptoQuote: CryptoQuoteOnTransact) (buySellData: JsonValue)=
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
                            return Ok data
        with
        | ex -> return Error (ParsingError("retrieve", cryptoQuote.Exchange))

    }
    
let getRemaingAmount (sub: JsonValue) (ret:JsonValue) (exchangeName:String) =
    match exchangeName with
    | "Bitfinex" ->
        let origAmount = sub.[3].[6].AsFloat()
        let exeAmount = ret.[4].AsFloat()
        origAmount-exeAmount
    | "Kraken" ->
        let txid = extractID sub exchangeName
        ret.["result"].[txid].["vol"].AsFloat()-ret.["result"].[txid].["vol_exec"].AsFloat()
    | "BitStamp" ->
        ret.["amount_remaining"].AsFloat()
    
        
//To add data saver
let generateOrderPlacement (cryptoQuotermation: CryptoQuoteOnTransact) (taskName: string) saver=
    async{
        let! orderPlacement = submitOrder cryptoQuotermation taskName
        match orderPlacement with
        | Ok result ->  
            do! Async.Sleep(5000)
            let! orderRetrieval = submitRetrieval cryptoQuotermation result
            match orderRetrieval with
            | Ok result2 ->
                [result,result2] |> List.iter (fun r ->
                                            let x = System.Text.Json.JsonSerializer.Serialize(r)
                                            saver x (extractID result2 cryptoQuotermation.Exchange)|>ignore)

                let remaining = getRemaingAmount result result2 cryptoQuotermation.Exchange
                match remaining < 0.0001 with
                | true ->
                    return Ok Full
                | false ->
                    let! result3 = submitOrder { cryptoQuotermation with Size = 2.0 } taskName
                    match result3 with
                    | Ok data ->
                                let x = System.Text.Json.JsonSerializer.Serialize(data)
                                saver x (extractID result2 cryptoQuotermation.Exchange) |>ignore
                                return Ok Partial
                    | Error err -> return Error err
            | Error err -> return Error err
        | Error err -> 
            return Error err
    }


let genOrderErrorMessage (e: OrderError) =
    match e with
    | NewWorkError(task, exchange) -> sprintf "Network error when %s at %s" task exchange
    | ParsingError(task, exchange) -> sprintf "Parsing error when %s at %s" task exchange
    | DbError -> "DB has error"
    | _ -> "Error"

let orderHandler (buycryptoQuotermation : CryptoQuoteOnTransact) (sellcryptoQuotermation: CryptoQuoteOnTransact) =
    async {
            let saver = DAC.UpsertTableString "transactionHistory" 
            let! results = 
                            [
                                generateOrderPlacement buycryptoQuotermation "buy" saver
                                generateOrderPlacement sellcryptoQuotermation "sell" saver
                            ] 
                            |> Async.Parallel
            
            let failAmount =
                    (results |>  Array.filter (fun result ->
                                                match result with
                                                | Ok _ -> false
                                                | Error e -> true)).Length
            match failAmount with
            | 1 -> printfn "send an email"
            | 2 -> printfn "failed"
            | _ -> printfn "Succeed"
            return failAmount

}