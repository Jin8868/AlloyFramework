namespace AlloyFramework.UI
{
    public interface IUIDefinitionProvider
    {
        /// <summary>
        /// 判断提供器是否包含指定的稳定 UI 名称。
        /// </summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <returns>包含该名称时返回 true，否则返回 false。</returns>
        bool Contains(string uiName);

        /// <summary>
        /// 尝试按稳定 UI 名称取得界面定义。
        /// </summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="definition">成功时返回对应界面定义。</param>
        /// <returns>能够解析该界面名称时返回 true，否则返回 false。</returns>
        bool TryGetDefinition(string uiName, out UIDefinition definition);
    }
}
