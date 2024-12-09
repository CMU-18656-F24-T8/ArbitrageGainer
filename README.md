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
1. `POST /api/pl/calculate`
2. `POST /api/pl/threshold`
3. `GET /api/pl/current`

### Database Access

Our database utilizes Azure Cosmos DB Table cloud service.
The following specifies the access to database:
* Account Name: arbitrage-db
* Endpoint: https://arbitrage-db.table.cosmos.azure.com:443
* Key: \*\*\*\*\*\*\*\* (see key in the code)

### Run the Application with Docker

1. Pull the docker image and run the container

    ```bash
    docker pull ziyuew2/18656_8
    docker run -p 8080:8080 arbitragegainer/arbitragegainer
    ```

1. Set up the trading strategy

    ```bash
    curl --location 'http://127.0.0.1:8080/trading_strategy' \
    --data '{
        "NumberOfCryptos": 5,
        "MinPriceSpread": 0.05,
        "MinTransactionProfit": 5,
        "MaxTransactionValue": 2000,
        "MaxTradingValue": 5000,
        "InitialInvestment": 2000.0
      }'
    ```

1. Set email for notification

    ```bash
    curl --location 'http://127.0.0.1:8080/email' \
    --data 'pkotchav@andrew.cmu.edu'
    ```

1. Get historical arbitrage opportunities

    ```bash
    curl --location --request GET 'http://127.0.0.1:8080/historical'
    ```

1. Get cross-traded currency pairs

    ```bash
    curl --location 'http://127.0.0.1:8080/crosstrade'
    ```

1. Start realtime trading

    ```bash
    curl --location 'http://127.0.0.1:8080/realtime' \
    --data 'start'
    ```

1. Stop realtime trading

    ```bash
    curl --location 'http://127.0.0.1:8080/realtime' \
    --data 'stop'
    ```
1. Calculate P&L for a user
   
    ```bash
    curl --location 'http://127.0.0.1:8080/api/pl/calculate' \
    --header 'Content-Type: application/json' \
    --data '{
    "userId": 1
    }'
    ```
    
1. Set P&L threshold  

   ```bash
    curl --location 'http://127.0.0.1:8080/api/pl/threshold' \
    --header 'Content-Type: application/json' \
    --data '{
        "userId": 1,
        "target": 1000.00
    }'
    ```

1. Get current P&L

   ```bash
    curl --location 'http://127.0.0.1:8080/api/pl/current?userId=1'
    ```   

### Performance Mesurements

*	Historical Arbitrage Analysis Time: 168 ms
*	Cross-Traded Currencies Identification Time: 417 ms
*	Time to First Order: 6.323 seconds


## External Links

DDD Miro Board: https://miro.com/app/board/uXjVLYapgIE=/

System Design Doc: https://docs.google.com/document/d/1ZwnIvwP54H6qXC3ViZc8g__lr0P-gK3Px6oR7eYKITs/edit?usp=sharing
