using SwarmUI.Utils;
using System.Linq;
using Hartsy.Extensions.MagicPromptExtension.WebAPI;

namespace Hartsy.Extensions.MagicPromptExtension
{
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
                    stream = false
                };
            }
            return new
            {
                model = model,
                messages = new[]
                {
                    new { role = "user", content = content.Text }
                },
                stream = false
            };
        }

        /// <summary>Generates a request body for OpenAI and compatible backends.</summary>
        private static object OpenAICompatibleRequestBody(MessageContent content, string model, MessageType messageType)
        {
            if (messageType == MessageType.Vision && content.Media?.Any() == true)
            {
                var messageContent = new List<object>
                {
                    // Add text content first
                    new { type = "text", text = content.Text }
                };
                // Add image content
                foreach (MediaContent media in content.Media)
                {
                    var imageUrl = media.Type == "base64" 
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
                var messageContent = new List<object>();
                // Add images first (Claude's format)
                foreach (var media in content.Media)
                {
                    messageContent.Add(new
                    {
                        type = "image",
                        source = new
                        {
                            type = "base64",
                            media_type = media.MediaType ?? "image/jpeg",
                            data = media.Data
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

        /// <summary>Validates the input parameters.</summary>
        private static void ValidateInput(MessageContent content, string model)
        {
            if (content == null || string.IsNullOrEmpty(model))
            {
                throw new ArgumentException("Content or model cannot be null or empty.");
            }
        }
    }
}