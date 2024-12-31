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
        public string Instructions { get; set; }
        public List<MediaContent> Media { get; set; }
        public int? KeepAlive { get; set; }
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
        _ = content.KeepAlive;
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
        List<object> messages = [];
        if (!string.IsNullOrEmpty(content.Instructions))
        {
            messages.Add(new { role = "system", content = content.Instructions });
        }
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            messages.Add(new
            {
                role = "user",
                content = content.Text,
                images = content.Media.Select(m => m.Data.Replace("data:image/jpeg;base64,", "")
                                                  .Replace("data:image/png;base64,", ""))
                                                  .ToArray()
            });
            return new
            {
                model,
                messages = messages.ToArray(),
                stream = false,
                content.KeepAlive,
                options = new
                {
                    temperature = 1.0,
                    top_p = 0.9
                }
            };
        }
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            stream = false,
            content.KeepAlive,
            options = new
            {
                temperature = 1.0,
                top_p = 0.9
            }
        };
    }

    /// <summary>Generates a request body for OpenAI and compatible backends.</summary>
    private static object OpenAICompatibleRequestBody(MessageContent content, string model, MessageType messageType)
    {
        List<object> messages = [];
        // Add system message if instructions exist
        if (!string.IsNullOrEmpty(content.Instructions))
        {
            messages.Add(new { role = "system", content = content.Instructions });
        }
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            List<object> contentList = [];
            // First add any images
            foreach (MediaContent media in content.Media)
            {
                // Clean up base64 data - remove data URL prefix if present
                string imageData = media.Data;
                if (media.Type == "base64" && imageData.Contains("base64,"))
                {
                    imageData = imageData[(imageData.IndexOf("base64,") + 7)..];
                }
                contentList.Add(new
                {
                    type = "image_url",
                    image_url = media.Type == "base64"
                        ? new { url = $"data:{media.MediaType};base64,{imageData}" }
                        : new { url = media.Data }
                });
            }
            // Then add the text prompt
            contentList.Add(new
            {
                type = "text",
                text = content.Text
            });
            // Add as a user message
            messages.Add(new
            {
                role = "user",
                content = contentList
            });
            return new
            {
                model,
                messages = messages.ToArray(),
                max_tokens = 300,
                temperature = 1.0,
                stream = false
            };
        }
        // For non-vision requests
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            temperature = 1.0,
            max_tokens = 300,
            top_p = 0.9,
            stream = false
        };
    }

    /// <summary>Generates a request body for the Anthropic (Claude) API.</summary>
    private static object AnthropicRequestBody(MessageContent content, string model, MessageType messageType)
    {
        List<object> messages = [];
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            List<object> messageContent = [];
            foreach (MediaContent media in content.Media)
            {
                string mediaType = media.MediaType ?? "image/jpeg";
                string imageData = media.Data;
                if (imageData.Contains("base64,"))
                {
                    imageData = imageData[(imageData.IndexOf("base64,") + 7)..];
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
            messageContent.Add(new
            {
                type = "text",
                text = content.Text
            });
            messages.Add(new
            {
                role = "user",
                content = messageContent.ToArray()
            });
            return new
            {
                model,
                messages = messages.ToArray(),
                system = content.Instructions,
                max_tokens = 1024
            };
        }
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            system = content.Instructions,
            max_tokens = 1024
        };
    }
}