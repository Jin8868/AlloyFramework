using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace AlloyFramework.UI
{
    internal sealed class URPUICameraStackBinding : IUIBlurCameraBinding, ILateUpdateable
    {
        private readonly Camera m_source; // 框架持久化 UI 相机。
        private readonly UniversalAdditionalCameraData m_sourceData; // UI 相机的 URP 配置。
        private readonly CameraRenderType m_originalType; // UI 相机原渲染类型。
        private List<Camera> m_ownedStack; // 由本绑定器加入源相机的栈。
        private bool m_disposed; // 是否已释放绑定器。

        internal URPUICameraStackBinding(Camera source)
        {
            m_source = source;
            m_sourceData = source.GetUniversalAdditionalCameraData();
            m_originalType = m_sourceData.renderType;
            GameLoop.Register(this);
        }

        /// <summary>使用 MainCamera 的 Base 栈显示持久化 UI，没有场景相机时保留独立 UI 渲染。</summary>
        public void Synchronize()
        {
            if (m_disposed || m_source == null)
            {
                return;
            }

            var sceneCamera = Camera.main;
            var sceneData = sceneCamera != null && sceneCamera != m_source
                ? sceneCamera.GetUniversalAdditionalCameraData() : null;
            var nextStack = sceneData != null && sceneData.renderType == CameraRenderType.Base &&
                sceneData.scriptableRenderer != null &&
                sceneData.scriptableRenderer.SupportsCameraStackingType(CameraRenderType.Base)
                ? sceneData.cameraStack : null;

            if (m_ownedStack != null && !ReferenceEquals(m_ownedStack, nextStack))
            {
                m_ownedStack.Remove(m_source);
                m_ownedStack = null;
            }

            if (nextStack == null)
            {
                // Boot 或场景切换的空档仍能显示 Loading；完整背景模糊需要场景 Base 相机。
                m_sourceData.renderType = CameraRenderType.Base;
                return;
            }

            m_sourceData.renderType = CameraRenderType.Overlay;
            if (!nextStack.Contains(m_source))
            {
                nextStack.Add(m_source);
                m_ownedStack = nextStack;
            }
        }

        /// <summary>跟随业务场景切换更新相机栈。</summary>
        /// <param name="deltaTime">受缩放影响的帧间隔。</param>
        /// <param name="unscaledDeltaTime">不受缩放影响的帧间隔。</param>
        public void OnLateUpdate(float deltaTime, float unscaledDeltaTime) => Synchronize();

        /// <summary>移除本绑定器创建的栈引用并恢复源相机类型。</summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            GameLoop.Unregister(this);
            m_ownedStack?.Remove(m_source);
            m_ownedStack = null;
            if (m_sourceData != null)
            {
                m_sourceData.renderType = m_originalType;
            }
        }
    }
}
