using System.Security.Cryptography;
using System.Text;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <inheritdoc cref="ITokenProtector"/>
public sealed class TokenProtector : ITokenProtector
{
    // Envelope layout for Passphrase mode:
    //   [0..16)   salt
    //   [16..28)  nonce
    //   [28..44)  GCM tag
    //   [44..)    ciphertext
    private const int SaltLength = 16;
    private const int NonceLength = 12;   // 96-bit, the GCM standard
    private const int TagLength = 16;
    private const int KeyLength = 32;     // AES-256

    // OWASP's 2023 floor for PBKDF2-HMAC-SHA256. Measured at ~50ms, which is
    // a one-off cost per unlock rather than per keystroke.
    private const int Pbkdf2Iterations = 210_000;

    private const string PassphrasePrefix = "pbkdf2$";
    private const string DpapiPrefix = "dpapi$";

    /// <summary>Entropy mixed into DPAPI so another app's blob can't be swapped in.</summary>
    private static readonly byte[] DpapiEntropy =
        Encoding.UTF8.GetBytes("QuizBuilder.GitHubToken.v1");

    private TokenProtectionMode _mode = TokenProtectionMode.MachineBound;

    /// <summary>Derived key, held only while unlocked in Passphrase mode.</summary>
    private byte[]? _sessionKey;

    /// <summary>Salt from the stored envelope, so re-Protect reuses the same key.</summary>
    private byte[]? _sessionSalt;

    /// <summary>In-memory token for None mode.</summary>
    private string? _transientToken;

    /// <summary>Set by the settings service so RequiresPassphrase can be answered.</summary>
    private string? _pendingCipherText;

    public TokenProtectionMode Mode => _mode;

    public bool IsUnlocked => _mode switch
    {
        TokenProtectionMode.MachineBound => true,
        TokenProtectionMode.None => true,
        TokenProtectionMode.Passphrase => _sessionKey is not null,
        _ => false
    };

    public bool RequiresPassphrase =>
        _mode == TokenProtectionMode.Passphrase
        && _sessionKey is null
        && !string.IsNullOrEmpty(_pendingCipherText);

    /// <summary>
    /// Tells the protector what ciphertext exists on disk, so it can report
    /// RequiresPassphrase before any decryption is attempted.
    /// </summary>
    public void SetPendingCipherText(string? cipherText) => _pendingCipherText = cipherText;

    public void SetMode(TokenProtectionMode mode)
    {
        if (_mode == mode) return;
        Lock();
        _transientToken = null;
        _mode = mode;
    }

    public bool Unlock(string passphrase)
    {
        if (_mode != TokenProtectionMode.Passphrase) return true;
        if (string.IsNullOrEmpty(passphrase)) return false;

        // No stored ciphertext: accept the passphrase and derive a fresh key
        // with a new salt, ready for the first Protect call.
        if (string.IsNullOrEmpty(_pendingCipherText))
        {
            _sessionSalt = RandomNumberGenerator.GetBytes(SaltLength);
            _sessionKey = DeriveKey(passphrase, _sessionSalt);
            return true;
        }

        // Otherwise the passphrase is only correct if GCM authenticates.
        try
        {
            var envelope = Convert.FromBase64String(StripPrefix(_pendingCipherText, PassphrasePrefix));
            if (envelope.Length < SaltLength + NonceLength + TagLength) return false;

            var salt = envelope.AsSpan(0, SaltLength).ToArray();
            var key = DeriveKey(passphrase, salt);

            // Throws CryptographicException on a wrong passphrase.
            _ = DecryptGcm(envelope, key);

            _sessionSalt = salt;
            _sessionKey = key;
            return true;
        }
        catch (CryptographicException)
        {
            return false;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public void Lock()
    {
        if (_sessionKey is not null)
        {
            CryptographicOperations.ZeroMemory(_sessionKey);
            _sessionKey = null;
        }
        _sessionSalt = null;
    }

    public string? Protect(string? plainText)
    {
        if (string.IsNullOrEmpty(plainText)) return null;

        switch (_mode)
        {
            case TokenProtectionMode.None:
                // Held in memory for this session only; nothing goes to disk.
                _transientToken = plainText;
                return null;

            case TokenProtectionMode.MachineBound:
                return DpapiPrefix + Convert.ToBase64String(
                    ProtectedDataShim.Protect(
                        Encoding.UTF8.GetBytes(plainText), DpapiEntropy));

            case TokenProtectionMode.Passphrase:
                if (_sessionKey is null || _sessionSalt is null)
                    throw new InvalidOperationException(
                        "Passphrase mode is locked. Call Unlock before storing a token.");

                var envelope = EncryptGcm(plainText, _sessionKey, _sessionSalt);
                var stored = PassphrasePrefix + Convert.ToBase64String(envelope);
                _pendingCipherText = stored;
                return stored;

            default:
                return null;
        }
    }

    public string? Unprotect(string? cipherText)
    {
        if (_mode == TokenProtectionMode.None)
            return _transientToken;

        if (string.IsNullOrEmpty(cipherText)) return null;

        try
        {
            switch (_mode)
            {
                case TokenProtectionMode.MachineBound:
                    if (!cipherText.StartsWith(DpapiPrefix, StringComparison.Ordinal))
                        return null;  // stored under a different mode
                    var blob = Convert.FromBase64String(StripPrefix(cipherText, DpapiPrefix));
                    return Encoding.UTF8.GetString(
                        ProtectedDataShim.Unprotect(blob, DpapiEntropy));

                case TokenProtectionMode.Passphrase:
                    if (_sessionKey is null) return null;   // locked
                    if (!cipherText.StartsWith(PassphrasePrefix, StringComparison.Ordinal))
                        return null;
                    var env = Convert.FromBase64String(StripPrefix(cipherText, PassphrasePrefix));
                    return DecryptGcm(env, _sessionKey);

                default:
                    return null;
            }
        }
        catch (CryptographicException)
        {
            // Wrong machine/profile for a DPAPI blob, or a corrupted envelope.
            // Null is correct here: the UI prompts for re-entry.
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static byte[] DeriveKey(string passphrase, byte[] salt) =>
        Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(passphrase),
            salt,
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            KeyLength);

    private static byte[] EncryptGcm(string plainText, byte[] key, byte[] salt)
    {
        var pt = Encoding.UTF8.GetBytes(plainText);
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ct = new byte[pt.Length];
        var tag = new byte[TagLength];

        using var gcm = new AesGcm(key, TagLength);
        gcm.Encrypt(nonce, pt, ct, tag);

        var envelope = new byte[SaltLength + NonceLength + TagLength + ct.Length];
        salt.CopyTo(envelope, 0);
        nonce.CopyTo(envelope, SaltLength);
        tag.CopyTo(envelope, SaltLength + NonceLength);
        ct.CopyTo(envelope, SaltLength + NonceLength + TagLength);
        return envelope;
    }

    private static string DecryptGcm(byte[] envelope, byte[] key)
    {
        var nonce = envelope.AsSpan(SaltLength, NonceLength).ToArray();
        var tag = envelope.AsSpan(SaltLength + NonceLength, TagLength).ToArray();
        var ct = envelope.AsSpan(SaltLength + NonceLength + TagLength).ToArray();
        var pt = new byte[ct.Length];

        using var gcm = new AesGcm(key, TagLength);
        gcm.Decrypt(nonce, ct, tag, pt);   // throws if the tag does not verify
        return Encoding.UTF8.GetString(pt);
    }

    private static string StripPrefix(string value, string prefix) =>
        value.StartsWith(prefix, StringComparison.Ordinal) ? value[prefix.Length..] : value;
}
