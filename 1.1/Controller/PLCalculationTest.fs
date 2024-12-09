module Tests.PLCalculation

open Xunit
open Controller.PLCalculation
open System

// Helper functions for creating test data
let createTrade orderId userId orderType amount price matchedAmount matchedPrice status =
    {
        OrderId = OrderId orderId
        UserId = UserId userId
        Type = orderType
        Amount = Amount amount
        Price = Price price
        MatchedAmount = Amount matchedAmount
        MatchedPrice = Price matchedPrice
        Status = status
        Timestamp = DateTime.UtcNow
    }

// Unit Tests
[<Fact>]
let ``Calculate PL for buy trade should work correctly`` () =
    let trade = createTrade 1L 1L Buy 100M 10M 100M 15M FullyMatched
    
    match PLCalculation.calculateTradePL trade with
    | Ok pl -> Assert.Equal(500M, pl)  // (15-10) * 100 = 500
    | Error e -> Assert.True(false, sprintf "Expected success but got error: %A" e)

[<Fact>]
let ``Calculate PL for sell trade should work correctly`` () =
    let trade = createTrade 1L 1L Sell 100M 15M 100M 10M FullyMatched
    
    match PLCalculation.calculateTradePL trade with
    | Ok pl -> Assert.Equal(500M, pl)  // (15-10) * 100 = 500
    | Error e -> Assert.True(false, sprintf "Expected success but got error: %A" e)

[<Fact>]
let ``Calculate PL for unmatched trade should return error`` () =
    let trade = createTrade 1L 1L Buy 100M 10M 100M 15M Unmatched
    
    match PLCalculation.calculateTradePL trade with
    | Ok _ -> Assert.True(false, "Expected error but got success")
    | Error e -> Assert.Equal(InvalidOrderState, e)

// Integration Tests using PLAgent
[<Fact>]
let ``PLAgent should track trades correctly`` () =
    async {
        // Create agent
        let agent = createPLAgent()
        let userId = UserId 1L
        
        // Start tracking
        agent.Post(StartTracking userId)
        
        // Add some trades
        let trade1 = createTrade 1L 1L Buy 100M 10M 100M 15M FullyMatched
        let trade2 = createTrade 2L 1L Sell 50M 20M 50M 15M FullyMatched
        
        agent.Post(UpdateTrade trade1)
        agent.Post(UpdateTrade trade2)
        
        // Get current P&L
        let! result = agent.PostAndAsyncReply(fun reply -> GetCurrentPL(userId, reply))
        
        match result with
        | Ok pl -> Assert.Equal(750M, pl)  // 500 from trade1 + 250 from trade2
        | Error e -> Assert.True(false, sprintf "Expected success but got error: %A" e)
    }

[<Fact>]
let ``PLAgent should handle threshold updates`` () =
    async {
        let agent = createPLAgent()
        let userId = UserId 1L
        
        // Set threshold
        let! result = agent.PostAndAsyncReply(fun reply -> 
            UpdateThreshold(userId, Some 1000M, reply))
            
        match result with
        | Ok _ -> Assert.True(true)
        | Error e -> Assert.True(false, sprintf "Expected success but got error: %A" e)
    }