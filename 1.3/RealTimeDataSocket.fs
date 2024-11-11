open System
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading

// Define types for handling incoming data
type CryptoQuote = {
    Pair: string
    AskPrice: decimal
    BidPrice: decimal
    ExchangeId: int
    Timestamp: int64
}

// Message structure for Polygon events
type PolygonMessage = {
    ev: string
    pair: string
    ap: decimal
    bp: decimal
    x: int
    t: int64
}

// Type for caching quotes
type PriceCache = {
    LastQuotes: Map<string * int, CryptoQuote>
}

// Initialize an empty cache to store the latest quotes
let mutable priceCache = { LastQuotes = Map.empty }

// Function to connect to Polygon WebSocket API
let connectToPolygon (apiKey: string) : Async<Result<ClientWebSocket, string>> =
    async {
        let uri = Uri("wss://socket.polygon.io/crypto")
        let wsClient = new ClientWebSocket()
        
        try
            // Connect to the WebSocket
            do! wsClient.ConnectAsync(uri, CancellationToken.None) |> Async.AwaitTask
            printfn "Connected to Polygon WebSocket."
            
            // Authenticate using the API key
            let authMessage = sprintf """{"action":"auth","params":"%s"}""" apiKey
            let authMessageBytes = Encoding.UTF8.GetBytes(authMessage)
            let! _ = wsClient.SendAsync(new ArraySegment<byte>(authMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None) |> Async.AwaitTask
            return Ok wsClient
        with
        | ex -> 
            return Error (sprintf "Connection failed: %s" ex.Message)
    }

// Function to subscribe to specific currency pairs for real-time updates
let subscribeToPairs (wsClient: ClientWebSocket) (pairs: string list) : Async<unit> =
    async {
        let pairsStr = String.concat "," pairs
        let subMessage = sprintf """{"action":"subscribe","params":"%s"}""" pairsStr
        let subMessageBytes = Encoding.UTF8.GetBytes(subMessage)
        do! wsClient.SendAsync(new ArraySegment<byte>(subMessageBytes), WebSocketMessageType.Text, true, CancellationToken.None) |> Async.AwaitTask
        printfn "Subscribed to pairs: %s" pairsStr
    }

// Function to process incoming WebSocket messages and update the cache
let processMessage (message: string) =
    try
        // Deserialize the incoming message to a list of PolygonMessage objects
        let quotes = JsonSerializer.Deserialize<PolygonMessage[]>(message)
        
        // Loop through each quote and update the cache
        for quote in quotes do
            if quote.ev = "XQ" then  // Only process "quote" events
                let updatedQuote = {
                    Pair = quote.pair
                    AskPrice = quote.ap
                    BidPrice = quote.bp
                    ExchangeId = quote.x
                    Timestamp = quote.t
                }
                // Update the price cache with the latest quote
                priceCache <- { LastQuotes = priceCache.LastQuotes.Add((quote.pair, quote.x), updatedQuote) }
                printfn "Cached quote for %s at %A" quote.pair updatedQuote  // Log each cached quote
    with
    | ex -> printfn "Error processing message: %s" ex.Message

// Recursive function to continuously receive data from the WebSocket
let rec receiveData (wsClient: ClientWebSocket) =
    async {
        let buffer = Array.zeroCreate 8192
        let segment = ArraySegment(buffer)
        
        let! result = wsClient.ReceiveAsync(segment, CancellationToken.None) |> Async.AwaitTask
        match result.MessageType with
        | WebSocketMessageType.Text ->
            let message = Encoding.UTF8.GetString(buffer, 0, result.Count)
            processMessage message  // Process each message received
            return! receiveData wsClient  // Recursive call for continuous retrieval
        | WebSocketMessageType.Close ->
            printfn "WebSocket connection closed."
        | _ -> return! receiveData wsClient  // Ignore other message types and continue
    }

// Function to initialize and start the market data feed
let startMarketDataFeed (apiKey: string) (pairs: string list) =
    async {
        let! wsClientResult = connectToPolygon apiKey
        match wsClientResult with
        | Ok wsClient ->
            do! subscribeToPairs wsClient pairs
            do! receiveData wsClient
        | Error errMsg -> printfn "WebSocket connection error: %s" errMsg
    }

// Entry point to start the real-time market data feed
[<EntryPoint>]
let main _ =
    let apiKey = "OZpD8OUeBy5zWFQ5v3Hd_BEopvquAvSt"
    let pairs = ["XQ.BTC-USD"]  
    
    // Start the market data feed asynchronously
    startMarketDataFeed apiKey pairs |> Async.RunSynchronously
    0 
