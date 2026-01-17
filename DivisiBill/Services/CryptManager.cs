#nullable enable

using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace DivisiBill.Services;

/// <summary>
/// <para>
/// <b>CryptManager implements hybrid encryption of streams for .NET MAUI:</b>
/// <list type="bullet">
/// <item>Symmetric payload encryption with AES-256-GCM.</item> 
/// <item>Ephemeral AES key is wrapped with RSA (OAEP-SHA256).</item>
/// <item>A compact header embeds a version and the SHA-256 fingerprint of the RSA public key to locate the correct private key.</item>
/// </list>
/// </para>
/// <para>
/// Encrypted Data Blob (v1) layout:
/// <code>
/// [Signature(8)][version(1)][fingerprint(32)][rsaWrappedKeyLength(4, little-endian)][rsaWrappedKey(var)][nonce(12)][ciphertext(..)][tag(16)]
/// </code>
/// </para>
/// <para>
/// <b>RSA key management:</b>
/// <list type="bullet">
/// <item>An RSA key is derived deterministically from a password by seeding a BouncyCastle SecureRandom with PBKDF2-SHA512 output, then generating a 2048-bit RSA keypair. The private key is converted to a .NET RSA and can be exported/imported as PKCS#8.</item>
/// <item>Keys are persisted in MAUI SecureStorage using a per-key storage name suffixed with the lowercase hex fingerprint.</item>
/// <item>An index of all stored keys is maintained to support bulk deletion.</item>
/// <item>Each RSA key is stored in secure storage under a key derived from its fingerprint; these are not normally removed, even when the password is changed, because they may have been used to encrypt something. The current password's fingerprint is stored.</item>
/// </list>
/// </para>
/// <para>
/// <b>Security notes:</b>
/// <list type="bullet">
/// <item>AES-GCM provides confidentiality and integrity via the authentication tag. Any modification to ciphertext or header fields causes decryption failure.</item>
/// <item>OAEP-SHA256 is used for RSA wrapping to provide modern padding and hash security.</item>
/// <item>The password-based RSA derivation is deterministic; changing the password changes the key.</item>
/// <item>Fingerprints are computed over the SubjectPublicKeyInfo (SPKI) and are 32 bytes (SHA-256).</item>
/// </list>
/// </para>
/// </summary>
internal class CryptManager
{
    #region Shared Constants
    // Symmetric crypto parameters
    private const int KeySize = 32;          // AES-256 key size in bytes
    private const int NonceSize = 12;        // Recommended nonce size for AES-GCM
    private const int TagSize = 16;          // Authentication tag size for AES-GCM (128-bit)

    // Header/versioning
    private const byte CurrentVersion = 1;   // Current header format version
    private const int FingerprintSize = 32;  // SHA-256 size in bytes
    private const int HeaderSize = 1;        // Version (1 byte)

    // Password hashing
    private const int PasswordSaltSize = 16;         // 128-bit salt
    private const int PasswordKeySize = 32;          // 256-bit derived key
    private const int PasswordIterations = 100_000;  // Tune for security/performance

    // Preference keys for storing various password and RSA related data
    private const string PasswordHashKey = "password.hash.v1"; // Not normally used; present for legacy reasons
    private const string ActiveRsaFingerprintKey = "password.fingerprint.v1";
    private const string RsaFingerprintListKey = "password.rsa.list.v1"; // We meed a list because you cannot enumerate secure storage

    // SecureStorage key prefix
    private const string RsaPrivateKeyPrefix = "password.rsa.prefix.v1."; // An RSA signature is appended to this to name the storage for each key
    #endregion
    #region Password / RSA management

    public static string PasswordSalt { get; set; } = string.Empty;// Salt value to be applied to all password uses (can be set at startup)

    /// <summary>
    /// Gets a value indicating whether a password hash has been stored for the current user or context.
    /// </summary>
    public static bool HasStoredPassword =>
        !string.IsNullOrEmpty(GetStoredPasswordFingerprintHex());

    /// <summary>
    /// Gets a value indicating whether an RSA key for the default password is currently in secure storage.
    /// </summary>
    /// <remarks>Use this property to determine if an RSA key has been previously saved and is available for
    /// retrieval. This property does not indicate the validity or usability of the stored key, only its
    /// presence.</remarks>
    public static bool HasStoredRsa
    {
        get
        {
            string? fingerprintHex = GetStoredPasswordFingerprintHex();
            if (string.IsNullOrWhiteSpace(fingerprintHex))
                return false;
            string storageKey = StorageKeyForFingerprintHex(fingerprintHex);
            string? base64 = Task.Run(() => SecureStorage.Default.GetAsync(storageKey)).GetAwaiter().GetResult();
            return !string.IsNullOrWhiteSpace(base64);
        }
    }

