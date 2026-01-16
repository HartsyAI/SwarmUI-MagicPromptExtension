using System.Text.RegularExpressions;
using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using Newtonsoft.Json.Linq;
using SwarmUI.Text2Image;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension;

public class PromptHandler
{
    // Matches <mpprompt:...> and <mpprompt[InstructionName]:...>
    // Group 1 = instruction identifier (optional), Group 2 = prompt content (handles nested tags)
    private static readonly Regex MppromptRegex = new(@"<mpprompt(?:\[([^\]]+)\])?:((?:[^<>]|<[^>]*>)+)>", RegexOptions.Compiled);
    private static readonly Regex MpresponseRegex = new(@"<mpresponse:(\d+)>", RegexOptions.Compiled);
    private readonly PromptCache _cache;
    private readonly T2IRegisteredParam<bool> _paramUseCache;
    private readonly T2IRegisteredParam<string> _paramModelId;
    private readonly T2IRegisteredParam<string> _paramInstructions;

    public PromptHandler(
        PromptCache cache,
        T2IRegisteredParam<bool> paramUseCache,
        T2IRegisteredParam<string> paramModelId,
        T2IRegisteredParam<string> paramInstructions)
    {
        _cache = cache;
        _paramUseCache = paramUseCache;
        _paramModelId = paramModelId;
        _paramInstructions = paramInstructions;
    }

    /// <summary>
    /// Processes a prompt, handling all mpprompt tags by calling the LLM and replacing them with responses.
    /// </summary>
    public void ProcessPrompt(T2IParamInput userInput)
    {
        var prompt = userInput.Get(T2IParamTypes.Prompt);
        var modelId = userInput.Get(_paramModelId);
        var useCache = userInput.Get(_paramUseCache);

        var matches = MppromptRegex.Matches(prompt);

        if (matches.Count == 0)
        {
            prompt = CleanOrphanedMpresponse(prompt);
            FinalizePrompt(prompt, "", userInput);
            return;
        }

        if (string.IsNullOrWhiteSpace(modelId))
        {
            prompt = StripMppromptTags(prompt);
            prompt = StripMpresponseTags(prompt);
            FinalizePrompt(prompt, "", userInput);
            return;
        }

        if (!useCache)
        {
            _cache.Clear();
        }

        var firstMppromptContent = matches[0].Groups[2].Value;
        var llmResponses = new List<string>();

        for (int i = 0; i < matches.Count; i++)
        {
            var match = matches[i];
            var fullTag = match.Value;
            var instructionId = match.Groups[1].Success ? match.Groups[1].Value : null;
            var mppromptContent = ResolveMpresponseReferences(match.Groups[2].Value, llmResponses);

            var llmResponse = GetLlmResponse(mppromptContent, userInput, instructionId, useCache, i, fullTag);
            llmResponses.Add(llmResponse);
            prompt = prompt.Replace(fullTag, llmResponse);
        }

        prompt = ResolveMpresponseReferences(prompt, llmResponses, logStandalone: true);
        FinalizePrompt(prompt, firstMppromptContent, userInput);
    }

    private string GetLlmResponse(string content, T2IParamInput userInput, string instructionId, bool useCache, int tagIndex, string fullTag)
    {
        try
        {
            string response;
            if (useCache)
            {
                var timeoutMs = LLMAPICalls.GetChatBackendTimeoutMs();
                response = _cache.GetOrCreate(content, instructionId, () => MakeLlmRequest(content, userInput, instructionId), timeoutMs);
            }
            else
            {
                response = MakeLlmRequest(content, userInput, instructionId);
            }

            if (string.IsNullOrEmpty(response))
            {
                Logs.Error($"MagicPromptExtension.PromptHandler: empty response from LLM for tag #{tagIndex}: {fullTag}");
                return content; // Fall back to original content
            }

            return response;
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.PromptHandler: LLM call failed for tag #{tagIndex} '{fullTag}': {ex.Message}");
            return content; // Fall back to original content
        }
    }

    private string MakeLlmRequest(string prompt, T2IParamInput userInput, string instructionId = null)
    {
        var request = new JObject
        {
            ["messageContent"] = new JObject
            {
                ["text"] = prompt,
                ["instructions"] = InstructionResolver.Resolve(userInput, instructionId, _paramInstructions)
            },
            ["modelId"] = userInput.Get(_paramModelId, defVal: string.Empty),
            ["messageType"] = "Text",
            ["action"] = "prompt",
            ["session_id"] = userInput.SourceSession?.ID ?? string.Empty,
            ["seed"] = userInput.Get(T2IParamTypes.Seed, -1).ToString()
        };

        var resp = LLMAPICalls.MagicPromptPhoneHome(request, userInput.SourceSession)
            .GetAwaiter()
            .GetResult();

        var success = resp?["success"];
        if (success == null || !success.Value<bool>())
        {
            return null;
        }

        var llmResponse = resp?["response"]?.ToString();
        return string.IsNullOrWhiteSpace(llmResponse) ? null : llmResponse;
    }

    /// <summary>
    /// Resolves mpresponse references within text, substituting with previously obtained LLM responses.
    /// </summary>
    private static string ResolveMpresponseReferences(string text, List<string> llmResponses, bool logStandalone = false)
    {
        return MpresponseRegex.Replace(text, m =>
        {
            if (!int.TryParse(m.Groups[1].Value, out int refIndex))
            {
                return m.Value;
            }

            if (refIndex >= 0 && refIndex < llmResponses.Count)
            {
                return llmResponses[refIndex];
            }

            if (logStandalone)
            {
                Logs.Warning($"MagicPromptExtension.PromptHandler: <mpresponse:{refIndex}> references invalid index (only {llmResponses.Count} responses available)");
                return "";
            }

            Logs.Error($"MagicPromptExtension.PromptHandler: <mpresponse:{refIndex}> references future mpprompt (only {llmResponses.Count} responses available)");
            return $"[ERROR: mpresponse:{refIndex} not yet available]";
        });
    }

    private static string CleanOrphanedMpresponse(string prompt)
    {
        if (MpresponseRegex.IsMatch(prompt))
        {
            Logs.Warning("MagicPromptExtension.PromptHandler: <mpresponse> found but no mpprompt tags exist");
            return MpresponseRegex.Replace(prompt, "");
        }
        return prompt;
    }

    private static string StripMppromptTags(string prompt)
    {
        return MppromptRegex.Replace(prompt, m => m.Groups[2].Value);
    }

    private static string StripMpresponseTags(string prompt)
    {
        return MpresponseRegex.Replace(prompt, "");
    }

    private static void FinalizePrompt(string prompt, string originalMpprompt, T2IParamInput userInput)
    {
        userInput.Set(T2IParamTypes.Prompt, prompt.Replace("<mporiginal>", originalMpprompt));
    }
}
