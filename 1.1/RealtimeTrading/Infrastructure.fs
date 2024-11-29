module RealtimeTrading.Infrastructure

type StatusMsg =
    | Start
    | Stop
    | GetStatus of AsyncReplyChannel<string>

let realTimeTradingStatusAgent = MailboxProcessor.Start(fun inbox ->
    let rec loop status =
        async {
            let! msg = inbox.Receive()
            match msg with
            | Start -> 
                printfn "Starting real-time trading"
                return! loop "started"
            | Stop -> 
                printfn "Stopping real-time trading"
                return! loop "stopped"
            | GetStatus channel -> 
                channel.Reply(status)
                return! loop status
        }
    loop "stopped"
)