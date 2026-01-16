using Newtonsoft.Json.Linq;
using System.Text.Json;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using SwarmUI.Core;
using SwarmUI.Accounts;
using System.Net.Http;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;

using static Hartsy.Extensions.MagicPromptExtension.BackendSchema;
using SwarmUI.Backends;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI;

public class LLMAPICalls : MagicPromptAPI
{
    protected static readonly HttpClient HttpClient = CreateHttpClient();

    // Disable global timeout; per-request CancellationTokenSource controls timeout instead
    private static HttpClient CreateHttpClient()
    {
        var client = NetworkBackendUtils.MakeHttpClient();
        client.Timeout = Timeout.InfiniteTimeSpan;
        return client;
    }

    /// <summary>Fetches available models from the LLM API endpoint.</summary>
    /// <returns>A JSON object containing the models or an error message.</returns>
    public static async Task<JObject> GetMagicPromptModels(Session session)
    {
        try
        {
            // Get current settings
            JObject sessionSettings = await SessionSettings.GetMagicPromptSettings();
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
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Exception occurred: {ex.Message}");
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

    public static async Task<List<ModelData>> GetModelsForBackend(string backend, JObject settings, bool isVision = false, Session session = null)
    {
        // Get endpoint based on backend
        string endpoint = GetEndpoint(backend, settings, "models");
        if (string.IsNullOrEmpty(endpoint))
        {
            string errorMessage = $"Failed to get endpoint for backend: {backend}";
            Logs.Error($"MagicPromptExtension.LLMAPICalls: {errorMessage}");
            return [];
        }
        // Create and configure request
        HttpRequestMessage request = new(HttpMethod.Get, endpoint);
        if (!ConfigureRequest(request, backend, settings, session, out string error))
        {
            Logs.Error($"MagicPromptExtension.LLMAPICalls: {error}");
            return [];
        }

        // Send request and handle response
        try
        {
            // Log the target endpoint and backend for diagnostics
            Logs.Debug($"[MagicPrompt] Fetching models | backend={backend} | endpoint={endpoint}");
            // Get timeout from backend settings, defaulting to 20 seconds for model listing
            int timeoutSec = GetBackendTimeout(settings, backend, defaultTimeout: 20);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
            string responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                List<ModelData> models = MagicPromptAPI.DeserializeModels(responseContent, backend);
                return models ?? [];
            }
            else
            {
                // Process error using the ErrorHandler
                string errorMessage = ErrorHandler.ProcessErrorResponse(responseContent, response.StatusCode, backend);
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Error fetching models for {backend}: {errorMessage}");
                return [];
            }
        }
        catch (System.Threading.Tasks.TaskCanceledException)
        {
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Model listing request timed out for backend {backend} ({endpoint})");
            return [];
        }
        catch (OperationCanceledException)
        {
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Model listing request was canceled (timeout) for backend {backend} ({endpoint})");
            return [];
        }
        catch (HttpRequestException ex)
        {
            // Handle network connectivity issues
            Logs.Error($"MagicPromptExtension.LLMAPICalls: HTTP request error fetching models for {backend}: {ex.Message}");
            return [];
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Exception fetching models for {backend}: {ex.Message}");
            return [];
        }
    }

    /// <summary>Gets the appropriate endpoint for model listing based on the backend.</summary>
    /// <param name="backend">The LLM backend being used.</param>
    /// <param name="settings">The current settings object.</param>
    /// <param name="endpointType">The type of endpoint (chat, vision, etc.)</param>
    /// <returns>The endpoint URL for model listing.</returns>
    public static string GetEndpoint(string backend, JObject settings, string endpointType)
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
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Invalid configuration for backend {backend}: BaseUrl={baseUrl}, Endpoints={endpoints}");
            return string.Empty;
        }
        string endpoint;
        // Special handling for vision endpoints
        if (endpointType == "vision")
        {
            endpoint = backend.ToLower() switch
            {
                "openrouter" or "openai" or "openaiapi" or "grok" => endpoints["chat"]?.ToString(),// OpenAI, Grok, and OpenRouter use the chat endpoint for vision
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
    public static bool ConfigureRequest(HttpRequestMessage request, string backend, JObject settings, Session session, out string error)
    {
        // Initialize error message as empty
        error = string.Empty;
        // Validate input parameters
        if (request == null)
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "HTTP request object is null");
            return false;
        }
        if (string.IsNullOrEmpty(backend))
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "Backend name is null or empty");
            return false;
        }
        // Get backends configuration
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            error = ErrorHandler.FormatErrorMessage(ErrorType.Generic, "Backend configuration not found");
            return false;
        }
        // Normalize backend name for consistency
        backend = backend.ToLower();
        // Process based on backend type
        switch (backend)
        {
            case "openai":
                string apiKey = session.User.GetGenericData("openai_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openai_api", "key");
                if (string.IsNullOrEmpty(apiKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "OpenAI API Key not found", "openai");
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {apiKey}");
                break;
            case "grok":
                string grokKey = session?.User?.GetGenericData("grok_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("grok_api", "key");
                if (string.IsNullOrEmpty(grokKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "Grok API Key not found", "grok");
                    return false;
                }
                request.Headers.Add("Authorization", $"Bearer {grokKey}");
                break;
            case "openrouter":
                string openRouterKey = session?.User?.GetGenericData("openrouter_api", "key") ?? Program.Sessions.GenericSharedUser.GetGenericData("openrouter_api", "key");
                if (string.IsNullOrEmpty(openRouterKey))
                {
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "OpenRouter API Key not found", "openrouter");
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
                    error = ErrorHandler.FormatErrorMessage(ErrorType.Authentication, "Anthropic API Key not found", "anthropic");
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
                // Handle unsupported backend
                error = ErrorHandler.FormatErrorMessage(ErrorType.Generic,
                    $"Unsupported LLM backend: {backend}. Please select one of the supported backends: Ollama, OpenAI, OpenRouter, or Anthropic.");
                return false;
        }
        return true;
    }

    public static int GetChatBackendTimeoutMs()
    {
        try
        {
            var sessionSettings = SessionSettings.GetMagicPromptSettings().GetAwaiter().GetResult();
            if (sessionSettings?["success"]?.Value<bool>() != true)
            {
                return 90_000;
            }

            var settings = sessionSettings["settings"] as JObject;
            var backend = settings?["backend"]?.ToString()?.ToLower();
            int defaultTimeout = backend == "ollama" || backend == "openaiapi" ? 120 : 60;
            int timeoutSec = GetBackendTimeout(settings, backend, defaultTimeout);

            // Add 30 second buffer for cache waiting (to exceed the actual LLM timeout)
            return (timeoutSec + 30) * 1000;
        }
        catch
        {
            return 90_000; // 90 second fallback
        }
    }

    /// <summary>Gets the timeout value from backend settings, with a fallback default.</summary>
    public static int GetBackendTimeout(JObject settings, string backend, int defaultTimeout = 60)
    {
        if (settings == null || string.IsNullOrEmpty(backend))
        {
            return defaultTimeout;
        }
        JObject backends = settings["backends"] as JObject;
        if (backends == null)
        {
            return defaultTimeout;
        }
        JObject backendConfig = backends[backend.ToLower()] as JObject;
        if (backendConfig == null)
        {
            return defaultTimeout;
        }
        int? timeout = backendConfig["timeout"]?.Value<int?>();
        if (timeout.HasValue && timeout.Value >= 0)
        {
            return timeout.Value;
        }
        return defaultTimeout;
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
    public static async Task<JObject> MagicPromptPhoneHome(JObject requestData, Session session = null)
    {
        try
        {
            long seed = requestData["seed"]?.Value<long>() ?? -1;

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
                    Logs.Error($"MagicPromptExtension.LLMAPICalls: Failed to parse media content: {ex.Message}");
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
            string action = requestData["action"]?.ToString()?.ToLower() ?? "chat";
            if (string.IsNullOrEmpty(modelId))
            {
                return CreateErrorResponse("Model ID is missing");
            }
            if (action != "random-prompt" && string.IsNullOrEmpty(messageContent.Text))
            {
                return CreateErrorResponse("Message content is missing");
            }
            // Get current settings
            JObject sessionSettings = await SessionSettings.GetMagicPromptSettings();
            if (!sessionSettings["success"].Value<bool>())
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: {sessionSettings["error"]?.ToString() ?? "Failed to load settings"}");
                return CreateErrorResponse(sessionSettings["error"]?.ToString() ?? "Failed to load settings");
            }
            JObject settings = sessionSettings["settings"] as JObject;
            if (settings == null)
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Configuration not found. Please check your settings.");
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
                clientProvidedInstructions = action switch
                {
                    "vision" => settings["instructions"]?["vision"]?.ToString() ?? "",
                    "prompt" => settings["instructions"]?["prompt"]?.ToString() ?? "",
                    "caption" => settings["instructions"]?["caption"]?.ToString() ?? "",
                    "generate-instruction" => settings["instructions"]?["instructiongen"]?.ToString() ?? "",
                    "random-prompt" => settings["instructions"]?["randomprompt"]?.ToString() ?? "",
                    _ => settings["instructions"]?["chat"]?.ToString() ?? ""
                };
            }
            messageContent.Instructions = clientProvidedInstructions;
            messageContent.Text = $"{messageContent.Text}";
            // Create request with proper headers and body
            using HttpRequestMessage request = new(HttpMethod.Post, endpoint);
            if (!ConfigureRequest(request, backend, settings, session, out string error))
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: {error}");
                return CreateErrorResponse(error);
            }
            // Add request body using BackendSchema
            object requestBody;
            try
            {
                requestBody = GetSchemaType(backend, messageContent, modelId, messageType, seed);
            }
            catch (ArgumentException ex)
            {
                // Typically thrown for validation issues (e.g., Grok vision requires direct JPG/PNG URLs)
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Request build error for {backend}: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.UnsupportedParameterImage, ex.Message, backend));
            }
            string jsonContent = JsonSerializer.Serialize(requestBody);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
            try
            {
                // Send request and handle response
                // Detailed diagnostics to help trace hanging requests
                Logs.Debug($"[MagicPrompt] Sending request | backend={backend} | type={(messageType == MessageType.Vision ? "vision" : "chat")} | endpoint={endpoint} | model={modelId}");
                int defaultTimeout = backend == "ollama" ? 120 : 60;
                int timeoutSec = GetBackendTimeout(settings, backend, defaultTimeout);
                Logs.Debug($"[MagicPrompt] Using timeout of {timeoutSec} seconds for {backend}");
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
                HttpResponseMessage response = await HttpClient.SendAsync(request, cts.Token);
                string responseContent = await response.Content.ReadAsStringAsync();
                // Handle API errors indicated by status code
                if (!response.IsSuccessStatusCode)
                {
                    // Use the improved error handler to process response
                    string formattedError = ErrorHandler.ProcessErrorResponse(responseContent, response.StatusCode, backend);
                    Logs.Error($"MagicPromptExtension.LLMAPICalls: HTTP {(int)response.StatusCode} error from {backend}: {responseContent}");
                    return CreateErrorResponse(formattedError);
                }
                // Process successful response
                try
                {
                    string llmResponse = await DeserializeResponse(response, backend);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        return CreateSuccessResponse(llmResponse);
                    }
                }
                catch (Exception ex)
                {
                    // Error during successful response deserialization
                    string errorType = ErrorHandler.DetectErrorType(ex.Message, response.StatusCode, backend);
                    string formattedError = ErrorHandler.FormatErrorMessage(errorType, ex.Message, backend);
                    Logs.Error($"MagicPromptExtension.LLMAPICalls: Error deserializing response: {ex.Message}");
                    return CreateErrorResponse(formattedError);
                }
                // If we reach here, something went wrong with a seemingly successful response
                return CreateErrorResponse(
                    ErrorHandler.FormatErrorMessage(ErrorType.Generic,
                    "Response could not be processed properly",
                    backend)
                );
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Request timed out for backend {backend} ({endpoint})");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.RequestTimeout, $"Request exceeded timeout at {endpoint}", backend));
            }
            catch (OperationCanceledException)
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Request canceled (timeout) for backend {backend} ({endpoint})");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.RequestTimeout, $"Request canceled due to timeout at {endpoint}", backend));
            }
            catch (HttpRequestException ex)
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: HTTP request error: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.HttpRequestError, ex.Message, backend));
            }
            catch (Exception ex)
            {
                Logs.Error($"MagicPromptExtension.LLMAPICalls: Error in MagicPromptPhoneHome: {ex.Message}");
                return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.GenericException, ex.Message, backend));
            }
        }
        catch (Exception ex)
        {
            Logs.Error($"MagicPromptExtension.LLMAPICalls: Error in MagicPromptPhoneHome: {ex.Message}");
            return CreateErrorResponse(ErrorHandler.FormatErrorMessage(ErrorType.GenericException, ex.Message, "unknown"));
        }
    }
}
