# Wallet History Reload Plugin

Backfills missing transaction data (fees, fee rates, historical USD prices) for wallet transactions.

## Features

- Fetches transaction fees and fee rates from Mempool.space API
- Retrieves historical BTC/USD prices from CoinGecko API
- Respects API rate limits (500ms delay between requests)
- Provides UI to trigger backfill for specific wallets

## Usage

1. Navigate to Store → Wallet → Bitcoin
2. Click "Backfill data" in the navigation
3. Select which data to backfill (fees, historical prices)
4. Click "Start Backfill"
