module RealtimeTrading.HistoricalOpportunities

open RealtimeTrading.RealtimeDataSocket
open FSharp.Data
open Suave.Logging
open Util.Logger
open Util.DAC
open Newtonsoft.Json
open RealtimeTrading.Infrastructure
// Static
let TEXT_FILE = "/app/Data/historicalData.txt"
let BUCKET_SIZE = 5L // milliseconds


type Quotes = JsonProvider<"""[{"ev":"XQ","pair":"MKR-USD","lp":0.0,"ls":0.0,"bp":1012.5,"bs":50.0,"ap":1010.5,
"as":50.0,"t":1690409119848,"x":2,"r":1690409119856}]""">

let data = System.IO.File.ReadAllText(TEXT_FILE)
let quotes = Quotes.Parse(data)

// Map
let mapFunction (bucketQuotes: seq<Quotes.Root>) =
    bucketQuotes
    |> Seq.groupBy (fun q -> q.Pair)
    |> Seq.filter (fun (_, quotes) -> quotes |> Seq.map (fun q -> q.X) |> Seq.distinct |> Seq.length > 1)
    |> Seq.map (fun (pair, quotes) ->
        let quotesByExchange =
            quotes
            |> Seq.groupBy (fun q -> q.X)
            |> Seq.map (fun (exchange, exchangeQuotes) ->
                let highestBidQuote = exchangeQuotes |> Seq.maxBy (fun q -> q.Bp)
                (exchange, highestBidQuote))
        (pair, quotesByExchange))


// Reduce
let reduceFunction (pair: string, exchangeQuotes: seq<int * Quotes.Root>) =
    let opportunities =
        exchangeQuotes
        |> Seq.collect (fun (_, q1) ->
            exchangeQuotes
            |> Seq.toList
            |> Seq.collect (fun (_, q2) ->
                match float q1.Bp - float q2.Ap > 0.01, float q2.Bp - float q1.Ap > 0.01 with
                | true, _ -> [(pair, 1)]
                | _, true -> [(pair, 1)]
                | _ -> []))
        |> Seq.length
        |> fun x -> x / 2
    (pair, opportunities)

// Main logic
let opportunitiesPerPair =
    quotes
    |> Seq.groupBy (fun q ->  q.T / BUCKET_SIZE)
    |> Seq.map (fun (_, bucketQuotes) ->
        mapFunction bucketQuotes
        |> Seq.map reduceFunction)
    |> Seq.concat
    |> Seq.groupBy fst
    |> Seq.map (fun (pair, ops) ->
        let totalOpportunities = ops |> Seq.sumBy snd
        (pair, totalOpportunities))
    
let calcTopNOpportunities n =
    opportunitiesPerPair
    |> Seq.sortByDescending snd
    |> Seq.take n
    |> Seq.map (fun (pair, _) -> CurrencyPair pair)
    |> Seq.toList

let getTopNOpportunities n = async {
    let! oppStr = LoadStringFromDB "opportunities" (string n)
    printfn "oppStr: %A" oppStr
    match oppStr with
    | Ok str -> 
        return JsonConvert.DeserializeObject<CurrencyPair list>(str)
    | _ ->
        logger "Start calculating top opportunities"
        let res = calcTopNOpportunities n
        logger "Finish calculating top opportunities"
        let resStr = JsonConvert.SerializeObject res
        WriteJsonToTableRow "opportunities" (string n) resStr |> ignore
        return res
}