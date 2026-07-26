namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// How the GitHub PAT is protected at rest in settings.json.
/// The user chooses; there is no universally correct answer here.
/// </summary>
public enum TokenProtectionMode
{
    /// <summary>
    /// Windows DPAPI, CurrentUser scope. Strongest option and needs no
    /// passphrase, but the ciphertext is bound to this Windows user profile
    /// on this machine: copying settings.json elsewhere will not decrypt.
    /// Best when the app lives on one machine.
    /// </summary>
    MachineBound,

    /// <summary>
    /// AES-256-GCM with a key derived from a user passphrase (PBKDF2-SHA256).
    /// Fully portable -- the token travels with the USB stick -- at the cost
    /// of typing the passphrase once per session. Best for genuinely portable
    /// use across machines.
    /// </summary>
    Passphrase,

    /// <summary>
    /// The token is not persisted at all; it is re-entered each session and
    /// held in memory only. No ciphertext is written to settings.json.
    /// </summary>
    None
}

/// <summary>
/// Protects and unprotects the GitHub token according to the active
/// <see cref="TokenProtectionMode"/>.
///
/// Lifecycle note: settings load during DI bootstrap, before any window
/// exists to prompt with. So Passphrase mode cannot decrypt at load time.
/// Instead the ciphertext is read but left sealed, and <see cref="IsUnlocked"/>
/// stays false until the UI calls <see cref="Unlock"/>. Callers must handle
/// a null return from the settings service's token getter.
/// </summary>
public interface ITokenProtector
{
    TokenProtectionMode Mode { get; }

    /// <summary>
    /// True when a token can currently be read. Always true for MachineBound
    /// (DPAPI needs no user input) and for None (nothing to unlock). False in
    /// Passphrase mode until Unlock succeeds.
    /// </summary>
    bool IsUnlocked { get; }

    /// <summary>True when Passphrase mode has ciphertext waiting to be unlocked.</summary>
    bool RequiresPassphrase { get; }

    /// <summary>
    /// Supplies the passphrase for Passphrase mode. Returns false when the
    /// passphrase is wrong (GCM authentication fails), leaving state unchanged.
    /// No-op returning true in other modes.
    /// </summary>
    bool Unlock(string passphrase);

    /// <summary>Clears any cached key/plaintext from memory.</summary>
    void Lock();

    /// <summary>
    /// Encrypts a token for storage. Returns null when the token is null/empty
    /// or when Mode is None (nothing is persisted).
    /// Throws InvalidOperationException in Passphrase mode if not unlocked.
    /// </summary>
    string? Protect(string? plainText);

    /// <summary>
    /// Decrypts stored ciphertext, or null when absent, locked, or
    /// undecryptable (e.g. a DPAPI blob moved to another machine).
    /// </summary>
    string? Unprotect(string? cipherText);

    /// <summary>
    /// Switches mode. Any existing token must be re-supplied afterwards --
    /// ciphertext is not transcoded between modes, since doing so would
    /// require the old mode to be unlocked and the new one configured
    /// simultaneously.
    /// </summary>
    void SetMode(TokenProtectionMode mode);
}
