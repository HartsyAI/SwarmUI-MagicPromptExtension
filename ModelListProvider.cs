using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using Newtonsoft.Json.Linq;
using SwarmUI.Accounts;

namespace Hartsy.Extensions.MagicPromptExtension;

public static class ModelListProvider
{
    private const string LoadingPlaceholder = "loading///loading";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(10);
    private static readonly object _cacheLock = new();
    private static JObject _cachedResponse;
    private static DateTime _cacheTimeUtc;

    public static List<string> GetModelList(Session session)
    {
        var defaultResponse = new List<string> { LoadingPlaceholder };

        try
        {
            var response = GetCachedResponse(session);
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
                if (string.IsNullOrWhiteSpace(modelId))
                {
                    continue;
                }

                var name = m?["name"]?.ToString();
                if (string.IsNullOrWhiteSpace(name))
                {
                    name = modelId;
                }

                list.Add($"{modelId}///{name}");
            }

            return list.Count > 0 ? list : defaultResponse;
        }
        catch
        {
            return defaultResponse;
        }
    }

    public static List<string> GetInstructionList(Session session)
    {
        var defaultResponse = new List<string> { LoadingPlaceholder };

        try
        {
            var list = new List<string>();
            var response = GetCachedResponse(session);
            var settings = response?["settings"] as JObject;
            var instructions = settings?["instructions"] as JObject;

            var prompt = instructions?["prompt"]?.ToString();
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                list.Add("prompt///Enhance Prompt (Default)");
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

    private static JObject GetCachedResponse(Session session)
    {
        lock (_cacheLock)
        {
            if (_cachedResponse != null && DateTime.UtcNow - _cacheTimeUtc < CacheTtl)
            {
                return _cachedResponse;
            }
        }

        var response = LLMAPICalls.GetMagicPromptModels(session)
            .GetAwaiter()
            .GetResult();

        lock (_cacheLock)
        {
            _cachedResponse = response;
            _cacheTimeUtc = DateTime.UtcNow;
        }

        return response;
    }
}
