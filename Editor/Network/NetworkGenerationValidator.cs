using System;
using System.Collections.Generic;
using System.IO;

namespace AlloyFramework.Net.Editor
{
    internal static class NetworkGenerationValidator
    {
        internal static void Validate(NetworkDescriptorReader reader, HashSet<string> businessFiles,
            string generatedNamespace)
        {
            ValidateNamespace(generatedNamespace);
            var identifiers = new Dictionary<uint, string>();
            var generatedTypes = new HashSet<string>(StringComparer.Ordinal);
            generatedTypes.Add("global::" + generatedNamespace + ".GameNetworkProtocol");
            generatedTypes.Add("global::" + generatedNamespace + ".GameNetworkMessageID");
            var routedRequests = new HashSet<uint>();
            foreach (string source in businessFiles)
            {
                var file = reader.Files[source];
                ValidateIdentifier(Path.GetFileNameWithoutExtension(source));
                ValidateNamespace(file.Options?.CsharpNamespace);
                // 官方 descriptor 反射类型也参与名称冲突校验。
                string reflectionType = "global::" + file.Options.CsharpNamespace + "." +
                    Path.GetFileNameWithoutExtension(source) + "Reflection";
                if (!generatedTypes.Add(reflectionType)) throw new InvalidDataException("反射类型重名。");
                foreach (var service in file.Service)
                {
                    ValidateIdentifier(service.Name);
                    var methods = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var method in service.Method)
                    {
                        ValidateIdentifier(method.Name);
                        if (!methods.Add(method.Name) || !methods.Add(method.Name + "Async"))
                            throw new InvalidDataException("RPC 生成方法名称冲突。");
                        if (method.ClientStreaming || method.ServerStreaming)
                            throw new InvalidDataException("首版不支持 streaming RPC。");
                        var request = reader.GetMessage(method.InputType);
                        var response = reader.GetMessage(method.OutputType);
                        if (request.Kind != 1 || response.Kind != 2 ||
                            request.MessageID == 0 || response.MessageID == 0)
                            throw new InvalidDataException("RPC 请求响应缺少编号或用途不匹配。");
                        if (!routedRequests.Add(request.MessageID))
                            throw new InvalidDataException("同一请求消息不能路由到多个 RPC 方法。");
                    }
                }
            }
            foreach (var message in reader.Messages.Values)
            {
                if (!businessFiles.Contains(message.Source)) continue;
                foreach (string part in message.CSharpName.Replace("global::", "").Split('.'))
                    ValidateIdentifier(part);
                if (generatedTypes.Contains(message.CSharpName))
                    throw new InvalidDataException("消息类型与注册表或反射类型冲突。");
                if (message.MessageID == 0 && message.Kind == 0) continue;
                if (message.MessageID == 0 || message.Kind < 1 || message.Kind > 4)
                    throw new InvalidDataException("线上消息必须同时指定有效编号和用途。");
                if (identifiers.TryGetValue(message.MessageID, out string previous))
                    throw new InvalidDataException($"消息 ID 重复：{previous} / {message.ProtoName}");
                identifiers.Add(message.MessageID, message.ProtoName);
                if (message.Kind == 1 && !routedRequests.Contains(message.MessageID))
                    throw new InvalidDataException("请求消息没有对应的 RPC 声明。");
            }
            foreach (string generatedType in generatedTypes)
                if (reader.HasType(generatedType))
                    throw new InvalidDataException("消息或枚举与生成类型发生冲突：" + generatedType);
        }

        internal static void ValidateNamespace(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new InvalidDataException("必须明确指定 C# 命名空间。");
            foreach (string part in name.Split('.')) ValidateIdentifier(part);
        }

        internal static void ValidateIdentifier(string name)
        {
            if (string.IsNullOrEmpty(name) || name[0] < 'A' || name[0] > 'Z')
                throw new InvalidDataException("生成标识符必须使用英文 PascalCase：" + name);
            foreach (char character in name)
                if (!(character >= 'A' && character <= 'Z') && !(character >= 'a' && character <= 'z') &&
                    !(character >= '0' && character <= '9'))
                    throw new InvalidDataException("生成标识符含不支持的字符：" + name);
        }
    }
}
