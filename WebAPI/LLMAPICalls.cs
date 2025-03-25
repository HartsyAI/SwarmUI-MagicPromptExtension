using Newtonsoft.Json.Linq;
using System.Text.Json;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using static Hartsy.Extensions.MagicPromptExtension.BackendSchema;
using SwarmUI.Core;
using SwarmUI.Accounts;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI;

public class LLMAPICalls : MagicPromptAPI
{
    private static readonly HttpClient _httpClient = new();

    /// <summary>Fetches available models from the LLM API endpoint.</summary>
    /// <returns>A JSON object containing the models or an error message.</returns>
    public static async Task<JObject> GetModelsAsync(Session session = null)
    {
        try
        {
            // Get current settings
            JObject sessionSettings = await SessionSettings.GetSettingsAsync();
            if (!sessionSettings["success"].Value<bool>())
            {
                return CreateErrorResponse(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
            }
            JObject settings = sessionSettings["settings"] as JObject;
            if (settings == null)
            {
                return CreateErrorResponse("Configuration not found. Please check your settings.");
            }
            string chatBackend = settings["backend"]?.ToString()?.ToLower() ?? "openrouter";
            string visionBackend = settings["visionbackend"]?.ToString()?.ToLower() ?? "openrouter";
            // Get chat models
            List<ModelData> chatModels = await GetModelsForBackend(chatBackend, settings, false, session);
            if (chatModels == null || !chatModels.Any())
            {
                return CreateErrorResponse($"Failed to fetch models for {chatBackend}. Please check your API key and try again.");
            }
            // Get vision models
            List<ModelData> visionmodels = await GetModelsForBackend(visionBackend, settings, true, session);
            if (visionmodels == null || !visionmodels.Any())
            {
                return CreateErrorResponse($"Failed to fetch vision models for {visionBackend}. Please check your API key and try again.");
            }
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

    private static async Task<List<ModelData>> GetModelsForBackend(string backend, JObject settings, bool isVision = false, Session session = null)
    {
        // Get endpoint based on backend
        string endpoint = GetEndpoint(backend, settings, "models");
        if (string.IsNullOrEmpty(endpoint))
        {
            string errorMessage = $"Failed to get endpoint for backend: {backend}";
            Logs.Error(errorMessage);
            return new List<ModelData>();
        }
        // Create and configure request
        HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!ConfigureRequest(request, backend, settings, session, out string error))
        {
            Logs.Error(error);
            return new List<ModelData>();
        }
        
        // Send request and handle response
        try
        {
            HttpResponseMessage response = await _httpClient.SendAsync(request);
            string responseContent = await response.Content.ReadAsStringAsync();
            
            if (response.IsSuccessStatusCode)
            {
                List<ModelData> models = MagicPromptAPI.DeserializeModels(responseContent, backend);
                return models ?? new List<ModelData>();
            }
            else
            {
                // Handle common HTTP status codes
                switch ((int)response.StatusCode)
                {
                    case 401: // Unauthorized
                    case 403: // Forbidden
                        Logs.Error($"API key error fetching models for {backend}: {response.StatusCode}");
                        // No need to return error to user here, just log it and return empty list
                        return new List<ModelData>();
                        
                    case 429: // Too Many Requests
                        Logs.Error($"Rate limit exceeded fetching models for {backend}: {response.StatusCode}");
                        return new List<ModelData>();
                        
                    case 500: // Internal Server Error
                    case 502: // Bad Gateway
                    case 503: // Service Unavailable 
                    case 504: // Gateway Timeout
                        Logs.Error($"Server error fetching models for {backend}: {response.StatusCode}");
                        return new List<ModelData>();
                        
                    default:
                        // Try to detect error type from response content
                        string errorType = ErrorHandler.DetectErrorType(responseContent, backend);
                        Logs.Error($"Error fetching models for {backend} ({errorType}): {responseContent}");
                        return new List<ModelData>();
                }
            }
        }
        catch (HttpRequestException ex)
        {
            // Handle network connectivity issues
            Logs.Error($"HTTP request error fetching models for {backend}: {ex.Message}");
            return new List<ModelData>();
        }
        catch (Exception ex)
        {
            Logs.Error($"Exception fetching models for {backend}: {ex.Message}");
            return new List<ModelData>();
        }
    }

    /// <summary>Gets the appropriate endpoint for model listing based on the backend.</summary>
    /// <param name="backend">The LLM backend being used.</param>
    /// <param name="settings">The current settings object.</param>
    /// <param name="endpointType">The type of endpoint (chat, vision, etc.)</param>
    /// <returns>The endpoint URL for model listing.</returns>
    private static string GetEndpoint(string backend, JObject settings, string endpointType)
    {
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            return string.Empty;
        }
        JObject backendConfig = backends[backend] as JObject;
        if (backendConfig == null)
        {
            return string.Empty;
        }
        string baseUrl = backendConfig["baseurl"]?.ToString();
        JObject endpoints = backendConfig["endpoints"] as JObject;
        if (string.IsNullOrEmpty(baseUrl) || endpoints == null)
        {
            Logs.Error($"Invalid configuration for backend {backend}: BaseUrl={baseUrl}, Endpoints={endpoints}");
            return string.Empty;
        }
        string endpoint;
        // Special handling for vision endpoints
        if (endpointType == "vision")
        {
            endpoint = backend.ToLower() switch
            {
                "openrouter" or "openai" or "openaiapi" => endpoints["chat"]?.ToString(),// OpenAI and OpenRouter use the chat endpoint for vision
                "anthropic" => endpoints["messages"]?.ToString() ?? endpoints["chat"]?.ToString(),// Anthropic uses the messages endpoint for both chat and vision
                "ollama" => endpoints["vision"]?.ToString() ?? endpoints["chat"]?.ToString(),// Ollama has a dedicated vision endpoint
                _ => endpoints[endpointType]?.ToString() ?? endpoints["chat"]?.ToString(),
            };
        }
        // Special handling for models endpoints
        else if (endpointType == "models")
        {
            endpoint = backend.ToLower() switch
            {
                "anthropic" => "/v1/models", // Anthropic has a standardized models endpoint now
                _ => endpoints[endpointType]?.ToString()
            };
        }
        else
        {
            endpoint = endpoints[endpointType]?.ToString();
        }
        if (string.IsNullOrEmpty(endpoint))
        {
            Logs.Error($"{endpointType} endpoint not found for backend {backend}");
            return string.Empty;
        }
        // Ensure the base URL doesn't end with a slash and the endpoint starts with one
        baseUrl = baseUrl.TrimEnd('/');
        endpoint = endpoint.StartsWith($"/") ? endpoint : "/" + endpoint;
        string fullUrl = $"{baseUrl}{endpoint}";
        return fullUrl;
    }

    /// <summary>Configures the HTTP request with appropriate headers based on the backend.</summary>
    /// <param name="request">The HTTP request to configure.</param>
    /// <param name="backend">The LLM backend being used.</param>
    /// <param name="settings">The current settings object.</param>
    /// <param name="session">The current user session.</param>
    /// <param name="error">Output parameter for any error message.</param>
    /// <returns>True if configuration was successful, false otherwise.</returns>
    private static bool ConfigureRequest(HttpRequestMessage request, string backend, JObject settings, Session session, out string error)
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
                string apiKey = session.User.GetGenericData("openai_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openai_api", "key");
                if (string.IsNullOrEmpty(apiKey))
                {
                    error = "OpenAI API Key not found. To configure:\n" +
                        "1. Go to the User tab\n" +
                        "2. Add your OpenAI API key in the API Keys section\n" +
                        "3. Save your changes\n\n" +
                        "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                break;
            case "openrouter":
                string openRouterKey = session?.User?.GetGenericData("openrouter_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openrouter_api", "key");
                if (string.IsNullOrEmpty(openRouterKey))
                {
                    error = "OpenRouter API Key not found. To configure:\n" +
                        "1. Go to the User tab\n" +
                        "2. Add your OpenRouter API key in the API Keys section\n" +
                        "3. Save your changes\n\n" +
                        "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {openRouterKey}");
                request.Headers.Add("HTTP-Referer", "https://github.com/HartsyAI/SwarmUI-MagicPromptExtension");
                request.Headers.Add("X-Title", "SwarmUI-MagicPromptExtension");
                break;
            case "anthropic":
                string anthropicKey = session?.User?.GetGenericData("anthropic_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("anthropic_api", "key");
                if (string.IsNullOrEmpty(anthropicKey))
                {
                    error = "Anthropic API Key not found. To configure:\n" +
                        "1. Go to the User tab\n" +
                        "2. Add your Anthropic API key in the API Keys section\n" +
                        "3. Save your changes\n\n" +
                        "Need help? Visit: https://github.com/HartsyAI/SwarmUI-MagicPromptExtension";
                    return false;
                }
                request.Headers.Add("x-api-key", anthropicKey);
                request.Headers.Add("anthropic-version", "2023-06-01");
                break;
            case "openaiapi":
                string openaiApiKey = session?.User?.GetGenericData("openaiapi_local", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openaiapi_local", "key");
                if (!string.IsNullOrEmpty(openaiApiKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {openaiApiKey}");
                }
                break;
            case "ollama":
                string ollamaKey = session?.User?.GetGenericData("ollama_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("ollama_api", "key");
                if (!string.IsNullOrEmpty(ollamaKey))
                {
                    request.Headers.Add("Authorization", $"Bearer {ollamaKey}");
                }
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
    public static async Task<JObject> PhoneHomeAsync(JObject requestData, Session session = null)
    {
        try
        {
            if (requestData == null)
            {
                return CreateErrorResponse("Request data is null");
            }
            // Safely parse message content
            JToken messageContentToken = requestData["messageContent"];
            if (messageContentToken == null)
            {
                return CreateErrorResponse("Message content is missing");
            }
            // Create message content with explicit parsing
            MessageContent messageContent = new()
            {
                Text = messageContentToken["text"]?.ToString(),
                KeepAlive = messageContentToken["KeepAlive"]?.Value<int?>()
            };
            // Safely parse media content if it exists
            JToken mediaToken = messageContentToken["media"];
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
            JObject sessionSettings = await SessionSettings.GetSettingsAsync();
            if (!sessionSettings["success"].Value<bool>())
            {
                Logs.Error(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
                return CreateErrorResponse(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
            }

            JObject settings = sessionSettings["settings"] as JObject;
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
            // Get the instructions from the request if provided
            string clientProvidedInstructions = messageContentToken["instructions"]?.ToString();
            // Only perform server-side lookup if client didn't provide instructions
            if (string.IsNullOrEmpty(clientProvidedInstructions))
            {
                string action = requestData["action"]?.ToString()?.ToLower() ?? "chat";
                clientProvidedInstructions = action switch
                {
                    "vision" => settings["instructions"]?["vision"]?.ToString() ?? "",
                    "prompt" => settings["instructions"]?["prompt"]?.ToString() ?? "",
                    "caption" => settings["instructions"]?["caption"]?.ToString() ?? "",
                    "generate-instruction" => settings["instructions"]?["instructiongen"]?.ToString() ?? "",
                    _ => settings["instructions"]?["chat"]?.ToString() ?? ""
                };
            }
            messageContent.Instructions = clientProvidedInstructions;
            messageContent.Text = $"{messageContent.Text}";
            // Create request with proper headers and body
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
            if (!ConfigureRequest(request, backend, settings, session, out string error))
            {
                Logs.Error(error);
                return CreateErrorResponse(error);
            }
            // Add request body using BackendSchema
            object requestBody = GetSchemaType(backend, messageContent, modelId, messageType);
            string jsonContent = JsonSerializer.Serialize(requestBody);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            
            try
            {
                // Send request and handle response
                HttpResponseMessage response = await _httpClient.SendAsync(request);
                string responseContent = await response.Content.ReadAsStringAsync();
                
                // Handle API errors indicated by status code
                if (!response.IsSuccessStatusCode)
                {
                    // Check for common HTTP status codes
                    switch ((int)response.StatusCode)
                    {
                        case 401: // Unauthorized
                        case 403: // Forbidden
                            Logs.Error($"Authentication error: {response.StatusCode} from {backend}");
                            return CreateSuccessResponse(ErrorHandler.FormatErrorMessage("authentication", 
                                $"API key error (HTTP {(int)response.StatusCode}): {responseContent}", backend));
                            
                        case 429: // Too Many Requests
                            Logs.Error($"Rate limit exceeded: {response.StatusCode} from {backend}");
                            return CreateSuccessResponse(ErrorHandler.FormatErrorMessage("quota", 
                                $"Rate limit exceeded (HTTP 429): {responseContent}", backend));
                            
                        case 500: // Internal Server Error
                        case 502: // Bad Gateway
                        case 503: // Service Unavailable 
                        case 504: // Gateway Timeout
                            Logs.Error($"Server error: {response.StatusCode} from {backend}");
                            return CreateSuccessResponse(ErrorHandler.FormatErrorMessage("server_error", 
                                $"Server error (HTTP {(int)response.StatusCode}): {responseContent}", backend));
                            
                        default:
                            // Try to detect error type from response content
                            string errorType = ErrorHandler.DetectErrorType(responseContent, backend);
                            string formattedError = ErrorHandler.FormatErrorMessage(errorType, 
                                $"HTTP error {(int)response.StatusCode}: {responseContent}", backend);
                            Logs.Error($"HTTP {(int)response.StatusCode} error from {backend}: {responseContent}");
                            return CreateSuccessResponse(formattedError);
                    }
                }
                
                // Use the ErrorHandler to check for known error patterns in the response
                string errorType = ErrorHandler.DetectErrorType(responseContent, backend);
                if (errorType != "generic")
                {
                    string formattedError = ErrorHandler.FormatErrorMessage(errorType, responseContent, backend);
                    Logs.Error($"{errorType} error detected: {responseContent}");
                    return CreateSuccessResponse(formattedError);
                }
                
                if (response.IsSuccessStatusCode)
                {
                    try {
                        string llmResponse = await DeserializeResponse(response, backend);
                        if (!string.IsNullOrEmpty(llmResponse))
                        {
                            return CreateSuccessResponse(llmResponse);
                        }
                    }
                    catch (Exception ex) {
                        // Check if the exception message indicates a quota issue
                        if (ex.Message.Contains("insufficient_quota") || ex.Message.Contains("quota exceeded"))
                        {
                            string quotaErrorMessage = ErrorHandler.FormatErrorMessage("quota", ex.Message, backend);
                            Logs.Error($"API quota limit reached (parsing error): {ex.Message}");
                            return CreateSuccessResponse(quotaErrorMessage);
                        }
                        
                        // Log the error but continue to the general error handling
                        Logs.Error($"Error deserializing response: {ex.Message}");
                    }
                }
                
                // Try to extract detailed error from response
                try
                {
                    // Try specific provider error handling first
                    if (backend.Equals("openai", StringComparison.OrdinalIgnoreCase) ||
                        backend.Equals("openaiapi", StringComparison.OrdinalIgnoreCase))
                    {
                        if (ErrorHandler.TryParseOpenAIError(responseContent, out string openAIErrorMessage))
                        {
                            return CreateSuccessResponse(openAIErrorMessage);
                        }
                    }
                    
                    // Try OpenRouter error parsing
                    if (ErrorHandler.TryParseOpenRouterError(responseContent, out string openRouterErrorMessage))
                    {
                        return CreateSuccessResponse(openRouterErrorMessage);
                    }
                    
                    // Try Ollama error parsing
                    if (backend.Equals("ollama", StringComparison.OrdinalIgnoreCase) &&
                        ErrorHandler.TryParseOllamaError(responseContent, out string ollamaErrorMessage))
                    {
                        return CreateSuccessResponse(ollamaErrorMessage);
                    }
                    
                    // If all specific parsing attempts failed, use generic handling
                    string errorMessage = ErrorHandler.ProcessErrorResponse(responseContent, backend);
                    return CreateSuccessResponse(errorMessage);
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error handling API response: {ex.Message}");
                    return CreateSuccessResponse("I apologize, but something went wrong. Please try again later.");
                }
            }
            catch (HttpRequestException ex)
            {
                Logs.Error($"HTTP request error: {ex.Message}");
                return CreateSuccessResponse(ErrorHandler.FormatErrorMessage("http_request_error", ex.Message, backend));
            }
            catch (Exception ex)
            {
                Logs.Error($"Error in PhoneHomeAsync: {ex.Message}");
                string errorMessage = ErrorHandler.FormatErrorMessage("generic_exception", ex.Message, "unknown");
                return CreateSuccessResponse(errorMessage);
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"Error in PhoneHomeAsync: {ex.Message}");
            string errorMessage = ErrorHandler.FormatErrorMessage("generic_exception", ex.Message, "unknown");
            return CreateSuccessResponse(errorMessage);
        }
    }
}
