using Newtonsoft.Json.Linq;
using SwarmUI.Utils;
using SwarmUI.WebAPI;
using System.Net.Http;
using System.Text.Json;
using System.IO;
using Kalebbroo.Extensions.MagicPromptExtension.WebAPI.Models;

namespace Kalebbroo.Extensions.MagicPromptExtension.WebAPI
{
    [API.APIClass("API routes related to MagicPromptExtension extension")]
    public static class MagicPromptAPI
    {
        /// <summary>Registers the API call for the extension, enabling methods to be called from JavaScript.</summary>
        public static void Register()
        {
            API.RegisterAPICall(PhoneHomeAsync, true);
        }

        private static readonly HttpClient _httpClient = new();

        /// <summary>Sends the prompt to the LLM API and processes the response.</summary>
        /// <returns>Returns a JSON object with success and a rewritten prompt or an error.</returns>
        [API.APIDescription("Returns a JSON object containing the response from the language model API or an error message.",
            """
            {
                "success": true,
                "response": string,
                "error": string
            }
            """)]
        public static async Task<JObject> PhoneHomeAsync(
            [API.APIParameter("Text input")] string inputText)
        {
            try
            {
                var (instructions, llmEndpoint) = await LoadConfigData();
                if (instructions == null || llmEndpoint == null)
                {
                    return CreateErrorResponse("Failed to load configuration.");
                }
                object requestBody = CreateRequestBody(inputText, instructions, llmEndpoint);
                HttpResponseMessage response = await SendRequest(llmEndpoint, requestBody);
                if (response.IsSuccessStatusCode)
                {
                    string llmResponse = await DeserializeResponse(response);
                    if (!string.IsNullOrEmpty(llmResponse))
                    {
                        llmResponse = llmResponse.Replace("<|end_of_turn|>", "");
                        return CreateSuccessResponse(llmResponse);
                    }
                    else
                    {
                        return CreateErrorResponse("Unexpected API response format or empty response.");
                    }
                }
                else
                {
                    return CreateErrorResponse($"API request failed with status code: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Logs.Error(ex.Message);
                return CreateErrorResponse(ex.Message);
            }
        }

        /// <summary>Loads the setup.json file, extracts instructions and the API endpoint, handles errors if they occur.</summary>
        /// <returns>Tuple containing the instructions and API endpoint, or null values if loading fails.</returns>
        private static async Task<(string instructions, string llmEndpoint)> LoadConfigData()
        {
            string configFilePath = Utilities.CombinePathWithAbsolute(Environment.CurrentDirectory, "src/Extensions/MagicPromptExtension", "setup.json");
            try
            {
                string configContent = await File.ReadAllTextAsync(configFilePath);
                ConfigData configData = JsonSerializer.Deserialize<ConfigData>(configContent);
                if (configData == null)
                {
                    Logs.Error("Failed to deserialize config data.");
                    return (null, null);
                }
                return (configData.Instructions, configData.LlmEndpoint);
            }
            catch (Exception ex)
            {
                Logs.Error($"Error reading or deserializing config file: {ex.Message}");
                return (null, null);
            }
        }

        /// <summary>Chooses the body for the LLM API request. This will be dynamic if needed.</summary>
        /// <returns>An object representing the JSON structure for the API request.</returns>
        private static object CreateRequestBody(string inputText, string instructions, string llmEndpoint)
        {
            // TODO: Add other request body formats for other API backends if needed
            return new
            {
                model = "openchat-3.5-7b",  // TODO: Create method to get current model instead of hardcoding
                messages = new[]
                    {
                        new { role = "system", content = instructions },
                        new { role = "user", content = inputText }
                    },
                stream = false,
                max_tokens = 2048,
                stop = new[] { "End" },
                frequency_penalty = 0.2,
                presence_penalty = 0.6,
                temperature = 0.8,
                top_p = 0.95
            };
        }

        /// <summary>Sends a POST request to the LLM API endpoint with the chosen request body. The body should match what LLM backend you are using.</summary>
        /// <returns>HttpResponseMessage received from the API.</returns>
        private static async Task<HttpResponseMessage> SendRequest(string endpoint, object requestBody)
        {
            try
            {
                string jsonRequestBody = JsonSerializer.Serialize(requestBody);
                StringContent httpContent = new(jsonRequestBody, Encoding.UTF8, "application/json");
                HttpResponseMessage response = await _httpClient.PostAsync(endpoint, httpContent);
                return response;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error sending request: {ex.Message}");
                throw;
            }
        }

        /// <summary>Makes the JSON response into a structured object and extracys the Message.Content.</summary>
        /// <returns>The rewritten prompt, or null if deserialization fails.</returns>
        private static async Task<string> DeserializeResponse(HttpResponseMessage response)
        {
            try
            {
                string responseContent = await response.Content.ReadAsStringAsync();
                JsonSerializerOptions jsonSerializerOptions = new()
                {
                    PropertyNameCaseInsensitive = true
                };
                JsonSerializerOptions options = jsonSerializerOptions;
                LLMResponse llmResponse = JsonSerializer.Deserialize<LLMResponse>(responseContent, options);
                if (llmResponse?.Choices != null && llmResponse.Choices.Length > 0)
                {
                    Choice choice = llmResponse.Choices[0];
                    if (choice.Message != null)
                    {
                        return choice.Message.Content;
                    }
                    else
                    {
                        Logs.Error("Message object is null in the Choice element.");
                    }
                }
                else
                {
                    Logs.Error("Choices array is null or empty.");
                }
                return null;
            }
            catch (Exception ex)
            {
                Logs.Error($"Error deserializing response: {ex.Message}");
                return null;
            }
        }

        /// <summary>Creates a JSON object for a success, includes the rewritten prompt.</summary>
        /// <returns>The success bool and the rewritten prompt to the JavaScript function that called it.</returns>
        private static JObject CreateSuccessResponse(string llmResponse)
        {
            return new JObject
            {
                { "success", true },
                { "response", llmResponse }
            };
        }

        /// <summary>Creates a JSON object for a failure, includes the error message.</summary>
        /// <returns>The success bool and the error to the JavaScript function that called it.</returns>
        private static JObject CreateErrorResponse(string errorMessage)
        {
            return new JObject
            {
                { "success", false },
                { "error", errorMessage }
            };
        }
    }
}
