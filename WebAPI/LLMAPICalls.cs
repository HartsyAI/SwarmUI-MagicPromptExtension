using Newtonsoft.Json.Linq;
using System.Text.Json;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using SwarmUI.Accounts;
using static Hartsy.Extensions.MagicPromptExtension.BackendSchema;

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
                // Get current settings
                JObject session = await SessionSettings.GetSettingsAsync();
                if (!session["success"].Value<bool>())
                {
                    return CreateErrorResponse(session["error"]?.ToString() ?? "Failed to load settings");
                }
                JObject settings = session["settings"] as JObject;
                if (settings == null)
                {
                    return CreateErrorResponse("Configuration not found. Please check your settings.");
                }
                string chatBackend = settings["backend"]?.ToString()?.ToLower() ?? "openrouter";
                string visionBackend = settings["visionbackend"]?.ToString()?.ToLower() ?? "openrouter";
                Logs.Debug($"Chat backend: {chatBackend}, Vision backend: {visionBackend}");

                // Get chat models
                List<ModelData> chatModels = await GetModelsForBackend(chatBackend, settings);
                // Get vision models
                List<ModelData> visionmodels = await GetModelsForBackend(visionBackend, settings, true);
                // Create combined response
                return JObject.FromObject(new
                {
                    success = true,
                    models = chatModels,
                    visionmodels,
                    settings
                });
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception occurred: {ex.Message}");
                return CreateErrorResponse(
                    "Unexpected error while fetching models:\n" +
                    $"{ex.Message}\n\n" +
                    "Common solutions:\n" +
                    "1. Restart your LLM backend\n" +
                    "2. Check your system resources\n" +
                    "3. Verify your configuration\n\n" +
                    "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
            }
        }

        private static async Task<List<ModelData>> GetModelsForBackend(string backend, JObject settings, bool isVision = false)
        {
            // Handle Anthropic's hardcoded models
            if (backend == "anthropic")
            {
                Logs.Debug($"Using Anthropic as {(isVision ? "vision" : "chat")} backend.");
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
                return models;
            }
            // Get endpoint based on backend
            string endpoint = GetEndpoint(backend, settings, "models");
            if (string.IsNullOrEmpty(endpoint))
            {
                Logs.Error($"Failed to get endpoint for backend: {backend}");
                return [];
            }
            Logs.Debug($"{(isVision ? "Vision" : "Chat")} LLM Endpoint: {endpoint}");
            // Create and configure request
            HttpRequestMessage request = new(HttpMethod.Get, endpoint);
            if (!ConfigureRequest(request, backend, settings, out string error))
            {
                Logs.Error(error);
                return [];
            }
            // Send request and handle response
            try
            {
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                //Logs.Debug($"Response from {(isVision ? "Vision" : "Chat")} LLM API: {response}");
                if (response.IsSuccessStatusCode)
                {
                    string jsonString = await response.Content.ReadAsStringAsync();
                    List<ModelData> models = DeserializeModels(jsonString, backend);
                    return models ?? [];
                }
                else
                {
                    Logs.Error($"Failed to fetch {(isVision ? "vision" : "chat")} models: {response.StatusCode} - {response.ReasonPhrase}");
                    return [];
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception fetching {(isVision ? "vision" : "chat")} models: {ex.Message}");
                return [];
            }
        }

        /// <summary>Gets the appropriate endpoint for model listing based on the backend.</summary>
        /// <param name="backend">The LLM backend being used.</param>
        /// <param name="settings">The current settings object.</param>
        /// <param name="endpointType">The type of endpoint (chat, vision, etc.)</param>
        /// <returns>The endpoint URL for model listing.</returns>
        private static string GetEndpoint(string backend, JObject settings, string endpointType)
        {
            Logs.Debug($"Getting {endpointType} endpoint for backend: {backend}");
            Logs.Debug($"Settings: {settings}");
            JObject backends = settings["backends"] as JObject;
            if (backends == null)
            {
                Logs.Error("Backends configuration is null");
                return string.Empty;
            }
            Logs.Debug($"Backends config: {backends}");
            // Get the backend configuration
            JObject backendConfig = backends[backend] as JObject;
            if (backendConfig == null)
            {
                Logs.Error($"Configuration for backend '{backend}' not found");
                return string.Empty;
            }
            Logs.Debug($"Backend config for {backend}: {backendConfig}");
            // Get base URL and endpoints
            string baseUrl = backendConfig["baseurl"]?.ToString();
            JObject endpoints = backendConfig["endpoints"] as JObject;
            Logs.Debug($"BaseUrl: {baseUrl}");
            Logs.Debug($"Endpoints: {endpoints}");
            if (string.IsNullOrEmpty(baseUrl) || endpoints == null)
            {
                Logs.Error($"Invalid configuration for backend {backend}: BaseUrl={baseUrl}, Endpoints={endpoints}");
                return string.Empty;
            }
            string endpoint;
            // Special handling for vision endpoints
            if (endpointType == "vision")
            {
                switch (backend.ToLower())
                {
                    case "openrouter":
                    case "openai":
                    case "openaiapi":
                        // OpenAI and OpenRouter use the chat endpoint for vision
                        endpoint = endpoints["chat"]?.ToString();
                        Logs.Debug($"Using chat endpoint for {backend} vision request: {endpoint}");
                        break;
                    case "anthropic":
                        // Anthropic uses the messages endpoint for both chat and vision
                        endpoint = endpoints["messages"]?.ToString() ?? endpoints["chat"]?.ToString();
                        Logs.Debug($"Using messages/chat endpoint for Anthropic vision request: {endpoint}");
                        break;
                    case "ollama":
                        // Ollama has a dedicated vision endpoint
                        endpoint = endpoints["vision"]?.ToString() ?? endpoints["chat"]?.ToString();
                        Logs.Debug($"Using vision/chat endpoint for Ollama request: {endpoint}");
                        break;
                    default:
                        endpoint = endpoints[endpointType]?.ToString() ?? endpoints["chat"]?.ToString();
                        break;
                }
            }
            else
            {
                endpoint = endpoints[endpointType]?.ToString();
            }
            Logs.Debug($"Found endpoint for {endpointType}: {endpoint}");
            if (string.IsNullOrEmpty(endpoint))
            {
                Logs.Error($"{endpointType} endpoint not found for backend {backend}");
                return string.Empty;
            }
            // Ensure the base URL doesn't end with a slash and the endpoint starts with one
            baseUrl = baseUrl.TrimEnd('/');
            endpoint = endpoint.StartsWith("/") ? endpoint : "/" + endpoint;
            string fullUrl = $"{baseUrl}{endpoint}";
            Logs.Debug($"Generated {endpointType} endpoint for {backend}: {fullUrl}");
            return fullUrl;
        }

        /// <summary>Configures the HTTP request with appropriate headers based on the backend.</summary>
        /// <param name="request">The HTTP request to configure.</param>
        /// <param name="backend">The LLM backend being used.</param>
        /// <param name="settings">The current settings object.</param>
        /// <param name="error">Output parameter for any error message.</param>
        /// <returns>True if configuration was successful, false otherwise.</returns>
        private static bool ConfigureRequest(HttpRequestMessage request, string backend, JObject settings, out string error)
        {
            error = string.Empty;
            JObject backends = settings["backends"] as JObject;
            if (backends == null)
            {
                error = "Backend configuration not found";
                return false;
            }
            switch (backend)
            {
                case "openai":
                    string apiKey = backends["openai"]?["apikey"]?.ToString();
                    if (string.IsNullOrEmpty(apiKey))
                    {
                        error = "OpenAI API Key not found. To configure:\n" +
                            "1. Get your API key from OpenAI\n" +
                            "2. Add it in the extension settings\n" +
                            "3. Save your changes\n\n" +
                            "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        return false;
                    }
                    request.Headers.Add("Authorization", $"Bearer {apiKey}");
                    break;
                case "openrouter":
                    string openRouterKey = backends["openrouter"]?["apikey"]?.ToString();
                    if (string.IsNullOrEmpty(openRouterKey))
                    {
                        error = "OpenRouter API Key not found. To configure:\n" +
                            "1. Get your API key from OpenRouter\n" +
                            "2. Add it in the extension settings\n" +
                            "3. Save your changes\n\n" +
                            "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        return false;
                    }
                    request.Headers.Add("Authorization", $"Bearer {openRouterKey}");
                    request.Headers.Add("HTTP-Referer", "https://github.com/HartsyAI/SwarmUI-MagicPromptExtension");
                    request.Headers.Add("X-Title", "SwarmUI-MagicPromptExtension");
                    break;
                case "anthropic":
                    string anthropicKey = backends["anthropic"]?["apikey"]?.ToString();
                    if (string.IsNullOrEmpty(anthropicKey))
                    {
                        error = "Anthropic API Key not found. To configure:\n" +
                            "1. Get your API key from Anthropic\n" +
                            "2. Add it in the extension settings\n" +
                            "3. Save your changes\n\n" +
                            "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                        return false;
                    }
                    request.Headers.Add("x-api-key", anthropicKey);
                    request.Headers.Add("anthropic-version", "2023-06-01");
                    break;
                case "openaiapi":
                case "ollama":
                    // No additional headers needed
                    break;
                default:
                    error = $"Unsupported LLM backend: {backend}\n" +
                        "Please select one of the supported backends:\n" +
                        "- Ollama (recommended for local use)\n" +
                        "- OpenAI\n" +
                        "- OpenRouter\n" +
                        "- Anthropic\n\n" +
                        "For backend setup guides, visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                    return false;
            }
            return true;
        }

        /// <summary>Loads the specified model from the LLM backend.</summary>
        /// <param name="modelId">The ID of the model to load.</param>
        /// <returns>A JSON object indicating success or error.</returns>
        public static async Task<JObject> LoadModelAsync([API.APIParameter("Model ID")] string modelId)
        {
            JObject settingsResponse = await SessionSettings.GetSettingsAsync();
            if (settingsResponse == null || !settingsResponse["success"].Value<bool>())
            {
                return CreateErrorResponse("Configuration not found. Please:\n" +
                    "1. Verify you installed Ollama or other compatible backend (That is not done for you)\n" +
                    "2. Check that you have properly saved your settings\n" +
                    "3. Try restarting Swarm\n\n" +
                    "Setup guide: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
            }
            // Get the actual settings object
            JObject settings = settingsResponse["settings"] as JObject;
            if (settings == null)
            {
                return CreateErrorResponse("Invalid settings format");
            }
            // Debug logging
            Logs.Debug($"Current settings: {settings}");
            // Create a new settings object preserving the structure
            JObject newSettings = new()
            {
                ["backend"] = settings["backend"],
                ["unloadmodel"] = settings["unloadmodel"] ?? false,
                ["instructions"] = settings["instructions"] ?? "",
                ["model"] = modelId,
                ["backends"] = settings["backends"]?.DeepClone()
            };
            await SessionSettings.SaveSettingsAsync(newSettings);
            // Validate backend
            if (settings["backend"] == null)
            {
                return CreateErrorResponse("LLMBackend not found in settings");
            }
            string backend = settings["backend"].ToString().ToLower();
            Logs.Debug($"Using backend: {backend}");
            string endpoint;
            switch (backend)
            {
                case "anthropic":
                case "openai":
                case "openrouter":
                    return CreateSuccessResponse("Skip Model Preload: Not needed for 3rd party APIs.");
                case "openaiapi":
                    if (settings["backends"]?["openaiapi"]?["baseurl"] == null)
                    {
                        return CreateErrorResponse("OpenAI API BaseUrl not found in settings");
                    }
                    endpoint = settings["backends"]["openaiapi"]["baseurl"].ToString();
                    break;
                case "ollama":
                    if (settings["backends"]?["ollama"] == null)
                    {
                        return CreateErrorResponse("Ollama configuration not found in settings");
                    }
                    string baseUrl = settings["backends"]["ollama"]["baseurl"]?.ToString() ?? "http://localhost:11434";
                    if (settings["backends"]["ollama"]["endpoints"]?["load"] == null)
                    {
                        return CreateErrorResponse("Ollama load endpoint not found in settings");
                    }
                    endpoint = $"{baseUrl}{settings["backends"]["ollama"]["endpoints"]["load"]}";
                    break;
                default:
                    Logs.Error($"Unsupported backend type in LoadModels: {backend}");
                    return CreateErrorResponse($"Unsupported LLM backend: {backend}");
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
                    settings["Model"] = modelId; // Update the model in settings
                    await SessionSettings.SaveSettingsAsync(settings); // Save the updated settings
                    return CreateSuccessResponse($"Successfully loaded model: {modelId}", null, settings);
                }
                else
                {
                    string errorMessage = await response.Content.ReadAsStringAsync();
                    Logs.Error($"Failed to load model {modelId}: {response.StatusCode} - {response.ReasonPhrase}. Response: {errorMessage}");
                    return CreateErrorResponse($"Failed to load model '{modelId}'. Please ensure:\n" +
                        "1. The model name is correct\n" +
                        "2. You have sufficient system resources\n" +
                        "3. The model is downloaded (for Ollama)\n" +
                        $"4. Check the error: {response.StatusCode} - {errorMessage}\n\n" +
                        "For help, visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                    );
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error loading model {modelId}: {ex.Message}");
                return CreateErrorResponse($"Error loading model '{modelId}':\n" +
                    $"{ex.Message}\n\n" +
                    "Common solutions:\n" +
                    "1. Verify your LLM backend is running\n" +
                    "2. Check your network connection\n" +
                    "3. Ensure the model is available\n\n" +
                    "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension"
                );
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
        public static async Task<JObject> PhoneHomeAsync(JObject requestData)
        {
            try
            {
                // Debug logging of incoming request
                Logs.Debug($"Received request data: {requestData?.ToString(Newtonsoft.Json.Formatting.None)}");
                Logs.Debug($"Request data type: {requestData?.Type}");

                if (requestData == null)
                {
                    return CreateErrorResponse("Request data is null");
                }

                // Safely parse message content
                var messageContentToken = requestData["messageContent"];
                if (messageContentToken == null)
                {
                    return CreateErrorResponse("Message content is missing");
                }

                // Create message content with explicit parsing
                var messageContent = new MessageContent
                {
                    Text = messageContentToken["text"]?.ToString()
                };

                // Safely parse media content if it exists
                var mediaToken = messageContentToken["media"];
                if (mediaToken != null && mediaToken.Type == JTokenType.Array)
                {
                    try
                    {
                        messageContent.Media = mediaToken.ToObject<List<MediaContent>>();
                    }
                    catch (Exception ex)
                    {
                        Logs.Error($"Failed to parse media content: {ex.Message}");
                        return CreateErrorResponse("Failed to parse media content");
                    }
                }

                // Parse model ID
                string modelId = requestData["modelId"]?.ToString();

                // Parse message type with validation
                MessageType messageType = MessageType.Text;
                string messageTypeStr = requestData["messageType"]?.ToString();
                if (!string.IsNullOrEmpty(messageTypeStr))
                {
                    if (!Enum.TryParse(messageTypeStr, true, out messageType))
                    {
                        messageType = MessageType.Text;
                    }
                }

                // Validate required data
                if (string.IsNullOrEmpty(messageContent.Text) || string.IsNullOrEmpty(modelId))
                {
                    return CreateErrorResponse("Message content or model ID is missing");
                }

                // Get current settings
                JObject session = await SessionSettings.GetSettingsAsync();
                if (!session["success"].Value<bool>())
                {
                    Logs.Error(session["error"]?.ToString() ?? "Failed to load settings");
                    return CreateErrorResponse(session["error"]?.ToString() ?? "Failed to load settings");
                }

                JObject settings = session["settings"] as JObject;
                if (settings == null)
                {
                    Logs.Error("Configuration not found. Please check your settings.");
                    return CreateErrorResponse("Configuration not found. Please check your settings.");
                }

                // Get appropriate backend based on message type
                string backend = messageType == MessageType.Vision
                    ? settings["visionbackend"]?.ToString()?.ToLower() ?? settings["backend"].ToString().ToLower()
                    : settings["backend"].ToString().ToLower();

                // Get appropriate endpoint
                string endpoint = GetEndpoint(backend, settings, messageType == MessageType.Vision ? "vision" : "chat");
                if (string.IsNullOrEmpty(endpoint))
                {
                    return CreateErrorResponse($"Failed to get endpoint for {backend}");
                }

                // Add instructions if present
                string instructions = messageType == MessageType.Vision
                    ? settings["instructions"]?["vision"]?.ToString() ?? ""
                    : settings["instructions"]?["chat"]?.ToString() ?? "";

                if (!string.IsNullOrEmpty(instructions))
                {
                    messageContent.Text = $"{instructions}\n\nUser: {messageContent.Text}";
                }

                // Create request with proper headers
                using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
                if (!ConfigureRequest(request, backend, settings, out string error))
                {
                    Logs.Error(error);
                    return CreateErrorResponse(error);
                }

                // Add request body using BackendSchema
                object requestBody = GetSchemaType(backend, messageContent, modelId, messageType);
                string jsonContent = JsonSerializer.Serialize(requestBody);
                request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

                // Log request details for debugging
                Logs.Debug($"Sending request to endpoint: {endpoint}");
                Logs.Debug($"Request body: {jsonContent}");

                // Send request
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response, backend);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        return CreateSuccessResponse(llmResponse);
                    }
                }

                // Try to extract detailed error from response
                try
                {
                    var errorResponse = Newtonsoft.Json.JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
                    if (errorResponse?.Error?.Metadata?.Raw != null)
                    {
                        // Use the detailed error message as the response
                        return CreateSuccessResponse(errorResponse.Error.Metadata.Raw);
                    }
                    else if (errorResponse?.Error?.Message != null)
                    {
                        return CreateSuccessResponse(errorResponse.Error.Message);
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error parsing API response: {ex.Message}");
                }

                // Fallback to generic error if we couldn't parse the detailed one
                return CreateSuccessResponse("I apologize, but something went wrong. Please try again later.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error in PhoneHomeAsync: {ex.Message}");
                return CreateErrorResponse($"Error in PhoneHomeAsync: {ex.Message}" +
                    "I apologize, but an unexpected error occurred. Please try again later.");
            }
        }
    }
}
