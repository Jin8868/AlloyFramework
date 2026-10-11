using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Net.Tcp
{
    /// <summary>使用独立连接状态隔离迟到工作线程的有界 TCP 传输。</summary>
    public sealed class TcpNetworkTransport : INetworkTransport
    {
        private readonly NetworkSettings m_settings; // 独立限制快照。
        private Connection m_connection; // 当前连接代次。
        private bool m_disposed; // 生命周期终止标志。

        /// <summary>创建 TCP 传输。</summary>
        /// <param name="settings">资源限制。</param>
        public TcpNetworkTransport(NetworkSettings settings)
        {
            m_settings = (settings ?? throw new ArgumentNullException(nameof(settings))).Snapshot();
        }

        /// <summary>TCP 提供可靠字节流。</summary>
        public bool IsReliable => true;
        /// <summary>TCP 提供有序字节流。</summary>
        public bool IsOrdered => true;
        /// <summary>当前连接错误。</summary>
        public Exception Failure => m_connection?.Failure;
        /// <summary>接收积压。</summary>
        public int ReceiveQueueCount => m_connection?.ReceiveCount ?? 0;
        /// <summary>发送积压。</summary>
        public int SendQueueCount => m_connection?.SendCount ?? 0;

        /// <summary>连接并启动串行收发线程。</summary>
        /// <param name="host">远端地址。</param>
        /// <param name="port">远端端口。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接任务。</returns>
        /// <exception cref="NetworkException">连接失败或传输已释放。</exception>
        public async UniTask ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            throw new NetworkException(ENetworkError.Connection, "WebGL 不支持此原始 TCP 传输。");
#else
            if (m_disposed)
                throw new NetworkException(ENetworkError.Disposed, "传输已释放。");
            Disconnect();
            var connection = new Connection(m_settings);
            m_connection = connection;
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(m_settings.ConnectTimeoutSeconds));
                using (timeout.Token.Register(CloseConnecting, connection))
                {
                    try
                    {
                        await connection.Client.ConnectAsync(host, port);
                        timeout.Token.ThrowIfCancellationRequested();
                        if (m_disposed || !ReferenceEquals(m_connection, connection))
                            throw new OperationCanceledException("连接代次已失效。");
                        connection.Start();
                    }
                    catch (Exception exception)
                    {
                        connection.Close(exception);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new NetworkException(ENetworkError.Connection,
                            timeout.IsCancellationRequested ? "连接超时。" : "TCP 连接失败。", exception);
                    }
                }
            }
