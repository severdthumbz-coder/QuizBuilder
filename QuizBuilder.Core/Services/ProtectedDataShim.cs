using System.Security.Cryptography;

namespace QuizBuilder.Core.Services;

/// <summary>
/// Thin seam over Windows DPAPI.
///
/// Why this exists: System.Security.Cryptography.ProtectedData is Windows-only.
/// Calling it directly from TokenProtector would make the whole Core assembly
/// untestable anywhere else (including on a Linux CI runner) and would surface
/// a raw PlatformNotSupportedException instead of a message the UI can show.
/// Routing through a swappable delegate keeps TokenProtector testable and lets
/// the non-Windows path fail cleanly.
/// </summary>
internal static class ProtectedDataShim
{
    /// <summary>
    /// Overridable for tests. Production leaves these pointing at DPAPI.
    /// </summary>
    internal static Func<byte[], byte[], byte[]> ProtectImpl { get; set; } = WindowsProtect;
    internal static Func<byte[], byte[], byte[]> UnprotectImpl { get; set; } = WindowsUnprotect;

    public static byte[] Protect(byte[] data, byte[] entropy) => ProtectImpl(data, entropy);

    public static byte[] Unprotect(byte[] data, byte[] entropy) => UnprotectImpl(data, entropy);

    // Platform note (CA1416):
    //
    // These methods are assigned to the delegates above, which are callable
    // from any platform; that is the point of the shim. So they must NOT carry
    // [SupportedOSPlatform("windows")]: annotating them while exposing them
    // through a platform-neutral delegate is a contradiction, and the analyzer
    // rightly flagged it.
    //
    // Instead the DPAPI call sits lexically inside an `if (IsWindows())` branch.
    // This positive-guard form is what the platform-compatibility analyzer
    // recognises: it narrows the platform within the branch, so the
    // ProtectedData call is provably reachable only on Windows and CA1416 is
    // satisfied without a suppression.
    //
    // The earlier `if (!IsWindows()) throw;` shape expressed the same intent but
    // relied on the analyzer following a negative guard across a subsequent
    // statement, which it does not do dependably.

    private static byte[] WindowsProtect(byte[] data, byte[] entropy)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(data, entropy, DataProtectionScope.CurrentUser);

        throw new CryptographicException(
            "Machine-bound protection requires Windows. Choose passphrase mode instead.");
    }

    private static byte[] WindowsUnprotect(byte[] data, byte[] entropy)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(data, entropy, DataProtectionScope.CurrentUser);

        throw new CryptographicException(
            "Machine-bound protection requires Windows. Choose passphrase mode instead.");
    }
}
