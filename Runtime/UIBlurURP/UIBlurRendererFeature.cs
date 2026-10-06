using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace AlloyFramework.UI
{
    /// <summary>在背景 UI 渲染后捕获颜色并生成模糊纹理，前景相机随后显示该纹理。</summary>
    public sealed class UIBlurRendererFeature : ScriptableRendererFeature
    {
        private static readonly Dictionary<Camera, URPUIBlurRenderer> m_sessions =
            new Dictionary<Camera, URPUIBlurRenderer>(); // 每个源相机的活动会话。
        private static readonly List<UIBlurRendererFeature> m_features =
            new List<UIBlurRendererFeature>(); // 用于会话结束时及时释放纹理的 Feature。
        [SerializeField] private Shader m_blurShader; // 必须在 Renderer 资产中显式引用，防止构建裁剪。
        private readonly Dictionary<Camera, BlurPass> m_passes = new Dictionary<Camera, BlurPass>(); // 相机独立纹理。

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSessions()
        {
            // 兼容关闭 Domain Reload 的编辑器启动，避免沿用上次运行的 GPU 资源。
            m_sessions.Clear();
            foreach (var feature in m_features)
            {
                if (feature != null)
                {
                    feature.ReleasePasses();
                }
            }
        }

        /// <summary>初始化 Feature；材质和纹理只在有模糊界面时创建。</summary>
        public override void Create()
        {
            ReleasePasses();
            if (!m_features.Contains(this))
            {
                m_features.Add(this);
            }
        }

        /// <summary>按源相机请求插入捕获，不处理前景、Scene View 或没有请求的相机。</summary>
        /// <param name="renderer">当前相机的 Renderer。</param>
        /// <param name="renderingData">当前相机渲染信息。</param>
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            var camera = renderingData.cameraData.camera;
            if (!m_sessions.TryGetValue(camera, out var session) || !session.CanCapture)
            {
                return;
            }

            if (m_blurShader == null || !m_blurShader.isSupported)
            {
                session.Failed(new InvalidOperationException("UIBlurRendererFeature 缺少受支持的模糊 Shader 引用。"));
                return;
            }

            if (!m_passes.TryGetValue(camera, out var pass))
            {
                pass = new BlurPass(m_blurShader);
                m_passes.Add(camera, pass);
            }

            pass.Session = session;
            // 静态模式也检查尺寸变化，避免旋转或 Render Scale 改动沿用旧纹理。
            if (session.NeedsCapture || pass.NeedsResize(renderingData.cameraData.cameraTargetDescriptor))
            {
                renderer.EnqueuePass(pass);
            }
        }

        /// <summary>在 Renderer 分配目标之后取得颜色缓冲。</summary>
        /// <param name="renderer">当前相机的 Renderer。</param>
        /// <param name="renderingData">当前相机渲染信息。</param>
        public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
        {
            if (m_passes.TryGetValue(renderingData.cameraData.camera, out var pass))
            {
                pass.Source = renderer.cameraColorTargetHandle;
            }
        }

        /// <summary>释放所有材质和纹理，不影响其他 Renderer Feature。</summary>
        /// <param name="disposing">是否执行完整释放。</param>
        protected override void Dispose(bool disposing)
        {
            ReleasePasses();
            m_features.Remove(this);
        }

        internal static void Register(URPUIBlurRenderer session)
        {
            m_sessions.Add(session.Source, session);
        }

        internal static void Unregister(URPUIBlurRenderer session)
        {
            m_sessions.Remove(session.Source);
            foreach (var feature in m_features)
            {
                if (feature != null && feature.m_passes.TryGetValue(session.Source, out var pass))
                {
                    pass.Dispose();
                    feature.m_passes.Remove(session.Source);
                }
            }
        }

        private void ReleasePasses()
        {
            foreach (var pass in m_passes.Values)
            {
                pass.Dispose();
            }

            m_passes.Clear();
        }

        private sealed class BlurPass : ScriptableRenderPass, IDisposable
        {
            private static readonly int m_offsetID = Shader.PropertyToID("_BlurOffset"); // 模糊采样偏移属性。
            private readonly Material m_material; // 本相机独享的模糊材质。
            private readonly ProfilingSampler m_sampler = new ProfilingSampler("Alloy UI Blur"); // GPU 分析标记。
            private RTHandle m_first; // 第一张降采样工作纹理，也是最终输出。
            private RTHandle m_second; // 第二张降采样工作纹理。
            private int m_width; // 当前源纹理宽度。
            private int m_height; // 当前源纹理高度。
            private int m_downsample; // 当前纹理降采样倍数。

            internal URPUIBlurRenderer Session { get; set; }
            internal RTHandle Source { get; set; }

            internal BlurPass(Shader shader)
            {
                m_material = CoreUtils.CreateEngineMaterial(shader);
                renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
                // 让 URP 分配可采样颜色目标，而不是读取无法采样的最终 Backbuffer。
                ConfigureInput(ScriptableRenderPassInput.Color);
            }

            internal bool NeedsResize(RenderTextureDescriptor descriptor)
            {
                return m_first == null || m_width != descriptor.width || m_height != descriptor.height ||
                    m_downsample != Session.Settings.Downsample ||
                    m_first.rt.graphicsFormat != descriptor.graphicsFormat;
            }

            /// <summary>按相机实际分辨率分配两张无深度、无 MSAA 的低分辨率纹理。</summary>
            /// <param name="commandBuffer">当前命令缓冲。</param>
            /// <param name="renderingData">当前渲染信息。</param>
            public override void OnCameraSetup(CommandBuffer commandBuffer, ref RenderingData renderingData)
            {
                var descriptor = renderingData.cameraData.cameraTargetDescriptor;
                m_width = descriptor.width;
                m_height = descriptor.height;
                m_downsample = Session.Settings.Downsample;
                descriptor.width = Mathf.Max(1, descriptor.width / m_downsample);
                descriptor.height = Mathf.Max(1, descriptor.height / m_downsample);
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.bindMS = false;
                descriptor.useMipMap = false;
                descriptor.autoGenerateMips = false;
                RenderingUtils.ReAllocateIfNeeded(ref m_first, descriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: "AlloyUIBlurFirst");
                RenderingUtils.ReAllocateIfNeeded(ref m_second, descriptor,
                    FilterMode.Bilinear, TextureWrapMode.Clamp, name: "AlloyUIBlurSecond");
            }

            /// <summary>降采样并执行可分离高斯模糊，不读回 CPU，也不重新渲染场景。</summary>
            /// <param name="context">渲染上下文。</param>
            /// <param name="renderingData">当前渲染信息。</param>
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                var commandBuffer = CommandBufferPool.Get("Alloy UI Blur");
                try
                {
                    using (new ProfilingScope(commandBuffer, m_sampler))
                    {
                        Blitter.BlitCameraTexture(commandBuffer, Source, m_first);
                        var radius = Session.Settings.Radius;
                        for (var index = 0; index < Session.Settings.Iterations; index++)
                        {
                            commandBuffer.SetGlobalVector(m_offsetID,
                                new Vector4(radius / m_first.rt.width, 0f, 0f, 0f));
                            Blitter.BlitCameraTexture(commandBuffer, m_first, m_second, m_material, 0);
                            commandBuffer.SetGlobalVector(m_offsetID,
                                new Vector4(0f, radius / m_first.rt.height, 0f, 0f));
                            Blitter.BlitCameraTexture(commandBuffer, m_second, m_first, m_material, 0);
                        }
                    }

                    context.ExecuteCommandBuffer(commandBuffer);
                    Session.Captured(m_first.rt);
                }
                catch (Exception exception)
                {
                    Session.Failed(exception);
                }
                finally
                {
                    CommandBufferPool.Release(commandBuffer);
                }
            }

            /// <summary>释放 GPU 工作纹理和材质。</summary>
            public void Dispose()
            {
                m_first?.Release();
                m_second?.Release();
                m_first = null;
                m_second = null;
                CoreUtils.Destroy(m_material);
            }
        }
    }
}
