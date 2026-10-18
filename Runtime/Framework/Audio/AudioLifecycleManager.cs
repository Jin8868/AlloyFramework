using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Audio
{
    /// <summary>统一负责音频引擎初始化、更新接入以及正常和强制退出清理。</summary>
    internal sealed class AudioLifecycleManager : FrameworkSystem
    {
        private bool m_callbacksRegistered; // 当前实例是否持有音频更新和低内存订阅。

        /// <summary>接入音频更新，并等待项目选择的后端和音频引擎完成初始化。</summary>
        /// <param name="context">当前框架服务容器。</param>
        /// <param name="cancellationToken">用于取消音频初始化的令牌。</param>
        /// <returns>音频引擎就绪的任务。</returns>
        public override async UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                // 保持音频更新可用，安装器只决定后端和内容来源，不管理框架生命周期。
                GameLoop.Register(AudioManager.Instance);
                m_callbacksRegistered = true;
                UnityEngine.Application.lowMemory += AudioManager.Instance.HandleLowMemory;
                await AudioManager.Instance.ConfigureAndInitializeAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                // 当前系统尚未进入成功记录，必须自行回收部分初始化资源并保留原始异常。
                try
                {
                    await ShutdownAsync();
                }
                catch (Exception cleanupException)
                {
                    AlloyDebug.Error(cleanupException);
                }

                throw;
            }
        }

        /// <summary>等待音频任务和内容租约退出，再解除框架回调接入。</summary>
        /// <returns>音频引擎和持有资源清理完成的任务。</returns>
        public override async UniTask ShutdownAsync()
        {
            try
            {
                // 资源系统仍然可用，等待被取消的交付真正结束后再继续逆序关闭。
                await AudioManager.Instance.ShutdownAsync();
            }
            finally
            {
                // 正常退出和失败兜底共享幂等的最终清理，避免残留原生引擎或回调。
                Shutdown();
            }
        }

        /// <summary>在进程强制退出或异步关闭失败时立即终止音频并解除订阅。</summary>
        public override void Shutdown()
        {
            try
            {
                AudioManager.Instance.ShutdownImmediately();
            }
            finally
            {
                UnregisterCallbacks();
            }
        }

        private void UnregisterCallbacks()
        {
            if (!m_callbacksRegistered)
            {
                return;
            }

            m_callbacksRegistered = false;
            GameLoop.Unregister(AudioManager.Instance);
            UnityEngine.Application.lowMemory -= AudioManager.Instance.HandleLowMemory;
        }
    }
}
