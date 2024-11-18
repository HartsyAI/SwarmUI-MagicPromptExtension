using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using System.Text.Json;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Config;
using SwarmUI.Accounts;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    // Define a permission group specifically for MagicPromptAPI
    public static class MagicPromptPermissions
    {
        public static readonly PermInfoGroup MagicPromptPermGroup = new("MagicPrompt", "Permissions related to MagicPrompt functionality for API calls and settings.");
        public static readonly PermInfo PermPhoneHome = Permissions.Register(new("magicprompt_phone_home", "Phone Home", "Allows the extension to make outbound calls to retrieve external data.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermSaveConfig = Permissions.Register(new("magicprompt_save_config", "Save Configuration", "Allows the user to save configuration settings.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermSaveApiKey = Permissions.Register(new("magicprompt_save_api_key", "Save API Key", "Allows the user to save API keys.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermReadConfig = Permissions.Register(new("magicprompt_read_config", "Read Configuration", "Allows the user to read configuration settings.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermGetModels = Permissions.Register(new("magicprompt_get_models", "Get Models", "Allows the user to retrieve the list of available models.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermLoadModel = Permissions.Register(new("magicprompt_load_model", "Load Model", "Allows the user to load a model for usage.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
    }

    [API.APIClass("API routes related to MagicPromptExtension extension")]
    public class MagicPromptAPI
    {
        /// <summary>Registers the API calls for the extension, enabling methods to be called from JavaScript with appropriate permissions.</summary>
        public static void Register()
        {
            // Register API calls with permissions
            API.RegisterAPICall(LLMAPICalls.PhoneHomeAsync, true, MagicPromptPermissions.PermPhoneHome);
            API.RegisterAPICall(SaveConfigSettings.SaveSettingsAsync, false, MagicPromptPermissions.PermSaveConfig);
            API.RegisterAPICall(SaveConfigSettings.SaveApiKeyAsync, false, MagicPromptPermissions.PermSaveApiKey);
            API.RegisterAPICall(SaveConfigSettings.ReadConfigAsync, false, MagicPromptPermissions.PermReadConfig);
            API.RegisterAPICall(LLMAPICalls.GetModelsAsync, false, MagicPromptPermissions.PermGetModels);
            API.RegisterAPICall(LLMAPICalls.LoadModelAsync, true, MagicPromptPermissions.PermLoadModel);
        }

    /// <summary>Makes the JSON response into a structured object and extracts the message content based on the backend type.</summary>
    /// <returns>The rewritten prompt, or null if deserialization fails.</returns>
    public static async Task<string> DeserializeResponse(HttpResponseMessage response, string llmBackend)
        {
            try
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                Logs.Debug($"Response content: {responseContent}");
                JsonSerializerOptions jsonSerializerOptions = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                string messageContent = null;
                switch (llmBackend.ToLower())
                {
                    case "openai":
                        OpenAIResponse openAIResponse = JsonSerializer.Deserialize<OpenAIResponse>(responseContent, jsonSerializerOptions);
                        if (openAIResponse?.Choices != null && openAIResponse.Choices.Count > 0)
                        {
                            messageContent = openAIResponse.Choices[0].Message.Content;
                        }
                        else
                        {
                            Logs.Error("OpenAI response is null or has no choices.");
                            return null;
                        }
                        break;
                    case "anthropic":
                        AnthropicResponse anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseContent, jsonSerializerOptions);
                        if (anthropicResponse?.Content != null && anthropicResponse.Content.Length > 0)
                        {
                            messageContent = anthropicResponse.Content[0].Text;
                        }
                        else
                        {
                            Logs.Error("Anthropic response is null or has no content.");
                            return null;
                        }
                        break;
                    case "ollama":
                        OllamaResponse ollamaResponse = JsonSerializer.Deserialize<OllamaResponse>(responseContent, jsonSerializerOptions);
                        if (ollamaResponse?.Message != null)
                        {
                            messageContent = ollamaResponse.Message.Content;
                        }
                        else
                        {
                            Logs.Error("Message object is null in the Ollama response.");
                            return null;
                        }
                        break;
                    case "openaiapi":
                        OpenAIAPIResponse openAIAPIResponse = JsonSerializer.Deserialize<OpenAIAPIResponse>(responseContent, jsonSerializerOptions);
                        if (openAIAPIResponse?.Choices != null && openAIAPIResponse.Choices.Count > 0)
                        {
                            messageContent = openAIAPIResponse.Choices[0].Message.Content;
                        }
                        else
                        {
                            Logs.Error("OpenAI API response is null or has no choices.");
                            return null;
                        }
                        break;
                    case "openrouter":
                        OpenRouterResponse openRouterResponse = JsonSerializer.Deserialize<OpenRouterResponse>(responseContent, jsonSerializerOptions);
                        if (openRouterResponse?.Choices != null && openRouterResponse.Choices.Count > 0)
                        {
                            messageContent = openRouterResponse.Choices[0].Message.Content;
                        }
                        else
                        {
                            Logs.Error("OpenRouter response is null or has no choices.");
                            return null;
                        }
                        break;
                    default:
                        Logs.Error("Unsupported LLM backend.");
                        return null;
                }
                if (!string.IsNullOrEmpty(messageContent) && messageContent.StartsWith("AI: "))
                {
                    messageContent = messageContent[4..].TrimStart();
                }
                return messageContent;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error deserializing response: {ex.Message}");
                return null;
            }
        }

        /// <summary>Deserializes the API response into a list of models.</summary>
        /// <returns>A list of models or null if deserialization fails.</returns>
        public static List<ModelData> DeserializeModels(string responseContent, string backendType)
        {
            try
            {
                switch (backendType.ToLower())
                {
                    case "ollama":
                        RootObject rootObject = Newtonsoft.Json.JsonConvert.DeserializeObject<RootObject>(responseContent);
                        if (rootObject?.Data != null)
                        {
                            return rootObject.Data;
                        }
                        else
                        {
                            Logs.Error("Data array is null or empty.");
                            return null;
                        }
                    case "openai":
                        OpenAIResponse openAIResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenAIResponse>(responseContent);
                        if (openAIResponse?.Data != null)
                        {
                            return openAIResponse.Data.Select(x => new ModelData
                            {
                                Model = x.Id,
                                Name = x.Id
                            }).ToList();
                        }
                        else
                        {
                            Logs.Error("Data array is null or empty.");
                            return null;
                        }
                    case "anthropic":
                        AnthropicResponse anthropicResponse = null;
                        return anthropicResponse.Data.Select(x => new ModelData
                        {
                            Model = x.Id,
                            Name = x.Id
                        }).ToList();
                    case "openaiapi":
                        OpenAIAPIResponse openAIAPIResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenAIAPIResponse>(responseContent);
                        if (openAIAPIResponse?.Data != null)
                        {
                            return openAIAPIResponse.Data.Select(x => new ModelData
                            {
                                Model = x.Id,
                                Name = x.Id
                            }).ToList();
                        }
                        else
                        {
                            Logs.Error("Data array is null or empty in OpenAI API response.");
                            return null;
                        }
                    case "openrouter":
                        Logs.Debug($"Raw OpenRouter Response: {responseContent}");
                        try
                        {
                            var errorResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
                            if (errorResponse?.Error != null)
                            {
                                Logs.Error($"OpenRouter API error: {errorResponse.Error.Message}");
                                throw new Exception($"OpenRouter API error: {errorResponse.Error.Message}");
                            }
                            OpenRouterResponse openRouterResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenRouterResponse>(responseContent);
                            if (openRouterResponse?.Data != null)
                            {
                                return openRouterResponse.Data.Select(x => new ModelData
                                {
                                    Model = x.Id,
                                    Name = x.Name ?? x.Id
                                }).ToList();
                            }
                            Logs.Error("OpenRouter response contains no model data");
                            return null;
                        }
                        catch (Exception ex)
                        {
                            Logs.Error($"Error parsing OpenRouter response: {ex.Message}");
                            throw;
                        }
                    default:
                        Logs.Error("Unsupported backend type.");
                        return null;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error deserializing models: {ex.Message}");
                return null;
            }
        }

        /// <summary>Creates a JSON object for a success, includes models and config data.</summary>
        /// <returns>The success bool, models, and config data to the JavaScript function that called it.</returns>
        public static JObject CreateSuccessResponse(string response, List<ModelData> models = null, ConfigData configData = null)
        {
            return new JObject
            {
                ["success"] = true,
                ["response"] = response,
                ["models"] = models != null ? JArray.FromObject(models) : null,
                ["config"] = configData != null ? JObject.FromObject(configData) : null,
                ["error"] = null
            };
        }

        /// <summary>Creates a JSON object for a failure, includes the error message.</summary>
        /// <returns>The success bool and the error to the JavaScript function that called it.</returns>
        public static JObject CreateErrorResponse(string errorMessage)
        {
            return new JObject
            {
                { "success", false },
                { "error", errorMessage }
            };
        }
    }
}
