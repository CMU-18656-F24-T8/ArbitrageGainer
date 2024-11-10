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
open SourceDataParser

let httpClient = new HttpClient()

type WebError =
    | HttpError of string
    | ParsingError of string
    | DatabaseError of string
    
    
    
let fetchData (url:string) (fetcher: JsonValue -> (string * string) array) () =
    async {
        let! response = httpClient.GetAsync(url) |> Async.AwaitTask
        let! content = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        let data = JsonValue.Parse(content)
        let dataPair = (fetcher data)
        return dataPair |> Array.map (fun (v1,v2) -> v1 + "-" + v2)
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
        let! results = 
            urls 
            |> Map.toList
            |> List.map (fun (k, v) -> async { return (k, fetchDataFromExchange k () |> Async.RunSynchronously) })
            |> Async.Parallel
        return  results
                |> Array.map (fun (k, v) -> v)
                |> Array.concat
                |> Array.countBy (fun pair -> pair)
                |> Array.filter (fun (pairName, count) -> count > 1)
                |> Array.map (fun (pairName, count) -> pairName)
    }


[<EntryPoint>]
let main argv =
    let task = fetchAllCrossTradedExchangeData () |> Async.StartAsTask
    task.Result |> Array.iter (fun v -> printfn "Post Pro: %s" v)
    0