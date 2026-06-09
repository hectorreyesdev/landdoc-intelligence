using System.ClientModel;
using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Chat;

namespace LandDoc.Api.Model;

/// <summary>
/// Azure OpenAI GPT chat adapter — the live slice-default provider (ADR-0012). Talks OpenAI Chat
/// Completions via the <c>Azure.AI.OpenAI</c> SDK. Endpoint, key, and deployment come from
/// <see cref="AzureOpenAIOptions"/> (dotnet user-secrets / managed identity — never committed); the
/// Azure client is built ONCE in the constructor. <see cref="ExtractFieldsAsync"/> implements the
/// generic role-neutral field extraction schema (ADR-0015) with structured outputs at temperature 0.
/// </summary>
public sealed class AzureOpenAIChatClient : IChatClient
{
    private const string SystemPrompt =
        "Answer using only the supplied passages. " +
        "If the answer is not present in the passages, respond exactly: " +
        "\"The answer is not found in the document(s).\" " +
        "Do not fabricate or infer information beyond what the passages state.";

    private const string ExtractionSystemPrompt =
        "Extract the key terms of this land/title document. " +
        "First identify the document type. " +
        "Capture the effective date and, if explicitly stated, the lease/term expiration (end) date — " +
        "do not compute the expiration from the primary term. " +
        "Return ONLY information explicitly present; do NOT infer or fabricate; omit/null anything absent. " +
        "Label each party with its role as the document uses it " +
        "(e.g. Lessor/Lessee, Grantor/Grantee, Operator, Assignor/Assignee, Affiant, Heirs).";

    // Used when the deployed model rejects json_schema response format — includes the key list so
    // the model knows the expected shape under plain json_object mode.
    private const string ExtractionSystemPromptFallback =
        "Extract the key terms of this land/title document. " +
        "First identify the document type. " +
        "Capture the effective date and, if explicitly stated, the lease/term expiration (end) date — " +
        "do not compute the expiration from the primary term. " +
        "Return ONLY information explicitly present; do NOT infer or fabricate; omit/null anything absent. " +
        "Label each party with its role as the document uses it " +
        "(e.g. Lessor/Lessee, Grantor/Grantee, Operator, Assignor/Assignee, Affiant, Heirs).\n\n" +
        "Return a JSON object with exactly these keys: " +
        "documentType (string), parties (array of {role, name} objects), " +
        "effectiveDate, expirationDate, legalDescription, county, state, acres, royalty, bonus, primaryTerm " +
        "(each a string or null), " +
        "otherNotableTerms (array of {name, value} objects).";

    // Generic role-neutral schema per ADR-0015. All keys required; scalar values nullable via anyOf
    // so the model can explicitly return null for absent fields without strict-mode violations.
    private static readonly BinaryData ExtractionSchema = BinaryData.FromString("""
        {
          "type": "object",
          "required": ["documentType","parties","effectiveDate","expirationDate","legalDescription","county","state","acres","royalty","bonus","primaryTerm","otherNotableTerms"],
          "additionalProperties": false,
          "properties": {
            "documentType": { "type": "string" },
            "parties": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["role","name"],
                "additionalProperties": false,
                "properties": {
                  "role": { "type": "string" },
                  "name": { "type": "string" }
                }
              }
            },
            "effectiveDate":    { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "expirationDate":   { "anyOf": [{"type": "string"}, {"type": "null"}], "description": "The lease/term expiration or end date, ONLY if explicitly stated. Do not compute it from the primary term." },
            "legalDescription": { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "county":           { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "state":            { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "acres":            { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "royalty":          { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "bonus":            { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "primaryTerm":      { "anyOf": [{"type": "string"}, {"type": "null"}] },
            "otherNotableTerms": {
              "type": "array",
              "items": {
                "type": "object",
                "required": ["name","value"],
                "additionalProperties": false,
                "properties": {
                  "name":  { "type": "string" },
                  "value": { "type": "string" }
                }
              }
            }
          }
        }
        """);

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

    public async Task<IReadOnlyList<ExtractedField>> ExtractFieldsAsync(
        string documentText,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentText);

