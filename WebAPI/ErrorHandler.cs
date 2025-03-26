using System;
using System.Collections.Generic;
using System.Linq;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Newtonsoft.Json;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI
{
    /// <summary>Defines the type of error that occurred during API operations</summary>
    public enum ErrorType
    {
        /// <summary>Default error type for generic errors</summary>
        Generic,

        /// <summary>Authentication or API key related errors</summary>
        Authentication,

        /// <summary>API usage quota or rate limit exceeded</summary>
        Quota,

        /// <summary>Token limit or context length exceeded</summary>
        TokenLimit,

        /// <summary>Server-side errors from the provider</summary>
        ServerError,

        /// <summary>Issues with image uploads or vision features</summary>
        UnsupportedParameterImage,

        /// <summary>Issues with max_tokens parameter</summary>
        MaxTokensParameter,

        /// <summary>Network connectivity issues</summary>
        Connectivity,

        /// <summary>HTTP request errors</summary>
        HttpRequestError,

        /// <summary>Model not found or not loaded</summary>
        ModelNotFound,

        /// <summary>Unexpected exceptions during processing</summary>
        GenericException
    }

    /// <summary>Interface for error handling services to allow for dependency injection and testing</summary>
    public interface IErrorHandler
    {
        /// <summary>Formats an error message based on error type and provider</summary>
        string FormatErrorMessage(ErrorType errorType, string originalMessage = null, string backend = null);

        /// <summary>Processes an error response and returns a user-friendly message</summary>
        string ProcessErrorResponse(string responseContent, string backend);

        /// <summary>Attempts to detect the error type from response content</summary>
        ErrorType DetectErrorTypeEnum(string responseContent, string provider = null);

        /// <summary>Attempts to parse an OpenAI error response</summary>
        bool TryParseOpenAIError(string responseContent, out string errorMessage);

        /// <summary>Attempts to parse an OpenRouter error response</summary>
        bool TryParseOpenRouterError(string responseContent, out string errorMessage);

        /// <summary>Attempts to parse an Ollama error response</summary>
        bool TryParseOllamaError(string responseContent, out string errorMessage);
    }

    /// <summary>Centralized error handling for API responses</summary>
    public static class ErrorHandler
    {
        public static readonly ErrorHandlerImplementation _instance = new();

        /// <summary>Format an error template into a user-friendly message</summary>
        /// <param name="errorType">Type of error (quota, token_limit, server_error, etc.)</param>
        /// <param name="originalMessage">Original error message from the API</param>
        /// <param name="backend">Backend provider name (openai, anthropic, ollama, etc.)</param>
        /// <returns>Formatted user-friendly error message</returns>
        public static string FormatErrorMessage(string errorType, string originalMessage = null, string backend = null)
        {
            ErrorType parsedType = ConvertStringToErrorType(errorType);
            return _instance.FormatErrorMessage(parsedType, originalMessage, backend);
        }

        /// <summary>Process an error response and return a user-friendly message</summary>
        public static string ProcessErrorResponse(string responseContent, string backend)
        {
            return _instance.ProcessErrorResponse(responseContent, backend);
        }

        /// <summary>Try to parse OpenAI error response</summary>
        public static bool TryParseOpenAIError(string responseContent, out string errorMessage)
        {
            return _instance.TryParseOpenAIError(responseContent, out errorMessage);
        }

        /// <summary>Try to parse OpenRouter error response</summary>
        public static bool TryParseOpenRouterError(string responseContent, out string errorMessage)
        {
            return _instance.TryParseOpenRouterError(responseContent, out errorMessage);
        }

        /// <summary>Attempts to parse Ollama-specific error responses</summary>
        /// <param name="responseContent">Raw response content</param>
        /// <param name="errorMessage">Extracted error message if successful</param>
        /// <returns>True if successfully parsed, false otherwise</returns>
        public static bool TryParseOllamaError(string responseContent, out string errorMessage)
        {
            return _instance.TryParseOllamaError(responseContent, out errorMessage);
        }

        /// <summary>Detect error type from raw response content</summary>
        public static string DetectErrorType(string responseContent, string provider = null)
        {
            ErrorType errorType = _instance.DetectErrorTypeEnum(responseContent, provider);
            return ConvertErrorTypeToString(errorType);
        }

        /// <summary>Converts an ErrorType enum value to its string representation</summary>
        public static string ConvertErrorTypeToString(ErrorType errorType)
        {
            return errorType switch
            {
                ErrorType.Authentication => "authentication",
                ErrorType.Quota => "quota",
                ErrorType.TokenLimit => "token_limit",
                ErrorType.ServerError => "server_error",
                ErrorType.UnsupportedParameterImage => "unsupported_parameter_image",
                ErrorType.MaxTokensParameter => "max_tokens_parameter",
                ErrorType.Connectivity => "connectivity",
                ErrorType.HttpRequestError => "http_request_error",
                ErrorType.ModelNotFound => "model_not_found",
                ErrorType.GenericException => "generic_exception",
                _ => "generic"
            };
        }

        /// <summary>Converts a string error type to its ErrorType enum representation</summary>
        public static ErrorType ConvertStringToErrorType(string errorType)
        {
            if (string.IsNullOrEmpty(errorType)) return ErrorType.Generic;

            return errorType.ToLower() switch
            {
                "authentication" => ErrorType.Authentication,
                "quota" => ErrorType.Quota,
                "token_limit" => ErrorType.TokenLimit,
                "server_error" => ErrorType.ServerError,
                "unsupported_parameter_image" => ErrorType.UnsupportedParameterImage,
                "max_tokens_parameter" => ErrorType.MaxTokensParameter,
                "connectivity" => ErrorType.Connectivity,
                "http_request_error" => ErrorType.HttpRequestError,
                "model_not_found" => ErrorType.ModelNotFound,
                "generic_exception" => ErrorType.GenericException,
                _ => ErrorType.Generic
            };
        }
    }

    /// <summary>Implementation of the IErrorHandler interface</summary>
    public class ErrorHandlerImplementation : IErrorHandler
    {
        /// <summary>Represents a structured error template</summary>
        private class ErrorTemplateObject(string title, string description, string[] causes, string[] solutions)
        {
            public string Title { get; } = title;
            public string Description { get; } = description;
            public string[] Causes { get; } = causes;
            public string[] Solutions { get; } = solutions;

            public override string ToString()
            {
                string causesText = Causes.Length > 0 ? $"\n\n{string.Join("\n", Causes.Select(c => $"• {c}"))}" : "";
                string solutionsText = Solutions.Length > 0 ?
                    $"\n\nTo fix this issue:\n{string.Join("\n", Solutions.Select((s, i) => $"{i + 1}. {s}"))}" : "";
                return $"{Title}\n\n{Description}{causesText}{solutionsText}";
            }
        }
        // Dictionary of error types and their corresponding user-friendly templates
        private readonly Dictionary<ErrorType, ErrorTemplateObject> _errorTemplates;
        // Provider-specific overrides for error messages
        private readonly Dictionary<string, Dictionary<ErrorType, ErrorTemplateObject>> _providerSpecificTemplates;

        public ErrorHandlerImplementation()
        {
            // Initialize error templates
            _errorTemplates = new Dictionary<ErrorType, ErrorTemplateObject>
            {
                [ErrorType.Quota] = new ErrorTemplateObject(
                    "API Usage Limit Reached",
                    "Your API usage has reached its limit.",
                    [
                        "Your free credits have been exhausted",
                        "You've reached your monthly spending cap",
                        "The backend provider has rate-limited your requests"
                    ],
                    [
                        "Wait a few minutes and try again",
                        "Check your billing settings on the provider's website",
                        "Consider upgrading your account or increasing your spending limits"
                    ]
                ),
                [ErrorType.TokenLimit] = new ErrorTemplateObject(
                    "Response Length Limit Reached",
                    "The response was cut off because it reached the maximum allowed length.",
                    [
                        "The model generated a response that exceeded the token limit",
                        "The combined input and output tokens exceeded the model's context window"
                    ],
                    [
                        "Making your input prompt shorter",
                        "Adjusting max_tokens in the API settings",
                        "Using a model with a larger context window",
                        "Breaking your request into smaller parts"
                    ]
                ),
                [ErrorType.UnsupportedParameterImage] = new ErrorTemplateObject(
                    "Image Upload Error",
                    "The API provider doesn't support the way your image was uploaded.",
                    [
                        "The selected model doesn't support vision/image inputs",
                        "The image format isn't supported (try JPG or PNG)",
                        "The image is too large (try resizing to under 5MB)"
                    ],
                    [
                        "Verify the model supports vision (look for 'vision' in the model name)",
                        "Try a different image format or resize the image",
                        "Select a different vision-capable model or provider"
                    ]
                ),
                [ErrorType.MaxTokensParameter] = new ErrorTemplateObject(
                    "Parameter Error: max_tokens",
                    "This model doesn't support the 'max_tokens' parameter.",
                    [
                        "Newer OpenAI models that use different parameter naming",
                        "Models that have specific parameter requirements"
                    ],
                    [
                        "Try a different model that supports the standard parameters",
                        "The system will attempt to automatically adjust parameters for you"
                    ]
                ),
                [ErrorType.ServerError] = new ErrorTemplateObject(
                    "Server Error",
                    "The API provider is experiencing server issues.",
                    Array.Empty<string>(),
                    [
                        "Waiting a few minutes and trying again",
                        "Using a different model",
                        "Checking the provider's status page for outages"
                    ]
                ),
                [ErrorType.Generic] = new ErrorTemplateObject(
                    "API Error",
                    "Something went wrong with your request.",
                    [
                        "Temporary server issues",
                        "Invalid parameters in your request",
                        "Model availability issues"
                    ],
                    [
                        "Please try again later or select a different model."
                    ]
                ),
                [ErrorType.GenericException] = new ErrorTemplateObject(
                    "Unexpected Error",
                    "An unexpected error occurred while communicating with the API.",
                    [
                        "Network connectivity issues",
                        "The API server being down or unreachable",
                        "A problem with the backend service"
                    ],
                    [
                        "Check your internet connection",
                        "Verify that your LLM backend is running",
                        "Restart the extension if necessary",
                        "Check the logs for more detailed information"
                    ]
                ),
                [ErrorType.Authentication] = new ErrorTemplateObject(
                    "API Key Error",
                    "Failed to authenticate with the API provider.",
                    [
                        "Missing API key - you haven't entered one yet",
                        "Invalid API key - the key is incorrect or improperly formatted",
                        "Expired API key - your key is no longer valid",
                        "Account issues - your account may be suspended"
                    ],
                    [
                        "Go to the Users tab in SwarmUI",
                        "Click on the Settings icon for your user",
                        "Enter or update your API key in the appropriate field",
                        "If the issue persists, generate a new key from the provider's website"
                    ]
                ),
                [ErrorType.Connectivity] = new ErrorTemplateObject(
                    "Connection Error",
                    "Cannot connect to the API provider.",
                    [
                        "Your internet connection is down or unstable",
                        "The API provider's servers are temporarily unavailable",
                        "A firewall or proxy is blocking the connection",
                        "DNS resolution issues"
                    ],
                    [
                        "Check your internet connection",
                        "Visit the provider's status page to check for outages",
                        "Try again in a few minutes",
                        "If using a VPN, try disabling it temporarily"
                    ]
                ),
                [ErrorType.HttpRequestError] = new ErrorTemplateObject(
                    "Connection Error",
                    "Failed to complete the HTTP request to the API provider.",
                    [
                        "Your internet connection is down or unstable",
                        "The API endpoint URL is incorrect",
                        "DNS resolution issues",
                        "The service may be temporarily unavailable"
                    ],
                    [
                        "Check your internet connection",
                        "Verify the API endpoint in your configuration",
                        "Try again in a few minutes",
                        "Check the provider's status page for any outages"
                    ]
                ),
                [ErrorType.ModelNotFound] = new ErrorTemplateObject(
                    "Model Not Found",
                    "The requested model could not be found or loaded.",
                    [
                        "The model name is incorrect or misspelled",
                        "The model has not been downloaded (Ollama)",
                        "The model is no longer available from the provider",
                        "You don't have access to this model with your current subscription"
                    ],
                    [
                        "Check that the model name is spelled correctly",
                        "For Ollama: run 'ollama pull modelname' to download the model",
                        "Try a different model from the available models list",
                        "Contact the provider to verify your access to this model"
                    ]
                )
            };
            // Initialize provider-specific templates
            _providerSpecificTemplates = new Dictionary<string, Dictionary<ErrorType, ErrorTemplateObject>>
            {
                ["openai"] = new Dictionary<ErrorType, ErrorTemplateObject>
                {
                    [ErrorType.UnsupportedParameterImage] = new ErrorTemplateObject(
                        "OpenAI Image Upload Error",
                        "The selected OpenAI model doesn't support the way your image was uploaded.",
                        [
                            "You're using a model without vision capabilities",
                            "The image format isn't supported (try JPG or PNG)",
                            "The image is too large (try resizing to under 20MB)"
                        ],
                        [
                            "Make sure you're using GPT-4 Vision or a compatible vision model",
                            "Check that your image is in the correct format",
                            "Try a smaller image"
                        ]
                    )
                },
                ["ollama"] = new Dictionary<ErrorType, ErrorTemplateObject>
                {
                    [ErrorType.ServerError] = new ErrorTemplateObject(
                        "Ollama Server Error",
                        "The Ollama server is experiencing issues.",
                        Array.Empty<string>(),
                        [
                            "Checking that Ollama is running on your system",
                            "Restarting the Ollama service",
                            "Verifying that the model is correctly downloaded",
                            "Checking system resources (CPU, RAM, disk space)"
                        ]
                    )
                },
                ["anthropic"] = new Dictionary<ErrorType, ErrorTemplateObject>
                {
                    [ErrorType.TokenLimit] = new ErrorTemplateObject(
                        "Claude Token Limit Exceeded",
                        "Your request exceeded Claude's token limit.",
                        Array.Empty<string>(),
                        [
                            "Using Claude-3 Opus or another model with a larger context window",
                            "Shortening your prompt",
                            "Reducing the number or size of attached images",
                            "Breaking your request into smaller chunks"
                        ]
                    )
                }
            };
        }

        /// <summary>Format an error template into a user-friendly message</summary>
        public string FormatErrorMessage(ErrorType errorType, string originalMessage = null, string backend = null)
        {
            ErrorTemplateObject template = GetErrorTemplate(errorType, backend);
            string message = template.ToString();
            if (!string.IsNullOrEmpty(originalMessage))
            {
                message += $"\n\nOriginal error: {originalMessage}";
            }
            return message;
        }

        /// <summary>Get the appropriate error template based on error type and provider</summary>
        private ErrorTemplateObject GetErrorTemplate(ErrorType errorType, string provider = null)
        {
            // Normalize provider name if provided
            string normalizedProvider = !string.IsNullOrEmpty(provider) ? NormalizeBackendName(provider) : null;
            // Check if there's a provider-specific template
            if (!string.IsNullOrEmpty(normalizedProvider) &&
                _providerSpecificTemplates.TryGetValue(normalizedProvider, out var templates) &&
                templates.TryGetValue(errorType, out var template))
            {
                return template;
            }
            // Fallback to standard template
            return _errorTemplates.TryGetValue(errorType, out var standardTemplate)
                ? standardTemplate
                : _errorTemplates[ErrorType.Generic];  // Default to generic error
        }

        /// <summary>Process an error response and return a user-friendly message</summary>
        public string ProcessErrorResponse(string responseContent, string backend)
        {
            if (string.IsNullOrEmpty(responseContent))
            {
                return FormatErrorMessage(ErrorType.Generic, "Empty response", backend);
            }
            // Try to detect error type from response content
            ErrorType errorType = DetectErrorTypeEnum(responseContent, backend);
            string originalMessage = ExtractOriginalMessage(responseContent);
            // Use normalized backend name
            string provider = NormalizeBackendName(backend);
            // Return formatted message
            return FormatErrorMessage(errorType, originalMessage, provider);
        }

        /// <summary>Try to parse OpenAI error response</summary>
        public bool TryParseOpenAIError(string responseContent, out string errorMessage)
        {
            errorMessage = string.Empty;

            if (string.IsNullOrEmpty(responseContent))
            {
                return false;
            }
            try
            {
                OpenAIErrorResponse openAIError = JsonConvert.DeserializeObject<OpenAIErrorResponse>(responseContent);
                if (openAIError?.Error != null)
                {
                    string detectedErrorType = string.Empty;
                    // Handle specific OpenAI errors by checking the error message or code
                    if (openAIError.Error.Code != null && openAIError.Error.Code.ToString().Contains("unsupported_parameter"))
                    {
                        if (responseContent.Contains("image") || responseContent.Contains("vision"))
                        {
                            Logs.Error($"OpenAI image upload error: {openAIError.Error.Message}");
                            errorMessage = GetErrorTemplate(ErrorType.UnsupportedParameterImage, "openai").ToString();
                            return true;
                        }
                    }
                    // Check for the specific max_tokens parameter error
                    if (openAIError.Error.Type == "invalid_request_error" &&
                        (openAIError.Error.Message.Contains("max_tokens") && openAIError.Error.Message.Contains("max_completion_tokens")))
                    {
                        Logs.Error($"OpenAI max_tokens parameter error: {openAIError.Error.Message}");
                        errorMessage = GetErrorTemplate(ErrorType.MaxTokensParameter, null).ToString();
                        return true;
                    }
                    // Determine error type based on the message
                    ErrorType errorType;
                    if (openAIError.Error.Type == "tokens" || openAIError.Error.Message.Contains("token"))
                    {
                        errorType = ErrorType.TokenLimit;
                    }
                    else if (openAIError.Error.Type == "server_error")
                    {
                        errorType = ErrorType.ServerError;
                    }
                    else
                    {
                        errorType = ErrorType.Generic;
                    }
                    // Create the error message with original error included
                    errorMessage = FormatErrorMessage(
                        errorType,
                        $"OpenAI API Error: {openAIError.Error.Message} (Type: {openAIError.Error.Type})",
                        "openai"
                    );
                    Logs.Error($"OpenAI error: {openAIError.Error.Message} (Type: {openAIError.Error.Type})");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Logs.Error($"Error parsing OpenAI error response: {ex.Message}");
            }
            return false;
        }

        /// <summary>Try to parse OpenRouter error response</summary>
        public bool TryParseOpenRouterError(string responseContent, out string errorMessage)
        {
            errorMessage = string.Empty;
            if (string.IsNullOrEmpty(responseContent))
            {
                return false;
            }
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
                    ErrorType errorType = DetectErrorTypeEnum(responseContent, "openrouter");
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
                    ErrorType errorType = DetectErrorTypeEnum(responseContent, "openrouter");
                    errorMessage = FormatErrorMessage(errorType, responseContent, "openrouter");
                    return true;
                }
            }
            return false;
        }

        /// <summary>Attempts to parse Ollama-specific error responses</summary>
        public bool TryParseOllamaError(string responseContent, out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrEmpty(responseContent))
            {
                return false;
            }
            try
            {
                // Ollama errors usually have a simple { "error": "message" } format
                Dictionary<string, string> ollamaError = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
                if (ollamaError != null && ollamaError.TryGetValue("error", out string error) && !string.IsNullOrEmpty(error))
                {
                    // Check for common Ollama error patterns
                    ErrorType errorType = ErrorType.Generic;
                    // Ollama connection errors
                    if (error.Contains("connection refused") ||
                        error.Contains("cannot connect") ||
                        error.Contains("dial tcp"))
                    {
                        errorType = ErrorType.Connectivity;
                    }
                    // Model not found/loaded
                    else if (error.Contains("model") && (error.Contains("not found") || error.Contains("not loaded")))
                    {
                        errorType = ErrorType.ModelNotFound;
                    }
                    // Request format issues, use generic
                    else if (error.Contains("invalid") || error.Contains("bad request") || error.Contains("unexpected"))
                    {
                        errorType = ErrorType.Generic;
                    }
                    else
                    {
                        errorType = DetectErrorTypeEnum(error, "ollama");
                    }
                    errorMessage = FormatErrorMessage(errorType, error, "ollama");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error parsing Ollama error response: {ex.Message}");
                return false;
            }
        }

        /// <summary>Detect error type from raw response content</summary>
        public ErrorType DetectErrorTypeEnum(string responseContent, string provider = null)
        {
            if (string.IsNullOrEmpty(responseContent))
            {
                return ErrorType.Generic;
            }
            // Convert to lowercase for easier matching
            string content = responseContent.ToLower();
            // Define error signature matchers - ordered by priority
            Dictionary<ErrorType, Func<string, bool>> errorSignatures = new()
            {
                [ErrorType.Authentication] = c =>
                    c.Contains("unauthorized") ||
                    c.Contains("authentication") ||
                    c.Contains("api key") ||
                    c.Contains("apikey") ||
                    c.Contains("auth") ||
                    c.Contains("invalid key") ||
                    c.Contains("missing api key") ||
                    c.Contains("no api key") ||
                    c.Contains("not authenticate") ||
                    c.Contains("invalid_request_error") ||
                    c.Contains("forbidden") ||
                    c.Contains("authentication_error") ||
                    c.Contains("permission") ||
                    c.Contains("401") ||
                    c.Contains("403"),
                [ErrorType.Quota] = c =>
                    c.Contains("quota") ||
                    c.Contains("rate limit") ||
                    c.Contains("rate_limit") ||
                    c.Contains("exceeded") ||
                    c.Contains("too many requests") ||
                    c.Contains("usage_limit") ||
                    c.Contains("429"),
                [ErrorType.TokenLimit] = c =>
                    (c.Contains("token") && (c.Contains("limit") || c.Contains("exceed"))) ||
                    c.Contains("too long") ||
                    c.Contains("context_length_exceeded") ||
                    c.Contains("maximum context length"),
                [ErrorType.ServerError] = c =>
                    c.Contains("server_error") ||
                    c.Contains("500") ||
                    c.Contains("503") ||
                    c.Contains("internal server error") ||
                    c.Contains("maintenance") ||
                    c.Contains("unavailable"),
                [ErrorType.UnsupportedParameterImage] = c =>
                    c.Contains("image") &&
                    (c.Contains("error") || c.Contains("invalid")),
                [ErrorType.MaxTokensParameter] = c =>
                    c.Contains("max_tokens") && c.Contains("max_completion_tokens"),
                [ErrorType.Connectivity] = c =>
                    c.Contains("connection") ||
                    c.Contains("connect") ||
                    c.Contains("network") ||
                    c.Contains("dns"),
                [ErrorType.HttpRequestError] = c =>
                    c.Contains("http") ||
                    c.Contains("request") ||
                    c.Contains("httprequest") ||
                    c.Contains("http request"),
                [ErrorType.ModelNotFound] = c =>
                    c.Contains("model") && (c.Contains("not found") || c.Contains("not loaded"))
            };
            // Check against signatures in priority order
            foreach (var signature in errorSignatures)
            {
                if (signature.Value(content))
                {
                    return signature.Key;
                }
            }
            // Default to generic error
            return ErrorType.Generic;
        }

        /// <summary>Extract original error message from response content</summary>
        public string ExtractOriginalMessage(string responseContent)
        {
            if (string.IsNullOrEmpty(responseContent))
            {
                return "Empty response";
            }
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

        /// <summary>Normalize backend name for error template lookup</summary>
        public string NormalizeBackendName(string backend)
        {
            if (string.IsNullOrEmpty(backend))
            {
                return null;
            }
            backend = backend.ToLower();
            return backend switch
            {
                "openaiapi" or "openai-api" => "openai",
                "anthropicapi" or "anthropic-api" => "anthropic",
                _ => backend
            };
        }
    }
}