using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models
{
    ///<summary>The structure for the config.json.</summary>
    public class ConfigData
    {
        public string LLMBackend { get; set; }
        public string Instructions { get; set; }
        public string LlmEndpoint { get; set; }
        public bool UnloadModel { get; set; }
        public string Model { get; set; }
        public BackendsConfig Backends { get; set; }
    }

    ///<summary>The structure for the backends in config.json.</summary>
    public class BackendsConfig
    {
        [JsonPropertyName("ollama")]
        public Backend Ollama { get; set; }

        [JsonPropertyName("openai")]
        public OpenAIBackend OpenAI { get; set; }

        [JsonPropertyName("openaiapi")]
        public OpenAIAPIBackend OpenAIAPI { get; set; }

        [JsonPropertyName("anthropic")]
        public AnthropicBackend Anthropic { get; set; }
    }

    ///<summary>Represents the structure of Ollama's configuration (no BaseUrl or ApiKey).</summary>
    public class Backend
    {
        public Dictionary<string, string> Endpoints { get; set; }
    }

    ///<summary>Represents the structure for openaiapi which requires only an ApiKey (optional).</summary>
    public class OpenAIAPIBackend : Backend
    {
        public string BaseUrl { get; set; }
        public string ApiKey { get; set; }
    }

    /// <summary>The message content within a response, including the content and the role.</summary>
    public class Message
    {
        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("role")]
        public string Role { get; set; }
    }

    /// <summary>The root object containing a list of models.</summary>
    public class RootObject // TODO: Rename this class to something more descriptive
    {
        [JsonProperty("models")]
        public List<ModelData> Data { get; set; }
    }
}
