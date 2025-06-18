# BTCPay Server POS Performance Tester

A minimal tool to test the performance of BTCPay Server Point of Sale checkout flows using Playwright automation and the BTCPay Greenfield API.

## Features

- Automated checkout flow testing using Playwright
- Lightning invoice extraction and payment via BTCPay Greenfield API
- Detailed performance timing for each operation
- Configurable via command line, environment variables, or JSON config
- Console logging with timestamps

## Setup

1. Install Playwright browsers:
   ```bash
   dotnet run -- install
   ```

2. Configure your test parameters (see Configuration section below)

3. Run the test:
   ```bash
   dotnet run
   ```

## Configuration

You can configure the tester in three ways:

### 1. Command Line Arguments
```bash
dotnet run -- --CheckoutUrl https://btcprague.btcpay.tech/apps/3enfCFaCvczTzkcdsBjsYpVkWS8V/pos \
              --BTCPayServerUrl https://btcprague.btcpay.tech \
              --ApiKey your_api_key \
              --StoreId your_store_id \
              --Amount 0.10
```

### 2. Environment Variables
```bash
export POSTESTER_CheckoutUrl="https://btcprague.btcpay.tech/apps/3enfCFaCvczTzkcdsBjsYpVkWS8V/pos"
export POSTESTER_BTCPayServerUrl="https://btcprague.btcpay.tech"
export POSTESTER_ApiKey="your_api_key"
export POSTESTER_StoreId="your_store_id"
export POSTESTER_Amount="0.10"
```

### 3. appsettings.json
```json
{
  "CheckoutUrl": "https://btcprague.btcpay.tech/apps/3enfCFaCvczTzkcdsBjsYpVkWS8V/pos",
  "BTCPayServerUrl": "https://btcprague.btcpay.tech",
  "ApiKey": "your_api_key_here",
  "StoreId": "your_store_id_here",
  "Amount": 0.10,
  "TimeoutSeconds": 60,
  "Headless": true,
  "SlowMo": 100
}
```

## Required Configuration

- **CheckoutUrl**: The POS URL to test (e.g., `https://example.btcpay.tech/apps/xyz/pos`)
- **BTCPayServerUrl**: Base URL of your BTCPay Server instance
- **ApiKey**: BTCPay Server API key with lightning permissions
- **StoreId**: Store ID for API calls
- **Amount**: Amount to charge (in the store's default currency)

## Optional Configuration

- **TimeoutSeconds**: Maximum time to wait for payment confirmation (default: 60)
- **Headless**: Run browser in headless mode (default: true)
- **SlowMo**: Milliseconds to slow down Playwright operations (default: 100)

## Getting API Keys

1. Log into your BTCPay Server
2. Go to Account → Manage Account → API Keys
3. Create a new API key with these permissions:
   - `btcpay.store.canviewstoresettings`
   - `btcpay.store.canmodifyinvoices`
   - `btcpay.store.lightning.canuseinternallightningnode`

## Test Flow

1. **Browser Initialization**: Launch Chromium browser
2. **Page Load**: Navigate to the POS URL
3. **Amount Entry & Charge**: Enter amount and click charge button
4. **Invoice Extraction**: Extract lightning invoice from checkout page
5. **API Payment**: Pay invoice using BTCPay Greenfield API
6. **Payment Confirmation**: Wait for payment to be confirmed

## Output

The tool provides detailed console logging with timestamps and performance metrics:

```
[10:26:32.123] Starting POS checkout test
[10:26:32.124] Target URL: https://btcprague.btcpay.tech/apps/xyz/pos
[10:26:32.125] Starting: Browser Initialization
[10:26:33.456] Completed: Browser Initialization in 1331.23ms
...
=== TIMING SUMMARY ===
Browser Initialization: 1331.23ms
Page Load: 2156.78ms
Amount Entry & Charge: 1023.45ms
Invoice Extraction: 234.56ms
API Payment: 1567.89ms
Payment Confirmation: 3456.78ms
Total Time: 9770.69ms
Status: SUCCESS
```

## Troubleshooting

- Ensure your API key has the correct permissions
- Check that the POS URL is accessible and properly configured
- Verify your store has lightning enabled
- For debugging, set `"Headless": false` in appsettings.json to see the browser

## Requirements

- .NET 8.0
- Internet connection
- Valid BTCPay Server instance with lightning support
- API key with appropriate permissions
