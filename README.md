# ArbitrageGainer
ArbitrageGainer is a functional programming-based cryptocurrency arbitrage trading application that identifies and acts on arbitrage opportunities across multiple cryptocurrency exchanges

Team Miro Board: https://miro.com/app/board/uXjVLYapgIE=/

Team Google Doc: https://docs.google.com/document/d/1ZwnIvwP54H6qXC3ViZc8g__lr0P-gK3Px6oR7eYKITs/edit?usp=sharing

## List of Services
### 1.1 Trading Strategy Management Service
**Location**: `./1.1/Program.fs`  
A configuration management service that handles trading parameters and notifications. Several endpoints could be used to conveniently access and modify trading strategies.
#### Endpoints:
- `POST /trading_strategy` - Set trading strategy parameters
- `GET /trading_strategy` - Retrieve current trading strategy
- `PATCH /trading_strategy` - Update max trading value
- `POST /email` - Set notification email
- `GET /email` - Retrieve configured email
### 1.2 Retrieval of Cross-traded Currency Pairs
**Location**: `./1.2/main.fs`  
Fetches and processes trading pairs from multiple exchanges:  
- Bitfinex: `https://api-pub.bitfinex.com/v2/conf/pub:list:pair:exchange`
- Bitstamp: `https://www.bitstamp.net/api/v2/ticker/`
- Kraken: `https://api.kraken.com/0/public/AssetPairs`
Identifies and stores cross-traded pairs in format: `currency1Symbol-currency2symbol`.  
### 1.3 Real-time Market Data Management
**Location**: `./1.3/RealTimeDataSocket.fs`  

Implements WebSocket connection to Polygon.io API for real-time crypto data:  
- Maintains price cache for different exchanges
- Processes real-time trade events
- Evaluates trading opportunities based on configured parameters  
### Endpoints:
 - "GET /crosstraded" -> return a list of cross traded string

### 1.4 Annualized Return Metric Calculation
**Location**: `./1.1/annualizedReturnCalculate.fs`  
the average yearly return of a trading strategy, providing a more extended view of performance of the system.  
REST API endpoint:
- `POST /annualized_return` - Calculate annualized return
