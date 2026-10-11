using System;
using System.IO;

namespace AlloyFramework.Net
{
    /// <summary>小端序 24 字节帧头，客户端和演示服务端共用。</summary>
    public static class NetworkWireCodec
    {
        /// <summary>包含长度字段的包头字节数。</summary>
        public const int HEADERSIZE = 24;

        /// <summary>检查长度前缀，禁止根据不可信长度先分配。</summary>
        /// <param name="prefix">至少四字节的长度前缀。</param>
        /// <param name="maxFrameBytes">包长上限。</param>
        /// <returns>验证过的总长度。</returns>
        /// <exception cref="InvalidDataException">长度越界。</exception>
        public static int ReadLength(byte[] prefix, int maxFrameBytes)
        {
            if (prefix == null || prefix.Length < 4)
                throw new InvalidDataException("帧长度前缀不足。");
            uint length = (uint)(prefix[0] | prefix[1] << 8 | prefix[2] << 16 | prefix[3] << 24);
            if (length < HEADERSIZE || length > maxFrameBytes)
                throw new InvalidDataException("帧长度越界。");
            return (int)length;
        }

        /// <summary>序列化一个完整帧。</summary>
        /// <param name="frame">完整帧。</param>
        /// <param name="version">协议版本。</param>
        /// <param name="maxFrameBytes">最大包长。</param>
        /// <returns>独占帧字节。</returns>
        /// <exception cref="InvalidDataException">帧格式无效。</exception>
        public static byte[] Encode(NetworkFrame frame, ushort version, int maxFrameBytes)
        {
            Validate(frame);
            if (frame.Payload.Length > maxFrameBytes - HEADERSIZE)
                throw new InvalidDataException("载荷过大。");
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((uint)(HEADERSIZE + frame.Payload.Length));
                writer.Write(version);
                writer.Write((byte)frame.Kind);
                writer.Write((byte)0);
                writer.Write(frame.MessageID);
                writer.Write(frame.RequestID);
                writer.Write(frame.ErrorCode);
                writer.Write(frame.Payload);
                return stream.ToArray();
            }
        }

        /// <summary>解析一个完整帧。</summary>
        /// <param name="bytes">完整帧字节。</param>
        /// <param name="version">预期版本。</param>
        /// <param name="maxFrameBytes">最大包长。</param>
        /// <returns>独占帧对象。</returns>
        /// <exception cref="InvalidDataException">包头或长度不匹配。</exception>
        public static NetworkFrame Decode(byte[] bytes, ushort version, int maxFrameBytes)
        {
            if (ReadLength(bytes, maxFrameBytes) != bytes.Length)
                throw new InvalidDataException("完整帧长度不一致。");
            using (var stream = new MemoryStream(bytes, false))
            using (var reader = new BinaryReader(stream))
            {
                reader.ReadUInt32();
                if (reader.ReadUInt16() != version)
                    throw new InvalidDataException("协议版本不一致。");
                var kind = (ENetworkMessageKind)reader.ReadByte();
                if (reader.ReadByte() != 0)
                    throw new InvalidDataException("未知包头标志。");
                uint messageID = reader.ReadUInt32();
                ulong requestID = reader.ReadUInt64();
                int errorCode = reader.ReadInt32();
                var frame = new NetworkFrame(messageID, requestID, kind,
                    reader.ReadBytes(bytes.Length - HEADERSIZE), errorCode);
                Validate(frame);
                return frame;
            }
        }

        private static void Validate(NetworkFrame frame)
        {
            if (frame == null || frame.MessageID == 0 ||
                frame.Kind < ENetworkMessageKind.Request || frame.Kind > ENetworkMessageKind.Error)
                throw new InvalidDataException("消息用途或编号无效。");
            bool hasRequest = frame.Kind == ENetworkMessageKind.Request ||
                frame.Kind == ENetworkMessageKind.Response || frame.Kind == ENetworkMessageKind.Error;
            if (hasRequest != (frame.RequestID != 0) ||
                (frame.Kind == ENetworkMessageKind.Error ? frame.ErrorCode == 0 : frame.ErrorCode != 0) ||
                (frame.Kind == ENetworkMessageKind.Error && frame.Payload.Length > 4096))
                throw new InvalidDataException("关联编号或错误载荷无效。");
        }
    }
}
