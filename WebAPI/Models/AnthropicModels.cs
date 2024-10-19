using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models
{
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

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("choices")]
        public List<AnthropicChoice> Choices { get; set; }
    }

    public class AnthropicChoice
    {
        [JsonPropertyName("index")]
        public int Index { get; set; }

        [JsonPropertyName("message")]
        public AnthropicMessage Message { get; set; }

        [JsonPropertyName("finish_reason")]
        public string FinishReason { get; set; }
    }

    public class AnthropicMessage
    {
        [JsonPropertyName("role")]
        public string Role { get; set; }

        [JsonPropertyName("content")]
        public string Content { get; set; }
    }

    public class AnthropicModel
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("object")]
        public string Object { get; set; }

        [JsonProperty("created")]
        public long Created { get; set; }

        [JsonProperty("description")]
        public string Description { get; set; }

        [JsonProperty("tags")]
        public List<string> Tags { get; set; }
    }
}
