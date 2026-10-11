using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Net
{
    /// <summary>在 Model 之后初始化、Model 之前关闭的多会话网络管理器。</summary>
    [FrameworkSystem((FrameworkSystemPriority)2900)]
    public sealed class NetworkManager : FrameworkSystem, IUpdateable
    {
        private readonly List<NetworkSession> m_sessions = new List<NetworkSession>(); // 管理中的会话。
        private NetworkServiceRegistry m_services; // 项目安装的服务获取定义。
        private int m_threadID; // 框架主线程编号。
        private bool m_ready; // 管理器是否可用。

        /// <summary>框架创建的唯一管理器。</summary>
        public static NetworkManager Instance { get; private set; }
        /// <summary>只读会话快照，用于诊断。</summary>
        public IReadOnlyList<NetworkSession> Sessions => m_sessions.AsReadOnly();

        /// <summary>初始化主线程网络更新，不自动建立连接。</summary>
        /// <param name="context">框架上下文。</param>
        /// <param name="cancellationToken">启动取消令牌。</param>
        /// <returns>已完成任务。</returns>
        /// <exception cref="InvalidOperationException">已经初始化。</exception>
        public override UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Instance != null) throw new InvalidOperationException("网络管理器已经初始化。");
            // 在框架主线程建立唯一实例，再接入更新；此时不会连接远端。
            m_threadID = Thread.CurrentThread.ManagedThreadId;
            m_ready = true;
            Instance = this;
            GameLoop.Register(this);
            return UniTask.CompletedTask;
        }

        /// <summary>创建独立会话，管理器取得传输的所有权。</summary>
        /// <param name="name">唯一诊断名称。</param>
        /// <param name="transport">独占传输。</param>
        /// <param name="protocol">协议注册表。</param>
        /// <param name="settings">限制配置。</param>
        /// <returns>新会话。</returns>
        /// <exception cref="ArgumentException">名称或传输能力无效。</exception>
        /// <exception cref="InvalidOperationException">管理器不可用或名称重复。</exception>
        public NetworkSession CreateSession(string name, INetworkTransport transport,
            INetworkProtocol protocol, NetworkSettings settings)
        {
            EnsureReady();
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("会话名称不能为空。", nameof(name));
            foreach (var current in m_sessions)
                if (current.State != ENetworkSessionState.Disposed && current.Name == name)
                    throw new InvalidOperationException("会话名称重复。");
            // 只有成功创建后才取得管理权，构造失败时由调用方释放传输。
            var session = new NetworkSession(name, transport, protocol, settings);
            m_sessions.Add(session);
            return session;
        }

        /// <summary>派发消息并处理不受 timeScale 影响的超时。</summary>
        /// <param name="deltaTime">游戏帧时间。</param>
        /// <param name="unscaledDeltaTime">真实帧时间。</param>
        public void OnUpdate(float deltaTime, float unscaledDeltaTime)
        {
            if (!m_ready) return;
            // 使用快照，允许回调创建或释放其他会话。
            var sessions = m_sessions.ToArray();
            foreach (var session in sessions) session.Tick();
            for (int index = m_sessions.Count - 1; index >= 0; index--)
                if (m_sessions[index].State == ENetworkSessionState.Disposed) m_sessions.RemoveAt(index);
        }

        /// <summary>停止更新并结束全部请求，先于 ModelManager 释放。</summary>
        public override void Shutdown()
        {
            if (!m_ready) return;
            EnsureReady();
            // 先解除业务服务与推送订阅，再释放底层会话。
            m_services?.ReleaseServices();
            m_services = null;
            m_ready = false;
            GameLoop.Unregister(this);
            var sessions = m_sessions.ToArray();
            m_sessions.Clear();
            foreach (var session in sessions)
            {
                try { session.Dispose(); }
                catch (Exception exception) { AlloyDebug.Error(exception); }
            }
            if (ReferenceEquals(Instance, this)) Instance = null;
        }

        /// <summary>安装项目定义的网络服务注册表，不自动建立连接。</summary>
        /// <param name="services">项目提供的服务注册表。</param>
        /// <exception cref="ArgumentNullException">注册表为空。</exception>
        /// <exception cref="InvalidOperationException">管理器不可用或已安装其他注册表。</exception>
        public void SetServiceRegistry(NetworkServiceRegistry services)
        {
            EnsureReady();
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (m_services != null)
            {
                throw new InvalidOperationException("网络服务注册表已经安装。");
            }

            m_services = services;
        }

        /// <summary>获取项目注册的当前生命周期服务。</summary>
        /// <typeparam name="TService">服务的注册类型。</typeparam>
        /// <returns>当前生命周期的共享服务实例。</returns>
        /// <exception cref="InvalidOperationException">管理器不可用或服务未安装、未创建、未注册。</exception>
        public TService GetService<TService>() where TService : class
        {
            EnsureReady();
            if (m_services == null)
            {
                throw new InvalidOperationException("项目尚未安装网络服务注册表。");
            }

            return m_services.Get<TService>();
        }

        /// <summary>解除当前项目的服务注册表，其他注册表不会受到影响。</summary>
        /// <param name="services">需要解除的注册表。</param>
        /// <exception cref="InvalidOperationException">管理器不可用。</exception>
        public void RemoveServiceRegistry(NetworkServiceRegistry services)
        {
            EnsureReady();
            if (ReferenceEquals(m_services, services))
            {
                m_services.ReleaseServices();
                m_services = null;
            }
        }

        private void EnsureReady()
        {
            if (!m_ready || Thread.CurrentThread.ManagedThreadId != m_threadID)
                throw new InvalidOperationException("网络管理器仅在初始化后的主线程可用。");
        }
    }
}
