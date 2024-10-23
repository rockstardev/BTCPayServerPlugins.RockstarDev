# Admin Pass Reset, Plugin for BTCPay Server

This plugin is designed to address the issue of forgotten passwords on older BTCPay Server instances. If you follow the [FAQ documentation on resetting a forgotten server admin password](https://docs.btcpayserver.org/FAQ/ServerSettings/#forgot-btcpay-admin-password), you’ll end up with a new server admin account but without access to old  stores.

For those who don’t have SMTP server credentials handy, I created this plugin as a solution. Additionally, this plugin serves as a quick demo and a showcase of the plugin system - illustrating how the BTCPay team can't stop users from making risky decisions with their server. Now that this plugin exists, anyone added as a server admin to your BTCPay instance can easily reset your password.

**With great power comes great responsibility.**
- Uncle Rockstar (probably) 

## Usage

1. Install the plugin by navigating to BTCPay Server > Settings > Plugin > Available Plugins, and restart your server.

![Admin Pass Reset plugin](https://github.com/user-attachments/assets/2df211cb-04eb-4dac-97cf-5e77d3f97286)

2. Once installed, you'll see the Admin Pass Reset plugin listed in the left sidebar of BTCPay Server.
3. Click the link, and you'll be presented with a form where you can reset the password of any user by entering their email address and clicking the "Reset Password" button.
4. A link will be displayed, which you can click to reset the user's password.

## Contributing to plugin development
You shouldn't. Like, seriously. The BTCPay team has improved the onboarding process in version 2.0, and you can already do this without the plugin. This is just a temporary solution for those who haven’t upgraded to 2.0 yet.

## License
https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/LICENSE
