using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Audio
{
    [FrameworkSystem(FrameworkSystemPriority.Audio)]
    internal sealed class AudioSystem : FrameworkSystem
    {
        /// <summary>安装音频主线程管理，具体引擎由业务启动步骤安装。</summary>
        /// <param name="context">框架上下文。</param>
        /// <param name="cancellationToken">启动取消令牌。</param>
        /// <returns>注册完成任务。</returns>
        public override UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameLoop.Register(AudioManager.Instance);
            UnityEngine.Application.lowMemory += AudioManager.Instance.HandleLowMemory;
            return UniTask.CompletedTask;
        }

        /// <summary>在资源系统退出前关闭音频引擎及内容租约。</summary>
        public override void Shutdown()
        {
            GameLoop.Unregister(AudioManager.Instance);
            UnityEngine.Application.lowMemory -= AudioManager.Instance.HandleLowMemory;
            AudioManager.Instance.ShutdownImmediately();
        }
    }
}
