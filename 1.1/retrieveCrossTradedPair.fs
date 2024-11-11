module RetrieveCrossTradedPair

open Microsoft.FSharp.Collections
open Microsoft.FSharp.Core
open Suave
open System.Net.Http
open Azure.Data.Tables
open Azure
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json
open FSharp.Data
open System.IO
open ExchangeDataParser

let httpClient = new HttpClient()

type WebError =
    | HttpError of string
    | ParsingError of string
    | DatabaseError of string
    
    
    
let fetchData (url:string) (fetcher: JsonValue -> (string * string) array) () =
    async {
        try 
            let! response = httpClient.GetAsync(url) |> Async.AwaitTask
            match response.IsSuccessStatusCode with
            | false -> return Error (HttpError response.ReasonPhrase)
            | true ->
                    let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
                    let data = JsonValue.Parse(content)
                    let dataPair = (fetcher data)
                    return Ok (dataPair |> Array.map (fun (v1,v2) -> v1 + "-" + v2))
        with
        | ex -> return Error (ParsingError "Parsing wrong")
    }

let urls = 
    Map.ofList [
        "bitstamp", "https://www.bitstamp.net/api/v2/ticker/"
        "bitfinex", "https://api-pub.bitfinex.com/v2/conf/pub:list:pair:exchange"
        "kraken", "https://api.kraken.com/0/public/AssetPairs"
    ]
let fetchers = Map.ofList [
        "bitstamp", BitstampDataParser
        "bitfinex", BitfinexDataParser
        "kraken", KrakenDataParser
]

let fetchDataFromExchange (name:string) ()=
    fetchData urls.[name] fetchers.[name] ()

let fetchAllCrossTradedExchangeData ()=
    async{
        try
            let! results = 
                urls 
                |> Map.toList
                |> List.map (fun (k, v) -> async { return k, (fetchDataFromExchange k () |> Async.RunSynchronously) })
                |> Async.Parallel
            let checkResult = results |> Array.forall (fun (k,v) ->
                                                        match v with
                                                        | Ok _ -> true
                                                        | Error _-> false)
            match checkResult with
            | false -> return Error (HttpError "Error in Fetching")
            | true ->
                        return  Ok (results
                                |> Array.choose (fun (k, v) -> match v with
                                                                | Ok data -> Some data
                                                                | _ -> None) 
                                |> Array.concat
                                |> Array.countBy (fun pair -> pair)
                                |> Array.filter (fun (pairName, count) -> count > 1)
                                |> Array.map (fun (pairName, count) -> pairName))
        with
        | ex -> return Error (ParsingError "Error")
    }


let retrieveCrossTradedPairsHandler (ctx: HttpContext) : Async<HttpContext option> = 
    async {
        let! task = fetchAllCrossTradedExchangeData () 
        match task with
        | Ok pairs-> return! Successful.OK "Success" ctx
        | Error err -> return! BAD_REQUEST "Error" ctx
    }