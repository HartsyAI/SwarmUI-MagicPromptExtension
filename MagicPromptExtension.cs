using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    /// <summary>
    /// Immutable snapshot of the last static-tag prompt rewrite result.
    /// Storing as a single volatile reference ensures readers see a consistent pair.
    /// </summary>
    private sealed record CacheSnapshot(string NormalizedPrompt, string LlmPrompt);

    private static volatile CacheSnapshot _cacheSnapshot;

    // Single lock to coordinate LLM requests and cache updates atomically
    private static readonly object _cacheLock = new();

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
        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
        {
            // Get the current positive prompt early; if missing, nothing to do
            var posPrompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
            if (string.IsNullOrWhiteSpace(posPrompt))
            {
                return;
            }

            // Check for static tag safely without relying on exceptions
            if (userInput.ExtraMeta != null && userInput.ExtraMeta.TryGetValue("original_prompt", out var originalObj))
            {
                var originalPrompt = originalObj?.ToString() ?? string.Empty;
                
                if (originalPrompt.Contains("<comment:magicpromptstatic=1>", StringComparison.OrdinalIgnoreCase))
                {
                    HandleCacheableRequest(posPrompt, userInput);
                    return;
                }
            }

            // No static tag: proceed with normal behavior (no cache coordination needed)
            try
            {
                MakeLlmRequest(posPrompt, userInput);
            }
            catch (Exception ex)
            {
                Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            }
        });
    }

    private static void HandleCacheableRequest(string posPrompt,  T2IParamInput userInput)
    {
        var normalizedPrompt = string.IsNullOrWhiteSpace(posPrompt) 
            ? string.Empty 
            : new string(posPrompt.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Fast path: double-checked locking
        // 1) Check outside the lock to avoid serializing the common case (cache hits).
        // 2) Acquire the lock and check again to avoid a race if another thread populated the cache meanwhile.
        // Note: Checking only once inside the lock would be correct but increases contention and latency under concurrency.
        if (CheckCache(normalizedPrompt, userInput))
        {
            Logs.Debug("MagicPrompt: cache hit (pre-lock) for static-tag prompt.");
            return;
        }

        // Single-lock synchronization to avoid duplicate LLM calls for the same prompt in parallel
        lock (_cacheLock)
        {
            // Double-check cache under the lock in case another thread populated it meanwhile
            if (CheckCache(normalizedPrompt, userInput))
            {
                Logs.Debug("MagicPrompt: cache hit (post-lock) for static-tag prompt.");
                return;
            }

            try
            {
                var llmResponse = MakeLlmRequest(posPrompt, userInput);
                if (!string.IsNullOrEmpty(llmResponse))
                {
                    // Atomically swap the cache snapshot so readers observe a consistent pair
                    _cacheSnapshot = new CacheSnapshot(normalizedPrompt, llmResponse);
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            }
        }
    }

    private static bool CheckCache(string normalizedPrompt, T2IParamInput userInput)
    {
        // Atomic snapshot read of the last cache entry
        var snapshot = _cacheSnapshot;
        if (snapshot is null) return false;
        if (!string.Equals(snapshot.NormalizedPrompt, normalizedPrompt, StringComparison.Ordinal)) return false;
        if (string.IsNullOrEmpty(snapshot.LlmPrompt)) return false;

        userInput.InternalSet.Set(T2IParamTypes.Prompt, snapshot.LlmPrompt);
        return true;
    }

    private static string MakeLlmRequest(string posPrompt,  T2IParamInput userInput)
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
                ["text"] = posPrompt,
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

        if (string.IsNullOrWhiteSpace(llmResponse))
        {
            return null;
        }

        userInput.InternalSet.Set(T2IParamTypes.Prompt, llmResponse);
        return llmResponse;
    }
}

