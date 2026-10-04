using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 定义业务配置的加载和释放行为。
    /// </summary>
    public interface IAlloyConfigLoader
    {
        /// <summary>
        /// 全量加载业务配置并注册可供框架访问的配置表。
        /// </summary>
        /// <param name="config">用于注册配置表的框架配置入口。</param>
        /// <param name="cancellationToken">用于取消本次加载的令牌。</param>
        /// <returns>表示异步加载过程的任务。</returns>
        UniTask LoadAsync(AlloyConfig config, CancellationToken cancellationToken);

        /// <summary>
        /// 释放业务配置加载过程中持有的资源。
        /// </summary>
        void Unload();
    }
}
