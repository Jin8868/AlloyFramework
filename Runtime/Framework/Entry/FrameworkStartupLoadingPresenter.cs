using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEventSystem = UnityEngine.EventSystems.EventSystem;

namespace AlloyFramework
{
    /// <summary>随主包内置的统一启动 Loading 与模态错误弹窗。</summary>
    public sealed class FrameworkStartupLoadingPresenter : MonoBehaviour, IStartupLoadingPresenter
    {
        [SerializeField]
        private Slider m_progressSlider; // 从原 Loading 迁移的进度条。
        [SerializeField]
        private Text m_statusText; // 当前步骤及真实下载字节提示。
        [SerializeField]
        private Text m_percentText; // 聚合后的总体进度。
        private Canvas m_canvas; // 跨场景存在的最高层启动画布。
        private GameObject m_errorPanel; // 唯一模态错误面板。
        private Text m_errorText; // 可信错误提示，不展示原始堆栈。
        private Button m_exitButton; // 不可恢复时退出应用。
        private Button m_logButton; // 启动失败时直接打开主包日志，不要求先识别手势。
        private UnityEventSystem m_ownedEventSystem; // 场景未提供输入时的持久化回退。
        private float m_nextInputCheck; // 限制启动期间的输入节点检查频率。
        private bool m_isReady; // 已实际绘制一帧，可以开始联网。
        private bool m_canShowLoading; // Logo 绘制一帧后才显示 Loading。
        private bool m_registered; // 是否成功取得唯一展示对象身份。
        private bool m_releasing; // 防止重复释放和退出回调。
        private bool m_showCanvas; // 当前启动状态是否要求显示画布，不受日志窗口临时遮挡影响。
        private StartupLoadingError m_reportedError; // 已输出的原始异常，防止重复渲染时刷屏。

        private IStartupLogViewer m_logViewer; // 框架安装并注入的日志适配对象。

        public bool IsReady => m_isReady;

        /// <summary>渲染当前轮次的统一 Loading 和错误弹窗。</summary>
        /// <param name="snapshot">最新启动状态。</param>
        /// <param name="error">本轮唯一错误；正常时为空。</param>
        public void Render(StartupLoadingSnapshot snapshot, StartupLoadingError error)
        {
            if (snapshot == null || m_releasing) return;
            m_showCanvas = m_canShowLoading && snapshot.IsVisible;
            m_progressSlider.SetValueWithoutNotify(snapshot.OverallProgress);
            m_statusText.text = snapshot.Message;
            if (snapshot.TotalBytes > 0)
            {
                m_statusText.text += $"\n{snapshot.CurrentBytes / 1048576f:F2} / " +
                    $"{snapshot.TotalBytes / 1048576f:F2} MB";
            }
            m_percentText.text = $"{Mathf.FloorToInt(snapshot.OverallProgress * 100f)}%";
            m_errorPanel.SetActive(error != null);
            if (error != null)
            {
                m_errorText.text = error.Message + "\n\n请重新启动游戏。";
                m_showCanvas = m_canShowLoading;
                // 正式包也保留启动失败的原始异常，不能使用会被编译条件移除的日志入口。
                if (m_reportedError != error)
                {
                    m_reportedError = error;
                    Debug.LogException(error.Exception);
                }
                EnsureInput();
            }
            m_canvas.enabled = m_showCanvas && !(m_logViewer?.IsVisible ?? false);
        }

        /// <summary>释放持久化根节点、按钮监听和自行创建的输入对象。</summary>
        public void Release()
        {
            if (m_releasing) return;
            m_releasing = true;
            m_canvas.enabled = false;
            Destroy(gameObject);
        }

        internal void SetLogViewer(IStartupLogViewer logViewer)
        {
            m_logViewer = logViewer;
        }

        private void ShowLogViewer()
        {
            m_logViewer?.Show();
        }

        private void Awake()
        {
            // 全部布局和依赖来自主包预制体，不调用业务 UIManager 或资源系统。
            DontDestroyOnLoad(gameObject);
            m_canvas = GetComponent<Canvas>();
            m_canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 为独立日志入口预留最高排序，即使启动错误面板覆盖全屏也能打开日志。
            m_canvas.sortingOrder = short.MaxValue - 1;
            gameObject.AddComponent<GraphicRaycaster>();
            m_progressSlider.interactable = false;
            m_statusText.supportRichText = false;
            m_statusText.rectTransform.sizeDelta = new Vector2(1600f, 140f);
            m_percentText.supportRichText = false;
            BuildErrorPanel();
            EnsureInput();
            try
            {
                StartupLoading.Register(this);
                m_registered = true;
                SceneManager.sceneLoaded += OnSceneLoaded;
                Application.quitting += OnApplicationQuitting;
                m_canvas.enabled = false;
                StartCoroutine(ConfirmFirstFrame());
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                Release();
            }
        }

        private IEnumerator ConfirmFirstFrame()
        {
            // Logo 先显示一帧，再确认 Loading 真正绘制，主线程始终保持可推进。
            yield return new WaitForEndOfFrame();
            m_canShowLoading = true;
            Render(StartupLoading.Snapshot, StartupLoading.Error);
            yield return new WaitForEndOfFrame();
            m_isReady = true;
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureInput();
        }

