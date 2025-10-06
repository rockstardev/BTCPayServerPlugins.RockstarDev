using System;
using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

public static class SeedEncryptionHelper
{
    /// <summary>
    /// Encrypts a seed phrase using AES encryption with a passphrase
    /// NOTE: This is NOT secure for production use - it's a basic encryption for MVP
    /// </summary>
    public static string EncryptSeed(string seedPhrase, string passphrase)
    {
        if (string.IsNullOrEmpty(seedPhrase) || string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Seed phrase and passphrase cannot be empty");

        using var aes = Aes.Create();
        aes.Key = DeriveKeyFromPassphrase(passphrase);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
        var plainBytes = Encoding.UTF8.GetBytes(seedPhrase);
        var encryptedBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        // Combine IV and encrypted data
        var result = new byte[aes.IV.Length + encryptedBytes.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encryptedBytes, 0, result, aes.IV.Length, encryptedBytes.Length);

        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts an encrypted seed phrase using the passphrase
    /// </summary>
    public static string DecryptSeed(string encryptedSeed, string passphrase)
    {
        if (string.IsNullOrEmpty(encryptedSeed) || string.IsNullOrEmpty(passphrase))
            throw new ArgumentException("Encrypted seed and passphrase cannot be empty");

        var fullCipher = Convert.FromBase64String(encryptedSeed);

        using var aes = Aes.Create();
        aes.Key = DeriveKeyFromPassphrase(passphrase);

        // Extract IV
        var iv = new byte[aes.IV.Length];
        var cipherText = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipherText, 0, cipherText.Length);
        aes.IV = iv;

        using var decryptor = aes.CreateDecryptor(aes.Key, aes.IV);
        var decryptedBytes = decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);

        return Encoding.UTF8.GetString(decryptedBytes);
    }

    /// <summary>
    /// Derives a 256-bit key from a passphrase using PBKDF2
    /// </summary>
    private static byte[] DeriveKeyFromPassphrase(string passphrase)
    {
        // Use a fixed salt for simplicity (NOT secure for production!)
        // In production, you'd want to store a unique salt per configuration
        var salt = Encoding.UTF8.GetBytes("WalletSweeperSalt2025");
        
        using var pbkdf2 = new Rfc2898DeriveBytes(passphrase, salt, 10000, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(32); // 256 bits
    }
}
