using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.U2D;

namespace AlloyFramework.UI
{
    /// <summary>
    /// 将图集中的图片设置到 Image，并随组件生命周期归还图集引用。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Image))]
    public sealed class UIAtlasImage : MonoBehaviour
    {
        [SerializeField] private Image m_image; // 显示图集图片的目标组件，未指定时使用同节点 Image。
        private IAssetHandle<SpriteAtlas> m_atlasHandle; // 当前显示图片持有的独立图集租约。
        private Sprite m_sprite; // GetSprite 创建的副本，由当前组件负责销毁。
        private CancellationTokenSource m_pendingRequest; // 最新设置请求的取消源。
        private bool m_destroyed; // 防止销毁后继续提交加载结果。

        /// <summary>
        /// 当前使用的目标 Image。
        /// </summary>
        public Image TargetImage => ResolveImage();

        /// <summary>
        /// 加载图集图片并设置到目标 Image，通过回调报告完成或异常。
        /// </summary>
        /// <param name="atlasLocation">图集在资源包中的地址。</param>
        /// <param name="spriteName">图集内的图片名称。</param>
        /// <param name="onCompleted">完成回调，成功时异常为空；请求被替换或取消时收到取消异常。</param>
        /// <param name="packageName">图集所属资源包。</param>
        /// <param name="cancellationToken">用于取消本次设置的令牌。</param>
        public void SetImage(
            string atlasLocation,
            string spriteName,
            Action<Exception> onCompleted = null,
            string packageName = ResourceSettings.DefaultPackageName,
            CancellationToken cancellationToken = default)
        {
            UICallbacks.Run(
                SetImageAsync(atlasLocation, spriteName, packageName, cancellationToken),
                onCompleted);
        }

        /// <summary>
        /// 加载并设置图集图片，仅允许最新请求提交；加载失败或取消时保留原图片。
        /// </summary>
        /// <param name="atlasLocation">图集在资源包中的地址。</param>
        /// <param name="spriteName">图集内的图片名称。</param>
        /// <param name="packageName">图集所属资源包。</param>
        /// <param name="cancellationToken">用于取消本次设置的令牌。</param>
        /// <returns>图片设置完成的任务。</returns>
        /// <exception cref="ArgumentException">图集地址、图片名称或资源包名称为空。</exception>
        /// <exception cref="ObjectDisposedException">组件已销毁。</exception>
        /// <exception cref="InvalidOperationException">目标 Image 不存在、图片不存在或资源加载失败。</exception>
        /// <exception cref="OperationCanceledException">请求被取消、被新请求替换或组件在加载期间销毁。</exception>
        public async UniTask SetImageAsync(
            string atlasLocation,
            string spriteName,
            string packageName = ResourceSettings.DefaultPackageName,
            CancellationToken cancellationToken = default)
        {
            // 无效调用不能打断正在进行的有效设置。
            ThrowIfDestroyed();
            ValidateName(atlasLocation, nameof(atlasLocation));
            ValidateName(spriteName, nameof(spriteName));
            ValidateName(packageName, nameof(packageName));
            cancellationToken.ThrowIfCancellationRequested();
            if (ResolveImage() == null)
            {
                throw new InvalidOperationException("图集图片组件没有可用的目标 Image。");
            }

            // 请求拥有自己的取消源与临时租约，旧请求退出时不能清除新请求的状态。
            var request = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken, this.GetCancellationTokenOnDestroy());
            var previousRequest = m_pendingRequest;
            m_pendingRequest = request;
            IAssetHandle<SpriteAtlas> atlasHandle = null;
            Sprite sprite = null;
            try
            {
                CancelRequest(previousRequest);
                request.Token.ThrowIfCancellationRequested();
                atlasHandle = await ResourceManager.Instance.LoadAssetAsync<SpriteAtlas>(
                    atlasLocation, packageName, cancellationToken: request.Token);
                request.Token.ThrowIfCancellationRequested();
                if (m_destroyed || this == null || !ReferenceEquals(m_pendingRequest, request))
                {
                    throw new OperationCanceledException(request.Token);
                }

                // GetSprite 返回独立副本；查找失败时由 finally 归还本次图集引用。
                sprite = atlasHandle.Asset.GetSprite(spriteName);
                if (sprite == null)
                {
                    throw new InvalidOperationException(
                        $"图集 {packageName}/{atlasLocation} 中不存在图片 {spriteName}。");
                }
                var image = ResolveImage();
                if (image == null)
                {
                    throw new InvalidOperationException("加载期间目标 Image 已被销毁。");
                }

                // 新图片就绪后再归还旧引用，避免切换同一图集时引用短暂归零。
                image.sprite = sprite;
                ReleaseCurrent();
                m_atlasHandle = atlasHandle;
                m_sprite = sprite;
                atlasHandle = null;
                sprite = null;
            }
            finally
            {
                // 未提交的结果始终由当前请求回收，包括取消、销毁和图片查找失败。
                if (ReferenceEquals(m_pendingRequest, request))
                {
                    m_pendingRequest = null;
                }
                request.Dispose();
                if (sprite != null)
                {
                    Destroy(sprite);
                }
                atlasHandle?.Dispose();
            }
        }

        /// <summary>
        /// 取消待完成的设置，清空当前图片并立即归还当前图集引用，可用于列表项复用。
        /// </summary>
        /// <exception cref="ObjectDisposedException">组件已销毁。</exception>
        public void Clear()
        {
            ThrowIfDestroyed();
            CancelPendingRequest();
            ReleaseCurrent();
        }

        private void Reset()
        {
            m_image = GetComponent<Image>();
        }

        private void OnDestroy()
        {
            // 禁止后续提交，并在组件移除或所属对象销毁时归还已经获得的引用。
            m_destroyed = true;
            CancelPendingRequest();
            ReleaseCurrent();
        }

        private Image ResolveImage()
        {
            if (!m_destroyed && this != null && m_image == null)
            {
                m_image = GetComponent<Image>();
            }
            return m_image;
        }

        private void CancelPendingRequest()
        {
            var request = m_pendingRequest;
            m_pendingRequest = null;
            CancelRequest(request);
        }

        private static void CancelRequest(CancellationTokenSource request)
        {
            // 取消回调抛出异常也不能阻止图集引用回收；取消源由所属异步请求最终释放。
            try
            {
                request?.Cancel();
            }
            catch (Exception exception)
            {
                AlloyDebug.Error(exception);
            }
        }

        private void ReleaseCurrent()
        {
            var sprite = m_sprite;
            var atlasHandle = m_atlasHandle;
            m_sprite = null;
            m_atlasHandle = null;

            // 只清除本组件设置的图片，避免覆盖外部后来赋予 Image 的其他图片。
            if (m_image != null && sprite != null && m_image.sprite == sprite)
            {
                m_image.sprite = null;
            }
            if (sprite != null)
            {
                Destroy(sprite);
            }
            atlasHandle?.Dispose();
        }

        private void ThrowIfDestroyed()
        {
            if (m_destroyed || this == null)
            {
                throw new ObjectDisposedException(nameof(UIAtlasImage));
            }
        }

        private static void ValidateName(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("图集地址、图片名称和资源包名称不能为空。", parameterName);
            }
        }
    }
}
