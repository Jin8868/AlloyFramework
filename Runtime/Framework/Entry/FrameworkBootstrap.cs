using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace AlloyFramework
{
    public static class FrameworkBootstrap
    {
        private const string StartupScreenObjectName = "FrameworkSplash";

        private static readonly List<FrameworkSystem> InitializedSystems =
            new List<FrameworkSystem>();

        private static FrameworkContext _context;
        private static IGameEntry _gameEntry;
        private static CancellationTokenSource _startupCancellation;
        private static GameObject _startupScreen;
        private static bool _isStarting;
        private static bool _isInitialized;
        private static bool _isShuttingDown;
        private static bool _isGameEntryStarted;

        public static bool IsStarting => _isStarting;

        public static bool IsInitialized => _isInitialized;

        public static event Action<Type, int, int> SystemInitializing;

        public static event Action Initialized;

        public static event Action<Exception> StartupFailed;

        /// <summary>
        /// 销毁框架启动时保存的启动画面。没有找到或已经销毁时不会执行任何操作。
        /// </summary>
        public static void DestroyStartupScreen()
        {
            var startupScreen = _startupScreen;
            _startupScreen = null;
            if (startupScreen != null) UnityEngine.Object.Destroy(startupScreen);
        }

        public static void SetGameEntry(IGameEntry gameEntry)
        {
            if (gameEntry == null)
            {
                throw new ArgumentNullException(nameof(gameEntry));
            }

            if (_isStarting || _isInitialized)
            {
                throw new InvalidOperationException(
                    "The game entry must be injected before framework startup.");
            }

            if (_gameEntry != null)
            {
                throw new InvalidOperationException("The game entry has already been injected.");
            }

            _gameEntry = gameEntry;
        }

        public static TService GetRequired<TService>() where TService : class
        {
            if (_context == null)
            {
                throw new InvalidOperationException("The framework has not started.");
            }

            return _context.GetRequired<TService>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Application.quitting -= HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException -= HandleUnobservedTaskException;

            ResetObjectPools();

            _startupCancellation?.Dispose();
            _startupCancellation = null;
            _startupScreen = null;
            _context = null;
            _gameEntry = null;
            _isStarting = false;
            _isInitialized = false;
            _isShuttingDown = false;
            _isGameEntryStarted = false;

            InitializedSystems.Clear();
            SystemInitializing = null;
            Initialized = null;
            StartupFailed = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Startup()
        {
            if (_isStarting || _isInitialized)
            {
                return;
            }

            var startupStartedAt = FrameworkStartupLog.Now;
            FrameworkStartupLog.Info("开始启动框架");
            _context = new FrameworkContext();
            _startupCancellation = new CancellationTokenSource();

            Application.quitting += HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;

            RunStartupAsync(_startupCancellation.Token, startupStartedAt).Forget(HandleStartupException);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CaptureStartupScreen()
        {
            _startupScreen = GameObject.Find(StartupScreenObjectName);
        }

        public static async UniTask ShutdownAsync()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            _startupCancellation?.Cancel();

            if (_isGameEntryStarted)
            {
                try
                {
                    await _gameEntry.ShutdownAsync();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }

                _isGameEntryStarted = false;
            }

            ShutdownSystems();
        }

        private static void ShutdownImmediately()
        {
            if (_isShuttingDown)
            {
                return;
            }

            _isShuttingDown = true;
            _startupCancellation?.Cancel();
            _isGameEntryStarted = false;
            ShutdownSystems();
        }

        private static void ShutdownSystems()
        {
            for (var index = InitializedSystems.Count - 1; index >= 0; index--)
            {
                try
                {
                    InitializedSystems[index].Shutdown();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }
            }

            InitializedSystems.Clear();
            ResetObjectPools();
            _context?.Clear();
            _isInitialized = false;
            _isStarting = false;
            _isShuttingDown = false;
        }

        private static void ResetObjectPools()
        {
            try
            {
                ObjectPoolManager.ResetInstance();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static async UniTask RunStartupAsync(CancellationToken cancellationToken,
            double startupStartedAt)
        {
            _isStarting = true;

            try
            {
                var discoveryStartedAt = FrameworkStartupLog.Now;
                var descriptors = DiscoverFrameworkSystems();
                FrameworkStartupLog.Step($"发现 {descriptors.Count} 个框架系统", discoveryStartedAt);

                for (var index = 0; index < descriptors.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var descriptor = descriptors[index];
                    var systemName = descriptor.SystemType.FullName;
                    var systemStartedAt = FrameworkStartupLog.Now;
                    FrameworkStartupLog.Info($"开始初始化框架系统 ({index + 1}/{descriptors.Count}): {systemName}");
                    try
                    {
                        SystemInitializing?.Invoke(descriptor.SystemType, index + 1, descriptors.Count);
                        var system = CreateSystem(descriptor.SystemType);
                        await system.InitializeAsync(_context, cancellationToken);
                        _context.Register(system);
                        InitializedSystems.Add(system);
                        FrameworkStartupLog.Step($"框架系统 {systemName}", systemStartedAt);
                    }
                    catch
                    {
                        FrameworkStartupLog.Failure($"框架系统 {systemName}", systemStartedAt);
                        throw;
                    }
                }

                _isInitialized = true;
                var callbackStartedAt = FrameworkStartupLog.Now;
                try
                {
                    Initialized?.Invoke();
                    FrameworkStartupLog.Step("初始化回调", callbackStartedAt);
                }
                catch
                {
                    FrameworkStartupLog.Failure("初始化回调", callbackStartedAt);
                    throw;
                }
                FrameworkStartupLog.Info($"框架启动完成，总耗时 {FrameworkStartupLog.ElapsedMilliseconds(startupStartedAt):F1} ms");

                if (_gameEntry != null)
                {
                    _isGameEntryStarted = true;
                    var gameEntryStartedAt = FrameworkStartupLog.Now;
                    FrameworkStartupLog.Info($"开始执行业务入口: {_gameEntry.GetType().FullName}");
                    try
                    {
                        await _gameEntry.MainAsync(cancellationToken);
                        FrameworkStartupLog.Step("业务入口", gameEntryStartedAt);
                        FrameworkStartupLog.Info(
                            $"完整启动完成（含业务入口），总耗时 {FrameworkStartupLog.ElapsedMilliseconds(startupStartedAt):F1} ms");
                    }
                    catch
                    {
                        FrameworkStartupLog.Failure("业务入口", gameEntryStartedAt);
                        throw;
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                FrameworkStartupLog.Info($"框架启动取消，已耗时 {FrameworkStartupLog.ElapsedMilliseconds(startupStartedAt):F1} ms");
                await ShutdownAsync();
            }
            catch (Exception exception)
            {
                FrameworkStartupLog.Failure("框架启动", startupStartedAt);
                await ShutdownAsync();
                StartupFailed?.Invoke(exception);
                throw;
            }
            finally
            {
                _isStarting = false;
            }
        }

        private static List<SystemDescriptor> DiscoverFrameworkSystems()
        {
            var descriptors = new List<SystemDescriptor>();
            var assemblies = AppDomain.CurrentDomain.GetAssemblies();

            for (var assemblyIndex = 0; assemblyIndex < assemblies.Length; assemblyIndex++)
            {
                var types = GetLoadableTypes(assemblies[assemblyIndex]);

                for (var typeIndex = 0; typeIndex < types.Length; typeIndex++)
                {
                    var type = types[typeIndex];
                    if (type == null ||
                        type.IsAbstract ||
                        type.IsGenericTypeDefinition ||
                        !typeof(FrameworkSystem).IsAssignableFrom(type))
                    {
                        continue;
                    }

                    var attribute = type.GetCustomAttribute<FrameworkSystemAttribute>(false);
                    if (attribute == null)
                    {
                        continue;
                    }

                    descriptors.Add(new SystemDescriptor(type, attribute.Priority));
                }
            }

            descriptors.Sort(CompareDescriptors);
            return descriptors;
        }

        private static Type[] GetLoadableTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types;
            }
        }

        private static FrameworkSystem CreateSystem(Type systemType)
        {
            try
            {
                return (FrameworkSystem)Activator.CreateInstance(systemType, true);
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Framework system requires a parameterless constructor: {systemType.FullName}",
                    exception);
            }
        }

        private static int CompareDescriptors(SystemDescriptor left, SystemDescriptor right)
        {
            var priorityComparison = left.Priority.CompareTo(right.Priority);
            if (priorityComparison != 0)
            {
                return priorityComparison;
            }

            return string.Compare(
                left.SystemType.FullName,
                right.SystemType.FullName,
                StringComparison.Ordinal);
        }

        private static void HandleApplicationQuitting()
        {
            ShutdownImmediately();
        }

        private static void HandleStartupException(Exception exception)
        {
            AlloyDebug.Error($"[AlloyFramewrokStartup] {exception}");
        }

        private static void HandleUnobservedTaskException(Exception exception)
        {
            AlloyDebug.Error(exception);
        }

        private sealed class SystemDescriptor
        {
            public SystemDescriptor(Type systemType, FrameworkSystemPriority priority)
            {
                SystemType = systemType;
                Priority = priority;
            }

            public Type SystemType { get; }

            public FrameworkSystemPriority Priority { get; }
        }
    }

    internal static class FrameworkStartupLog
    {
        private const string Prefix = "[AlloyFramewrokStartup]";

        internal static double Now => Time.realtimeSinceStartupAsDouble;

        internal static double ElapsedMilliseconds(double startedAt) => (Now - startedAt) * 1000d;

        internal static void Info(string message) => AlloyDebug.Log($"{Prefix} {message}");

        internal static void Step(string name, double startedAt) =>
            Info($"{name} 完成，耗时 {ElapsedMilliseconds(startedAt):F1} ms");

        internal static void Failure(string name, double startedAt) =>
            AlloyDebug.Error($"{Prefix} {name} 失败，耗时 {ElapsedMilliseconds(startedAt):F1} ms");
    }
}
