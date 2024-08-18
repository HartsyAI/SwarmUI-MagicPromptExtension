using Kalebbroo.Extensions.MagicPromptExtension.WebAPI;
using SwarmUI.Core;
using SwarmUI.Utils;

namespace Kalebbroo.Extensions.MagicPromptExtension
{
    public class MagicPromptExtension : Extension
    {
        public override void OnPreInit()
        {
            Logs.Debug("MagicPromptExtension started.");
            ScriptFiles.Add("Assets/magicprompt.js");
        }

        public override void OnInit()
        {
            MagicPromptAPI.Register();
        }
    }
}
