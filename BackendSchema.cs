using ImageMagick;
using SwarmUI.Utils;
using SwarmUI.Media;

namespace Hartsy.Extensions.MagicPromptExtension;

public static class BackendSchema
{
    public static readonly int DefaultMaxTokens = 1024;

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

    /// <summary>A vision image ready to send: base64 data without the data-URL prefix, and the MIME type that describes it.</summary>
    public record VisionImage(string Data, string MimeType);

    /// <summary>Get the schema type for the backend.</summary>
    /// <param name="type">Backend type (ollama, openai, anthropic, etc.)</param>
    /// <param name="content">Message content including text and media</param>
    /// <param name="model">Model name to use</param>
    /// <param name="messageType">Type of message (Text or Vision)</param>
    /// <returns>Returns an object with the schema type for the backend.</returns>
    public static object GetSchemaType(string type, MessageContent content, string model, MessageType messageType = MessageType.Text, long seed = -1, int? maxTokens = null)
    {
        if (content == null || string.IsNullOrEmpty(model))
        {
            throw new ArgumentException("Content or model cannot be null or empty.");
        }
        type = type.ToLower();
        _ = content.KeepAlive;
        return type switch
        {
            "ollama" => OllamaRequestBody(content, model, messageType, seed),
            "grok" => OpenAICompatibleRequestBody(content, model, messageType, preferPngForBase64: true, seed, maxTokens),
            "openai" or "openaiapi" or "openrouter" => OpenAICompatibleRequestBody(content, model, messageType, preferPngForBase64: false, seed, maxTokens),
            "anthropic" => AnthropicRequestBody(content, model, messageType, maxTokens),
            _ => throw new ArgumentException($"Unsupported backend type: {type}")
        };
    }

    /// <summary>Raster formats accepted from the client. ImageMagick also renders SVG, and an SVG can reference local
    /// files, so every other format is rejected before its pixels are decoded.</summary>
    private static readonly HashSet<MagickFormat> AllowedFormats = [MagickFormat.Png, MagickFormat.Jpeg, MagickFormat.WebP, MagickFormat.Gif, MagickFormat.Bmp, MagickFormat.Bmp2, MagickFormat.Bmp3, MagickFormat.Tiff];

    private const string UnsupportedFormatMessage = "Unsupported image format. Allowed: PNG, JPEG, WebP, GIF, BMP, TIFF.";

    /// <summary>Rejects data that is not an allowed raster image. The file signature is checked first so ImageMagick never
    /// inspects any other format: identifying a PDF, for example, launches Ghostscript. ImageMagick then confirms the
    /// format from the header without decoding pixels. Throws <see cref="InvalidOperationException"/> on rejection.</summary>
    private static void EnsureRasterImage(byte[] bytes)
    {
        if (bytes.Length == 0)
        {
            throw new InvalidOperationException("Image data is empty.");
        }
        if (!HasRasterSignature(bytes))
        {
            throw new InvalidOperationException(UnsupportedFormatMessage);
        }
        MagickFormat format;
        try
        {
            format = new MagickImageInfo(bytes).Format;
        }
        catch (MagickException ex)
        {
            throw new InvalidOperationException("Image could not be read.", ex);
        }
        if (!AllowedFormats.Contains(format))
        {
            throw new InvalidOperationException(UnsupportedFormatMessage);
        }
    }

    /// <summary>True when the data starts with the signature of PNG, JPEG, GIF, BMP, WebP, or TIFF.</summary>
    private static bool HasRasterSignature(byte[] b)
    {
        if (b.Length < 12)
        {
            return false;
        }
        bool png = b[0] == 0x89 && b[1] == (byte)'P' && b[2] == (byte)'N' && b[3] == (byte)'G';
        bool jpeg = b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
        bool gif = b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8';
        bool bmp = b[0] == (byte)'B' && b[1] == (byte)'M';
        bool webp = b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P';
        bool tiff = (b[0] == (byte)'I' && b[1] == (byte)'I' && b[2] == 0x2A && b[3] == 0) || (b[0] == (byte)'M' && b[1] == (byte)'M' && b[2] == 0 && b[3] == 0x2A);
        return png || jpeg || gif || bmp || webp || tiff;
    }

