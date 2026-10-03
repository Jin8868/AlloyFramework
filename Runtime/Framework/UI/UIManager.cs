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
            var snapshot = m_entries.ToArray();
            foreach (var entry in snapshot) Release(entry);
            m_entries.Clear();
            m_definitions.Clear();
            m_rootInstance?.Dispose();
            m_rootInstance = null;
            m_root = null;
            m_shutdown?.Dispose();
            m_shutdown = null;
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
                Release(entry);
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
            var snapshot = m_entries.ToArray();
            foreach (var entry in snapshot)
                if (entry.State != UIState.Cached) await CloseEntryAsync(entry);
        }

        public void Close(string uiName, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(() => CloseAsync(uiName), onCompleted);
        public void Close(UIDefinition definition, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(() => CloseAsync(definition), onCompleted);
        public void Close(UIHandle handle, Action<Exception> onCompleted = null) =>
            UICallbacks.Run(() => CloseAsync(handle), onCompleted);
        public void CloseAll(Action<Exception> onCompleted = null) =>
            UICallbacks.Run(CloseAllAsync, onCompleted);

        public bool IsOpen(string uiName)
        {
            foreach (var entry in m_entries)
                if (entry.Definition.UIName == uiName &&
                    (entry.State == UIState.Active || entry.State == UIState.Paused)) return true;
            return false;
        }

        public bool IsOpen(UIDefinition definition) =>
            IsOpen(definition?.UIName ?? throw new ArgumentNullException(nameof(definition)));

        public bool TryGet<TController>(UIDefinition definition, out UIHandle<TController> handle)
            where TController : UIController
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            foreach (var entry in m_entries)
                if (entry.Definition.UIName == definition.UIName &&
                    (entry.State == UIState.Active || entry.State == UIState.Paused) &&
                    entry.Handle is UIHandle<TController> typed)
                {
                    handle = typed;
                    return true;
                }
            handle = null;
            return false;
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
                    throw new InvalidOperationException($"UI {entry.Definition.UIName} is missing {entry.Definition.ViewType.Name} on the prefab root.");
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
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            var handle = new UIHandle<TController>(++m_nextInstanceId, entry.Definition.UIName,
                (TController)entry.Controller, CloseAsync, () => entry.State);
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
            SetState(entry, UIState.Opening);
            EventSystem.Instance.SendEvent(UIEventNames.Opening, entry.Definition.UIName);
            entry.Controller.StartOpenAnimation();
            await entry.Controller.PlayOpenAnimationAsync(token);
            token.ThrowIfCancellationRequested();
            entry.Controller.EndOpenAnimation();
            token.ThrowIfCancellationRequested();
            canvasGroup.alpha = 1f;
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
            if (entry.State == UIState.Loading || entry.State == UIState.Preparing ||
                entry.State == UIState.Opening)
            {
                entry.Lifetime?.Cancel();
                try { if (entry.Opening != null) await entry.Opening.Task; }
                catch (Exception) { }
                if (entry.State == UIState.Disposed || entry.State == UIState.Cached) return;
            }

            var completion = new UniTaskCompletionSource();
            entry.Closing = completion;
            SetState(entry, UIState.Closing);
            EventSystem.Instance.SendEvent(UIEventNames.Closing, entry.Definition.UIName);
            try
            {
                var controller = entry.Controller;
                if (entry.Opened)
                {
                    controller.Closed();
                    entry.Opened = false;
                }
                if (controller != null)
                {
                    controller.StartCloseAnimation();
                    await controller.PlayCloseAnimationAsync();
                    controller.EndCloseAnimation();
                }
                entry.Handle?.Invalidate();
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
