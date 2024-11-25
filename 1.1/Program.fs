open System
open System.Net.Http
open Microsoft.FSharp.Core
open Suave
open Suave.Filters
open Suave.Operators

open Controller.RetrieveCrossTradedPair
open Controller.AnnualizedReturnCalculate
open Controller.RealtimeDataSocket
open TradingStrategy.Service
open TradingStrategy.Infrastructure

let apiKey = "OZpD8OUeBy5zWFQ5v3Hd_BEopvquAvSt"
let pairs = ["XQ.BTC-USD"]  
// SERVER SETUP

[<EntryPoint>]
let main argv =
    let httpClient = new HttpClient()
    let app =
        choose
            [
              POST >=> path "/trading_strategy" >=> setTradingStrategyHandler strategyAgent
              GET >=> path "/trading_strategy" >=> getTradingStrategyHandler strategyAgent
              POST >=> path "/email" >=> setEmailHandler strategyAgent
              GET >=> path "/email" >=> getEmailHandler strategyAgent
              PATCH >=> path "/trading_strategy" >=> setMaxTradingValueHandler strategyAgent 
              GET >=> path "/crosstrade" >=> retrieveCrossTradedPairsHandler
              POST >=> path "/annualized_return" >=> calculateAnnualizedReturnHandler
              GET >=> path "/realtime" >=> realtimeDataFeedBeginController 
            ]
    startWebServer defaultConfig app
    0
