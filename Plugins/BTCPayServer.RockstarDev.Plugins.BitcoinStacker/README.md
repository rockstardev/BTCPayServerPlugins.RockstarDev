# Bitcoin Stacker, Plugin for BTCPay Server

This plugin is designed to automatically place a Bitcoin orders on supported exchanges (Strike for start),
based on the data on fiat income on supported platforms (Stripe for start).

## Strike API key permissions

Create the Strike API key with these scopes:

| Scope | Endpoint | Used for |
| --- | --- | --- |
| `partner.payment-method.bank.read` | `GET /v1/payment-methods/bank` | Validating the key and finding the linked ACH bank account when settings are saved |
| `partner.deposit.manage` | `POST /v1/deposits`, `GET /v1/deposits/{id}` | Starting the ACH deposit for an order and checking its state |
| `partner.currency-exchange-quote.create` | `POST /v1/currency-exchange-quotes` | Quoting the USD to BTC exchange |
| `partner.currency-exchange-quote.execute` | `PATCH /v1/currency-exchange-quotes/{id}/execute` | Executing the purchase |
| `partner.balances.read` | `GET /v1/balances` | Buying while a deposit is still pending if the USD balance covers it; balances on the page |
| `partner.rates.ticker` | `GET /v1/rates/ticker` | BTC price for the profit summary on the page |

The first four are required. Without `partner.balances.read`, an order with a pending deposit waits for
the deposit to complete; without `partner.rates.ticker`, the page shows no profit summary. No send,
payout or withdrawal scope is needed: the plugin never moves funds out of Strike.

The Strike account must already have an ACH bank account linked; when settings are saved without a
payment method, the plugin picks the first ACH account.

## License

https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/LICENSE
