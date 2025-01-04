using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using SwarmUI.Accounts;
using System.Text.Json;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using System.Net.Http;
using Newtonsoft.Json;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    // Define a permission group specifically for MagicPromptAPI
    public static class MagicPromptPermissions
    {
        public static readonly PermInfoGroup MagicPromptPermGroup = new("MagicPrompt", "Permissions related to MagicPrompt functionality for API calls and settings.");
        public static readonly PermInfo PermPhoneHome = Permissions.Register(new("magicprompt_phone_home", "Phone Home", "Allows the extension to make outbound calls to retrieve external data.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermSaveConfig = Permissions.Register(new("magicprompt_save_config", "Save Configuration", "Allows the user to save configuration settings.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermReadConfig = Permissions.Register(new("magicprompt_read_config", "Read Configuration", "Allows the user to read configuration settings.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermGetModels = Permissions.Register(new("magicprompt_get_models", "Get Models", "Allows the user to retrieve the list of available models.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
        public static readonly PermInfo PermResetConfig = Permissions.Register(new("magicprompt_reset_config", "Reset Configuration", "Allows the user to reset configuration settings.", PermissionDefault.POWERUSERS, MagicPromptPermGroup));
    }

    [API.APIClass("API routes related to MagicPromptExtension extension")]
    public class MagicPromptAPI
    {
        /// <summary>Registers the API calls for the extension, enabling methods to be called from JavaScript with appropriate permissions.</summary>
        public static void Register()
        {
            // Register API calls with permissions
            API.RegisterAPICall(LLMAPICalls.PhoneHomeAsync, true, MagicPromptPermissions.PermPhoneHome);
            API.RegisterAPICall(SessionSettings.GetSettingsAsync, false, MagicPromptPermissions.PermReadConfig);
            API.RegisterAPICall(SessionSettings.SaveSettingsAsync, false, MagicPromptPermissions.PermSaveConfig);
            API.RegisterAPICall(SessionSettings.ResetSettingsAsync, false, MagicPromptPermissions.PermResetConfig);
            API.RegisterAPICall(LLMAPICalls.GetModelsAsync, false, MagicPromptPermissions.PermGetModels);
        }

        /// <summary>Makes the JSON response into a structured object and extracts the message content based on the backend type.</summary>
        /// <returns>The rewritten prompt, or null if deserialization fails.</returns>
        public static async Task<string> DeserializeResponse(HttpResponseMessage response, string llmBackend)
        {
            try
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                JsonSerializerOptions jsonSerializerOptions = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                string messageContent = null;
                switch (llmBackend.ToLower())
                {
                    case "openai":
                        OpenAIResponse openAIResponse = System.Text.Json.JsonSerializer.Deserialize<OpenAIResponse>(responseContent, jsonSerializerOptions);
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
                        AnthropicResponse anthropicResponse = System.Text.Json.JsonSerializer.Deserialize<AnthropicResponse>(responseContent, jsonSerializerOptions);
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
                        OllamaResponse ollamaResponse = System.Text.Json.JsonSerializer.Deserialize<OllamaResponse>(responseContent, jsonSerializerOptions);
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
                        OpenAIAPIResponse openAIAPIResponse = System.Text.Json.JsonSerializer.Deserialize<OpenAIAPIResponse>(responseContent, jsonSerializerOptions);
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
                        // First check for error response
                        try 
                        {
                            OpenRouterError errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
                            if (errorResponse?.Error != null)
                            {
                                string errorMessage = errorResponse.Error.Message;
                                if (errorResponse.Error.Metadata != null)
                                {
                                    // For maintenance errors, show the detailed message
                                    if (errorResponse.Error.Metadata.Raw?.Contains("maintenance") == true)
                                    {
                                        errorMessage = errorResponse.Error.Metadata.Raw;
                                    }
                                    // For other errors, just show a clean user-friendly message
                                    else if (errorMessage == "Invalid URL")
                                    {
                                        errorMessage = "The provided image appears to be invalid or corrupted. Please try uploading a different image.";
                                    }
                                    else if (!string.IsNullOrEmpty(errorResponse.Error.Metadata.Raw))
                                    {
                                        errorMessage = $"Error from {errorResponse.Error.Metadata.ProviderName}: {errorMessage}";
                                    }
                                }
                                Logs.Error($"OpenRouter API error: {errorMessage}");
                                return null;
                            }
                        }
                        catch
                        {
                            // Not an error response, continue with normal processing
                        }
                        OpenRouterResponse openRouterResponse = JsonConvert.DeserializeObject<OpenRouterResponse>(responseContent);
                        if (openRouterResponse?.Choices != null && openRouterResponse.Choices.Count > 0)
                        {
                            OpenRouterMessage message = openRouterResponse.Choices[0].Message;
                            if (message != null)
                            {
                                // Handle both string and object content
                                messageContent = message.Content?.ToString();
                                if (string.IsNullOrEmpty(messageContent) && message.Content != null)
                                {
                                    // Try to extract content from a structured response
                                    try
                                    {
                                        Dictionary<string, string> contentObj = JsonConvert.DeserializeObject<Dictionary<string, string>>(message.Content.ToString());
                                        if (contentObj != null && contentObj.TryGetValue("text", out string value))
                                        {
                                            messageContent = value;
                                        }
                                    }
                                    catch
                                    {
                                        // If we can't parse as JSON, use the content as is
                                        messageContent = message.Content.ToString();
                                    }
                                }
                            }
                        }
                        if (string.IsNullOrEmpty(messageContent))
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
        public static List<ModelData> DeserializeModels(string responseContent, string backend)
        {
            try
            {
                switch (backend)
                {
                    case "ollama":
                        RootObject rootObject = JsonConvert.DeserializeObject<RootObject>(responseContent);
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
                        OpenAIResponse openAIResponse = JsonConvert.DeserializeObject<OpenAIResponse>(responseContent);
                        if (openAIResponse?.Data != null)
                        {
                            return openAIResponse.Data.Select(
                                x => new ModelData
                                {
                                    Model = x.Id,
                                    Name = x.Id
                                }).ToList();
                        }
                        Logs.Error("OpenAI response data is null");
                        return null;
                    case "anthropic":
                        AnthropicResponse anthropicResponse = null;
                        return anthropicResponse.Data.Select(x => new ModelData
                        {
                            Model = x.Id,
                            Name = x.Id
                        }).ToList();
                    case "openaiapi":
                        OpenAIAPIResponse openAIAPIResponse = JsonConvert.DeserializeObject<OpenAIAPIResponse>(responseContent);
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
                        try
                        {
                            OpenRouterError errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
                            if (errorResponse?.Error != null)
                            {
                                Logs.Error($"OpenRouter API error: {errorResponse.Error.Message}");
                                throw new Exception($"OpenRouter API error: {errorResponse.Error.Message}");
                            }
                            OpenRouterResponse openRouterResponse = JsonConvert.DeserializeObject<OpenRouterResponse>(responseContent);
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
        public static JObject CreateSuccessResponse(string response, List<ModelData> models = null, JObject settings = null)
        {
            return new JObject
            {
                ["success"] = true,
                ["response"] = response,
                ["models"] = models != null ? JArray.FromObject(models) : null,
                ["settings"] = settings,
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
