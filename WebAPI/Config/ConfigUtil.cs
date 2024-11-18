using System.IO;
using System.Text.Json;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Config
{
    public static class GlobalConfig
    {
        public static ConfigData ConfigData { get; set; }
    }

    public class ConfigUtil
    {
        private static readonly string ConfigFilePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory,
            "src/Extensions/SwarmUI-MagicPromptExtension", "config.json");

        /// <summary>Reads the current configuration from config.json and stores it in memory.</summary>
        /// <returns>A dictionary of the configuration data.</returns>
        public static void ReadConfig()
        {
            if (!File.Exists(ConfigFilePath))
            {
                throw new FileNotFoundException("Configuration file not found.");
            }
            string json = File.ReadAllText(ConfigFilePath);
            GlobalConfig.ConfigData = JsonSerializer.Deserialize<ConfigData>(json);
        }

        /// <summary>Updates the configuration in memory and writes the changes to config.json.</summary>
        /// <param name="updates">A dictionary of new configuration values.</param>
        public static void UpdateConfig(ConfigData config)
        {
            if (GlobalConfig.ConfigData == null)
            {
                ReadConfig();  // Load config into memory if not already loaded
            }
            GlobalConfig.ConfigData = config;
            // Serialize and write the updated config back to the file
            string json = JsonSerializer.Serialize(GlobalConfig.ConfigData, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }

        /// <summary>Get the LLM endpoint dynamically based on the backend.</summary>
        public static string GetLlmEndpoint(string backend, string endpointType)
        {
            ConfigData config = GlobalConfig.ConfigData;
            string endpoint = backend.ToLower() switch
            {
                "ollama" => config.LlmEndpoint + config.Backends.Ollama.Endpoints[endpointType],
                "openai" => config.Backends.OpenAI.BaseUrl + config.Backends.OpenAI.Endpoints[endpointType],
                "openaiapi" => config.Backends.OpenAIAPI.BaseUrl + config.Backends.OpenAIAPI.Endpoints[endpointType],
                "anthropic" => config.Backends.Anthropic.BaseUrl + config.Backends.Anthropic.Endpoints[endpointType],
                "openrouter" => config.Backends.OpenRouter.BaseUrl + config.Backends.OpenRouter.Endpoints[endpointType],
                _ => throw new InvalidOperationException($"Unknown backend: {backend}"),
            };
            return endpoint;
        }
    }
}
