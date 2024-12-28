using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension;

public static class BackendSchema
{
    public enum MessageType
    {
        Text,
        Vision
    }

    public class MessageContent
    {
        public string Text { get; set; }
        public List<MediaContent> Media { get; set; }
    }

    public class MediaContent
    {
        public string Type { get; set; }  // "base64" or "url"
        public string Data { get; set; }
        public string MediaType { get; set; }  // "image/jpeg", "image/png", etc.
    }

    /// <summary>Get the schema type for the backend.</summary>
    /// <param name="type">Backend type (ollama, openai, anthropic, etc.)</param>
    /// <param name="content">Message content including text and media</param>
    /// <param name="model">Model name to use</param>
    /// <param name="messageType">Type of message (Text or Vision)</param>
    /// <returns>Returns an object with the schema type for the backend.</returns>
    public static object GetSchemaType(string type, MessageContent content, string model, MessageType messageType = MessageType.Text)
    {
        if (content == null || string.IsNullOrEmpty(model))
        {
            throw new ArgumentException("Content or model cannot be null or empty.");
        }
        type = type.ToLower();
        return type switch
        {
            "ollama" => OllamaRequestBody(content, model, messageType),
            "openai" or "openaiapi" or "openrouter" => OpenAICompatibleRequestBody(content, model, messageType),
            "anthropic" => AnthropicRequestBody(content, model, messageType),
            _ => throw new ArgumentException($"Unsupported backend type: {type}")
        };
    }

    /// <summary>Generates a request body for Ollama backend.</summary>
    private static object OllamaRequestBody(MessageContent content, string model, MessageType messageType)
    {
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            return new
            {
                model = model,
                messages = new[]
                {
                new
                {
                    role = "user",
                    content = content.Text,
                    images = content.Media.Select(m => m.Data.Replace("data:image/jpeg;base64,", "")
                                                      .Replace("data:image/png;base64,", ""))
                                                      .ToArray()
                }
            },
                stream = false,
                keep_alive = 0 // Set to 0 to unload model after response
            };
        }
        return new
        {
            model = model,
            messages = new[]
            {
            new { role = "user", content = content.Text }
        },
            stream = false,
            keep_alive = 0 // Set to 0 to unload model after response
        };
    }

    /// <summary>Generates a request body for OpenAI and compatible backends.</summary>
    private static object OpenAICompatibleRequestBody(MessageContent content, string model, MessageType messageType)
    {
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            List<object> messageContent =
            [
                // Add text content first
                new { type = "text", text = content.Text }
            ];
            // Add image content
            foreach (MediaContent media in content.Media)
            {
                string imageUrl = media.Type == "base64" 
                    ? $"data:{media.MediaType};base64,{media.Data}"  // Format as data URL for base64
                    : media.Data;  // Use as-is for regular URLs
                messageContent.Add(new
                {
                    type = "image_url",
                    image_url = new { url = imageUrl }
                });
            }
            return new
            {
                model = model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = messageContent
                    }
                },
                max_tokens = 300
            };
        }
        return new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = content.Text }
            },
            temperature = 1.0,
            max_tokens = 300,
            top_p = 0.9,
            stream = false
        };
    }

    /// <summary>Generates a request body for the Anthropic (Claude) API.</summary>
    private static object AnthropicRequestBody(MessageContent content, string model, MessageType messageType)
    {
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            List<object> messageContent = [];
            // Add images first (Claude's format)
            foreach (MediaContent media in content.Media)
            {
                // Ensure a valid media type
                string mediaType = media.MediaType;
                Logs.Debug($"\n\nMediaType: {mediaType}\n\n");
                if (string.IsNullOrEmpty(mediaType))
                {
                    mediaType = "image/jpeg";
                    Logs.Debug($"\n\nMediaType was null changed to: {mediaType}\n\n");
                }
                // Clean base64 data if needed
                string imageData = media.Data;
                if (imageData.Contains("base64,"))
                {
                    imageData = imageData.Substring(imageData.IndexOf("base64,") + 7);
                }
                messageContent.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = mediaType,
                        data = imageData
                    }
                });
            }
            // Add text after images
            messageContent.Add(new
            {
                type = "text",
                text = content.Text
            });
            return new
            {
                model = model,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = messageContent.ToArray()
                    }
                },
                max_tokens = 1024
            };
        }
        return new
        {
            model = model,
            messages = new[]
            {
                new { role = "user", content = content.Text }
            },
            max_tokens = 1024
        };
    }
}