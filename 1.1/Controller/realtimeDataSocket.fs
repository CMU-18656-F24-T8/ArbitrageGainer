module Controller.RealtimeDataSocket

open System
open System.Net.WebSockets
open System.Text
open System.Text.Json
open System.Threading

// Domain types
type Exchange = Exchange of int
type CurrencyPair = CurrencyPair of string

type Quote = {
    AskPrice: decimal
    BidPrice: decimal
    Timestamp: int64
}

type CryptoQuote = {
    Pair: CurrencyPair
    Exchange: Exchange
    Quote: Quote
}

// Discriminated union for WebSocket messages
type WebSocketMessage =
    | Text of string
    | Close
    | Other

// Types for Polygon messages
type PolygonMessage = {
    ev: string
    pair: string
    ap: decimal
    bp: decimal
    x: int
    t: int64
}

// Immutable price cache
type PriceCache = {
    Quotes: Map<CurrencyPair * Exchange, Quote>
}

// Result type for operations
type WebSocketResult<'T> =
    | Success of 'T
    | Error of string

// Module for cache operations
module QuoteCache =
    let empty = { Quotes = Map.empty }
    
    let updateQuote (cache: PriceCache) (quote: CryptoQuote) =
        let key = (quote.Pair, quote.Exchange)
        { cache with Quotes = cache.Quotes.Add(key, quote.Quote) }
    
    let tryGetQuote (cache: PriceCache) (pair: CurrencyPair) (exchange: Exchange) =
        cache.Quotes.TryFind((pair, exchange))

// WebSocket connection module
module WebSocket =
    let connect (apiKey: string) : Async<WebSocketResult<ClientWebSocket>> =
        async {
            let uri = Uri("wss://socket.polygon.io/crypto")
            let wsClient = new ClientWebSocket()
            
            try
                do! wsClient.ConnectAsync(uri, CancellationToken.None) |> Async.AwaitTask
                let authMessage = sprintf """{"action":"auth","params":"%s"}""" apiKey
                let authMessageBytes = Encoding.UTF8.GetBytes(authMessage)
                do! wsClient.SendAsync(
                        new ArraySegment<byte>(authMessageBytes), 
                        WebSocketMessageType.Text, 
                        true, 
                        CancellationToken.None) |> Async.AwaitTask
                return Success wsClient
            with
            | ex -> return Error (sprintf "Connection failed: %s" ex.Message)
        }
    
    let subscribe (wsClient: ClientWebSocket) (pairs: CurrencyPair list) : Async<WebSocketResult<unit>> =
        async {
            try
                let pairsStr = 
                    pairs 
                    |> List.map (fun (CurrencyPair p) -> sprintf "XQ.%s" p) 
                    |> String.concat ","
                let subMessage = sprintf """{"action":"subscribe","params":"%s"}""" pairsStr
                let subMessageBytes = Encoding.UTF8.GetBytes(subMessage)
                do! wsClient.SendAsync(
                        new ArraySegment<byte>(subMessageBytes), 
                        WebSocketMessageType.Text, 
                        true, 
                        CancellationToken.None) |> Async.AwaitTask
                return Success ()
            with
            | ex -> return Error (sprintf "Subscription failed: %s" ex.Message)
        }
    
    let receiveMessage (wsClient: ClientWebSocket) : Async<WebSocketMessage> =
        async {
            let buffer = Array.zeroCreate 8192
            let segment = ArraySegment(buffer)
            let! result = wsClient.ReceiveAsync(segment, CancellationToken.None) |> Async.AwaitTask
            
            match result.MessageType with
            | WebSocketMessageType.Text ->
                return Text(Encoding.UTF8.GetString(buffer, 0, result.Count))
            | WebSocketMessageType.Close ->
                return Close
            | _ ->
                return Other
        }

// Market data processing module
module MarketData =
    let processMessage (message: string) : CryptoQuote list =
        try
            let quotes = JsonSerializer.Deserialize<PolygonMessage[]>(message)
            quotes
            |> Array.filter (fun q -> q.ev = "XQ")
            |> Array.map (fun q -> 
                {
                    Pair = CurrencyPair q.pair
                    Exchange = Exchange q.x
                    Quote = {
                        AskPrice = q.ap
                        BidPrice = q.bp
                        Timestamp = q.t
                    }
                })
            |> Array.toList
        with
        | ex -> 
            printfn "Error processing message: %s" ex.Message
            []

// Market data service module
module MarketDataService =
    type DataFeedState = {
        Cache: PriceCache
        WebSocket: ClientWebSocket option
        SubscribedPairs: CurrencyPair list
    }
    
    let initState = {
        Cache = QuoteCache.empty
        WebSocket = None
        SubscribedPairs = []
    }
    
    let subscribeToMarketData 
        (apiKey: string) 
        (pairs: CurrencyPair list) 
        (onQuoteReceived: CryptoQuote -> unit) : Async<unit> = // Changed return type to Async<unit>
        
        let rec processMarketData (wsClient: ClientWebSocket) (state: DataFeedState) =
            async {
                match! WebSocket.receiveMessage wsClient with
                | Text message ->
                    let quotes = MarketData.processMessage message
                    let newCache = 
                        quotes 
                        |> List.fold QuoteCache.updateQuote state.Cache
                    
                    // Notify about updates
                    quotes |> List.iter onQuoteReceived
                    
                    return! processMarketData wsClient { state with Cache = newCache }
                | Close ->
                    printfn "Market data feed connection closed."
                    ()  // Return unit instead of state
                | Other ->
                    return! processMarketData wsClient state
            }
        
        async {
            match! WebSocket.connect apiKey with
            | Success wsClient ->
                match! WebSocket.subscribe wsClient pairs with
                | Success _ ->
                    let initialState = { 
                        initState with 
                            WebSocket = Some wsClient
                            SubscribedPairs = pairs 
                    }
                    do! processMarketData wsClient initialState  // Changed to do!
                | Error err ->
                    printfn "Failed to subscribe to market data: %s" err
            | Error err ->
                printfn "Failed to connect to market data feed: %s" err
        }

let pairs = [CurrencyPair "BTC-USD"; CurrencyPair "ETH-USD"]
let apiKey = "OZpD8OUeBy5zWFQ5v3Hd_BEopvquAvSt"
let handleQuoteUpdate (quote: CryptoQuote) =
    // Handle incoming market data quotes
    printfn "Received quote for %A: Ask=%M Bid=%M" 
        quote.Pair quote.Quote.AskPrice quote.Quote.BidPrice