    /// <summary>Compresses image data for LLM vision models: first frame only, long edge capped, re-encoded in the target
    /// format. GIF and WebP register as animations in SwarmUI and are reduced to their first frame. On any failure the
    /// original data is returned with its original MIME type.</summary>
    /// <param name="media">The media content containing image data</param>
    /// <param name="targetFormat">The target format ("PNG", "JPG" or "WEBP")</param>
    public static VisionImage CompressImageForVision(MediaContent media, string targetFormat = "WEBP")
    {
        string originalMime = string.IsNullOrEmpty(media.MediaType) ? "image/png" : media.MediaType;
        if (media.Type != "base64")
        {
            return new VisionImage(media.Data, originalMime);
        }
        try
        {
            ImageFile image = ImageFile.FromDataString($"data:{originalMime};base64,{media.Data}");
            // Video and audio pass through untouched.
            if (image.Type.MetaType != MediaMetaType.Image && image.Type.MetaType != MediaMetaType.Animation)
            {
                return new VisionImage(media.Data, originalMime);
            }
            EnsureRasterImage(image.RawData);
            int maxDimension = 256; // TODO: This needs to be tested and adjusted
            // The ImageFile owns its decoded frames, so the first frame is cloned before it is resized.
            using IMagickImage<ushort> frame = image.ToMagick.Clone();
            if (frame.Width > maxDimension || frame.Height > maxDimension)
            {
                float scaleFactor = maxDimension / (float)Math.Max(frame.Width, frame.Height);
                uint newWidth = Math.Max(1u, (uint)(frame.Width * scaleFactor));
                uint newHeight = Math.Max(1u, (uint)(frame.Height * scaleFactor));
                frame.Resize(new MagickGeometry(newWidth, newHeight) { IgnoreAspectRatio = true });
                frame.ResetPage();
            }
            // Set compression quality based on format TODO: This needs to be tested and adjusted
            int quality = targetFormat == "PNG" ? 60 : 40;
            ImageFile compressedImage = new Image(frame).ConvertTo(targetFormat, quality: quality);
            return new VisionImage(compressedImage.AsBase64, compressedImage.Type.MimeType);
        }
        catch (Exception ex)
        {
            Logs.Error($"Failed to compress image: {ex.Message}");
            return new VisionImage(media.Data, originalMime);
        }
    }

    /// <summary>Generates a request body for Ollama backend.</summary>
    private static object OllamaRequestBody(MessageContent content, string model, MessageType messageType, long seed = -1)
    {
        List<object> messages = [];
        if (!string.IsNullOrEmpty(content.Instructions))
        {
            messages.Add(new { role = "system", content = content.Instructions });
        }

        object options = seed == -1
            ? new { temperature = 1.0, top_p = 0.9 }
            : new { temperature = 1.0, top_p = 0.9, seed };

        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            messages.Add(new
            {
                role = "user",
                content = content.Text,
                images = content.Media.Select(m => CompressImageForVision(m, "JPG").Data).ToArray()
            });

            return new
            {
                model,
                messages = messages.ToArray(),
                stream = false,
                keep_alive = content.KeepAlive,
                options
            };
        }
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            stream = false,
            keep_alive = content.KeepAlive,
            options
        };
    }

    /// <summary>Generates a request body for OpenAI and compatible backends.</summary>
    private static object OpenAICompatibleRequestBody(MessageContent content, string model, MessageType messageType, bool preferPngForBase64, long seed = -1, int? maxTokens = null)
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
                VisionImage imageData = CompressImageForVision(media, preferPngForBase64 ? "PNG" : "WEBP");
                contentList.Add(new
                {
                    type = "image_url",
                    image_url = media.Type == "base64"
                        ? new { url = $"data:{imageData.MimeType};base64,{imageData.Data}" }
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

            if (seed != -1)
            {
                return new
                {
                    model,
                    messages = messages.ToArray(),
                    max_tokens = maxTokens ?? DefaultMaxTokens,
                    temperature = 1.0,
                    stream = false,
                    seed
                };
            }

            return new
            {
                model,
                messages = messages.ToArray(),
                max_tokens = maxTokens ?? DefaultMaxTokens,
                temperature = 1.0,
                stream = false
            };
        }
        messages.Add(new { role = "user", content = content.Text });

        if (seed != -1)
        {
            return new
            {
                model,
                messages = messages.ToArray(),
                max_tokens = maxTokens ?? DefaultMaxTokens,
                temperature = 1.0,
                stream = false,
                seed
            };
        }

        return new
        {
            model,
            messages = messages.ToArray(),
            temperature = 1.0,
            max_tokens = maxTokens ?? DefaultMaxTokens,
            top_p = 0.9,
            stream = false
        };
    }

    /// <summary>Generates a request body for the Anthropic (Claude) API.</summary>
    private static object AnthropicRequestBody(MessageContent content, string model, MessageType messageType, int? maxTokens = null)
    {
        List<object> messages = [];
        if (messageType == MessageType.Vision && content.Media?.Any() == true)
        {
            List<object> messageContent = [];
            foreach (MediaContent media in content.Media)
            {
                // Compress to PNG; if that fails the original data is sent under its own MIME type.
                VisionImage imageData = CompressImageForVision(media, "PNG");
                messageContent.Add(new
                {
                    type = "image",
                    source = new
                    {
                        type = "base64",
                        media_type = imageData.MimeType,
                        data = imageData.Data
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
                max_tokens = maxTokens ?? DefaultMaxTokens
            };
        }
        messages.Add(new { role = "user", content = content.Text });
        return new
        {
            model,
            messages = messages.ToArray(),
            system = content.Instructions,
            max_tokens = maxTokens ?? DefaultMaxTokens
        };
    }
}
