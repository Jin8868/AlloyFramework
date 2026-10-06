using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    /// <summary>为持久化 UI 相机连接当前业务场景的渲染相机。</summary>
    public interface IUIBlurCameraBinding : IDisposable
    {
        /// <summary>场景切换后同步相机栈，也可在首次打开模糊界面前立即调用。</summary>
        void Synchronize();
    }

    /// <summary>可替换的渲染管线模糊后端，管理相机分离和 GPU 纹理。</summary>
    public interface IUIBlurRenderer : IDisposable
    {
        /// <summary>更新配置并在需要时请求重新捕获。</summary>
        /// <param name="settings">已经验证的配置副本。</param>
        void Configure(UIBlurSettings settings);

        /// <summary>请求重新捕获，不分配新的相机。</summary>
        void Refresh();

        /// <summary>等待有效纹理，使用不受时间缩放影响的超时。</summary>
        /// <param name="cancellationToken">本次打开的取消令牌。</param>
        /// <returns>首张可用模糊纹理的等待任务。</returns>
        UniTask WaitForCaptureAsync(CancellationToken cancellationToken);

        /// <summary>在渲染前同步相机配置并检查相机栈。</summary>
        void Synchronize();
    }

    /// <summary>在框架启动前安装渲染管线后端，不让 UI 核心依赖 URP 类型。</summary>
    public static class UIBlurRendererProvider
    {
        private static Func<Camera, RawImage, UIBlurSettings, IUIBlurRenderer> m_factory; // 渲染后端创建方法。
        private static Func<Camera, IUIBlurCameraBinding> m_cameraBinder; // 持久化 UI 相机的场景绑定方法。

        /// <summary>安装后端创建方法，应在 UI 框架启动前执行。</summary>
        /// <param name="factory">接收背景相机、输出 Image 和配置的创建方法。</param>
        /// <param name="cameraBinder">可选的场景相机绑定创建方法。</param>
        /// <exception cref="ArgumentNullException">创建方法为空。</exception>
        public static void Install(
            Func<Camera, RawImage, UIBlurSettings, IUIBlurRenderer> factory,
            Func<Camera, IUIBlurCameraBinding> cameraBinder = null)
        {
            m_factory = factory ?? throw new ArgumentNullException(nameof(factory));
            m_cameraBinder = cameraBinder;
        }

        internal static IUIBlurCameraBinding BindCamera(Camera camera) =>
            camera == null ? null : m_cameraBinder?.Invoke(camera);

        internal static IUIBlurRenderer Create(Camera camera, RawImage image, UIBlurSettings settings)
        {
            if (m_factory == null)
            {
                throw new InvalidOperationException("未安装 UI 模糊渲染后端，请接入 Alloy.UIBlur.URP 程序集。");
            }

            return m_factory(camera, image, settings);
        }
    }
}
