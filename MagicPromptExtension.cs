using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

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

        T2IPromptHandling.PromptTagPostProcessors["mpprompt"] = (data, context) =>
        {
            return $"<mpprompt:{context.Parse(data)}>";
        };

        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
        {
            var prompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
            var promptRegion = new PromptRegion(prompt);
            var mpprompt = "";
            var modelId = userInput.InternalSet.Get(_paramModelId);
            var useCache = userInput.InternalSet.Get(_paramUseCache);

            foreach (var part in promptRegion.Parts.Where(part => part.Prefix == "mpprompt"))
            {
                mpprompt = part.DataText;
                break;
            }

            if (string.IsNullOrWhiteSpace(modelId))
            {
                Logs.Debug("MagicPrompt: disabled");
                prompt = prompt.Replace($"<mpprompt:{mpprompt}>", mpprompt);
                ReplaceMpOriginal(prompt, "", userInput);
                return;
            }

            if (string.IsNullOrWhiteSpace(mpprompt))
            {
                Logs.Debug("MagicPrompt: <mpprompt> not found or empty");
                ReplaceMpOriginal(prompt, "", userInput);
                return;
            }

            try
            {
                if (!useCache)
                {
                    Logs.Debug("MagicPrompt: not using cache");
                    ClearCache();
                } else {
                    Logs.Debug("MagicPrompt: using cache");
                }

                var llmResponse = useCache
                    ? HandleCacheableRequest(mpprompt, userInput)
                    // Use Cache is disabled: proceed with normal behavior
                    // (no cache coordination needed)
                    : MakeLlmRequest(mpprompt, userInput);

                // No response from LLM, fallback to original prompt
                if (string.IsNullOrEmpty(llmResponse)) {
                    Logs.Error("MagicPrompt: empty response from LLM");
                    prompt = prompt.Replace($"<mpprompt:{mpprompt}>", mpprompt);
                    ReplaceMpOriginal(prompt, "", userInput);
                    return;
                }

                prompt = prompt.Replace($"<mpprompt:{mpprompt}>", llmResponse);
                ReplaceMpOriginal(prompt, mpprompt, userInput);
            }
            catch (Exception ex)
            {
                Logs.Error($"MagicPrompt phone home call failed: {ex.Message}");
                prompt = prompt.Replace($"<mpprompt:{mpprompt}>", mpprompt);
                ReplaceMpOriginal(prompt, "", userInput);
            }
        });
    }

    private static void ReplaceMpOriginal(string prompt, string mpprompt, T2IParamInput userInput)
    {
        userInput.InternalSet.Set(T2IParamTypes.Prompt, prompt.Replace("<mporiginal>", mpprompt));
    }

    private static string HandleCacheableRequest(string prompt, T2IParamInput userInput)
    {
        var normalizedPrompt = string.IsNullOrWhiteSpace(prompt)
            ? string.Empty
            : new string(prompt.Trim().ToLowerInvariant().Where(c => !char.IsWhiteSpace(c)).ToArray());

        // Fast path: check cache with lock
        var cachedPrompt = CheckCache(normalizedPrompt);
        if (!string.IsNullOrEmpty(cachedPrompt))
        {
            Logs.Debug("MagicPrompt: cache hit for static-tag prompt.");
            return cachedPrompt;
        }

        // Double-check cache under lock to avoid race conditions
        lock (CacheLock)
        {
            if (_promptCache.TryGetValue(normalizedPrompt, out cachedPrompt))
            {
                Logs.Debug("MagicPrompt: cache hit (post-lock) for static-tag prompt.");
                // Update LRU order
                if (_cacheNodes.TryGetValue(normalizedPrompt, out var node))
                {
                    _cacheAccessOrder.Remove(node);
                    _cacheAccessOrder.AddLast(node);
                }
                return cachedPrompt;
            }
        }

        // Make LLM request OUTSIDE the lock to avoid blocking other threads
        // and to prevent async-over-sync deadlocks
        string llmPrompt;
        try
        {
            llmPrompt = MakeLlmRequest(prompt, userInput);
        }
        catch (Exception ex)
        {
            Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
            return null;
        }

        if (string.IsNullOrEmpty(llmPrompt))
        {
            return null;
        }

        // Add to cache under lock
        lock (CacheLock)
        {
            // Check again in case another thread added it while we were making the request
            if (_promptCache.TryGetValue(normalizedPrompt, out cachedPrompt))
            {
                Logs.Debug("MagicPrompt: another thread already cached this prompt.");
                return cachedPrompt;
            }

            AddToCache(normalizedPrompt, llmPrompt);
        }

        return llmPrompt;
    }

    private static string CheckCache(string normalizedPrompt)
    {
        lock (CacheLock)
        {
            if (_promptCache.TryGetValue(normalizedPrompt, out var cachedResponse))
            {
                // Update LRU order: move this key to the end (most recently used)
                if (_cacheNodes.TryGetValue(normalizedPrompt, out var node))
                {
                    _cacheAccessOrder.Remove(node);
                    _cacheAccessOrder.AddLast(node);
                }
                return cachedResponse;
            }
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
        }
    }

    private static string MakeLlmRequest(string prompt, T2IParamInput userInput)
    {
        var request = new JObject
        {
            ["messageContent"] = new JObject
            {
                ["text"] = prompt,
                ["instructions"] = ResolveInstructions(userInput.InternalSet.Get(_paramInstructions))
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

    private static string ResolveInstructions(string instructions)
    {
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

        // instructions currently holds the selected key
        var customPrompt = instructionsObj["custom"]?[instructions]?["content"]?.ToString();
        if (!string.IsNullOrWhiteSpace(customPrompt))
        {
            var title = instructionsObj["custom"][instructions]?["title"]?.ToString();
            Logs.Debug($"MagicPrompt: using custom instructions \"{title}\"");
            return customPrompt;
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