    /// <summary>
    /// Retrieves the stored password fingerprint as a string of hex digits if available, otherwise null.
    /// </summary>
    /// <remarks>The fingerprint is retrieved from application preferences using a predefined key. If the
    /// stored value is missing or not a valid hexadecimal string, the method returns null.</remarks>
    /// <returns>A string containing the stored password fingerprint, or null if no fingerprint is stored or the stored value
    /// is invalid.</returns>
    public static string? GetStoredPasswordFingerprintHex()
    {
        string? hexString = Preferences.Default.Get(ActiveRsaFingerprintKey, string.Empty);
        if (string.IsNullOrWhiteSpace(hexString))
            return null;
        try
        {
            _ = Convert.FromHexString(hexString);
            return hexString;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves the stored password fingerprint as a byte array, if available.
    /// </summary>
    /// <remarks>The fingerprint is retrieved from application preferences using a predefined key. If the
    /// stored value is missing or not a valid hexadecimal string, the method returns null.</remarks>
    /// <returns>A byte array containing the stored password fingerprint, or null if no fingerprint is stored or the stored value
    /// is invalid.</returns>
    public static byte[]? GetStoredPasswordFingerprint()
    {
        string? hexString = Preferences.Default.Get(ActiveRsaFingerprintKey, string.Empty);
        if (string.IsNullOrWhiteSpace(hexString))
            return null;
        try
        {
            return Convert.FromHexString(hexString);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets or removes the stored password fingerprint used for authentication.
    /// </summary>
    /// <param name="newFingerprint">The new fingerprint value to store. If null, an exception is thrown. If empty or consists only of white-space
    /// characters, the stored fingerprint is removed.</param>
    public static void SetStoredPasswordFingerprint(string newFingerprint)
    {
        ArgumentNullException.ThrowIfNull(newFingerprint);

        if (string.IsNullOrWhiteSpace(newFingerprint))
            Preferences.Default.Remove(ActiveRsaFingerprintKey);
        else
            Preferences.Default.Set(ActiveRsaFingerprintKey, newFingerprint);
    }
    /// <summary>
    /// Asynchronously verifies whether the specified password matches the stored password fingerprint.
    /// </summary>
    /// <remarks>If no password is stored, the method returns true iff the provided password is null or whitespace.
    /// The comparison is performed in constant time to help mitigate timing attacks.</remarks>
    /// <param name="password">The password to verify against the stored fingerprint. If null or whitespace, verification will only succeed if
    /// no password is stored.</param>
    /// <returns>A task that represents the asynchronous operation. The task result is true if the password
    /// matches the stored fingerprint; otherwise, false.</returns>
    public static async Task<bool> VerifyPasswordAgainstStoredAsync(string password)
    {
        byte[]? stored = GetStoredPasswordFingerprint();
        if (stored is null)
            return string.IsNullOrWhiteSpace(password);
        else if (string.IsNullOrWhiteSpace(password))
            return false;

        byte[]? localFingerprint = default;
        await Task.Run(() =>
        {
            using RSA rsa = RsaFromPassword(password);
            localFingerprint = ComputeRsaFingerprint(rsa);
        });
        return CryptographicOperations.FixedTimeEquals(stored, localFingerprint);
    }

    /// <summary>
    /// Derives and securely stores an RSA private key and its fingerprint based on the specified password. Updates the
    /// active fingerprint and maintains an index of stored fingerprints because there is no way to iterate through the list
    /// of keys in SecureStorage.
    /// </summary>
    /// <remarks>The derived private key is stored in secure storage and its fingerprint is persisted for
    /// future identification. If the fingerprint is new, it is added to the index of stored fingerprints. The method
    /// does not validate password strength; callers should ensure that a sufficiently strong password is provided for
    /// security.</remarks>
    /// <param name="password">The password used to derive the RSA private key. Cannot be null.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SetPasswordAsync(string password)
    {
        ArgumentNullException.ThrowIfNull(password);

        byte[]? fingerprint;
        return Task.Run(async () =>
        {
            // Derive RSA and persist fingerprint and private key
            using RSA rsa = RsaFromPassword(password);
            fingerprint = ComputeRsaFingerprint(rsa);
            Preferences.Default.Set(ActiveRsaFingerprintKey, ToFingerprintHex(fingerprint));
            byte[] privateKey = rsa.ExportPkcs8PrivateKey();
            try
            {
                byte[] fingerprint = ComputeRsaFingerprint(rsa);
                string fingerprintHex = ToFingerprintHex(fingerprint);

                string privateKeyBase64 = Convert.ToBase64String(privateKey);
                await SecureStorage.Default.SetAsync(StorageKeyForFingerprintHex(fingerprintHex), privateKeyBase64);

                // Update index (deduplicated)
                HashSet<string> index = await LoadVerifiedRsaFingerprintListAsync();
                if (index.Add(fingerprintHex))
                    SaveRsaFingerprintList(index);
            }
            finally
            {
                Array.Clear(privateKey, 0, privateKey.Length);
            }
        });
    }

    public static void ClearPassword() =>
        // Clear the active fingerprint (current identity)
        Preferences.Default.Remove(ActiveRsaFingerprintKey); // There is no longer a current RSA, though we keep the RSA itself for decryption

    public static async Task<RSA?> GetStoredRsaFromFingerprintAsync()
    {
        byte[]? fp = GetStoredPasswordFingerprint();
        return fp is null ? null : await RetrieveRsaByFingerprintAsync(fp);
    }
    #endregion Password / RSA management
    #region Password hashing primitives
    [Obsolete($"Use {nameof(GetStoredPasswordFingerprintHex)} instead")]
    public static string? GetStoredPasswordHash()
    {
        string hash = Preferences.Default.Get(PasswordHashKey, string.Empty);
        return string.IsNullOrWhiteSpace(hash) ? null : hash;
    }

    /// <summary>
    /// Hashes a password using PBKDF2-HMAC-SHA256 with a random salt.
    /// Format: {iterations}.{base64 salt}.{base64 key}
    /// </summary>
    [Obsolete($"See {nameof(SetPasswordAsync)} for alternatives.")]
    public static string HashPassword(string password)
    {
        using var rng = RandomNumberGenerator.Create();
        byte[] salt = new byte[PasswordSaltSize];
        rng.GetBytes(salt);

        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, PasswordIterations, HashAlgorithmName.SHA256, PasswordKeySize);

        return $"{PasswordIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
    }

    /// <summary>
    /// Verifies a password against a stored PBKDF2-HMAC-SHA256 hash.
    /// </summary>
    [Obsolete($"Use {nameof(VerifyPasswordAgainstStoredAsync)} instead.")]
    public static bool VerifyPasswordHash(string password, string hashedPassword)
    {
        string[] parts = hashedPassword.Split('.');
        if (parts.Length != 3)
            return false;

        if (!int.TryParse(parts[0], out int iterations) || iterations <= 1)
            return false;
        byte[] salt = Convert.FromBase64String(parts[1]);
        byte[] key = Convert.FromBase64String(parts[2]);

        byte[] attemptedKey = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, PasswordKeySize);

        return CryptographicOperations.FixedTimeEquals(key, attemptedKey);
    }
    #endregion Password hashing primitives
    #region Hybrid encryption
    private static readonly byte[] signature = "DM250928"u8.ToArray();

    /// <summary>
    /// Encrypts the given plaintext using AES-256-GCM and wraps the AES key with RSA-OAEP(SHA-256).
    /// </summary>
    /// <param name="plaintext">The plaintext to encrypt (as a readonly span).</param>
    /// <param name="rsa">RSA public key for wrapping the AES key.</param>
    /// <returns>The encrypted envelope as a byte array: header(v1) + RSA-OAEP(SHA-256)-wrapped AES key + nonce + ciphertext + tag.</returns>
    public static byte[] EncryptToBytes(ReadOnlySpan<byte> plaintext, RSA rsa)
    {
        // Generate a fresh AES-256 key and wrap it with RSA-OAEP(SHA-256)
        byte[] aesKey = new byte[KeySize];
        RandomNumberGenerator.Fill(aesKey);

        byte[] aesEncrypted = rsa.Encrypt(aesKey, RSAEncryptionPadding.OaepSHA256);

        // Generate a random GCM nonce
        byte[] nonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(nonce);

        // Prepare buffers for ciphertext and tag
        byte[] ciphertext = new byte[plaintext.Length];
        byte[] tag = new byte[TagSize];

        // Encrypt plaintext
        using (AesGcm aesGcm = new(aesKey, TagSize))
        {
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);
        }

        // Build header: [signature][version][fingerprint][aesEncryptedLength][aesEncrypted][nonce]
        byte[] fingerprint = ComputeRsaFingerprint(rsa);
        byte[] versionBuf = [CurrentVersion];
        byte[] encLenBytes = BitConverter.GetBytes(aesEncrypted.Length);

        int totalLength = signature.Length + versionBuf.Length + fingerprint.Length +
                         encLenBytes.Length + aesEncrypted.Length + nonce.Length +
                         ciphertext.Length + tag.Length;

        byte[] envelope = new byte[totalLength];
        int offset = 0;

        Buffer.BlockCopy(signature, 0, envelope, offset, signature.Length);
        offset += signature.Length;

        Buffer.BlockCopy(versionBuf, 0, envelope, offset, versionBuf.Length);
        offset += versionBuf.Length;

        Buffer.BlockCopy(fingerprint, 0, envelope, offset, fingerprint.Length);
        offset += fingerprint.Length;

        Buffer.BlockCopy(encLenBytes, 0, envelope, offset, encLenBytes.Length);
        offset += encLenBytes.Length;

        Buffer.BlockCopy(aesEncrypted, 0, envelope, offset, aesEncrypted.Length);
        offset += aesEncrypted.Length;

        Buffer.BlockCopy(nonce, 0, envelope, offset, nonce.Length);
        offset += nonce.Length;

        Buffer.BlockCopy(ciphertext, 0, envelope, offset, ciphertext.Length);
        offset += ciphertext.Length;

        Buffer.BlockCopy(tag, 0, envelope, offset, tag.Length);

        // Clear sensitive key material
        Array.Clear(aesKey, 0, aesKey.Length);

        return envelope;
    }

    /// <summary>
    /// Encrypts the entire input stream using AES-256-GCM and writes a self-describing hybrid format:
    /// header(v1) + RSA-OAEP(SHA-256)-wrapped AES key + nonce + ciphertext + tag.
    /// </summary>
    public static async Task EncryptAsync(Stream inputStream, Stream outputStream, RSA rsa, CancellationToken cancellationToken = default)
    {
        // Read plaintext from input stream
        byte[] plaintext = await ReadFullyAsync(inputStream, cancellationToken).ConfigureAwait(false);
        // Encrypt using EncryptToBytes
        byte[] encrypted = await Task.Run(() => EncryptToBytes(plaintext, rsa));
        // Write encrypted data to output stream
        await outputStream.WriteAsync(encrypted, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Decrypts the specified encrypted data and returns the plaintext as a byte array.
    /// </summary>
    /// <remarks>This method expects the encrypted data to be in a specific format that includes a header,
    /// fingerprint, RSA-encrypted AES key, nonce, ciphertext, and authentication tag. The appropriate RSA private key
    /// must be available for decryption. The method performs cryptographic validation and will throw an exception if
    /// the data is malformed or authentication fails.</remarks>
    /// <param name="encrypted">A read-only span containing the encrypted data to decrypt. The data must be formatted with the expected header,
    /// fingerprint, encrypted key, nonce, ciphertext, and authentication tag.</param>
    /// <returns>A byte array containing the decrypted plaintext. The array will be empty if the original plaintext was empty.</returns>
    /// <exception cref="InvalidDataException">Thrown if the input data is missing required header fields, has an unrecognized signature or version, contains
    /// invalid lengths, the RSA private key cannot be found or does not match the fingerprint, or if any other format
    /// or cryptographic validation fails.</exception>
    public static byte[] DecryptToBytes(ReadOnlySpan<byte> encrypted, RSA? rsaParameter = null)
    {
        // Parse header fields
        int offset = 0;

        // Signature
        if (encrypted.Length < signature.Length)
            throw new InvalidDataException("Missing signature.");
        if (!encrypted[..signature.Length].SequenceEqual(signature))
            throw new InvalidDataException("Unrecognized signature.");
        offset += signature.Length;

        // Version
        if (encrypted.Length < offset + 1)
            throw new InvalidDataException("Missing version.");
        byte version = encrypted[offset];
        offset += 1;

        int aesEncryptedLength;
        byte[] storedFingerprint;

        if (version == 1)
        {
            // Fingerprint
            if (encrypted.Length < offset + FingerprintSize)
                throw new InvalidDataException("Missing fingerprint.");
            storedFingerprint = encrypted.Slice(offset, FingerprintSize).ToArray();
            offset += FingerprintSize;

            // Wrapped key length
            if (encrypted.Length < offset + sizeof(int))
                throw new InvalidDataException("Missing encrypted key length.");
            aesEncryptedLength = BitConverter.ToInt32(encrypted.Slice(offset, sizeof(int)));
            offset += sizeof(int);
        }
        else
            throw new InvalidDataException($"Unsupported header version ({version}).");

        if (aesEncryptedLength is < 63 or > 1024)
            throw new InvalidDataException($"Invalid encrypted key length ({aesEncryptedLength}).");

        string fingerprintHex = ToFingerprintHex(storedFingerprint);
        RSA? rsa = null;
        try
        {
            rsa = (rsaParameter is null)
                ? Task.Run(() => RetrieveRsaByFingerprintAsync(fingerprintHex)).GetAwaiter().GetResult() // Locate RSA by fingerprint
                : rsaParameter;

            if (rsa is null)
                throw new InvalidDataException($"No RSA private key found for fingerprint {fingerprintHex}.");

            byte[] localFingerprint = ComputeRsaFingerprint(rsa);
            if (!CryptographicOperations.FixedTimeEquals(storedFingerprint, localFingerprint))
                throw new InvalidDataException("RSA fingerprint mismatch.");

            // RSA-encrypted AES key
            if (encrypted.Length < offset + aesEncryptedLength)
                throw new InvalidDataException("Missing encrypted AES key.");
            byte[] aesEncrypted = encrypted.Slice(offset, aesEncryptedLength).ToArray();
            offset += aesEncryptedLength;

            // Nonce
            if (encrypted.Length < offset + NonceSize)
                throw new InvalidDataException("Missing nonce.");
            byte[] nonce = encrypted.Slice(offset, NonceSize).ToArray();
            offset += NonceSize;

            // Ciphertext + tag
            int remaining = encrypted.Length - offset;
            if (remaining < TagSize)
                throw new InvalidDataException("Missing authentication tag.");
            int cipherLength = remaining - TagSize;
            byte[] ciphertext = encrypted.Slice(offset, cipherLength).ToArray();
            byte[] tag = encrypted.Slice(offset + cipherLength, TagSize).ToArray();

            // Decrypt AES key
            byte[] aesKey = rsa.Decrypt(aesEncrypted, RSAEncryptionPadding.OaepSHA256);

            // Decrypt payload
            byte[] plaintext = new byte[cipherLength];
            using AesGcm aesGcm = new(aesKey, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }
        finally
        {
            if (rsaParameter is null)
                rsa?.Clear(); // if we allocated it then clear it
        }
    }
    /// <summary>
    /// Decrypts a stream previously produced by <see cref="EncryptAsync"/> using the RSA private key
    /// identified by the embedded fingerprint stored in SecureStorage.
    /// </summary>
    /// <param name="inputStream">The hybrid envelope stream.</param>
    /// <param name="outputStream">Destination stream receiving the plaintext.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidDataException">
    /// Thrown when the header is invalid, the required RSA key is missing/mismatched,
    /// or when the authentication tag verification fails.
    /// </exception>
    public static async Task DecryptAsync(Stream inputStream, Stream outputStream, CancellationToken cancellationToken = default)
    {
        // Read the entire encrypted envelope from the input stream
        byte[] encrypted = await ReadFullyAsync(inputStream, cancellationToken).ConfigureAwait(false);
        // Decrypt using DecryptEnvelopeToBytes
        byte[] plaintext = await Task.Run(() => DecryptToBytes(encrypted));
        // Write the plaintext to the output stream
        await outputStream.WriteAsync(plaintext, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Asynchronously decrypts a Base64-encoded string and returns the resulting plaintext.
    /// </summary>
    /// <remarks>The method expects the input string to be a valid Base64-encoded representation of encrypted
    /// data.  If the input is invalid or the decryption process fails, an exception may be thrown.</remarks>
    /// <param name="encryptedB64String">A Base64-encoded string representing the encrypted data. This value cannot be <see langword="null"/> or empty.</param>
    /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The task result contains the decrypted
    /// plaintext string.</returns>
    public static async Task<string> DecryptB64StringAsync(string encryptedB64String)
    {
        byte[] encryptedBytes = Convert.FromBase64String(encryptedB64String);
        byte[] plaintextBytes = await Task.Run(() => DecryptToBytes(encryptedBytes));
        return Encoding.UTF8.GetString(plaintextBytes);
    }
    #endregion Hybrid encryption
    #region Archive and Restore RSA Keys
    /// <summary>
    /// Archives all stored RSA private keys into a zip file.
    /// Only private keys are stored as [fingerprint].enc (encrypted with password).
    /// Public keys are not included in the archive, but can be trivially recovered from
    /// the PKCS#8 private key data.
    /// </summary>
    /// <param name="password">Password used to encrypt private keys.</param>
    /// <param name="outputStream">Destination stream for the zip archive.</param>
    /// <returns>Task representing the archive operation and an integer representing
    /// the number of keys archived.</returns>
    public static async Task<int> ArchivePrivateKeysToZipAsync(string password, Stream outputStream)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(outputStream);

        HashSet<string> fingerprints = await LoadVerifiedRsaFingerprintListAsync();
        if (fingerprints.Count == 0)
            return 0;
        using ZipArchive zip = new(outputStream, ZipArchiveMode.Create, leaveOpen: true);
        using RSA rsaFromPwd = RsaFromPassword(password);

        // Get the active fingerprint and ensure it's first
        string? activeFingerprintHex = Preferences.Default.Get(ActiveRsaFingerprintKey, string.Empty);
        List<string> orderedFingerprints = [];

        if (!string.IsNullOrWhiteSpace(activeFingerprintHex) && fingerprints.Contains(activeFingerprintHex))
        {
            orderedFingerprints.Add(activeFingerprintHex);
            fingerprints.Remove(activeFingerprintHex);
        }
        orderedFingerprints.AddRange(fingerprints);

        foreach (string fingerprintHex in orderedFingerprints)
        {
            // Retrieve private key (the public key can be derived from it)
            string storageKey = StorageKeyForFingerprintHex(fingerprintHex);
            string? base64 = await SecureStorage.Default.GetAsync(storageKey);
            if (string.IsNullOrWhiteSpace(base64))
                continue;

            byte[] pkcs8 = Convert.FromBase64String(base64);

            // Encrypt private key with password
            byte[] encryptedPrivateKey = EncryptToBytes(pkcs8, rsaFromPwd);

            // Add encrypted private key entry
            ZipArchiveEntry encEntry = zip.CreateEntry(fingerprintHex + ".enc", CompressionLevel.Optimal);
            using (Stream encStream = encEntry.Open())
            {
                await encStream.WriteAsync(encryptedPrivateKey);
            }

            // Clear sensitive buffers
            Array.Clear(pkcs8);
            Array.Clear(encryptedPrivateKey);
        }
        return orderedFingerprints.Count;
    }

    /// <summary>
    /// Restores only private RSA keys from a zip archive stream.
    /// Selects entries that end with .enc recreates the fingerprint index from the imported private keys.
    /// If the current password fingerprint is not set, it is set from the first ".enc" entry in the zip.
    /// </summary>
    /// <param name="password">Password used to decrypt private keys.</param>
    /// <param name="zipStream">Input stream containing the zip archive.</param>
    /// <returns>Task representing the restore operation and an integer representing th
    /// number of keys restored.</returns>
    public static async Task<(int, int)> RestorePrivateKeysFromZipAsync(string password, Stream zipStream)
    {
        ArgumentNullException.ThrowIfNull(password);
        ArgumentNullException.ThrowIfNull(zipStream);

        // Collect fingerprints as we restore
        HashSet<string> importedFingerprints = new(StringComparer.Ordinal);
        HashSet<string> existingFingerprints = await LoadVerifiedRsaFingerprintListAsync();

        using ZipArchive zip = new(zipStream, ZipArchiveMode.Read, leaveOpen: true);
        using RSA rsaFromPwd = RsaFromPassword(password);

        var keyEntries = zip.Entries.Where((e) => e.Name.EndsWith(".enc")).ToList();

        foreach (ZipArchiveEntry entry in keyEntries)
        {
            string fingerprintHex = entry.Name[..^4];
            if (!IsHexFingerprint(fingerprintHex))
                continue;
            if (existingFingerprints.Contains(fingerprintHex))
                continue; // Do not bother to decrypt keys we already have

            // Read encrypted private key
            using Stream encStream = entry.Open();
            using MemoryStream ms = new();
            await encStream.CopyToAsync(ms);
            byte[] encryptedPrivateKey = ms.ToArray();

            // Decrypt private key using password-derived RSA
            byte[] pkcs8 = DecryptToBytes(encryptedPrivateKey, rsaFromPwd);

            // Store in SecureStorage
            string base64 = Convert.ToBase64String(pkcs8);
            await SecureStorage.Default.SetAsync(StorageKeyForFingerprintHex(fingerprintHex), base64);

            importedFingerprints.Add(fingerprintHex);

            Array.Clear(pkcs8);
            Array.Clear(encryptedPrivateKey);
        }

        // The key entries appear in the list in reverse order, so set the password from the actual first entry if it's not already set
        if (!CryptManager.HasStoredPassword && keyEntries.Count > 0)
            SetStoredPasswordFingerprint(keyEntries[^1].Name[..^4]);

        // Merge existing list and new one
        existingFingerprints.UnionWith(importedFingerprints);
        SaveRsaFingerprintList(existingFingerprints);
        return (importedFingerprints.Count, keyEntries.Count);
    }
    #endregion
    #region Fingerprint Utilities
    /// <summary>
    /// Computes a 32-byte SHA-256 fingerprint of the RSA public key (SPKI DER).
    /// </summary>
    /// <param name="rsa">RSA key (public component is used).</param>
    /// <returns>32-byte fingerprint suitable for storage key addressing.</returns>
    internal static byte[] ComputeRsaFingerprint(RSA rsa)
    {
        // SHA-256 over the SubjectPublicKeyInfo (SPKI) DER bytes
        byte[] publicKey = rsa.ExportSubjectPublicKeyInfo();
        try
        {
            using var sha256 = SHA256.Create();
            return sha256.ComputeHash(publicKey); // 32 bytes
        }
        finally
        {
            Array.Clear(publicKey, 0, publicKey.Length);
        }
    }

    private static string ToFingerprintHex(byte[] fingerprint) =>
        Convert.ToHexString(fingerprint).ToLowerInvariant();

    private static string StorageKeyForFingerprintHex(string fingerprintHex) =>
        RsaPrivateKeyPrefix + fingerprintHex.ToLowerInvariant();
    #endregion Fingerprint Utilities
    #region Stream helpers
    /// <summary>
    /// Reads the entire stream into a byte array.
    /// </summary>
    private static async Task<byte[]> ReadFullyAsync(Stream stream, CancellationToken cancellationToken)
    {
        using MemoryStream ms = new();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return ms.ToArray();
    }

    /// <summary>
    /// Reads exactly <paramref name="count"/> bytes or throws on EOF.
    /// </summary>
    private static async Task ReadExactlyAsync(Stream stream, byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        int readTotal = 0;
        while (readTotal < count)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + readTotal, count - readTotal), cancellationToken).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Unexpected end of stream.");
            readTotal += read;
        }
    }
    #endregion
    #region Deterministic RSA derivation
    /// <summary>
    /// Derives a deterministic seed from the password and salt using PBKDF2-SHA512.
    /// </summary>
    private static byte[] DeriveSeed(string password, byte[] salt, int seedLength = 32)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA512, seedLength);

    /// <summary>
    /// Generates a deterministic RSA keypair by seeding BouncyCastle's SecureRandom with the provided seed.
    /// </summary>
    private static AsymmetricCipherKeyPair GenerateDeterministicRsaKey(byte[] seed, int keySize = 2048)
    {
        SecureRandom random = new(new Org.BouncyCastle.Crypto.Prng.DigestRandomGenerator(new Org.BouncyCastle.Crypto.Digests.Sha512Digest()));
        random.SetSeed(seed);

        RsaKeyPairGenerator keyGen = new();
        keyGen.Init(new KeyGenerationParameters(random, keySize));

        return keyGen.GenerateKeyPair();
    }

    /// <summary>
    /// Attempts to clear sensitive big integer components from a BouncyCastle keypair.
    /// </summary>
    private static void SecurelyDisposeKeyPair(AsymmetricCipherKeyPair keyPair)
    {
        static void ClearBigInteger(Org.BouncyCastle.Math.BigInteger value)
        {
            byte[] bytes = value.ToByteArrayUnsigned();
            Array.Clear(bytes, 0, bytes.Length);
        }

        if (keyPair?.Private is RsaPrivateCrtKeyParameters rsaPrivate)
        {
            ClearBigInteger(rsaPrivate.Exponent);
            ClearBigInteger(rsaPrivate.P);
            ClearBigInteger(rsaPrivate.Q);
            ClearBigInteger(rsaPrivate.DP);
            ClearBigInteger(rsaPrivate.DQ);
            ClearBigInteger(rsaPrivate.QInv);
            ClearBigInteger(rsaPrivate.Modulus);
        }

        if (keyPair?.Public is RsaKeyParameters rsaPublic)
        {
            ClearBigInteger(rsaPublic.Exponent);
            ClearBigInteger(rsaPublic.Modulus);
        }
    }

    /// <summary>
    /// Converts a BouncyCastle RSA keypair into a .NET <see cref="RSA"/> instance (PKCS#1 parameters).
    /// </summary>
    private static RSA ConvertToDotNetRsa(AsymmetricCipherKeyPair keyPair)
    {
        // Extract private key parameters
        var privateKeyParams = (RsaPrivateCrtKeyParameters)keyPair.Private;

        // Convert to RSAParameters
        var rsaParams = DotNetUtilities.ToRSAParameters(privateKeyParams);

        // Create and import into RSA object
        var rsa = RSA.Create();
        rsa.ImportParameters(rsaParams);
        rsaParams = default; // Clear sensitive data
        return rsa;
    }

    /// <summary>
    /// Produces a deterministic RSA key from a password using PBKDF2(SHA-512) to seed a deterministic RSA generator.
    /// </summary>
    /// <param name="password">Input password.</param>
    /// <returns>Disposable RSA instance containing private key material.</returns>
    /// <remarks>
    /// Changing the password yields a different key; store the derived key (or its fingerprint) if you need stable lookup.
    /// </remarks>
    public static RSA RsaFromPassword(string password)
    {
        byte[] salt = Encoding.UTF8.GetBytes(PasswordSalt);
        byte[] seed = DeriveSeed(password, salt);
        AsymmetricCipherKeyPair keyPair = GenerateDeterministicRsaKey(seed);
        try
        {
            return ConvertToDotNetRsa(keyPair);
        }
        finally
        {
            SecurelyDisposeKeyPair(keyPair);
            Array.Clear(seed, 0, seed.Length);
        }
    }
    #endregion Deterministic RSA derivation
    #region SecureStorage index helpers
    /// <summary>
    /// Loads the persisted RSA private key index from Preferences. Does not check secure storage 
    /// (use <see cref="LoadVerifiedRsaFingerprintListAsync"/> for that).
    /// Each entry in the index is the fingerprint part of the full SecureStorage key where a PKCS#8 private key is stored.
    /// The index:
    ///   - Enables O(1) existence checks using a HashSet.
    ///   - Supports bulk deletion (we iterate the keys and remove each from SecureStorage).
    /// Storage format: newline-delimited list (no trailing newline). Empty / missing value => empty set.
    /// </summary>
    /// <remarks>
    /// We intentionally keep the index in Preferences (plain) because:
    ///   - Keys themselves remain protected in SecureStorage.
    ///   - The index contains only identifiers (names), not secret material.
    /// </remarks>
    public static HashSet<string> LoadRsaFingerprintList()
    {
        string? data = Preferences.Default.Get(RsaFingerprintListKey, string.Empty);
        HashSet<string> set = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(data))
        {
            foreach (string line in data.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!IsHexFingerprint(line))
                    throw new InvalidDataException($"Index data was not a fingerprint: {line}.");
                set.Add(line.ToLowerInvariant());
            }
        }
        return set;
    }

    /// <summary>
    /// Asynchronously loads the set of RSA fingerprint hex strings that have been verified to be present in secure storage.
    /// </summary>
    /// <remarks>Each fingerprint is included in the returned set only if a corresponding entry exists in
    /// secure storage. The comparison is case-insensitive, and all fingerprints are returned in lowercase. This method
    /// is thread-safe and can be awaited.</remarks>
    /// <returns>A hash set containing the lowercase RSA fingerprint hex strings that are verified and found in secure storage.
    /// The set will be empty if no verified fingerprints are available.</returns>
    public static async Task<HashSet<string>> LoadVerifiedRsaFingerprintListAsync()
    {
        HashSet<string> fingerPrintSet = LoadRsaFingerprintList();
        HashSet<string> set = new(StringComparer.Ordinal);
        foreach (string line in fingerPrintSet)
        {
            // Ensure there is a correctly named item in secure storage
            string storageKey = StorageKeyForFingerprintHex(line);
            string? base64 = await SecureStorage.Default.GetAsync(storageKey);
            if (string.IsNullOrWhiteSpace(base64))
                continue; // Skip missing keys
            set.Add(line.ToLowerInvariant());
        }
        return set;
    }

    /// <summary>
    /// Persists the deduplicated RSA key index back to Preferences.
    /// Input is assumed already distinct (HashSet). Order is not significant.
    /// Uses '\n' join for compactness; no trailing newline to avoid empty tokens on reload.
    /// </summary>
    /// <param name="fingerprints">Set of SecureStorage fingerprints for stored RSA private keys.</param>
    private static void SaveRsaFingerprintList(HashSet<string> fingerprints)
    {
        string data = string.Join('\n', fingerprints);
        Preferences.Default.Set(RsaFingerprintListKey, data);
    }

    private static bool IsHexFingerprint(string s)
    {
        if (s.Length != 64)
            return false;
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            bool hex = c is (>= '0' and <= '9') or
                       (>= 'a' and <= 'f') or
                       (>= 'A' and <= 'F');
            if (!hex)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Retrieves an RSA private key by its 32-byte fingerprint from SecureStorage.
    /// </summary>
    /// <param name="fingerprint">32-byte SHA-256 fingerprint of the SPKI.</param>
    /// <returns>RSA instance or null if not found/invalid.</returns>
    public static async Task<RSA?> RetrieveRsaByFingerprintAsync(byte[] fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != FingerprintSize)
            return null;

        string storageKey = StorageKeyForFingerprintHex(ToFingerprintHex(fingerprint));
        string? base64 = await SecureStorage.Default.GetAsync(storageKey);
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            byte[] pkcs8 = Convert.FromBase64String(base64);
            try
            {
                var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                return rsa;
            }
            finally
            {
                Array.Clear(pkcs8, 0, pkcs8.Length);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Retrieves an RSA private key by lowercase/uppercase hex fingerprint (64 chars).
    /// </summary>
    /// <param name="fingerprintHex">Hex string of the 32-byte fingerprint.</param>
    public static Task<RSA?> RetrieveRsaByFingerprintAsync(string fingerprintHex)
    {
        if (string.IsNullOrWhiteSpace(fingerprintHex))
            return Task.FromResult<RSA?>(null);

        string storageKey = StorageKeyForFingerprintHex(fingerprintHex);
        return RetrieveRsaByStorageKeyAsync(storageKey);
    }

    /// <summary>
    /// Same as <see cref="RetrieveRsaByFingerprintAsync(string)"/>, but uses the full SecureStorage key.
    /// </summary>
    private static async Task<RSA?> RetrieveRsaByStorageKeyAsync(string storageKey)
    {
        string? base64 = await SecureStorage.Default.GetAsync(storageKey);
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            byte[] pkcs8 = Convert.FromBase64String(base64);
            try
            {
                var rsa = RSA.Create();
                rsa.ImportPkcs8PrivateKey(pkcs8, out _);
                return rsa;
            }
            finally
            {
                Array.Clear(pkcs8, 0, pkcs8.Length);
            }
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Bulk-deletes all stored RSA keys and the index from SecureStorage
    /// </summary>
    /// <returns>The number of removed entries.</returns>
    public static int DeleteAllStoredRsa()
    {
        int removed = 0;

        try
        {
            // Remove all indexed keys
            HashSet<string> fingerprints = LoadRsaFingerprintList();
            foreach (string fingerprint in fingerprints)
            {
                if (SecureStorage.Default.Remove(CryptManager.StorageKeyForFingerprintHex(fingerprint)))
                    removed++;
            }
        }
        finally
        {
            // Remove the index itself, even if iterating through it (above) failed
            Preferences.Default.Remove(RsaFingerprintListKey);
        }

        return removed;
    }

    /// <summary>
    /// Delete a single RSA key by fingerprint (32-byte raw)
    /// </summary>
    /// <param name="fingerprint"></param>
    /// <returns></returns>
    public static bool DeleteRsaByFingerprint(byte[] fingerprint)
    {
        if (fingerprint is null || fingerprint.Length != FingerprintSize)
            return false;

        string storageKey = StorageKeyForFingerprintHex(ToFingerprintHex(fingerprint));
        bool removed = SecureStorage.Default.Remove(storageKey);
        if (removed)
        {
            HashSet<string> index = LoadRsaFingerprintList();
            index.Remove(ToFingerprintHex(fingerprint));
            SaveRsaFingerprintList(index);
        }
        return removed;
    }
    #endregion SecureStorage index helpers
}
