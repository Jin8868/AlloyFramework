using System.Collections.Generic;
using AlloyFramework.UI;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.GM
{
    /// <summary>绑定本轮 GM 命令，并通过界面句柄关闭面板。</summary>
    internal sealed class GMPanelController : UIController<GMPanel, IReadOnlyList<GMCommand>>
    {
        protected override void OnInitData(IReadOnlyList<GMCommand> commands)
        {
            View.Initialize(commands, HandleClose);
        }

        private void HandleClose()
        {
            if (State == UIState.Active)
            {
                CloseAsync().Forget(Debug.LogException);
            }
        }
    }
}
