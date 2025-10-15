# MarkPaid Checkout Plugin

Accept off-chain payments like cash, credit cards, or bank transfers. Mark invoices as paid directly from the checkout page.

By default, the plugin provides a **CASH** payment method. No additional configuration is needed to start using it - after you install plugin, just go to the store you want to enable it for.

If you want to add more payment methods, you can do so by going to the MarkPaid plugin Server settings, and update the textbox to say "CASH,CREDIT". Please be aware you'll need to restart the server for settings to take effect.

## Features

- **Custom Payment Methods**: Add payment methods like CASH, CREDIT, BANK_TRANSFER, or any custom identifier
- **Instant Settlement**: Mark invoices as settled directly from the checkout page with a single click
- **Proper State Machine**: Follows BTCPay's invoice state machine (New → Processing → Settled)
- **Payment Recording**: All payments are properly recorded in the invoice history
- **Flexible Configuration**: Enable/disable payment methods per store
- **Environment Variable Support**: Configure available payment methods via `MARKPAID_METHODS` environment variable

## Use Cases

- **Point of Sale**: Accept cash payments at physical locations
- **Credit Card Processing**: Record payments processed through external payment processors
- **Custom Payment Methods**: Support any other off-chain payment method needed 

## License

MIT License - see LICENSE file for details

## Support

- **Issues**: [GitHub Issues](https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/issues)
- **Documentation**: [BTCPay Server Docs](https://docs.btcpayserver.org)
- **Community**: [BTCPay Server Mattermost](https://chat.btcpayserver.org)

