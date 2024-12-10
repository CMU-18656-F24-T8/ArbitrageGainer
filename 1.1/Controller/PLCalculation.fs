module Controller.PLCalculation

open System
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json
open Util.DAC
open RealtimeTrading.Core

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
    | InvalidRequest of string

// P&L Agent Messages
type PLMessage =
    | StartTracking of UserId
    | StopTracking of UserId
    | UpdateTrade of MatchedTrade
    | CalculatePL of UserId * AsyncReplyChannel<Result<decimal, PLError>>
    | UpdateThreshold of UserId * decimal option * AsyncReplyChannel<Result<unit, PLError>>
    | GetCurrentPL of UserId * AsyncReplyChannel<Result<decimal, PLError>>
    | CalculateHistoricalPL of UserId * DateTime * DateTime * AsyncReplyChannel<Result<decimal, PLError>>

// P&L State
type UserPLInfo = {
    CurrentValue: decimal
    Threshold: decimal option
    IsTracking: bool
    LastUpdate: DateTime
}

type PLState = {
    UserPLs: Map<UserId, UserPLInfo>
    LastCalculation: DateTime
}

// API Types
type PLCalculateRequest = {
    UserId: int64
    StartDate: DateTime option
    EndDate: DateTime option
}

type PLResponse = {
    UserId: int64
    Value: decimal
    Timestamp: DateTime
    IsThresholdBreached: bool option
}

type ThresholdUpdateRequest = {
    UserId: int64
    Target: decimal option
}

type PLRecord = {
    UserId: int64
    Value: decimal
    Threshold: decimal option
    Timestamp: DateTime
    OrderId: int64 option
}

// JSON Helpers
let toJson v = JsonConvert.SerializeObject(v, Formatting.Indented)
let fromJson<'T> json = JsonConvert.DeserializeObject<'T>(json)

// Core calculation module
module PLCalculation =
    let calculateTradePL (trade: MatchedTrade) : Result<decimal, PLError> =
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

    let calculateTotalPL (trades: MatchedTrade list) : Result<decimal, PLError> =
        let folder state tradeResult =
            match state, tradeResult with
            | Ok acc, Ok value -> Ok (acc + value)
            | Error e, _ -> Error e
            | _, Error e -> Error e

        trades
        |> List.map calculateTradePL
        |> List.fold folder (Ok 0M)

    let isThresholdBreached (threshold: decimal option) (currentPL: decimal) : bool option =
        threshold |> Option.map (fun t -> currentPL >= t)

module Storage =
    let savePLRecord (record: PLRecord) = 
        let partitionKey = sprintf "PL_%d" record.UserId
        let rowKey = DateTime.UtcNow.Ticks.ToString()
        UpsertTable<PLRecord> partitionKey record rowKey

    let saveThreshold (userId: int64) (threshold: decimal option) =
        let partitionKey = sprintf "PLThreshold_%d" userId
        let rowKey = DateTime.UtcNow.Ticks.ToString()
        let valueToSave = 
            match threshold with 
            | Some t -> t.ToString()
            | None -> "null"
        UpsertTableString partitionKey valueToSave rowKey

// Conversion helpers
let mapStorageResult (res: Result<string, string>) : Result<unit, PLError> =
    match res with
    | Ok _ -> Ok ()
    | Error msg -> Error (InvalidRequest msg)

