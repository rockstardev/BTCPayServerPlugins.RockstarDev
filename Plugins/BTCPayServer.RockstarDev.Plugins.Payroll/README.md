# Payroll Plugin for BTCPay Server

The Payroll Plugin for BTCPay Server aims to streamline the payroll process by providing an easy-to-use interface for uploading invoices and facilitating payments. 
This plugin enables administrators to manage payroll users, inspect invoices, and initiate payments seamlessly within the BTCPay Server.

## Usage

- Install the plugin from the BTCPay Server > Settings > Plugin > Available Plugins, and restart

![Payroll Plugin](https://github.com/btcpayserver/btcpayserver/assets/47084273/a918ff08-7444-4b69-a2ca-b75e38f19bcc)

- Once done, you'd see the Payroll plugin listed under plugins in the left side bar of Btcpay Server
- You can create payroll user for each person that needs access to the system. It is recommended that you generate strong passwords and share the login link to the respective users

- To do that click on the manage users button on top right, and then click on the create user button.

![Manage User](https://github.com/btcpayserver/btcpayserver/assets/47084273/629e0d3d-db67-489a-baa1-c7b2eb11932a)

![Create User](https://github.com/btcpayserver/btcpayserver/assets/47084273/9d27aa5e-f187-4b58-b758-320125be277f)

- Do well to fill in the form with the appropriate information, and also with a strong password.
- As an admin, you can go ahead and upload invoices manually for users by going to the Payroll Invoice section and clicking on admin upload invoice.
- The admin can share link to the invoice page to users, allowing users to login and manually upload their invoice.

![Share Invoice upload link](https://github.com/btcpayserver/btcpayserver/assets/47084273/f654d1f7-4114-4b46-8f3e-b9410cec95ed)

- Once invoices are uploaded, the admin has the ability to pay invoice(s), download invoice(s), and also mark an invoice as paid, if it has been initially signed off my the admin.
- When an admin clicks on pay invoice, it takes them to Bitcoin wallet with prepopulated Send dialog (amount of Bitcoin is calulated automatically, based on current conversion rates)
- The admin can then sign the generated transaction and broadcast it.
- Once transaction is confirmed on the blockchain, payroll invoice state will be updated to Completed


- The admin can also manage payroll users. The admin is able to do all of the following
- An admin is able to reset password for users
- An admin can disable/activate payroll users. It would be
- An admin can edit payroll users
- An admin can also download invoices belonging to a particular user


## Contributing to plugin development
This documentation is work in progress. You can start by improving it.
Also, list of open issues is maintained on: https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/issues?q=is%3Aissue+is%3Aopen+label%3Apayroll
If issue is not assigned to anyone, feel free to pick it up and open PR

## License
https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/LICENSE