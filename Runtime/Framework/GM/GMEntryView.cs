using System;
using AlloyFramework.UI;
using UnityEngine.UI;

namespace AlloyFramework.GM
{
    /// <summary>提供打开 GM 面板的预制体入口。</summary>
    public sealed class GMEntryView : UIView
    {
        internal Button GetOpenButton()
        {
            var button = transform.Find("SafeAreaContent/btn_open")?.GetComponent<Button>();
            if (button == null)
            {
                throw new InvalidOperationException("GM 入口预制体缺少 SafeAreaContent/btn_open 上的 Button。");
            }
            return button;
        }
    }
}
