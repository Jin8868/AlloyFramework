namespace AlloyFramework
{
    /// <summary>
    /// 表示框架配置加载步骤的运行状态。
    /// </summary>
    public interface IConfigService : IFrameworkSystem
    {
        /// <summary>
        /// 获取全部业务配置是否已加载完成。
        /// </summary>
        bool IsLoaded { get; }
    }
}
