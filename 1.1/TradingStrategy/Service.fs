module TradingStrategy.Service


open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json
open Suave
open Suave.Filters
open Suave.Operators
open TradingStrategy.Infrastructure
open TradingStrategy.Core

let setTradingStrategyHandler (agent: MailboxProcessor<Msg>) =
    request (fun ctx ->
        let strategy =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<TradingStrategy>

        insertEntity agent strategy
        OK "Trading strategy set")

let setMaxTradingValueHandler (agent: MailboxProcessor<Msg>) =
    request (fun ctx ->
        let maxTradingValue =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<MaxTradingValue>

        updateMaxTradingValue agent maxTradingValue
        OK "Max trading value set")
let getTradingStrategyHandler agent =
    request (fun ctx ->
        let strategy = getTradingStrategy agent

        OK(JsonConvert.SerializeObject(strategy)))

let setEmailHandler (agent: MailboxProcessor<Msg>) =
    request (fun ctx ->
        let email =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<Email>

        setEmail agent email
        OK "Email set")

let getEmailHandler (agent: MailboxProcessor<Msg>) =
    request (fun ctx ->
        let email = getEmail agent

        OK(JsonConvert.SerializeObject(email)))