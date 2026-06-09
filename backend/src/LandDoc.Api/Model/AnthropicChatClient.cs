using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Options;

namespace LandDoc.Api.Model;

/// <summary>
/// Anthropic API direct chat adapter — the config-swap fallback provider (ADR-0012; Azure OpenAI GPT is
/// the live slice default). Uses the official Anthropic .NET SDK (NuGet <c>Anthropic</c>). API key,
/// model id, and base URL come from <see cref="AnthropicOptions"/> (the per-provider <c>Anthropic</c>
/// config section). The API key must be set via <c>dotnet user-secrets</c> or environment variable
/// (<c>Anthropic__ApiKey</c>) — never committed or hardcoded.
/// </summary>
public sealed class AnthropicChatClient : IChatClient
{
    private const int MaxTokens = 1024;

    private const string SystemPrompt =
        "Answer the question using only the supplied passages. " +
        "Provide the answer whenever the passages contain it — you may quote or combine information stated " +
        "across different passages, but do not add facts the passages do not contain. " +
        "Only if the passages genuinely do not contain the answer, respond exactly: " +
        "\"The answer is not found in the document(s).\"";

    private readonly AnthropicClient _client;
    private readonly string _model;

    public AnthropicChatClient(IOptions<AnthropicOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "Anthropic:ApiKey is required for AnthropicChatClient. " +
                "Set it via 'dotnet user-secrets set Anthropic:ApiKey <key>' or the " +
                "Anthropic__ApiKey environment variable. Never commit it.");

        _model = opts.Model;

        // Base URL from config so routing through a gateway stays config-only; the SDK defaults to
        // https://api.anthropic.com when BaseUrl is left unset.
        _client = string.IsNullOrWhiteSpace(opts.BaseUrl)
            ? new AnthropicClient { ApiKey = opts.ApiKey }
            : new AnthropicClient { ApiKey = opts.ApiKey, BaseUrl = opts.BaseUrl };
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
            contextText.AppendLine($"[Source: {passage.SourceName} · Chunk {passage.ChunkId}]");
            contextText.AppendLine(passage.Text);
            contextText.AppendLine();
        }

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = MaxTokens,
            System = SystemPrompt,
            Messages =
            [
                new()
                {
                    Role = Role.User,
                    Content = $"Passages:\n{contextText}\nQuestion: {question}",
                },
            ],
        };

        var message = await _client.Messages.Create(parameters, cancellationToken);

        // Grounded answer is the first text block; the union wrapper's .Value unwraps each block.
        return message.Content
            .Select(block => block.Value)
            .OfType<TextBlock>()
            .FirstOrDefault()?.Text ?? string.Empty;
    }
}
