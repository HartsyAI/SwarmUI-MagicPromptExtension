using Hartsy.Extensions.MagicPromptExtension.WebAPI.Models;
using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;

namespace Hartsy.Extensions.MagicPromptExtension.WebAPI.Config
{
    public class SaveConfigSettings : MagicPromptAPI
    {
        /// <summary>Saves user settings to the setup.json file.</summary>
        /// <param name="selectedBackend">The selected backend.</param>
        /// <param name="modelUnload">Whether to unload the model after use.</param>
        /// <param name="apiUrl">The API URL.</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        public static async Task<JObject> SaveSettingsAsync(
            [API.APIParameter("Selected Backend")] string selectedBackend,
            [API.APIParameter("Model Unload Option")] bool modelUnload,
            [API.APIParameter("API URL")] string apiUrl)
        {
            try
            {
                // TODO: if the user uses Paid API ignore URL field and check for API key exists.
                ConfigData config = GlobalConfig.ConfigData;
                config.LLMBackend = selectedBackend.ToLower();
                config.UnloadModel = modelUnload;
                if (config == null || string.IsNullOrEmpty(config.LLMBackend))
                {
                    return CreateErrorResponse("Config data is not loaded or LLMBackend is not configured.");
                }
                switch (config.LLMBackend)
                {
                    case "openai":
                        if (!string.IsNullOrEmpty(apiUrl))
                        {
                            config.Backends.OpenAI.ApiKey = apiUrl;
                        }
                        break;
                    case "anthropic":
                        if (!string.IsNullOrEmpty(apiUrl))
                        {
                            config.Backends.Anthropic.ApiKey = apiUrl;
                        }
                        break;
                    case "openaiapi":
                        if (!string.IsNullOrEmpty(apiUrl))
                        {
                            config.Backends.OpenAIAPI.BaseUrl = apiUrl;
                        }
                        break;
                    case "ollama":
                        if (!string.IsNullOrEmpty(apiUrl))
                        {
                            config.LlmEndpoint = apiUrl;
                        }
                        break;
                    default:
                        return CreateErrorResponse($"Unknown backend: {selectedBackend}");
                }
                ConfigUtil.UpdateConfig(config);
                Logs.Info("LLM settings saved successfully.");
                return CreateSuccessResponse("Settings saved successfully.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error saving settings: {ex.Message}");
                return CreateErrorResponse($"Error saving settings: {ex.Message}");
            }
        }

        /// <summary>Reads the configuration file asynchronously.</summary>
        /// <returns>A JSON object with the configuration data or an error message.</returns>
        public static async Task<JObject> ReadConfigAsync()
        {
            try
            {
                if (GlobalConfig.ConfigData == null)
                {
                    ConfigUtil.ReadConfig();
                    if (GlobalConfig.ConfigData == null)
                    {
                        return CreateErrorResponse("Failed to load configuration from file.");
                    }
                }
                ConfigData configDataObj = GlobalConfig.ConfigData;
                return CreateSuccessResponse("success"); // TODO: What do we need to do with the config data now that we have it?
            }
            catch (Exception ex)
            {
                Logs.Error($"Exception occurred while reading config: {ex.Message}");
                return CreateErrorResponse(ex.Message);
            }
        }

        /// <summary>Saves a new API key to the config.json file.</summary>
        /// <param name="apiKey">The new API key entered by the user.</param>
        /// <param name="apiProvider">The API provider (e.g., OpenAI, Claude).</param>
        /// <returns>A JSON object indicating success or failure.</returns>
        public static async Task<JObject> SaveApiKeyAsync(
        [API.APIParameter("API Key")] string apiKey,
        [API.APIParameter("API Provider")] string apiProvider)
        {
            try
            {
                ConfigData config = GlobalConfig.ConfigData;
                if (config == null)
                {
                    return CreateErrorResponse("Config data is not loaded.");
                }

                if (apiProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        config.Backends.OpenAI.ApiKey = apiKey;
                    }
                }
                else if (apiProvider.Equals("Anthropic", StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrEmpty(apiKey))
                    {
                        config.Backends.OpenAI.ApiKey = apiKey;
                    }
                }
                else
                {
                    return CreateErrorResponse($"Unknown API provider: {apiProvider}");
                }
                ConfigUtil.UpdateConfig(config);
                Logs.Info($"API key for {apiProvider} saved successfully.");
                return CreateSuccessResponse($"API key for {apiProvider} saved successfully.");
            }
            catch (Exception ex)
            {
                Logs.Error($"Error saving API key: {ex.Message}");
                return CreateErrorResponse($"Error saving API key: {ex.Message}");
            }
        }
    }
}