using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Configuration;

namespace LandDoc.Evals.Judge;

/// <summary>
/// Builds the LLM judge for the grounding + correctness evaluators (ADR-0020): Claude Sonnet 4.6 as a
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

        IChatClient judge = new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model);
        return new ChatConfiguration(judge);
    }
}
