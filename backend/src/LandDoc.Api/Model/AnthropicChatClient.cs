using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Model;

/// <summary>
/// Anthropic API direct chat adapter — the slice-default provider (ADR-0010). Calls the Anthropic
/// Messages REST API directly so no third-party NuGet package is needed in the slice. Base URL and
/// credentials come from <see cref="ModelClientOptions"/> so a later Foundry gateway swap is
/// config-only. API key must be set via <c>dotnet user-secrets</c> or environment variable
/// (<c>ModelClient__ApiKey</c>) — never committed or hardcoded.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private static readonly HttpClient Http = new();

    private const string SystemPrompt =
        "Answer using only the supplied passages. " +
        "If the answer is not present in the passages, respond exactly: " +
        "\"The answer is not found in the document(s).\" " +
        "Do not fabricate or infer information beyond what the passages state.";

    private readonly string _apiKey;
    private readonly string _model;
    private readonly string _messagesUrl;

    public AnthropicChatClient(IOptions<ModelClientOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "ModelClient:ApiKey is required for AnthropicChatClient. " +
                "Set it via 'dotnet user-secrets set ModelClient:ApiKey <key>' or the " +
                "ModelClient__ApiKey environment variable. Never commit it.");

        _apiKey = opts.ApiKey;
        _model = opts.Model;

        // Base URL from config so swapping to a Foundry gateway is config-only (ADR-0007).
        var baseUrl = string.IsNullOrWhiteSpace(opts.BaseUrl)
            ? "https://api.anthropic.com"
            : opts.BaseUrl.TrimEnd('/');
        _messagesUrl = $"{baseUrl}/v1/messages";
    }

    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(
        string documentText,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException(
            "AnthropicChatClient.ExtractFieldsAsync is not implemented in the slice.");

    public async Task<string> AnswerAsync(
        string question,
        IReadOnlyList<QaPassage> context,
        CancellationToken cancellationToken = default)
    {
        var contextText = new StringBuilder();
        foreach (var passage in context)
        {
            contextText.AppendLine($"[Chunk {passage.ChunkId}]");
            contextText.AppendLine(passage.Text);
            contextText.AppendLine();
        }

        var body = new
        {
            model = _model,
            max_tokens = 1024,
            system = SystemPrompt,
            messages = new[]
            {
                new { role = "user", content = $"Passages:\n{contextText}\nQuestion: {question}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, _messagesUrl);
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = JsonContent.Create(body);

        using var response = await Http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken),
            cancellationToken: cancellationToken);

        return doc.RootElement
            .GetProperty("content")[0]
            .GetProperty("text")
            .GetString() ?? string.Empty;
    }
}
