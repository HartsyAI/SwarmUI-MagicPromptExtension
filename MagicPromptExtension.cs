using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;
using SwarmUI.Text2Image;
using Newtonsoft.Json.Linq;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    public override void OnPreInit()
    {
        Logs.Info("MagicPromptExtension Version 2.2 Now with Vision! has started.");
        ScriptFiles.Add("Assets/magicprompt.js");
        ScriptFiles.Add("Assets/vision.js");
        ScriptFiles.Add("Assets/chat.js");
        ScriptFiles.Add("Assets/settings.js");
        StyleSheetFiles.Add("Assets/magicprompt.css");
        StyleSheetFiles.Add("Assets/vision.css");
        StyleSheetFiles.Add("Assets/chat.css");
        StyleSheetFiles.Add("Assets/settings.css");
    }

    public override void OnInit()
    {
        // Register API endpoints so they can be used in the frontend
        MagicPromptAPI.Register();
        AddT2IParameters();
    }

    public void AddT2IParameters()
    {
        T2IParamInput.LateSpecialParameterHandlers.Add(userInput =>
            {
                var posPrompt = userInput.InternalSet.Get(T2IParamTypes.Prompt);
                if (posPrompt != null)
                {
                    try
                    {
                        // Build request for MagicPromptPhoneHome including required fields
                        string modelId = null;
                        try
                        {
                            var settingsResp = SessionSettings.GetMagicPromptSettings().GetAwaiter().GetResult();
                            if (settingsResp != null && settingsResp["success"]?.Value<bool>() == true)
                            {
                                var settings = settingsResp["settings"] as JObject;
                                modelId = settings?["model"]?.ToString();
                            }
                        }
                        catch { /* ignore and leave modelId null */ }

                        JObject request = new()
                        {
                            ["messageContent"] = new JObject
                            {
                                ["text"] = posPrompt,
                                // Leave instructions empty to let server-side default logic choose based on action
                                ["instructions"] = ""
                            },
                            ["modelId"] = modelId ?? string.Empty,
                            ["messageType"] = "Text",
                            ["action"] = "prompt",
                            ["session_id"] = userInput.SourceSession?.ID ?? string.Empty
                        };
                        var resp = LLMAPICalls.MagicPromptPhoneHome(request, userInput.SourceSession).GetAwaiter().GetResult();
                        string newPrompt = resp?["response"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(newPrompt))
                        {
                            userInput.InternalSet.Set(T2IParamTypes.Prompt, newPrompt);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Debug($"MagicPrompt phone home call failed: {ex.Message}");
                    }
                }
            });
    }
}

