using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;

///<summary>Represents the structure for backends with BaseUrl and optional ApiKey.</summary>
public class AnthropicBackend : Backend
{
    public string BaseUrl { get; set; }
    public string ApiKey { get; set; }
}

public class AnthropicResponse
{
    [JsonProperty("data")]
    public List<AnthropicModel> Data { get; set; }

    [JsonPropertyName("choices")]
    public List<AnthropicChoice> Choices { get; set; }

    [JsonPropertyName("content")]
    public AnthropicContent[] Content { get; set; }
}

public class AnthropicContent
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; }
}

public class AnthropicChoice
{
    [JsonPropertyName("message")]
    public AnthropicMessage Message { get; set; }
}

public class AnthropicRequest
{
    [JsonProperty("model")]
    public string Model { get; set; }

    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonProperty("messages")]
    public List<AnthropicMessage> Messages { get; set; }
}

public class AnthropicMessage
{
    [JsonProperty("role")]
    public string Role { get; set; }

    [JsonProperty("content")]
    public string Content { get; set; }
}

public class AnthropicModel
{
    [JsonProperty("id")]
    public string Id { get; set; }
    
    [JsonProperty("name")]
    public string Name { get; set; }
    
    [JsonProperty("description")]
    public string Description { get; set; }
    
    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; }
    
    [JsonProperty("created")]
    public long Created { get; set; }
    
    [JsonProperty("updated")]
    public long Updated { get; set; }
    
    [JsonProperty("capabilities")]
    public AnthropicModelCapabilities Capabilities { get; set; }
}

public class AnthropicModelCapabilities
{
    [JsonProperty("vision")]
    public bool Vision { get; set; }
}
