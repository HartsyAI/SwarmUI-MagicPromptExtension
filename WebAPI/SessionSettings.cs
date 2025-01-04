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
                ["chat"] = "v1/chat/completions",
                ["models"] = "v1/models"
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
        ["backend"] = "ollama",
        ["visionbackend"] = "ollama",
        ["model"] = "llama3.2-vision:latest",
        ["visionmodel"] = "llama3.2-vision:latest",
        ["instructions"] = new JObject
        {
            ["chat"] = "You are a chatbot named Hartsy. Come up with a random backstory as to why you were created and how you were made to help the user with Stable Diffusion. You will respond to any questions or chats in this character. You will include tips on how to make good prompts for stable diffusion. Never break character and randomly end your response with \"Thank you for choosing Hartsy!\"",
            ["vision"] = "Analyze the image and respond to any of my requests with the image in mind. If I ask you about something or tell you to do something assume I am wanting you to reference the image in your decision making or response.",
            ["caption"] = "Respond only with a detailed caption for the image. Only use the format of describing what is in the image without any other text or comments. Describe everything in detail and include anything you can see.",
            ["prompt"] = "Only respond with an enhanced prompt for stable diffusion based on the users input. Here are some examples:\nUser: beautiful radiating glowing trashcan in a clean immaculate city park\\n\\nAI: surrealistic scene of a trash can emitting bright, colorful light in the middle of an immaculate city park, photorealism, highly detailed, trending on artstation\\n\\nUser: a tasty looking beer in a cool glass with metal surrounding it in an empty beergarten with rustic wooden tables and benches\\n\\nAI: a frosty mug of amber beer sits atop a rustic wooden table, surrounded by empty stools in an old German beer hall, warm wooden benches and walls adorned with vintage beer steins, soft lighting from a hanging pendant lamp illuminates the scene\\n\\nUser: a photo realistic of modern wood house, dark, with high detailed, realistic\\n\\nAI: a beautiful, modern wooden house at dusk, surrounded by tall trees and lush greenery, photo-realistic rendering with intricate details, sharp focus on every element, dramatic lighting creating a warm and inviting atmosphere\\n\\nUser: aang from avatar the last airbender, lightning bending, water bending, earth bending\\n\\nAI: Aang flying on his sky bison, blue aura around him, bending air, water, fire and earth simultaneously, in the style of Kairos, photorealistic, highly detailed\\n\\nUser: Salena Gomez as Deadpool\\n\\nAI: Selena Gomez dressed in red and black Deadpool outfit, holding two guns, posing with a serious face, high-quality portrait photo, studio lighting\\n\\nUser: a realistic photo of England queen\\n\\nAI: portrait photography of Queen Elizabeth II, in her iconic green dress, sitting on the throne, majestic, regal, royal, highly detailed\\n\\nUser: A beautiful Russian woman with bright blue eyes, long blonde hair, and pale white skin, depicted in pointillism style. She is wearing traditional Russian clothing, such as a kokoshnik and sarafan, with intricate embroidery details. The background is a picturesque Russian countryside scene, with towering churches and wheat fields. Art style: Hyperrealism, pointillism. Soft pastel colors.\\n\\nAI: A stunning portrait of a beautiful Russian woman with bright blue eyes, long blonde hair, and pale white skin. She is wearing traditional Russian clothing, including a kokoshnik and sarafan, with intricate embroidery details. The background showcases a picturesque Russian countryside scene, featuring towering churches and wheat fields. Art style: Hyperrealism, pointillism. Soft pastel colors, detailed close-up shot, high-resolution image.\\n\\nUser: Megumin (Konosuba)\\n\\nAI: detailed portrait of Megumin from Konosuba with a serious expression, long brown hair in ponytail, blue eyes, purple magic circle on forehead, highly detailed digital painting, anime style\\n\\nUser: Realistic photo of Taylor Swift with rainbow hair\\n\\nAI: a stunning, high-resolution portrait of Taylor Swift with vibrant rainbow-colored hair, soft lighting, natural environment, serene background, highly detailed, sharp focus",
        },
        ["backends"] = DefaultBackendConfig.DeepClone() // TODO: Change the name of this to something more descriptive
    };

    public static async Task<JObject> GetSettingsAsync()
    {
        try
        {
            string settingsJson = Program.Sessions.GenericSharedUser.GetGenericData(SETTINGS_KEY, SETTINGS_SUBKEY);
            JObject settings;
            if (string.IsNullOrEmpty(settingsJson))
            {
                settings = DefaultSettings.DeepClone() as JObject;
            }
            else
            {
                Logs.Verbose($"Retrieved settings from {SETTINGS_KEY}/{SETTINGS_SUBKEY}: {settingsJson}");
                settings = JObject.Parse(settingsJson);
                // Ensure we have all required backend configurations
                JObject backendsConfig = settings["backends"] as JObject ?? [];
                foreach (KeyValuePair<string, JToken> backend in DefaultBackendConfig)
                {
                    if (backendsConfig[backend.Key] == null)
                    {
                        backendsConfig[backend.Key] = backend.Value.DeepClone();
                        Logs.Verbose($"Added missing backend config for {backend.Key}");
                    }
                    else
                    {
                        // Ensure all required fields exist
                        JObject defaultBackend = backend.Value as JObject;
                        JObject existingBackend = backendsConfig[backend.Key] as JObject;
                        foreach (KeyValuePair<string, JToken> prop in defaultBackend)
                        {
                            if (existingBackend[prop.Key] == null)
                            {
                                existingBackend[prop.Key] = prop.Value.DeepClone();
                                Logs.Verbose($"Added missing property {prop.Key} for backend {backend.Key}");
                            }
                        }
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
            JObject existingSettings = await GetSettingsAsync();
            JObject newSettings = [];
            // Helper function to merge objects recursively
            static void MergeSettings(JObject target, JObject source, string parentKey = null)
            {
                if (source == null) return;
                foreach (JProperty prop in source.Properties())
                {
                    string key = prop.Name;
                    JToken value = prop.Value;
                    string path = parentKey == null ? key : $"{parentKey}.{key}";
                    // Special handling for backends
                    if (key == "backends")
                    {
                        if (target[key] == null || target[key] is not JObject)
                        {
                            target[key] = new JObject();
                        }
                        JObject targetBackends = target[key] as JObject;
                        JObject sourceBackends = value as JObject;
                        // Merge each backend's configuration, preserving unloadModel
                        foreach (JProperty backend in sourceBackends.Properties())
                        {
                            if (targetBackends[backend.Name] == null)
                            {
                                targetBackends[backend.Name] = new JObject();
                            }
                            MergeSettings(targetBackends[backend.Name] as JObject, backend.Value as JObject, $"{path}.{backend.Name}");
                        }
                        continue;
                    }
                    // Handle nested objects
                    if (value is JObject sourceObj)
                    {
                        if (target[key] == null || target[key] is not JObject)
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
                            target[key] = value;
                        }
                    }
                }
            }
            // Start with existing settings
            newSettings = existingSettings["settings"] as JObject ?? [];
            // Merge in new settings
            MergeSettings(newSettings, settings["settings"] as JObject);
            if (newSettings["baseurl"] != null)
            {
                string backend = newSettings["backend"]?.ToString();
                if (backend == "ollama" || backend == "openaiapi")
                {
                    // Only sync URL for configurable backends
                    if (newSettings["backends"]?[backend] != null)
                    {
                        ((JObject)newSettings["backends"][backend])["baseurl"] = newSettings["baseurl"];
                    }
                }
            }
            // Fixed backend URLs should never change
            newSettings["backends"]["openai"]["baseurl"] = "https://api.openai.com";
            newSettings["backends"]["anthropic"]["baseurl"] = "https://api.anthropic.com";
            newSettings["backends"]["openrouter"]["baseurl"] = "https://openrouter.ai";
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
            // Override the user settings with defaults and save
            Program.Sessions.GenericSharedUser.SaveGenericData(SETTINGS_KEY, SETTINGS_SUBKEY, settings.ToString());
            Logs.Verbose($"Reset settings to lowercase defaults: {settings}");
            return CreateSuccessResponse(null, null, settings);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to reset settings: {ex.Message}\nStack trace: {ex.StackTrace}");
            return CreateErrorResponse($"Failed to reset settings: {ex.Message}");
        }
    }
}
