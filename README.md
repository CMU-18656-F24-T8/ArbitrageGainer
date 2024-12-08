# ArbitrageGainer
ArbitrageGainer is a functional programming-based cryptocurrency arbitrage trading application that identifies and acts on arbitrage opportunities across multiple cryptocurrency exchanges


PLEASE REFER TO 1.1 as the code base. 

## Course Project Documentation

### REST Api Endpoints

#### Trading strategy

1. `POST /trading-strategy`: initialize a strategy

1. `GET /trading-strategy`: get a strategy

1. `PATCH /trading_strategy`: update `MaxTradingValue` of a strategy

1. `POST /email`: send an email

1. `GET /email`: get the email


#### Trading

1. `POST /realtime`: initiate and start realtime trading (body: `start`)

1. `POST /realtime`: stop realtime trading (body: `stop`)


#### Retrival of Cross Traded Currency Pairs
1. `GET /crosstrade`

#### Identify Historical Arbitrage Opportunities
1. `GET /historical`

#### P&L Management


### Database Access

### Run the Application with Docker

### Performance Mesurements




## External Links

DDD Miro Board: https://miro.com/app/board/uXjVLYapgIE=/

System Design Doc: https://docs.google.com/document/d/1ZwnIvwP54H6qXC3ViZc8g__lr0P-gK3Px6oR7eYKITs/edit?usp=sharing
