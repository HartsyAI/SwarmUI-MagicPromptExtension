using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;

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
