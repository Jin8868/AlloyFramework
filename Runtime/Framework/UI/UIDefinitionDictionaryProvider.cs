using System;
using System.Collections.Generic;

namespace AlloyFramework.UI
{
    public sealed class UIDefinitionDictionaryProvider : IUIDefinitionProvider
    {
        private readonly IReadOnlyDictionary<string, UIDefinition> m_definitions; // 按稳定名称保存的 UI 定义。

        /// <summary>
        /// 创建基于只读字典的 UI 定义提供器。
        /// </summary>
        /// <param name="definitions">已经完成构建的全部 UI 定义。</param>
        public UIDefinitionDictionaryProvider(
            IReadOnlyDictionary<string, UIDefinition> definitions)
        {
            m_definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        /// <summary>
        /// 判断是否包含指定的稳定 UI 名称。
        /// </summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <returns>包含该名称时返回 true，否则返回 false。</returns>
        public bool Contains(string uiName)
        {
            return !string.IsNullOrEmpty(uiName) && m_definitions.ContainsKey(uiName);
        }

        /// <summary>
        /// 尝试按稳定 UI 名称取得界面定义。
        /// </summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="definition">成功时返回对应界面定义。</param>
        /// <returns>找到定义时返回 true，否则返回 false。</returns>
        public bool TryGetDefinition(string uiName, out UIDefinition definition)
        {
            if (string.IsNullOrEmpty(uiName))
            {
                definition = null;
                return false;
            }

            return m_definitions.TryGetValue(uiName, out definition);
        }
    }
}
