using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;

public class OpenRouterBackend : Backend
{
    public string ApiKey { get; set; }
    public string BaseUrl { get; set; }
}

public class OpenRouterResponse
{
    [JsonProperty("data")]
    public List<OpenRouterModel> Data { get; set; }

    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("choices")]
    public List<OpenRouterChoice> Choices { get; set; }

    [JsonProperty("usage")]
    public OpenRouterUsage Usage { get; set; }

    [JsonProperty("model")]
    public string Model { get; set; }
}

public class OpenRouterModel
{
    [JsonProperty("id")]
    public string Id { get; set; }

    [JsonProperty("name")]
    public string Name { get; set; }

    [JsonProperty("created")]
    public long Created { get; set; }

    [JsonProperty("description")]
    public string Description { get; set; }

    [JsonProperty("context_length")]
    public int? ContextLength { get; set; }

    [JsonProperty("pricing")]
    public OpenRouterPricing Pricing { get; set; }

    [JsonProperty("architecture")]
    public OpenRouterArchitecture Architecture { get; set; }

    [JsonProperty("top_provider")]
    public OpenRouterProvider TopProvider { get; set; }
}

public class OpenRouterPricing
{
    [JsonProperty("prompt")]
    public string Prompt { get; set; }

    [JsonProperty("completion")]
    public string Completion { get; set; }

    [JsonProperty("request")]
    public string Request { get; set; }

    [JsonProperty("image")]
    public string Image { get; set; }
}

public class OpenRouterArchitecture
{
    [JsonProperty("tokenizer")]
    public string Tokenizer { get; set; }

    [JsonProperty("instruct_type")]
    public string InstructType { get; set; }

    [JsonProperty("modality")]
    public string Modality { get; set; }
}

public class OpenRouterProvider
{
    [JsonProperty("context_length")]
    public int? ContextLength { get; set; }

    [JsonProperty("max_completion_tokens")]
    public int? MaxCompletionTokens { get; set; }

    [JsonProperty("is_moderated")]
    public bool IsModerated { get; set; }
}

public class OpenRouterChoice
{
    [JsonProperty("finish_reason")]
    public string FinishReason { get; set; }
    
    [JsonProperty("native_finish_reason")]
    public string NativeFinishReason { get; set; }
    
    [JsonProperty("index")]
    public int Index { get; set; }

    [JsonProperty("message")]
    public OpenRouterMessage Message { get; set; }
}

public class OpenRouterMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }

    [JsonProperty("content")]
    [JsonConverter(typeof(OpenRouterContentConverter))]
    public object Content { get; set; }
}

public class OpenRouterContentConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return true;
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
    {
        try
        {
            if (reader.TokenType == JsonToken.String)
            {
                return reader.Value as string;
            }
            else if (reader.TokenType == JsonToken.StartObject)
            {
                JObject obj = JObject.Load(reader);
                return obj.ToString(Formatting.None);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        serializer.Serialize(writer, value);
    }
}

public class OpenRouterUsage
{
    [JsonProperty("prompt_tokens")]
    public int PromptTokens { get; set; }

    [JsonProperty("completion_tokens")]
    public int CompletionTokens { get; set; }

    [JsonProperty("total_tokens")]
    public int TotalTokens { get; set; }
}

public class OpenRouterError
{
    [JsonProperty("error")]
    public OpenRouterErrorDetails Error { get; set; }

    [JsonProperty("user_id")]
    public string UserId { get; set; }
}

public class OpenRouterErrorDetails
{
    [JsonProperty("code")]
    public object Code { get; set; }

    [JsonProperty("message")]
    public string Message { get; set; }

    [JsonProperty("metadata")]
    public OpenRouterErrorMetadata Metadata { get; set; }
}

public class OpenRouterErrorMetadata
{
    [JsonProperty("raw")]
    public string Raw { get; set; }

    [JsonProperty("provider_name")]
    public string ProviderName { get; set; }
}