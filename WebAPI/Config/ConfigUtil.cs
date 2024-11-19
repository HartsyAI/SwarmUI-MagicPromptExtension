using System.Reflection;
using System.Text.Json;
using SwarmUI.Utils;
using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using System.IO;

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
        private static readonly string ConfigExamplePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory,
            "src/Extensions/SwarmUI-MagicPromptExtension", "config.json.example");

        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Reads and initializes the configuration, handling migration if necessary.</summary>
        public static void ReadConfig()
        {
            EnsureConfigExists();
            MigrateConfigIfNeeded();
            string json = File.ReadAllText(ConfigFilePath);
            GlobalConfig.ConfigData = JsonSerializer.Deserialize<ConfigData>(json, SerializerOptions);
        }

        /// <summary>Ensures a valid config.json exists by copying from example if necessary.</summary>
        private static void EnsureConfigExists()
        {
            if (File.Exists(ConfigFilePath))
                return;
            if (!File.Exists(ConfigExamplePath))
                throw new FileNotFoundException("Neither config.json nor config.json.example were found.");
            File.Copy(ConfigExamplePath, ConfigFilePath);
            Logs.Info("Created new config.json from example file.");
        }

        /// <summary>Checks if config needs migration and performs it if necessary.</summary>
        private static void MigrateConfigIfNeeded()
        {
            if (!File.Exists(ConfigExamplePath) || !File.Exists(ConfigFilePath))
                return;
            string exampleJson = File.ReadAllText(ConfigExamplePath);
            string currentJson = File.ReadAllText(ConfigFilePath);
            ConfigData exampleConfig = JsonSerializer.Deserialize<ConfigData>(exampleJson, SerializerOptions);
            ConfigData currentConfig = JsonSerializer.Deserialize<ConfigData>(currentJson, SerializerOptions);
            if (exampleConfig == null || currentConfig == null)
                return;
            bool needsMigration = false;
            ConfigData migratedConfig = MergeConfigs(currentConfig, exampleConfig, ref needsMigration);
            if (needsMigration)
            {
                string updatedJson = JsonSerializer.Serialize(migratedConfig, SerializerOptions);
                File.WriteAllText(ConfigFilePath, updatedJson);
                Logs.Info("Config file successfully migrated with new properties.");
            }
        }

        /// <summary>Recursively merges two objects, keeping existing values while adding new properties.</summary>
        private static T MergeConfigs<T>(T current, T example, ref bool needsMigration) where T : class
        {
            if (current == null) return example;
            if (example == null) return current;
            Type type = typeof(T);
            PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            // Clone the current object to maintain existing state
            T merged = current;
            foreach (PropertyInfo prop in properties)
            {
                try
                {
                    object currentValue = prop.GetValue(current);
                    object exampleValue = prop.GetValue(example);
                    // Special handling for Dictionary types
                    if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
                    {
                        if (currentValue == null)
                        {
                            prop.SetValue(merged, exampleValue);
                            needsMigration = true;
                        }
                        continue;
                    }
                    // For other class types (excluding string)
                    if (prop.PropertyType.IsClass && prop.PropertyType != typeof(string))
                    {
                        if (currentValue == null && exampleValue != null)
                        {
                            prop.SetValue(merged, exampleValue);
                            needsMigration = true;
                        }
                        else if (currentValue != null && exampleValue != null)
                        {
                            object mergedValue = MergeConfigs(currentValue, exampleValue, ref needsMigration);
                            prop.SetValue(merged, mergedValue);
                        }
                    }
                    else
                    {
                        // For primitive types and strings
                        if (currentValue == null && exampleValue != null)
                        {
                            prop.SetValue(merged, exampleValue);
                            needsMigration = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logs.Error($"Error merging property {prop.Name}: {ex.Message}");
                    continue;
                }
            }
            return merged;
        }

        /// <summary>Updates the configuration in memory and writes the changes to config.json.</summary>
        public static void UpdateConfig(ConfigData config)
        {
            if (GlobalConfig.ConfigData == null)
            {
                ReadConfig();  // Load config into memory if not already loaded
            }
            GlobalConfig.ConfigData = config;
            string json = JsonSerializer.Serialize(GlobalConfig.ConfigData, SerializerOptions);
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