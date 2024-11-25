module TradingStrategy.Infrastructure

open TradingStrategy.Core

type Msg =
    | UpdateEmail of Email
    | GetEmail of AsyncReplyChannel<Email>
    | UpdateTradingStrategy of TradingStrategy
    | GetTradingStrategy of AsyncReplyChannel<TradingStrategy>
    | UpdateMaxTradingValue of MaxTradingValue
   
type State = {
    Email: Email option
    TradingStrategy: TradingStrategy option
}
    
let strategyAgent = MailboxProcessor.Start(fun inbox ->
   let rec loop state = async {
       let! msg = inbox.Receive()
       match msg with
       | UpdateEmail email -> 
           return! loop {state with Email = Some email}
       | GetEmail channel ->
           channel.Reply(state.Email.Value)
           return! loop state
       | UpdateTradingStrategy strategy ->
           return! loop {state with TradingStrategy = Some strategy}
       | GetTradingStrategy channel ->
           channel.Reply(state.TradingStrategy.Value)
           return! loop state
       | UpdateMaxTradingValue maxTradingValue ->
           let tradingStrategy = state.TradingStrategy.Value
           let updatedTradingStrategy = {tradingStrategy with MaxTradingValue = maxTradingValue}
           return! loop {state with TradingStrategy = Some updatedTradingStrategy}
   } 
   let initialState = { Email = None; TradingStrategy = None; }
   loop initialState
)


let insertEntity (agent: MailboxProcessor<Msg>) (strategy: TradingStrategy) =
    agent.Post(UpdateTradingStrategy strategy)
    

let updateMaxTradingValue (agent: MailboxProcessor<Msg>)  (maxTradingValue: MaxTradingValue) =
    agent.Post(UpdateMaxTradingValue maxTradingValue)

let setEmail (agent: MailboxProcessor<Msg>) (email: Email) =
    agent.Post(UpdateEmail email)

let getTradingStrategy (agent: MailboxProcessor<Msg>) =
    agent.PostAndReply(GetTradingStrategy)
    
let getEmail (agent: MailboxProcessor<Msg>) =
    agent.PostAndReply(GetEmail)