module RealtimeTrading.Service

open FSharp.Data.HttpStatusCodes
open RealtimeTrading.Core
open RealtimeTrading.Infrastructure
open RealtimeTrading.RealtimeDataSocket
open RealtimeTrading.HistoricalOpportunities
open TradingStrategy.Infrastructure
open RealtimeTrading.RealtimeTrading
open Newtonsoft.Json
open Suave.Successful
open Suave
open Suave.RequestErrors
open Util.Logger

let subscribeToQuotes (apiKey: string) (endpoint: string) =
    let strategy = getTradingStrategy strategyAgent
    let pairs = getTopNOpportunities strategy.NumberOfCryptos |> Async.RunSynchronously
    MarketDataService.subscribeToMarketData endpoint apiKey pairs handleQuote
    |> Async.Start

let realtimeDataFeedBeginController =
    request (fun ctx ->
        let body =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
        match body with
            | "start" ->
                logger "Starting realtime trading"
                realTimeTradingStatusAgent.Post(Start)
                subscribeToQuotes "" "wss://one8656-live-data.onrender.com/"
                OK "Realtime trading started"
            | "stop" ->
                logger "Stop realtime trading"
                realTimeTradingStatusAgent.Post(Stop)
                OK "Realtime trading stopped"
            | _ -> BAD_REQUEST "Invalid request")
    
let getHistoricalOpportunitiesController =
    request (fun ctx ->
        let strategy = getTradingStrategy strategyAgent
        let pairs = getTopNOpportunities strategy.NumberOfCryptos |> Async.RunSynchronously
        OK(JsonConvert.SerializeObject(pairs)))