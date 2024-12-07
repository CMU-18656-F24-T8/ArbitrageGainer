module Controller.AnnualizedReturnCalculate

open System
open Suave
open Suave.Filters
open Suave.Operators
open Suave.Successful
open Suave.RequestErrors
open Newtonsoft.Json

// TYPE DEFINITIONS

type AnnualizedReturnInput =
    { InitialInvestment: float
      CumulativePnL: float
      DurationInMonths: float }

type AnnualizedReturnResult = { AnnualizedReturn: float }

type Error = { Message: string; Code: int }

type Result<'T> =
    | Success of 'T
    | Error of Error


let calculateAnnualizedReturn (input: AnnualizedReturnInput) =
    try
        match input with
        | _ when input.InitialInvestment <= 0.0 ->
            Error
                { Message = "Initial investment must be greater than zero"
                  Code = 400 }
        | _ when input.DurationInMonths <= 0.0 ->
            Error
                { Message = "Duration must be greater than zero months"
                  Code = 400 }
        | _ ->
            let durationInYears = input.DurationInMonths / 12.0

            let annualizedReturn =
                ((input.CumulativePnL / input.InitialInvestment) ** (1.0 / durationInYears))
                - 1.0

            Success { AnnualizedReturn = annualizedReturn }
    with :? System.Exception as ex ->
        Error
            { Message = sprintf "Internal server error: %s" ex.Message
              Code = 500 }


let calculateAnnualizedReturnHandler =
    request (fun ctx ->
        let input =
            ctx.rawForm
            |> System.Text.Encoding.UTF8.GetString
            |> JsonConvert.DeserializeObject<AnnualizedReturnInput>

        match calculateAnnualizedReturn input with
        | Success result -> OK(JsonConvert.SerializeObject(result))
        | Error error ->
            match error.Code with
            | 400 -> BAD_REQUEST error.Message
            | _ -> ServerErrors.INTERNAL_ERROR error.Message)
