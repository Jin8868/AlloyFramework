using System;
using System.Collections.Generic;
using AlloyFramework.UI;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEventSystem = UnityEngine.EventSystems.EventSystem;

namespace AlloyFramework.GM
{
    /// <summary>
    /// 绑定 GM 面板预制体并按命令类型填充操作列表。
    /// </summary>
    public sealed class GMPanel : UIView
    {
        private const float PADDING = 20f; // 命令行内部的横向留白。
        private const float ROWHEIGHT = 80f; // 按钮和输入区域的触屏操作高度。
        private static readonly Color m_trackColor = new Color32(15, 24, 37, 255); // 开关及输入控件底色。
        private static readonly Color m_controlColor = new Color32(46, 62, 81, 255); // 一般命令控件底色。
        private static readonly Color m_accentColor = new Color32(82, 226, 195, 255); // 开关启用时的指示色。
        private static readonly Color m_submitColor = new Color32(0, 104, 96, 255); // 提交按钮底色。
        // 常见系统中文字体，按优先级排序。
        private static readonly string[] m_chineseFontNames =
        {
            "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Heiti SC", "Noto Sans CJK SC",
            "Noto Sans SC", "Droid Sans Fallback", "SimHei"
        };
        [SerializeField] private Button m_closeButton; // 交给框架关闭当前面板的按钮。
        [SerializeField] private RectTransform m_content; // 滚动命令列表的内容节点。
        [SerializeField] private Text m_emptyText; // 无有效命令时显示的提示。
        [SerializeField] private Sprite m_controlShape; // 命令控件使用的圆角九宫格。
        private Font m_font; // 当前用于绘制命令文字的字体。
        private Font m_systemFont; // 面板负责释放的动态系统字体。

        internal void Initialize(IReadOnlyList<GMCommand> commands, UnityAction close)
        {
            // 弹窗接管交互时，清除底层界面保留的键盘焦点。
            if (UnityEventSystem.current != null)
                UnityEventSystem.current.SetSelectedGameObject(null);

            // 预制体持有固定布局，运行时只创建注册的命令行。
            m_font = GetDisplayFont();
            m_closeButton.onClick.AddListener(close);
            m_emptyText.font = m_font;
            m_emptyText.gameObject.SetActive(commands.Count == 0);
            foreach (var command in commands)
            {
                switch (command.Kind)
                {
                    case EGMControlKind.Button:
                        var button = CreateButton(m_content, command.Label, m_controlColor);
                        button.onClick.AddListener(command.InvokeButton);
                        break;
                    case EGMControlKind.Toggle:
                        CreateToggle(m_content, command);
                        break;
                    case EGMControlKind.Text:
                        CreateTextCommand(m_content, command);
                        break;
                }
            }
        }

        private void OnDisable()
        {
            // 关闭视图后，键盘焦点不应继续指向已停用的输入框。
            var eventSystem = UnityEventSystem.current;
            var selected = eventSystem != null ? eventSystem.currentSelectedGameObject : null;
            if (selected != null && selected.transform.IsChildOf(transform))
                eventSystem.SetSelectedGameObject(null);
        }

        private void OnDestroy()
        {
            if (m_systemFont != null)
                Destroy(m_systemFont);
        }

        private void CreateToggle(RectTransform parent, GMCommand command)
        {
            // 整行可点击，右侧的青绿色指示块表示已启用。
            var row = CreateRow(parent, command.Label, ROWHEIGHT);
            var background = AddImage(row, m_controlColor);
            var label = CreateText(row, command.Label, 30, TextAnchor.MiddleLeft);
            Stretch(label.rectTransform, new Vector2(PADDING, 0f), new Vector2(-ROWHEIGHT, 0f));
            var marker = CreateRect(row, "Indicator");
            marker.anchorMin = new Vector2(1f, 0.5f);
            marker.anchorMax = marker.anchorMin;
            marker.pivot = new Vector2(1f, 0.5f);
            marker.anchoredPosition = new Vector2(-PADDING, 0f);
            marker.sizeDelta = new Vector2(38f, 38f);
            AddImage(marker, m_trackColor).raycastTarget = false;
            var selected = CreateRect(marker, "Selected");
            Stretch(selected, new Vector2(4f, 4f), new Vector2(-4f, -4f));
            var toggle = row.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = background;
            toggle.graphic = AddImage(selected, m_accentColor);
            toggle.graphic.raycastTarget = false;
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.SetIsOnWithoutNotify(command.ToggleValue);
            toggle.onValueChanged.AddListener(command.SetToggle);
        }

