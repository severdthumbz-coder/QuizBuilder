using System.Text;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

/// <summary>
/// AiKeyProtector: the isolated DPAPI protector for the AI review key. Like
/// TokenProtectorTests, MachineBound DPAPI is covered by swapping
/// ProtectedDataShim's global delegates for an in-memory transform, so this runs
/// on Linux CI. MUST join the shim collection — those delegates are process-
/// global statics and running concurrently with another shim-mutating class is
/// the documented intermittent-CI race.
/// </summary>
[Collection(ProtectedDataShimCollection.Name)]
public class AiKeyProtectorTests : IDisposable
{
    private readonly Func<byte[], byte[], byte[]> _origProtect = ProtectedDataShim.ProtectImpl;
    private readonly Func<byte[], byte[], byte[]> _origUnprotect = ProtectedDataShim.UnprotectImpl;

    public AiKeyProtectorTests()
    {
        // Reversible in-memory stand-in for DPAPI (XOR), so round-trips work off
        // Windows. Entropy is ignored by the fake — we're testing the protector's
        // envelope/base64/scheme handling, not DPAPI itself.
        ProtectedDataShim.ProtectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        ProtectedDataShim.UnprotectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    public void Dispose()
    {
        ProtectedDataShim.ProtectImpl = _origProtect;
        ProtectedDataShim.UnprotectImpl = _origUnprotect;
    }

    [Fact]
    public void RoundTripsAKey()
    {
        var p = new AiKeyProtector();
        var cipher = p.Protect("sk-secret-key-123");
        Assert.NotNull(cipher);
        Assert.StartsWith("dpapi$", cipher);
        Assert.DoesNotContain("sk-secret-key-123", cipher); // not stored in the clear
        Assert.Equal("sk-secret-key-123", p.Unprotect(cipher));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ProtectBlankReturnsNull(string? input) =>
        Assert.Null(new AiKeyProtector().Protect(input));

    [Fact]
    public void UnprotectNullOrBlankReturnsNull()
    {
        var p = new AiKeyProtector();
        Assert.Null(p.Unprotect(null));
        Assert.Null(p.Unprotect(""));
        Assert.Null(p.Unprotect("   "));
    }

    [Fact]
    public void UnprotectWrongSchemeReturnsNull()
    {
        // A value without the dpapi$ prefix (e.g. a GitHub pbkdf2$ blob) must not
        // be mistaken for an AI key blob.
        Assert.Null(new AiKeyProtector().Unprotect("pbkdf2$abc123"));
        Assert.Null(new AiKeyProtector().Unprotect("plaintext"));
    }

    [Fact]
    public void UnprotectCorruptedBase64ReturnsNull() =>
        Assert.Null(new AiKeyProtector().Unprotect("dpapi$not valid base64!!"));

    [Fact]
    public void UnicodeKeyRoundTrips()
    {
        var p = new AiKeyProtector();
        var key = "clé-secrète-🔑-123";
        Assert.Equal(key, p.Unprotect(p.Protect(key)));
    }
}

/// <summary>
/// The settings service's AI-key methods, exercised through the same shim fake.
/// Confirms Set/Get/Has and that the key lives in Extra (protected), not in a
/// plaintext field, and that clearing removes it.
/// </summary>
[Collection(ProtectedDataShimCollection.Name)]
public class SettingsServiceAiKeyTests : IDisposable
{
    private readonly Func<byte[], byte[], byte[]> _origProtect = ProtectedDataShim.ProtectImpl;
    private readonly Func<byte[], byte[], byte[]> _origUnprotect = ProtectedDataShim.UnprotectImpl;
    private readonly string _tempDir;

    public SettingsServiceAiKeyTests()
    {
        ProtectedDataShim.ProtectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        ProtectedDataShim.UnprotectImpl = (data, _) => data.Select(b => (byte)(b ^ 0x5A)).ToArray();
        _tempDir = Path.Combine(Path.GetTempPath(), "qb-ai-key-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        ProtectedDataShim.ProtectImpl = _origProtect;
        ProtectedDataShim.UnprotectImpl = _origUnprotect;
        try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
    }

    private SettingsService NewService() =>
        new(new TokenProtector(), overrideDirectory: _tempDir);

    [Fact]
    public void SetGetRoundTripsAndReportsHas()
    {
        var s = NewService();
        Assert.False(s.HasAiReviewKey);
        Assert.Null(s.GetAiReviewKey());

        s.SetAiReviewKey("sk-ant-xyz");
        Assert.True(s.HasAiReviewKey);
        Assert.Equal("sk-ant-xyz", s.GetAiReviewKey());
    }

    [Fact]
    public void KeyIsStoredProtectedInExtraNotPlaintext()
    {
        var s = NewService();
        s.SetAiReviewKey("sk-ant-secret");

        var stored = s.Current.Extra["ai.reviewKey"];
        Assert.StartsWith("dpapi$", stored);
        Assert.DoesNotContain("sk-ant-secret", stored);
    }

    [Fact]
    public void SettingNullClearsTheKey()
    {
        var s = NewService();
        s.SetAiReviewKey("sk-ant-xyz");
        Assert.True(s.HasAiReviewKey);

        s.SetAiReviewKey(null);
        Assert.False(s.HasAiReviewKey);
        Assert.Null(s.GetAiReviewKey());
        Assert.False(s.Current.Extra.ContainsKey("ai.reviewKey"));
    }

    [Fact]
    public void KeyPersistsAcrossSaveAndReload()
    {
        var s1 = NewService();
        s1.SetAiReviewKey("sk-ant-persist");
        s1.Save();

        var s2 = NewService();
        s2.Load();
        Assert.True(s2.HasAiReviewKey);
        Assert.Equal("sk-ant-persist", s2.GetAiReviewKey());
    }

    [Fact]
    public void AiReviewSettingsDefaultToOff()
    {
        var s = NewService();
        Assert.Equal(AiProvider.Off, s.Current.AiReview.Provider);
    }
}
