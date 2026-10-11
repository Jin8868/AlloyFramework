using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Net
{
    /// <summary>主线程会话，关联请求、响应、推送与连接代次。</summary>
    public sealed class NetworkSession : IDisposable
    {
        private readonly INetworkTransport m_transport; // 当前会话独占传输。
        private readonly INetworkProtocol m_protocol; // 不变协议注册表。
        private readonly NetworkSettings m_settings; // 会话配置快照。
        private readonly int m_threadID; // 创建时的主线程编号。
        private readonly Dictionary<ulong, PendingRequest> m_pending =
            new Dictionary<ulong, PendingRequest>(); // 未完成请求。
        private readonly Dictionary<uint, HandlerRegistration> m_handlers =
            new Dictionary<uint, HandlerRegistration>(); // 推送注册。
        private readonly List<ulong> m_expired = new List<ulong>(); // 本帧取消与超时快照。
        private ulong m_nextRequestID; // 生命周期内不重复的关联编号。
        private int m_connectionGeneration; // 迟到连接结果隔离。
        private long m_nextHeartbeat; // 下一次心跳的单调时间。
        private Action<NetworkSession> m_heartbeat; // 业务提供的命名心跳方法。
        private double m_heartbeatSeconds; // 心跳周期。

        internal NetworkSession(string name, INetworkTransport transport,
            INetworkProtocol protocol, NetworkSettings settings)
        {
            Name = name;
            m_transport = transport ?? throw new ArgumentNullException(nameof(transport));
            m_protocol = protocol ?? throw new ArgumentNullException(nameof(protocol));
            m_settings = settings.Snapshot();
            m_threadID = Thread.CurrentThread.ManagedThreadId;
            if (!transport.IsReliable || !transport.IsOrdered)
                throw new ArgumentException("首版 RPC 会话要求可靠有序传输。", nameof(transport));
        }

        /// <summary>诊断名称。</summary>
        public string Name { get; }
        /// <summary>当前状态。</summary>
        public ENetworkSessionState State { get; private set; }
        /// <summary>待请求数量。</summary>
        public int PendingCount => m_pending.Count;
        /// <summary>接收积压。</summary>
        public int ReceiveQueueCount => m_transport.ReceiveQueueCount;
        /// <summary>发送积压。</summary>
        public int SendQueueCount => m_transport.SendQueueCount;
        /// <summary>累计发送字节，包含包头。</summary>
        public long SentBytes { get; private set; }
        /// <summary>累计接收字节，包含包头。</summary>
        public long ReceivedBytes { get; private set; }
        /// <summary>累计超时次数。</summary>
        public long TimeoutCount { get; private set; }
        /// <summary>最近成功 RPC 往返毫秒数。</summary>
        public double LastRoundTripMilliseconds { get; private set; }
        /// <summary>最近错误；不保存消息内容。</summary>
        public Exception LastError { get; private set; }
        /// <summary>主线程状态通知。</summary>
        public event Action<NetworkSession> StateChanged;

        /// <summary>连接远端；成功只代表传输可用。</summary>
        /// <param name="host">地址。</param>
        /// <param name="port">端口。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接任务。</returns>
        /// <exception cref="ArgumentException">地址或端口无效。</exception>
        /// <exception cref="InvalidOperationException">重复连接或非主线程调用。</exception>
        /// <exception cref="NetworkException">传输失败或会话释放。</exception>
        public async UniTask ConnectAsync(string host, int port, CancellationToken cancellationToken = default)
        {
            EnsureAlive();
            if (State != ENetworkSessionState.Disconnected)
                throw new InvalidOperationException("会话正在连接或已经连接。");
            if (string.IsNullOrWhiteSpace(host) || port < 1 || port > 65535)
                throw new ArgumentException("远端地址或端口无效。");
            cancellationToken.ThrowIfCancellationRequested();
            int generation = ++m_connectionGeneration;
            SetState(ENetworkSessionState.Connecting);
            Exception failure = null;
            try { await m_transport.ConnectAsync(host, port, cancellationToken); }
            catch (Exception exception) { failure = exception; }
            await UniTask.SwitchToMainThread();
            if (generation != m_connectionGeneration || State == ENetworkSessionState.Disposed)
                throw new OperationCanceledException("连接代次已失效。");
            if (failure != null)
            {
                LastError = failure;
                m_transport.Disconnect();
                SetState(ENetworkSessionState.Disconnected);
                throw failure;
            }
            m_nextHeartbeat = Stopwatch.GetTimestamp();
            SetState(ENetworkSessionState.Connected);
        }

        /// <summary>回调式连接；回调在主线程完成一次。</summary>
        /// <param name="host">地址。</param>
        /// <param name="port">端口。</param>
        /// <param name="onCompleted">完成回调，空错误表示成功。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        public void Connect(string host, int port, Action<Exception> onCompleted,
            CancellationToken cancellationToken = default)
        {
            ConnectCallbackAsync(host, port, onCompleted, cancellationToken).Forget(ReportCallbackError);
        }

        /// <summary>发送无响应消息。</summary>
        /// <param name="messageID">注册的单向消息编号。</param>
        /// <param name="message">消息对象。</param>
        /// <exception cref="NetworkException">会话不可用或消息用途错误。</exception>
        public void Send(uint messageID, object message)
        {
            EnsureConnected();
            if (m_protocol.GetKind(messageID) != ENetworkMessageKind.OneWay)
                throw new NetworkException(ENetworkError.Protocol, "Send 仅支持单向消息。");
            SendFrame(new NetworkFrame(messageID, 0, ENetworkMessageKind.OneWay,
                m_protocol.Encode(messageID, message)));
        }

        /// <summary>等待类型明确的 RPC 响应；网络成功不代表业务成功。</summary>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="requestID">请求消息编号。</param>
        /// <param name="responseID">预期响应编号。</param>
        /// <param name="request">请求对象。</param>
        /// <param name="cancellationToken">仅取消本次请求。</param>
        /// <param name="options">可选超时。</param>
        /// <returns>响应对象。</returns>
        /// <exception cref="InvalidOperationException">调用线程不是会话创建线程。</exception>
        public UniTask<TResponse> RequestAsync<TResponse>(uint requestID, uint responseID, object request,
            CancellationToken cancellationToken = default, NetworkRequestOptions options = null)
        {
            var pending = new PendingRequest<TResponse>(null);
            BeginRequest(requestID, responseID, request, pending, cancellationToken, options);
            return pending.Task;
        }

        /// <summary>回调式 RPC，成功、取消、超时、断线仅产生一个最终结果。</summary>
        /// <typeparam name="TResponse">响应类型。</typeparam>
        /// <param name="requestID">请求编号。</param>
        /// <param name="responseID">预期响应编号。</param>
        /// <param name="request">请求对象。</param>
        /// <param name="onCompleted">响应与错误回调。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <param name="options">可选超时。</param>
        /// <exception cref="ArgumentNullException">回调为空。</exception>
        /// <exception cref="InvalidOperationException">调用线程不是会话创建线程。</exception>
        public void Request<TResponse>(uint requestID, uint responseID, object request,
            Action<TResponse, Exception> onCompleted, CancellationToken cancellationToken = default,
            NetworkRequestOptions options = null)
        {
            EnsureThread();
            if (onCompleted == null) throw new ArgumentNullException(nameof(onCompleted));
            var pending = new PendingRequest<TResponse>(onCompleted);
            BeginRequest(requestID, responseID, request, pending, cancellationToken, options);
        }

        /// <summary>安装一个推送或单向消息处理器。</summary>
        /// <typeparam name="TMessage">消息类型。</typeparam>
        /// <param name="messageID">消息编号。</param>
        /// <param name="handler">命名处理方法。</param>
        /// <returns>显式解除注册的句柄。</returns>
        /// <exception cref="InvalidOperationException">重复注册或用途无效。</exception>
        public IDisposable RegisterHandler<TMessage>(uint messageID, Action<TMessage> handler)
        {
            EnsureAlive();
            var kind = m_protocol.GetKind(messageID);
            if ((kind != ENetworkMessageKind.Push && kind != ENetworkMessageKind.OneWay) ||
                m_handlers.ContainsKey(messageID) || handler == null)
                throw new InvalidOperationException("推送用途、处理器或重复注册无效。");
            var registration = new HandlerRegistration<TMessage>(this, messageID, handler);
            m_handlers.Add(messageID, registration);
            return registration;
        }

        /// <summary>配置主线程心跳调度；具体消息与在线语义由业务决定。</summary>
        /// <param name="intervalSeconds">单调时间周期。</param>
        /// <param name="heartbeat">命名心跳方法；空值停用。</param>
        /// <exception cref="ArgumentOutOfRangeException">启用心跳的周期无效。</exception>
        /// <exception cref="NetworkException">会话已释放。</exception>
        public void ConfigureHeartbeat(double intervalSeconds, Action<NetworkSession> heartbeat)
        {
            EnsureAlive();
            if (heartbeat != null && (!NetworkSettings.IsPositiveFinite(intervalSeconds) || intervalSeconds > 86400))
                throw new ArgumentOutOfRangeException(nameof(intervalSeconds));
            m_heartbeatSeconds = intervalSeconds;
            m_heartbeat = heartbeat;
            m_nextHeartbeat = Stopwatch.GetTimestamp();
        }

        /// <summary>断开并结束全部待请求，允许以后重新连接。</summary>
        /// <exception cref="InvalidOperationException">调用线程不是会话创建线程。</exception>
        public void Disconnect()
        {
            EnsureThread();
            if (State == ENetworkSessionState.Disposed) return;
            DisconnectCore(new NetworkException(ENetworkError.Connection, "会话已断开。"), false);
        }

        /// <summary>永久释放队列、处理器、待请求与传输。</summary>
        /// <exception cref="InvalidOperationException">调用线程不是会话创建线程。</exception>
        public void Dispose()
        {
            EnsureThread();
            if (State == ENetworkSessionState.Disposed) return;
            m_handlers.Clear();
            m_heartbeat = null;
            DisconnectCore(new NetworkException(ENetworkError.Disposed, "会话已释放。"), true);
            StateChanged = null;
            m_transport.Dispose();
        }

        internal void Tick()
        {
            if (State != ENetworkSessionState.Connected) return;
            if (m_transport.Failure != null)
            {
                Exception failure = m_transport.Failure;
                var kind = failure is InvalidDataException ? ENetworkError.Protocol : ENetworkError.Connection;
                DisconnectCore(failure as NetworkException ??
                    new NetworkException(kind, "传输中断。", failure), false);
                return;
            }

            // 先处理取消与超时，再接受本帧响应，确保已取消请求不能写回业务。
            long now = Stopwatch.GetTimestamp();
            m_expired.Clear();
            foreach (var pair in m_pending)
                if (pair.Value.Token.IsCancellationRequested || now >= pair.Value.Deadline)
                    m_expired.Add(pair.Key);
            foreach (ulong requestID in m_expired)
            {
                if (!m_pending.TryGetValue(requestID, out var pending)) continue;
                m_pending.Remove(requestID);
                Exception failure;
                if (pending.Token.IsCancellationRequested)
                    failure = new OperationCanceledException(pending.Token);
                else
                {
                    TimeoutCount++;
                    failure = new NetworkException(ENetworkError.Timeout, "RPC 超时，远端执行结果未知。");
                }
                Complete(pending, null, failure);
            }

            for (int index = 0; index < m_settings.DispatchBudget &&
                State == ENetworkSessionState.Connected && m_transport.TryReceive(out var frame); index++)
            {
                ReceivedBytes += NetworkWireCodec.HEADERSIZE + frame.Payload.Length;
                try { Dispatch(frame); }
                catch (Exception exception)
                {
                    DisconnectCore(new NetworkException(ENetworkError.Protocol, "帧派发失败。", exception), false);
                }
            }
            if (State == ENetworkSessionState.Connected && m_heartbeat != null && now >= m_nextHeartbeat)
            {
                m_nextHeartbeat = now + ToTicks(m_heartbeatSeconds);
                try { m_heartbeat(this); }
                catch (Exception exception) { ReportCallbackError(exception); }
            }
        }

        private void BeginRequest(uint requestID, uint responseID, object request, PendingRequest pending,
            CancellationToken token, NetworkRequestOptions options)
        {
            EnsureThread();
            ulong correlation = 0;
            try
            {
                EnsureConnected();
                token.ThrowIfCancellationRequested();
                double timeout = options == null || options.TimeoutSeconds == 0
                    ? m_settings.RequestTimeoutSeconds : options.TimeoutSeconds;
                if (!NetworkSettings.IsPositiveFinite(timeout) || timeout > 86400)
                    throw new ArgumentOutOfRangeException(nameof(options), "超时必须在零至一天之间。");
                if (m_pending.Count >= m_settings.MaxPendingRequests)
                    throw new NetworkException(ENetworkError.Capacity, "待请求表已满。");
                if (m_protocol.GetKind(requestID) != ENetworkMessageKind.Request ||
                    m_protocol.GetKind(responseID) != ENetworkMessageKind.Response)
                    throw new NetworkException(ENetworkError.Protocol, "RPC 请求响应用途错误。");
                if (m_nextRequestID == ulong.MaxValue)
                    throw new NetworkException(ENetworkError.Capacity, "请求关联编号耗尽，请创建新会话。");
                var payload = m_protocol.Encode(requestID, request);
                correlation = ++m_nextRequestID;
                pending.ResponseID = responseID;
                pending.RequestMessageID = requestID;
                pending.Token = token;
                pending.Started = Stopwatch.GetTimestamp();
                pending.Deadline = pending.Started + ToTicks(timeout);
                m_pending.Add(correlation, pending);
                SendFrame(new NetworkFrame(requestID, correlation, ENetworkMessageKind.Request, payload));
            }
            catch (Exception exception)
            {
                if (correlation != 0) m_pending.Remove(correlation);
                Complete(pending, null, exception);
            }
        }

        private void Dispatch(NetworkFrame frame)
        {
            if (frame.Kind == ENetworkMessageKind.Response || frame.Kind == ENetworkMessageKind.Error)
            {
                // 超时、取消或旧代次的迟到响应不重新完成任务。
                if (!m_pending.TryGetValue(frame.RequestID, out var pending)) return;
                if (frame.Kind == ENetworkMessageKind.Response &&
                    (frame.MessageID != pending.ResponseID || m_protocol.GetKind(frame.MessageID) != frame.Kind))
                    throw new InvalidDataException("响应编号与请求不匹配。");
                if (frame.Kind == ENetworkMessageKind.Error && frame.MessageID != pending.RequestMessageID)
                    throw new InvalidDataException("错误帧没有回显原请求消息编号。");
                m_pending.Remove(frame.RequestID);
                if (pending.Token.IsCancellationRequested)
                {
                    Complete(pending, null, new OperationCanceledException(pending.Token));
                    return;
                }
                if (frame.Kind == ENetworkMessageKind.Error)
                {
                    Complete(pending, null, new NetworkException(ENetworkError.Remote,
                        Encoding.UTF8.GetString(frame.Payload), remoteCode: frame.ErrorCode));
                    return;
                }
                object response;
                try { response = m_protocol.Decode(frame.MessageID, frame.Payload); }
                catch (Exception exception)
                {
                    Complete(pending, null, new NetworkException(ENetworkError.Protocol, "响应解析失败。", exception));
                    throw;
                }
                LastRoundTripMilliseconds = (Stopwatch.GetTimestamp() - pending.Started) * 1000d /
                    Stopwatch.Frequency;
                Complete(pending, response, null);
                return;
            }
            if ((frame.Kind != ENetworkMessageKind.Push && frame.Kind != ENetworkMessageKind.OneWay) ||
                m_protocol.GetKind(frame.MessageID) != frame.Kind)
                throw new InvalidDataException("非预期消息用途。");
            var message = m_protocol.Decode(frame.MessageID, frame.Payload);
            if (m_handlers.TryGetValue(frame.MessageID, out var handler))
            {
                try { handler.Invoke(message); }
                catch (Exception exception) { ReportCallbackError(exception); }
            }
        }

        private void DisconnectCore(Exception failure, bool disposed)
        {
            ++m_connectionGeneration;
            LastError = failure;
            m_transport.Disconnect();
            // 先清空请求表并改变状态，再完成任务，防止业务重入破坏集合。
            var pending = new List<PendingRequest>(m_pending.Values);
            m_pending.Clear();
            State = disposed ? ENetworkSessionState.Disposed : ENetworkSessionState.Disconnected;
            foreach (var request in pending) Complete(request, null, failure);
            NotifyStateChanged();
        }

        private void Complete(PendingRequest pending, object response, Exception failure)
        {
            if (failure != null) LastError = failure;
            try { pending.Complete(response, failure); }
            catch (Exception exception) { ReportCallbackError(exception); }
        }

        private void SendFrame(NetworkFrame frame)
        {
            m_transport.Send(frame);
            SentBytes += NetworkWireCodec.HEADERSIZE + frame.Payload.Length;
        }

        private void SetState(ENetworkSessionState state)
        {
            State = state;
            NotifyStateChanged();
        }

        private void NotifyStateChanged()
        {
            try { StateChanged?.Invoke(this); }
            catch (Exception exception) { ReportCallbackError(exception); }
        }

        private async UniTask ConnectCallbackAsync(string host, int port, Action<Exception> callback,
            CancellationToken token)
        {
            Exception failure = null;
            try { await ConnectAsync(host, port, token); }
            catch (Exception exception) { failure = exception; }
            await UniTask.SwitchToMainThread();
            callback?.Invoke(failure);
        }

        private void EnsureThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != m_threadID)
                throw new InvalidOperationException("会话必须在创建线程调用。");
        }

        private void EnsureAlive()
        {
            EnsureThread();
            if (State == ENetworkSessionState.Disposed)
                throw new NetworkException(ENetworkError.Disposed, "会话已释放。");
        }

        private void EnsureConnected()
        {
            EnsureAlive();
            if (State != ENetworkSessionState.Connected)
                throw new NetworkException(ENetworkError.Connection, "会话尚未连接。");
        }

        private static long ToTicks(double seconds) => (long)(seconds * Stopwatch.Frequency);
        private static void ReportCallbackError(Exception exception) => AlloyDebug.Error(exception);

        private abstract class PendingRequest
        {
            internal uint RequestMessageID { get; set; }
            internal uint ResponseID { get; set; }
            internal long Started { get; set; }
            internal long Deadline { get; set; }
            internal CancellationToken Token { get; set; }
            internal abstract void Complete(object response, Exception failure);
        }

        private sealed class PendingRequest<TResponse> : PendingRequest
        {
            private readonly UniTaskCompletionSource<TResponse> m_completion; // 异步请求的单次完成源。
            private readonly Action<TResponse, Exception> m_callback; // 回调请求的命名委托。
            private bool m_completed; // 重入时仍只能完成一次。

            internal PendingRequest(Action<TResponse, Exception> callback)
            {
                m_callback = callback;
                if (callback == null) m_completion = new UniTaskCompletionSource<TResponse>();
            }
            internal UniTask<TResponse> Task => m_completion.Task;
            internal override void Complete(object response, Exception failure)
            {
                if (m_completed) return;
                m_completed = true;
                TResponse result = default;
                if (failure == null && !(response is TResponse))
                    failure = new NetworkException(ENetworkError.Protocol, "响应对象类型不匹配。");
                if (failure == null) result = (TResponse)response;
                if (m_callback != null) { m_callback(result, failure); return; }
                if (failure is OperationCanceledException canceled)
                    m_completion.TrySetCanceled(canceled.CancellationToken);
                else if (failure != null) m_completion.TrySetException(failure);
                else m_completion.TrySetResult(result);
            }
        }

        private abstract class HandlerRegistration : IDisposable
        {
            private NetworkSession m_owner; // 所属会话，解除后清空。
            private readonly uint m_messageID; // 注册编号。
            protected HandlerRegistration(NetworkSession owner, uint messageID)
            {
                m_owner = owner;
                m_messageID = messageID;
            }
            internal abstract void Invoke(object message);
            /// <summary>解除处理器注册。</summary>
            public void Dispose()
            {
                if (m_owner == null) return;
                m_owner.EnsureThread();
                if (m_owner.m_handlers.TryGetValue(m_messageID, out var current) && ReferenceEquals(current, this))
                    m_owner.m_handlers.Remove(m_messageID);
                m_owner = null;
            }
        }

        private sealed class HandlerRegistration<TMessage> : HandlerRegistration
        {
            private readonly Action<TMessage> m_handler; // 强类型业务处理方法。
            internal HandlerRegistration(NetworkSession owner, uint messageID, Action<TMessage> handler)
                : base(owner, messageID) { m_handler = handler; }
            internal override void Invoke(object message) => m_handler((TMessage)message);
        }
    }
}
