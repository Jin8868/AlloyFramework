using System;
using UnityEngine;

namespace AlloyFramework.GM
{
    /// <summary>保存已验证的命令及其面板输入状态。</summary>
    internal sealed class GMCommand
    {
        private readonly Action m_button; // 按钮点击时执行的命令。
        private readonly Action<bool> m_toggle; // 开关变化时执行的命令。
        private readonly Action<string> m_text; // 提交输入时执行的命令。

        internal string Label { get; }
        internal EGMControlKind Kind { get; }
        internal bool ToggleValue { get; private set; }
        internal string TextValue { get; private set; } = string.Empty;

        internal GMCommand(string label, Action button)
        {
            Label = label;
            Kind = EGMControlKind.Button;
            m_button = button;
        }

        internal GMCommand(string label, Action<bool> toggle)
        {
            Label = label;
            Kind = EGMControlKind.Toggle;
            m_toggle = toggle;
        }

        internal GMCommand(string label, Action<string> text)
        {
            Label = label;
            Kind = EGMControlKind.Text;
            m_text = text;
        }

        internal void InvokeButton()
        {
            try
            {
                m_button();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal void SetToggle(bool value)
        {
            try
            {
                m_toggle(value);
                ToggleValue = value;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        internal void SubmitText(string value)
        {
            try
            {
                m_text(value);
                TextValue = value;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }
    }
}
