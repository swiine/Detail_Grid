using Anthropic;
using Anthropic.Exceptions;
using Anthropic.Models.Beta;
using Anthropic.Models.Beta.Messages;

namespace PavementBuildup.Ai;

public static class BuildupInterpreter
{
    public const string DefaultModel = "claude-opus-5-5";

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
                OutputConfig = new BetaOutputConfig { Effort = Effort.Low },
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
            throw Fail("auth", "The AI API key or account sign-in was rejected. Check it with TTWPVMTAI; an account sign-in may need signing in again.");
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
