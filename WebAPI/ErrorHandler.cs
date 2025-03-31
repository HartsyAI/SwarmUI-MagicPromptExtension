using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Newtonsoft.Json;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI;

/// <summary>Defines the type of error that occurred during API operations.
/// These categories help classify errors across different LLM providers.</summary>
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
    GenericException,
    /// <summary>Content moderation or safety filtering issues</summary>
    ContentModeration,
    /// <summary>Request timeout errors</summary>
    RequestTimeout
}

/// <summary>Interface for error handling services to allow for dependency injection and testing</summary>
public interface IErrorHandler
{
    /// <summary>Formats an error message based on error type and provider</summary>
    string FormatErrorMessage(ErrorType errorType, string originalMessage = null, string backend = null);
    /// <summary>Processes an error response and returns a user-friendly message</summary>
    string ProcessErrorResponse(string responseContent, HttpStatusCode statusCode, string backend);
    /// <summary>Attempts to detect the error type from response content and HTTP status code</summary>
    ErrorType DetectErrorType(string responseContent, HttpStatusCode statusCode, string provider = null);
}

/// <summary>Provides centralized error handling for API responses.
/// Acts as a facade for the ErrorHandlerImplementation class.</summary>
public static class ErrorHandler
{
    private static readonly ErrorHandlerImplementation _instance = new();

    /// <summary>Format an error template into a user-friendly message</summary>
    public static string FormatErrorMessage(string errorType, string originalMessage = null, string backend = null)
    {
        ErrorType parsedType = ConvertStringToErrorType(errorType);
        return _instance.FormatErrorMessage(parsedType, originalMessage, backend);
    }

    /// <summary>Format an error template into a user-friendly message</summary>
    public static string FormatErrorMessage(ErrorType errorType, string originalMessage = null, string backend = null)
    {
        return _instance.FormatErrorMessage(errorType, originalMessage, backend);
    }

    /// <summary>Process an error response and return a user-friendly message</summary>
    public static string ProcessErrorResponse(string responseContent, HttpStatusCode statusCode, string backend)
    {
        return _instance.ProcessErrorResponse(responseContent, statusCode, backend);
    }

    /// <summary>Try to parse OpenAI error response</summary>
    public static bool TryParseOpenAIError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        return _instance.TryParseOpenAIError(responseContent, statusCode, out errorType, out errorMessage);
    }

    /// <summary>Try to parse OpenRouter error response</summary>
    public static bool TryParseOpenRouterError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        return _instance.TryParseOpenRouterError(responseContent, statusCode, out errorType, out errorMessage);
    }

    /// <summary>Attempts to parse Ollama-specific error responses</summary>
    public static bool TryParseOllamaError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        return _instance.TryParseOllamaError(responseContent, statusCode, out errorType, out errorMessage);
    }

    /// <summary>Try to parse Anthropic error response</summary>
    public static bool TryParseAnthropicError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        return _instance.TryParseAnthropicError(responseContent, statusCode, out errorType, out errorMessage);
    }

    /// <summary>Detect error type from raw response content and HTTP status code</summary>
    public static string DetectErrorType(string responseContent, HttpStatusCode statusCode, string provider = null)
    {
        ErrorType errorType = _instance.DetectErrorType(responseContent, statusCode, provider);
        return ConvertErrorTypeToString(errorType);
    }

    /// <summary>Converts an ErrorType enum value to its string representation</summary>
    public static string ConvertErrorTypeToString(ErrorType errorType) => errorType switch
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
        ErrorType.ContentModeration => "content_moderation",
        ErrorType.RequestTimeout => "request_timeout",
        _ => "generic"
    };

    /// <summary>Converts a string error type to its ErrorType enum representation</summary>
    public static ErrorType ConvertStringToErrorType(string errorType)
    {
        if (string.IsNullOrEmpty(errorType))
        {
            return ErrorType.Generic;
        }
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
            "content_moderation" => ErrorType.ContentModeration,
            "request_timeout" => ErrorType.RequestTimeout,
            _ => ErrorType.Generic
        };
    }
}

/// <summary>Implementation of the IErrorHandler interface.
/// Provides structured error parsing and handling for various LLM providers.</summary>
public class ErrorHandlerImplementation : IErrorHandler
{
    /// <summary>Represents a structured error template with title, description, causes, and solutions</summary>
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

    private readonly Dictionary<ErrorType, ErrorTemplateObject> _errorTemplates;
    private readonly Dictionary<string, Dictionary<ErrorType, ErrorTemplateObject>> _providerSpecificTemplates;
    private readonly Dictionary<HttpStatusCode, Dictionary<string, ErrorType>> _statusCodeMappings;

