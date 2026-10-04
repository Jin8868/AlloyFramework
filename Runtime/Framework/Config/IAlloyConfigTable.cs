namespace AlloyFramework
{
    /// <summary>
    /// 定义按配置记录 ID 查询的配置表适配器。
    /// </summary>
    /// <typeparam name="TConfig">配置记录的运行时类型。</typeparam>
    public interface IAlloyConfigTable<TConfig> where TConfig : class
    {
        /// <summary>
        /// 尝试按配置记录 ID 获取配置。
        /// </summary>
        /// <param name="configID">配置记录的唯一标识。</param>
        /// <param name="config">成功时返回配置记录。</param>
        /// <returns>找到配置记录时返回 true，否则返回 false。</returns>
        bool TryGet(int configID, out TConfig config);
    }
}
