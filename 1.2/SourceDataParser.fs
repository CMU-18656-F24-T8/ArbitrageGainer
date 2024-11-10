module SourceDataParser

open FSharp.Data

let KrakenDataParser (datas:JsonValue) =    datas.["result"].Properties()
                                            |> Array.map (fun (_, v) ->
                                                         (v.["base"].AsString(), v.["quote"].AsString()))
let BitstampDataParser (datas:JsonValue) =  datas.AsArray()
                                            |> Array.map (fun x -> x.["pair"].AsString().Replace("/", ":").Split(':'))
                                            |> Array.choose (fun arr ->  match arr.Length = 2 with 
                                                                            |  true -> Some (arr.[0], arr.[1])
                                                                            | false -> None )

let BitfinexDataParser (datas :JsonValue) =
            datas.[0].AsArray() |> Array.map (fun x -> 
            let pair = x.AsString()
            match pair.Contains(":") with
            | true -> pair.Split(':')
            | false ->
                        let middleIndex = pair.Length / 2
                        [| pair.[0..middleIndex-1]; pair[middleIndex..] |]
            ) |> Array.choose (fun arr -> 
                                        match arr.Length = 2 with 
                                            | true -> Some (arr.[0], arr.[1])
                                            | false -> None)