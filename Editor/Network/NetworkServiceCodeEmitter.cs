using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Google.Protobuf.Reflection;

namespace AlloyFramework.Net.Editor
{
    /// <summary>生成分部网络服务及不会覆盖业务实现的处理骨架。</summary>
    internal static class NetworkServiceCodeEmitter
    {
        // 业务文件保留此标记，新增处理方法只插入标记前，不重写已有实现。
        private const string HANDLERMARKER = "        // <AlloyNetworkHandlers>";

        internal static string GetServiceName(string protocolName)
        {
            string name = protocolName.EndsWith("Rpc", StringComparison.Ordinal)
                ? protocolName.Substring(0, protocolName.Length - 3) : protocolName;
            return name + "Service";
        }

        internal static Dictionary<string, string> EmitServices(
            NetworkDescriptorReader reader, string source, string serviceNamespace)
        {
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            var file = reader.Files[source];
            var pushes = reader.Messages.Values.Where(message => message.Source == source &&
                (message.Kind == 3 || message.Kind == 4))
                .OrderBy(message => message.MessageID).ToArray();
            if (pushes.Length != 0 && file.Service.Count > 1)
            {
                throw new InvalidDataException("包含推送的协议文件只能定义一个 service，避免重复注册推送。");
            }

            int serviceCount = Math.Max(file.Service.Count, pushes.Length == 0 ? 0 : 1);
            for (int serviceIndex = 0; serviceIndex < serviceCount; serviceIndex++)
            {
                var service = file.Service.Count == 0 ? null : file.Service[serviceIndex];
                string className = GetServiceName(service?.Name ?? Path.GetFileNameWithoutExtension(source));
                var methods = service == null ? Array.Empty<MethodDescriptorProto>() : service.Method.ToArray();
                var builder = Header(serviceNamespace, true);
                builder.AppendLine("    /// <summary>协议收发部分；业务回包处理位于同名手写分部文件。</summary>");
                builder.AppendLine("    public sealed partial class " + className + " : IDisposable");
                builder.AppendLine("    {");
                builder.AppendLine("        private readonly NetworkSession m_session; // 此服务共用的账号会话。");
                builder.AppendLine("        private readonly CancellationToken m_accountToken; // 账号生命周期令牌。");
                builder.AppendLine("        private bool m_disposed; // 此账号服务是否已释放。");
                foreach (var push in pushes)
                {
                    builder.AppendLine("        private IDisposable " + RegistrationField(push) +
                        "; // 推送订阅的释放句柄。");
                }

                builder.AppendLine();
                builder.AppendLine("        /// <summary>绑定共享会话并安装本模块推送处理器。</summary>");
                builder.AppendLine("        /// <param name=\"session\">当前账号共享会话。</param>");
                builder.AppendLine("        /// <param name=\"accountToken\">账号生命周期令牌。</param>");
                builder.AppendLine("        /// <exception cref=\"ArgumentNullException\">会话为空。</exception>");
                builder.AppendLine("        public " + className +
                    "(NetworkSession session, CancellationToken accountToken)");
                builder.AppendLine("        {");
                builder.AppendLine("            m_session = session ?? " +
                    "throw new ArgumentNullException(nameof(session));");
                builder.AppendLine("            m_accountToken = accountToken;");
                if (pushes.Length != 0)
                {
                    builder.AppendLine("            // 构造失败时解除已安装订阅，避免遗留半初始化服务。");
                    builder.AppendLine("            try");
                    builder.AppendLine("            {");
                    foreach (var push in pushes)
                    {
                        builder.AppendLine("                " + RegistrationField(push) + " =");
                        builder.AppendLine("                    session.RegisterHandler<" + push.CSharpName + ">(");
                        builder.AppendLine("                        " + push.MessageID +
                            "u, Receive" + MessageName(push) + ");");
                    }

                    builder.AppendLine("            }");
                    builder.AppendLine("            catch");
                    builder.AppendLine("            {");
                    builder.AppendLine("                Dispose();");
                    builder.AppendLine("                throw;");
                    builder.AppendLine("            }");
                }

                builder.AppendLine("        }");
                foreach (var method in methods)
                {
                    AppendRequest(builder, reader, method);
                }

                foreach (var message in pushes.Where(message => message.Kind == 4))
                {
                    AppendOneWay(builder, message);
                }

                builder.AppendLine();
                builder.AppendLine("        /// <summary>解除推送并通知业务部分清理账号任务。</summary>");
                builder.AppendLine("        public void Dispose()");
                builder.AppendLine("        {");
                builder.AppendLine("            if (m_disposed) return;");
                builder.AppendLine("            m_disposed = true;");
                builder.AppendLine("            // 即使业务清理失败，也必须解除全部协议订阅。");
                builder.AppendLine("            try { OnServiceDisposed(); }");
                builder.AppendLine("            finally");
                builder.AppendLine("            {");
                foreach (var push in pushes)
                {
                    builder.AppendLine("                " + RegistrationField(push) + "?.Dispose();");
                }

                builder.AppendLine("            }");
                builder.AppendLine("        }");
                builder.AppendLine();
                builder.AppendLine("        private void EnsureServiceReady()");
                builder.AppendLine("        {");
                builder.AppendLine("            if (m_disposed || m_accountToken.IsCancellationRequested ||");
                builder.AppendLine("                m_session.State != ENetworkSessionState.Connected)");
                builder.AppendLine("                throw new InvalidOperationException(\"当前账号网络服务不可用。\");");
                builder.AppendLine("        }");
                foreach (var push in pushes)
                {
                    builder.AppendLine();
                    builder.AppendLine("        private void Receive" + MessageName(push) +
                        "(" + push.CSharpName + " message)");
                    builder.AppendLine("        {");
                    builder.AppendLine("            // 已退出账号的迟到推送不能进入业务处理。");
                    builder.AppendLine("            if (m_disposed || m_accountToken.IsCancellationRequested) return;");
                    builder.AppendLine("            On" + MessageName(push) + "(message);");
                    builder.AppendLine("        }");
                }

                AppendHookDeclarations(builder, reader, methods, pushes);
                builder.AppendLine("    }");
                builder.AppendLine("}");
                builder.AppendLine("#endif");
                outputs.Add("Services/" + className + ".g.cs", Normalize(builder));
            }

            return outputs;
        }

