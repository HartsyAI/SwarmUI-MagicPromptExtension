namespace Kalebbroo.Extensions.MagicPromptExtension.WebAPI.Models
{
    /// <summary>The structure for the setup.json</summary>
    public class ConfigData
    {
        public string Instructions { get; set; }
        public string LlmEndpoint { get; set; }
    }

    /// <summary>The structure for what is returned from the LLM API</summary>
    public class LLMResponse
    {
        public Choice[] Choices { get; set; }
        public string Id { get; set; }
        public string Object { get; set; }
        public int Created { get; set; }
        public Usage Usage { get; set; }
    }

    /// <summary>The sub structure for the LLMResponse Choice[]</summary>
    public class Choice
    {
        public Message Message { get; set; }
        public string FinishReason { get; set; }
        public int Index { get; set; }
    }

    /// <summary>The message content within a choice, including the content and the role of the LLM</summary>
    public class Message
    {
        public string Content { get; set; }
        public string Role { get; set; }
    }

    /// <summary>The token usage statistics of the API call.</summary>
    public class Usage
    {
        public int CompletionTokens { get; set; }
        public int PromptTokens { get; set; }
        public int TotalTokens { get; set; }
    }
}