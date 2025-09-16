using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;
using System.Security.Cryptography;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    // Simple single-entry cache keyed by SHA1 hash of normalized prompt
    private static string _cachedPromptHash;
    private static string _cachedPrompt;
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

    public void AddT2IParameters()
    {
        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
        {
            // Get the current positive prompt early; if missing, nothing to do
            var posPrompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
            if (posPrompt == null)
            {
                return;
            }

            try
            {
                bool hasStaticTag = userInput.ExtraMeta["original_prompt"].ToString().Contains("<comment:magicpromptstatic=1>");

                if (hasStaticTag)
                {
                    HandleCacheableRequest(posPrompt, userInput);
                    return;
                }
            }
            catch { /* ignore if missing */ }

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
        // Use hash of current prompt for caching key
        var currentPromptHash = ComputePromptHash(posPrompt);

        // Synchronize all accesses so parallel image requests share the same result
        lock (_cacheLock)
        {
            // Early return if cache hit (hash matches and we have a cached prompt)
            if (!string.IsNullOrEmpty(_cachedPromptHash) && _cachedPromptHash == currentPromptHash && !string.IsNullOrEmpty(_cachedPrompt))
            {
                userInput.InternalSet.Set(T2IParamTypes.Prompt, _cachedPrompt);
                return;
            }

            // If cache exists but hash does not match, unset it now; it will be replaced after the API call below
            if (!string.IsNullOrEmpty(_cachedPromptHash) && _cachedPromptHash != currentPromptHash)
            {
                _cachedPromptHash = null;
                _cachedPrompt = "";
            }

            try
            {
                var llmResponse = MakeLlmRequest(posPrompt, userInput);
                if (llmResponse != null)
                {
                    // Update the cache with the current hash and new prompt
                    _cachedPromptHash = currentPromptHash;
                    _cachedPrompt = llmResponse;
                }
            }
            catch (Exception ex)
            {
                Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            }

            // Regardless of outcome, we handled the static-tag path; don't fall through to a second call
            return;
        }
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
        string llmResponse = resp?["response"]?.ToString();

        if (!string.IsNullOrWhiteSpace(llmResponse))
        {
            userInput.InternalSet.Set(T2IParamTypes.Prompt, llmResponse);
            return llmResponse;
        }

        return null;
    }

    private static string ComputePromptHash(string prompt)
    {
        if (string.IsNullOrEmpty(prompt)) return string.Empty;
        // normalize: lowercase and strip all whitespaces
        var normalized = new string(prompt.ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
        using var sha1 = SHA1.Create();
        var bytes = Encoding.UTF8.GetBytes(normalized);
        var hash = sha1.ComputeHash(bytes);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (var b in hash)
            sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}