        internal static Dictionary<string, string> EmitRegistration(
            NetworkDescriptorReader reader, HashSet<string> generatedFiles,
            string serviceNamespace, string serviceAssembly)
        {
            var builder = Header(serviceNamespace, true);
            builder.AppendLine("    /// <summary>项目网络初始化时调用的自动生成服务注册入口。</summary>");
            builder.AppendLine("    public static class GameNetworkServices");
            builder.AppendLine("    {");
            builder.AppendLine("        /// <summary>创建并注册全部协议服务，共用项目提供的会话。</summary>");
            builder.AppendLine("        /// <param name=\"registry\">框架提供的服务注册表。</param>");
            builder.AppendLine("        /// <param name=\"session\">当前账号共享会话。</param>");
            builder.AppendLine("        /// <param name=\"accountToken\">账号生命周期令牌。</param>");
            builder.AppendLine("        /// <exception cref=\"ArgumentNullException\">注册表或会话为空。</exception>");
            builder.AppendLine("        /// <exception cref=\"InvalidOperationException\">服务类型重复注册。</exception>");
            builder.AppendLine("        public static void Register(");
            builder.AppendLine("            NetworkServiceRegistry registry,");
            builder.AppendLine("            NetworkSession session,");
            builder.AppendLine("            CancellationToken accountToken)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (registry == null) throw new ArgumentNullException(nameof(registry));");
            builder.AppendLine("            if (session == null) throw new ArgumentNullException(nameof(session));");
            builder.AppendLine("            accountToken.ThrowIfCancellationRequested();");
            builder.AppendLine("            // 直接创建协议服务，注册表负责实例获取和生命周期释放。");
            var names = ServiceNames(reader, generatedFiles).ToArray();
            foreach (string name in names)
            {
                builder.AppendLine("            registry.Register<" + name + ">(new " +
                    name + "(session, accountToken));");
            }

            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine("#endif");
            return new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "Services/GameNetworkServices.g.cs", Normalize(builder) },
                { "Services/NetworkServices.asmref", "{\n  \"reference\": \"" + serviceAssembly + "\"\n}\n" }
            };
        }

        internal static Dictionary<string, string> PrepareBusinessFiles(
            NetworkDescriptorReader reader, IEnumerable<string> sources,
            string serviceNamespace, string businessRoot)
        {
            var outputs = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string source in sources)
            {
                var file = reader.Files[source];
                var pushes = reader.Messages.Values.Where(message => message.Source == source &&
                    (message.Kind == 3 || message.Kind == 4))
                    .OrderBy(message => message.MessageID).ToArray();
                int count = Math.Max(file.Service.Count, pushes.Length == 0 ? 0 : 1);
                for (int index = 0; index < count; index++)
                {
                    var service = file.Service.Count == 0 ? null : file.Service[index];
                    string name = GetServiceName(service?.Name ?? Path.GetFileNameWithoutExtension(source));
                    string path = NetworkGenerationSettings.ChildPath(businessRoot, name + ".cs");
                    string existing = File.Exists(path) ? File.ReadAllText(path) : null;
                    var missing = new StringBuilder();
                    var methods = service == null ? Array.Empty<MethodDescriptorProto>() : service.Method.ToArray();
                    foreach (var method in methods)
                    {
                        var request = reader.GetMessage(method.InputType);
                        var response = reader.GetMessage(method.OutputType);
                        AppendBusinessHook(missing, existing, "On" + method.Name + "Response",
                            request.CSharpName + " request, " + response.CSharpName + " response");
                    }

                    foreach (var push in pushes)
                    {
                        AppendBusinessHook(missing, existing, "On" + MessageName(push), push.CSharpName + " message");
                    }

                    AppendBusinessHook(missing, existing, "OnServiceDisposed", "");
                    if (existing == null)
                    {
                        var builder = Header(serviceNamespace, false);
                        builder.AppendLine("    /// <summary>业务回包处理；此文件不会被协议生成器整体覆盖。</summary>");
                        builder.AppendLine("    public sealed partial class " + name);
                        builder.AppendLine("    {");
                        builder.Append(missing);
                        builder.AppendLine(HANDLERMARKER);
                        builder.AppendLine("    }");
                        builder.AppendLine("}");
                        builder.AppendLine("#endif");
                        outputs.Add(path, Normalize(builder));
                    }
                    else if (missing.Length != 0)
                    {
                        // 已有文件只追加缺失方法，不更改任何现有方法的签名或实现。
                        if (!existing.Contains(HANDLERMARKER))
                        {
                            throw new InvalidDataException("业务文件缺少处理器插入标记，请手动添加：" + path);
                        }

                        outputs.Add(path, existing.Replace(HANDLERMARKER, missing.ToString() + HANDLERMARKER));
                    }
                }
            }

            return outputs;
        }

        private static void AppendRequest(StringBuilder builder, NetworkDescriptorReader reader,
            MethodDescriptorProto method)
        {
            var request = reader.GetMessage(method.InputType);
            var response = reader.GetMessage(method.OutputType);
            builder.AppendLine();
            builder.AppendLine("        /// <summary>发送请求，业务回包处理完成后返回响应。</summary>");
            builder.AppendLine("        /// <param name=\"request\">本次请求独占的消息对象。</param>");
            builder.AppendLine("        /// <param name=\"cancellationToken\">本次调用取消令牌。</param>");
            builder.AppendLine("        /// <param name=\"options\">可选请求超时配置。</param>");
            builder.AppendLine("        /// <returns>已经经过业务处理的响应。</returns>");
            builder.AppendLine("        /// <exception cref=\"ArgumentNullException\">请求为空。</exception>");
            builder.AppendLine("        /// <exception cref=\"InvalidOperationException\">账号服务不可用。</exception>");
            builder.AppendLine("        public async UniTask<" + response.CSharpName + ">");
            builder.AppendLine("            " + method.Name + "Async(");
            builder.AppendLine("                " + request.CSharpName + " request,");
            builder.AppendLine("                CancellationToken cancellationToken = default,");
            builder.AppendLine("                NetworkRequestOptions options = null)");
            builder.AppendLine("        {");
            builder.AppendLine("            EnsureServiceReady();");
            builder.AppendLine("            if (request == null) throw new ArgumentNullException(nameof(request));");
            builder.AppendLine("            // 请求同时受调用者和账号生命周期约束，回包先处理业务再完成等待。");
            builder.AppendLine("            using (var cancellation = " +
                "CancellationTokenSource.CreateLinkedTokenSource(");
            builder.AppendLine("                m_accountToken, cancellationToken))");
            builder.AppendLine("            {");
            builder.AppendLine("                var response = await m_session.RequestAsync<" +
                response.CSharpName + ">(");
            builder.AppendLine("                    " + request.MessageID + "u, " + response.MessageID + "u,");
            builder.AppendLine("                    request, cancellation.Token, options);");
            builder.AppendLine("                cancellation.Token.ThrowIfCancellationRequested();");
            builder.AppendLine("                EnsureServiceReady();");
                builder.AppendLine("                On" + method.Name + "Response(request, response);");
            builder.AppendLine("                return response;");
            builder.AppendLine("            }");
            builder.AppendLine("        }");
        }

        private static void AppendHookDeclarations(StringBuilder builder, NetworkDescriptorReader reader,
            IEnumerable<MethodDescriptorProto> methods, IEnumerable<NetworkMessageDescriptor> pushes)
        {
            builder.AppendLine();
            builder.AppendLine("        // 分部方法由手写业务文件实现，重新生成不会覆盖业务逻辑。");
            foreach (var method in methods)
            {
                var request = reader.GetMessage(method.InputType);
                var response = reader.GetMessage(method.OutputType);
                builder.AppendLine("        partial void On" + method.Name + "Response(");
                builder.AppendLine("            " + request.CSharpName + " request,");
                builder.AppendLine("            " + response.CSharpName + " response);");
            }

            foreach (var push in pushes)
            {
                builder.AppendLine("        partial void On" + MessageName(push) +
                    "(" + push.CSharpName + " message);");
            }

            builder.AppendLine("        partial void OnServiceDisposed();");
        }

        private static void AppendBusinessHook(StringBuilder builder, string existing, string name, string parameters)
        {
            if (existing != null && System.Text.RegularExpressions.Regex.IsMatch(existing,
                @"(?m)^\s*partial\s+void\s+" + name + @"\s*\("))
            {
                return;
            }

            builder.AppendLine();
            if (parameters.Length != 0)
            {
                builder.AppendLine("        partial void " + name + "(");
                string[] parameterDeclarations = parameters.Split(new[] { ", " }, StringSplitOptions.None);
                for (int index = 0; index < parameterDeclarations.Length; index++)
                {
                    builder.AppendLine("            " + parameterDeclarations[index] +
                        (index == parameterDeclarations.Length - 1 ? ")" : ","));
                }
            }
            else
            {
                builder.AppendLine("        partial void " + name + "()");
            }

            builder.AppendLine("        {");
            builder.AppendLine("            // 在此处理业务数据；协议生成器不会覆盖已有实现。");
            builder.AppendLine("        }");
        }

        private static IEnumerable<string> ServiceNames(NetworkDescriptorReader reader, IEnumerable<string> sources)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in sources.OrderBy(value => value, StringComparer.Ordinal))
            {
                var file = reader.Files[source];
                var candidates = file.Service.Count == 0
                    ? (reader.Messages.Values.Any(message => message.Source == source &&
                        (message.Kind == 3 || message.Kind == 4))
                        ? new[] { GetServiceName(Path.GetFileNameWithoutExtension(source)) } : Array.Empty<string>())
                    : file.Service.Select(service => GetServiceName(service.Name));
                foreach (string name in candidates)
                {
                    NetworkGenerationValidator.ValidateIdentifier(name);
                    if (!names.Add(name)) throw new InvalidDataException("生成服务名称重复：" + name);
                    yield return name;
                }
            }
        }

        private static string MessageName(NetworkMessageDescriptor message)
        {
            return message.CSharpName.Substring(message.CSharpName.LastIndexOf('.') + 1);
        }

        private static void AppendOneWay(StringBuilder builder, NetworkMessageDescriptor message)
        {
            builder.AppendLine();
            builder.AppendLine("        /// <summary>发送单向消息，不等待响应。</summary>");
            builder.AppendLine("        /// <param name=\"message\">本次发送的消息对象。</param>");
            builder.AppendLine("        /// <exception cref=\"ArgumentNullException\">消息为空。</exception>");
            builder.AppendLine("        /// <exception cref=\"InvalidOperationException\">账号服务不可用。</exception>");
            builder.AppendLine("        public void Send" + MessageName(message) +
                "(" + message.CSharpName + " message)");
            builder.AppendLine("        {");
            builder.AppendLine("            EnsureServiceReady();");
            builder.AppendLine("            if (message == null) throw new ArgumentNullException(nameof(message));");
            builder.AppendLine("            m_session.Send(" + message.MessageID + "u, message);");
            builder.AppendLine("        }");
        }

        private static string RegistrationField(NetworkMessageDescriptor message)
        {
            string name = MessageName(message);
            return "m_" + char.ToLowerInvariant(name[0]) + name.Substring(1) + "Registration";
        }

        private static StringBuilder Header(string serviceNamespace, bool generated)
        {
            var builder = new StringBuilder();
            if (generated) builder.AppendLine("// 自动生成，切勿手动修改。由 AlloyFramework 网络协议生成器维护。");
            builder.AppendLine("#if ALLOY_PROTOBUF");
            if (generated)
            {
                builder.AppendLine("using System;");
                builder.AppendLine("using System.Threading;");
                builder.AppendLine("using AlloyFramework.Net;");
                builder.AppendLine("using Cysharp.Threading.Tasks;");
            }

            builder.AppendLine();
            builder.AppendLine("namespace " + serviceNamespace);
            builder.AppendLine("{");
            return builder;
        }

        private static string Normalize(StringBuilder builder)
        {
            string code = builder.ToString().Replace("\r\n", "\n");
            foreach (string line in code.Split('\n'))
            {
                if (line.Length > 120)
                {
                    throw new InvalidDataException("生成服务行宽超过 120：" + line);
                }
            }

            return code;
        }
    }
}
