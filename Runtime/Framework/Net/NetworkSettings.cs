using System;

namespace AlloyFramework.Net
{
    /// <summary>单个会话的资源限制。</summary>
    public sealed class NetworkSettings
    {
        /// <summary>协议版本。</summary>
        public ushort ProtocolVersion { get; set; } = 1;
        /// <summary>单包上限，含包头。</summary>
        public int MaxFrameBytes { get; set; } = 1024 * 1024;
        /// <summary>每方向最多排队包数。</summary>
        public int MaxQueueCount { get; set; } = 128;
        /// <summary>每方向最多排队字节数。</summary>
        public int MaxQueueBytes { get; set; } = 4 * 1024 * 1024;
        /// <summary>最大同时待完成请求数。</summary>
        public int MaxPendingRequests { get; set; } = 128;
        /// <summary>单会话每帧派发预算。</summary>
        public int DispatchBudget { get; set; } = 32;
        /// <summary>默认请求超时秒数。</summary>
        public double RequestTimeoutSeconds { get; set; } = 10;
        /// <summary>默认连接超时秒数。</summary>
        public double ConnectTimeoutSeconds { get; set; } = 10;

        /// <summary>验证限制并创建快照，隔离调用者后续修改。</summary>
        /// <returns>独立配置快照。</returns>
        /// <exception cref="ArgumentOutOfRangeException">限制无效。</exception>
        public NetworkSettings Snapshot()
        {
            if (ProtocolVersion == 0 || MaxFrameBytes < NetworkWireCodec.HEADERSIZE ||
                MaxFrameBytes > 16 * 1024 * 1024 || MaxQueueCount < 1 || MaxQueueBytes < MaxFrameBytes ||
                MaxPendingRequests < 1 || DispatchBudget < 1 ||
                !IsPositiveFinite(RequestTimeoutSeconds) || RequestTimeoutSeconds > 86400 ||
                !IsPositiveFinite(ConnectTimeoutSeconds) || ConnectTimeoutSeconds > 86400)
            {
                throw new ArgumentOutOfRangeException(nameof(NetworkSettings), "网络资源限制无效。");
            }
            return (NetworkSettings)MemberwiseClone();
        }

        internal static bool IsPositiveFinite(double seconds)
        {
            return seconds > 0 && !double.IsNaN(seconds) && !double.IsInfinity(seconds);
        }
    }

    /// <summary>单次请求配置。</summary>
    public sealed class NetworkRequestOptions
    {
        /// <summary>超时秒数；零表示使用会话默认值。</summary>
        public double TimeoutSeconds { get; set; }
    }
}
