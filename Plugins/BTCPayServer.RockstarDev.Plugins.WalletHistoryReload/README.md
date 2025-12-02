# Wallet History Reload Plugin

Backfills missing transaction data (fees, fee rates, historical USD prices) for wallet transactions.

## Features

- Fetches transaction fees and fee rates from Mempool.space API
- Retrieves historical BTC/USD prices from CoinGecko API
- Respects API rate limits (500ms delay between requests)
- Provides UI to trigger backfill for specific wallets

## Usage

1. Navigate to Store → Wallets → [Your Wallet]
2. Click "Wallet History Reload" in the navigation
3. Select which data to backfill (fees, historical prices)
4. Click "Start Backfill"

## TODO

- Update NBXplorer database with fetched fee data
- Store historical prices in BTCPayServer database
- Add progress indicator for long-running backfills
- Add ability to backfill specific date ranges
