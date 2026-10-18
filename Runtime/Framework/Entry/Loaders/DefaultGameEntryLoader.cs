using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Scripting;

namespace AlloyFramework
{
    /// <summary>从代码准备阶段确定的静态业务程序集中创建唯一入口。</summary>
    public sealed class DefaultGameEntryLoader : IGameEntryLoader
    {
        private readonly GameCodePreparationResult m_result; // 与静态业务代码准备步骤共享的结果。

        /// <summary>绑定静态业务代码准备阶段提交的程序集。</summary>
        /// <param name="result">代码准备结果。</param>
        /// <exception cref="ArgumentNullException">结果为空。</exception>
        [Preserve]
        public DefaultGameEntryLoader(GameCodePreparationResult result)
        {
            m_result = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>从已准备完成的静态业务程序集中创建入口。</summary>
        /// <param name="context">当前框架服务容器。</param>
        /// <param name="cancellationToken">取消加载的令牌。</param>
        /// <returns>唯一的游戏入口。</returns>
        /// <exception cref="InvalidOperationException">静态业务代码尚未准备完成。</exception>
        public UniTask<IGameEntry> LoadAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_result.IsCompleted || m_result.RuntimeAssembly == null)
            {
                throw new InvalidOperationException("静态业务代码尚未准备完成，不能创建入口。");
            }

            return UniTask.FromResult(GameEntryActivator.Create(m_result.RuntimeAssembly));
        }
    }
}
