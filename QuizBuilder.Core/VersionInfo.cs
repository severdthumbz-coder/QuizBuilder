using System.Reflection;

namespace QuizBuilder.Core;

/// <summary>
/// Reads the version stamped into the assembly at build time.
///
/// build.bat passes -p:Version / -p:AssemblyVersion / -p:InformationalVersion
/// from version.json, so these values track the central source rather than
/// being duplicated in code. The Help/About tab and the window title both
/// read from here, so they cannot drift apart.
///
/// Note: this reads the *entry* assembly (the .exe), not Core's own assembly.
/// They carry the same version because Directory.Build.props applies to every
/// project, but the entry assembly is the one the user thinks of as "the app".
/// </summary>
public static class VersionInfo
{
    private static readonly Lazy<Assembly> EntryAssembly = new(() =>
        Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly);

    /// <summary>
    /// Semantic version with any build metadata, e.g. "0.1.0+build.1".
    /// Falls back through progressively less specific attributes.
    /// </summary>
    public static string Informational =>
        EntryAssembly.Value.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion
        ?? FileVersion;

    /// <summary>Four-part file version, e.g. "0.1.0.1".</summary>
    public static string FileVersion =>
        EntryAssembly.Value.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version
        ?? EntryAssembly.Value.GetName().Version?.ToString()
        ?? "0.0.0.0";

    /// <summary>
    /// Three-part version without build metadata, e.g. "0.1.0".
    /// </summary>
    public static string Semantic
    {
        get
        {
            var info = Informational;

            // InformationalVersion may carry "+build.1" metadata or a
            // "-preview" prerelease tag. Strip both for the short form.
            var plus = info.IndexOf('+');
            if (plus >= 0) info = info[..plus];

            var dash = info.IndexOf('-');
            if (dash >= 0) info = info[..dash];

            return info;
        }
    }

    /// <summary>
    /// Sequential build number: the fourth part of the file version.
    /// Returns 0 when unavailable rather than throwing, since a missing
    /// build number should never prevent the app starting.
    /// </summary>
    public static int Build
    {
        get
        {
            var parts = FileVersion.Split('.');
            return parts.Length >= 4 && int.TryParse(parts[3], out var b) ? b : 0;
        }
    }

    /// <summary>
    /// Display form used in the title bar and Help/About: "v0.1.0 (build 1)".
    /// </summary>
    public static string Display => $"v{Semantic} (build {Build})";

    /// <summary>
    /// Filename-safe form used for build artifacts: "v0.1.0-build.1".
    /// Contains no characters that are illegal in a Windows filename.
    /// </summary>
    public static string FileNameSuffix => $"v{Semantic}-build.{Build}";
}
