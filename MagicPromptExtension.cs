using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension;

public class MagicPromptExtension : Extension
{
    public override void OnPreInit()
    {
        Logs.Info("MagicPromptExtension Version 2.1 Now with Vision! has started.");
        ScriptFiles.Add("Assets/magicprompt.js");
        ScriptFiles.Add("Assets/vision.js");
        ScriptFiles.Add("Assets/chat.js");
        StyleSheetFiles.Add("Assets/magicprompt.css");
        StyleSheetFiles.Add("Assets/vision.css");
        StyleSheetFiles.Add("Assets/chat.css");
    }

    public override void OnInit()
    {
        // Register API endpoints so they can be used in the frontend
        MagicPromptAPI.Register();
    }
}

