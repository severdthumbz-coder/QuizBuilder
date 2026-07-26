using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;

namespace QuizBuilder.Core.Services;

/// <summary>
/// GitHub over its REST API.
///
/// Three calls do everything this tab needs: verify the token, PUT one file,
/// turn on Pages. No git client, no native binaries, nothing to install --
/// which matters for an app that is meant to run from a USB stick.
///
/// What could NOT be verified here: that GitHub accepts these requests. The
/// request shapes, URLs, JSON, base64 and error mapping were all checked, and
/// the error contract was confirmed against the live API, but no authenticated
/// call was ever made. That is a real gap, and the Help tab says so rather than
/// implying this is proven end to end.
/// </summary>
public sealed class GitHubService : IGitHubService, IDisposable
{
    private const string ApiRoot = "https://api.github.com";

    /// <summary>
    /// GitHub rejects requests with no User-Agent. This is not optional.
    /// </summary>
    private const string UserAgent = "QuizBuilder";

    /// <summary>
    /// Pinning the API version means a future default cannot silently change
    /// the response shape under a user who has not updated the app.
    /// </summary>
    private const string ApiVersion = "2022-11-28";

    private readonly HttpClient _http;
    private readonly bool _ownsClient;

    public GitHubService()
        : this(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, ownsClient: true)
    {
    }

