using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;
using FreneticUtilities.FreneticExtensions;
using System.Text.RegularExpressions;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    /// <summary>
    /// Cache for prompt rewrite results. Key is the normalized prompt, value is the LLM response.
    /// Limited to 1,000 entries with LRU (Least Recently Used) eviction.
    /// </summary>
    private static readonly Dictionary<string, string> _promptCache = new();
    private static readonly LinkedList<string> _cacheAccessOrder = new();
    private static readonly Dictionary<string, LinkedListNode<string>> _cacheNodes = new();
    private const int MaxCacheSize = 1000;
    private static readonly object CacheLock = new();

    /// <summary>
    /// Tracks pending LLM requests to prevent duplicate calls for the same normalized prompt.
    /// Key is the normalized prompt, value is a TaskCompletionSource that completes when the LLM response is ready.
    /// </summary>
    private static readonly Dictionary<string, TaskCompletionSource<string>> _pendingRequests = new();

    // Matches <mpprompt:...> and <mpprompt[InstructionName]:...>
    // Group 1 = instruction identifier (optional), Group 2 = prompt content (handles nested tags)
    private static readonly Regex MppromptRegex = new(@"<mpprompt(?:\[([^\]]+)\])?:((?:[^<>]|<[^>]*>)+)>", RegexOptions.Compiled);
    private static readonly Regex MpresponseRegex = new(@"<mpresponse:(\d+)>", RegexOptions.Compiled);
    private const int MaxWaitTimeMs = 30000; // 30-second timeout for waiting threads

    // Cache for models/settings response to avoid duplicate API calls between GetValues lambdas
    private static readonly object ModelsCacheLock = new();
    private static JObject _modelsCacheResponse;
    private static DateTime _modelsCacheTimeUtc;
    private static readonly TimeSpan ModelsCacheTtl = TimeSpan.FromSeconds(10);

    private static T2IRegisteredParam<bool> _paramUseCache;
    private static T2IRegisteredParam<string> _paramModelId;
    private static T2IRegisteredParam<string> _paramInstructions;

    public override void OnPreInit()
    {
        Logs.Info("MagicPromptExtension Version 2.4 Now with Vision and automatic processing! has started.");
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
        var paramGroup = new T2IParamGroup(
            Name: "Magic Prompt Auto Enable",
            Description: "Automatically use Magic Prompt to rewrite your prompt " +
                         "before generation.",
            Toggles: true,
            Open: false,
            OrderPriority: 9
        );
        _paramUseCache = T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "MP Use Cache",
            Description: "Cache LLM results for static prompts to avoid repeated " +
                         "requests to LLM.",
            Default: "true",
            Group: paramGroup,
            OrderPriority: 1
        ));
        T2IParamTypes.Register<bool>(new T2IParamType(
            Name: "MP Generate Wildcard Seed",
            Description: "Every time you press Generate, a new Wildcard Seed is " +
                         "generated. This is extremely useful for batching images, " +
                         "so they can reuse cached LLM responses.",
            Default: "false",
            Group: paramGroup,
            OrderPriority: 2
        ));
        _paramModelId = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "MP Model ID",
            Description: "Select an LLM to use for this batch",
            Default: "loading",
            IgnoreIf: "loading",
            Group: paramGroup,
            OrderPriority: 3,
            ValidateValues: false,
            GetValues: GetModelList
        ));
        _paramInstructions = T2IParamTypes.Register<string>(new T2IParamType(
            Name: "MP Instructions",
            Description: "Select a prompt to use for this batch",
            Default: "loading",
            IgnoreIf: "loading",
            Group: paramGroup,
            OrderPriority: 4,
            ValidateValues: false,
            GetValues: GetInstructionList
        ));

        PromptRegion.RegisterCustomPrefix("mpprompt");
        PromptRegion.RegisterCustomPrefix("mpresponse");

        T2IPromptHandling.PromptTagPostProcessors["mpprompt"] = (data, context) =>
        {
            if (context.Variables.Count > 0 && context.Input != null)
            {
                context.Input.ExtraMeta["mp_variables"] = new Dictionary<string, string>(context.Variables);
            }

            var instructionPart = !string.IsNullOrEmpty(context.PreData) ? $"[{context.PreData}]" : "";
            var parsedData = context.Parse(data);
            return $"<mpprompt{instructionPart}:{parsedData}>";
        };

        T2IPromptHandling.PromptTagPostProcessors["mpresponse"] = (data, context) =>
        {
            return $"<mpresponse:{data}>";
        };

        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
        {
            var prompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
            var modelId = userInput.InternalSet.Get(_paramModelId);
            var useCache = userInput.InternalSet.Get(_paramUseCache);

            var matches = MppromptRegex.Matches(prompt);

            if (matches.Count == 0)
            {
                if (MpresponseRegex.IsMatch(prompt))
                {
                    Logs.Warning("MagicPrompt: <mpresponse> found but no mpprompt tags exist");
                    prompt = MpresponseRegex.Replace(prompt, "");
                }
                ReplaceMpOriginal(prompt, "", userInput);
                return;
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                prompt = MppromptRegex.Replace(prompt, m => m.Groups[2].Value);
                prompt = MpresponseRegex.Replace(prompt, "");
                ReplaceMpOriginal(prompt, "", userInput);
                return;
            }

            if (!useCache)
            {
                ClearCache();
            }

            var firstMppromptContent = matches[0].Groups[2].Value;
            var llmResponses = new List<string>();

            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var fullTag = match.Value;
                var instructionId = match.Groups[1].Success ? match.Groups[1].Value : null;
                var mppromptContent = ResolveMpresponseReferences(match.Groups[2].Value, llmResponses);

                string llmResponse;
                try
                {
                    llmResponse = useCache
                        ? HandleCacheableRequest(mppromptContent, userInput, instructionId)
                        : MakeLlmRequest(mppromptContent, userInput, instructionId);

                    if (string.IsNullOrEmpty(llmResponse))
                    {
                        Logs.Error($"MagicPrompt: empty response from LLM for tag #{i}: {fullTag}");
                        llmResponse = mppromptContent;
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"MagicPrompt: LLM call failed for tag #{i} '{fullTag}': {ex.Message}");
                    llmResponse = mppromptContent;
                }

                llmResponses.Add(llmResponse);
                prompt = prompt.Replace(fullTag, llmResponse);
            }

            prompt = ResolveMpresponseReferences(prompt, llmResponses, logStandalone: true);
            ReplaceMpOriginal(prompt, firstMppromptContent, userInput);
        });
    }

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
                Logs.Warning($"MagicPrompt: <mpresponse:{refIndex}> references invalid index (only {llmResponses.Count} responses available)");
                return "";
            }
            Logs.Error($"MagicPrompt: <mpresponse:{refIndex}> references future mpprompt (only {llmResponses.Count} responses available)");
            return $"[ERROR: mpresponse:{refIndex} not yet available]";
        });
    }

    private static void ReplaceMpOriginal(string prompt, string mpprompt, T2IParamInput userInput)
    {
        userInput.InternalSet.Set(T2IParamTypes.Prompt, prompt.Replace("<mporiginal>", mpprompt));
    }

    /// <summary>
    /// Normalizes a prompt by trimming, converting to lowercase, and removing all whitespace.
    /// Used for cache key generation to ensure consistent matching.
    /// </summary>
    private static string NormalizePrompt(string prompt)
    {
        return string.IsNullOrWhiteSpace(prompt)
            ? string.Empty
            : new string(prompt.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());
    }

    /// <summary>
    /// Attempts to retrieve a cached result for the given normalized prompt.
    /// Updates LRU order if found.
    /// </summary>
    /// <remarks>Must be called within a lock(CacheLock) block.</remarks>
    private static bool TryGetFromCache(string normalizedPrompt, out string cachedResult)
    {
        if (_promptCache.TryGetValue(normalizedPrompt, out cachedResult))
        {
            Logs.Debug("MagicPrompt: cache hit for static-tag prompt.");
            // Update LRU order: move to end (most recently used)
            if (_cacheNodes.TryGetValue(normalizedPrompt, out var node))
            {
                _cacheAccessOrder.Remove(node);
                _cacheAccessOrder.AddLast(node);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Signals all waiting threads that the LLM request has completed.
    /// </summary>
    /// <remarks>Must be called within a lock(CacheLock) block.</remarks>
    private static void SignalPendingRequest(string normalizedPrompt, string result)
    {
        if (_pendingRequests.TryGetValue(normalizedPrompt, out var tcs))
        {
            // Always set a result (even if empty), never leave TCS pending
            tcs.SetResult(result ?? string.Empty);
        }
    }

    /// <summary>
    /// Removes a pending request from tracking, typically after completion or failure.
    /// </summary>
    /// <remarks>Must be called within a lock(CacheLock) block.</remarks>
    private static void CleanupPendingRequest(string normalizedPrompt)
    {
        _pendingRequests.Remove(normalizedPrompt);
    }

    private static string HandleCacheableRequest(string prompt, T2IParamInput userInput, string instructionId = null)
    {
        // Include instruction ID in cache key to differentiate requests with same prompt but different instructions
        var cacheKey = string.IsNullOrEmpty(instructionId)
            ? NormalizePrompt(prompt)
            : $"{NormalizePrompt(prompt)}||{instructionId.ToLowerInvariant()}";

        TaskCompletionSource<string> pendingTcs = null;

        // Fast path: check if already cached
        lock (CacheLock)
        {
            if (TryGetFromCache(cacheKey, out var cachedResult))
            {
                return cachedResult;
            }

            // Check if another thread is already fetching this prompt
            if (_pendingRequests.TryGetValue(cacheKey, out var existingTcs))
            {
                Logs.Debug("MagicPrompt: another thread is already fetching this prompt, waiting for result...");
                pendingTcs = existingTcs;
                // Don't return yet - we need to exit the lock first!
            }
            else
            {
                // We are the OWNER - create a TaskCompletionSource for other threads to wait on
                var tcs = new TaskCompletionSource<string>();
                _pendingRequests[cacheKey] = tcs;
            }
        }

        // If another thread was already fetching, wait for it OUTSIDE the lock
        if (pendingTcs != null)
        {
            return WaitForPendingRequest(cacheKey, pendingTcs);
        }

        // Make LLM request OUTSIDE the lock to avoid blocking other threads
        string llmResult;
        try
        {
            llmResult = MakeLlmRequest(prompt, userInput, instructionId);
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPrompt LLM request failed: {ex.Message}");
            
            // Clean up the pending request so other threads can retry
            lock (CacheLock)
            {
                CleanupPendingRequest(cacheKey);
            }

            return null;
        }

        // Add successful result to cache and signal waiting threads
        lock (CacheLock)
        {
            // Store in cache even if null to avoid retrying failed requests
            AddToCache(cacheKey, llmResult ?? string.Empty);
            
            // Signal all waiting threads with the result
            SignalPendingRequest(cacheKey, llmResult);
            
            // Clean up the pending request entry
            CleanupPendingRequest(cacheKey);
        }

        return llmResult;
    }

    /// <summary>
    /// Waits for a pending LLM request to complete with a timeout.
    /// This is called by non-owner threads that detected another thread is already fetching the result.
    /// </summary>
    private static string WaitForPendingRequest(string normalizedPrompt, TaskCompletionSource<string> tcs)
    {
        try
        {
            // Wait for the owner thread to complete the request
            if (tcs.Task.Wait(MaxWaitTimeMs))
            {
                var result = tcs.Task.Result;
                
                // If result is empty, it means LLM request failed or returned empty
                if (string.IsNullOrEmpty(result))
                {
                    Logs.Debug("MagicPrompt: waited for owner request, but got empty result");
                    return null;
                }

                Logs.Debug($"MagicPrompt: waited {MaxWaitTimeMs}ms for owner request, got result");
                return result;
            }
            else
            {
                Logs.Warning($"MagicPrompt: timeout after {MaxWaitTimeMs}ms waiting for owner request to complete");
                return null;
            }
        }
        catch (OperationCanceledException)
        {
            Logs.Error("MagicPrompt: pending request was cancelled");
            return null;
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPrompt: error waiting for pending request: {ex.Message}");
            return null;
        }
    }

    private static void AddToCache(string normalizedPrompt, string llmPrompt)
    {
        // Prune oldest entries if we've reached the max cache size
        while (_promptCache.Count >= MaxCacheSize)
        {
            var oldestNode = _cacheAccessOrder.First;
            if (oldestNode != null)
            {
                var oldestKey = oldestNode.Value;
                _promptCache.Remove(oldestKey);
                _cacheNodes.Remove(oldestKey);
                _cacheAccessOrder.RemoveFirst();
            }
            else
            {
                break;
            }
        }

        // Add or update the cache entry
        _promptCache[normalizedPrompt] = llmPrompt;
        
        // Remove from access order if already exists (for update case)
        if (_cacheNodes.TryGetValue(normalizedPrompt, out var existingNode))
        {
            _cacheAccessOrder.Remove(existingNode);
        }
        
        // Add to the end (most recently used)
        var newNode = _cacheAccessOrder.AddLast(normalizedPrompt);
        _cacheNodes[normalizedPrompt] = newNode;
    }

    private static void ClearCache()
    {
        lock (CacheLock)
        {
            _promptCache.Clear();
            _cacheAccessOrder.Clear();
            _cacheNodes.Clear();

            // Cancel any pending requests so waiting threads don't hang indefinitely
            foreach (var tcs in _pendingRequests.Values)
            {
                tcs.TrySetCanceled();
            }
            _pendingRequests.Clear();
        }
    }

    private static string MakeLlmRequest(string prompt, T2IParamInput userInput, string instructionId = null)
    {
        var request = new JObject
        {
            ["messageContent"] = new JObject
            {
                ["text"] = prompt,
                ["instructions"] = ResolveInstructions(userInput, instructionId)
            },
            ["modelId"] = userInput.InternalSet.Get(_paramModelId, defVal: string.Empty),
            ["messageType"] = "Text",
            ["action"] = "prompt",
            ["session_id"] = userInput.SourceSession?.ID ?? string.Empty,
            ["seed"] = userInput.InternalSet.Get(T2IParamTypes.Seed, -1).ToString()
        };

        var resp = LLMAPICalls.MagicPromptPhoneHome(request, userInput.SourceSession)
            .GetAwaiter()
            .GetResult();

        if (!(bool)resp?["success"])
        {
            return null;
        }

        var llmResponse = resp?["response"]?.ToString();
        return string.IsNullOrWhiteSpace(llmResponse) ? null : llmResponse;
    }

    /// <summary>
    /// Resolves the instruction content to use for an LLM request.
    /// Priority: 1) instructionId from tag, 2) instructions parameter from UI, 3) default "prompt" instruction
    /// </summary>
    /// <param name="instructionId">Optional instruction identifier from the mpprompt tag (e.g., from &lt;mpprompt[Video Request]:...&gt;)</param>
    private static string ResolveInstructions(T2IParamInput userInput, string instructionId = null)
    {
        // If instructionId is provided from the tag, use it; otherwise fall back to UI parameter
        var uiInstruction = userInput.InternalSet.Get(_paramInstructions);
        var instructions = !string.IsNullOrWhiteSpace(instructionId)
            ? instructionId
            : uiInstruction;

        Logs.Debug($"MagicPrompt ResolveInstructions: instructionId='{instructionId ?? "(null)"}', uiInstruction='{uiInstruction}', using='{instructions}'");

        var sessionSettings = SessionSettings.GetMagicPromptSettings()
            .GetAwaiter()
            .GetResult();
        if (!sessionSettings["success"]!.Value<bool>())
        {
            return instructions;
        }

        var settings = sessionSettings!["settings"];
        if (settings == null)
        {
            return instructions;
        }

        var instructionsObj = settings!["instructions"];
        if (instructionsObj == null)
        {
            return instructions;
        }

        if (string.IsNullOrWhiteSpace(instructions))
        {
            return instructionsObj["prompt"]?.ToString();
        }

        // instructions currently holds the selected key (either from tag or UI)
        // First check custom instructions by key
        var customPrompt = instructionsObj["custom"]?[instructions]?["content"]?.ToString();
        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            var title = instructionsObj["custom"][instructions]?["title"]?.ToString();
            Logs.Debug($"MagicPrompt: using custom instructions \"{title}\"");
            return SubstituteVariablesInInstructions(customPrompt, userInput);
        }

        // Check custom instructions by title (case-insensitive match for user convenience)
        if (instructionsObj["custom"] is JObject customObj)
        {
            foreach (var prop in customObj.Properties())
            {
                var title = prop.Value?["title"]?.ToString();
                if (!string.IsNullOrEmpty(title) &&
                    string.Equals(title, instructions, StringComparison.OrdinalIgnoreCase))
                {
                    var content = prop.Value?["content"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(content))
                    {
                        Logs.Debug($"MagicPrompt: using custom instructions \"{title}\" (matched by title)");
                        return SubstituteVariablesInInstructions(content, userInput);
                    }
                }
            }
        }

        var basePrompt = instructionsObj[instructions]?.ToString();
        if (!string.IsNullOrWhiteSpace(basePrompt))
        {
            Logs.Debug($"MagicPrompt: using base instructions \"{instructions}\"");
            return basePrompt;
        }

        Logs.Debug("MagicPrompt: using base instructions \"prompt\"");
        return instructionsObj["prompt"]?.ToString();
    }

    /// <summary>
    /// Substitutes &lt;var:name&gt; tags in instructions with variable values from the prompt.
    /// Variables are set in the prompt using &lt;setvar[name]:value&gt; syntax.
    /// </summary>
    private static string SubstituteVariablesInInstructions(string instructions, T2IParamInput userInput)
    {
        if (string.IsNullOrWhiteSpace(instructions) || !instructions.Contains("<var:"))
        {
            return instructions;
        }

        if (!userInput.ExtraMeta.TryGetValue("mp_variables", out object variablesObj) ||
            variablesObj is not Dictionary<string, string> variables ||
            variables.Count == 0)
        {
            return instructions;
        }

        var parsedInstructions = FreneticUtilities.FreneticToolkit.StringConversionHelper.QuickSimpleTagFiller(
            instructions, "<", ">", tag =>
            {
                string prefix = tag.BeforeAndAfter(':', out string varName);
                if (prefix.ToLowerInvariant() != "var")
                {
                    return $"<{tag}>";
                }

                if (variables.TryGetValue(varName, out string value))
                {
                    Logs.Debug($"MagicPrompt: substituted <var:{varName}> with \"{value}\" in instructions");
                    return value;
                }

                Logs.Warning($"MagicPrompt: variable '{varName}' not found in prompt for instruction substitution");
                return $"<var:{varName}>";
            }, false, 0);

        return parsedInstructions;
    }

    private static List<string> GetModelList(Session session)
    {
        var defaultResponse = new List<string>{"loading///loading"};

        try
        {
            var response = GetModelsResponseCached(session);
            if (response?["success"]?.Value<bool>() != true)
            {
                return defaultResponse;
            }

            var models = response["models"] as JArray;
            if (models == null || models.Count == 0)
            {
                return defaultResponse;
            }

            var list = new List<string>(models.Count);
            foreach (var m in models)
            {
                var modelId = m?["model"]?.ToString();
                var name = m?["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(modelId)) continue;
                if (string.IsNullOrWhiteSpace(name)) name = modelId;
                list.Add($"{modelId}///{name}");
            }

            return list.Count > 0 ? list : defaultResponse;
        }
        catch
        {
            return defaultResponse;
        }
    }

    private static List<string> GetInstructionList(Session session)
    {
        var defaultResponse = new List<string>{"loading///loading"};

        try
        {
            var list = new List<string>();
            var response = GetModelsResponseCached(session);
            var settings = response?["settings"] as JObject;
            var instructions = settings?["instructions"] as JObject;

            var prompt = instructions?["prompt"]?.ToString();
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                list.Add($"prompt///Enhance Prompt (Default)");
            }

            var custom = instructions?["custom"] as JObject;
            if (custom != null)
            {
                foreach (var prop in custom.Properties())
                {
                    var title = prop.Value?["title"]?.ToString();
                    if (!string.IsNullOrEmpty(title))
                    {
                        list.Add($"{prop.Name}///{title}");
                    }
                }
            }

            return list.Count > 0 ? list : defaultResponse;
        }
        catch
        {
            return defaultResponse;
        }
    }

    private static JObject GetModelsResponseCached(Session session)
    {
        // Serve from cache if fresh
        lock (ModelsCacheLock)
        {
            if (_modelsCacheResponse != null && DateTime.UtcNow - _modelsCacheTimeUtc < ModelsCacheTtl)
            {
                return _modelsCacheResponse;
            }
        }

        var resp = LLMAPICalls.GetMagicPromptModels(session)
            .GetAwaiter()
            .GetResult();

        lock (ModelsCacheLock)
        {
            _modelsCacheResponse = resp;
            _modelsCacheTimeUtc = DateTime.UtcNow;
        }

        return resp;
    }
}