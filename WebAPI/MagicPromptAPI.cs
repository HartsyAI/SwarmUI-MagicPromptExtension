using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using SwarmUI.Extensions.SwarmUI_MagicPromptExtension.WebAPI;

namespace hartsy.Extensions.MagicPromptExtension.WebAPI
{
    [API.APIClass("API routes related to MagicPromptExtension extension")]
    public static class MagicPromptAPI
    {
        /// <summary>Registers the API call for the extension, enabling methods to be called from JavaScript.</summary>
        public static void Register()
        {
            API.RegisterAPICall(PhoneHomeAsync, true);
            API.RegisterAPICall(GetAvailableModelsAsync);
            API.RegisterAPICall(SaveSettingsAsync);
            API.RegisterAPICall(SaveApiKeyAsync);
        }

        private static readonly HttpClient _httpClient = new();

        /// <summary>Saves a new API key to the config.json file.</summary>
        /// <param name="apiKey">The new API key entered by the user.</param>
        /// <param name="apiProvider">The API provider (e.g., OpenAI, Claude).</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        public static async Task<JObject> SaveApiKeyAsync(
        [API.APIParameter("API Key")] string apiKey,
        [API.APIParameter("API Provider")] string apiProvider)
        {
            try
            {
                Dictionary<string, string> updates = [];
                if (apiProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    updates["OpenAIKey"] = apiKey;
                }
                else if (apiProvider.Equals("Claude", StringComparison.OrdinalIgnoreCase))
                {
                    updates["ClaudeAPIKey"] = apiKey;
                }
                else
                {
                    return CreateErrorResponse($"Unknown API provider: {apiProvider}");
                }
                ConfigUtil.UpdateConfig(updates);
                Logs.Info($"API key for {apiProvider} saved successfully.");
                return CreateSuccessResponse($"API key for {apiProvider} saved successfully.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error saving API key: {ex.Message}");
                return CreateErrorResponse($"Error saving API key: {ex.Message}");
            }
        }

