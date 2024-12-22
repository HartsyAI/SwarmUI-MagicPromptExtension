using Newtonsoft.Json.Linq;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI;

public class SessionSettings : MagicPromptAPI
{
    private const string SETTINGS_KEY = "magicprompt";
    private const string SETTINGS_SUBKEY = "config";

    // Hardcoded API endpoints and configurations
    private static readonly JObject DefaultBackendConfig = new()
    {
        ["ollama"] = new JObject
        {
            ["baseurl"] = "http://localhost:11434",
            ["unloadModel"] = false,
            ["endpoints"] = new JObject
            {
                ["chat"] = "/api/chat",
                ["models"] = "/api/tags"
            }
        },
        ["openaiapi"] = new JObject
        {
            ["baseurl"] = "http://localhost:11434",
            ["unloadModel"] = false,
            ["endpoints"] = new JObject
            {
                ["chat"] = "v1/chat/completions",
                ["models"] = "/v1/models"
            },
            ["apikey"] = ""
        },
        ["openai"] = new JObject
        {
            ["baseurl"] = "https://api.openai.com",
            ["endpoints"] = new JObject
            {
                ["chat"] = "/v1/chat/completions",
                ["models"] = "/v1/models"
            },
            ["apikey"] = ""
        },
        ["anthropic"] = new JObject
        {
            ["baseurl"] = "https://api.anthropic.com",
            ["endpoints"] = new JObject
            {
                ["chat"] = "v1/messages"
            },
            ["apikey"] = ""
        },
        ["openrouter"] = new JObject
        {
            ["baseurl"] = "https://openrouter.ai",
            ["endpoints"] = new JObject
            {
                ["chat"] = "/api/v1/chat/completions",
                ["models"] = "/api/v1/models"
            },
            ["apikey"] = ""
        }
    };

    private static readonly JObject DefaultSettings = new()
    {
        ["backend"] = "openrouter",
        ["visionbackend"] = "openrouter",
        ["unloadmodel"] = false,
        ["model"] = "meta-llama/llama-3.2-90b-vision-instruct:free",
        ["visionmodel"] = "meta-llama/llama-3.2-90b-vision-instruct:free",
        ["instructions"] = new JObject
        {
            ["chat"] = "INSTRUCTIONS: You are a helpful chatbot named Hartsy! talk in a conversational way. At the end of your responses you should include sayings like (Thanks for choosing Hartsy!) USER:",
            ["vision"] = "INSTRUCTIONS: Respond only with a detailed prompt for Stable Diffusion. If there is an image, describe it to me by giving me a detailed prompt I can use to recreate the exact image. IMPORTANT: Only respond with the prompt nothing more. USER:",
            ["caption"] = "INSTRUCTIONS: Respond only with a caption for the image. or answer the users question only using the format of describing the image as a caption. If there is no image, respond with 'No image found'. USER:"
        },
        ["backends"] = DefaultBackendConfig.DeepClone() // TODO: Change the name of this to something more descriptive
    };

    public static async Task<JObject> GetSettingsAsync()
    {
        try
        {
            string settingsJson = Program.Sessions.GenericSharedUser.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
            //Logs.Debug($"Raw settings from storage: {settingsJson}");
            
            JObject settings;
            if (string.IsNullOrEmpty(settingsJson))
            {
                Logs.Debug($"No existing settings found for {SETTINGS_KEY}/{SETTINGS_SUBKEY}, using defaults");
                settings = DefaultSettings.DeepClone() as JObject;
            }
            else
            {
                Logs.Debug($"Retrieved settings from {SETTINGS_KEY}/{SETTINGS_SUBKEY}: {settingsJson}");
                settings = JObject.Parse(settingsJson);

                // Ensure we have all required backend configurations
                var backendsConfig = settings["backends"] as JObject ?? new JObject();
                foreach (var backend in DefaultBackendConfig)
                {
                    if (backendsConfig[backend.Key] == null)
                    {
                        backendsConfig[backend.Key] = backend.Value.DeepClone();
                        Logs.Debug($"Added missing backend config for {backend.Key}");
                    }
                    else
                    {
                        // Ensure all required fields exist
                        var defaultBackend = backend.Value as JObject;
                        var existingBackend = backendsConfig[backend.Key] as JObject;
                        foreach (var prop in defaultBackend)
                        {
                            if (existingBackend[prop.Key] == null)
                            {
                                existingBackend[prop.Key] = prop.Value.DeepClone();
                                Logs.Debug($"Added missing property {prop.Key} for backend {backend.Key}");
                            }
                        }
                        
                        // Log the API key if it exists
                        var apiKey = existingBackend["apikey"]?.ToString();
                        Logs.Debug($"Backend {backend.Key} API key: {(string.IsNullOrEmpty(apiKey) ? "not set" : "is set")}");
                    }
                }
                settings["backends"] = backendsConfig;
            }
            return CreateSuccessResponse(null, null, settings);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to get settings: {ex.Message}\nStack trace: {ex.StackTrace}");
            return CreateErrorResponse($"Failed to get settings: {ex.Message}");
        }
    }

