using System.Security.Cryptography;
using System.Text;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Protects the AI grammar-review API key at rest, independently of the GitHub
/// token machinery. Deliberately minimal (option C from the design): DPAPI,
/// CurrentUser scope, machine-bound — no passphrase mode, no unlock lifecycle.
///
/// <para>
/// Rationale: an API key does not need the portable-passphrase story the GitHub
/// token has. Machine-bound is the right default for a secret you can always
/// re-enter, and it sidesteps the passphrase-prompt-at-load complexity entirely.
/// Keeping this separate means the security-sensitive, already-tested
/// <see cref="TokenProtector"/> is left untouched.
/// </para>
///
/// <para>
/// The ciphertext is stored (base64, "dpapi$"-prefixed) under a namespaced key
/// in <see cref="Interfaces.AppSettings.Extra"/>. If the settings file is copied
/// to another machine/profile the blob will not decrypt — <see cref="Unprotect"/>
/// returns null and the user re-enters the key, which is the correct, safe
/// failure for a machine-bound secret.
/// </para>
/// </summary>
public sealed class AiKeyProtector
{
    // Distinct entropy from the GitHub token's, so a blob from one context can
    // never be unsealed in the other even on the same machine/profile.
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("QuizBuilder.AiReviewKey.v1");

    private const string Scheme = "dpapi$";

    /// <summary>
    /// Encrypts <paramref name="plainKey"/> to a "dpapi$base64" string, or null
    /// for null/blank input (which means "no key stored").
    /// </summary>
    public string? Protect(string? plainKey)
    {
        if (string.IsNullOrWhiteSpace(plainKey))
            return null;

        var bytes = Encoding.UTF8.GetBytes(plainKey);
        var blob = ProtectedDataShim.Protect(bytes, Entropy);
        return Scheme + Convert.ToBase64String(blob);
    }

    /// <summary>
    /// Decrypts a value produced by <see cref="Protect"/>. Returns null for
    /// null/blank input, a value written under a different scheme, or a blob
    /// that will not decrypt on this machine/profile (copied settings file).
    /// Never throws for these expected cases — the caller treats null as
    /// "no usable key, ask the user to enter it".
    /// </summary>
    public string? Unprotect(string? cipher)
    {
        if (string.IsNullOrWhiteSpace(cipher) || !cipher.StartsWith(Scheme, StringComparison.Ordinal))
            return null;

        var base64 = cipher[Scheme.Length..];

        try
        {
            var blob = Convert.FromBase64String(base64);
            var bytes = ProtectedDataShim.Unprotect(blob, Entropy);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (FormatException)
        {
            return null; // not valid base64 — corrupted or hand-edited
        }
        catch (CryptographicException)
        {
            return null; // wrong machine/profile, or a corrupted DPAPI envelope
        }
    }
}
