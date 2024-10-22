# Xpub Extractor

This plugin is designed to to easily extract xpub from hardware wallets using BTCPayServer Vault.
Make sure you have [the latest version of BTCPayServer Vault](https://github.com/btcpayserver/BTCPayServer.Vault/releases) installed on your computer.

## Usage
- Navigate to Xpub Extractor by selecting it from the list on the left
- Connect your hardware wallet to your computer
- Once the page starts loading, the BTCPay Server Vault will request access, grant permission.
- If necessary enter PIN on your hardware wallet
- Once the form appears, you will have ability to select 
  - Signature Type: Singlesig / Multisig
  - Address type: Segwit, Segwit Wrapped, Legacy
  - Account: 0, 1, 2... 
- Depending on selection different derivation paths will be used to extract xpub
  - Singlesig Derivation Paths
    - Native Segwit (P2WPKH): m/84'/0'/0' 
    - Wrapped Segwit (P2SH-P2WPKH): m/49'/0'/0'
    - Legacy (P2PKH): m/44'/0'/0'
    - Taproot (P2TR): m/86'/0'/0'
  - Multisig Derivation Paths
    - Native Segwit Multisig (P2WSH): m/48'/0'/0'/2'
    - Wrapped Segwit Multisig (P2SH-P2WSH): m/48'/0'/0'/1'
    - Legacy Multisig (P2SH): m/45'/0'/0'
    - Taproot Multisig (potential future path): No standard currently, but using like m/48'/0'/0'/3'
- Once you click `Confirm` button the Derivation Scheme, Root Fingerprint, and KeyPath will be displayed
- For your convenience, click the 'Copy Information' button to copy all the data

See this video demonstration for more information:

https://github.com/user-attachments/assets/45136a17-f50d-4295-a520-44c2e0051de8

## Contributing to plugin development
Open issue or pull request on [BTCPayServerPlugins.RockstarDev GitHub](https://github.com/btcpayserver/BTCPayServer.Vault/releases)

## License
https://github.com/rockstardev/BTCPayServerPlugins.RockstarDev/blob/master/LICENSE
