using System.Net;
using System.Text;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;
using Xunit;

namespace QuizBuilder.Tests;

public class RepositoryReferenceTests
{
    [Theory]
    [InlineData("https://github.com/octocat/Hello-World")]
    [InlineData("https://github.com/octocat/Hello-World.git")]
    [InlineData("https://github.com/octocat/Hello-World/")]
    [InlineData("HTTPS://GitHub.com/octocat/Hello-World")]
    [InlineData("http://github.com/octocat/Hello-World")]
    [InlineData("www.github.com/octocat/Hello-World")]
    [InlineData("https://www.github.com/octocat/Hello-World")]
    [InlineData("github.com/octocat/Hello-World")]
    [InlineData("git@github.com:octocat/Hello-World.git")]
    [InlineData("octocat/Hello-World")]
    [InlineData("  octocat/Hello-World  ")]
    public void ParsesEveryShapeSomeoneMightPaste(string text)
    {
        var repository = RepositoryReference.TryParse(text, out var error);

        Assert.NotNull(repository);
        Assert.Null(error);
        Assert.Equal("octocat", repository!.Owner);
        Assert.Equal("Hello-World", repository.Name);
    }

    [Fact]
    public void KeepsDotsAndDashesInNames()
    {
        var repository = RepositoryReference.TryParse("my-org/my.quiz-repo", out _);

        Assert.NotNull(repository);
        Assert.Equal("my-org", repository!.Owner);
        Assert.Equal("my.quiz-repo", repository.Name);
    }

    [Fact]
    public void BuildsTheRepositoryUrl()
    {
        var repository = RepositoryReference.TryParse("octocat/Hello-World", out _);

        Assert.Equal("https://github.com/octocat/Hello-World", repository!.HtmlUrl);
        Assert.Equal("octocat/Hello-World", repository.FullName);
    }

    [Fact]
    public void ATreeUrlIsRejectedWithAnExplanation()
    {
        // What you get from the address bar while browsing a repo. It looks
        // perfectly good to the person pasting it, so the message has to say
        // what is wrong rather than just refusing.
        var repository = RepositoryReference.TryParse("https://github.com/a/b/tree/main", out var error);

        Assert.Null(repository);
        Assert.Contains("page inside", error);
    }

    [Fact]
    public void ABlobUrlIsRejectedWithAnExplanation()
    {
        var repository = RepositoryReference.TryParse("https://github.com/a/b/blob/main/index.html", out var error);

        Assert.Null(repository);
        Assert.Contains("page inside", error);
    }

    [Fact]
    public void AnotherHostIsRejectedByName()
    {
        var repository = RepositoryReference.TryParse("https://gitlab.com/owner/name", out var error);

        Assert.Null(repository);
        Assert.Contains("github.com", error);
    }

    [Fact]
    public void AGitHubUrlIsNotMistakenForAnotherHost()
    {
        // The host check captures and compares rather than using a negative
        // lookahead. A lookahead after an optional "www." backtracks: it retries
        // without consuming the www., then sees "www.github.com" != "github.com"
        // and rejects a perfectly good URL. This test pins that.
        var repository = RepositoryReference.TryParse("www.github.com/octocat/Hello-World", out var error);

        Assert.NotNull(repository);
        Assert.Null(error);
    }

    [Fact]
    public void AnOwnerWithNoRepositoryIsRejectedSpecifically()
    {
        var repository = RepositoryReference.TryParse("https://github.com/octocat", out var error);

        Assert.Null(repository);
        Assert.Contains("repository name", error);
    }

