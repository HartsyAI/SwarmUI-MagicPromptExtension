using Hartsy.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Hartsy.Extensions.MagicPromptExtension
{
    public class MagicPromptExtension : Extension
    {
        public override void OnPreInit()
        {
            Logs.Info("MagicPromptExtension started.");
            ScriptFiles.Add("Assets/magicprompt.js");
        }

        public override void OnInit()
        {
            MagicPromptAPI.Register();
        }
    }
}
