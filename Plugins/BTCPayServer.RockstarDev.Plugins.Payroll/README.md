# Payroll Plugin for BTCPay Server

The goal of this plugin is to enable easy upload of invoices and streamlined flow for paying them.
Main flow right now is:
- Create Payroll User for each person that needs access to the system
- Generate strong passwords and share link to login page, along with credentials
- Once user logs in, they should be encouraged to change the password to one only they know
- They upload the invoice while populating Destination (address of payout) and amount with currency
- Admin receives and inspects the invoices in the main list
- Admin can select invoices and click on Pay Invoices option
- This takes them to Bitcoin wallet with prepopulated Send dialog (amount of Bitcoin is calulated automatically, based on current conversion rates)
- Admin can then sign the generated transaction and broadcast it
- Once transaction is confirmed on the blockchain, payroll invoice state will be updated to Completed

## Contributing to plugin development
This documentation is work in progress. You can start by improving it.
Also, list of open issues is maintained on: https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/issues?q=is%3Aissue+is%3Aopen+label%3Apayroll
If issue is not assigned to anyone, feel free to pick it up and open PR

## License
https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/LICENSE