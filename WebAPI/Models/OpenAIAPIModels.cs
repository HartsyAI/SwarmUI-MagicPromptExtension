using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models
{
    ///<summary>Represents the structure of models returned by OpenAIAPI (local instance).</summary>
    public class OpenAIAPIModels
    {
        [JsonProperty("models")]
        public List<OpenAIAPIModelData> Models { get; set; }
    }

    ///<summary>Represents individual model data from OpenAIAPI.</summary>
    public class OpenAIAPIModelData
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

    public class OpenAIAPIResponse
    {
        [JsonProperty("data")]
        public List<OpenAIModel> Data { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("object")]
        public string Object { get; set; }

        [JsonPropertyName("created")]
        public long Created { get; set; }

        [JsonPropertyName("model")]
        public string Model { get; set; }

        [JsonPropertyName("choices")]
        public List<OpenAIChoice> Choices { get; set; }
    }
}
