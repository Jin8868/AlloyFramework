namespace AlloyFramework
{
    /// <summary>由主包内置配置资产提供框架启动所需的类型和入口信息。</summary>
    public interface IGameStartupConfigurationProvider
    {
        /// <summary>配置资源与音频接入约定，并声明各步骤需要创建的实现类型。</summary>
        /// <returns>本轮框架启动配置。</returns>
        GameStartupConfiguration CreateStartupConfiguration();
    }
}
