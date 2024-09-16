using Hartsy.Extensions.MagicPromptExtension.WebAPI;
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
                Logs.Info("Fetching available models for the MagicPrompt Extension...");
                if (GlobalConfig.ConfigData == null)
                {
                    await SaveConfigSettings.ReadConfigAsync();
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                Logs.Debug($"LLMBackend: {configDataObj.LLMBackend}");
                string llmEndpoint = ConfigUtil.GetLlmEndpoint(configDataObj.LLMBackend, "models");
                Logs.Debug($"LLM Endpoint: {llmEndpoint}");
                HttpRequestMessage request = new(HttpMethod.Get, llmEndpoint);
                if (configDataObj.LLMBackend.Equals("openai", StringComparison.CurrentCultureIgnoreCase))
                {
                    string apiKey = configDataObj.Backends.OpenAI.ApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        string error = "OpenAI API Key is missing from the configuration.";
                        Logs.Error(error);
                        return CreateErrorResponse(error);
                    }
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    Logs.Debug("Added OpenAI API key to the request headers.");
                }
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                Logs.Debug($"Response from LLM API: {response}");
                Logs.Debug($"Response Content: {response.Content}");
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    List<ModelData> models = DeserializeModels(jsonString, configDataObj.LLMBackend); return models != null
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

        public static async Task<JObject> LoadModelAsync(
        [API.APIParameter("Model ID")] string modelId)
        {
            ConfigData configDataObj = GlobalConfig.ConfigData;
            if (GlobalConfig.ConfigData == null)
            {
                await SaveConfigSettings.ReadConfigAsync();
            }
            else
            {
                configDataObj.Model = modelId;
                ConfigUtil.UpdateConfig(configDataObj);
            }
            if (configDataObj.LLMBackend == "openai")
            {
                return CreateSuccessResponse("SKIP: 3rd party APIs do not pre load models."); // TODO: Maybe this should be success?
            }
            string loadEndpoint = configDataObj.LlmEndpoint + "/api/generate"; // TODO: Get the endpoint from the config
            var requestBody = new
            {
                model = modelId,
            };
            try
            {
                string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                StringContent httpContent = new(jsonRequestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(loadEndpoint, httpContent);
                if (response.IsSuccessStatusCode)
                {
                    Logs.Info($"Successfully loaded model: {modelId}");
                    configDataObj.Model = modelId;
                    ConfigUtil.UpdateConfig(configDataObj);
                    return CreateSuccessResponse($"Successfully loaded model: {modelId}", null, configDataObj);
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Logs.Error($"Failed to load model {modelId}: {response.StatusCode} - {response.ReasonPhrase}. Response: {errorMessage}");
                    return CreateErrorResponse($"Failed to load model {modelId}: {response.StatusCode} - {response.ReasonPhrase}. Response: {errorMessage}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error loading model {modelId}: {ex.Message}");
                return CreateErrorResponse($"Error loading model {modelId}: {ex.Message}");
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
                        return CreateErrorResponse("Failed to load configuration from file.");
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
                Logs.Verbose($"Request body for LLM Backend: {JsonSerializer.Serialize(requestBody)}");
                HttpRequestMessage request = new(HttpMethod.Post, llmEndpoint);
                if (llmBackend.Equals("openai", StringComparison.CurrentCultureIgnoreCase))
                {
                    string apiKey = configDataObj.Backends.OpenAI.ApiKey;
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        string error = "OpenAI API Key is missing from the configuration.";
                        Logs.Error(error);
                        return CreateErrorResponse(error);
                    }
                    // Add the API key to the Authorization header
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    Logs.Debug("Added OpenAI API key to the request headers.");
                }
                request.Content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                Logs.Debug($"Response From LLM Backend: {response}");
                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response, llmBackend);
                    return !string.IsNullOrEmpty(llmResponse)
                        ? CreateSuccessResponse(llmResponse)
                        : CreateErrorResponse("Unexpected API response format or empty response.");
                }
                else
                {
                    Logs.Error($"API request failed {response.Content} with status code: {response.StatusCode}");
                    return CreateErrorResponse($"API request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error(ex.Message);
                return CreateErrorResponse(ex.Message);
            }
        }
    }
}
