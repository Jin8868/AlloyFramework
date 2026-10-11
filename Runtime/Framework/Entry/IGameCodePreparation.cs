using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 在资源系统绑定后、业务配置初始化前准备业务代码。
    /// </summary>
    public interface IGameCodePreparation
    {
        /// <summary>
        /// 加载业务代码并显式注册业务配置服务。
        /// </summary>
        /// <param name="context">本次框架启动上下文。</param>
        /// <param name="cancellationToken">取消本次启动的令牌。</param>
        /// <returns>代码准备完成的异步操作。</returns>
        UniTask PrepareAsync(FrameworkContext context, CancellationToken cancellationToken);
    }
}
