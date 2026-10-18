using UnityEngine.Scripting;
using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 从代码准备阶段的结果创建唯一业务入口，不重复加载程序集。
    /// </summary>
    public sealed class HybridCLRGameEntryLoader : IGameEntryLoader
    {
        private readonly GameCodePreparationResult m_result; // 早期代码准备阶段提交的结果。

        /// <summary>
        /// 绑定代码准备阶段共享的结果。
        /// </summary>
        /// <param name="result">代码准备结果。</param>
        /// <exception cref="ArgumentNullException">结果为空。</exception>
        [Preserve]
        public HybridCLRGameEntryLoader(GameCodePreparationResult result)
        {
            m_result = result ?? throw new ArgumentNullException(nameof(result));
        }

        /// <summary>
        /// 在框架系统初始化后创建已加载程序集中的业务入口。
        /// </summary>
        /// <param name="context">框架上下文。</param>
        /// <param name="cancellationToken">取消启动的令牌。</param>
        /// <returns>唯一业务入口。</returns>
        /// <exception cref="InvalidOperationException">代码准备未成功完成。</exception>
        public UniTask<IGameEntry> LoadAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!m_result.IsCompleted || m_result.RuntimeAssembly == null)
            {
                throw new InvalidOperationException("业务代码尚未准备完成，不能创建业务入口。");
            }

            // 只消费成功提交的结果，避免在配置初始化后才开始加载代码。
            return UniTask.FromResult(GameEntryActivator.Create(m_result.RuntimeAssembly));
        }
    }
}