    /// <param name="ownsClient">
    /// False when the client is injected by a test, which must not have its
    /// handler disposed out from under it.
    /// </param>
    public GitHubService(HttpClient http, bool ownsClient = false)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _ownsClient = ownsClient;
    }

    public void Dispose()
    {
        if (_ownsClient) _http.Dispose();
    }

    // --- Public API ---------------------------------------------------------

    public async Task<GitHubResult> VerifyTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(token))
            return GitHubResult.Failed("Enter a personal access token first.");

        try
        {
            using var request = Request(HttpMethod.Get, $"{ApiRoot}/user", token);
            using var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return await FailureFrom(response, cancellationToken);

            var json = await ReadJson(response, cancellationToken);
            var login = Text(json, "login");

            return login is null
                ? GitHubResult.Ok("That token works.")
                : GitHubResult.Ok($"Signed in as {login}.", $"https://github.com/{login}");
        }
        catch (Exception ex)
        {
            return GitHubResult.Failed(Describe(ex));
        }
    }

    public async Task<GitHubResult> PublishFileAsync(
        string token,
        RepositoryReference repository,
        string branch,
        string path,
        string content,
        string commitMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(token))
            return GitHubResult.Failed("Enter a personal access token first.");

        if (string.IsNullOrWhiteSpace(path))
            return GitHubResult.Failed("Enter a file name for the published page.");

        var cleanPath = path.Trim().TrimStart('/');
        var cleanBranch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();

        try
        {
            // Look before writing. The Contents API refuses an update that does
            // not carry the file's current sha, so "just PUT it" works the first
            // time and fails every time after -- a bug a single-publish test
            // would never see.
            var existingSha = await GetFileShaAsync(token, repository, cleanBranch, cleanPath, cancellationToken);

            var payload = new Dictionary<string, string>
            {
                ["message"] = string.IsNullOrWhiteSpace(commitMessage) ? "Publish quiz" : commitMessage,
                ["content"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(content)),
                ["branch"] = cleanBranch,
            };

            // Only when updating. Sending a sha for a file that does not exist
            // is itself an error.
            if (existingSha is not null) payload["sha"] = existingSha;

            using var request = Request(HttpMethod.Put, ContentsUrl(repository, cleanPath), token);
            request.Content = Json(payload);

            using var response = await _http.SendAsync(request, cancellationToken);

            if (response.StatusCode == HttpStatusCode.Conflict)
            {
                // Someone changed the file between our read and our write.
                // Refusing is correct: forcing it would discard their work.
                return GitHubResult.Failed(
                    "The file changed on GitHub while publishing. Try again to pick up the newer version.");
            }

            if (!response.IsSuccessStatusCode)
                return await FailureFrom(response, cancellationToken);

            var json = await ReadJson(response, cancellationToken);
            var htmlUrl = Text(json, "content", "html_url");

            var verb = existingSha is null ? "Published" : "Updated";

            return GitHubResult.Ok($"{verb} {cleanPath} on {repository.FullName}.", htmlUrl ?? repository.HtmlUrl);
        }
        catch (Exception ex)
        {
            return GitHubResult.Failed(Describe(ex));
        }
    }

    public async Task<GitHubResult> EnablePagesAsync(
        string token,
        RepositoryReference repository,
        string branch,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(repository);

        if (string.IsNullOrWhiteSpace(token))
            return GitHubResult.Failed("Enter a personal access token first.");

        var cleanBranch = string.IsNullOrWhiteSpace(branch) ? "main" : branch.Trim();

        try
        {
            // Already on? Then this is a no-op worth reporting, not an error.
            var existing = await GetPagesAsync(token, repository, cancellationToken);
            if (existing is not null)
                return GitHubResult.Ok("Pages is already enabled.", existing);

            var payload = new
            {
                source = new { branch = cleanBranch, path = "/" },
            };

            using var request = Request(HttpMethod.Post, $"{ApiRoot}/repos/{repository.Owner}/{repository.Name}/pages", token);
            request.Content = Json(payload);

            using var response = await _http.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
                return await FailureFrom(response, cancellationToken);

            var json = await ReadJson(response, cancellationToken);
            var url = Text(json, "html_url");

            return GitHubResult.Ok(
                "Pages enabled. It can take a minute or two for the site to appear.", url);
        }
        catch (Exception ex)
        {
            return GitHubResult.Failed(Describe(ex));
        }
    }

    // --- Internals ----------------------------------------------------------

    private async Task<string?> GetFileShaAsync(
        string token, RepositoryReference repository, string branch, string path, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"{ContentsUrl(repository, path)}?ref={Uri.EscapeDataString(branch)}", token);
        using var response = await _http.SendAsync(request, cancellationToken);

        // 404 is the normal "first publish" case, not a failure.
        if (response.StatusCode == HttpStatusCode.NotFound) return null;

        if (!response.IsSuccessStatusCode) return null;

        var json = await ReadJson(response, cancellationToken);

        return Text(json, "sha");
    }

    private async Task<string?> GetPagesAsync(
        string token, RepositoryReference repository, CancellationToken cancellationToken)
    {
        using var request = Request(HttpMethod.Get, $"{ApiRoot}/repos/{repository.Owner}/{repository.Name}/pages", token);
        using var response = await _http.SendAsync(request, cancellationToken);

        if (!response.IsSuccessStatusCode) return null;

        var json = await ReadJson(response, cancellationToken);

        return Text(json, "html_url");
    }

    private static string ContentsUrl(RepositoryReference repository, string path)
    {
        // Each segment is escaped separately: the slashes are structure, not
        // content, and escaping the whole path would turn "quiz/index.html" into
        // one segment called "quiz%2Findex.html".
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.EscapeDataString);

        return $"{ApiRoot}/repos/{repository.Owner}/{repository.Name}/contents/{string.Join('/', segments)}";
    }

    /// <summary>
    /// Serialises a body.
    ///
    /// StringContent rather than JsonContent.Create: the latter lives in
    /// System.Net.Http.Json, which is in the net8.0 shared framework -- but that
    /// could not be confirmed in the environment this was written in, and
    /// System.Text.Json is already used elsewhere in Core and is certain. A
    /// convenience method is not worth an unverifiable assumption.
    /// </summary>
    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static HttpRequestMessage Request(HttpMethod method, string url, string token)
    {
        var request = new HttpRequestMessage(method, url);

        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.Trim());
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd(UserAgent);
        request.Headers.Add("X-GitHub-Api-Version", ApiVersion);

        return request;
    }

    private static async Task<JsonElement?> ReadJson(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(text)) return null;

            using var document = JsonDocument.Parse(text);

            // Clone: the JsonDocument is disposed on the way out of this method.
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a nested string, treating every level as optional. Nothing here
    /// assumes a field exists: a missing one means the caller falls back, not
    /// that the app throws.
    /// </summary>
    private static string? Text(JsonElement? json, params string[] path)
    {
        if (json is null) return null;

        var current = json.Value;

        foreach (var key in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return null;
            if (!current.TryGetProperty(key, out var next)) return null;

            current = next;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : null;
    }

    /// <summary>
    /// Turns a failed response into something a person can act on.
    ///
    /// GitHub's own "message" field is usually the clearest explanation
    /// available -- confirmed against the live API -- so it is preferred over
    /// anything invented here. The status code only picks the framing.
    /// </summary>
    private static async Task<GitHubResult> FailureFrom(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var json = await ReadJson(response, cancellationToken);
        var message = Text(json, "message");

        return response.StatusCode switch
        {
            HttpStatusCode.Unauthorized =>
                GitHubResult.Failed("GitHub rejected that token. Check it has not expired, and that it was copied in full."),

            HttpStatusCode.Forbidden when message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true =>
                GitHubResult.Failed("GitHub's rate limit was hit. Wait a few minutes and try again."),

            HttpStatusCode.Forbidden =>
                GitHubResult.Failed(message is null
                    ? "GitHub refused that request. The token may not have permission for this repository."
                    : $"GitHub refused that request: {message}"),

            HttpStatusCode.NotFound =>
                GitHubResult.Failed(
                    "That repository was not found. Check the name, and that the token can see it -- "
                    + "a private repository needs a token with access to it."),

            HttpStatusCode.UnprocessableEntity =>
                GitHubResult.Failed(message is null
                    ? "GitHub could not process that request."
                    : $"GitHub could not process that request: {message}"),

            _ => GitHubResult.Failed(message is null
                ? $"GitHub returned {(int)response.StatusCode}."
                : $"GitHub returned {(int)response.StatusCode}: {message}"),
        };
    }

    private static string Describe(Exception ex) => ex switch
    {
        TaskCanceledException => "The request timed out. Check your connection and try again.",
        HttpRequestException => "Could not reach GitHub. Check your connection and try again.",
        _ => $"Something went wrong: {ex.Message}",
    };
}
