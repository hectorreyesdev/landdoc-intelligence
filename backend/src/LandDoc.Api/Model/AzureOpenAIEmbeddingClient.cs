using System.ClientModel;
using Azure.AI.OpenAI;
using Microsoft.Extensions.Options;
using OpenAI.Embeddings;

namespace LandDoc.Api.Model;

/// <summary>
/// Azure OpenAI embeddings adapter — the live slice-default provider (ADR-0013). Uses the
/// <c>Azure.AI.OpenAI</c> SDK with the deployment named by <see cref="AzureOpenAIOptions.EmbeddingDeployment"/>
/// (e.g. <c>text-embedding-3-small</c>). Endpoint, API key, and deployment name come from
/// <see cref="AzureOpenAIOptions"/> (dotnet user-secrets / env vars — never committed); the Azure
/// embeddings client is built ONCE in the constructor. <see cref="EmbeddingOptions.Dimension"/> is
/// forwarded as the SDK <c>dimensions</c> parameter so the cosine invariant holds (all vectors share
/// the same length regardless of the model's native dimension).
/// </summary>
public sealed class AzureOpenAIEmbeddingClient : IEmbeddingClient
{
    private readonly EmbeddingClient _embeddingClient;
    private readonly int _dimension;

    public AzureOpenAIEmbeddingClient(
        IOptions<AzureOpenAIOptions> azureOptions,
        IOptions<EmbeddingOptions> embeddingOptions)
    {
        ArgumentNullException.ThrowIfNull(azureOptions);
        ArgumentNullException.ThrowIfNull(embeddingOptions);
        var opts = azureOptions.Value;

        if (string.IsNullOrWhiteSpace(opts.Endpoint))
            throw new InvalidOperationException(
                "AzureOpenAI:Endpoint is required for AzureOpenAIEmbeddingClient. " +
                "Set it via 'dotnet user-secrets set AzureOpenAI:Endpoint <url>' or the " +
                "AzureOpenAI__Endpoint environment variable. Never commit it.");
        if (string.IsNullOrWhiteSpace(opts.ApiKey))
            throw new InvalidOperationException(
                "AzureOpenAI:ApiKey is required for AzureOpenAIEmbeddingClient. " +
                "Set it via 'dotnet user-secrets set AzureOpenAI:ApiKey <key>' or the " +
                "AzureOpenAI__ApiKey environment variable. Never commit it.");
        if (string.IsNullOrWhiteSpace(opts.EmbeddingDeployment))
            throw new InvalidOperationException(
                "AzureOpenAI:EmbeddingDeployment is required for AzureOpenAIEmbeddingClient — the Azure " +
                "deployment name for the embeddings model, e.g. text-embedding-3-small.");

        var azureClient = new AzureOpenAIClient(new Uri(opts.Endpoint), new ApiKeyCredential(opts.ApiKey));
        _embeddingClient = azureClient.GetEmbeddingClient(opts.EmbeddingDeployment);
        _dimension = embeddingOptions.Value.Dimension;
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);

        var options = new EmbeddingGenerationOptions { Dimensions = _dimension };
        ClientResult<OpenAIEmbedding> result =
            await _embeddingClient.GenerateEmbeddingAsync(text, options, cancellationToken);
        return result.Value.ToFloats().ToArray();
    }
}