        ChatMessage[] messages =
        [
            new SystemChatMessage(ExtractionSystemPrompt),
            new UserChatMessage(documentText),
        ];

        var schemaOptions = new ChatCompletionOptions
        {
            ResponseFormat = ChatResponseFormat.CreateJsonSchemaFormat(
                "land_document_extraction",
                ExtractionSchema,
                jsonSchemaIsStrict: true),
            Temperature = 0f,
        };

        string json;
        try
        {
            var result = await _chat.CompleteChatAsync(messages, schemaOptions, cancellationToken);
            json = ExtractText(result.Value);
        }
        catch (ClientResultException ex) when (ex.Status == 400)
        {
            // The deployed model rejected json_schema response format (or temperature); fall back
            // to json_object with an explicit key-list prompt and no temperature override.
            ChatMessage[] fallbackMessages =
            [
                new SystemChatMessage(ExtractionSystemPromptFallback),
                new UserChatMessage(documentText),
            ];
            var fallbackOptions = new ChatCompletionOptions
            {
                ResponseFormat = ChatResponseFormat.CreateJsonObjectFormat(),
            };
            var fallbackResult = await _chat.CompleteChatAsync(fallbackMessages, fallbackOptions, cancellationToken);
            json = ExtractText(fallbackResult.Value);
        }

        return ParseFields(json);
    }

    /// <summary>
    /// Flattens the structured-output JSON from the extraction call into an ordered list of
    /// <see cref="ExtractedField"/> records. Null/empty values are omitted; party roles become
    /// field <c>Name</c>s. Malformed or empty <paramref name="json"/> throws — ingest swallows it.
    /// </summary>
    internal static IReadOnlyList<ExtractedField> ParseFields(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("Extraction model returned empty content.");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Extraction model returned non-object JSON.");

        var fields = new List<ExtractedField>();

        // 1. DocumentType
        AppendScalar(fields, root, "documentType", "DocumentType");

        // 2. Parties — each party's role label becomes the field Name
        if (root.TryGetProperty("parties", out var parties) && parties.ValueKind == JsonValueKind.Array)
        {
            foreach (var party in parties.EnumerateArray())
            {
                var role = ReadString(party, "role");
                var name = ReadString(party, "name");
                if (!string.IsNullOrWhiteSpace(role) && !string.IsNullOrWhiteSpace(name))
                    fields.Add(new ExtractedField(role, name, null));
            }
        }

        // 3. Universal core scalars
        AppendScalar(fields, root, "effectiveDate",    "EffectiveDate");
        AppendScalar(fields, root, "expirationDate",   "ExpirationDate");
        AppendScalar(fields, root, "legalDescription", "LegalDescription");
        AppendScalar(fields, root, "county",           "County");
        AppendScalar(fields, root, "state",            "State");

        // 4. Conditional economics (omitted when null/absent)
        AppendScalar(fields, root, "acres",       "Acres");
        AppendScalar(fields, root, "royalty",     "Royalty");
        AppendScalar(fields, root, "bonus",       "Bonus");
        AppendScalar(fields, root, "primaryTerm", "PrimaryTerm");

        // 5. Open escape hatch for type-specific terms
        if (root.TryGetProperty("otherNotableTerms", out var other) && other.ValueKind == JsonValueKind.Array)
        {
            foreach (var term in other.EnumerateArray())
            {
                var name = ReadString(term, "name");
                var value = ReadString(term, "value");
                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(value))
                    fields.Add(new ExtractedField(name, value, null));
            }
        }

        return fields.AsReadOnly();
    }

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

    private static string ExtractText(ChatCompletion completion)
    {
        var content = completion.Content;
        if (content.Count == 0 || string.IsNullOrWhiteSpace(content[0].Text))
            throw new InvalidOperationException("Extraction model returned empty content.");
        return content[0].Text;
    }

    private static void AppendScalar(List<ExtractedField> fields, JsonElement root, string jsonKey, string fieldName)
    {
        var value = ReadString(root, jsonKey);
        if (!string.IsNullOrWhiteSpace(value))
            fields.Add(new ExtractedField(fieldName, value, null));
    }

    private static string? ReadString(JsonElement element, string key)
    {
        if (element.TryGetProperty(key, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }
}
