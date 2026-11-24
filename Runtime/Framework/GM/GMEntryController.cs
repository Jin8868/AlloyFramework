using AlloyFramework.UI;
using UnityEngine.UI;

namespace AlloyFramework.GM
{
    /// <summary>将入口按钮的点击交给本轮 GM 会话。</summary>
    internal sealed class GMEntryController : UIController<GMEntryView, GMUISession>
    {
        private Button m_openButton; // 入口预制体上的按钮。

        protected override void OnCreate()
        {
            m_openButton = View.GetOpenButton();
            m_openButton.onClick.AddListener(HandleOpen);
        }

        protected override void OnDispose()
        {
            if (m_openButton != null)
            {
                m_openButton.onClick.RemoveListener(HandleOpen);
            }
        }

        private void HandleOpen()
        {
            if (State == UIState.Active)
            {
                Data.OpenPanel();
            }
        }
    }
}
