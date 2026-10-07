using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    internal sealed class URPUIBlurRenderer : IUIBlurRenderer
    {
        private readonly Camera m_source; // 捕获场景和背景 UI 的源相机。
        private readonly RawImage m_image; // 前景相机中显示的全屏模糊纹理。
        private readonly int m_originalMask; // 源相机原剔除掩码。
        private readonly Camera m_foreground; // 只渲染当前边界及以上 UI 的 Overlay 相机。
        private readonly UniversalAdditionalCameraData m_foregroundData; // 前景相机的 URP 设置。
        private List<Camera> m_stack; // 当前所属 Base 相机的相机栈。
        private Camera m_host; // 负责渲染场景的 Base 相机。
        private int m_hostOriginalMask; // Base 相机原剔除掩码。
        private Camera[] m_cameraBuffer = new Camera[8]; // 无分配的相机枚举缓冲。
        private UIBlurSettings m_settings; // 当前配置。
        private bool m_needsCapture = true; // 静态模式是否需要重新捕获。
        private bool m_hasCapture; // 是否已有有效纹理。
        private bool m_disposed; // 是否已经释放会话。
        private bool m_canCapture = true; // 场景相机切换空档暂停捕获，保留上一张纹理。
        private Exception m_error; // 渲染端报告的错误。

        internal Camera Source => m_source;
        internal UIBlurSettings Settings => m_settings;
        internal bool CanCapture => !m_disposed && m_canCapture;
        internal bool NeedsCapture => CanCapture &&
            (m_settings.UpdateMode == EUIBlurUpdateMode.Realtime || m_needsCapture);

        internal URPUIBlurRenderer(Camera source, RawImage image, UIBlurSettings settings)
        {
            m_source = source;
            m_image = image;
            m_settings = settings;
            m_originalMask = source.cullingMask;
            var sourceData = source.GetUniversalAdditionalCameraData();
            if (sourceData.renderType != CameraRenderType.Overlay)
            {
                throw new InvalidOperationException(
                    "包含场景和下层 UI 的模糊要求 UICamera 为 Overlay，并加入场景 Base 相机的 Stack。");
            }

            if (sourceData.scriptableRenderer == null ||
                !sourceData.scriptableRenderer.SupportsCameraStackingType(CameraRenderType.Overlay))
            {
                throw new InvalidOperationException("UI 模糊需要支持 Camera Stacking 的 URP Universal Renderer。");
            }

            // 复用当前界面独立根画布绑定的前景相机，不在每次打开时创建或销毁相机。
            m_foreground = image.canvas.rootCanvas.worldCamera;
            if (m_foreground == null || m_foreground == source)
            {
                throw new InvalidOperationException("模糊界面的根 Canvas 必须绑定独立的 BlurForegroundCamera。");
            }

            m_foreground.enabled = false;
            m_foregroundData = m_foreground.GetUniversalAdditionalCameraData();
            if (!m_foregroundData.clearDepth)
            {
                throw new InvalidOperationException("BlurForegroundCamera 的 Clear Depth 必须开启。");
            }

            m_foregroundData.renderType = CameraRenderType.Overlay;
            // URP 14 的 clearDepth 为只读，必须在预制体 Inspector 中启用。
            m_foregroundData.renderPostProcessing = false;
            m_foregroundData.renderShadows = false;
            m_foregroundData.requiresColorOption = CameraOverrideOption.Off;
            m_foregroundData.requiresDepthOption = CameraOverrideOption.Off;
            try
            {
                Synchronize();
                UIBlurRendererFeature.Register(this);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
        private static void Install()
        {
            UIBlurRendererProvider.Install(Create, CreateCameraBinding);
        }

        private static IUIBlurCameraBinding CreateCameraBinding(Camera camera) => new URPUICameraStackBinding(camera);

        private static IUIBlurRenderer Create(Camera camera, RawImage image, UIBlurSettings settings)
        {
            return new URPUIBlurRenderer(camera, image, settings);
        }

        /// <summary>更新模糊质量和模式，并要求生成新纹理。</summary>
        /// <param name="settings">框架已验证的配置。</param>
        public void Configure(UIBlurSettings settings)
        {
            m_settings = settings;
            Refresh();
        }

        /// <summary>请求在下一个源相机渲染帧捕获背景。</summary>
        public void Refresh()
        {
            m_needsCapture = true;
            m_hasCapture = false;
        }

        /// <summary>等待首张纹理，缺少 Renderer Feature 时以实际时间超时。</summary>
        /// <param name="cancellationToken">打开界面的取消令牌。</param>
        /// <returns>捕获完成的任务。</returns>
        public async UniTask WaitForCaptureAsync(CancellationToken cancellationToken)
        {
            var startedAt = Time.realtimeSinceStartupAsDouble;
            while (!m_hasCapture)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(URPUIBlurRenderer));
                }

                if (m_error != null)
                {
                    throw new InvalidOperationException("URP UI 模糊捕获失败。", m_error);
                }

                if (Time.realtimeSinceStartupAsDouble - startedAt >= m_settings.CaptureTimeoutSeconds)
                {
                    throw new TimeoutException(
                        "UI 模糊捕获超时：请检查源 UICamera 是否启用、相机栈和 Renderer 上的 UIBlurRendererFeature。");
                }

                await UniTask.NextFrame(cancellationToken);
            }
        }

        /// <summary>同步源相机投影、Renderer 和前景掩码，保持相机栈连续。</summary>
        public void Synchronize()
        {
            if (m_disposed || m_source == null)
            {
                throw new InvalidOperationException("UI 模糊源相机已销毁。");
            }

            if (m_error != null)
            {
                throw new InvalidOperationException("UI 模糊渲染会话已失败。", m_error);
            }

            if (m_source.stereoEnabled || m_source.allowDynamicResolution || m_source.targetTexture != null ||
                m_source.targetDisplay != 0 || m_source.rect != new Rect(0f, 0f, 1f, 1f))
            {
                throw new InvalidOperationException(
                    "当前 UI 模糊后端要求单显示器、全屏、非 XR、无 TargetTexture 且关闭 Dynamic Resolution。");
            }

            var sourceData = m_source.GetUniversalAdditionalCameraData();
            var stack = FindStack(out var host);
            if (stack == null)
            {
                if (m_image.texture != null)
                {
                    // 场景切换空档保留纹理，两台 UI 相机分别显示自己的独立根画布。
                    m_stack?.Remove(m_foreground);
                    m_stack = null;
                    if (m_host != null)
                    {
                        m_host.cullingMask = m_hostOriginalMask;
                    }

                    m_host = null;
                    m_canCapture = false;
                    SynchronizeForeground(CameraRenderType.Base);
                    m_foreground.depth = m_source.depth + 1f;
                    m_foreground.clearFlags = CameraClearFlags.Depth;
                    m_source.cullingMask = (m_originalMask |
                        (1 << LayerMask.NameToLayer(m_settings.BackgroundLayerName))) &
                        ~(1 << LayerMask.NameToLayer(m_settings.ForegroundLayerName));
                    return;
                }

                throw new InvalidOperationException(
                    "Overlay UICamera 必须位于启用的 Base 相机栈中；请将 UICamera 加入场景相机的 Stack。");
            }

            if (!m_canCapture)
            {
                m_canCapture = true;
                Refresh();
            }

            if (!ReferenceEquals(m_stack, stack))
            {
                m_stack?.Remove(m_foreground);
                if (m_host != null)
                {
                    m_host.cullingMask = m_hostOriginalMask;
                }

                m_host = host;
                m_hostOriginalMask = host.cullingMask;
                m_stack = stack;
            }

            // 场景相机不重复渲染 UI；两个专用 Layer 只允许用于框架 UI。
            m_host.cullingMask = m_hostOriginalMask &
                ~(1 << LayerMask.NameToLayer(m_settings.BackgroundLayerName)) &
                ~(1 << LayerMask.NameToLayer(m_settings.ForegroundLayerName));

            // 保持前景紧随背景相机，后面的业务相机仍按项目自己的栈顺序渲染。
            var insertionIndex = m_stack.IndexOf(m_source) + 1;
            if (m_stack.IndexOf(m_foreground) != insertionIndex)
            {
                m_stack.Remove(m_foreground);
                insertionIndex = m_stack.IndexOf(m_source) + 1;
                m_stack.Insert(insertionIndex, m_foreground);
            }

            SynchronizeForeground(CameraRenderType.Overlay);
            // 只剔除专用前景 Layer，其余源相机可见内容保持不变。
            m_source.cullingMask = (m_originalMask | (1 << LayerMask.NameToLayer(m_settings.BackgroundLayerName))) &
                ~(1 << LayerMask.NameToLayer(m_settings.ForegroundLayerName));
        }

        private void SynchronizeForeground(CameraRenderType renderType)
        {
            // 同步投影，同时严格限制前景 Layer，避免重复绘制场景和背景 UI。
            m_foreground.CopyFrom(m_source);
            m_foreground.transform.SetPositionAndRotation(m_source.transform.position, m_source.transform.rotation);
            m_foreground.cullingMask = 1 << LayerMask.NameToLayer(m_settings.ForegroundLayerName);
            m_foreground.clearFlags = CameraClearFlags.Nothing;
            m_foreground.useOcclusionCulling = false;
            m_foregroundData.renderType = renderType;
            m_foregroundData.SetRenderer(m_settings.URPRendererIndex);
            var sourceData = m_source.GetUniversalAdditionalCameraData();
            if (!ReferenceEquals(m_foregroundData.scriptableRenderer, sourceData.scriptableRenderer))
            {
                throw new InvalidOperationException("UIBlurSettings.URPRendererIndex 必须与源 UICamera 的 Renderer 一致。");
            }

            m_foreground.enabled = m_source.enabled;
        }

        private List<Camera> FindStack(out Camera host)
        {
            host = null;
            var count = Camera.allCamerasCount;
            if (m_cameraBuffer.Length < count)
            {
                m_cameraBuffer = new Camera[count];
            }

            count = Camera.GetAllCameras(m_cameraBuffer);
            for (var index = 0; index < count; index++)
            {
                var camera = m_cameraBuffer[index];
                if (!camera.TryGetComponent<UniversalAdditionalCameraData>(out var cameraData) ||
                    cameraData.renderType != CameraRenderType.Base)
                {
                    continue;
                }

                var stack = cameraData.cameraStack;
                if (stack != null && stack.Contains(m_source))
                {
                    host = camera;
                    return stack;
                }
            }

            return null;
        }

        internal void Captured(RenderTexture texture)
        {
            if (m_disposed || m_image == null)
            {
                return;
            }

            m_image.texture = texture;
            m_image.enabled = true;
            m_hasCapture = true;
            m_needsCapture = false;
        }

        internal void Failed(Exception exception)
        {
            if (m_error == null)
            {
                m_error = exception;
                AlloyDebug.Error(exception);
            }
        }

        /// <summary>从相机栈移除前景，恢复源掩码并释放 GPU 会话。</summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            UIBlurRendererFeature.Unregister(this);
            m_stack?.Remove(m_foreground);
            if (m_host != null)
            {
                m_host.cullingMask = m_hostOriginalMask;
            }
            if (m_source != null)
            {
                m_source.cullingMask = m_originalMask;
            }

            if (m_foreground != null)
            {
                m_foreground.enabled = false;
                m_foregroundData.renderType = CameraRenderType.Overlay;
            }
        }
    }
}
