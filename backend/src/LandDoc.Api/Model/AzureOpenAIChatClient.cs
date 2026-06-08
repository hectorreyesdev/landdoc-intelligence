using System.ClientModel;
using System.Text;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace LandDoc.Api.Model;

/// <summary>
/// Azure OpenAI GPT chat adapter — the live slice-default provider (ADR-0012). Talks OpenAI Chat
/// Completions via the <c>Azure.AI.OpenAI</c> SDK. Endpoint, key, and deployment come from
/// <see cref="AzureOpenAIOptions"/> (dotnet user-secrets / managed identity — never committed); the
/// Azure client is built ONCE in the constructor. <see cref="ExtractFieldsAsync"/> stays unimplemented —
/// field extraction is best-effort and the ingest service swallows the failure (spec 0001).
/// </summary>
public sealed class AzureOpenAIChatClient : IChatClient
{
    private const string SystemPrompt =
        "Answer using only the supplied passages. " +
        "If the answer is not present in the passages, respond exactly: " +
        "\"The answer is not found in the document(s).\" " +
        "Do not fabricate or infer information beyond what the passages state.";

    private readonly ChatClient _chat;

    public AzureOpenAIChatClient(IOptions<AzureOpenAIOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var opts = options.Value;

        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is required for AzureOpenAIChatClient. " +
                "Set it via 'dotnet user-secrets set AzureOpenAI:Endpoint <url>' or the " +
                "AzureOpenAI__Endpoint environment variable. Never commit it.");
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "AzureOpenAI:ApiKey is required for AzureOpenAIChatClient. " +
                "Set it via 'dotnet user-secrets set AzureOpenAI:ApiKey <key>' or the " +
                "AzureOpenAI__ApiKey environment variable. Never commit it.");
        if (string.IsNullOrWhiteSpace(opts.Deployment))
            throw new InvalidOperationException(
                "AzureOpenAI:Deployment is required for AzureOpenAIChatClient — the Azure deployment name " +
                "(the SDK deploymentName), e.g. gpt-5.4-mini.");

        // api-version is left to the Azure.AI.OpenAI default for the slice; pinning via
        // AzureOpenAI:ApiVersion (AZURE-CONFIG §4) is a config-only follow-up.
        var azureClient = new AzureOpenAIClient(new Uri(opts.Endpoint), new ApiKeyCredential(opts.ApiKey));
        _chat = azureClient.GetChatClient(opts.Deployment);
    }

    public Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(
        string documentText,
        CancellationToken cancellationToken = default)
        => throw new NotImplementedException(
            "AzureOpenAIChatClient.ExtractFieldsAsync is not implemented — extraction is best-effort (spec 0001).");

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

        ChatMessage[] messages =
        [
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage($"Passages:\n{contextText}\nQuestion: {question}"),
        ];

        ClientResult<ChatCompletion> result =
            await _chat.CompleteChatAsync(messages, cancellationToken: cancellationToken);

        var content = result.Value.Content;
        return content.Count > 0 ? content[0].Text : string.Empty;
    }
}
