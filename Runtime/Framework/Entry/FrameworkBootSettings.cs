using System;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>主包中业务代码的执行方式。</summary>
    public enum EGameCodeMode
    {
        Default,
        HybridCLR
    }

    /// <summary>Player 的资源来源。</summary>
    public enum EGameResourceMode
    {
        Offline,
        HotUpdate
    }

    /// <summary>
    /// 主包内嵌的热更新入口配置，由 AOT 启动和编辑器构建共同读取。
    /// </summary>
    public abstract class FrameworkBootSettings : ScriptableObject, IGameStartupConfigurationProvider
    {

        [Header("Boot 运行模式")]
        [SerializeField]
        private EGameCodeMode m_codeMode = EGameCodeMode.HybridCLR; // 默认从代码资源包加载业务 DLL。
        [SerializeField]
        private EGameResourceMode m_resourceMode; // 默认保留无需服务器的离线启动。
        [SerializeField]
        private bool m_editorHostMode; // 编辑器是否测试远端资源，业务代码仍复用编辑器程序集。
        [Header("远端发布")]
        [SerializeField]
        private string m_serverURL = "http://127.0.0.1:8080"; // 本地测试服务器，可改为真机可访问的地址。
        [SerializeField]
        private string m_channel = "local"; // 按渠道隔离发布指针。
        [SerializeField, Min(1)]
        private int m_requestTimeoutSeconds = 30; // 发布描述请求的超时秒数。
        [SerializeField, Min(0)]
        private int m_requestRetryCount = 2; // 发布描述请求失败后的重试次数。

        [Header("启动展示")]
        [SerializeField]
        private GameObject m_logViewerPrefab; // 框架安装的日志资源，根节点实现日志适配契约。
        [SerializeField]
        private GameObject m_loadingPrefab; // 带框架 Loading 组件的主包展示资源。

        /// <summary>构建工具使用的固定 Resources 配置地址。</summary>
        public abstract string BuildConfigurationResourcePath { get; }

        /// <summary>主包日志预制体。</summary>
        public GameObject LogViewerPrefab => m_logViewerPrefab;

        /// <summary>主包启动展示预制体。</summary>
        public GameObject LoadingPrefab => m_loadingPrefab;

        /// <summary>固定的业务程序集名称，不含 DLL 扩展名。</summary>
        public abstract string RuntimeAssemblyName { get; }
        /// <summary>固定的业务安装入口完整类型名称。</summary>
        public abstract string InstallerTypeName { get; }
        public EGameCodeMode CodeMode => m_codeMode;
        public EGameResourceMode ResourceMode => m_resourceMode;
        public bool EditorHostMode => m_editorHostMode;
        public string ServerURL => m_serverURL.TrimEnd('/');
        public string Channel => m_channel;
        public int RequestTimeoutSeconds => m_requestTimeoutSeconds;
        public int RequestRetryCount => m_requestRetryCount;

        /// <summary>校验当前配置资产，供场景启动与编辑器构建复用。</summary>
        /// <exception cref="InvalidOperationException">展示资源、服务器参数或运行模式不合法。</exception>
        public void Validate()
        {
            // 框架必须能够安装固定展示组件，业务不能通过资源替换启动编排。
            if (m_logViewerPrefab == null || m_logViewerPrefab.GetComponent<IStartupLogViewer>() == null)
            {
                throw new InvalidOperationException("日志预制体根节点必须实现 IStartupLogViewer。");
            }
            if (m_loadingPrefab == null ||
                m_loadingPrefab.GetComponent<FrameworkStartupLoadingPresenter>() == null)
            {
                throw new InvalidOperationException("Loading 预制体根节点必须挂载框架启动展示组件。");
            }

            // 发布路径只允许单段标识，防止配置逃逸出当前渠道目录。
            if (!IsPathSegment(m_channel) || m_requestTimeoutSeconds < 1 ||
                m_requestRetryCount < 0 ||
                m_serverURL != m_serverURL?.Trim() ||
                !Uri.TryCreate(m_serverURL, UriKind.Absolute, out Uri serverURI) ||
                (serverURI.Scheme != Uri.UriSchemeHttp && serverURI.Scheme != Uri.UriSchemeHttps) ||
                !string.IsNullOrEmpty(serverURI.Query) || !string.IsNullOrEmpty(serverURI.Fragment))
            {
                throw new InvalidOperationException("Boot 服务器地址、渠道或请求参数不合法。");
            }
            if (!Enum.IsDefined(typeof(EGameCodeMode), m_codeMode) ||
                !Enum.IsDefined(typeof(EGameResourceMode), m_resourceMode))
            {
                throw new InvalidOperationException("Boot 代码模式或资源模式不合法。");
            }
        }

        /// <summary>提供业务入口和代码准备实现，框架决定调用时机及后续步骤顺序。</summary>
        /// <returns>本轮业务接入配置。</returns>
        public abstract GameStartupConfiguration CreateStartupConfiguration();

        /// <summary>检查标识是否可以安全用作发布路径中的单段目录。</summary>
        /// <param name="value">待检查的标识。</param>
        /// <returns>标识非空且只包含允许字符时返回 true。</returns>
        public static bool IsPathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value == "." || value == "..")
            {
                return false;
            }
            foreach (char character in value)
            {
                if (!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.')
                {
                    return false;
                }
            }
            return true;
        }
    }
}
