using System;
using System.Threading;
#if UNITY_5_3_OR_NEWER
using Cysharp.Threading.Tasks;
#endif

namespace AlloyFramework.Net
{
    /// <summary>线上帧用途。</summary>
    public enum ENetworkMessageKind : byte
    {
        /// <summary>需要对应响应的请求。</summary>
        Request = 1,
        /// <summary>与关联编号对应的响应。</summary>
        Response = 2,
        /// <summary>服务端主动推送。</summary>
        Push = 3,
        /// <summary>不等待响应的单向消息。</summary>
        OneWay = 4,
        /// <summary>关联请求的网络错误帧。</summary>
        Error = 5
    }

    /// <summary>会话的连接生命周期。</summary>
    public enum ENetworkSessionState
    {
        /// <summary>未建立连接。</summary>
        Disconnected,
        /// <summary>正在建立连接。</summary>
        Connecting,
        /// <summary>传输可用，不代表认证完成。</summary>
        Connected,
        /// <summary>生命周期已经永久结束。</summary>
        Disposed
    }

    /// <summary>网络失败分类；业务拒绝由具体响应表示。</summary>
    public enum ENetworkError
    {
        /// <summary>连接建立或维持失败。</summary>
        Connection,
        /// <summary>线上契约或消息类型无效。</summary>
        Protocol,
        /// <summary>远端明确返回网络错误。</summary>
        Remote,
        /// <summary>超过本地等待时限，远端执行结果未知。</summary>
        Timeout,
        /// <summary>资源上限已经达到。</summary>
        Capacity,
        /// <summary>生命周期已经结束。</summary>
        Disposed
    }

    /// <summary>保留网络错误种类与诊断信息。</summary>
    public sealed class NetworkException : Exception
    {
        /// <summary>错误分类。</summary>
        public ENetworkError Kind { get; }
        /// <summary>远端错误编号；本地错误为零。</summary>
        public int RemoteCode { get; }

        /// <summary>创建网络异常。</summary>
        /// <param name="kind">错误分类。</param>
        /// <param name="message">诊断信息。</param>
        /// <param name="innerException">底层错误。</param>
        /// <param name="remoteCode">远端错误编号。</param>
        public NetworkException(ENetworkError kind, string message,
            Exception innerException = null, int remoteCode = 0) : base(message, innerException)
        {
            Kind = kind;
            RemoteCode = remoteCode;
        }
    }

    /// <summary>完整帧；Payload 由持有者独占，不使用借用池内存。</summary>
    public sealed class NetworkFrame
    {
        /// <summary>消息编号。</summary>
        public uint MessageID { get; }
        /// <summary>请求关联编号。</summary>
        public ulong RequestID { get; }
        /// <summary>帧用途。</summary>
        public ENetworkMessageKind Kind { get; }
        /// <summary>网络错误码。</summary>
        public int ErrorCode { get; }
        /// <summary>拥有所有权的载荷。</summary>
        public byte[] Payload { get; }

        /// <summary>创建完整帧。</summary>
        /// <param name="messageID">消息编号。</param>
        /// <param name="requestID">关联编号。</param>
        /// <param name="kind">帧用途。</param>
        /// <param name="payload">拥有所有权的载荷。</param>
        /// <param name="errorCode">远端网络错误。</param>
        public NetworkFrame(uint messageID, ulong requestID, ENetworkMessageKind kind,
            byte[] payload, int errorCode = 0)
        {
            MessageID = messageID;
            RequestID = requestID;
            Kind = kind;
            Payload = payload ?? Array.Empty<byte>();
            ErrorCode = errorCode;
        }
    }

    /// <summary>框架不依赖具体序列化运行库的协议契约。</summary>
    public interface INetworkProtocol
    {
        /// <summary>按注册类型编码消息。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <param name="message">消息对象。</param>
        /// <returns>独占的序列化字节。</returns>
        byte[] Encode(uint messageID, object message);
        /// <summary>按注册类型解析消息。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <param name="payload">完整载荷。</param>
        /// <returns>解析后的独立对象。</returns>
        object Decode(uint messageID, byte[] payload);
        /// <summary>取得注册消息用途。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <returns>用途。</returns>
        ENetworkMessageKind GetKind(uint messageID);
    }

#if UNITY_5_3_OR_NEWER
    /// <summary>传输契约；工作线程仅入队，TryReceive 由主线程调用。</summary>
    public interface INetworkTransport : IDisposable
    {
        /// <summary>是否可靠传输。</summary>
        bool IsReliable { get; }
        /// <summary>是否保证有序。</summary>
        bool IsOrdered { get; }
        /// <summary>当前传输错误。</summary>
        Exception Failure { get; }
        /// <summary>接收积压。</summary>
        int ReceiveQueueCount { get; }
        /// <summary>发送积压。</summary>
        int SendQueueCount { get; }
        /// <summary>连接远端，不访问 Unity 对象。</summary>
        /// <param name="host">地址。</param>
        /// <param name="port">端口。</param>
        /// <param name="cancellationToken">取消令牌。</param>
        /// <returns>连接任务。</returns>
        UniTask ConnectAsync(string host, int port, CancellationToken cancellationToken);
        /// <summary>入队发送；不代表远端已经执行。</summary>
        /// <param name="frame">完整帧。</param>
        void Send(NetworkFrame frame);
        /// <summary>尝试取得独占完整帧。</summary>
        /// <param name="frame">取得的帧。</param>
        /// <returns>是否取得。</returns>
        bool TryReceive(out NetworkFrame frame);
        /// <summary>断开并清理队列。</summary>
        void Disconnect();
    }
#endif
}
