using Hartsy.Extensions.MagicPromptExtension.WebAPI.Config;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Newtonsoft.Json.Linq;
using System.Text.Json;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    public class LLMAPICalls : MagicPromptAPI
    {
        private static readonly HttpClient _httpClient = new();

        /// <summary>Fetches available models from the LLM API endpoint.</summary>
        /// <returns>A JSON object containing the models or an error message.</returns>
        public static async Task<JObject> GetModelsAsync()
        {
            try
            {
                Logs.Info("Fetching available models for Hartsy's MagicPrompt Extension...");
                if (GlobalConfig.ConfigData == null)
                {
                    await SaveConfigSettings.ReadConfigAsync();
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                if (configDataObj == null || string.IsNullOrEmpty(configDataObj.LLMBackend))
                {
                    return CreateErrorResponse("No LLM backend configured or installed. Please:\n" + "1. After you have installed your backend. Open the extension settings\n" +
                        "2. Select and configure a backend (Ollama, OpenAI, etc.)\n" + "3. Save your settings\n" +
                        "If this is your first time using MagicPrompt Make sure you have installed Ollams or a compatable LLM backecd. This is not done for you automatically." +
                        " For install and setup instructions, " + "visit:\nhttps://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                    );
                }
                Logs.Debug($"LLMBackend: {configDataObj.LLMBackend}");
                string llmEndpoint = ConfigUtil.GetLlmEndpoint(configDataObj.LLMBackend, "models");
                Logs.Debug($"LLM Endpoint: {llmEndpoint}");
                // Create the GET request for the API call
                HttpRequestMessage request = new(HttpMethod.Get, llmEndpoint);
                switch (configDataObj.LLMBackend.ToLower())
                {
                    case "openai":
                        string apiKey = configDataObj.Backends.OpenAI.ApiKey;
                        if (string.IsNullOrEmpty(apiKey))
                        {
                            string error ="OpenAI API Key not found. To configure:\n" +
                                "1. Get your API key from OpenAI\n" + "2. Add it in the extension settings\n" +
                                "3. Save your changes\n\n" +
                                "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                            Logs.Error(error);
                            return CreateErrorResponse(error);
                        }
                        request.Headers.Add("Authorization", $"Bearer {apiKey}");
                        Logs.Debug("Added OpenAI API key to the request headers.");
                        break;
                    case "openaiapi":
                        Logs.Debug("Using openaiapi as LLM backend.");
                        break;
                    case "ollama":
                        Logs.Debug("Using Ollama as LLM backend.");
                        break;
                    case "anthropic":
                        Logs.Debug("Using Anthropic as LLM backend.");
                        List<ModelData> models =
                        [
                            new ModelData
                            {
                                Name = "Claude 3.5 Sonnet",
                                Version = "20241022",
                                Model = "claude-3-5-sonnet-20241022",
                            },
                            new ModelData
                            {
                                Name = "Claude 3 Opus",
                                Version = "20240229",
                                Model = "claude-3-opus-20240229",
                            },
                            new ModelData
                            {
                                Name = "Claude 3 Sonnet",
                                Version = "20240229",
                                Model = "claude-3-sonnet-20240229",
                            },
                            new ModelData
                            {
                                Name = "Claude 3 Haiku",
                                Version = "20240307",
                                Model = "claude-3-haiku-20240229",
                            }
                        ];
                        return CreateSuccessResponse(null, models, configDataObj);
                    default:
                        string unsupportedError =$"Unsupported LLM backend: {configDataObj.LLMBackend}\n" +
                            "Please select one of the supported backends:\n" + "- Ollama (recommended for local use)\n" +
                            "- OpenAI\n" + "- Anthropic\n\n" + "For backend setup guides, " +
                            "visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        Logs.Error(unsupportedError);
                        return CreateErrorResponse(unsupportedError);
                }
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                Logs.Debug($"Response from LLM API: {response}");
                Logs.Debug($"Response Content: {response.Content}");
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    List<ModelData> models = DeserializeModels(jsonString, configDataObj.LLMBackend);

                    return models != null
                        ? CreateSuccessResponse(null, models, configDataObj)
                        : CreateErrorResponse(
                            "Unable to load available models. Please ensure:\n" + "1. Your LLM backend is running\n" +
                            "2. You have models installed\n" + "3. Your network connection is stable\n\n" +
                            "For troubleshooting steps, visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                        );
                }
                else
                {
                    string error =$"Failed to fetch models: {response.StatusCode} - {response.ReasonPhrase}\n" +
                        "This usually means:\n" + "1. Your LLM backend is not running\n" +
                        "2. Network connectivity issues\n" + "3. Invalid API credentials\n\n" +
                        "Check our setup guide: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                    Logs.Error(error);
                    return CreateErrorResponse(error);
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception occurred: {ex.Message}");
                return CreateErrorResponse("Unexpected error while fetching models:\n" +
                    $"{ex.Message}\n\n" + "Common solutions:\n" + "1. Restart your LLM backend\n" +
                    "2. Check your system resources\n" + "3. Verify your configuration\n\n" +
                    "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
            }
        }

        /// <summary>Loads the specified model from the LLM backend.</summary>
        /// <param name="modelId">The ID of the model to load.</param>
        /// <returns>A JSON object indicating success or error.</returns>
        public static async Task<JObject> LoadModelAsync(
            [API.APIParameter("Model ID")] string modelId)
        {
            ConfigData configDataObj = GlobalConfig.ConfigData;
            if (configDataObj == null)
            {
                await SaveConfigSettings.ReadConfigAsync();
                configDataObj = GlobalConfig.ConfigData; // Refresh after reading
            }
            configDataObj.Model = modelId;
            ConfigUtil.UpdateConfig(configDataObj);
            string endpoint;
            switch (configDataObj.LLMBackend)
            {
                case "anthropic":
                case "openai":
                    return CreateSuccessResponse("Skip Model Preload: Not needed for 3rd party APIs.");
                case "openaiapi":
                    endpoint = configDataObj.Backends.OpenAIAPI.BaseUrl;
                    break;
                case "ollama":
                    endpoint = configDataObj.LlmEndpoint + configDataObj.Backends.Ollama.Endpoints["load"];
                    break;
                default:
                    Logs.Error($"Unsupported backend type in LoadModels: {configDataObj.LLMBackend}");
                    return CreateErrorResponse($"Unsupported LLM backend: {configDataObj.LLMBackend}\n" +
                        "Please configure a supported backend:\n" + "- Ollama (recommended for local use)\n" + "- OpenAI\n" +
                        "- Anthropic\n\n" + "Setup instructions: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                    );
            }
            var requestBody = new
            {
                model = modelId,
            };
            try
            {
                string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                StringContent httpContent = new(jsonRequestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, httpContent);
                if (response.IsSuccessStatusCode)
                {
                    Logs.Info($"Successfully loaded model: {modelId}");
                    configDataObj.Model = modelId; // Update the model in config
                    ConfigUtil.UpdateConfig(configDataObj); // Save the updated config
                    return CreateSuccessResponse($"Successfully loaded model: {modelId}", null, configDataObj);
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Logs.Error($"Failed to load model {modelId}: {response.StatusCode} - {response.ReasonPhrase}. Response: {errorMessage}");
                    return CreateErrorResponse($"Failed to load model '{modelId}'. Please ensure:\n" +
                        "1. The model name is correct\n" + "2. You have sufficient system resources\n" +
                        "3. The model is downloaded (for Ollama)\n" + $"4. Check the error: {response.StatusCode} - {errorMessage}\n\n" +
                        "For help, visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                    );
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error loading model {modelId}: {ex.Message}");
                return CreateErrorResponse($"Error loading model '{modelId}':\n" + $"{ex.Message}\n\n" +
                    "Common solutions:\n" + "1. Verify your LLM backend is running\n" +
                    "2. Check your network connection\n" + "3. Ensure the model is available\n\n" +
                    "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
            }
        }

        /// <summary>Unloads the specified model in Ollama by setting keep_alive to 0.</summary>
        /// <param name="modelId">The ID of the model to unload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private static async Task UnloadModelAsync(
        [API.APIParameter("Model ID")] string modelId)
            // TODO: Not currently implemented
        {
            string unloadEndpoint = "http://localhost:11434/api/generate";
            var requestBody = new
            {
                model = modelId,
                keep_alive = 0
            };
            try
            {
                string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                StringContent httpContent = new(jsonRequestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(unloadEndpoint, httpContent);

                if (response.IsSuccessStatusCode)
                {
                    Logs.Info($"Model {modelId} unloaded successfully.");
                }
                else
                {
                    Logs.Error($"Failed to unload model {modelId}. Status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error unloading model {modelId}: {ex.Message}");
            }
        }

        /// <summary>Sends the prompt to the LLM API and processes the response.</summary>
        /// <returns>Returns a JSON object with success and a rewritten prompt or an error.</returns>
        [API.APIDescription("Returns a JSON object containing the response from the language model API or an error message.",
            """
            {
                "success": true,
                "response": string,
                "error": string
            }
            """)]
        public static async Task<JObject> PhoneHomeAsync(
        [API.APIParameter("Text input")] string inputText,
        [API.APIParameter("Model ID")] string modelId)
        {
            try
            {
                if (GlobalConfig.ConfigData == null)
                {
                    ConfigUtil.ReadConfig();
                    if (GlobalConfig.ConfigData == null)
                    {
                        return CreateErrorResponse("Configuration not found. Please:\n" +
                            "1. Verify you installed Ollama or other compatable backend (That is not done for you)\n" + "2. Check that you have properly saved your settings\n" +
                            "3. Try restarting Swarm\n\n" + "Setup guide: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                        );
                    }
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                // Split the config into seperate variables. This can probably be cleaned up.
                string llmBackend = configDataObj.LLMBackend;
                configDataObj.Model = modelId;
                string llmEndpoint = ConfigUtil.GetLlmEndpoint(llmBackend, "chat");
                string instructions = configDataObj.Instructions;
                bool unloadModel = configDataObj.UnloadModel;
                inputText = $"{instructions} User: {inputText}"; // Add instructions to the prompt
                Logs.Verbose($"Sending prompt to LLM API: {inputText}");
                Logs.Verbose($"ConfigData.Model: {configDataObj.Model}");
                Logs.Verbose($"ConfigData.LLMBackend: {configDataObj.LLMBackend}");
                object requestBody = BackendSchema.GetSchemaType(llmBackend, inputText, configDataObj.Model);
                HttpRequestMessage request = new(HttpMethod.Post, llmEndpoint);
                if (llmBackend.Equals("openai", StringComparison.CurrentCultureIgnoreCase))
                {
                    string apiKey = configDataObj.Backends.OpenAI.ApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        string error ="OpenAI API Key required. To set up:\n" +
                            "1. Get your API key from OpenAI\n" + "2. Add it in the extension settings\n" +
                            "3. Save your changes\n\n" + "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        Logs.Error(error);
                        return CreateErrorResponse(error);
                    }
                    // Add the API key to the Authorization header
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    Logs.Debug("Added OpenAI API key to the request headers.");
                }
                if (llmBackend.Equals("anthropic", StringComparison.CurrentCultureIgnoreCase))
                {
                    string apiKey = configDataObj.Backends.Anthropic.ApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        string error = "Anthropic API Key required. To set up:\n" + "1. Get your API key from Anthropic\n" +
                            "2. Add it in the extension settings\n" + "3. Save your changes\n\n" +
                            "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        Logs.Error(error);
                        return CreateErrorResponse(error);
                    }
                    // Add the API key to the Authorization header
                    request.Headers.Add("x-api-key", apiKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    Logs.Debug("Added Anthropic API key and version to the request headers.");
                }
                request.Headers.Add("Accept", "application/json");
                string jsonBody = JsonSerializer.Serialize(requestBody);
                request.Content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                Logs.Debug("=== Full Request Details ===");
                Logs.Debug($"URL: {request.RequestUri}");
                Logs.Debug("Headers:");
                foreach (var header in request.Headers)
                {
                    Logs.Debug($"  {header.Key}: {string.Join(", ", header.Value)}");
                }
                Logs.Debug("Content Headers:");
                foreach (var header in request.Content.Headers)
                {
                    Logs.Debug($"  {header.Key}: {string.Join(", ", header.Value)}");
                }
                Logs.Debug("Request Body:");
                Logs.Debug(jsonBody);  // Print the actual JSON string
                Logs.Debug("========================");
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                //Logs.Debug($"Response From LLM Backend: {response}"); // Debug
                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response, llmBackend);
                    return !string.IsNullOrEmpty(llmResponse)
                        ? CreateSuccessResponse(llmResponse)
                        : CreateErrorResponse("Unexpected API response format or empty response.");
                }
                else
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    Logs.Error($"API request failed with status code: {response.StatusCode}");
                    Logs.Error($"Error details: {errorContent}");
                    return CreateErrorResponse($"API request failed with status code: {response.StatusCode}. Details: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error(ex.Message);
                return CreateErrorResponse(
                    $"Unexpected error: {ex.Message}\n\n" + "Please try:\n" + "1. Restarting your LLM backend\n" +
                    "2. Open the config.json and confirm you settings were saved and the URL is correct\n" +
                    "3. Verify You actually installed Ollama or a compatable backend (This is not done automatically for you)\n\n" +
                    "For help: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
            }
        }
    }
}
