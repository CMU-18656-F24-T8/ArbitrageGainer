# ArbitrageGainer
ArbitrageGainer is a functional programming-based cryptocurrency arbitrage trading application that identifies and acts on arbitrage opportunities across multiple cryptocurrency exchanges

Team Miro Board: https://miro.com/app/board/uXjVLYapgIE=/

Team Google Doc: https://docs.google.com/document/d/1ZwnIvwP54H6qXC3ViZc8g__lr0P-gK3Px6oR7eYKITs/edit?usp=sharing

PLEASE REFER TO 1.1 as the main code base. 

## List of Services
### 1.1 Trading Strategy Management Service
A configuration management service that handles trading parameters and notifications. Several endpoints could be used to conveniently access and modify trading strategies.
#### Endpoints:
- `POST /trading_strategy` - Set trading strategy parameters
- `GET /trading_strategy` - Retrieve current trading strategy
- `PATCH /trading_strategy` - Update max trading value
- `POST /email` - Set notification email
- `GET /email` - Retrieve configured email
- `POST /realtime` - Start/stop real-time trading
  - Message "start" starts real-time trading
  - Message "stop" stops real-time trading
### 1.2 Retrieval of Cross-traded Currency Pairs
Fetches and processes trading pairs from multiple exchanges:  
- Bitfinex: `https://api-pub.bitfinex.com/v2/conf/pub:list:pair:exchange`
- Bitstamp: `https://www.bitstamp.net/api/v2/ticker/`
- Kraken: `https://api.kraken.com/0/public/AssetPairs`
  ### Endpoints:
 - "GET /crosstraded" -> return a list of cross traded string

Identifies and stores cross-traded pairs in format: `currency1Symbol-currency2symbol`.  
### 1.3 Real-time Market Data Management
Implements WebSocket connection to Polygon.io API for real-time crypto data:  
- Maintains price cache for different exchanges
- Processes real-time trade events
- Evaluates trading opportunities based on configured parameters  

### 1.4 Annualized Return Metric Calculation
the average yearly return of a trading strategy, providing a more extended view of performance of the system.  
REST API endpoint:
- `POST /annualized_return` - Calculate annualized return

### 2.1 Real-time Trading Service

Source code: `RealtimeTrading/RtTradingService.fs`

### 2.2 Orders Management Service

Source code: `RealtimeTrading/orderPlacementHandler.fs`

### 2.3 P&L calculation

Source code: `Controller/PLCalculation.fs`

### 2.4 Annualized return metric calculation refactoring

Source code: `Controller/annualizedReturnCalculate.fs`

# Technical Debt

There are some technical debt still remaining: 
- Naming issue due to lack of communication. This means that a lot of the bridges need to be adapted. 
- Namespace inconsistency
- The performance of a lot of function could be improved. 