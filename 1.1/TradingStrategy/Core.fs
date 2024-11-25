module TradingStrategy.Core

type TradingStrategy =
    { NumberOfCryptos: int
      MinPriceSpread: float
      MinTransactionProfit: float
      MaxTransactionValue: float
      MaxTradingValue: float
      InitialInvestment: float }

type MaxTradingValue = float


type Email = string

type Success = | Data

type Error = { Message: string; Code: int }

type Result<'Success, 'Error> =
    | Success of 'Success
    | Error of 'Error