#endif
        }

        /// <summary>将编码后的独占帧入队。</summary>
        /// <param name="frame">完整帧。</param>
        /// <exception cref="NetworkException">未连接或队列已满。</exception>
        public void Send(NetworkFrame frame)
        {
            if (m_disposed || m_connection == null)
                throw new NetworkException(ENetworkError.Connection, "传输未连接。");
            m_connection.Enqueue(NetworkWireCodec.Encode(frame,
                m_settings.ProtocolVersion, m_settings.MaxFrameBytes));
        }

        /// <summary>取得接收队列中的独占帧。</summary>
        /// <param name="frame">完整帧。</param>
        /// <returns>是否取得。</returns>
        public bool TryReceive(out NetworkFrame frame)
        {
            frame = null;
            return m_connection != null && m_connection.TryReceive(out frame);
        }

        /// <summary>结束当前连接代次。</summary>
        public void Disconnect()
        {
            var connection = m_connection;
            m_connection = null;
            connection?.Close(null);
        }

        /// <summary>永久结束传输生命周期。</summary>
        public void Dispose()
        {
            m_disposed = true;
            Disconnect();
        }

        private static void CloseConnecting(object state)
        {
            ((Connection)state).Close(new OperationCanceledException("连接被取消。"));
        }

        private sealed class Connection
        {
            private readonly object m_gate = new object(); // 队列与关闭状态锁。
            private readonly NetworkSettings m_settings; // 不变限制。
            private readonly Queue<byte[]> m_sendQueue = new Queue<byte[]>(); // 独占待发送字节。
            private readonly Queue<NetworkFrame> m_receiveQueue = new Queue<NetworkFrame>(); // 独占接收帧。
            private readonly TcpClient m_client = new TcpClient(); // 本代次独占 socket。
            private int m_sendBytes; // 发送积压字节数。
            private int m_receiveBytes; // 接收积压字节数。
            private bool m_closed; // 关闭后不能再次入队。
            private Exception m_failure; // 第一次故障。

            internal Connection(NetworkSettings settings) { m_settings = settings; }
            internal TcpClient Client => m_client;
            internal Exception Failure { get { lock (m_gate) return m_failure; } }
            internal int ReceiveCount { get { lock (m_gate) return m_receiveQueue.Count; } }
            internal int SendCount { get { lock (m_gate) return m_sendQueue.Count; } }

            internal void Start()
            {
                lock (m_gate)
                {
                    if (m_closed) throw new IOException("连接已关闭。");
                    m_client.NoDelay = true;
                    new Thread(ReceiveLoop) { IsBackground = true, Name = "Alloy TCP Receive" }.Start();
                    new Thread(SendLoop) { IsBackground = true, Name = "Alloy TCP Send" }.Start();
                }
            }

            internal void Enqueue(byte[] bytes)
            {
                lock (m_gate)
                {
                    if (m_closed) throw new NetworkException(ENetworkError.Connection, "TCP 已关闭。", m_failure);
                    if (m_sendQueue.Count >= m_settings.MaxQueueCount ||
                        bytes.Length > m_settings.MaxQueueBytes - m_sendBytes)
                        throw new NetworkException(ENetworkError.Capacity, "发送队列已满。");
                    m_sendQueue.Enqueue(bytes);
                    m_sendBytes += bytes.Length;
                    Monitor.PulseAll(m_gate);
                }
            }

            internal bool TryReceive(out NetworkFrame frame)
            {
                lock (m_gate)
                {
                    frame = null;
                    if (m_receiveQueue.Count == 0) return false;
                    frame = m_receiveQueue.Dequeue();
                    m_receiveBytes -= NetworkWireCodec.HEADERSIZE + frame.Payload.Length;
                    return true;
                }
            }

            internal void Close(Exception exception)
            {
                lock (m_gate)
                {
                    if (m_closed) return;
                    m_closed = true;
                    m_failure = exception;
                    m_sendQueue.Clear();
                    m_receiveQueue.Clear();
                    m_sendBytes = 0;
                    m_receiveBytes = 0;
                    Monitor.PulseAll(m_gate);
                }
                m_client.Close();
            }

            private void SendLoop()
            {
                try
                {
                    while (true)
                    {
                        byte[] bytes;
                        lock (m_gate)
                        {
                            while (!m_closed && m_sendQueue.Count == 0) Monitor.Wait(m_gate);
                            if (m_closed) return;
                            bytes = m_sendQueue.Dequeue();
                            m_sendBytes -= bytes.Length;
                        }
                        // 当前帧必须完整发送后才允许下一帧，防止并发交错。
                        int offset = 0;
                        while (offset < bytes.Length)
                        {
                            int sent = m_client.Client.Send(bytes, offset, bytes.Length - offset, SocketFlags.None);
                            if (sent <= 0) throw new IOException("TCP 发送返回零。");
                            offset += sent;
                        }
                    }
                }
                catch (Exception exception) { Close(exception); }
            }

            private void ReceiveLoop()
            {
                try
                {
                    var prefix = new byte[4];
                    while (true)
                    {
                        // 先读四字节前缀并限制长度，再分配完整帧；半包和粘包由精确读取处理。
                        ReceiveExact(prefix, 0, prefix.Length);
                        int length = NetworkWireCodec.ReadLength(prefix, m_settings.MaxFrameBytes);
                        var bytes = new byte[length];
                        Buffer.BlockCopy(prefix, 0, bytes, 0, prefix.Length);
                        ReceiveExact(bytes, prefix.Length, length - prefix.Length);
                        var frame = NetworkWireCodec.Decode(bytes,
                            m_settings.ProtocolVersion, m_settings.MaxFrameBytes);
                        lock (m_gate)
                        {
                            if (m_closed) return;
                            if (m_receiveQueue.Count >= m_settings.MaxQueueCount ||
                                length > m_settings.MaxQueueBytes - m_receiveBytes)
                                throw new NetworkException(ENetworkError.Capacity, "接收队列已满。");
                            m_receiveQueue.Enqueue(frame);
                            m_receiveBytes += length;
                        }
                    }
                }
                catch (Exception exception) { Close(exception); }
            }

            private void ReceiveExact(byte[] bytes, int offset, int count)
            {
                while (count > 0)
                {
                    int received = m_client.Client.Receive(bytes, offset, count, SocketFlags.None);
                    if (received == 0) throw new IOException("远端关闭 TCP。");
                    offset += received;
                    count -= received;
                }
            }
        }
    }
}
