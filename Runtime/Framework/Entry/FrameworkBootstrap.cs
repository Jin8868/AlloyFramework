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
        private static readonly List<FrameworkSystem> InitializedSystems =
            new List<FrameworkSystem>();

        private static FrameworkContext _context;
        private static IGameEntry _gameEntry;
        private static CancellationTokenSource _startupCancellation;
        private static bool _isStarting;
        private static bool _isInitialized;
        private static bool _isShuttingDown;
        private static bool _isGameEntryStarted;

        public static bool IsStarting => _isStarting;

        public static bool IsInitialized => _isInitialized;

        public static event Action<Type, int, int> SystemInitializing;

        public static event Action Initialized;

        public static event Action<Exception> StartupFailed;

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

            _context = new FrameworkContext();
            _startupCancellation = new CancellationTokenSource();

            Application.quitting += HandleApplicationQuitting;
            UniTaskScheduler.UnobservedTaskException += HandleUnobservedTaskException;

            RunStartupAsync(_startupCancellation.Token).Forget(HandleStartupException);
        }

        public static void Shutdown()
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
                    _gameEntry.Shutdown();
                }
                catch (Exception exception)
                {
                    AlloyDebug.Error(exception);
                }

                _isGameEntryStarted = false;
            }

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

        private static async UniTask RunStartupAsync(CancellationToken cancellationToken)
        {
            _isStarting = true;

            try
            {
                var descriptors = DiscoverFrameworkSystems();

                for (var index = 0; index < descriptors.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var descriptor = descriptors[index];
                    SystemInitializing?.Invoke(
                        descriptor.SystemType,
                        index + 1,
                        descriptors.Count);

                    AlloyDebug.Log(
                        $"Initializing framework system: {descriptor.SystemType.FullName}");

                    var system = CreateSystem(descriptor.SystemType);
                    await system.InitializeAsync(_context, cancellationToken);

                    _context.Register(system);
                    InitializedSystems.Add(system);
                }

                _isInitialized = true;
                Initialized?.Invoke();
                AlloyDebug.Log("Framework startup completed.");

                if (_gameEntry != null)
                {
                    _isGameEntryStarted = true;
                    await _gameEntry.MainAsync(cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Shutdown();
            }
            catch (Exception exception)
            {
                Shutdown();
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
            Shutdown();
        }

        private static void HandleStartupException(Exception exception)
        {
            AlloyDebug.Error(exception);
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
}
