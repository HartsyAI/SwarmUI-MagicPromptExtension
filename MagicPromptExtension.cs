using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    /// <summary>
    /// Immutable snapshot of the last cached prompt rewrite result.
    /// Storing as a single volatile reference ensures readers see a consistent pair.
    /// </summary>
    private sealed record CacheSnapshot(string NormalizedPrompt, string LlmPrompt);
    private static volatile CacheSnapshot _cacheSnapshot;
    private static readonly object CacheLock = new();
    private static T2IRegisteredParam<bool> _paramAutoEnable;
    private static T2IRegisteredParam<bool> _paramAppendOriginal;
    private static T2IRegisteredParam<bool> _paramUseCache;

    public override void OnPreInit()
    {
        Logs.Info("MagicPromptExtension Version 2.2 Now with Vision! has started.");
        ScriptFiles.Add("Assets/magicprompt.js");
        ScriptFiles.Add("Assets/vision.js");
        ScriptFiles.Add("Assets/chat.js");
        ScriptFiles.Add("Assets/settings.js");
        StyleSheetFiles.Add("Assets/magicprompt.css");
        StyleSheetFiles.Add("Assets/vision.css");
        StyleSheetFiles.Add("Assets/chat.css");
        StyleSheetFiles.Add("Assets/settings.css");
    }

    public override void OnInit()
    {
        // Register API endpoints so they can be used in the frontend
        MagicPromptAPI.Register();
        AddT2IParameters();
    }

    private static void AddT2IParameters()
    {
        var paramGroup = new T2IParamGroup("Magic Prompt", Toggles: false, Open: false, IsAdvanced: false, OrderPriority: 9);
        _paramAutoEnable = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Auto Enable",
            Description: "Automatically use Magic Prompt to rewrite your prompt before generation.",
            Default: "false",
            Group: paramGroup,
            OrderPriority: 1
        ));
        _paramAppendOriginal = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Append Original Prompt",
            Description: "Append the original prompt after the generated LLM prompt.",
            Default: "false",
            Group: paramGroup,
            OrderPriority: 2
        ));
        _paramUseCache = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "Use Cache",
            Description: "Cache LLM results for static prompts to avoid repeated requests to LLM.",
            Default: "false",
            Group: paramGroup,
            OrderPriority: 3
        ));

        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
        {
            // Get the current positive prompt early; if missing, nothing to do
            var prompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
            if (string.IsNullOrWhiteSpace(prompt))
            {
                return;
            }

            // If Auto Enable is not checked, clear cache and return early
            if (!userInput.InternalSet.Get(_paramAutoEnable))
            {
                // Clear cache whenever Auto Enable is off
                ClearCache();
                return;
            }

            // Parse the prompt to handle regional tags, segments, etc, and only send the core text to the LLM
            var promptRegions = new PromptRegion(prompt);
            var llmInputPrompt = string.IsNullOrWhiteSpace(promptRegions.GlobalPrompt)
                ? prompt
                : promptRegions.GlobalPrompt;

            try
            {
                var useCache = userInput.InternalSet.Get(_paramUseCache);
                if (!useCache)
                {
                    // Clear cache whenever Use Cache is off
                    ClearCache();
                }
                var llmResponse = useCache 
                    ? HandleCacheableRequest(llmInputPrompt, userInput)
                    // Use Cache is disabled: proceed with normal behavior (no cache coordination needed)
                    : MakeLlmRequest(llmInputPrompt, userInput);

                // No response from LLM, fallback to original prompt
                if (string.IsNullOrEmpty(llmResponse)) return;
                if (userInput.InternalSet.Get(_paramAppendOriginal))
                {
                    llmResponse = $"{llmResponse}\n{prompt}";
                }

                userInput.InternalSet.Set(T2IParamTypes.Prompt, llmResponse);
            }
            catch (Exception ex)
            {
                Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            }
        });
    }

    private static string HandleCacheableRequest(string prompt,  T2IParamInput userInput)
    {
        var normalizedPrompt = string.IsNullOrWhiteSpace(prompt) 
            ? string.Empty 
            : new string(prompt.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Fast path: double-checked locking
        // 1) Check outside the lock to avoid serializing the common case (cache hits).
        // 2) Acquire the lock and check again to avoid a race if another thread populated the cache meanwhile.
        // Note: Checking only once inside the lock would be correct but increases contention and latency under concurrency.
        var cachedPrompt = CheckCache(normalizedPrompt);
        if (!string.IsNullOrEmpty(cachedPrompt))
        {
            Logs.Debug("MagicPrompt: cache hit (pre-lock) for static-tag prompt.");
            return cachedPrompt;
        }

        // Single-lock synchronization to avoid duplicate LLM calls for the same prompt in parallel
        lock (CacheLock)
        {
            cachedPrompt = CheckCache(normalizedPrompt);
            // Double-check cache under the lock in case another thread populated it meanwhile
            if (!string.IsNullOrEmpty(cachedPrompt))
            {
                Logs.Debug("MagicPrompt: cache hit (post-lock) for static-tag prompt.");
                return cachedPrompt;
            }

            try
            {
                var llmResponse = MakeLlmRequest(prompt, userInput);
                if (!string.IsNullOrEmpty(llmResponse))
                {
                    // Atomically swap the cache snapshot so readers observe a consistent pair
                    _cacheSnapshot = new CacheSnapshot(normalizedPrompt, llmResponse);

                    return llmResponse;
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            }
        }

        return null;
    }

    private static string CheckCache(string normalizedPrompt)
    {
        // Atomic snapshot read of the last cache entry
        var snapshot = _cacheSnapshot;
        if (snapshot is null) return null;
        if (!string.Equals(snapshot.NormalizedPrompt, normalizedPrompt, StringComparison.Ordinal)) return null;
        return string.IsNullOrEmpty(snapshot.LlmPrompt) ? null : snapshot.LlmPrompt;
    }

    private static void ClearCache()
    {
        lock (CacheLock)
        {
            _cacheSnapshot = null;
        }
    }

    private static string MakeLlmRequest(string prompt,  T2IParamInput userInput)
    {
        // Build request for MagicPromptPhoneHome including required fields
        string modelId = null;
        try
        {
            var settingsResp = SessionSettings.GetMagicPromptSettings().GetAwaiter().GetResult();
            if (settingsResp != null && settingsResp["success"]?.Value<bool>() == true)
            {
                var settings = settingsResp["settings"] as JObject;
                modelId = settings?["model"]?.ToString();
            }
        }
        catch { /* ignore and leave modelId null */ }

        JObject request = new()
        {
            ["messageContent"] = new JObject
            {
                ["text"] = prompt,
                // Leave instructions empty to let server-side default logic choose based on action
                ["instructions"] = ""
            },
            ["modelId"] = modelId ?? string.Empty,
            ["messageType"] = "Text",
            ["action"] = "prompt",
            ["session_id"] = userInput.SourceSession?.ID ?? string.Empty
        };

        var resp = LLMAPICalls.MagicPromptPhoneHome(request, userInput.SourceSession).GetAwaiter().GetResult();
        var llmResponse = resp?["response"]?.ToString();

        return string.IsNullOrWhiteSpace(llmResponse) ? null : llmResponse;
    }
}

