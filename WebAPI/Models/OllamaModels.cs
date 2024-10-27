using Newtonsoft.Json;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models
{
    /// <summary>The structure for what is returned from the Ollama API.</summary>
    public class OllamaResponse
    {
        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; }

        [JsonProperty("message")]
        public Message Message { get; set; }

        [JsonProperty("done")]
        public bool Done { get; set; }

        [JsonProperty("total_duration")]
        public long TotalDuration { get; set; }

        [JsonProperty("load_duration")]
        public long LoadDuration { get; set; }

        [JsonProperty("prompt_eval_count")]
        public int PromptEvalCount { get; set; }

        [JsonProperty("prompt_eval_duration")]
        public long PromptEvalDuration { get; set; }

        [JsonProperty("eval_count")]
        public int EvalCount { get; set; }

        [JsonProperty("eval_duration")]
        public long EvalDuration { get; set; }
    }

    /// <summary>The structure for models and related metadata.</summary>
    public class ModelData
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("model")]
        public string Model { get; set; }

        [JsonProperty("modified_at")]
        public DateTime ModifiedAt { get; set; }

        [JsonProperty("size")]
        public long Size { get; set; }

        [JsonProperty("digest")]
        public string Digest { get; set; }

        [JsonProperty("details")]
        public ModelDetails Details { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }

    public class ModelDetails
    {
        [JsonProperty("parent_model")]
        public string ParentModel { get; set; }

        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("family")]
        public string Family { get; set; }

        [JsonProperty("families")]
        public List<string> Families { get; set; }

        [JsonProperty("parameter_size")]
        public string ParameterSize { get; set; }

        [JsonProperty("quantization_level")]
        public string QuantizationLevel { get; set; }
    }
}
