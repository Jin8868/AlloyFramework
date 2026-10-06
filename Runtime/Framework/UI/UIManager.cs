using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework.UI
{
    [FrameworkSystem(FrameworkSystemPriority.UI)]
    public sealed class UIManager : FrameworkSystem
    {
        private readonly Dictionary<string, UIDefinition> m_definitions =
            new Dictionary<string, UIDefinition>(StringComparer.Ordinal);
        private readonly List<UIEntry> m_entries = new List<UIEntry>();
        private UINavigator m_navigator; // 配置驱动的 UI 导航器。
        private IUIDefinitionProvider m_definitionProvider; // 业务安装的 UI 定义提供器。
        private CancellationTokenSource m_shutdown;
        private IInstanceHandle m_rootInstance;
        private UIRoot m_root;
        private long m_nextInstanceId;

        internal UIManager() { }

        public static UIManager Instance { get; private set; }

        public override async UniTask InitializeAsync(FrameworkContext context,
            CancellationToken cancellationToken)
        {
            m_shutdown = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            try
            {
                var rootStartedAt = FrameworkStartupLog.Now;
                m_rootInstance = await ResourceManager.Instance.InstantiateAsync(
                    UISettings.RootLocation, packageName: UISettings.RootPackageName,
                    cancellationToken: m_shutdown.Token);
                m_shutdown.Token.ThrowIfCancellationRequested();
                FrameworkStartupLog.Step("加载 UI 根预制体", rootStartedAt);
                var validationStartedAt = FrameworkStartupLog.Now;
                m_root = m_rootInstance.Instance.GetComponentInChildren<UIRoot>(true);
                if (m_root == null)
                    throw new InvalidOperationException($"UIRoot component is missing from {UISettings.RootLocation}.");
                m_root.Initialize(m_rootInstance.Instance);
                UnityEngine.Object.DontDestroyOnLoad(m_rootInstance.Instance);
                m_navigator = new UINavigator(this);
                Instance = this;
                FrameworkStartupLog.Step("校验并启用 UI 根节点", validationStartedAt);
            }
            catch
            {
                m_rootInstance?.Dispose();
                m_rootInstance = null;
                m_root = null;
                m_shutdown.Dispose();
                m_shutdown = null;
                throw;
            }
        }

        public override void Shutdown()
        {
            if (ReferenceEquals(Instance, this)) Instance = null;
            m_shutdown?.Cancel();
            m_navigator?.Clear();
            var snapshot = m_entries.ToArray();
            foreach (var entry in snapshot) Release(entry);
            m_entries.Clear();
            m_definitions.Clear();
            m_definitionProvider = null;
            m_rootInstance?.Dispose();
            m_rootInstance = null;
            m_root = null;
            m_navigator = null;
            m_shutdown?.Dispose();
            m_shutdown = null;
        }

        /// <summary>当前全屏 UI 主画布。</summary>
        public Canvas RootCanvas
        {
            get { EnsureReady(); return m_root.RootCanvas; }
        }

        /// <summary>主画布使用的 UI 相机；Overlay 模式下为空。</summary>
        public Camera UICamera => RootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : RootCanvas.worldCamera;

        /// <summary>当前全屏 UI 缩放器。</summary>
        public UnityEngine.UI.CanvasScaler CanvasScaler
        {
            get { EnsureReady(); return m_root.Scaler; }
        }

        /// <summary>全屏 UI 内容根容器。</summary>
        public RectTransform WindowRoot
        {
            get { EnsureReady(); return m_root.WindowRoot; }
        }

        /// <summary>获取指定 UI 层的内容容器。</summary>
        /// <param name="layer">目标 UI 层。</param>
        /// <returns>目标层的内容容器。</returns>
        public RectTransform GetLayerRoot(UILayer layer)
        {
            EnsureReady();
            return m_root.GetLayer(layer);
        }

        /// <summary>获取指定 UI 层的画布。</summary>
        /// <param name="layer">目标 UI 层。</param>
        /// <returns>目标层的画布。</returns>
        public Canvas GetLayerCanvas(UILayer layer)
        {
            EnsureReady();
            return m_root.GetLayerCanvas(layer);
        }

        /// <summary>尝试获取当前有效的屏幕适配快照。</summary>
        /// <param name="snapshot">成功时返回当前快照。</param>
        /// <returns>存在有效快照时返回 true。</returns>
        public bool TryGetScreenAdaptationSnapshot(out UIScreenAdaptationSnapshot snapshot)
        {
            snapshot = default;
            return ReferenceEquals(Instance, this) && m_root != null &&
                   m_root.ScreenAdaptationSystem != null &&
                   m_root.ScreenAdaptationSystem.TryGetSnapshot(out snapshot);
        }

        /// <summary>尝试获取当前管理器所属界面实例的根节点。</summary>
        /// <param name="handle">具体界面实例的句柄。</param>
        /// <param name="rectTransform">成功时返回界面根节点。</param>
        /// <returns>句柄有效且界面实例存在时返回 true。</returns>
        public bool TryGetUIRectTransform(UIHandle handle, out RectTransform rectTransform)
        {
            rectTransform = null;
            if (handle == null || !handle.IsValid)
            {
                return false;
            }

            var entry = FindByHandle(handle);
            if (entry == null || entry.View == null)
            {
                return false;
            }

            rectTransform = entry.View.transform as RectTransform;
            return rectTransform != null;
        }

        public void Register(UIDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (m_definitions.TryGetValue(definition.UIName, out var existing))
            {
                if (!ReferenceEquals(existing, definition))
                    throw new InvalidOperationException($"UIName {definition.UIName} has multiple definitions.");
                return;
            }
            m_definitions.Add(definition.UIName, definition);
        }

        /// <summary>
        /// 安装业务提供的 UI 跳转配置并立即校验所有静态引用。
        /// </summary>
        /// <param name="provider">UI 跳转配置提供器。</param>
        public void ConfigureNavigation(IUIJumpConfigProvider provider)
        {
            EnsureReady();
            m_navigator.Configure(provider);
        }

        /// <summary>
        /// 安装按名称查询业务 UI 定义的提供器。
        /// </summary>
        /// <param name="provider">业务 UI 定义提供器。</param>
        public void SetDefinitionProvider(IUIDefinitionProvider provider)
        {
            EnsureReady();
            m_definitionProvider = provider ?? throw new ArgumentNullException(nameof(provider));
        }

        /// <summary>
        /// 注册需要接收全局屏幕适配快照的目标，并立即应用当前快照。
        /// </summary>
        /// <param name="target">需要接收屏幕适配更新的目标。</param>
        public void RegisterScreenAdaptationTarget(IUIScreenAdaptationTarget target)
        {
            EnsureReady();
            m_root.RegisterScreenAdaptationTarget(target);
        }

        /// <summary>
        /// 注销不再需要接收全局屏幕适配快照的目标。
        /// </summary>
        /// <param name="target">需要注销的屏幕适配目标。</param>
        public void UnregisterScreenAdaptationTarget(IUIScreenAdaptationTarget target)
        {
            EnsureReady();
            m_root.UnregisterScreenAdaptationTarget(target);
        }

        /// <summary>
        /// 尝试按稳定 UI 名称取得已注册的界面定义。
        /// </summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="definition">成功时返回对应界面定义。</param>
        /// <returns>找到定义时返回 true，否则返回 false。</returns>
        public bool TryGetDefinition(string uiName, out UIDefinition definition)
        {
            if (m_definitions.TryGetValue(uiName, out definition))
            {
                return true;
            }

            if (m_definitionProvider == null ||
                !m_definitionProvider.TryGetDefinition(uiName, out definition) ||
                definition == null)
            {
                definition = null;
                return false;
            }

            Register(definition);
            return true;
        }

        /// <summary>
        /// 根据跳转配置 ID 打开目标界面。
        /// </summary>
        /// <param name="jumpID">跳转配置的唯一标识。</param>
        /// <param name="prepareHandler">本次跳转特有的数据准备方法。</param>
        /// <param name="data">调用方传入的原始业务数据。</param>
        /// <param name="cancellationToken">用于取消本次跳转的令牌。</param>
        /// <returns>目标界面的运行时句柄。</returns>
        public UniTask<UIHandle> JumpAsync(
            int jumpID,
            UIJumpPrepareHandler prepareHandler = null,
            object data = null,
            CancellationToken cancellationToken = default)
        {
            EnsureReady();
            return m_navigator.JumpAsync(jumpID, prepareHandler, data, cancellationToken);
        }

        /// <summary>
        /// 根据跳转配置 ID 打开目标界面并通过回调返回结果。
        /// </summary>
        /// <param name="jumpID">跳转配置的唯一标识。</param>
        /// <param name="prepareHandler">本次跳转特有的数据准备方法。</param>
        /// <param name="data">调用方传入的原始业务数据。</param>
        /// <param name="onCompleted">跳转完成后的结果回调。</param>
        /// <param name="cancellationToken">用于取消本次跳转的令牌。</param>
        public void Jump(
            int jumpID,
            UIJumpPrepareHandler prepareHandler = null,
            object data = null,
            Action<UIHandle, Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            UICallbacks.Run(JumpAsync(jumpID, prepareHandler, data, cancellationToken), onCompleted);
        }

        /// <summary>
        /// 按最近一次已提交的导航记录返回。
        /// </summary>
        /// <param name="cancellationToken">用于取消本次返回的令牌。</param>
        /// <returns>成功执行返回时为 true，没有可返回记录时为 false。</returns>
        public UniTask<bool> BackAsync(CancellationToken cancellationToken = default)
        {
            EnsureReady();
            return m_navigator.BackAsync(cancellationToken);
        }

        /// <summary>
        /// 按最近一次已提交的导航记录返回并通过回调报告结果。
        /// </summary>
        /// <param name="onCompleted">返回完成后的结果回调。</param>
        /// <param name="cancellationToken">用于取消本次返回的令牌。</param>
        public void Back(
            Action<bool, Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
        {
            UICallbacks.Run(BackAsync(cancellationToken), onCompleted);
        }

        public async UniTask<UIHandle<TController>> OpenAsync<TView, TController, TData>(
            UIDefinition<TView, TController, TData> definition, TData data,
            CancellationToken cancellationToken = default)
            where TView : UIView
            where TController : UIController<TView, TData>, new()
        {
            EnsureReady();
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            cancellationToken.ThrowIfCancellationRequested();
            Register(definition);

            if (definition.OpenMode != UIOpenMode.Multiple)
            {
                var current = FindLatest(definition.UIName, false);
                if (current != null)
                {
                    if (current.State == UIState.Closing)
                    {
                        await current.Closing.Task.AttachExternalCancellation(cancellationToken);
                    }
                    else
                    {
                        if (definition.OpenMode == UIOpenMode.SingleReject)
                            throw new InvalidOperationException($"UI {definition.UIName} is already open.");
                        if (current.Opening != null)
                            await current.Opening.Task.AttachExternalCancellation(cancellationToken);
                        cancellationToken.ThrowIfCancellationRequested();
                        if (current.State == UIState.Active || current.State == UIState.Paused)
                        {
                            if (definition.OpenMode == UIOpenMode.SingleRefresh)
                                current.Controller.Refresh(data);
                            BringToFront(current);
                            return (UIHandle<TController>)current.Handle;
                        }
                    }
                }
            }

            var cached = FindLatest(definition.UIName, true);
            var entry = cached ?? new UIEntry { Definition = definition };
            if (cached == null) m_entries.Add(entry);
            entry.Data = data;
            entry.Lifetime = CancellationTokenSource.CreateLinkedTokenSource(
                m_shutdown.Token, cancellationToken);
            entry.Opening = new UniTaskCompletionSource<UIHandle>();
            SetState(entry, UIState.Loading);
            try
            {
                var handle = await OpenEntryAsync<TController>(entry, data);
                entry.Lifetime?.Dispose();
                entry.Lifetime = null;
                entry.Opening.TrySetResult(handle);
                entry.Opening = null;
                return handle;
            }
            catch (Exception exception)
            {
                entry.Opening?.TrySetException(exception);
                entry.Opening = null;
                if (entry.CloseRequested)
                {
                    entry.Lifetime?.Dispose();
                    entry.Lifetime = null;
                }
                else
                {
                    Release(entry);
                }

                throw;
            }
        }

        public UniTask<UIHandle<TController>> OpenAsync<TView, TController>(
            UIDefinition<TView, TController> definition,
            CancellationToken cancellationToken = default)
            where TView : UIView
            where TController : UIController<TView, UIEmptyData>, new() =>
            OpenAsync<TView, TController, UIEmptyData>(definition, default, cancellationToken);

        public void Open<TView, TController, TData>(
            UIDefinition<TView, TController, TData> definition, TData data,
            Action<UIHandle<TController>, Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
            where TView : UIView
            where TController : UIController<TView, TData>, new() =>
            UICallbacks.Run(OpenAsync(definition, data, cancellationToken), onCompleted);

        public void Open<TView, TController>(UIDefinition<TView, TController> definition,
            Action<UIHandle<TController>, Exception> onCompleted = null,
            CancellationToken cancellationToken = default)
            where TView : UIView
            where TController : UIController<TView, UIEmptyData>, new() =>
            UICallbacks.Run(OpenAsync(definition, cancellationToken), onCompleted);

        public async UniTask CloseAsync(string uiName)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(uiName)) throw new ArgumentException("UI name is required.", nameof(uiName));
            var snapshot = m_entries.ToArray();
            foreach (var entry in snapshot)
                if (entry.Definition.UIName == uiName && entry.State != UIState.Cached)
                    await CloseEntryAsync(entry);
        }

        public UniTask CloseAsync(UIDefinition definition) =>
            CloseAsync(definition?.UIName ?? throw new ArgumentNullException(nameof(definition)));

        public UniTask CloseAsync(UIHandle handle)
        {
            EnsureReady();
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            foreach (var entry in m_entries)
                if (ReferenceEquals(entry.Handle, handle)) return CloseEntryAsync(entry);
            return UniTask.CompletedTask;
        }

        public async UniTask CloseAllAsync()
        {
            EnsureReady();
            m_navigator.ClearRecords();
            var snapshot = m_entries.ToArray();
            foreach (var entry in snapshot)
                if (entry.State != UIState.Cached) await CloseEntryAsync(entry);
        }

        public void Close(string uiName, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAsync(uiName), onCompleted);
        public void Close(UIDefinition definition, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAsync(definition), onCompleted);
        public void Close(UIHandle handle, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAsync(handle), onCompleted);
        public void CloseAll(Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAllAsync, onCompleted);

        /// <summary>判断指定名称是否有处于激活或暂停状态的界面。</summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <returns>存在已打开实例时返回 true。</returns>
        public bool IsOpen(string uiName)
        {
            foreach (var entry in m_entries)
                if (entry.Definition.UIName == uiName &&
                    (entry.State == UIState.Active || entry.State == UIState.Paused)) return true;
            return false;
        }

        public bool IsOpen(UIDefinition definition) =>
            IsOpen(definition?.UIName ?? throw new ArgumentNullException(nameof(definition)));

        /// <summary>按界面定义获取最近打开实例的强类型句柄。</summary>
        /// <param name="definition">目标界面的定义。</param>
        /// <param name="handle">成功时返回对应的实例句柄。</param>
        /// <typeparam name="TController">目标界面控制器类型。</typeparam>
        /// <returns>存在已打开实例且类型匹配时返回 true。</returns>
        /// <exception cref="ArgumentNullException">界面定义为空。</exception>
        public bool TryGet<TController>(UIDefinition definition, out UIHandle<TController> handle)
            where TController : UIController
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            return TryGet(definition.UIName, out handle);
        }

        /// <summary>按名称获取最近打开且处于激活或暂停状态的界面。</summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="handle">成功时返回实例句柄。</param>
        /// <returns>找到有效实例时返回 true。</returns>
        public bool TryGet(string uiName, out UIHandle handle)
        {
            // 实例编号体现打开顺序，缓存实例重新打开也会得到新的编号。
            handle = null;
            foreach (var entry in m_entries)
            {
                if (entry.Definition.UIName == uiName && IsEntryOpen(entry) &&
                    (handle == null || entry.Handle.InstanceId > handle.InstanceId))
                {
                    handle = entry.Handle;
                }
            }

            return handle != null;
        }

        /// <summary>按名称获取最近打开实例的强类型句柄。</summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="handle">成功时返回指定控制器类型的句柄。</param>
        /// <typeparam name="TController">界面控制器类型。</typeparam>
        /// <returns>找到实例且控制器类型匹配时返回 true。</returns>
        public bool TryGet<TController>(string uiName, out UIHandle<TController> handle)
            where TController : UIController
        {
            TryGet(uiName, out UIHandle currentHandle);
            handle = currentHandle as UIHandle<TController>;
            return handle != null;
        }

        /// <summary>获取指定名称的全部已打开实例，按打开顺序排列。</summary>
        /// <param name="uiName">界面的稳定名称。</param>
        /// <param name="results">接收结果的列表，调用时先清空。</param>
        /// <exception cref="ArgumentNullException">结果列表为空。</exception>
        public void GetOpenHandles(string uiName, List<UIHandle> results)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            foreach (var entry in m_entries)
            {
                if (entry.Definition.UIName != uiName || !IsEntryOpen(entry))
                {
                    continue;
                }

                var insertIndex = results.Count;
                while (insertIndex > 0 && results[insertIndex - 1].InstanceId > entry.Handle.InstanceId)
                {
                    insertIndex--;
                }

                results.Insert(insertIndex, entry.Handle);
            }
        }

        private static bool IsEntryOpen(UIEntry entry)
        {
            return (entry.State == UIState.Active || entry.State == UIState.Paused) &&
                   entry.Handle != null && entry.Handle.IsValid;
        }

        public UIHandle Top(UILayer layer)
        {
            UIEntry best = null;
            foreach (var entry in m_entries)
                if (entry.Definition.Layer == layer && entry.State == UIState.Active &&
                    (best == null || entry.View.transform.GetSiblingIndex() > best.View.transform.GetSiblingIndex()))
                    best = entry;
            return best?.Handle;
        }

        internal UIHandle CurrentHandle
        {
            get
            {
                for (var index = m_entries.Count - 1; index >= 0; index--)
                {
                    var entry = m_entries[index];
                    if (entry.State == UIState.Active && entry.Handle != null)
                    {
                        return entry.Handle;
                    }
                }

                return null;
            }
        }

        internal bool ContainsDefinition(string uiName)
        {
            return m_definitions.ContainsKey(uiName) ||
                   m_definitionProvider != null && m_definitionProvider.Contains(uiName);
        }

        internal UniTask<UIHandle> OpenDefinitionAsync(
            UIDefinition definition,
            object data,
            int jumpID,
            CancellationToken cancellationToken)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            var normalizedData = NormalizeData(definition, data, jumpID);
            return definition.OpenAsync(this, normalizedData, cancellationToken);
        }

        internal void Pause(UIHandle handle)
        {
            var entry = FindByHandle(handle);
            if (entry == null || entry.State != UIState.Active)
            {
                return;
            }

            var canvasGroup = entry.Instance.Instance.GetComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            SetState(entry, UIState.Paused);
            entry.Controller.Pause();
        }

        internal void Resume(UIHandle handle)
        {
            var entry = FindByHandle(handle);
            if (entry == null || entry.State != UIState.Paused)
            {
                return;
            }

            var canvasGroup = entry.Instance.Instance.GetComponent<CanvasGroup>();
            canvasGroup.interactable = entry.Definition.InputMode != UIInputMode.PassThrough;
            canvasGroup.blocksRaycasts = entry.Definition.InputMode != UIInputMode.PassThrough;
            SetState(entry, UIState.Active);
            entry.Controller.Resume();
            BringToFront(entry);
        }

        private async UniTask<UIHandle<TController>> OpenEntryAsync<TController>(UIEntry entry, object data)
            where TController : UIController
        {
            var token = entry.Lifetime.Token;
            if (entry.Instance == null)
            {
                var instance = await ResourceManager.Instance.InstantiateAsync(
                    entry.Definition.Location, m_root.GetLayer(entry.Definition.Layer),
                    entry.Definition.PackageName, cancellationToken: token);
                if (entry.State == UIState.Disposed)
                {
                    instance.Dispose();
                    throw new OperationCanceledException(token);
                }
                entry.Instance = instance;
                token.ThrowIfCancellationRequested();
                var gameObject = entry.Instance.Instance;
                entry.View = gameObject.GetComponent(entry.Definition.ViewType) as UIView;
                if (entry.View == null)
                {
                    throw new InvalidOperationException(
                        $"UI {entry.Definition.UIName} is missing " +
                        $"{entry.Definition.ViewType.Name} on the prefab root.");
                }
                entry.Controller = entry.Definition.CreateController();
                entry.Controller.UIName = entry.Definition.UIName;
                entry.Controller.State = entry.State;
                entry.Controller.AttachView(entry.View);
            }
            else
            {
                entry.Instance.Instance.SetActive(true);
                BringToFront(entry);
            }

            var canvasGroup = entry.Instance.Instance.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = entry.Instance.Instance.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var handle = new UIHandle<TController>(++m_nextInstanceId, entry.Definition.UIName,
                (TController)entry.Controller, CloseAsync, entry);
            entry.Handle = handle;
            entry.Controller.SetHandle(handle);
            if (!entry.Created)
            {
                entry.Created = true;
                entry.Controller.Create();
            }
            token.ThrowIfCancellationRequested();
            SetState(entry, UIState.Preparing);
            await entry.Controller.PrepareAsync(data, token);
            token.ThrowIfCancellationRequested();
            entry.Controller.InitData(data);
            entry.OpenLifecycleStarted = true;
            entry.View.PrepareOpenAnimation();
            SetState(entry, UIState.Opening);
            EventSystem.Instance.SendEvent(UIEventNames.Opening, entry.Definition.UIName);
            entry.Controller.StartOpenAnimation();
            await UniTask.WhenAll(
                entry.View.PlayOpenAnimationAsync(token),
                entry.Controller.PlayOpenAnimationAsync(token));
            token.ThrowIfCancellationRequested();
            entry.Controller.EndOpenAnimation();
            token.ThrowIfCancellationRequested();
            canvasGroup.interactable = entry.Definition.InputMode != UIInputMode.PassThrough;
            canvasGroup.blocksRaycasts = entry.Definition.InputMode != UIInputMode.PassThrough;
            SetState(entry, UIState.Active);
            entry.Opened = true;
            entry.Controller.Opened();
            if (entry.State != UIState.Active)
                throw new OperationCanceledException($"UI {entry.Definition.UIName} closed during OnOpen.", token);
            token.ThrowIfCancellationRequested();
            EventSystem.Instance.SendEvent(UIEventNames.Opened, entry.Definition.UIName);
            return handle;
        }

        private async UniTask CloseEntryAsync(UIEntry entry)
        {
            if (entry.State == UIState.Disposed || entry.State == UIState.Cached) return;
            if (entry.State == UIState.Closing)
            {
                await entry.Closing.Task;
                return;
            }
            if (entry.Opening != null)
            {
                entry.CloseRequested = true;
                entry.Lifetime?.Cancel();
                try { if (entry.Opening != null) await entry.Opening.Task; }
                catch (Exception) { }
                if (entry.State == UIState.Disposed || entry.State == UIState.Cached) return;
            }

            if (entry.Instance == null || entry.Controller == null || entry.View == null ||
                !entry.OpenLifecycleStarted)
            {
                Release(entry);
                return;
            }

            entry.CloseRequested = false;
            entry.Lifetime = CancellationTokenSource.CreateLinkedTokenSource(m_shutdown.Token);
            var token = entry.Lifetime.Token;
            var completion = new UniTaskCompletionSource();
            entry.Closing = completion;
            SetState(entry, UIState.Closing);
            EventSystem.Instance.SendEvent(UIEventNames.Closing, entry.Definition.UIName);
            try
            {
                var controller = entry.Controller;
                var canvasGroup = entry.Instance?.Instance.GetComponent<CanvasGroup>();
                if (canvasGroup != null)
                {
                    canvasGroup.interactable = false;
                    canvasGroup.blocksRaycasts = false;
                }

                if (entry.OpenLifecycleStarted)
                {
                    controller.Closed();
                    entry.OpenLifecycleStarted = false;
                    entry.Opened = false;
                }
                if (controller != null)
                {
                    controller.StartCloseAnimation();
                    await UniTask.WhenAll(
                        entry.View.PlayCloseAnimationAsync(token),
                        controller.PlayCloseAnimationAsync(token));
                    token.ThrowIfCancellationRequested();
                    controller.EndCloseAnimation();
                }
                entry.Handle?.Invalidate();
                m_navigator?.OnClosed(entry.Handle);
                entry.Handle = null;
                controller?.SetHandle(null);
                if (entry.Definition.CacheMode == UICacheMode.DestroyOnClose)
                {
                    EventSystem.Instance.SendEvent(UIEventNames.Closed, entry.Definition.UIName);
                    Release(entry);
                }
                else
                {
                    entry.Instance.Instance.SetActive(false);
                    SetState(entry, UIState.Cached);
                    entry.Lifetime?.Dispose();
                    entry.Lifetime = null;
                    EventSystem.Instance.SendEvent(UIEventNames.Closed, entry.Definition.UIName);
                }
                completion.TrySetResult();
            }
            catch (Exception exception)
            {
                Release(entry);
                completion.TrySetException(exception);
                throw;
            }
            finally { entry.Closing = null; }
        }

        private UIEntry FindLatest(string uiName, bool cached)
        {
            for (var index = m_entries.Count - 1; index >= 0; index--)
            {
                var entry = m_entries[index];
                if (entry.Definition.UIName == uiName &&
                    (cached ? entry.State == UIState.Cached :
                        entry.State != UIState.Cached && entry.State != UIState.Disposed))
                    return entry;
            }
            return null;
        }

        private UIEntry FindByHandle(UIHandle handle)
        {
            if (handle == null)
            {
                return null;
            }

            foreach (var entry in m_entries)
            {
                if (ReferenceEquals(entry.Handle, handle))
                {
                    return entry;
                }
            }

            return null;
        }

        private static object NormalizeData(UIDefinition definition, object data, int jumpID)
        {
            if (definition.DataType == typeof(UIEmptyData) && data == null)
            {
                return default(UIEmptyData);
            }

            if (data == null)
            {
                if (definition.DataType.IsValueType)
                {
                    throw new InvalidOperationException(
                        $"UI 跳转数据为空：JumpID={jumpID}，UIName={definition.UIName}，" +
                        $"Expected={definition.DataType.FullName}。");
                }

                return null;
            }

            if (!definition.DataType.IsInstanceOfType(data))
            {
                throw new InvalidOperationException(
                    $"UI 跳转数据类型错误：JumpID={jumpID}，UIName={definition.UIName}，" +
                    $"Expected={definition.DataType.FullName}，Actual={data.GetType().FullName}。");
            }

            return data;
        }

        private static void BringToFront(UIEntry entry) =>
            entry.Instance?.Instance.transform.SetAsLastSibling();

        private static void SetState(UIEntry entry, UIState state)
        {
            entry.State = state;
            if (entry.Controller != null) entry.Controller.State = state;
        }

        private void Release(UIEntry entry)
        {
            if (entry.State == UIState.Disposed) return;
            var uiName = entry.Definition.UIName;
            var hadInstance = entry.Instance != null;
            entry.Lifetime?.Cancel();
            entry.Handle?.Invalidate();
            entry.Handle = null;
            if (entry.Created)
            {
                try { entry.Controller.DisposeController(); }
                catch (Exception exception) { AlloyDebug.Error(exception); }
            }
            entry.Instance?.Dispose();
            entry.Instance = null;
            entry.View = null;
            entry.Controller = null;
            SetState(entry, UIState.Disposed);
            entry.Lifetime?.Dispose();
            entry.Lifetime = null;
            m_entries.Remove(entry);
            if (hadInstance)
                EventSystem.Instance.SendEvent(UIEventNames.Destroyed, uiName);
        }

        private void EnsureReady()
        {
            if (!ReferenceEquals(Instance, this) || m_root == null || m_shutdown == null ||
                m_shutdown.IsCancellationRequested)
                throw new InvalidOperationException("UIManager is not initialized.");
        }
    }
}
