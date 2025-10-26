using System;
using System.Security.Cryptography;
using System.Text;

namespace BTCPayServer.RockstarDev.Plugins.WalletSweeper.Services;

/// <summary>
/// Service for encrypting and decrypting seed phrases using AES-256
/// </summary>
public class SeedEncryptionService
{
    private const int KeySize = 256;
    private const int IvSize = 16; // 128 bits for AES

    /// <summary>
    /// Encrypts a seed phrase using a password
    /// </summary>
    /// <param name="seedPhrase">The mnemonic seed phrase to encrypt</param>
    /// <param name="password">Password to use for encryption</param>
    /// <returns>Base64 encoded encrypted data (IV + ciphertext)</returns>
    public string EncryptSeed(string seedPhrase, string password)
    {
        if (string.IsNullOrWhiteSpace(seedPhrase))
            throw new ArgumentException("Seed phrase cannot be empty", nameof(seedPhrase));
        
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        using var aes = Aes.Create();
        aes.KeySize = KeySize;
        
        // Derive key from password using PBKDF2
        var salt = GenerateSalt();
        var key = DeriveKey(password, salt);
        
        aes.Key = key;
        aes.GenerateIV();
        
        using var encryptor = aes.CreateEncryptor();
        var plainTextBytes = Encoding.UTF8.GetBytes(seedPhrase);
        var cipherTextBytes = encryptor.TransformFinalBlock(plainTextBytes, 0, plainTextBytes.Length);
        
        // Combine salt + IV + ciphertext
        var result = new byte[salt.Length + aes.IV.Length + cipherTextBytes.Length];
        Buffer.BlockCopy(salt, 0, result, 0, salt.Length);
        Buffer.BlockCopy(aes.IV, 0, result, salt.Length, aes.IV.Length);
        Buffer.BlockCopy(cipherTextBytes, 0, result, salt.Length + aes.IV.Length, cipherTextBytes.Length);
        
        return Convert.ToBase64String(result);
    }

    /// <summary>
    /// Decrypts an encrypted seed phrase using a password
    /// </summary>
    /// <param name="encryptedData">Base64 encoded encrypted data</param>
    /// <param name="password">Password used for encryption</param>
    /// <returns>Decrypted seed phrase</returns>
    public string DecryptSeed(string encryptedData, string password)
    {
        if (string.IsNullOrWhiteSpace(encryptedData))
            throw new ArgumentException("Encrypted data cannot be empty", nameof(encryptedData));
        
        if (string.IsNullOrWhiteSpace(password))
            throw new ArgumentException("Password cannot be empty", nameof(password));

        try
        {
            var fullData = Convert.FromBase64String(encryptedData);
            
            // Extract salt (first 32 bytes)
            var salt = new byte[32];
            Buffer.BlockCopy(fullData, 0, salt, 0, salt.Length);
            
            // Extract IV (next 16 bytes)
            var iv = new byte[IvSize];
            Buffer.BlockCopy(fullData, salt.Length, iv, 0, iv.Length);
            
            // Extract ciphertext (remaining bytes)
            var cipherTextBytes = new byte[fullData.Length - salt.Length - iv.Length];
            Buffer.BlockCopy(fullData, salt.Length + iv.Length, cipherTextBytes, 0, cipherTextBytes.Length);
            
            // Derive key from password using same salt
            var key = DeriveKey(password, salt);
            
            using var aes = Aes.Create();
            aes.KeySize = KeySize;
            aes.Key = key;
            aes.IV = iv;
            
            using var decryptor = aes.CreateDecryptor();
            var plainTextBytes = decryptor.TransformFinalBlock(cipherTextBytes, 0, cipherTextBytes.Length);
            
            return Encoding.UTF8.GetString(plainTextBytes);
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Failed to decrypt seed phrase. Incorrect password or corrupted data.", ex);
        }
    }

    private byte[] GenerateSalt()
    {
        var salt = new byte[32]; // 256 bits
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(salt);
        return salt;
    }

    private byte[] DeriveKey(string password, byte[] salt)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(
            password,
            salt,
            iterations: 100000, // OWASP recommendation
            HashAlgorithmName.SHA256);
        
        return pbkdf2.GetBytes(KeySize / 8); // 32 bytes for 256-bit key
    }
}
