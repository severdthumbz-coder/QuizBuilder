using System.Net.Http;
using System.Text;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Services;

/// <summary>
/// AI grammar reviewer that calls Anthropic's Claude via the Messages API. The
/// cloud counterpart to <see cref="LocalEndpointReviewProvider"/>: same Core
/// <see cref="GrammarReviewEngine"/> (prompt + resilient parse), only the
/// transport differs. Two things distinguish the Messages API from the
/// OpenAI-compatible shape the local provider uses:
/// <list type="bullet">
/// <item>the system instruction is a TOP-LEVEL <c>system</c> field, not a
///   message with role "system";</item>
/// <item>auth is <c>x-api-key</c> + <c>anthropic-version</c> headers, and the
///   reply text is at <c>content[0].text</c> (a content-block array), not
///   <c>choices[0].message.content</c>.</item>
/// </list>
///
/// <para>
/// The key comes from settings at call time (DPAPI-decrypted via
/// <see cref="ISettingsService.GetAiReviewKey"/>), never held here. Every
/// expected failure — no key, unreachable, non-200 (bad key / rate limit),
/// malformed envelope, cancellation — returns
/// <see cref="GrammarReviewResult.Failed"/> with a plain message, never an
/// exception to the caller. This is the piece least verifiable without a live
/// call: the request/response shape is matched from Anthropic's documented
/// Messages API, but a real key on the maintainer's machine is the confirmation.
/// </para>
/// </summary>
public sealed class ClaudeReviewProvider : IGrammarReviewProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string AnthropicVersion = "2023-06-01";
    private const string DefaultModel = "claude-sonnet-4-5";
    private const int MaxTokens = 4096;

    private readonly HttpClient _http;
    private readonly Func<(string? apiKey, string? model)> _config;

    public ClaudeReviewProvider(Func<(string? apiKey, string? model)> config, HttpClient? http = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string DisplayName => "Claude";

    public async Task<GrammarReviewResult> ReviewAsync(
        IReadOnlyList<GrammarField> fields,
        CancellationToken cancellationToken = default)
    {
        if (!GrammarReviewEngine.HasCheckableText(fields))
            return GrammarReviewResult.Ok(Array.Empty<GrammarSuggestion>());

        var (apiKey, model) = _config();
        if (string.IsNullOrWhiteSpace(apiKey))
            return GrammarReviewResult.Failed(
                "No Claude API key is saved. Add one in Settings → AI grammar review.");

        // Messages API: system is a top-level field; the user turn carries the
        // fields to review. temperature 0 for determinism.
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model,
            max_tokens = MaxTokens,
            temperature = 0,
            system = GrammarReviewEngine.SystemInstruction,
            messages = new object[]
            {
                new { role = "user", content = GrammarReviewEngine.BuildUserPrompt(fields) },
            },
        };

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", AnthropicVersion);

            using var response = await _http.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return GrammarReviewResult.Failed(DescribeHttpError((int)response.StatusCode, body));
        }
        catch (OperationCanceledException)
        {
            return GrammarReviewResult.Failed("The grammar check was cancelled.");
        }
        catch (HttpRequestException)
        {
            return GrammarReviewResult.Failed(
                "Couldn't reach Claude. Check your internet connection.");
        }
        catch (Exception)
        {
            return GrammarReviewResult.Failed("Something went wrong talking to Claude.");
        }

        var content = ExtractText(body);
        if (content is null)
            return GrammarReviewResult.Failed("Claude's response was not in the expected shape.");

        return GrammarReviewEngine.ParseResponse(content, fields);
    }

    /// <summary>Plain-words messages for the common Messages API error codes.</summary>
    private static string DescribeHttpError(int status, string body) => status switch
    {
        401 => "Claude rejected the API key (401). Check the key in Settings.",
        403 => "This key isn't allowed to use that model (403).",
        429 => "Claude is rate-limiting requests (429). Wait a moment and try again.",
        >= 500 => $"Claude had a server error ({status}). Try again shortly.",
        _ => $"Claude returned an error ({status}).",
    };

    /// <summary>
    /// Pulls the assistant text out of a Messages envelope: the first text block
    /// in <c>content</c> (<c>content[0].text</c> when type == "text"). Returns
    /// null if that path is missing.
    /// </summary>
    private static string? ExtractText(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) &&
                        type.ValueKind == JsonValueKind.String &&
                        type.GetString() == "text" &&
                        block.TryGetProperty("text", out var text) &&
                        text.ValueKind == JsonValueKind.String)
                    {
                        return text.GetString();
                    }
                }
            }
        }
        catch (JsonException)
        {
            // fall through to null — a shape error
        }
        return null;
    }
}