        private void EnsureInput()
        {
            // Boot 输入节点会随场景卸载，回退输入节点与 Loading 同寿命。
            var eventSystems = UnityEngine.Object.FindObjectsOfType<UnityEventSystem>();
            foreach (var eventSystem in eventSystems)
            {
                if (eventSystem == m_ownedEventSystem || !eventSystem.isActiveAndEnabled) continue;
                UnityEventSystem.current = eventSystem;
                if (m_ownedEventSystem != null)
                {
                    m_ownedEventSystem.gameObject.SetActive(false);
                    Destroy(m_ownedEventSystem.gameObject);
                    m_ownedEventSystem = null;
                }
                return;
            }
            // 正常 Loading 无可交互按钮，不创建与后续业务 UI 冲突的输入节点。
            if (StartupLoading.Error == null) return;
            if (m_ownedEventSystem == null)
            {
                var inputRoot = new GameObject("StartupInput",
                    typeof(UnityEventSystem), typeof(StandaloneInputModule));
                inputRoot.transform.SetParent(transform, false);
                m_ownedEventSystem = inputRoot.GetComponent<UnityEventSystem>();
            }
        }

        private void Update()
        {
            // 业务 UI 根节点也可能在场景回调之后创建，及时让出或补回输入。
            if (Time.unscaledTime < m_nextInputCheck) return;
            m_nextInputCheck = Time.unscaledTime + 0.25f;
            EnsureInput();
        }

        private void LateUpdate()
        {
            // 画圈唤出日志后隐藏 Overlay Canvas，使 IMGUI 能显示并接收输入；关闭日志自动恢复。
            if (!m_releasing) m_canvas.enabled = m_showCanvas && !(m_logViewer?.IsVisible ?? false);
        }

        private void BuildErrorPanel()
        {
            // 最后一个兄弟节点覆盖所有内容，背景拦截射线，错误只提供退出。
            RectTransform overlay = CreateRect("StartupError", transform, Vector2.zero, Vector2.one);
            m_errorPanel = overlay.gameObject;
            Image shade = m_errorPanel.AddComponent<Image>();
            shade.color = new Color(0f, 0f, 0f, 0.85f);
            RectTransform panel = CreateRect("Message", overlay, new Vector2(0.15f, 0.25f),
                new Vector2(0.85f, 0.8f));
            m_errorText = panel.gameObject.AddComponent<Text>();
            m_errorText.font = m_statusText.font;
            m_errorText.fontSize = 36;
            m_errorText.alignment = TextAnchor.MiddleCenter;
            m_errorText.color = Color.white;
            m_errorText.supportRichText = false;
            m_errorText.raycastTarget = false;

            // 错误面板提供明确的日志入口，查看异常后仍可返回原错误状态。
            RectTransform logRect = CreateRect("ViewLogs", overlay, new Vector2(0.22f, 0.12f),
                new Vector2(0.46f, 0.22f));
            Image logImage = logRect.gameObject.AddComponent<Image>();
            logImage.color = new Color(0.25f, 0.45f, 0.7f, 1f);
            m_logButton = logRect.gameObject.AddComponent<Button>();
            m_logButton.targetGraphic = logImage;
            m_logButton.onClick.AddListener(ShowLogViewer);
            RectTransform logLabelRect = CreateRect("Label", logRect, Vector2.zero, Vector2.one);
            Text logLabel = logLabelRect.gameObject.AddComponent<Text>();
            logLabel.font = m_statusText.font;
            logLabel.fontSize = 36;
            logLabel.color = Color.white;
            logLabel.alignment = TextAnchor.MiddleCenter;
            logLabel.text = "查看日志";
            logLabel.raycastTarget = false;

            RectTransform buttonRect = CreateRect("Exit", overlay, new Vector2(0.54f, 0.12f),
                new Vector2(0.78f, 0.22f));
            Image buttonImage = buttonRect.gameObject.AddComponent<Image>();
            buttonImage.color = new Color(0.25f, 0.45f, 0.7f, 1f);
            m_exitButton = buttonRect.gameObject.AddComponent<Button>();
            m_exitButton.targetGraphic = buttonImage;
            m_exitButton.onClick.AddListener(ExitGame);
            RectTransform labelRect = CreateRect("Label", buttonRect, Vector2.zero, Vector2.one);
            Text label = labelRect.gameObject.AddComponent<Text>();
            label.font = m_statusText.font;
            label.fontSize = 36;
            label.color = Color.white;
            label.alignment = TextAnchor.MiddleCenter;
            label.text = "退出";
            label.raycastTarget = false;
            m_errorPanel.SetActive(false);
        }

        private static RectTransform CreateRect(string objectName, Transform parent, Vector2 minimum, Vector2 maximum)
        {
            var child = new GameObject(objectName, typeof(RectTransform));
            var rectangle = child.GetComponent<RectTransform>();
            rectangle.SetParent(parent, false);
            rectangle.anchorMin = minimum;
            rectangle.anchorMax = maximum;
            rectangle.offsetMin = Vector2.zero;
            rectangle.offsetMax = Vector2.zero;
            return rectangle;
        }

        private static void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OnApplicationQuitting()
        {
            Release();
        }

        private void OnDestroy()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            Application.quitting -= OnApplicationQuitting;
            if (m_exitButton != null) m_exitButton.onClick.RemoveListener(ExitGame);
            if (m_logButton != null) m_logButton.onClick.RemoveListener(ShowLogViewer);
            if (m_registered) StartupLoading.Unregister(this);
        }
    }
}
