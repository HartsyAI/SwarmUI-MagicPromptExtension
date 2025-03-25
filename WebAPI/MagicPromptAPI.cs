using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using SwarmUI.Accounts;
using System.Text.Json;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using System.Net.Http;
using Newtonsoft.Json;
using Microsoft.AspNetCore.Html;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI;

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
        API.RegisterAPICall(LLMAPICalls.GetModelsAsync, true, MagicPromptPermissions.PermGetModels);

        // All key types must be added to the accepted list first
        string[] keyTypes = ["openai_api", "anthropic_api", "openrouter_api", "openaiapi_local"]; 
        foreach (string keyType in keyTypes)
        {
            BasicAPIFeatures.AcceptedAPIKeyTypes.Add(keyType);
        }
        // Register API Key tables for each backend
        UserUpstreamApiKeys.Register(new("openai_api", "openai", "OpenAI (ChatGPT)", "https://platform.openai.com/api-keys", new HtmlString("To use OpenAI models in SwarmUI (via the MagicPrompt extension), you must set your OpenAI API key.")));
        UserUpstreamApiKeys.Register(new("anthropic_api", "anthropic", "Anthropic (Claude)", "https://console.anthropic.com/settings/keys", new HtmlString("To use Anthropic models like Claude in SwarmUI (via the MagicPrompt extension), you must set your Anthropic API key.")));
        UserUpstreamApiKeys.Register(new("openrouter_api", "openrouter", "OpenRouter", "https://openrouter.ai/keys", new HtmlString("To use OpenRouter models in SwarmUI (via the MagicPrompt extension), you must set your OpenRouter API key. OpenRouter gives you access to many different models through a single API.")));
        UserUpstreamApiKeys.Register(new("openaiapi_local", "openaiapi", "OpenAI API (Local)", "#", new HtmlString("For connecting to local servers that implement the OpenAI API schema (like LM Studio, text-generation-webui, or LocalAI). You may need to provide API keys or connection details depending on your local setup.")));
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
                        throw new InvalidOperationException("The response from OpenAI could not be processed (no choices found)");
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
                        throw new InvalidOperationException("The response from Anthropic could not be processed (no content found)");
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
                        throw new InvalidOperationException("The response from Ollama could not be processed (null message)");
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
                        throw new InvalidOperationException("The response from the OpenAI-compatible API could not be processed (no choices found)");
                    }
                    break;
                case "openrouter":
                    // First check for error response using the ErrorHandler
                    if (ErrorHandler.TryParseOpenRouterError(responseContent, out string openRouterErrorMessage))
                    {
                        throw new InvalidOperationException(openRouterErrorMessage);
                    }
                    OpenRouterResponse openRouterResponse = JsonConvert.DeserializeObject<OpenRouterResponse>(responseContent);
                    if (openRouterResponse?.Choices != null && openRouterResponse.Choices.Count > 0)
                    {
                        // Check if the response was cut off due to token limits
                        if (openRouterResponse.Choices[0].FinishReason == "length" || 
                            openRouterResponse.Choices[0].NativeFinishReason == "length")
                        {
                            Logs.Warning($"OpenRouter response was cut off due to token limit. Model: {openRouterResponse.Model}");
                            throw new InvalidOperationException(
                                ErrorHandler.FormatErrorMessage("token_limit", 
                                $"The response from {openRouterResponse.Model} was cut off because it reached the maximum allowed length.", 
                                "openrouter")
                            );
                        }
                        
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
                        throw new InvalidOperationException("The response from OpenRouter could not be processed (no choices found)");
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unsupported LLM backend.");
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
            
            // Check if the exception is related to quota issues
            if (ex.Message.Contains("insufficient_quota") || ex.Message.Contains("quota exceeded"))
            {
                throw new InvalidOperationException(
                    ErrorHandler.FormatErrorMessage("quota", ex.Message, llmBackend)
                );
            }
            
            // Check for token limit issues
            if (ex.Message.Contains("token") && (ex.Message.Contains("limit") || ex.Message.Contains("exceed")))
            {
                throw new InvalidOperationException(
                    ErrorHandler.FormatErrorMessage("token_limit", ex.Message, llmBackend)
                );
            }
            
            // For all other exceptions, use generic_exception handler
            throw new InvalidOperationException(
                ErrorHandler.FormatErrorMessage("generic_exception", ex.Message, llmBackend)
            );
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
                        throw new InvalidOperationException("Failed to retrieve models from Ollama. The response data was empty or invalid.");
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
                    else
                    {
                        Logs.Error("OpenAI models data array is null or empty.");
                        throw new InvalidOperationException("Failed to retrieve models from OpenAI. The response data was empty or invalid.");
                    }
                case "anthropic":
                    AnthropicResponse anthropicResponse = JsonConvert.DeserializeObject<AnthropicResponse>(responseContent);
                    if (anthropicResponse?.Data != null)
                    {
                        return anthropicResponse.Data.Select(x => new ModelData
                        {
                            Model = x.Id,
                            Name = GetFriendlyNameFromId(x.Id),
                            Version = ExtractVersionFromId(x.Id)
                        }).ToList();
                    }
                    else
                    {
                        Logs.Error("Anthropic models data array is null or empty.");
                        throw new InvalidOperationException("Failed to retrieve models from Anthropic. The response data was empty or invalid.");
                    }
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
                        throw new InvalidOperationException("Failed to retrieve models from OpenAI API. The response data was empty or invalid.");
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
                        throw new InvalidOperationException("Failed to retrieve models from OpenRouter. The response data was empty or invalid.");
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Error parsing OpenRouter response: {ex.Message}");
                        throw;
                    }
                default:
                    Logs.Error("Unsupported backend type.");
                    throw new InvalidOperationException("Unsupported backend type.");
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error deserializing models: {ex.Message}");
            
            // Check if the exception is related to quota issues
            if (ex.Message.Contains("insufficient_quota") || ex.Message.Contains("quota exceeded"))
            {
                throw new InvalidOperationException(
                    ErrorHandler.FormatErrorMessage("quota", ex.Message, backend)
                );
            }
            
            // For authentication errors
            if (ex.Message.Contains("unauthorized") || ex.Message.Contains("authentication"))
            {
                throw new InvalidOperationException(
                    ErrorHandler.FormatErrorMessage("authentication", ex.Message, backend)
                );
            }
            
            // For all other exceptions use a generic handler
            throw new InvalidOperationException(
                ErrorHandler.FormatErrorMessage("generic_exception", ex.Message, backend)
            );
        }
    }

    /// <summary>Extracts a version string from a model ID, if present</summary>
    private static string ExtractVersionFromId(string id)
    {
        var match = System.Text.RegularExpressions.Regex.Match(id, @"\d{8}$");
        return match.Success ? match.Value : "";
    }

    /// <summary>Creates a user-friendly name from a model ID</summary>
    private static string GetFriendlyNameFromId(string modelId)
    {
        // Extract the model name from ID patterns like "claude-3-opus-20240229"
        string baseName = modelId.Split('/').Last();
        
        // Remove version numbers and dates when possible
        var match = System.Text.RegularExpressions.Regex.Match(baseName, @"^(.*?)[-:]?\d{8}$");
        if (match.Success && match.Groups.Count > 1)
        {
            baseName = match.Groups[1].Value;
        }
        
        // Convert kebab-case to Title Case with proper spacing
        string[] parts = baseName.Split('-');
        for (int i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length > 0)
            {
                parts[i] = char.ToUpper(parts[i][0]) + parts[i].Substring(1);
            }
        }
        
        return string.Join(" ", parts);
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
