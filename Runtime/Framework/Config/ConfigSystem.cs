using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    [FrameworkSystem(FrameworkSystemPriority.Config)]
    internal sealed class ConfigSystem : FrameworkSystem, IConfigService
    {
        private bool m_isLoaded; // 配置全量加载状态。

        /// <summary>
        /// 获取全部业务配置是否已加载完成。
        /// </summary>
        public bool IsLoaded => m_isLoaded;

        /// <summary>
        /// 执行全量业务配置加载。
        /// </summary>
        /// <param name="context">当前框架上下文。</param>
        /// <param name="cancellationToken">用于取消本次加载的令牌。</param>
        /// <returns>表示异步初始化过程的任务。</returns>
        public override async UniTask InitializeAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            // Config 优先级位于 Resource 之后，可安全使用资源加载服务。
            await AlloyConfig.Instance.LoadAllAsync(cancellationToken);
            m_isLoaded = true;
        }

        /// <summary>
        /// 释放全部业务配置资源。
        /// </summary>
        public override void Shutdown()
        {
            AlloyConfig.Instance.UnloadAll();
            m_isLoaded = false;
        }
    }
}
