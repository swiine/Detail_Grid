using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace PavementBuildup.Ai;

/// <summary>
/// Sends free text to Claude with the pavement build-up prompt and returns the converted one-line build-up.
/// Called by reflection from PavementBuildup.Core.AiConverter (only primitive types cross the boundary).
/// </summary>
public static class BuildupInterpreter
{
    public const string DefaultModel = "claude-opus-5-5";

    /// <summary>
    /// Returns the converted text. Throws <see cref="InvalidOperationException"/> with a message fit for the user;
    /// its <c>Data["kind"]</c> is "auth", "rate", "server", "network", "refusal" or "other".
    /// </summary>
    public static async Task<string> ConvertAsync(string apiKey, string? model, string systemPrompt, string text, CancellationToken cancellationToken)
    {
        AnthropicClient client = string.IsNullOrWhiteSpace(apiKey) ? new() : new() { ApiKey = apiKey };

        try
        {
            var response = await client.Beta.Messages.Create(new MessageCreateParams
            {
                Model = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim(),
                MaxTokens = 16000,
                System = systemPrompt,
                // A simple rewrite: low effort keeps it quick and cheap.
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
                // If a safety classifier declines, let the server retry on a fallback model.
                Betas = [AnthropicBeta.ServerSideFallback2026_07_01],
                Fallbacks = new Default(),
                Messages = [new() { Role = Role.User, Content = text }],
            }, cancellationToken);

            if (response.StopReason == "refusal")
                throw Fail("refusal", "The AI declined to convert this text. Enter the build-up yourself or reword it.");

            var output = string.Join("\n", response.Content
                .Select(b => b.Value).OfType<BetaTextBlock>().Select(t => t.Text)).Trim();
            output = StripFences(output);
            if (output.Length == 0)
                throw Fail("other", "The AI returned no text.");
            return output;
        }
        catch (AnthropicUnauthorizedException)
        {
            throw Fail("auth", "The AI API key was rejected. Check it in AI settings.");
        }
        catch (AnthropicForbiddenException ex)
        {
            throw Fail("auth", "The AI API key isn't allowed to use this model: " + ex.Message);
        }
        catch (AnthropicRateLimitException)
        {
            throw Fail("rate", "The AI service is busy (rate limit). Try again in a moment.");
        }
        catch (Anthropic5xxException)
        {
            throw Fail("server", "The AI service had a problem. Try again shortly.");
        }
        catch (AnthropicBadRequestException ex)
        {
            throw Fail("other", "The AI request was rejected: " + ex.Message);
        }
        catch (AnthropicApiException ex)
        {
            throw Fail("other", "The AI request failed: " + ex.Message);
        }
        catch (AnthropicIOException ex)
        {
            throw Fail("network", "Couldn't reach the AI service (network/proxy): " + ex.Message);
        }
        catch (HttpRequestException ex)
        {
            throw Fail("network", "Couldn't reach the AI service (network/proxy): " + ex.Message);
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw Fail("network", "The AI service didn't answer in time.");
        }
    }

    private static InvalidOperationException Fail(string kind, string message)
    {
        var ex = new InvalidOperationException(message);
        ex.Data["kind"] = kind;
        return ex;
    }

    /// <summary>Removes ``` fences and a leading "Output:" the model might add despite the prompt.</summary>
    private static string StripFences(string s)
    {
        var lines = s.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => !l.TrimStart().StartsWith("```")).ToList();
        var joined = string.Join("\n", lines).Trim();
        foreach (var prefix in new[] { "Output:", "Converted:" })
            if (joined.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                joined = joined[prefix.Length..].Trim();
        return joined.Trim('`').Trim();
    }
}
