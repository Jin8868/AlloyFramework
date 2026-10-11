using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AlloyFramework.Net.Editor
{
    internal static class NetworkCodeEmitter
    {
        internal static Dictionary<string, string> EmitRegistry(NetworkDescriptorReader reader,
            HashSet<string> generatedFiles, string generatedNamespace, int protocolVersion)
        {
            var messages = reader.Messages.Values.Where(message =>
                generatedFiles.Contains(message.Source) && message.MessageID != 0)
                .OrderBy(message => message.MessageID).ToArray();
            var builder = Header(generatedNamespace);
            builder.AppendLine("    /// <summary>已生成协议集合的显式 Parser 注册入口。</summary>");
            builder.AppendLine("    public static class GameNetworkProtocol");
            builder.AppendLine("    {");
            builder.AppendLine("        /// <summary>创建独立协议注册表。</summary>");
            builder.AppendLine("        /// <returns>已安装全部消息 Parser 的适配器。</returns>");
            builder.AppendLine("        public static ProtobufNetworkProtocol Create()");
            builder.AppendLine("        {");
            builder.AppendLine("            // 所有类型均静态可达，避免运行时程序集扫描。");
            builder.AppendLine("            var protocol = new ProtobufNetworkProtocol();");
            foreach (var message in messages)
            {
                builder.AppendLine("            protocol.Register(");
                builder.AppendLine("                " + message.MessageID + "u, ENetworkMessageKind." +
                    ((ENetworkMessageKind)message.Kind) + ",");
                builder.AppendLine("                " + message.CSharpName + ".Parser);");
            }
            builder.AppendLine("            return protocol;");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal)
            { { "Registry/GameNetworkProtocol.g.cs", Normalize(builder) } };
            builder = Header(generatedNamespace, false);
            builder.AppendLine("    /// <summary>稳定显式线上消息编号，删除后不得复用。</summary>");
            builder.AppendLine("    public static class GameNetworkMessageID");
            builder.AppendLine("    {");
            builder.AppendLine("        /// <summary>双方共享的线上协议版本。</summary>");
            builder.AppendLine("        public const ushort PROTOCOLVERSION = " + protocolVersion + ";");
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in messages)
            {
                string name = message.ProtoName.Replace(".", "").ToUpperInvariant();
                if (!identifiers.Add(name)) throw new InvalidDataException("消息常量名称冲突。");
                builder.AppendLine("        /// <summary>" + EscapeXml(message.ProtoName) + " 的线上编号。</summary>");
                builder.AppendLine("        public const uint " + name + " = " + message.MessageID + "u;");
            }
            builder.AppendLine("    }");
            builder.AppendLine("}");
            outputs.Add("Registry/GameNetworkMessageID.g.cs", Normalize(builder));
            return outputs;
        }

        private static StringBuilder Header(string namespaceName, bool protocolImports = true)
        {
            var builder = new StringBuilder();
            builder.AppendLine("// 自动生成，切勿手动修改。由 AlloyFramework 网络协议生成器维护。");
            if (protocolImports)
            {
                builder.AppendLine("using System;");
                builder.AppendLine("using System.Threading;");
                builder.AppendLine("using AlloyFramework.Net;");
                builder.AppendLine("using AlloyFramework.Net.Protobuf;");
                builder.AppendLine("using Cysharp.Threading.Tasks;");
            }
            builder.AppendLine();
            builder.AppendLine("namespace " + namespaceName);
            builder.AppendLine("{");
            return builder;
        }

        private static string EscapeXml(string text)
        {
            return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string Normalize(StringBuilder builder)
        {
            string code = builder.ToString().Replace("\r\n", "\n");
            foreach (string line in code.Split('\n'))
                if (line.Length > 120) throw new InvalidDataException("生成代码行宽超过 120，请缩短协议标识符或注释。");
            return code;
        }
    }
}
