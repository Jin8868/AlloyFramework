using System;
using System.Collections.Generic;
using System.IO;
using Google.Protobuf;
using Google.Protobuf.Reflection;

namespace AlloyFramework.Net.Editor
{
    internal sealed class NetworkMessageDescriptor
    {
        internal string Source { get; set; }
        internal string ProtoName { get; set; }
        internal string CSharpName { get; set; }
        internal uint MessageID { get; set; }
        internal uint Kind { get; set; }
    }

    internal sealed class NetworkDescriptorReader
    {
        private readonly Dictionary<string, FileDescriptorProto> m_files =
            new Dictionary<string, FileDescriptorProto>(StringComparer.Ordinal); // import 图。
        private readonly Dictionary<string, NetworkMessageDescriptor> m_messages =
            new Dictionary<string, NetworkMessageDescriptor>(StringComparer.Ordinal); // 全限定消息名称。
        private readonly HashSet<string> m_typeNames = new HashSet<string>(StringComparer.Ordinal); // C# 类型冲突。

        internal IReadOnlyDictionary<string, FileDescriptorProto> Files => m_files;
        internal IReadOnlyDictionary<string, NetworkMessageDescriptor> Messages => m_messages;

        internal NetworkDescriptorReader(byte[] descriptorBytes)
        {
            var descriptors = FileDescriptorSet.Parser.ParseFrom(descriptorBytes);
            foreach (var file in descriptors.File)
            {
                if (m_files.ContainsKey(file.Name)) throw new InvalidDataException("重复 descriptor 文件。");
                m_files.Add(file.Name, file);
            }
            foreach (var file in descriptors.File)
            {
                foreach (string dependency in file.Dependency)
                    if (!m_files.ContainsKey(dependency))
                        throw new InvalidDataException($"缺少 import：{file.Name} → {dependency}");
                string namespaceName = file.Options?.CsharpNamespace;
                // 标准 Google 协议不在业务生成范围；业务协议必须显式声明命名空间。
                if (string.IsNullOrEmpty(namespaceName)) namespaceName = file.Package;
                foreach (var message in file.MessageType)
                    ReadMessage(file.Name, string.IsNullOrEmpty(file.Package) ? "." : "." + file.Package + ".",
                        "global::" + namespaceName + ".", message);
                foreach (var enumeration in file.EnumType)
                    RegisterType("global::" + namespaceName + "." + enumeration.Name);
            }
        }

        internal NetworkMessageDescriptor GetMessage(string name)
        {
            if (!m_messages.TryGetValue(name, out var message))
                throw new InvalidDataException($"RPC 消息类型不存在：{name}");
            return message;
        }

        internal bool HasType(string name) => m_typeNames.Contains(name);

        internal SortedSet<string> DependencyClosure(string source)
        {
            var result = new SortedSet<string>(StringComparer.Ordinal);
            AddDependencies(source, result, new HashSet<string>(StringComparer.Ordinal));
            return result;
        }

        internal string MethodComment(FileDescriptorProto file, int serviceIndex, int methodIndex)
        {
            if (file.SourceCodeInfo != null)
                foreach (var location in file.SourceCodeInfo.Location)
                    if (location.Path.Count == 4 && location.Path[0] == 6 &&
                        location.Path[1] == serviceIndex && location.Path[2] == 2 && location.Path[3] == methodIndex &&
                        !string.IsNullOrWhiteSpace(location.LeadingComments))
                        return location.LeadingComments.Trim();
            return "向目标会话发送请求并取得类型明确的响应。";
        }

        private void ReadMessage(string source, string protoPrefix, string csharpPrefix, DescriptorProto descriptor)
        {
            var message = new NetworkMessageDescriptor
            {
                Source = source,
                ProtoName = protoPrefix + descriptor.Name,
                CSharpName = csharpPrefix + descriptor.Name
            };
            if (descriptor.Options != null)
            {
                // 扩展元数据从官方 descriptor 的 wire 字段读取，不解析 proto 文本。
                using (var input = new CodedInputStream(descriptor.Options.ToByteArray()))
                {
                    uint tag;
                    while ((tag = input.ReadTag()) != 0)
                    {
                        if (tag == (51001u << 3)) message.MessageID = input.ReadUInt32();
                        else if (tag == (51002u << 3)) message.Kind = input.ReadUInt32();
                        else input.SkipLastField();
                    }
                }
            }
            RegisterType(message.CSharpName);
            m_messages.Add(message.ProtoName, message);
            foreach (var nested in descriptor.NestedType)
                if (nested.Options?.MapEntry != true)
                    ReadMessage(source, message.ProtoName + ".", message.CSharpName + ".Types.", nested);
            foreach (var enumeration in descriptor.EnumType)
                RegisterType(message.CSharpName + ".Types." + enumeration.Name);
        }

        private void RegisterType(string name)
        {
            if (!m_typeNames.Add(name)) throw new InvalidDataException($"生成 C# 类型重复：{name}");
        }

        private void AddDependencies(string source, SortedSet<string> result, HashSet<string> visiting)
        {
            if (visiting.Contains(source)) throw new InvalidDataException("发现循环 import。");
            if (result.Contains(source)) return;
            visiting.Add(source);
            foreach (string dependency in m_files[source].Dependency) AddDependencies(dependency, result, visiting);
            visiting.Remove(source);
            result.Add(source);
        }
    }
}
