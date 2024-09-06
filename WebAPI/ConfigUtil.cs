using System.IO;
using System.Text.Json;
using SwarmUI.Utils;

namespace SwarmUI.Extensions.SwarmUI_MagicPromptExtension.WebAPI
{
    public class ConfigUtil
    {
        private static readonly string ConfigFilePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, 
            "src/Extensions/SwarmUI-MagicPromptExtension", "config.json");

        /// <summary>
        /// Reads the current configuration from config.json without deserializing into strict types.
        /// </summary>
        /// <returns>A dynamic JSON object.</returns>
        public static Dictionary<string, object> ReadConfig()
        {
            if (!File.Exists(ConfigFilePath))
            {
                throw new FileNotFoundException("Configuration file not found.");
            }
            string json = File.ReadAllText(ConfigFilePath);
            Dictionary<string, object> configData = JsonSerializer.Deserialize<Dictionary<string, object>>(json);
            return configData;
        }

        /// <summary>
        /// Updates the configuration file only with the provided values.
        /// </summary>
        /// <param name="updates">A dictionary of new configuration values.</param>
        public static void UpdateConfig(Dictionary<string, string> updates)
        {
            Dictionary<string, object> currentConfig = ReadConfig();
            foreach (var key in updates.Keys)
            {
                if (!string.IsNullOrEmpty(updates[key]))
                {
                    currentConfig[key] = updates[key];  // Only update non-empty values
                }
            }
            string json = JsonSerializer.Serialize(currentConfig, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(ConfigFilePath, json);
        }
    }
}