    /// <summary>Initialize the error handler implementation with templates and mappings</summary>
    public ErrorHandlerImplementation()
    {
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
                [],
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
            ),
            [ErrorType.ContentModeration] = new ErrorTemplateObject(
                "Content Moderation",
                "Your request was flagged by content moderation filters.",
                [
                    "Your prompt contains content that violates the provider's content policy",
                    "The model provider has strict content filters in place",
                    "Your request may contain sensitive topics or prohibited content"
                ],
                [
                    "Review your prompt and remove or rephrase any potentially sensitive content",
                    "Try a different provider with less strict content filters",
                    "Break complex prompts into smaller, less ambiguous parts"
                ]
            ),
            [ErrorType.RequestTimeout] = new ErrorTemplateObject(
                "Request Timeout",
                "The request took too long to complete and timed out.",
                [
                    "The model is taking too long to generate a response",
                    "The provider's servers are overloaded",
                    "Network latency issues"
                ],
                [
                    "Try again with a shorter prompt",
                    "Use a different model that might be faster",
                    "Try again at a less busy time",
                    "Check your network connection"
                ]
            )
        };
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
                ),
                [ErrorType.Authentication] = new ErrorTemplateObject(
                    "OpenAI API Key Error",
                    "Failed to authenticate with the OpenAI API.",
                    [
                        "Missing or invalid API key",
                        "Your OpenAI API key may have expired",
                        "Your OpenAI account may have billing issues"
                    ],
                    [
                        "Go to the Users tab in SwarmUI",
                        "Click on the Settings icon for your user",
                        "Enter or update your OpenAI API key",
                        "Verify your API key at https://platform.openai.com/api-keys",
                        "Check your OpenAI account billing status"
                    ]
                )
            },
            ["ollama"] = new Dictionary<ErrorType, ErrorTemplateObject>
            {
                [ErrorType.ServerError] = new ErrorTemplateObject(
                    "Ollama Server Error",
                    "The Ollama server is experiencing issues.",
                    [],
                    [
                        "Checking that Ollama is running on your system",
                        "Restarting the Ollama service",
                        "Verifying that the model is correctly downloaded",
                        "Checking system resources (CPU, RAM, disk space)"
                    ]
                ),
                [ErrorType.ModelNotFound] = new ErrorTemplateObject(
                    "Ollama Model Not Found",
                    "The requested model could not be found or loaded by Ollama.",
                    [
                        "The model hasn't been downloaded yet",
                        "The model name is misspelled",
                        "The model file might be corrupted"
                    ],
                    [
                        "Run 'ollama pull modelname' in your terminal to download the model",
                        "Check the model name against the available models list",
                        "Restart the Ollama service and try again",
                        "View available models with 'ollama list'"
                    ]
                ),
                [ErrorType.Connectivity] = new ErrorTemplateObject(
                    "Ollama Connection Error",
                    "Cannot connect to the Ollama server.",
                    [
                        "The Ollama service is not running",
                        "Ollama is running on a different port than expected",
                        "Incorrect API URL configuration"
                    ],
                    [
                        "Ensure Ollama is running by checking for the process",
                        "Verify the Ollama URL in MagicPrompt settings (default: http://localhost:11434)",
                        "Restart the Ollama service",
                        "Check if any other application is using the same port"
                    ]
                )
            },
            ["anthropic"] = new Dictionary<ErrorType, ErrorTemplateObject>
            {
                [ErrorType.TokenLimit] = new ErrorTemplateObject(
                    "Claude Token Limit Exceeded",
                    "Your request exceeded Claude's token limit.",
                    [],
                    [
                        "Using Claude-3 Opus or another model with a larger context window",
                        "Shortening your prompt",
                        "Reducing the number or size of attached images",
                        "Breaking your request into smaller chunks"
                    ]
                ),
                [ErrorType.Authentication] = new ErrorTemplateObject(
                    "Anthropic API Key Error",
                    "Failed to authenticate with the Anthropic API.",
                    [
                        "Missing or invalid API key",
                        "Your Anthropic API key may have expired",
                        "Your Anthropic account may have issues"
                    ],
                    [
                        "Go to the Users tab in SwarmUI",
                        "Click on the Settings icon for your user",
                        "Enter or update your Anthropic API key",
                        "Verify your API key at https://console.anthropic.com/settings/keys",
                        "Check your Anthropic account status"
                    ]
                )
            },
            ["openrouter"] = new Dictionary<ErrorType, ErrorTemplateObject>
            {
                [ErrorType.Quota] = new ErrorTemplateObject(
                    "OpenRouter Credits Exhausted",
                    "Your OpenRouter account has run out of credits.",
                    [
                        "You've used all your free credits",
                        "You've reached your spending limit"
                    ],
                    [
                        "Add more credits to your OpenRouter account",
                        "Try using a different provider",
                        "Use a local model through Ollama if available"
                    ]
                ),
                [ErrorType.Authentication] = new ErrorTemplateObject(
                    "OpenRouter API Key Error",
                    "Failed to authenticate with the OpenRouter API.",
                    [
                        "Missing or invalid API key",
                        "Your OpenRouter API key may have expired"
                    ],
                    [
                        "Go to the Users tab in SwarmUI",
                        "Click on the Settings icon for your user",
                        "Enter or update your OpenRouter API key",
                        "Verify your API key at https://openrouter.ai/keys"
                    ]
                ),
                [ErrorType.ContentModeration] = new ErrorTemplateObject(
                    "OpenRouter Content Moderation",
                    "Your request was flagged by content moderation filters.",
                    [
                        "Your input contains content that violates the model provider's content policy",
                        "The specific model you're using has strict content filters"
                    ],
                    [
                        "Review your prompt and remove or rephrase any potentially sensitive content",
                        "Try a different model with less strict content filters",
                        "Check OpenRouter's documentation for content guidelines"
                    ]
                )
            }
        };
        _statusCodeMappings = new Dictionary<HttpStatusCode, Dictionary<string, ErrorType>>
        {
            [HttpStatusCode.Unauthorized] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.Authentication,
                ["ollama"] = ErrorType.Connectivity // Ollama doesn't use auth by default
            },
            [HttpStatusCode.Forbidden] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.Authentication,
                ["openrouter"] = ErrorType.ContentModeration // OpenRouter uses 403 for content moderation
            },
            [HttpStatusCode.PaymentRequired] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.Quota
            },
            [HttpStatusCode.RequestEntityTooLarge] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.TokenLimit
            },
            [HttpStatusCode.RequestTimeout] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.RequestTimeout
            },
            [HttpStatusCode.TooManyRequests] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.Quota
            },
            [HttpStatusCode.InternalServerError] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.ServerError
            },
            [HttpStatusCode.BadGateway] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.ServerError
            },
            [HttpStatusCode.ServiceUnavailable] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.ServerError
            },
            [HttpStatusCode.GatewayTimeout] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.ServerError
            },
            [HttpStatusCode.NotFound] = new Dictionary<string, ErrorType>
            {
                ["default"] = ErrorType.Generic,
                ["ollama"] = ErrorType.ModelNotFound // For Ollama, 404 usually means model not found
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
        string normalizedProvider = !string.IsNullOrEmpty(provider) ? NormalizeBackendName(provider) : null;
        if (!string.IsNullOrEmpty(normalizedProvider) &&
            _providerSpecificTemplates.TryGetValue(normalizedProvider, out var templates) &&
            templates.TryGetValue(errorType, out var template))
        {
            return template;
        }
        return _errorTemplates.TryGetValue(errorType, out var standardTemplate)
            ? standardTemplate
            : _errorTemplates[ErrorType.Generic];
    }

    /// <summary>Process an error response and return a user-friendly message</summary>
    public string ProcessErrorResponse(string responseContent, HttpStatusCode statusCode, string backend)
    {
        if (string.IsNullOrEmpty(responseContent))
        {
            return FormatErrorMessage(ErrorType.Generic, "Empty response", backend);
        }
        ErrorType errorType = DetectErrorType(responseContent, statusCode, backend);
        string originalMessage = ExtractOriginalMessage(responseContent);
        string provider = NormalizeBackendName(backend);
        return FormatErrorMessage(errorType, originalMessage, provider);
    }

    /// <summary>Try to parse OpenAI error response</summary>
    public bool TryParseOpenAIError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        errorType = ErrorType.Generic;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(responseContent))
        {
            return false;
        }
        try
        {
            OpenAIErrorResponse openAIError = JsonConvert.DeserializeObject<OpenAIErrorResponse>(responseContent);
            if (openAIError?.Error == null)
            {
                return false;
            }
            errorMessage = openAIError.Error.Message;
            // Map OpenAI error types directly to our enum
            errorType = MapOpenAIErrorType(openAIError.Error.Type, openAIError.Error.Code?.ToString(), statusCode);
            Logs.Error($"OpenAI error: {errorMessage} (Type: {openAIError.Error.Type})");
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error parsing OpenAI error response: {ex.Message}");
        }
        return false;
    }

    /// <summary>Maps OpenAI error types to our ErrorType enum</summary>
    private ErrorType MapOpenAIErrorType(string errorType, string errorCode, HttpStatusCode statusCode)
    {
        // First check specific error codes
        if (!string.IsNullOrEmpty(errorCode))
        {
            switch (errorCode)
            {
                case "invalid_api_key": return ErrorType.Authentication;
                case "insufficient_quota": return ErrorType.Quota;
                case "context_length_exceeded": return ErrorType.TokenLimit;
                case "unsupported_parameter":
                    // Specifically for image/vision parameters
                    return ErrorType.UnsupportedParameterImage;
            }
        }
        // Then check error types
        if (!string.IsNullOrEmpty(errorType))
        {
            switch (errorType)
            {
                case "authentication_error": return ErrorType.Authentication;
                case "permission_error": return ErrorType.Authentication;
                case "rate_limit_error": return ErrorType.Quota;
                case "quota_error": return ErrorType.Quota;
                case "server_error": return ErrorType.ServerError;
                case "overloaded_error": return ErrorType.ServerError;
                case "invalid_request_error":
                    // This is ambiguous, so use the status code for more info
                    if (statusCode == HttpStatusCode.Unauthorized || statusCode == HttpStatusCode.Forbidden)
                        return ErrorType.Authentication;
                    return ErrorType.Generic;
            }
        }
        // Fallback to HTTP status code
        return MapStatusCodeToErrorType(statusCode, "openai");
    }

    /// <summary>Try to parse OpenRouter error response</summary>
    public bool TryParseOpenRouterError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        errorType = ErrorType.Generic;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(responseContent))
        {
            return false;
        }
        try
        {
            OpenRouterError errorResponse = JsonConvert.DeserializeObject<OpenRouterError>(responseContent);
            if (errorResponse?.Error == null)
            {
                return false;
            }
            string originalMessage = errorResponse.Error.Message;
            string providerName = errorResponse.Error.Metadata?.ProviderName ?? "OpenRouter";
            errorMessage = originalMessage;
            // Map status code to error type
            errorType = MapStatusCodeToErrorType(statusCode, "openrouter");
            // Include provider details in the error message
            if (!string.IsNullOrEmpty(errorResponse.Error.Metadata?.Raw))
            {
                errorMessage = $"Error from {providerName}: {originalMessage}";
            }
            // Special case for moderation
            if (errorType == ErrorType.ContentModeration &&
                errorResponse.Error.Metadata?.Reasons != null &&
                errorResponse.Error.Metadata.Reasons.Length > 0)
            {
                string reasons = string.Join(", ", errorResponse.Error.Metadata.Reasons);
                errorMessage = $"Content moderation error from {providerName}: {originalMessage}. Reasons: {reasons}";
            }
            Logs.Error($"OpenRouter error ({errorType}): {originalMessage}");
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error parsing OpenRouter error response: {ex.Message}");
        }
        return false;
    }

    /// <summary>Try to parse Anthropic error response</summary>
    public bool TryParseAnthropicError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        errorType = ErrorType.Generic;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(responseContent))
        {
            return false;
        }
        try
        {
            dynamic jsonResponse = JsonConvert.DeserializeObject<dynamic>(responseContent);
            if (jsonResponse == null)
            {
                return false;
            }
            // Check if it's an error response by looking for the right structure
            if (jsonResponse.type == null || jsonResponse.type.ToString() != "error" ||
                jsonResponse.error == null || jsonResponse.error.type == null)
            {
                return false;
            }
            string errorTypeStr = jsonResponse.error.type.ToString();
            errorMessage = jsonResponse.error.message != null ?
                jsonResponse.error.message.ToString() : "Unknown Anthropic error";
            // Map Anthropic error types directly
            errorType = errorTypeStr switch
            {
                "authentication_error" => ErrorType.Authentication,
                "permission_error" => ErrorType.Authentication,
                "not_found_error" => statusCode == HttpStatusCode.NotFound ?
                    ErrorType.ModelNotFound : ErrorType.Generic,
                "rate_limit_error" => ErrorType.Quota,
                "api_error" => ErrorType.ServerError,
                "overloaded_error" => ErrorType.ServerError,
                "request_too_large" => ErrorType.TokenLimit,
                _ => MapStatusCodeToErrorType(statusCode, "anthropic")
            };
            Logs.Error($"Anthropic error ({errorTypeStr}): {errorMessage}");
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error parsing Anthropic error response: {ex.Message}");
        }
        return false;
    }

    /// <summary>Attempts to parse Ollama-specific error responses</summary>
    public bool TryParseOllamaError(string responseContent, HttpStatusCode statusCode, out ErrorType errorType, out string errorMessage)
    {
        errorType = ErrorType.Generic;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(responseContent))
        {
            return false;
        }
        try
        {
            Dictionary<string, string> ollamaError = JsonConvert.DeserializeObject<Dictionary<string, string>>(responseContent);
            if (ollamaError == null || !ollamaError.TryGetValue("error", out string error) || string.IsNullOrEmpty(error))
            {
                return false;
            }
            errorMessage = error;
            // Map Ollama errors based on known patterns without string checks
            if (statusCode == HttpStatusCode.NotFound)
            {
                errorType = ErrorType.ModelNotFound;
            }
            else
            {
                // Use HTTP status code as a fallback
                errorType = MapStatusCodeToErrorType(statusCode, "ollama");
            }
            Logs.Error($"Ollama error: {error} (HTTP {(int)statusCode})");
            return true;
        }
        catch (Exception ex)
        {
            Logs.Error($"Error parsing Ollama error response: {ex.Message}");
        }
        return false;
    }

    /// <summary>Attempts to detect the error type from response content and HTTP status code</summary>
    public ErrorType DetectErrorType(string responseContent, HttpStatusCode statusCode, string provider = null)
    {
        // Try provider-specific error parsing first
        if (TryParseProviderError(responseContent, statusCode, provider, out ErrorType errorType, out _))
        {
            return errorType;
        }
        // Fall back to HTTP status code mapping
        return MapStatusCodeToErrorType(statusCode, provider);
    }

    /// <summary>Maps an HTTP status code to an error type</summary>
    private ErrorType MapStatusCodeToErrorType(HttpStatusCode statusCode, string provider = null)
    {
        if (_statusCodeMappings.TryGetValue(statusCode, out Dictionary<string, ErrorType> providerMappings))
        {
            string normalizedProvider = !string.IsNullOrEmpty(provider) ? NormalizeBackendName(provider) : null;
            // Check for provider-specific mapping
            if (!string.IsNullOrEmpty(normalizedProvider) &&
                providerMappings.TryGetValue(normalizedProvider, out ErrorType specificErrorType))
            {
                return specificErrorType;
            }
            // Use the default mapping
            if (providerMappings.TryGetValue("default", out ErrorType defaultErrorType))
            {
                return defaultErrorType;
            }
        }
        // Default to generic error if no mapping exists
        return ErrorType.Generic;
    }

    /// <summary>Tries to parse the error response using provider-specific handlers</summary>
    private bool TryParseProviderError(string responseContent, HttpStatusCode statusCode, string provider, out ErrorType errorType, out string errorMessage)
    {
        errorType = ErrorType.Generic;
        errorMessage = string.Empty;
        if (string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(responseContent))
        {
            return false;
        }
        string normalizedProvider = NormalizeBackendName(provider);
        return normalizedProvider switch
        {
            "openai" or "openaiapi" => TryParseOpenAIError(responseContent, statusCode, out errorType, out errorMessage),
            "openrouter" => TryParseOpenRouterError(responseContent, statusCode, out errorType, out errorMessage),
            "anthropic" => TryParseAnthropicError(responseContent, statusCode, out errorType, out errorMessage),
            "ollama" => TryParseOllamaError(responseContent, statusCode, out errorType, out errorMessage),
            _ => false,
        };
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
            dynamic json = JsonConvert.DeserializeObject<dynamic>(responseContent);
            if (json != null)
            {
                // Try common error message paths
                if (json.error?.message != null)
                {
                    return json.error.message.ToString();
                }
                else if (json.error?.error?.message != null)
                {
                    return json.error.error.message.ToString();
                }
                else if (json.message != null)
                {
                    return json.message.ToString();
                }
                else if (json.error != null && json.error is string)
                {
                    return json.error.ToString();
                }
            }
        }
        catch
        {
            // Not valid JSON or doesn't have the expected structure
            Logs.Error($"Failed to extract message from: {responseContent}");
        }
        // Return truncated original content if we can't extract a specific message
        return responseContent.Length > 100 ?
            string.Concat(responseContent.AsSpan(0, 100), "...") : responseContent;
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