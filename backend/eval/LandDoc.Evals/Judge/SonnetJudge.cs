using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;

namespace LandDoc.Evals.Judge;

/// <summary>
/// Builds the LLM judge for the grounding + correctness evaluators (ADR-0021): Claude Sonnet 4.6 as a
/// <see cref="Microsoft.Extensions.AI.IChatClient"/> via the Anthropic SDK's built-in MEAI adapter
/// (<c>AnthropicClientExtensions.AsIChatClient</c>) — no hand-written adapter needed.
/// <para>
/// ⚠️ This is <b>MEAI's</b> <see cref="IChatClient"/>, NOT the project's <c>LandDoc.Api.Model.IChatClient</c>.
/// The two are different types: this judge is wired independently and has no effect on the system under
/// test, which keeps using its own chat/embedding ports unchanged.
/// </para>
/// Model id comes from <c>Eval:JudgeModel</c> (default <c>claude-sonnet-4-6</c>); the API key from the
/// existing <c>Anthropic:ApiKey</c> secret (user-secrets / env / Key Vault — never committed).
/// </summary>
public static class SonnetJudge
{
    public const string DefaultModel = "claude-sonnet-4-6";

    public static ChatConfiguration CreateChatConfiguration(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var apiKey = configuration["Anthropic:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException(
                "Anthropic:ApiKey is required for the eval judge. Set it via " +
                "'dotnet user-secrets set Anthropic:ApiKey <key>' or the Anthropic__ApiKey environment " +
                "variable before running the eval. Never commit it.");
        }

        var model = configuration["Eval:JudgeModel"];
        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        // The MEAI quality evaluators set BOTH Temperature and TopP on their ChatOptions, but the Claude
        // models reject a request that specifies both ("temperature and top_p cannot both be specified").
        // Wrap the judge to drop TopP (keeping the low Temperature the graders rely on) before each call.
        IChatClient judge = new SingleSamplingParameterChatClient(
            new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model));

        return new ChatConfiguration(judge);
    }
}

/// <summary>
/// Delegating chat client that ensures only ONE sampling parameter reaches the model. The
/// <c>Microsoft.Extensions.AI.Evaluation.Quality</c> evaluators populate both <c>Temperature</c> and
/// <c>TopP</c>; Anthropic's API rejects requests that set both. We keep <c>Temperature</c> (low, for
/// stable grading) and clear <c>TopP</c>. A no-op when only one — or neither — is set.
/// </summary>
internal sealed class SingleSamplingParameterChatClient(IChatClient innerClient)
    : DelegatingChatClient(innerClient)
{
    public override Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetResponseAsync(messages, DropTopPIfBothSet(options), cancellationToken);

    public override IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
        => base.GetStreamingResponseAsync(messages, DropTopPIfBothSet(options), cancellationToken);

    private static ChatOptions? DropTopPIfBothSet(ChatOptions? options)
    {
        if (options?.Temperature is not null && options.TopP is not null)
        {
            options = options.Clone();
            options.TopP = null;
        }

        return options;
    }
}