    public static async Task<JObject> SaveSettingsAsync(JObject settings)
    {
        try
        {
            Logs.Debug("[1] Starting SaveSettingsAsync");
            Logs.Debug($"Input settings: {settings}");

            if (settings["settings"] == null)
            {
                return new JObject
                {
                    ["success"] = false,
                    ["error"] = "No settings section provided"
                };
            }

            // Get existing settings
            var existingSettings = await GetSettingsAsync();
            var newSettings = new JObject();

            // Helper function to merge objects recursively
            void MergeSettings(JObject target, JObject source, string parentKey = null)
            {
                if (source == null) return;

                foreach (var prop in source.Properties())
                {
                    var key = prop.Name;
                    var value = prop.Value;
                    var path = parentKey == null ? key : $"{parentKey}.{key}";

                    // Special handling for API keys
                    if (key == "apikey")
                    {
                        if (!string.IsNullOrEmpty(value?.ToString()))
                        {
                            Logs.Debug($"[2] Updating API key for {parentKey}");
                            target[key] = value;
                        }
                        continue;
                    }

                    // Handle nested objects
                    if (value is JObject sourceObj)
                    {
                        if (target[key] == null || !(target[key] is JObject))
                        {
                            target[key] = new JObject();
                        }
                        MergeSettings(target[key] as JObject, sourceObj, path);
                    }
                    // Handle arrays or simple values
                    else
                    {
                        if (value != null && value.Type != JTokenType.Null)
                        {
                            Logs.Debug($"[3] Setting {path} = {(key == "apikey" ? "[REDACTED]" : value)}");
                            target[key] = value;
                        }
                    }
                }
            }

            // Start with existing settings
            newSettings = existingSettings["settings"] as JObject ?? new JObject();

            // Merge in new settings
            MergeSettings(newSettings, settings["settings"] as JObject);

            Logs.Debug("[4] Final merged settings structure:");
            Logs.Debug($"- backend (LLM): {newSettings["backend"]}");
            Logs.Debug($"- model (LLM): {newSettings["model"]}");
            Logs.Debug($"- baseurl (LLM): {newSettings["baseurl"]}");
            Logs.Debug($"- visionbackend: {newSettings["visionbackend"]}");
            Logs.Debug($"- visionmodel: {newSettings["visionmodel"]}");
            Logs.Debug($"- visionbaseurl: {newSettings["visionbaseurl"]}");
            Logs.Debug($"- unloadmodel: {newSettings["unloadmodel"]}");
            
            if (newSettings["instructions"] != null)
            {
                Logs.Debug("- instructions:");
                Logs.Debug($"  - chat: {newSettings["instructions"]?["chat"]}");
                Logs.Debug($"  - vision: {newSettings["instructions"]?["vision"]}");
            }

            if (newSettings["backends"] != null)
            {
                Logs.Debug("- backends:");
                foreach (var backend in newSettings["backends"].Children<JProperty>())
                {
                    Logs.Debug($"  - {backend.Name}:");
                    foreach (var prop in backend.Value.Children<JProperty>())
                    {
                        if (prop.Name == "apikey")
                        {
                            Logs.Debug($"    - {prop.Name}: [REDACTED]");
                        }
                        else
                        {
                            Logs.Debug($"    - {prop.Name}: {prop.Value}");
                        }
                    }
                }
            }

            Program.Sessions.GenericSharedUser.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, newSettings.ToString());
            
            return new JObject
            {
                ["success"] = true,
                ["settings"] = newSettings
            };
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in SaveSettingsAsync: {ex.Message}");
            return new JObject
            {
                ["success"] = false,
                ["error"] = ex.Message
            };
        }
    }

    /// <summary>You screw something up? Resets user settings to defaults.</summary>
    public static async Task<JObject> ResetSettingsAsync()
    {
        try
        {
            JObject settings = DefaultSettings;
            // Save the settings
            Program.Sessions.GenericSharedUser.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, settings.ToString());
            Logs.Debug($"Reset settings to lowercase defaults: {settings}");
            return CreateSuccessResponse(null, null, settings);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to reset settings: {ex.Message}\nStack trace: {ex.StackTrace}");
            return CreateErrorResponse($"Failed to reset settings: {ex.Message}");
        }
    }
}
