using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 在 Unity 主线程管理按类型唯一的 Model 实例及其生命周期。
    /// </summary>
    [FrameworkSystem(FrameworkSystemPriority.Model)]
    public sealed class ModelManager : FrameworkSystem
    {
        private readonly Dictionary<Type, ModelRegistration> m_registrations =
            new Dictionary<Type, ModelRegistration>(); // 注册类型及创建方式。
        private readonly List<ModelRegistration> m_created = new List<ModelRegistration>(); // 按初始化完成顺序记录实例。
        private bool m_isReady; // 是否允许业务访问。
        private int m_lifecycleDepth; // 正在执行的生命周期层数，禁止回调重入破坏管理集合。
        private int m_initializationDepth; // 初始化允许读取其他 Model，重置和销毁不允许创建新实例。

        internal ModelManager() { }

        /// <summary>当前框架管理器，框架尚未启动或关闭后为空。</summary>
        public static ModelManager Instance { get; private set; }

        /// <summary>初始化管理器，具体业务类型由业务启动管线注册。</summary>
        /// <param name="context">框架启动上下文。</param>
        /// <param name="cancellationToken">启动取消令牌。</param>
        /// <returns>已完成的初始化任务。</returns>
        /// <exception cref="InvalidOperationException">已有其他管理器实例。</exception>
        public override UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Instance != null || m_isReady)
            {
                throw new InvalidOperationException("ModelManager 已初始化。");
            }

            m_isReady = true;
            Instance = this;
            return UniTask.CompletedTask;
        }

        /// <summary>销毁全部 Model 并释放管理器；单个销毁失败不会阻断其他清理。</summary>
        /// <exception cref="AggregateException">一个或多个 Model 销毁失败。</exception>
        public override void Shutdown()
        {
            if (!m_isReady)
            {
                return;
            }

            EnsureMutable();
            try
            {
                Clear();
            }
            finally
            {
                m_isReady = false;
                if (ReferenceEquals(Instance, this))
                {
                    Instance = null;
                }
            }
        }

        /// <summary>注册类型，不立即创建实例；Model 需要公开的无参构造函数。</summary>
        /// <typeparam name="T">继承 ModelBase 的具体类型。</typeparam>
        /// <exception cref="InvalidOperationException">管理器不可用、生命周期重入或重复注册。</exception>
        public void Register<T>() where T : ModelBase, new()
        {
            EnsureMutable();
            var modelType = typeof(T);
            if (m_registrations.ContainsKey(modelType))
            {
                throw new InvalidOperationException($"Model 已重复注册：{modelType.FullName}。");
            }

            m_registrations.Add(modelType, new ModelRegistration<T>());
        }

        /// <summary>获取已注册类型的唯一实例，首次获取时创建并初始化。</summary>
        /// <typeparam name="T">已注册的具体类型。</typeparam>
        /// <returns>已初始化的 Model 实例。</returns>
        /// <exception cref="InvalidOperationException">管理器不可用、类型未注册或存在生命周期循环。</exception>
        public T Get<T>() where T : ModelBase
        {
            EnsureReady();
            var registration = GetRegistration<T>();
            if (registration.IsBusy)
            {
                throw new InvalidOperationException($"Model 存在循环初始化或生命周期重入：{typeof(T).FullName}。");
            }

            if (registration.Model != null)
            {
                return (T)registration.Model;
            }

            if (m_lifecycleDepth != m_initializationDepth)
            {
                throw new InvalidOperationException("Model 重置和销毁期间不能创建新的 Model 实例。");
            }

            registration.IsBusy = true;
            m_lifecycleDepth++;
            m_initializationDepth++;
            ModelBase model = null;
            try
            {
                // 完成初始化后才公开实例，避免业务读取半初始化的数据。
                model = registration.Create();
                model.Initialize();
                registration.Model = model;
                m_created.Add(registration);
                return (T)model;
            }
            catch (Exception initializationException)
            {
                // 初始化失败释放已建立的订阅和资源，保留注册以允许后续重试。
                try
                {
                    model?.Dispose();
                }
                catch (Exception disposalException)
                {
                    throw new AggregateException(initializationException, disposalException);
                }

                throw;
            }
            finally
            {
                registration.IsBusy = false;
                m_lifecycleDepth--;
                m_initializationDepth--;
            }
        }

        /// <summary>尝试读取已创建的实例，不触发创建。</summary>
        /// <typeparam name="T">具体 Model 类型。</typeparam>
        /// <param name="model">已创建实例；不存在或正在执行生命周期时为空。</param>
        /// <returns>是否找到可以使用的实例。</returns>
        /// <exception cref="InvalidOperationException">管理器不可用。</exception>
        public bool TryGet<T>(out T model) where T : ModelBase
        {
            EnsureReady();
            model = null;
            if (!m_registrations.TryGetValue(typeof(T), out var registration) || registration.IsBusy)
            {
                return false;
            }

            model = registration.Model as T;
            return model != null;
        }

        /// <summary>重置指定类型的已创建实例，不创建新实例。</summary>
        /// <typeparam name="T">已注册的具体类型。</typeparam>
        /// <exception cref="InvalidOperationException">管理器不可用、生命周期重入或类型未注册。</exception>
        public void Reset<T>() where T : ModelBase
        {
            EnsureMutable();
            ResetRegistration(GetRegistration<T>());
        }

        /// <summary>按初始化完成顺序重置所有已创建实例，保留注册和实例。</summary>
        /// <exception cref="InvalidOperationException">管理器不可用或生命周期重入。</exception>
        /// <exception cref="AggregateException">一个或多个 Model 重置失败。</exception>
        public void ResetAll()
        {
            EnsureMutable();
            ProcessAll(false);
        }

        /// <summary>销毁指定类型的实例，保留注册；再次 Get 时重新创建。</summary>
        /// <typeparam name="T">已注册的具体类型。</typeparam>
        /// <exception cref="InvalidOperationException">管理器不可用、生命周期重入或类型未注册。</exception>
        public void Remove<T>() where T : ModelBase
        {
            EnsureMutable();
            var registration = GetRegistration<T>();
            m_created.Remove(registration);
            DisposeRegistration(registration);
        }

        /// <summary>按初始化完成顺序逆序销毁所有实例，并清除所有注册。</summary>
        /// <exception cref="InvalidOperationException">管理器不可用或生命周期重入。</exception>
        /// <exception cref="AggregateException">一个或多个 Model 销毁失败。</exception>
        public void Clear()
        {
            EnsureMutable();
            try
            {
                ProcessAll(true);
            }
            finally
            {
                m_created.Clear();
                m_registrations.Clear();
            }
        }

        private void ProcessAll(bool dispose)
        {
            // 生命周期期间禁止创建新实例；快照使失败处理不影响其他实例。
            var registrations = m_created.ToArray();
            var errors = new List<Exception>();
            m_lifecycleDepth++;
            try
            {
                for (var index = 0; index < registrations.Length; index++)
                {
                    var registration = registrations[dispose ? registrations.Length - index - 1 : index];
                    try
                    {
                        if (dispose)
                        {
                            DisposeRegistration(registration);
                        }
                        else
                        {
                            ResetRegistration(registration);
                        }
                    }
                    catch (Exception exception)
                    {
                        errors.Add(exception);
                    }
                }
            }
            finally
            {
                m_lifecycleDepth--;
            }

            if (errors.Count != 0)
            {
                throw new AggregateException("Model 批量生命周期处理失败。", errors);
            }
        }

        private void ResetRegistration(ModelRegistration registration)
        {
            if (registration.Model == null)
            {
                return;
            }

            registration.IsBusy = true;
            m_lifecycleDepth++;
            try
            {
                registration.Model.Reset();
            }
            finally
            {
                registration.IsBusy = false;
                m_lifecycleDepth--;
            }
        }

        private void DisposeRegistration(ModelRegistration registration)
        {
            var model = registration.Model;
            registration.Model = null;
            if (model == null)
            {
                return;
            }

            registration.IsBusy = true;
            m_lifecycleDepth++;
            try
            {
                model.Dispose();
            }
            finally
            {
                registration.IsBusy = false;
                m_lifecycleDepth--;
            }
        }

        private ModelRegistration GetRegistration<T>() where T : ModelBase
        {
            if (!m_registrations.TryGetValue(typeof(T), out var registration))
            {
                throw new InvalidOperationException(
                    $"Model 未注册：{typeof(T).FullName}，请在业务集中注册函数中添加注册。");
            }

            return registration;
        }

        private void EnsureReady()
        {
            if (!m_isReady)
            {
                throw new InvalidOperationException("ModelManager 尚未初始化或已经关闭。");
            }
        }

        private void EnsureMutable()
        {
            EnsureReady();
            if (m_lifecycleDepth != 0)
            {
                throw new InvalidOperationException("Model 生命周期回调中不能注册、重置、移除或清空 Model。");
            }
        }

        private abstract class ModelRegistration
        {
            internal ModelBase Model; // 当前已初始化实例。
            internal bool IsBusy; // 当前类型是否正在执行生命周期。

            internal abstract ModelBase Create();
        }

        private sealed class ModelRegistration<T> : ModelRegistration where T : ModelBase, new()
        {
            internal override ModelBase Create() => new T();
        }
    }
}