        /// <summary>Saves user settings to the setup.json file.</summary>
        /// <param name="selectedModel">The selected model.</param>
        /// <param name="modelUnload">Whether to unload the model after use.</param>
        /// <param name="apiUrl">The API URL.</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        public static async Task<JObject> SaveSettingsAsync(
            [API.APIParameter("Selected Model")] string selectedLLM,
            [API.APIParameter("Model Unload Option")] bool modelUnload,
            [API.APIParameter("API URL")] string apiUrl)
        {
            try
            {
                string configFilePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, 
                    "src/Extensions/SwarmUI-MagicPromptExtension", "config.json");
                // Read the current config file and update the LLM backend, model unload option, and LLM endpoint
                string configContent = await File.ReadAllTextAsync(configFilePath);
                ConfigData configData = JsonSerializer.Deserialize<ConfigData>(configContent);
                configData.LLMBackend = selectedLLM;
                configData.UnloadModel = modelUnload;
                configData.LlmEndpoint = apiUrl;
                string updatedConfigContent = JsonSerializer.Serialize(configData, new JsonSerializerOptions { WriteIndented = true });
                await File.WriteAllTextAsync(configFilePath, updatedConfigContent);
                Logs.Info("LLM settings saved successfully.");
                return CreateSuccessResponse("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error saving settings: {ex.Message}");
                return CreateErrorResponse($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>Fetches available models from the LLM API endpoint.</summary>
        /// <returns>A JSON object containing the models or an error message.</returns>
        public static async Task<JObject> GetAvailableModelsAsync()
        {
            try
            {
                Logs.Info("Fetching available models for the MagicPrompt Extension...");
                var (instructions, llmEndpoint) = await LoadConfigData();
                llmEndpoint += "/api/tags";
                HttpResponseMessage response = await _httpClient.GetAsync(llmEndpoint);
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    List<ModelData> models = DeserializeModels(jsonString);
                    if (models != null)
                    {
                        return CreateSuccessResponse(null, models);
                    }
                    else
                    {
                        string error = "Failed to parse models from response.";
                        Logs.Error(error);
                        return CreateErrorResponse(error);
                    }
                }
                else
                {
                    string error = $"Error fetching models: {response.StatusCode} - {response.ReasonPhrase}";
                    Logs.Error(error);
                    return CreateErrorResponse(error);
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception occurred: {ex.Message}");
                return CreateErrorResponse(ex.Message);
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
            [API.APIParameter("Model ID")]string modelId)
        {
            try
            {
                var (instructions, llmEndpoint) = await LoadConfigData();
                if (instructions == null || llmEndpoint == null)
                {
                    return CreateErrorResponse("Failed to load configuration.");
                }
                llmEndpoint += "/api/chat";
                inputText = $"{instructions} User: {inputText}";
                Logs.Verbose($"Sending prompt to LLM API: {inputText}");
                object requestBody = CreateRequestBody(inputText, modelId);
                HttpResponseMessage response = await SendRequest(llmEndpoint, requestBody);
                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        // Remove the "AI: " prefix if it exists
                        string cleanedResponse = llmResponse.StartsWith("AI: ") ? llmResponse[4..] : llmResponse;
                        return CreateSuccessResponse(cleanedResponse);
                    }
                    else
                    {
                        return CreateErrorResponse("Unexpected API response format or empty response.");
                    }
                }
                else
                {
                    return CreateErrorResponse($"API request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error(ex.Message);
                return CreateErrorResponse(ex.Message);
            }
        }

        /// <summary>Loads the setup.json file, extracts instructions and the API endpoint, handles errors if they occur.</summary>
        /// <returns>Tuple containing the instructions and API endpoint, or null values if loading fails.</returns>
        private static async Task<(string instructions, string llmEndpoint)> LoadConfigData()
        {
            string configFilePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, "src/Extensions/SwarmUI-MagicPromptExtension", "config.json");
            try
            {
                string configContent = await File.ReadAllTextAsync(configFilePath);
                ConfigData configData = JsonSerializer.Deserialize<ConfigData>(configContent);
                if (configData == null)
                {
                    Logs.Error("Failed to deserialize config data.");
                    return (null, null);
                }
                return (configData.Instructions, configData.LlmEndpoint);
            }
            catch (Exception ex)
            {
                Logs.Error($"Error reading or deserializing config file: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>Chooses the body for the LLM API request. Currently only supports Ollama.</summary>
        /// <returns>An object representing the JSON structure for the API request.</returns>
        private static object CreateRequestBody(string inputText, string currentModel)
        {
            return new
            {
                model = currentModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = inputText
                    }
                },
                stream = false
            };
        }

        /// <summary>Sends a POST request to the LLM API endpoint with the chosen request body. The body should match what LLM backend you are using.</summary>
        /// <returns>HttpResponseMessage received from the API.</returns>
        private static async Task<HttpResponseMessage> SendRequest(string endpoint, object requestBody)
        {
            try
            {
                string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                StringContent httpContent = new(jsonRequestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, httpContent);
                return response;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error sending request: {ex.Message}");
                throw;
            }
        }

        /// <summary>Makes the JSON response into a structured object and extracts the Message.Content.</summary>
        /// <returns>The rewritten prompt, or null if deserialization fails.</returns>
        private static async Task<string> DeserializeResponse(HttpResponseMessage response)
        {
            try
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                JsonSerializerOptions jsonSerializerOptions = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                LLMOllamaResponse llmResponse = JsonSerializer.Deserialize<LLMOllamaResponse>(responseContent, jsonSerializerOptions);
                if (llmResponse?.Message != null)
                {
                    return llmResponse.Message.Content;
                }
                else
                {
                    Logs.Error("Message object is null in the response.");
                }
                return null;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error deserializing response: {ex.Message}");
                return null;
            }
        }

        /// <summary>Deserializes the API response into a list of models.</summary>
        /// <returns>A list of models or null if deserialization fails.</returns>
        private static List<ModelData> DeserializeModels(string responseContent)
        {
            try
            {
                RootObject rootObject = Newtonsoft.Json.JsonConvert.DeserializeObject<RootObject>(responseContent);
                if (rootObject?.Data != null)
                {
                    return rootObject.Data;
                }
                Logs.Error("Data array is null or empty.");
                return null;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error deserializing models: {ex.Message}");
                return null;
            }
        }

        /// <summary>Creates a JSON object for a success, includes the rewritten prompt.</summary>
        /// <returns>The success bool and the rewritten prompt to the JavaScript function that called it.</returns>
        private static JObject CreateSuccessResponse(string response, List<ModelData> models = null)
        {
            return new JObject
            {
                ["success"] = true,
                ["response"] = response,
                ["models"] = models != null ? JArray.FromObject(models) : null,
                ["error"] = null
            };
        }

        /// <summary>Creates a JSON object for a failure, includes the error message.</summary>
        /// <returns>The success bool and the error to the JavaScript function that called it.</returns>
        private static JObject CreateErrorResponse(string errorMessage)
        {
            return new JObject
            {
                { "success", false },
                { "error", errorMessage }
            };
        }

        /// <summary>
        /// Unloads the specified model in Ollama by setting keep_alive to 0.
        /// </summary>
        /// <param name="modelId">The ID of the model to unload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        private static async Task UnloadModelAsync(string modelId)
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

    }
}
