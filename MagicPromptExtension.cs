using hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace hartsy.Extensions.MagicPromptExtension
{
    public class MagicPromptExtension : Extension
    {
        public override void OnPreInit()
        {
            Logs.Debug("MagicPromptExtension started. Visit https://Hartsy.AI to see more.");
            ScriptFiles.Add("Assets/magicprompt.js");
        }

        public override void OnInit()
        {
            MagicPromptAPI.Register();
        }
    }
}
