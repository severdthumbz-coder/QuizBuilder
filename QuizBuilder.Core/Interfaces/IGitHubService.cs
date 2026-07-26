using System.Text.RegularExpressions;

namespace QuizBuilder.Core.Interfaces;

/// <summary>
/// An owner/name pair, parsed from whatever the user pasted.
/// </summary>
public sealed partial class RepositoryReference
{
    private RepositoryReference(string owner, string name)
    {
        Owner = owner;
        Name = name;
    }

    public string Owner { get; }
    public string Name { get; }

    public string FullName => $"{Owner}/{Name}";
    public string HtmlUrl => $"https://github.com/{Owner}/{Name}";

    /// <summary>
    /// Parses the shapes a person actually pastes: the address bar, the clone
    /// box (https or ssh), or just "owner/name" typed by hand.
    /// </summary>
    /// <param name="error">
    /// Why it failed, phrased for the user. A repo URL that is nearly right --
    /// a tree URL copied while browsing, say -- looks perfectly good to them, so
    /// "that is not a repository URL" alone would be useless.
    /// </param>
    public static RepositoryReference? TryParse(string? text, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Enter a repository, like owner/name.";
            return null;
        }

        var trimmed = text.Trim().TrimEnd('/');

        if (trimmed.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            trimmed = trimmed[..^4];

        var ssh = SshRegex().Match(trimmed);
        if (ssh.Success) return new RepositoryReference(ssh.Groups[1].Value, ssh.Groups[2].Value);

        var https = HttpsRegex().Match(trimmed);
        if (https.Success) return new RepositoryReference(https.Groups[1].Value, https.Groups[2].Value);

        var bare = BareRegex().Match(trimmed);
        if (bare.Success) return new RepositoryReference(bare.Groups[1].Value, bare.Groups[2].Value);

        // Everything below here is a targeted message. The generic fallback is
        // last, because a specific reason is worth several sentences of hedging.
        if (trimmed.Contains("github.com/", StringComparison.OrdinalIgnoreCase)
            && trimmed.Count(c => c == '/') > 3)
        {
            error = "That looks like a link to a page inside the repository. "
                    + "Use just the repository, like owner/name.";
            return null;
        }

        if (OwnerOnlyRegex().IsMatch(trimmed))
        {
            error = "Include the repository name as well, like owner/name.";
            return null;
        }

        if (IsOtherHost(trimmed))
        {
            error = "Only github.com repositories are supported.";
            return null;
        }

        if (!trimmed.Contains('/'))
        {
            error = "Include the owner as well, like owner/name.";
            return null;
        }

        error = "That does not look like a GitHub repository. Use owner/name, "
                + "or the URL from the repository's page.";
        return null;
    }

    [GeneratedRegex(@"^git@github\.com:([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex SshRegex();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex HttpsRegex();

    [GeneratedRegex(@"^([A-Za-z0-9._-]+)/([A-Za-z0-9._-]+)$")]
    private static partial Regex BareRegex();

    /// <summary>
    /// True for a URL on some host other than github.com.
    ///
    /// The host is captured and compared rather than excluded with a negative
    /// lookahead. A lookahead placed after an optional "www." backtracks: for
    /// "www.github.com/o/n" the engine tries with the www. consumed, fails the
    /// lookahead, then retries WITHOUT consuming it, and now the lookahead sees
    /// "www.github.com" -- which is not "github.com", so it passes, and a
    /// perfectly good GitHub URL gets rejected as another host. Comparing a
    /// captured group cannot backtrack into a wrong answer.
    /// </summary>
    private static bool IsOtherHost(string text)
    {
        var match = HostRegex().Match(text);

        return match.Success
               && !string.Equals(match.Groups[1].Value, "github.com", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?([A-Za-z0-9.-]+\.[A-Za-z]{2,})/", RegexOptions.IgnoreCase)]
    private static partial Regex HostRegex();

    [GeneratedRegex(@"^(?:https?://)?(?:www\.)?github\.com/([A-Za-z0-9._-]+)$", RegexOptions.IgnoreCase)]
    private static partial Regex OwnerOnlyRegex();
}

/// <summary>
/// The outcome of a GitHub call, phrased for a person rather than a log.
/// </summary>
public sealed class GitHubResult
{
    private GitHubResult(bool success, string? message, string? url)
    {
        Success = success;
        Message = message;
        Url = url;
    }

    public bool Success { get; }

    /// <summary>What happened, in plain words. Always set.</summary>
    public string? Message { get; }

    /// <summary>A link worth opening, when there is one.</summary>
    public string? Url { get; }

    public static GitHubResult Ok(string message, string? url = null) => new(true, message, url);
    public static GitHubResult Failed(string message) => new(false, message, null);
}

/// <summary>
/// Talks to GitHub over its REST API.
///
/// Not a git client. The spec said LibGit2Sharp, which was the wrong tool twice
/// over: it ships native binaries per-architecture that fight single-file
/// publish, and it could not be restored or verified in the environment this was
/// written in. What this tab actually needs is "put one file in a repo and give
/// me a link", which is three REST calls over HttpClient -- already in the BCL,
/// and verifiable by inspecting the requests.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Checks a token and returns the account it belongs to.
    /// </summary>
    Task<GitHubResult> VerifyTokenAsync(string token, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a single file, producing one commit.
    ///
    /// Reads the file's current sha first: the Contents API rejects an update
    /// that does not carry it, and carrying a stale one is how you overwrite
    /// somebody's work.
    /// </summary>
    Task<GitHubResult> PublishFileAsync(
        string token,
        RepositoryReference repository,
        string branch,
        string path,
        string content,
        string commitMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns on GitHub Pages for the repository, or reports where it already is.
    /// </summary>
    Task<GitHubResult> EnablePagesAsync(
        string token,
        RepositoryReference repository,
        string branch,
        CancellationToken cancellationToken = default);
}
