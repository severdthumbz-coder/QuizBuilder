using System.Net.Http;
using System.Text;
using System.Text.Json;
using QuizBuilder.Core.Interfaces;
using QuizBuilder.Core.Services;

namespace QuizBuilder.App.Services;

/// <summary>
/// AI grammar reviewer that talks to a local / self-hosted OpenAI-compatible
/// endpoint (Ollama, LM Studio, LocalAI, …) via the chat-completions API. Built
/// first because it needs no cloud account or key and keeps content on the
/// user's machine — the whole pipeline (prompt, HTTP, parse, anchor) can be
/// developed and verified offline. The Claude provider is a thin variant with
/// different auth/request shape.
///
/// <para>
/// The heavy lifting — building the prompt and parsing the reply into anchored
/// suggestions — is Core's <see cref="GrammarReviewEngine"/> (pure, tested).
/// This class only does transport: compose the chat-completions request, send
/// it, pull the assistant message text out, hand it to the engine. Every
/// expected failure (no endpoint configured, unreachable, non-200, malformed
/// envelope) comes back as <see cref="GrammarReviewResult.Failed"/> with a
/// plain-words message — never an exception to the caller.
/// </para>
/// </summary>
public sealed class LocalEndpointReviewProvider : IGrammarReviewProvider
{
    private readonly HttpClient _http;
    private readonly Func<(string? endpoint, string? model)> _config;

    /// <param name="config">Supplies the current endpoint URL and model from
    /// settings at call time (so a settings change takes effect without
    /// rebuilding the provider).</param>
    public LocalEndpointReviewProvider(Func<(string? endpoint, string? model)> config, HttpClient? http = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
    }

    public string DisplayName => "Local endpoint";

    public async Task<GrammarReviewResult> ReviewAsync(
        IReadOnlyList<GrammarField> fields,
        CancellationToken cancellationToken = default)
    {
        if (!GrammarReviewEngine.HasCheckableText(fields))
            return GrammarReviewResult.Ok(Array.Empty<GrammarSuggestion>());

        var (endpoint, model) = _config();
        if (string.IsNullOrWhiteSpace(endpoint))
            return GrammarReviewResult.Failed(
                "No local endpoint URL is set. Add one in Settings → AI grammar review.");

        var url = CombineChatCompletions(endpoint!);

        var payload = new
        {
            model = string.IsNullOrWhiteSpace(model) ? "llama3" : model,
            messages = new object[]
            {
                new { role = "system", content = GrammarReviewEngine.SystemInstruction },
                new { role = "user", content = GrammarReviewEngine.BuildUserPrompt(fields) },
            },
            temperature = 0,
            stream = false,
        };

        string body;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, url)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
            };

            using var response = await _http.SendAsync(request, cancellationToken);
            body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
                return GrammarReviewResult.Failed(
                    $"The endpoint returned {(int)response.StatusCode}. Check the URL and that the server is running.");
        }
        catch (OperationCanceledException)
        {
            return GrammarReviewResult.Failed("The grammar check was cancelled.");
        }
        catch (HttpRequestException)
        {
            return GrammarReviewResult.Failed(
                "Couldn't reach the endpoint. Check the URL and that the local server is running.");
        }
        catch (Exception)
        {
            return GrammarReviewResult.Failed("Something went wrong talking to the endpoint.");
        }

        var content = ExtractAssistantContent(body);
        if (content is null)
            return GrammarReviewResult.Failed("The endpoint's response was not in the expected shape.");

        return GrammarReviewEngine.ParseResponse(content, fields);
    }

    /// <summary>
    /// Appends the chat-completions path if the configured base URL doesn't
    /// already include it, so both "http://host:port/v1" and
    /// "http://host:port/v1/chat/completions" work.
    /// </summary>
    private static string CombineChatCompletions(string endpoint)
    {
        var trimmed = endpoint.TrimEnd('/');
        if (trimmed.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            return trimmed;
        return trimmed + "/chat/completions";
    }

    /// <summary>
    /// Pulls the assistant message text out of a chat-completions envelope:
    /// <c>choices[0].message.content</c>. Returns null if the envelope is
    /// missing that path (parsed by the engine as a shape error upstream).
    /// </summary>
    private static string? ExtractAssistantContent(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array &&
                choices.GetArrayLength() > 0)
            {
                var first = choices[0];
                if (first.TryGetProperty("message", out var message) &&
                    message.TryGetProperty("content", out var content) &&
                    content.ValueKind == JsonValueKind.String)
                {
                    return content.GetString();
                }
            }
        }
        catch (JsonException)
        {
            // fall through to null — treated as a shape error
        }
        return null;
    }
}
