open System
open System.Net.Http
open Controller
open Microsoft.FSharp.Core
open Suave
open Suave.Filters
open Suave.Operators

open RealtimeTrading.RetrieveCrossTradedPair
open Controller.AnnualizedReturnCalculate
open TradingStrategy.Service
open TradingStrategy.Infrastructure
open RealtimeTrading.Service

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
              POST >=> path "/realtime" >=> realtimeDataFeedBeginController 
            ]
    startWebServer defaultConfig app
    0


// Test script for realtime trading
// let main _ =
//     insertEntity strategyAgent { NumberOfCryptos = 5; MinPriceSpread = 0.05; MinTransactionProfit = 5; MaxTransactionValue = 2000.0; MaxTradingValue = 5000.0; InitialInvestment = 1000.0 }
//     subscribeToQuotes()
//     Console.ReadLine() |> ignore
//     0
