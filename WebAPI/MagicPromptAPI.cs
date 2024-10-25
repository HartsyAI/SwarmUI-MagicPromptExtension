using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using System.Text.Json;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Config;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    [API.APIClass("API routes related to MagicPromptExtension extension")]
    public class MagicPromptAPI
    {
        /// <summary>Registers the API call for the extension, enabling methods to be called from JavaScript.</summary>
        public static void Register()
        {
            API.RegisterAPICall(LLMAPICalls.PhoneHomeAsync, true);
            API.RegisterAPICall(SaveConfigSettings.SaveSettingsAsync);
            API.RegisterAPICall(SaveConfigSettings.SaveApiKeyAsync);
            API.RegisterAPICall(SaveConfigSettings.ReadConfigAsync);
            API.RegisterAPICall(LLMAPICalls.GetModelsAsync);
            API.RegisterAPICall(LLMAPICalls.LoadModelAsync);
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
