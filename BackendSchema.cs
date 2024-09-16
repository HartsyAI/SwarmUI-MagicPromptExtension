using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension
{
    public static class BackendSchema
    {
        /// <summary>Get the schema type for the backend.</summary>
        /// <param name="type"></param>
        /// <param name="inputText"></param>
        /// <param name="model"></param>
        /// <returns>Returns an object with the schema type for the backend.</returns>
        public static object GetSchemaType(string type, string inputText, string model)
        {
            type = type.ToLower();
            switch (type)
            {
                case "ollama":
                    return OllamaRequestBody(inputText, model);
                case "openai":
                case "oogabooga":
                    return OpenAICompatibleRequestBody(inputText, model);
                case "claude":
                    return ClaudeRequestBody(inputText, model);
                default:
                    Logs.Error("Unsupported or null backend. Check the config.json");
                    return null;
            }
        }

        /// <summary> </summary>
        /// <param name="inputText"></param>
        /// <param name="currentModel"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static object OllamaRequestBody(string inputText, string currentModel)
        {
            if (string.IsNullOrEmpty(inputText) || string.IsNullOrEmpty(currentModel))
            {
                throw new ArgumentException("Input text or model cannot be null or empty.");
            }
            return new
            {
                model = currentModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = inputText
                    }
                },
                stream = false
            };
        }

        /// <summary>Generates a request body for OpenAI and similar backends (Oogabooga).</summary>
        /// <param name="inputText"></param>
        /// <param name="currentModel"></param>
        /// <returns></returns>
        public static object OpenAICompatibleRequestBody(string inputText, string currentModel)
        {
            if (string.IsNullOrEmpty(inputText) || string.IsNullOrEmpty(currentModel))
            {
                throw new ArgumentException("Input text or model cannot be null or empty.");
            }
            return new
            {
                model = currentModel,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = inputText
                    }
                },
                temperature = 1.0,
                max_tokens = 200,
                top_p = 0.9,
                stream = false
            };
        }

        /// <summary>Generates a request body for Claude (Anthropic API).</summary>
        /// <param name="inputText"></param>
        /// <param name="currentModel"></param>
        /// <returns></returns>
        public static object ClaudeRequestBody(string inputText, string currentModel)
        {
            if (string.IsNullOrEmpty(inputText) || string.IsNullOrEmpty(currentModel))
            {
                throw new ArgumentException("Input text or model cannot be null or empty.");
            }
            return new
            {
                model = currentModel,
                max_tokens = 1024,
                messages = new[]
                {
                    new
                    {
                        role = "user",
                        content = inputText
                    }
                }
            };
        }
    }
}