        private void CreateTextCommand(RectTransform parent, GMCommand command)
        {
            // 输入与提交按钮并排放在标签下方，为窄屏上的长标签保留完整行宽。
            var row = CreateRow(parent, command.Label, ROWHEIGHT + 54f);
            var label = CreateText(row, command.Label, 30, TextAnchor.MiddleLeft);
            label.rectTransform.anchorMin = Vector2.up;
            label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = new Vector2(0f, -48f);
            label.rectTransform.offsetMax = Vector2.zero;

            const float SUBMITWIDTH = 140f;
            var fieldRect = CreateRect(row, "Input");
            Stretch(fieldRect, Vector2.zero, new Vector2(-SUBMITWIDTH - 12f, -54f));
            var background = AddImage(fieldRect, m_trackColor);
            var value = CreateText(fieldRect, string.Empty, 30, TextAnchor.MiddleLeft);
            Stretch(value.rectTransform, new Vector2(PADDING, 0f), new Vector2(-PADDING, 0f));
            var field = fieldRect.gameObject.AddComponent<InputField>();
            field.targetGraphic = background;
            field.textComponent = value;
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.SetTextWithoutNotify(command.TextValue);

            var submit = CreateButton(row, "执行", m_submitColor);
            var submitRect = (RectTransform)submit.transform;
            submitRect.anchorMin = Vector2.right;
            submitRect.anchorMax = Vector2.right;
            submitRect.offsetMin = new Vector2(-SUBMITWIDTH, 0f);
            submitRect.offsetMax = new Vector2(0f, ROWHEIGHT);
            var submission = new TextSubmission(command, field);
            submit.onClick.AddListener(submission.Submit);
        }

        private Button CreateButton(RectTransform parent, string caption, Color color)
        {
            var rect = CreateRow(parent, caption, ROWHEIGHT);
            var image = AddImage(rect, color);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var text = CreateText(rect, caption, 30, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform, new Vector2(8f, 0f), new Vector2(-8f, 0f));
            return button;
        }

        private Text CreateText(RectTransform parent, string caption, int size, TextAnchor alignment)
        {
            var rect = CreateRect(parent, "Text");
            var text = rect.gameObject.AddComponent<Text>();
            text.font = m_font;
            text.fontSize = size;
            text.text = caption;
            text.alignment = alignment;
            text.color = Color.white;
            text.raycastTarget = false;
            text.supportRichText = false;
            return text;
        }

        private Font GetDisplayFont()
        {
            var builtin = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (builtin.HasCharacter('执'))
                return builtin;

            // 内置字体缺少控件文字时，选择已安装的中文系统字体。
            var installedNames = Font.GetOSInstalledFontNames();
            if (installedNames == null)
                return builtin;

            foreach (var fontName in m_chineseFontNames)
            {
                if (Array.IndexOf(installedNames, fontName) < 0)
                    continue;

                m_systemFont = Font.CreateDynamicFontFromOSFont(fontName, 22);
                if (m_systemFont != null)
                    return m_systemFont;
            }

            return builtin;
        }

        private static RectTransform CreateRow(RectTransform parent, string name, float height)
        {
            var rect = CreateRect(parent, name);
            var layout = rect.gameObject.AddComponent<LayoutElement>();
            layout.minHeight = height;
            layout.preferredHeight = height;
            return rect;
        }

        private static RectTransform CreateRect(RectTransform parent, string name)
        {
            var child = new GameObject(name, typeof(RectTransform)) { layer = parent.gameObject.layer };
            var rect = (RectTransform)child.transform;
            rect.SetParent(parent, false);
            return rect;
        }

        private Image AddImage(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.sprite = m_controlShape;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.5f;
            return image;
        }

        private static void Stretch(RectTransform rect, Vector2 minimumOffset, Vector2 maximumOffset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = minimumOffset;
            rect.offsetMax = maximumOffset;
        }

        private sealed class TextSubmission
        {
            private readonly GMCommand m_command; // 当前输入行对应的命令。
            private readonly InputField m_field; // 获取提交时的当前文本。

            internal TextSubmission(GMCommand command, InputField field)
            {
                m_command = command;
                m_field = field;
            }

            internal void Submit()
            {
                m_command.SubmitText(m_field.text);
            }
        }
    }
}
