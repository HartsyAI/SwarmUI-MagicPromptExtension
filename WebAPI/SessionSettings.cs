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
        ["backend"] = "ollama",
        ["visionbackend"] = "ollama",
        ["unloadmodel"] = false,
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
            void MergeSettings(JObject target, JObject source, string parentKey = null)
            {
                if (source == null) return;

                foreach (JProperty prop in source.Properties())
                {
                    string key = prop.Name;
                    JToken value = prop.Value;
                    string path = parentKey == null ? key : $"{parentKey}.{key}";

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

            // Sync backend base URLs with top-level base URL if it exists
            if (newSettings["baseurl"] != null && newSettings["backend"] != null)
            {
                string backend = newSettings["backend"].ToString();
                if (newSettings["backends"] != null && newSettings["backends"][backend] != null)
                {
                    ((JObject)newSettings["backends"][backend])["baseurl"] = newSettings["baseurl"];
                    Logs.Debug($"Synced {backend} backend baseurl with top-level baseurl: {newSettings["baseurl"]}");
                }
            }
            if (newSettings["visionbaseurl"] != null && newSettings["visionbackend"] != null)
            {
                string visionBackend = newSettings["visionbackend"].ToString();
                if (newSettings["backends"] != null && newSettings["backends"][visionBackend] != null)
                {
                    ((JObject)newSettings["backends"][visionBackend])["baseurl"] = newSettings["visionbaseurl"];
                    Logs.Debug($"Synced {visionBackend} backend baseurl with top-level visionbaseurl: {newSettings["visionbaseurl"]}");
                }
            }

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