// PL Agent Implementation
let createPLAgent () = 
    MailboxProcessor.Start(fun inbox ->
        let rec loop (state: PLState) = async {
            let! msg = inbox.Receive()
            
            match msg with
            | StartTracking userId ->
                let newUserPLs =
                    match Map.tryFind userId state.UserPLs with
                    | Some info -> 
                        Map.add userId { info with IsTracking = true } state.UserPLs
                    | None -> 
                        Map.add userId {
                            CurrentValue = 0M
                            Threshold = None
                            IsTracking = true
                            LastUpdate = DateTime.UtcNow
                        } state.UserPLs
                return! loop { state with UserPLs = newUserPLs }

            | StopTracking userId ->
                let newUserPLs =
                    match Map.tryFind userId state.UserPLs with
                    | Some info -> 
                        Map.add userId { info with IsTracking = false } state.UserPLs
                    | None -> state.UserPLs
                return! loop { state with UserPLs = newUserPLs }

            | UpdateTrade trade ->
                try
                    let tradePLResult = PLCalculation.calculateTradePL trade
                    match tradePLResult with
                    | Ok tradePL ->
                        let! storageResult = 
                            let plRecord = {
                                UserId = match trade.UserId with UserId id -> id
                                Value = tradePL
                                Threshold = None
                                Timestamp = DateTime.UtcNow
                                OrderId = Some(match trade.OrderId with OrderId id -> id)
                            }
                            Storage.savePLRecord plRecord

                        match mapStorageResult storageResult with
                        | Ok _ ->
                            let newUserPLs =
                                match Map.tryFind trade.UserId state.UserPLs with
                                | Some info when info.IsTracking ->
                                    Map.add trade.UserId 
                                        { info with 
                                            CurrentValue = info.CurrentValue + tradePL
                                            LastUpdate = DateTime.UtcNow 
                                        } state.UserPLs
                                | _ -> state.UserPLs
                            return! loop { state with UserPLs = newUserPLs }
                        | Error _ ->
                            return! loop state
                    | Error _ ->
                        return! loop state
                with _ ->
                    return! loop state

            | CalculatePL (userId, reply) ->
                match Map.tryFind userId state.UserPLs with
                | Some info when info.IsTracking ->
                    reply.Reply(Ok info.CurrentValue)
                | Some _ ->
                    reply.Reply(Error (InvalidRequest "P&L tracking is not active for this user"))
                | None ->
                    reply.Reply(Error UserNotFound)
                return! loop state

            | UpdateThreshold (userId, threshold, reply) ->
                match threshold with
                | Some value when value <= 0M ->
                    reply.Reply(Error InvalidThreshold)
                    return! loop state
                | _ ->
                    let id = match userId with UserId id -> id
                    let! storageResult = Storage.saveThreshold id threshold
                    match mapStorageResult storageResult with
                    | Ok _ ->
                        let newUserPLs =
                            match Map.tryFind userId state.UserPLs with
                            | Some info ->
                                Map.add userId { info with Threshold = threshold } state.UserPLs
                            | None ->
                                Map.add userId {
                                    CurrentValue = 0M
                                    Threshold = threshold
                                    IsTracking = false
                                    LastUpdate = DateTime.UtcNow
                                } state.UserPLs
                        reply.Reply(Ok())
                        return! loop { state with UserPLs = newUserPLs }
                    | Error e ->
                        reply.Reply(Error e)
                        return! loop state

            | GetCurrentPL (userId, reply) ->
                match Map.tryFind userId state.UserPLs with
                | Some info -> reply.Reply(Ok info.CurrentValue)
                | None -> reply.Reply(Error UserNotFound)
                return! loop state

            | CalculateHistoricalPL (userId, startDate, endDate, reply) ->
                reply.Reply(Ok 0M)
                return! loop state
        }
        
        loop { 
            UserPLs = Map.empty
            LastCalculation = DateTime.UtcNow
        }
    )

// Suave Handlers
let calculatePLHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun ctx ->
        try
            let body = System.Text.Encoding.UTF8.GetString(ctx.request.rawForm)
            let request = fromJson<PLCalculateRequest> body
            let userId = UserId request.UserId
            
            let result = 
                agent.PostAndReply(fun reply -> CalculatePL(userId, reply))
            
            match result with
            | Ok value ->
                let response = {
                    UserId = request.UserId
                    Value = value
                    Timestamp = DateTime.UtcNow
                    IsThresholdBreached = None
                }
                OK (toJson response)
                >=> Writers.setMimeType "application/json; charset=utf-8"
            | Error err -> 
                BAD_REQUEST (sprintf "Error calculating PL: %A" err)
        with ex ->
            BAD_REQUEST ex.Message
    )

let updateThresholdHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun ctx ->
        try
            let body = System.Text.Encoding.UTF8.GetString(ctx.request.rawForm)
            let request = fromJson<ThresholdUpdateRequest> body
            let userId = UserId request.UserId
            
            let result = agent.PostAndReply(fun reply -> 
                UpdateThreshold(userId, request.Target, reply))
            
            match result with
            | Ok _ ->
                OK (toJson {| Success = true; UserId = request.UserId; NewThreshold = request.Target |})
                >=> Writers.setMimeType "application/json; charset=utf-8"
            | Error err -> 
                BAD_REQUEST (sprintf "Error updating threshold: %A" err)
        with ex ->
            BAD_REQUEST ex.Message
    )

let getCurrentPLHandler (agent: MailboxProcessor<PLMessage>) =
    warbler (fun ctx ->
        try
            match ctx.request.queryParam "userId" with
            | Choice1Of2 userIdStr ->
                match Int64.TryParse userIdStr with
                | true, userId ->
                    let result = 
                        agent.PostAndReply(fun reply -> 
                            GetCurrentPL(UserId userId, reply))
                    match result with
                    | Ok value ->
                        let response = {
                            UserId = userId
                            Value = value
                            Timestamp = DateTime.UtcNow
                            IsThresholdBreached = None
                        }
                        OK (toJson response)
                        >=> Writers.setMimeType "application/json; charset=utf-8"
                    | Error err -> 
                        BAD_REQUEST (sprintf "Error getting current PL: %A" err)
                | false, _ ->
                    BAD_REQUEST "Invalid userId format"
            | Choice2Of2 _ ->
                BAD_REQUEST "Missing userId parameter"
        with ex ->
            BAD_REQUEST ex.Message
    )

// Exports
let plAgent = createPLAgent()