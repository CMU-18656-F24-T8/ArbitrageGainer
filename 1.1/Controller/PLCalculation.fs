module Controller.PLCalculation

open System
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json

// Core Domain Types
type OrderType = Buy | Sell
type OrderId = OrderId of int64
type UserId = UserId of int64
type Amount = Amount of decimal
type Price = Price of decimal

type MatchStatus = 
    | FullyMatched
    | PartiallyMatched of remainingAmount: decimal
    | Unmatched

type MatchedTrade = {
    OrderId: OrderId
    UserId: UserId
    Type: OrderType
    Amount: Amount
    Price: Price
    MatchedAmount: Amount
    MatchedPrice: Price
    Status: MatchStatus
    Timestamp: DateTime
}

type PLThreshold = {
    UserId: UserId
    Target: decimal option
    IsActive: bool
}

type PLError =
    | InvalidThreshold
    | UserNotFound
    | CalculationError
    | InvalidOrderState

type Result<'T> = 
    | Ok of 'T 
    | Error of PLError

// Agent Messages
type PLMessage =
    | CalculatePL of AsyncReplyChannel<Result<decimal>>
    | UpdateThreshold of decimal option * AsyncReplyChannel<Result<unit>>
    | GetCurrentPL of AsyncReplyChannel<Result<decimal>>

// Agent State
type PLState = {
    CurrentValue: decimal
    Threshold: decimal option
}

// API Types for Requests/Responses
type PLCalculateRequest = {
    Trades: MatchedTrade list
}

type PLResponse = {
    Value: decimal
    Timestamp: DateTime
    IsThresholdBreached: bool option
}

type ThresholdUpdateRequest = {
    Target: decimal option
}

// JSON helpers
let toJson v = JsonConvert.SerializeObject v
let fromJson<'T> json = JsonConvert.DeserializeObject<'T>(json)

// Core calculation module
module PLCalculation =
    let calculateTradePL (trade: MatchedTrade) : Result<decimal> =
        match trade.Status with
        | Unmatched -> Error InvalidOrderState
        | FullyMatched | PartiallyMatched _ ->
            let (Amount amount) = trade.MatchedAmount
            let (Price price) = trade.Price
            let (Price matchedPrice) = trade.MatchedPrice
            
            let pl = 
                match trade.Type with
                | Buy -> amount * (matchedPrice - price)
                | Sell -> amount * (price - matchedPrice)
            Ok pl

    let calculateTotalPL (trades: MatchedTrade list) : Result<decimal> =
        let folder state tradeResult =
            match state, tradeResult with
            | Ok acc, Ok value -> Ok (acc + value)
            | Error e, _ -> Error e
            | _, Error e -> Error e

        trades
        |> List.map calculateTradePL
        |> List.fold folder (Ok 0M)

    let isThresholdReached (threshold: decimal) (currentPL: decimal) : bool =
        currentPL >= threshold

// Agent handler
let plAgent (strategyAgent: MailboxProcessor<_>) = 
    MailboxProcessor.Start(fun inbox ->
        let rec loop (state: PLState) = async {
            let! msg = inbox.Receive()
            
            match msg with
            | CalculatePL reply ->
                // In real implementation, get trades from strategyAgent
                let dummyTrade = {
                    OrderId = OrderId 1L
                    UserId = UserId 1L
                    Type = Buy
                    Amount = Amount 100M
                    Price = Price 10M
                    MatchedAmount = Amount 100M
                    MatchedPrice = Price 15M
                    Status = FullyMatched
                    Timestamp = DateTime.UtcNow
                }
                
                let result = PLCalculation.calculateTradePL dummyTrade
                reply.Reply(result)
                return! loop state
                
            | UpdateThreshold (threshold, reply) ->
                match threshold with
                | Some value when value <= 0M ->
                    reply.Reply(Error InvalidThreshold)
                | _ ->
                    let newState = { state with Threshold = threshold }
                    reply.Reply(Ok())
                    return! loop newState
                    
            | GetCurrentPL reply ->
                reply.Reply(Ok state.CurrentValue)
                return! loop state
        }
        
        loop { CurrentValue = 0M; Threshold = None })

// Suave handlers
let calculatePLHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun ctx ->
        try
            let result = agent.PostAndReply(fun reply -> CalculatePL reply)
            match result with
            | Ok value ->
                let response = {
                    Value = value
                    Timestamp = DateTime.UtcNow
                    IsThresholdBreached = None
                }
                OK (toJson response)
                >=> Writers.setMimeType "application/json; charset=utf-8"
            | Error err -> 
                BAD_REQUEST (sprintf "Error calculating PL: %A" err)
        with ex ->
            BAD_REQUEST ex.Message)

let updateThresholdHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun ctx ->
        try
            let body = System.Text.Encoding.UTF8.GetString(ctx.request.rawForm)
            let request = fromJson<ThresholdUpdateRequest> body
            
            let result = agent.PostAndReply(fun reply -> 
                UpdateThreshold(request.Target, reply))
            
            match result with
            | Ok _ ->
                OK (toJson {| Success = true; NewThreshold = request.Target |})
                >=> Writers.setMimeType "application/json; charset=utf-8"
            | Error err -> 
                BAD_REQUEST (sprintf "Error updating threshold: %A" err)
        with ex ->
            BAD_REQUEST ex.Message)

let getCurrentPLHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun _ ->
        let result = agent.PostAndReply(fun reply -> GetCurrentPL reply)
        match result with
        | Ok value ->
            let response = {
                Value = value
                Timestamp = DateTime.UtcNow
                IsThresholdBreached = None
            }
            OK (toJson response)
            >=> Writers.setMimeType "application/json; charset=utf-8"
        | Error err -> 
            BAD_REQUEST (sprintf "Error getting current PL: %A" err))

// Main webpart
let plWebPart (agent: MailboxProcessor<PLMessage>) =
    choose [
        POST >=> path "/pl/calculate" >=> calculatePLHandler agent
        POST >=> path "/pl/threshold" >=> updateThresholdHandler agent
        GET >=> path "/pl/current" >=> getCurrentPLHandler agent
    ]