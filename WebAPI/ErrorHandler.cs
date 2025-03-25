using System;
using System.Collections.Generic;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Newtonsoft.Json;
using SwarmUI.Utils;  // Correct namespace for the Logs class

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    /// <summary>
    /// Centralized error handling for API responses
    /// </summary>
    public static class ErrorHandler
    {
        // Dictionary of error types and their corresponding user-friendly messages
        private static readonly Dictionary<string, string> ErrorTemplates = new()
        {
            ["quota"] = "API Usage Limit Reached\n\n" +
                "Your API usage has reached its limit. This could be due to:\n" +
                "• Your free credits have been exhausted\n" +
                "• You've reached your monthly spending cap\n" +
                "• The backend provider has rate-limited your requests\n\n" +
                "To fix this issue:\n" +
                "1. Wait a few minutes and try again\n" +
                "2. Check your billing settings on the provider's website\n" +
                "3. Consider upgrading your account or increasing your spending limits",

            ["token_limit"] = "Response Length Limit Reached\n\n" +
                "The response was cut off because it reached the maximum allowed length. This happens when:\n" +
                "• The model generated a response that exceeded the token limit\n" +
                "• The combined input and output tokens exceeded the model's context window\n\n" +
                "To get a complete response, try:\n" +
                "1. Making your input prompt shorter\n" +
                "2. Adjusting max_tokens in the API settings\n" +
                "3. Using a model with a larger context window\n" +
                "4. Breaking your request into smaller parts",
            
            ["unsupported_parameter_image"] = "Image Upload Error\n\n" +
                "The API provider doesn't support the way your image was uploaded. This can happen because:\n" +
                "• The selected model doesn't support vision/image inputs\n" +
                "• The image format isn't supported (try JPG or PNG)\n" +
                "• The image is too large (try resizing to under 5MB)\n\n" +
                "To fix this issue:\n" +
                "1. Verify the model supports vision (look for 'vision' in the model name)\n" +
                "2. Try a different image format or resize the image\n" +
                "3. Select a different vision-capable model or provider",
            
            ["max_tokens_parameter"] = "Parameter Error: max_tokens\n\n" +
                "This model doesn't support the 'max_tokens' parameter. This typically happens with:\n" +
                "• Newer OpenAI models that use different parameter naming\n" +
                "• Models that have specific parameter requirements\n\n" +
                "To fix this issue:\n" +
                "1. Try a different model that supports the standard parameters\n" +
                "2. The system will attempt to automatically adjust parameters for you",
            
            ["server_error"] = "Server Error\n\n" +
                "The API provider is experiencing server issues. Try:\n" +
                "• Waiting a few minutes and trying again\n" +
                "• Using a different model\n" +
                "• Checking the provider's status page for outages",
            
            ["generic"] = "API Error\n\n" +
                "Something went wrong with your request. This could be due to:\n" +
                "• Temporary server issues\n" +
                "• Invalid parameters in your request\n" +
                "• Model availability issues\n\n" +
                "Please try again later or select a different model.",
            
            ["generic_exception"] = "Unexpected Error\n\n" +
                "An unexpected error occurred while communicating with the API. This could be due to:\n" +
                "• Network connectivity issues\n" +
                "• The API server being down or unreachable\n" +
                "• A problem with the backend service\n\n" +
                "To fix this issue:\n" +
                "1. Check your internet connection\n" +
                "2. Verify that your LLM backend is running\n" +
                "3. Restart the extension if necessary\n" +
                "4. Check the logs for more detailed information",
            
            ["authentication"] = "API Key Error\n\n" +
                "Failed to authenticate with the API provider. This could be due to:\n" +
                "• Missing API key - you haven't entered one yet\n" +
                "• Invalid API key - the key is incorrect or improperly formatted\n" +
                "• Expired API key - your key is no longer valid\n" +
                "• Account issues - your account may be suspended\n\n" +
                "To fix this issue:\n" +
                "1. Go to the Users tab in SwarmUI\n" +
                "2. Click on the Settings icon for your user\n" +
                "3. Enter or update your API key in the appropriate field\n" +
                "4. If the issue persists, generate a new key from the provider's website",
            
            ["connectivity"] = "Connection Error\n\n" +
                "Cannot connect to the API provider. This could be due to:\n" +
                "• Your internet connection is down or unstable\n" +
                "• The API provider's servers are temporarily unavailable\n" +
                "• A firewall or proxy is blocking the connection\n" +
                "• DNS resolution issues\n\n" +
                "To fix this issue:\n" +
                "1. Check your internet connection\n" +
                "2. Visit the provider's status page to check for outages\n" +
                "3. Try again in a few minutes\n" +
                "4. If using a VPN, try disabling it temporarily",
                
            ["http_request_error"] = "Connection Error\n\n" +
                "Failed to complete the HTTP request to the API provider. This could be due to:\n" +
                "• Your internet connection is down or unstable\n" +
                "• The API endpoint URL is incorrect\n" +
                "• DNS resolution issues\n" +
                "• The service may be temporarily unavailable\n\n" +
                "To fix this issue:\n" +
                "1. Check your internet connection\n" +
                "2. Verify the API endpoint in your configuration\n" +
                "3. Try again in a few minutes\n" +
                "4. Check the provider's status page for any outages",
            
            ["model_not_found"] = "Model Not Found\n\n" +
                "The requested model could not be found or loaded. This could be due to:\n" +
                "• The model name is incorrect or misspelled\n" +
                "• The model has not been downloaded (Ollama)\n" +
                "• The model is no longer available from the provider\n" +
                "• You don't have access to this model with your current subscription\n\n" +
                "To fix this issue:\n" +
                "1. Check that the model name is spelled correctly\n" +
                "2. For Ollama: run 'ollama pull modelname' to download the model\n" +
                "3. Try a different model from the available models list\n" +
                "4. Contact the provider to verify your access to this model",
            
            ["generic_exception"] = "Unexpected Error\n\n" +
                "An unexpected error occurred while communicating with the API. This could be due to:\n" +
                "• Network connectivity issues\n" +
                "• The API server being down or unreachable\n" +
                "• A problem with the backend service\n\n" +
                "To fix this issue:\n" +
                "1. Check your internet connection\n" +
                "2. Verify that your LLM backend is running\n" +
                "3. Restart the extension if necessary\n" +
                "4. Check the logs for more detailed information",
        };

        /// <summary>
        /// Provider-specific overrides for error messages
        /// </summary>
        private static readonly Dictionary<string, Dictionary<string, string>> ProviderSpecificTemplates = new()
        {
            ["openai"] = new Dictionary<string, string>
            {
                ["unsupported_parameter"] = "OpenAI Image Upload Error\n\n" +
                    "The selected OpenAI model doesn't support the way your image was uploaded.\n" +
                    "This can happen because:\n" +
                    "• You're using a model without vision capabilities\n" +
                    "• The image format isn't supported (try JPG or PNG)\n" +
                    "• The image is too large (try resizing to under 20MB)\n\n" +
                    "To fix this issue:\n" +
                    "1. Make sure you're using GPT-4 Vision or a compatible vision model\n" +
                    "2. Check that your image is in the correct format\n" +
                    "3. Try a smaller image"
            },
            
            ["ollama"] = new Dictionary<string, string>
            {
                ["server_error"] = "Ollama Server Error\n\n" +
                    "The Ollama server is experiencing issues. Try:\n" +
                    "• Checking that Ollama is running on your system\n" +
                    "• Restarting the Ollama service\n" +
                    "• Verifying that the model is correctly downloaded\n" +
                    "• Checking system resources (CPU, RAM, disk space)"
            },
            
            ["anthropic"] = new Dictionary<string, string>
            {
                ["token_limit"] = "Claude Token Limit Exceeded\n\n" +
                    "Your request exceeded Claude's token limit. Try:\n" +
                    "• Using Claude-3 Opus or another model with a larger context window\n" +
                    "• Shortening your prompt\n" +
                    "• Reducing the number or size of attached images\n" +
                    "• Breaking your request into smaller chunks"
            }
        };

        /// <summary>
        /// Format an error template into a user-friendly message
        /// </summary>
        /// <param name="errorType">Type of error (quota, token_limit, server_error, etc.)</param>
        /// <param name="originalMessage">Original error message from the API</param>
        /// <param name="backend">Backend provider name (openai, anthropic, ollama, etc.)</param>
        /// <returns>Formatted user-friendly error message</returns>
        public static string FormatErrorMessage(string errorType, string originalMessage = null, string backend = null)
        {
            string template = GetErrorTemplate(errorType, backend);
            string message = template;
            
            if (!string.IsNullOrEmpty(originalMessage))
            {
                message += $"\n\nOriginal error: {originalMessage}";
            }
            
            return message;
        }

        /// <summary>
        /// Get the appropriate error template based on error type and provider
        /// </summary>
        /// <param name="errorType">Type of error (quota, token_limit, server_error, etc.)</param>
        /// <param name="provider">Backend provider name (openai, anthropic, ollama, etc.)</param>
        /// <returns>The appropriate error message template</returns>
        private static string GetErrorTemplate(string errorType, string provider = null)
        {
            // Normalize provider name if provided
            string normalizedProvider = !string.IsNullOrEmpty(provider) ? NormalizeBackendName(provider) : null;
            
            // Check if there's a provider-specific template
            if (!string.IsNullOrEmpty(normalizedProvider) && 
                ProviderSpecificTemplates.TryGetValue(normalizedProvider, out var templates) && 
                templates.TryGetValue(errorType, out string template))
            {
                return template;
            }
            
            // Fallback to standard template
            return ErrorTemplates.TryGetValue(errorType, out string standardTemplate)
                ? standardTemplate
                : ErrorTemplates["generic"];  // Default to generic error
        }

        /// <summary>
        /// Process an error response and return a user-friendly message
        /// </summary>
        public static string ProcessErrorResponse(string responseContent, string backend)
        {
            // Try to detect error type from response content
            string errorType = DetectErrorType(responseContent, backend);
            string originalMessage = ExtractOriginalMessage(responseContent);
            
            // Use normalized backend name
            string provider = NormalizeBackendName(backend);
            
            // Get appropriate template and format message
            string template = GetErrorTemplate(errorType, provider);
            return FormatErrorMessage(template, originalMessage);
        }

        /// <summary>
        /// Try to parse OpenAI error response
        /// </summary>
        public static bool TryParseOpenAIError(string responseContent, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            try
            {
                OpenAIErrorResponse openAIError = JsonConvert.DeserializeObject<OpenAIErrorResponse>(responseContent);
                if (openAIError?.Error != null)
                {
                    // Handle specific OpenAI errors by checking the error message or code
                    if (openAIError.Error.Code != null && openAIError.Error.Code.ToString().Contains("unsupported_parameter"))
                    {
                        if (responseContent.Contains("image") || responseContent.Contains("vision"))
                        {
                            Logs.Error($"OpenAI image upload error: {openAIError.Error.Message}");
                            errorMessage = ErrorTemplates["unsupported_parameter_image"];
                            return true;
                        }
                    }
                    
                    // Check for the specific max_tokens parameter error
                    if (openAIError.Error.Type == "invalid_request_error" && 
                        (openAIError.Error.Message.Contains("max_tokens") && openAIError.Error.Message.Contains("max_completion_tokens")))
                    {
                        Logs.Error($"OpenAI max_tokens parameter error: {openAIError.Error.Message}");
                        errorMessage = ErrorTemplates["max_tokens_parameter"];
                        return true;
                    }
                    
                    // General error message
                    string userFriendlyMessage = $"OpenAI API Error: {openAIError.Error.Message}\n\n";
                    
                    // Add specific handling for common errors
                    if (openAIError.Error.Type == "tokens" || openAIError.Error.Message.Contains("token"))
                    {
                        userFriendlyMessage += ErrorTemplates["token_limit"];
                    }
                    else if (openAIError.Error.Type == "server_error")
                    {
                        userFriendlyMessage += ErrorTemplates["server_error"];
                    }
                    
                    Logs.Error($"OpenAI error: {openAIError.Error.Message} (Type: {openAIError.Error.Type})");
                    errorMessage = userFriendlyMessage;
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error parsing OpenAI error response: {ex.Message}");
            }
            
            return false;
        }

        /// <summary>
        /// Try to parse OpenRouter error response
        /// </summary>
        public static bool TryParseOpenRouterError(string responseContent, out string errorMessage)
        {
            errorMessage = string.Empty;
            
            try
            {
                OpenRouterError errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
                if (errorResponse?.Error != null)
                {
                    string originalMessage = errorResponse.Error.Message;
                    string providerName = errorResponse.Error.Metadata?.ProviderName ?? "OpenRouter";
                    
                    // Special case: For maintenance errors, show the detailed raw message
                    if (errorResponse.Error.Metadata?.Raw?.Contains("maintenance") == true)
                    {
                        errorMessage = errorResponse.Error.Metadata.Raw;
                        Logs.Error($"OpenRouter error (maintenance): {errorMessage}");
                        return true;
                    }
                    
                    // Special case: For "Invalid URL" error, use a more user-friendly message
                    if (originalMessage == "Invalid URL")
                    {
                        errorMessage = "The provided image appears to be invalid or corrupted. Please try uploading a different image.";
                        Logs.Error($"OpenRouter error (invalid URL): {errorMessage}");
                        return true;
                    }
                    
                    // For other errors, use our detection and formatting system
                    string errorType = DetectErrorType(responseContent, "openrouter");
                    
                    // Include the provider name in the error if available
                    string detailedMessage = string.IsNullOrEmpty(errorResponse.Error.Metadata?.Raw)
                        ? originalMessage
                        : $"Error from {providerName}: {originalMessage}";
                    
                    errorMessage = FormatErrorMessage(errorType, detailedMessage, "openrouter");
                    Logs.Error($"OpenRouter error ({errorType}): {originalMessage}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error parsing OpenRouter error response: {ex.Message}");
                // If parsing fails but the content looks like it might be an error, try to extract a message
                if (responseContent.Contains("error") || responseContent.Contains("exception"))
                {
                    string errorType = DetectErrorType(responseContent, "openrouter");
                    errorMessage = FormatErrorMessage(errorType, responseContent, "openrouter");
                    return true;
                }
            }
            
            return false;
        }

        /// <summary>
        /// Attempts to parse Ollama-specific error responses
        /// </summary>
        /// <param name="responseContent">Raw response content</param>
        /// <param name="errorMessage">Extracted error message if successful</param>
        /// <returns>True if successfully parsed, false otherwise</returns>
        public static bool TryParseOllamaError(string responseContent, out string errorMessage)
        {
            errorMessage = null;
            
            try
            {
                // Ollama errors usually have a simple { "error": "message" } format
                var ollamaError = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                if (ollamaError != null && ollamaError.TryGetValue("error", out string error) && !string.IsNullOrEmpty(error))
                {
                    // Check for common Ollama error patterns
                    string errorType = "generic";
                    
                    // Ollama connection errors
                    if (error.Contains("connection refused") || 
                        error.Contains("cannot connect") || 
                        error.Contains("dial tcp"))
                    {
                        errorType = "connectivity";
                    }
                    // Model not found/loaded
                    else if (error.Contains("model") && (error.Contains("not found") || error.Contains("not loaded")))
                    {
                        errorType = "model_not_found";
                    }
                    // Request format issues
                    else if (error.Contains("invalid") || error.Contains("bad request") || error.Contains("unexpected"))
                    {
                        errorType = "request_format";
                    }
                    else
                    {
                        errorType = DetectErrorType(error, "ollama");
                    }
                    
                    errorMessage = FormatErrorMessage(errorType, error, "ollama");
                    return true;
                }
                
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Detect error type from raw response content
        /// </summary>
        public static string DetectErrorType(string responseContent, string provider = null)
        {
            if (string.IsNullOrEmpty(responseContent))
            {
                return "generic";
            }
            
            // Convert to lowercase for easier matching
            string content = responseContent.ToLower();
            
            // Check for authentication and API key errors (highest priority)
            if (content.Contains("unauthorized") || 
                content.Contains("authentication") || 
                content.Contains("api key") || 
                content.Contains("apikey") ||
                content.Contains("auth") ||
                content.Contains("invalid key") ||
                content.Contains("missing api key") ||
                content.Contains("no api key") ||
                content.Contains("not authenticate") ||
                content.Contains("invalid_request_error") ||
                content.Contains("forbidden") ||
                content.Contains("authentication_error") ||
                content.Contains("permission") ||
                content.Contains("401") ||
                content.Contains("403"))
            {
                return "authentication";
            }
            
            // Check for quota/rate limit errors 
            if (content.Contains("quota") || 
                content.Contains("rate limit") || 
                content.Contains("rate_limit") || 
                content.Contains("exceeded") ||
                content.Contains("too many requests") ||
                content.Contains("usage_limit") ||
                content.Contains("429"))
            {
                return "quota";
            }
            
            // Check for token limit errors
            if ((content.Contains("token") && (content.Contains("limit") || content.Contains("exceed"))) || 
                content.Contains("too long") || 
                content.Contains("context_length_exceeded") ||
                content.Contains("maximum context length"))
            {
                return "token_limit";
            }
            
            // Check for server errors
            if (content.Contains("server_error") || 
                content.Contains("500") || 
                content.Contains("503") || 
                content.Contains("internal server error") ||
                content.Contains("maintenance") ||
                content.Contains("unavailable"))
            {
                return "server_error";
            }
            
            // Check for image/vision related errors
            if (content.Contains("image") && 
                (content.Contains("error") || content.Contains("invalid")))
            {
                return "unsupported_parameter_image";
            }
            
            // Check for max_tokens parameter errors
            if (content.Contains("max_tokens") && content.Contains("max_completion_tokens"))
            {
                return "max_tokens_parameter";
            }
            
            // Check for connectivity errors
            if (content.Contains("connection") || 
                content.Contains("connect") || 
                content.Contains("network") || 
                content.Contains("dns"))
            {
                return "connectivity";
            }
            
            // Check for HTTP request errors
            if (content.Contains("http") || 
                content.Contains("request") || 
                content.Contains("httprequest") || 
                content.Contains("http request"))
            {
                return "http_request_error";
            }
            
            // Check for model not found errors
            if (content.Contains("model") && (content.Contains("not found") || content.Contains("not loaded")))
            {
                return "model_not_found";
            }
            
            // Default to generic error
            return "generic";
        }

        /// <summary>
        /// Extract original error message from response content
        /// </summary>
        private static string ExtractOriginalMessage(string responseContent)
        {
            try
            {
                // Try to extract from JSON
                dynamic json = JsonConvert.DeserializeObject(responseContent);
                
                if (json?.error?.message != null)
                {
                    return json.error.message.ToString();
                }
                else if (json?.message != null)
                {
                    return json.message.ToString();
                }
            }
            catch
            {
                // Not valid JSON or doesn't have the expected structure
            }
            
            // Return truncated original content if we can't extract a specific message
            return responseContent.Length > 100 ? responseContent.Substring(0, 100) + "..." : responseContent;
        }

        /// <summary>
        /// Normalize backend name for error template lookup
        /// </summary>
        private static string NormalizeBackendName(string backend)
        {
            if (string.IsNullOrEmpty(backend))
            {
                return null;
            }
            
            backend = backend.ToLower();
            
            if (backend == "openaiapi" || backend == "openai-api")
            {
                return "openai";
            }
            else if (backend == "anthropicapi" || backend == "anthropic-api")
            {
                return "anthropic";
            }
            
            return backend;
        }
    }

    /// <summary>
    /// Template for formatting error messages
    /// </summary>
    public class ErrorTemplate
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public string[] Causes { get; set; }
        public string[] Solutions { get; set; }
    }
}
