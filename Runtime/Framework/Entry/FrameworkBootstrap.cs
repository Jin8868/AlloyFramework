using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    /// <summary>框架唯一场景入口，固定准备日志、展示、启动管线及退出清理。</summary>
    [DisallowMultipleComponent]
    public sealed class FrameworkBootstrap : MonoBehaviour
    {
        [SerializeField]
        private FrameworkBootSettings m_settings; // 主包配置资产，业务仅提供参数和契约实现。
        [SerializeField]
        private GameObject m_startupScreen; // Loading 就绪后销毁的场景启动画面。
        private FrameworkStartupController m_startupController; // 框架入口拥有的单轮控制器。
        private GameObject m_logRoot; // 框架创建的主包日志实例。
        private GameObject m_loadingRoot; // 框架创建的主包展示实例。
        private bool m_isQuitting; // 进程退出时采用同步兜底清理。

        /// <summary>本入口使用的主包配置。</summary>
        public FrameworkBootSettings Settings => m_settings;

        /// <summary>本轮控制器，可查询状态和订阅通知，不能重新编排启动步骤。</summary>
        public FrameworkStartupController StartupController => m_startupController;

        private void Awake()
        {
            // 已存在的持久化入口继续拥有生命周期，重复场景不能创建第二轮框架。
            var bootstraps = FindObjectsOfType<FrameworkBootstrap>(true);
            foreach (var bootstrap in bootstraps)
            {
                if (bootstrap != this && bootstrap.m_startupController != null)
                {
                    if (m_startupScreen != null) Destroy(m_startupScreen);
                    Destroy(gameObject);
                    return;
                }
            }

            if (m_settings == null)
            {
                throw new InvalidOperationException("Boot 场景未指定框架启动配置。");
            }

            // 持久化入口先取得生命周期所有权，所有准备工作均由框架按固定顺序执行。
            transform.SetParent(null);
            DontDestroyOnLoad(gameObject);
            m_startupController = new FrameworkStartupController();
            try
            {
                InstallLogViewer();
                m_settings.Validate();
                InstallLoadingPresenter();
                m_startupController.Startup(m_settings, m_startupScreen);
            }
            catch (Exception exception)
            {
                // 准备失败保留日志供排查，不继续进入资源或业务启动步骤。
                Debug.LogException(exception);
                m_startupController.Dispose();
                if (m_loadingRoot != null) Destroy(m_loadingRoot);
            }
        }

        private void InstallLogViewer()
        {
            if (m_settings.LogViewerPrefab == null)
            {
                throw new InvalidOperationException("启动配置未指定日志预制体。");
            }

            m_logRoot = Instantiate(m_settings.LogViewerPrefab);
            DontDestroyOnLoad(m_logRoot);
            m_logRoot.SetActive(true);
            if (m_logRoot.GetComponent<IStartupLogViewer>() == null)
            {
                throw new InvalidOperationException("日志预制体根节点必须实现 IStartupLogViewer。");
            }
        }

        private void InstallLoadingPresenter()
        {
            // 展示行为固定使用框架组件，游戏仅配置预制体的布局和美术资源。
            m_loadingRoot = Instantiate(m_settings.LoadingPrefab);
            DontDestroyOnLoad(m_loadingRoot);
            var presenter = m_loadingRoot.GetComponent<FrameworkStartupLoadingPresenter>();
            presenter.SetLogViewer(m_logRoot.GetComponent<IStartupLogViewer>());
            m_loadingRoot.SetActive(true);
        }

        private void OnApplicationQuit()
        {
            m_isQuitting = true;
            m_startupController?.Dispose();
        }

        private void OnDestroy()
        {
            if (m_startupController == null || m_isQuitting) return;

            // 入口被主动删除时，先等待业务退出，再释放框架创建的展示资源。
            ShutdownAsync().Forget(Debug.LogException);
        }

        private async UniTask ShutdownAsync()
        {
            try
            {
                await m_startupController.ShutdownAsync();
            }
            finally
            {
                try
                {
                    m_startupController.Dispose();
                }
                finally
                {
                    if (m_loadingRoot != null) Destroy(m_loadingRoot);
                    if (m_logRoot != null) Destroy(m_logRoot);
                }
            }
        }
    }
}
