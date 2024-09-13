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
            API.RegisterAPICall(SaveSettingsAsync);
            API.RegisterAPICall(SaveApiKeyAsync);
            API.RegisterAPICall(ReadConfigAsync);
            API.RegisterAPICall(GetModelsAsync);
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
        [API.APIParameter("Selected Backend")] string selectedBackend,
        [API.APIParameter("Model Unload Option")] bool modelUnload,
        [API.APIParameter("API URL")] string apiUrl)
        {
            try
            {
                Dictionary<string, string> updates = new()
                {
                    { "LLMBackend", selectedBackend },
                    { "UnloadModel", modelUnload.ToString() },
                    { "LlmEndpoint", apiUrl }
                };
                ConfigUtil.UpdateConfig(updates);
                Logs.Info("LLM settings saved successfully.");
                return CreateSuccessResponse("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error saving settings: {ex.Message}");
                return CreateErrorResponse($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>Reads the configuration file asynchronously.</summary>
        /// <returns>A JSON object with the configuration data or an error message.</returns>
        public static async Task<JObject> ReadConfigAsync()
        {
            try
            {
                // Check if GlobalConfig is already loaded in memory
                if (GlobalConfig.ConfigData == null)
                {
                    ConfigUtil.ReadConfig();
                    if (GlobalConfig.ConfigData == null)
                    {
                        return CreateErrorResponse("Failed to load configuration from file.");
                    }
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                return CreateSuccessResponse("success"); // TODO: What do we need to do with the config data now that we have it?
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception occurred while reading config: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        /// <summary>Fetches available models from the LLM API endpoint.</summary>
        /// <returns>A JSON object containing the models or an error message.</returns>
        public static async Task<JObject> GetModelsAsync()
        {
            try
            {
                Logs.Info("Fetching available models for the MagicPrompt Extension...");
                if (GlobalConfig.ConfigData == null)
                {
                    await ReadConfigAsync();
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                string llmEndpoint = configDataObj.LlmEndpoint + "/api/tags"; // TODO: Remove Ollama hardcoding
                HttpResponseMessage response = await _httpClient.GetAsync(llmEndpoint);
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    List<ModelData> models = DeserializeModels(jsonString);
                    return models != null
                    ? CreateSuccessResponse(null, models, configDataObj)
                    : CreateErrorResponse("Failed to parse models from response.");
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
    [API.APIParameter("Model ID")] string modelId)
        {
            try
            {
                if (GlobalConfig.ConfigData == null)
                {
                    ConfigUtil.ReadConfig();
                    if (GlobalConfig.ConfigData == null)
                    {
                        return CreateErrorResponse("Failed to load configuration from file.");
                    }
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                // Split the config into seperate variables. This can probably be cleaned up.
                string model = configDataObj.Model;
                string llmEndpoint = configDataObj.LlmEndpoint + "/api/chat"; // TODO: Remove hardcoding
                string instructions = configDataObj.Instructions;
                bool unloadModel = configDataObj.UnloadModel;
                string openAIKey = configDataObj.OpenAIKey;
                string claudeAPIKey = configDataObj.ClaudeAPIKey;
                inputText = $"{instructions} User: {inputText}"; // Add instructions to the prompt
                Logs.Verbose($"Sending prompt to LLM API: {inputText}");
                object requestBody = BackendSchema.GetSchemaType(model); // Dynamically get the schema type based on the model
                HttpResponseMessage response = await SendRequest(llmEndpoint, requestBody);
                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response);
                    return !string.IsNullOrEmpty(llmResponse)
                        ? CreateSuccessResponse(llmResponse)
                        : CreateErrorResponse("Unexpected API response format or empty response.");
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

        /// <summary>Creates a JSON object for a success, includes models and config data.</summary>
        /// <returns>The success bool, models, and config data to the JavaScript function that called it.</returns>
        private static JObject CreateSuccessResponse(string response, List<ModelData> models = null, ConfigData configData = null)
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
