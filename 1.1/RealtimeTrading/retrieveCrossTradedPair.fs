module RealtimeTrading.RetrieveCrossTradedPair

open System.Reflection.Metadata
open System.Text.Json
open Azure.Data.Tables
open Microsoft.FSharp.Collections
open Microsoft.FSharp.Core
open Newtonsoft.Json
open Suave
open System.Net.Http
open Suave.RequestErrors
open FSharp.Data

open Util
open Util.ExchangeDataParser
open Util.DAC
open Util.Logger

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

let retrieveData=
    async {
        try
            let! entity = table.GetEntityAsync<TableEntity>("common", "crossTradedPairs") |> Async.AwaitTask
            return Some (entity.Value.ToString())
        with
        | :? Azure.RequestFailedException as ex when ex.Status = 404 ->
            printfn "Entity not found: %s" ex.Message
            return None
    }

let retrieveCrossTradedPairsHandler (ctx: HttpContext) : Async<HttpContext option> = 
    async {
        let databaseResult = retrieveData |> Async.RunSynchronously
        match databaseResult with
        | Some entity-> return! Successful.OK entity ctx
        | None ->
                let saver = DAC.UpsertTableString "common"
                logger "retrieveCrossTradedPairsHandler Start"
                let! task = fetchAllCrossTradedExchangeData ()
                logger "retrieveCrossTradedPairsHandler End"
                

                match task with
                | Ok pairs->
                    (saver (System.Text.Json.JsonSerializer.Serialize(pairs)) "crossTradedPairs") |>Async.RunSynchronously  |> ignore
                    return! Successful.OK (JsonConvert.SerializeObject(pairs)) ctx
                | Error err -> return! BAD_REQUEST "Error" ctx
    }