    [Fact]
    public void ABareWordIsRejectedSpecifically()
    {
        var repository = RepositoryReference.TryParse("octocat", out var error);

        Assert.Null(repository);
        Assert.Contains("owner", error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void EmptyIsRejected(string? text)
    {
        var repository = RepositoryReference.TryParse(text, out var error);

        Assert.Null(repository);
        Assert.NotNull(error);
    }
}

/// <summary>
/// Tests the GitHub service against a fake transport.
///
/// These verify the REQUEST shape and the handling of each response -- what is
/// sent, when the sha is included, how failures are phrased. They cannot verify
/// that GitHub accepts any of it: no authenticated call was ever made from the
/// environment this was written in. The error-body contract (a "message" field)
/// was confirmed against the live API; everything else rests on the documented
/// behaviour and is coded defensively.
/// </summary>
public class GitHubServiceTests
{
    /// <summary>Returns canned responses and records what was asked.</summary>
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<HttpRequestMessage> Requests { get; } = new();
        public List<string> Bodies { get; } = new();

        public FakeHandler Respond(HttpStatusCode status, string body = "{}")
        {
            _responses.Enqueue((status, body));
            return this;
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            Bodies.Add(request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken));

            var (status, body) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, "{}");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (GitHubService Service, FakeHandler Handler) Build()
    {
        var handler = new FakeHandler();
        var client = new HttpClient(handler);

        return (new GitHubService(client), handler);
    }

    private static RepositoryReference Repo()
        => RepositoryReference.TryParse("octocat/Hello-World", out _)!;

    private static JsonElement BodyOf(FakeHandler handler, int index)
        => JsonDocument.Parse(handler.Bodies[index]).RootElement;

    // --- Token --------------------------------------------------------------

    [Fact]
    public async Task VerifyTokenReportsTheAccount()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.OK, """{"login":"octocat"}""");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.True(result.Success);
        Assert.Contains("octocat", result.Message);
    }

    [Fact]
    public async Task VerifyTokenSendsTheRequiredHeaders()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.OK, """{"login":"octocat"}""");

        await service.VerifyTokenAsync("ghp_x");

        var request = handler.Requests[0];

        // GitHub rejects a request with no User-Agent outright.
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("ghp_x", request.Headers.Authorization.Parameter);
        Assert.Contains("QuizBuilder", request.Headers.UserAgent.ToString());
        Assert.True(request.Headers.Contains("X-GitHub-Api-Version"));
    }

    [Fact]
    public async Task AnEmptyTokenNeverReachesTheNetwork()
    {
        var (service, handler) = Build();

        var result = await service.VerifyTokenAsync("   ");

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ABadTokenIsExplainedNotDumped()
    {
        var (service, handler) = Build();

        // The real body, confirmed against the live API.
        handler.Respond(HttpStatusCode.Unauthorized,
            """{"message":"Bad credentials","documentation_url":"https://docs.github.com/rest","status":"401"}""");

        var result = await service.VerifyTokenAsync("ghp_bad");

        Assert.False(result.Success);
        // "GitHub rejected that token. Check it has not expired..."
        Assert.Contains("token", result.Message);
    }

    // --- Publishing ---------------------------------------------------------

    [Fact]
    public async Task PublishingANewFileSendsNoSha()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");   // GET: absent
        handler.Respond(HttpStatusCode.Created, """{"content":{"html_url":"https://github.com/o/r/blob/main/index.html"}}""");

        var result = await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html/>", "Publish quiz");

        Assert.True(result.Success, result.Message);

        var body = BodyOf(handler, 1);

        // Sending a sha for a file that does not exist is itself an error.
        Assert.False(body.TryGetProperty("sha", out _));
        Assert.Equal("main", body.GetProperty("branch").GetString());
    }

    [Fact]
    public async Task PublishingOverAnExistingFileSendsItsSha()
    {
        // The bug this prevents: "just PUT it" succeeds the FIRST time and fails
        // every time after, because the Contents API refuses an update with no
        // sha. A test that published once would never see it.
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.OK, """{"sha":"abc123"}""");    // GET: present
        handler.Respond(HttpStatusCode.OK, """{"content":{"html_url":"https://x"}}""");

        var result = await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html/>", "Update quiz");

        Assert.True(result.Success, result.Message);
        Assert.Equal("abc123", BodyOf(handler, 1).GetProperty("sha").GetString());
    }

    [Fact]
    public async Task TheContentIsBase64Encoded()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html>café ✓</html>", "m");

        var encoded = BodyOf(handler, 1).GetProperty("content").GetString();
        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded!));

        Assert.Equal("<html>café ✓</html>", decoded);
    }

    [Fact]
    public async Task AConflictIsReportedRatherThanForced()
    {
        // 409 means someone changed the file between our read and our write.
        // Forcing past it would discard their work.
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.OK, """{"sha":"stale"}""");
        handler.Respond(HttpStatusCode.Conflict, """{"message":"is at 111 but expected 222"}""");

        var result = await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html/>", "m");

        Assert.False(result.Success);
        Assert.Contains("changed on GitHub", result.Message);
    }

    [Fact]
    public async Task AMissingRepositoryExplainsThePrivateRepoCase()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");   // GET sha
        handler.Respond(HttpStatusCode.NotFound, """{"message":"Not Found"}""");   // PUT

        var result = await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html/>", "m");

        Assert.False(result.Success);
        Assert.Contains("private repository", result.Message);
    }

    [Fact]
    public async Task TheBranchIsPassedWhenLookingUpTheSha()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.PublishFileAsync("ghp_x", Repo(), "gh-pages", "index.html", "<html/>", "m");

        // Without ?ref= the sha comes from the default branch, and publishing to
        // a different one would then send a sha that does not belong to it.
        Assert.Contains("ref=gh-pages", handler.Requests[0].RequestUri!.Query);
    }

    [Fact]
    public async Task AnEmptyBranchFallsBackToMain()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.PublishFileAsync("ghp_x", Repo(), "  ", "index.html", "<html/>", "m");

        Assert.Equal("main", BodyOf(handler, 1).GetProperty("branch").GetString());
    }

    [Fact]
    public async Task APathWithFoldersKeepsItsSlashes()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.PublishFileAsync("ghp_x", Repo(), "main", "quiz/index.html", "<html/>", "m");

        // Escaping the whole path would collapse this into one segment called
        // "quiz%2Findex.html".
        Assert.Contains("/contents/quiz/index.html", handler.Requests[0].RequestUri!.AbsolutePath);
    }

    [Fact]
    public async Task AnEmptyPathNeverReachesTheNetwork()
    {
        var (service, handler) = Build();

        var result = await service.PublishFileAsync("ghp_x", Repo(), "main", "", "<html/>", "m");

        Assert.False(result.Success);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task AnEmptyCommitMessageGetsADefault()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.PublishFileAsync("ghp_x", Repo(), "main", "index.html", "<html/>", "");

        Assert.False(string.IsNullOrWhiteSpace(BodyOf(handler, 1).GetProperty("message").GetString()));
    }

    // --- Pages --------------------------------------------------------------

    [Fact]
    public async Task EnablingPagesReportsTheUrl()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");                                  // GET pages: off
        handler.Respond(HttpStatusCode.Created, """{"html_url":"https://octocat.github.io/Hello-World/"}""");

        var result = await service.EnablePagesAsync("ghp_x", Repo(), "main");

        Assert.True(result.Success, result.Message);
        Assert.Equal("https://octocat.github.io/Hello-World/", result.Url);
    }

    [Fact]
    public async Task PagesAlreadyOnIsNotAnError()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.OK, """{"html_url":"https://octocat.github.io/Hello-World/"}""");

        var result = await service.EnablePagesAsync("ghp_x", Repo(), "main");

        Assert.True(result.Success);
        Assert.Contains("already", result.Message);

        // One call: it looked, found it, and stopped.
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task EnablingPagesSendsTheBranchAndRoot()
    {
        var (service, handler) = Build();

        handler.Respond(HttpStatusCode.NotFound, "{}");
        handler.Respond(HttpStatusCode.Created, "{}");

        await service.EnablePagesAsync("ghp_x", Repo(), "gh-pages");

        var source = BodyOf(handler, 1).GetProperty("source");

        Assert.Equal("gh-pages", source.GetProperty("branch").GetString());
        Assert.Equal("/", source.GetProperty("path").GetString());
    }

    // --- Failures -----------------------------------------------------------

    [Fact]
    public async Task RateLimitingIsNamed()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.Forbidden, """{"message":"API rate limit exceeded for 1.2.3.4."}""");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.False(result.Success);
        // "GitHub's rate limit was hit. Wait a few minutes and try again."
        Assert.Contains("rate limit", result.Message);
    }

    [Fact]
    public async Task GitHubsOwnMessageIsPreferredOverAnInventedOne()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.UnprocessableEntity, """{"message":"branch main does not exist"}""");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.False(result.Success);
        Assert.Contains("branch main does not exist", result.Message);
    }

    [Fact]
    public async Task AnUnexpectedStatusStillSaysSomethingUseful()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.InternalServerError, """{"message":"Server Error"}""");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.False(result.Success);
        Assert.Contains("500", result.Message);
    }

    [Fact]
    public async Task AnEmptyBodyDoesNotThrow()
    {
        // Every field is treated as optional. A 204, or HTML from a proxy, must
        // not take the app down.
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.OK, "");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.True(result.Success);
    }

    [Fact]
    public async Task NonJsonBodyDoesNotThrow()
    {
        var (service, handler) = Build();
        handler.Respond(HttpStatusCode.BadGateway, "<html>proxy error</html>");

        var result = await service.VerifyTokenAsync("ghp_x");

        Assert.False(result.Success);
        Assert.NotNull(result.Message);
    }
}
