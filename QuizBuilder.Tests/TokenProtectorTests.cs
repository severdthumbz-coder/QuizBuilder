using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// Exercises the passphrase state machine. These cases were validated against
/// a reference model before the implementation existed; they are pinned here
/// because the failure modes are quiet ones -- a wrong passphrase that half-
/// unlocks, or a nonce reused under a fixed GCM key, produces no error at all.
///
/// MachineBound is covered by swapping ProtectedDataShim's delegates, so these
/// run on any OS. That is the whole reason the shim exists.
/// </summary>
public class TokenProtectorTests : IDisposable
{
    private const string Token = "ghp_xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx";

    private readonly Func<byte[], byte[], byte[]> _originalProtect = ProtectedDataShim.ProtectImpl;
    private readonly Func<byte[], byte[], byte[]> _originalUnprotect = ProtectedDataShim.UnprotectImpl;

    public TokenProtectorTests()
    {
        // Stand in for DPAPI with a reversible transform. Not encryption --
        // it only has to round-trip so the surrounding logic can be tested
        // without a Windows user profile.
        ProtectedDataShim.ProtectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        ProtectedDataShim.UnprotectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    public void Dispose()
    {
        ProtectedDataShim.ProtectImpl = _originalProtect;
        ProtectedDataShim.UnprotectImpl = _originalUnprotect;
    }

    // ---------- Passphrase mode ----------

    [Fact]
    public void Passphrase_RoundTripsToken()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);

        Assert.True(p.Unlock("correct horse battery staple"));

        var cipher = p.Protect(Token);
        Assert.NotNull(cipher);
        Assert.Equal(Token, p.Unprotect(cipher));
    }

    [Fact]
    public void Passphrase_WrongPassphrase_IsRejectedAndLeavesStateClean()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);
        p.Unlock("right");
        var cipher = p.Protect(Token);

        // Simulate a restart: new protector, ciphertext from disk.
        var fresh = new TokenProtector();
        fresh.SetMode(TokenProtectionMode.Passphrase);
        fresh.SetPendingCipherText(cipher);

        Assert.True(fresh.RequiresPassphrase);
        Assert.False(fresh.Unlock("wrong"));

        // The critical part: a failed attempt must not half-unlock.
        Assert.False(fresh.IsUnlocked);
        Assert.Null(fresh.Unprotect(cipher));

        Assert.True(fresh.Unlock("right"));
        Assert.Equal(Token, fresh.Unprotect(cipher));
    }

    [Fact]
    public void Passphrase_LockedRead_ReturnsNullRatherThanThrowing()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);
        p.Unlock("pw");
        var cipher = p.Protect(Token);

        p.Lock();

        // Null, not an exception: the UI prompts rather than crashing.
        Assert.Null(p.Unprotect(cipher));
        Assert.False(p.IsUnlocked);
    }

    [Fact]
    public void Passphrase_ProtectWhileLocked_Throws()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);
        p.SetPendingCipherText("pbkdf2$AAAA");

        Assert.Throws<InvalidOperationException>(() => p.Protect(Token));
    }

    [Fact]
    public void Passphrase_ReProtect_KeepsSaltButRotatesNonce()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);
        p.Unlock("pw");

        var first = p.Protect("token-one")!;
        var second = p.Protect("token-two")!;

        var a = Convert.FromBase64String(first["pbkdf2$".Length..]);
        var b = Convert.FromBase64String(second["pbkdf2$".Length..]);

        // Salt (first 16 bytes) stable: same passphrase, same derived key.
        Assert.Equal(a.Take(16), b.Take(16));

        // Nonce (next 12) MUST differ. Reusing a nonce under a fixed GCM key
        // is a catastrophic break, and it fails silently -- hence this test.
        Assert.NotEqual(a.Skip(16).Take(12), b.Skip(16).Take(12));

        Assert.Equal("token-two", p.Unprotect(second));
    }

    [Fact]
    public void Passphrase_UnlockWithNoStoredToken_Succeeds()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);

        // Nothing stored yet: any passphrase is accepted and seeds a new salt.
        Assert.False(p.RequiresPassphrase);
        Assert.True(p.Unlock("anything"));
        Assert.True(p.IsUnlocked);
    }

    [Fact]
    public void Passphrase_EmptyPassphrase_IsRejected()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);

        Assert.False(p.Unlock(""));
    }

    // ---------- MachineBound mode ----------

    [Fact]
    public void MachineBound_RoundTripsToken()
    {
        var p = new TokenProtector();   // MachineBound is the default

        Assert.Equal(TokenProtectionMode.MachineBound, p.Mode);
        Assert.True(p.IsUnlocked);      // no passphrase needed

        var cipher = p.Protect(Token);
        Assert.NotNull(cipher);
        Assert.StartsWith("dpapi$", cipher);
        Assert.Equal(Token, p.Unprotect(cipher));
    }

    [Fact]
    public void MachineBound_RejectsCipherFromAnotherMode()
    {
        var p = new TokenProtector();

        // A pbkdf2 envelope must not be fed to the DPAPI path.
        Assert.Null(p.Unprotect("pbkdf2$AAAABBBB"));
    }

    [Fact]
    public void MachineBound_UndecryptableBlob_ReturnsNullNotThrow()
    {
        ProtectedDataShim.UnprotectImpl = (_, _) =>
            throw new System.Security.Cryptography.CryptographicException("wrong machine");

        var p = new TokenProtector();

        // This is the "settings.json carried to another PC" case. It must
        // degrade to a re-entry prompt, not an unhandled exception.
        Assert.Null(p.Unprotect("dpapi$AAAA"));
    }

    // ---------- None mode ----------

    [Fact]
    public void None_KeepsTokenInMemoryAndPersistsNothing()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.None);

        // Protect returns null: nothing is written to settings.json.
        Assert.Null(p.Protect(Token));

        // But the token is readable for the rest of the session.
        Assert.Equal(Token, p.Unprotect(null));
        Assert.True(p.IsUnlocked);
    }

    // ---------- Mode switching ----------

    [Fact]
    public void SwitchingMode_ClearsSessionState()
    {
        var p = new TokenProtector();
        p.SetMode(TokenProtectionMode.Passphrase);
        p.Unlock("pw");
        p.Protect(Token);

        p.SetMode(TokenProtectionMode.None);

        // The passphrase session must not survive the switch.
        Assert.Equal(TokenProtectionMode.None, p.Mode);
        Assert.Null(p.Unprotect(null));   // transient cleared too
    }

    [Fact]
    public void Protect_NullOrEmpty_ReturnsNull()
    {
        var p = new TokenProtector();

        Assert.Null(p.Protect(null));
        Assert.Null(p.Protect(""));
    }
}
