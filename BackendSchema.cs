using SixLabors.ImageSharp.Processing;
using SwarmUI.Utils;
using Image = SwarmUI.Utils.Image;
using ISImage = SixLabors.ImageSharp.Image;
using ISImage32 = SixLabors.ImageSharp.Image<SixLabors.ImageSharp.PixelFormats.Rgba32>;
using ISImageFrame32 = SixLabors.ImageSharp.ImageFrame<SixLabors.ImageSharp.PixelFormats.Rgba32>;

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
            "grok" => OpenAICompatibleRequestBody(content, model, messageType, preferPngForBase64: true),
            "openai" or "openaiapi" or "openrouter" => OpenAICompatibleRequestBody(content, model, messageType, preferPngForBase64: false),
            "anthropic" => AnthropicRequestBody(content, model, messageType),
            _ => throw new ArgumentException($"Unsupported backend type: {type}")
        };
    }

    /// <summary>Compresses image data to optimize for LLM vision models</summary>
    /// <param name="media">The media content containing image data</param>
    /// <param name="targetFormat">The target format ("PNG" or "WEBP")</param>
    /// <returns>Compressed base64 image data without the data URL prefix</returns>
    public static string CompressImageForVision(MediaContent media, string targetFormat = "WEBP")
    {
        if (media.Type != "base64")
        {
            return media.Data;
        }
        try
        {
            Image image = Image.FromDataString($"data:{media.MediaType};base64,{media.Data}");
            // Skip compression for videos etc..
            if (image.Type != Image.ImageType.IMAGE)
            {
                return media.Data;
            }
            ISImage img = image.ToIS;
            int maxDimension = 256; // TODO: This needs to be tested and adjusted
            if (img.Width > maxDimension || img.Height > maxDimension)
            {
                float scaleFactor = maxDimension / (float)Math.Max(img.Width, img.Height);
                int newWidth = (int)(img.Width * scaleFactor);
                int newHeight = (int)(img.Height * scaleFactor);
                img.Mutate(i => i.Resize(newWidth, newHeight));
            }
            // Set compression quality based on format TODO: This needs to be tested and adjusted
            int quality = targetFormat == "PNG" ? 60 : 40;
            Image compressedImage = new Image(img).ConvertTo(targetFormat, quality: quality);
            // Return just the base64 data (without the data:image/webp;base64, prefix)
            return compressedImage.AsBase64;
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to compress image: {ex.Message}");
            return media.Data;
        }
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
                images = content.Media.Select(m => CompressImageForVision(m, "JPG")).ToArray()
            });
            return new
            {
                model,
                messages = messages.ToArray(),
                stream = false,
                keep_alive = content.KeepAlive,
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
            keep_alive = content.KeepAlive,
            options = new
            {
                temperature = 1.0,
                top_p = 0.9
            }
        };
    }

    /// <summary>Generates a request body for OpenAI and compatible backends.</summary>
    private static object OpenAICompatibleRequestBody(MessageContent content, string model, MessageType messageType, bool preferPngForBase64)
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
            foreach (MediaContent media in content.Media)
            {
                string imageData = CompressImageForVision(media, preferPngForBase64 ? "PNG" : "WEBP");
                contentList.Add(new
                {
                    type = "image_url",
                    image_url = media.Type == "base64"
                        ? new { url = preferPngForBase64 ? $"data:image/png;base64,{imageData}" : $"data:image/webp;base64,{imageData}" }
                        : new { url = media.Data }
                });
            }
            contentList.Add(new
            {
                type = "text",
                text = content.Text
            });
            messages.Add(new
            {
                role = "user",
                content = contentList
            });
            return new
            {
                model,
                messages = messages.ToArray(),
                max_tokens = 1000,
                temperature = 1.0,
                stream = false
            };
        }
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            temperature = 1.0,
            max_tokens = 1000,
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
                // Compress image and convert to PNG. Anthropic only accepts PNG.
                string imageData = CompressImageForVision(media, "PNG");
                string mediaType = "image/png";
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