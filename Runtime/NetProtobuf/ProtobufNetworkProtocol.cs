using System;
using System.Collections.Generic;
using AlloyFramework.Net;
using Google.Protobuf;

namespace AlloyFramework.Net.Protobuf
{
    /// <summary>使用显式 Parser 注册，不扫描程序集的可选 Protobuf 适配。</summary>
    public sealed class ProtobufNetworkProtocol : INetworkProtocol
    {
        private readonly Dictionary<uint, Registration> m_registry =
            new Dictionary<uint, Registration>(); // 编号到明确解析器。
        private bool m_sealed; // 首次使用后不允许改变线上类型。

        /// <summary>注册一个稳定的线上消息编号。</summary>
        /// <typeparam name="TMessage">官方生成的消息类型。</typeparam>
        /// <param name="messageID">稳定编号。</param>
        /// <param name="kind">用途。</param>
        /// <param name="parser">官方 Parser。</param>
        /// <exception cref="InvalidOperationException">编号重复或已开始使用。</exception>
        public void Register<TMessage>(uint messageID, ENetworkMessageKind kind, MessageParser<TMessage> parser)
            where TMessage : class, IMessage<TMessage>
        {
            if (m_sealed || messageID == 0 || parser == null || m_registry.ContainsKey(messageID) ||
                kind < ENetworkMessageKind.Request || kind > ENetworkMessageKind.OneWay)
                throw new InvalidOperationException("协议注册重复、无效或已经封存。");
            m_registry.Add(messageID, new Registration<TMessage>(kind, parser));
        }

        /// <summary>编码并验证消息类型。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <param name="message">官方消息。</param>
        /// <returns>独占载荷。</returns>
        /// <exception cref="NetworkException">未注册或类型错误。</exception>
        public byte[] Encode(uint messageID, object message) => GetRegistration(messageID).Encode(message);

        /// <summary>通过显式 Parser 解析。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <param name="payload">独占载荷。</param>
        /// <returns>独立消息对象。</returns>
        /// <exception cref="NetworkException">编号未注册。</exception>
        public object Decode(uint messageID, byte[] payload) => GetRegistration(messageID).Decode(payload);

        /// <summary>取得消息用途并封存注册表。</summary>
        /// <param name="messageID">线上编号。</param>
        /// <returns>注册用途。</returns>
        /// <exception cref="NetworkException">编号未注册。</exception>
        public ENetworkMessageKind GetKind(uint messageID) => GetRegistration(messageID).Kind;

        private Registration GetRegistration(uint messageID)
        {
            m_sealed = true;
            if (!m_registry.TryGetValue(messageID, out var registration))
                throw new NetworkException(ENetworkError.Protocol, $"未注册消息编号：{messageID}。");
            return registration;
        }

        private abstract class Registration
        {
            internal ENetworkMessageKind Kind { get; }
            protected Registration(ENetworkMessageKind kind) { Kind = kind; }
            internal abstract byte[] Encode(object message);
            internal abstract object Decode(byte[] payload);
        }

        private sealed class Registration<TMessage> : Registration where TMessage : class, IMessage<TMessage>
        {
            private readonly MessageParser<TMessage> m_parser; // 明确的官方解析器。
            internal Registration(ENetworkMessageKind kind, MessageParser<TMessage> parser) : base(kind)
            { m_parser = parser; }
            internal override byte[] Encode(object message)
            {
                if (!(message is TMessage typed))
                    throw new NetworkException(ENetworkError.Protocol, "消息类型与编号不一致。");
                return typed.ToByteArray();
            }
            internal override object Decode(byte[] payload) => m_parser.ParseFrom(payload);
        }
    }
}
