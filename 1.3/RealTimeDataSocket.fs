open System
open System.Net
open System.Net.WebSockets
open System.Text.Json
open System.Threading
open System.Text

type CryptoTrade = {
    price: decimal
    size: decimal
    exchange: int
    timestamp: int64
}

type PolygonMessage = {
    ev: string  // Event type
    pair: string
    p: decimal  // Price
    s: decimal  // Size
    x: int      // Exchange ID 
    t: int64    // Timestamp
}

type TradingParameters = {
    MinimalSpread: decimal
    MinimalProfit: decimal
    MaxTotalTransactionValue: decimal
    MaxTradingValue: decimal
}

type WebSocketError =
    | ConnectionFailed of string
    | SendFailed of string
    | ReceiveFailed of string
    | InvalidMessage of string

// Create price cache
type PriceCache = {
    LastTrades: Map<string * int, CryptoTrade>
}

let createCache() = { LastTrades = Map.empty }
let mutable currentCache = createCache()

//Define a function to connect to the WebSocket
let connectToWebSocket (uri: Uri) : Async<Result<ClientWebSocket, WebSocketError>> =
    async {
        try
            let wsClient = new ClientWebSocket()
            do! Async.AwaitTask (wsClient.ConnectAsync(uri, CancellationToken.None))
            return Ok wsClient
        with
        | ex -> return Error (ConnectionFailed ex.Message)
    }

let processMessage (message: string) (tradingParams: TradingParameters) : unit =
    try
        let msg = JsonSerializer.Deserialize<PolygonMessage>(message)
        match msg.ev with
        | "XT" -> // Trade event
            let trade = {
                price = msg.p
                size = msg.s
                exchange = msg.x
                timestamp = msg.t
            }
            // Update cache
            currentCache <- { 
                LastTrades = currentCache.LastTrades.Add((msg.pair, msg.x), trade) 
            }
            
            // Check if trade meets strategy parameters
            if trade.price > tradingParams.MinimalSpread then
                printfn "Potential trading opportunity found for %s at price %M" msg.pair trade.price
        | _ -> ()
    with
    | ex -> printfn "Error processing message: %s" ex.Message

let rec receiveData (wsClient: ClientWebSocket) (tradingParams: TradingParameters) = 
    async {
        let buffer = Array.zeroCreate 10024
        try
            let segment = new ArraySegment<byte>(buffer)
            let! result = wsClient.ReceiveAsync(segment, CancellationToken.None) |> Async.AwaitTask

            match result.MessageType with
            | WebSocketMessageType.Text ->
                let message = Encoding.UTF8.GetString(buffer, 0, result.Count)
                processMessage message tradingParams
                return! receiveData wsClient tradingParams
            | WebSocketMessageType.Close ->
                printfn "WebSocket connection closed"
            | _ -> 
                return! receiveData wsClient tradingParams
        with
        | ex -> printfn "Error receiving data: %s" ex.Message
    }

let sendMessage (wsClient: ClientWebSocket) (message: string) =
    let messageBytes = Encoding.UTF8.GetBytes(message)
    wsClient.SendAsync(
        new ArraySegment<byte>(messageBytes), 
        WebSocketMessageType.Text, 
        true, 
        CancellationToken.None) 
    |> Async.AwaitTask 
    |> Async.RunSynchronously

// Main function to start market data subscription
let startMarketDataFeed (apiKey: string) (pairs: string list) (tradingParams: TradingParameters) =
    async {
        let uri = Uri("wss://socket.polygon.io/crypto")
        let! wsClientResult = connectToWebSocket uri
        
        match wsClientResult with
        | Ok wsClient ->
            // Authenticate
            let authMsg = sprintf """{"action":"auth","params":"%s"}""" apiKey
            sendMessage wsClient authMsg
            
            // Subscribe to pairs
            let pairsStr = String.concat "," pairs
            let subMsg = sprintf """{"action":"subscribe","params":"%s"}""" pairsStr
            sendMessage wsClient subMsg
            
            // Start receiving data
            do! receiveData wsClient tradingParams
        | Error err ->
            printfn "Failed to connect: %A" err
    }

[<EntryPoint>]
let main args =
    let tradingParams = {
        MinimalSpread = 0.01m
        MinimalProfit = 0.005m
        MaxTotalTransactionValue = 100000m
        MaxTradingValue = 10000m
    }
    let uri = Uri("wss://socket.polygon.io/crypto")
    let apiKey = "OZpD8OUeBy5zWFQ5v3Hd_BEopvquAvSt"
    let subscriptionParams = ["XT.BTC-USD"]
    startMarketDataFeed apiKey subscriptionParams tradingParams 
    |> Async.RunSynchronously
    0
