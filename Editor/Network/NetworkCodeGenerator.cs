using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace AlloyFramework.Net.Editor
{
    internal sealed class NetworkGenerationPlan
    {
        internal NetworkDescriptorReader Reader { get; set; }
        internal NetworkOutputManifest Manifest { get; set; }
        internal HashSet<string> BusinessFiles { get; set; }
        internal SortedSet<string> RequiredSelection { get; } = new SortedSet<string>(StringComparer.Ordinal);
        internal Dictionary<string, string> Fingerprints { get; } = new Dictionary<string, string>();
        internal string StageRoot { get; set; }
    }

    internal static class NetworkCodeGenerator
    {
        private const string MANIFESTNAME = "NetworkOutputManifest.json"; // 工具拥有的生成归属清单。
        private static readonly UTF8Encoding m_encoding = new UTF8Encoding(false); // 确定性的无 BOM 输出。

        internal static string[] ListSources(NetworkGenerationSettings settings)
        {
            string root = NetworkGenerationSettings.Resolve(settings.ProtoRoot);
            if (!Directory.Exists(root)) return Array.Empty<string>();
            return Directory.GetFiles(root, "*.proto", SearchOption.AllDirectories)
                .Select(path => path.Substring(root.Length + 1).Replace('\\', '/'))
                .OrderBy(path => path, StringComparer.Ordinal).ToArray();
        }

        internal static NetworkGenerationPlan Analyze(NetworkGenerationSettings settings)
        {
            string protoRoot = NetworkGenerationSettings.Resolve(settings.ProtoRoot);
            if (!Directory.Exists(protoRoot)) throw new DirectoryNotFoundException("Proto 根目录不存在。");
            string outputRoot = NetworkGenerationSettings.Resolve(settings.OutputRoot);
            if (!settings.OutputRoot.Replace('\\', '/').StartsWith("Assets/", StringComparison.Ordinal) ||
                !settings.OutputRoot.Replace('\\', '/').EndsWith("/Generated", StringComparison.Ordinal))
                throw new InvalidOperationException("输出目录必须是 Assets 内专用的 Generated 子目录。");
            if (settings.ProtocolVersion < 1 || settings.ProtocolVersion > ushort.MaxValue)
                throw new InvalidOperationException("协议版本必须是有效 uint16。");
            string[] sources = ListSources(settings);
            var businessFiles = new HashSet<string>(sources, StringComparer.Ordinal);
            var selected = new HashSet<string>(settings.SelectedFiles ?? Array.Empty<string>(), StringComparer.Ordinal);
            foreach (string source in selected)
                if (!businessFiles.Contains(source)) throw new InvalidOperationException("选中协议不存在：" + source);
            foreach (string source in sources)
            {
                // 拒绝 import 根目录遮蔽，避免 descriptor 与实际哈希来源不一致。
                if (File.Exists(NetworkGenerationSettings.ChildPath(
                    NetworkGenerationSettings.Resolve(settings.StandardInclude), source)) ||
                    File.Exists(NetworkGenerationSettings.ChildPath(
                        NetworkGenerationSettings.Resolve(settings.OptionsRoot), source)))
                    throw new InvalidOperationException("业务协议遮蔽外部 import：" + source);
            }
            string version = RunProtoc(settings, new[] { "--version" }).Trim();
            if (version != "libprotoc 34.1") throw new InvalidOperationException("必须使用锁定的 protoc 34.1。");
            string stageRoot = NetworkGenerationSettings.ChildPath(
                NetworkGenerationSettings.ProjectRoot, "Protocol/Build/Generation/" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stageRoot);
            string descriptorPath = Path.Combine(stageRoot, "Protocol.pb");
            var arguments = IncludeArguments(settings);
            arguments.Add("--descriptor_set_out=" + descriptorPath);
            arguments.Add("--include_imports");
            arguments.Add("--include_source_info");
            arguments.AddRange(sources);
            if (sources.Length > 0) RunProtoc(settings, arguments);
            var reader = new NetworkDescriptorReader(sources.Length > 0
                ? File.ReadAllBytes(descriptorPath) : Array.Empty<byte>());
            foreach (string import in reader.Files.Keys)
                if (!businessFiles.Contains(import) && import != "AlloyNetworkOptions.proto" &&
                    !import.StartsWith("google/protobuf/", StringComparison.Ordinal))
                    throw new InvalidOperationException("未声明外部协议的 C# 输出归属：" + import);
            NetworkGenerationValidator.Validate(reader, businessFiles, settings.GeneratedNamespace);
            NetworkGenerationValidator.ValidateNamespace(settings.ServiceNamespace);
            NetworkGenerationValidator.ValidateNamespace(settings.ServiceAssembly);
            // 服务注册输出同时检查名称冲突，避免运行时才发现重复服务。
            NetworkServiceCodeEmitter.EmitRegistration(
                reader, businessFiles, settings.ServiceNamespace, settings.ServiceAssembly);
            string manifestPath = Path.Combine(outputRoot, MANIFESTNAME);
            var manifest = File.Exists(manifestPath)
                ? JsonUtility.FromJson<NetworkOutputManifest>(File.ReadAllText(manifestPath))
                : new NetworkOutputManifest();
            ValidateManifest(manifest);
            var assignedIDs = (manifest.AssignedIDs ?? Array.Empty<NetworkIDRecord>())
                .ToDictionary(record => record.MessageID);
            foreach (var message in reader.Messages.Values)
                if (businessFiles.Contains(message.Source) && message.MessageID != 0 &&
                    assignedIDs.TryGetValue(message.MessageID, out var assignment) &&
                    assignment.ProtoName != message.ProtoName)
                    throw new InvalidOperationException("消息编号曾属于其他消息，禁止复用：" + message.MessageID);
            var plan = new NetworkGenerationPlan
            {
                Reader = reader, Manifest = manifest, BusinessFiles = businessFiles, StageRoot = stageRoot
            };
            foreach (string source in sources)
                plan.Fingerprints.Add(source, Fingerprint(settings, reader, source));
            var previous = manifest.Files.ToDictionary(record => record.Source, StringComparer.Ordinal);
            bool changedSettings = manifest.GeneratorVersion != "2" ||
                (manifest.Files.Length != 0 && (manifest.GeneratedNamespace != settings.GeneratedNamespace ||
                manifest.ProtocolVersion != settings.ProtocolVersion));
            // 未选中输出必须仍然有效；失效协议只能由面板明确补选。
            foreach (var record in manifest.Files)
                if (businessFiles.Contains(record.Source) && !selected.Contains(record.Source) &&
                    (changedSettings || record.Fingerprint != plan.Fingerprints[record.Source] ||
                    record.Outputs.Any(output => !File.Exists(
                        NetworkGenerationSettings.ChildPath(outputRoot, output.Path)))))
                    plan.RequiredSelection.Add(record.Source);
            var needed = new HashSet<string>(selected, StringComparer.Ordinal);
            needed.UnionWith(plan.RequiredSelection);
            foreach (string source in needed.ToArray())
                foreach (string dependency in reader.DependencyClosure(source))
                    if (businessFiles.Contains(dependency) && !selected.Contains(dependency) &&
                        (!previous.TryGetValue(dependency, out var record) || changedSettings ||
                        record.Fingerprint != plan.Fingerprints[dependency] || record.Outputs.Any(output =>
                        !File.Exists(NetworkGenerationSettings.ChildPath(outputRoot, output.Path)))))
                        plan.RequiredSelection.Add(dependency);
            return plan;
        }

        internal static void Generate(NetworkGenerationSettings settings, NetworkGenerationPlan plan)
        {
            if (plan.RequiredSelection.Count != 0)
                throw new InvalidOperationException("请先勾选所需依赖及受影响协议：" +
                    string.Join("、", plan.RequiredSelection));
            var selected = new HashSet<string>(settings.SelectedFiles ?? Array.Empty<string>(), StringComparer.Ordinal);
            string outputRoot = NetworkGenerationSettings.Resolve(settings.OutputRoot);
            var desired = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var records = plan.Manifest.Files.Where(record =>
                plan.BusinessFiles.Contains(record.Source) && !selected.Contains(record.Source)).ToList();
            foreach (string source in selected.OrderBy(path => path, StringComparer.Ordinal))
            {
                // 逐文件使用官方生成器，目录归属于源协议，不覆盖其他选择项。
                string relativeParent = Path.GetDirectoryName(source)?.Replace('\\', '/') ?? "";
                string relativeOutput = "Messages/" + (relativeParent.Length == 0 ? "" : relativeParent + "/") +
                    Path.GetFileNameWithoutExtension(source) + ".cs";
                string messageStage = Path.Combine(plan.StageRoot, "Messages", relativeParent);
                Directory.CreateDirectory(messageStage);
                var arguments = IncludeArguments(settings);
                arguments.Add("--csharp_out=" + messageStage);
                arguments.Add(source);
                RunProtoc(settings, arguments);
                var owned = new List<NetworkOwnedOutput>();
                AddOutput(desired, owned, relativeOutput,
                    File.ReadAllBytes(Path.Combine(messageStage, Path.GetFileNameWithoutExtension(source) + ".cs")));
                foreach (var output in NetworkServiceCodeEmitter.EmitServices(
                    plan.Reader, source, settings.ServiceNamespace))
                    AddOutput(desired, owned, output.Key, m_encoding.GetBytes(output.Value));
                records.Add(new NetworkFileRecord
                { Source = source, Fingerprint = plan.Fingerprints[source], Outputs = owned.ToArray() });
            }
            var generatedFiles = new HashSet<string>(records.Select(record => record.Source), StringComparer.Ordinal);
            var registry = new List<NetworkOwnedOutput>();
            foreach (var output in NetworkCodeEmitter.EmitRegistry(
                plan.Reader, generatedFiles, settings.GeneratedNamespace, settings.ProtocolVersion))
                AddOutput(desired, registry, output.Key, m_encoding.GetBytes(output.Value));
            foreach (var output in NetworkServiceCodeEmitter.EmitRegistration(
                plan.Reader, generatedFiles, settings.ServiceNamespace, settings.ServiceAssembly))
            {
                AddOutput(desired, registry, output.Key, m_encoding.GetBytes(output.Value));
            }

            var businessFiles = NetworkServiceCodeEmitter.PrepareBusinessFiles(
                plan.Reader, selected, settings.ServiceNamespace,
                NetworkGenerationSettings.Resolve(settings.BusinessServiceRoot));
            var assignments = (plan.Manifest.AssignedIDs ?? Array.Empty<NetworkIDRecord>())
                .ToDictionary(record => record.MessageID);
            foreach (var message in plan.Reader.Messages.Values)
                if (generatedFiles.Contains(message.Source) && message.MessageID != 0)
                    assignments[message.MessageID] = new NetworkIDRecord
                    { MessageID = message.MessageID, ProtoName = message.ProtoName };
            var manifest = new NetworkOutputManifest
            {
                GeneratedNamespace = settings.GeneratedNamespace, ProtocolVersion = settings.ProtocolVersion,
                Files = records.OrderBy(record => record.Source, StringComparer.Ordinal).ToArray(),
                RegistryOutputs = registry.ToArray(),
                AssignedIDs = assignments.Values.OrderBy(record => record.MessageID).ToArray()
            };
            desired.Add(MANIFESTNAME, m_encoding.GetBytes(JsonUtility.ToJson(manifest, true) + "\n"));
            Commit(outputRoot, desired, plan.Manifest, manifest, businessFiles);
        }

        private static void ValidateManifest(NetworkOutputManifest manifest)
        {
            // 清单损坏时提前停止，避免归属信息丢失后被误判为手写文件。
            if (manifest == null || manifest.Files == null || manifest.RegistryOutputs == null)
                throw new InvalidDataException("生成归属清单格式无效，请修复 NetworkOutputManifest.json。");
            foreach (var record in manifest.Files)
            {
                if (record == null || string.IsNullOrWhiteSpace(record.Source) ||
                    record.Outputs == null || record.Outputs.Length == 0)
                    throw new InvalidDataException(
                        "生成归属清单的 Outputs 必须是非空 JSON 数组，即使只有一个文件：" +
                        (record?.Source ?? "未知协议"));
            }
            foreach (var output in manifest.Files.SelectMany(record => record.Outputs)
                .Concat(manifest.RegistryOutputs))
                if (output == null || string.IsNullOrWhiteSpace(output.Path) ||
                    string.IsNullOrWhiteSpace(output.Hash))
                    throw new InvalidDataException("生成归属清单缺少有效的输出路径或哈希。");
        }

        private static void AddOutput(Dictionary<string, byte[]> desired, List<NetworkOwnedOutput> owned,
            string path, byte[] bytes)
        {
            if (desired.ContainsKey(path)) throw new InvalidDataException("多个协议拥有同一输出：" + path);
            desired.Add(path, bytes);
            owned.Add(new NetworkOwnedOutput { Path = path, Hash = Hash(bytes) });
        }

        private static void Commit(string outputRoot, Dictionary<string, byte[]> desired,
            NetworkOutputManifest oldManifest, NetworkOutputManifest newManifest,
            Dictionary<string, string> businessFiles)
        {
            var oldOutputs = oldManifest.Files.SelectMany(record => record.Outputs)
                .Concat(oldManifest.RegistryOutputs)
                .ToDictionary(output => output.Path, StringComparer.OrdinalIgnoreCase);
            var newPaths = new HashSet<string>(newManifest.Files.SelectMany(record => record.Outputs)
                .Concat(newManifest.RegistryOutputs).Select(output => output.Path), StringComparer.OrdinalIgnoreCase);
            var touched = new HashSet<string>(desired.Keys, StringComparer.OrdinalIgnoreCase);
            touched.UnionWith(oldOutputs.Keys.Where(path => !newPaths.Contains(path)));
            foreach (string removed in oldOutputs.Keys.Where(path => !newPaths.Contains(path)))
            {
                if (File.Exists(NetworkGenerationSettings.ChildPath(outputRoot, removed + ".meta")))
                    touched.Add(removed + ".meta");
            }

            // 生成输出仍按清单校验，手写文件仅由骨架逻辑补充，不纳入覆盖清单。
            foreach (var oldOutput in oldOutputs.Values)
            {
                string path = NetworkGenerationSettings.ChildPath(outputRoot, oldOutput.Path);
                if (File.Exists(path) && Hash(File.ReadAllBytes(path)) != oldOutput.Hash)
                    throw new InvalidOperationException("生成文件已被手工修改，停止覆盖：" + oldOutput.Path);
            }

            foreach (string relative in desired.Keys)
            {
                if (relative != MANIFESTNAME &&
                    File.Exists(NetworkGenerationSettings.ChildPath(outputRoot, relative)) &&
                    !oldOutputs.ContainsKey(relative))
                    throw new InvalidOperationException("输出路径存在非本工具拥有的文件：" + relative);
            }

            // 合并两处目录的写入，业务骨架与生成输出一并提交和回滚。
            var changes = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string relative in touched)
            {
                desired.TryGetValue(relative, out var bytes);
                changes.Add(NetworkGenerationSettings.ChildPath(outputRoot, relative), bytes);
            }

            foreach (var businessFile in businessFiles)
                changes.Add(businessFile.Key, m_encoding.GetBytes(businessFile.Value));
            var backup = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            foreach (string path in changes.Keys)
                backup.Add(path, File.Exists(path) ? File.ReadAllBytes(path) : null);
            try
            {
                foreach (var change in changes.OrderBy(entry => entry.Key, StringComparer.Ordinal))
                {
                    if (change.Value == null)
                    {
                        File.Delete(change.Key);
                        continue;
                    }

                    if (backup[change.Key] != null && backup[change.Key].SequenceEqual(change.Value)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(change.Key));
                    File.WriteAllBytes(change.Key, change.Value);
                }
            }
            catch
            {
                foreach (var previous in backup)
                {
                    if (previous.Value == null) File.Delete(previous.Key);
                    else File.WriteAllBytes(previous.Key, previous.Value);
                }

                throw;
            }
        }

        internal static string Fingerprint(NetworkGenerationSettings settings,
            NetworkDescriptorReader reader, string source)
        {
            var builder = new StringBuilder();
            builder.Append("2\n").Append(settings.GeneratedNamespace).Append('\n')
                .Append(settings.ProtocolVersion).Append('\n');
            builder.Append(settings.ServiceNamespace).Append('\n')
                .Append(settings.ServiceAssembly).Append('\n').Append(settings.BusinessServiceRoot).Append('\n');
            string protocHash = Hash(File.ReadAllBytes(NetworkGenerationSettings.Resolve(settings.ProtocPath)));
            builder.Append(protocHash).Append('\n');
            foreach (string dependency in reader.DependencyClosure(source))
            {
                string path = LocateImport(settings, dependency);
                builder.Append(dependency).Append(':').Append(Hash(File.ReadAllBytes(path))).Append('\n');
            }
            return Hash(m_encoding.GetBytes(builder.ToString()));
        }

        private static string LocateImport(NetworkGenerationSettings settings, string source)
        {
            string found = null;
            foreach (string root in new[] { settings.ProtoRoot, settings.StandardInclude, settings.OptionsRoot })
            {
                string path = NetworkGenerationSettings.ChildPath(NetworkGenerationSettings.Resolve(root), source);
                if (!File.Exists(path)) continue;
                if (found != null) throw new InvalidOperationException("import 根目录遮蔽：" + source);
                found = path;
            }
            return found ?? throw new FileNotFoundException("找不到 import 文件。", source);
        }

        private static List<string> IncludeArguments(NetworkGenerationSettings settings)
        {
            return new List<string>
            {
                "--proto_path=" + NetworkGenerationSettings.Resolve(settings.ProtoRoot),
                "--proto_path=" + NetworkGenerationSettings.Resolve(settings.StandardInclude),
                "--proto_path=" + NetworkGenerationSettings.Resolve(settings.OptionsRoot)
            };
        }

        private static string RunProtoc(NetworkGenerationSettings settings, IEnumerable<string> arguments)
        {
            string executable = NetworkGenerationSettings.Resolve(settings.ProtocPath);
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = NetworkGenerationSettings.Resolve(settings.ProtoRoot),
                Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            using (var process = Process.Start(start))
            {
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    throw new TimeoutException("protoc 超过 30 秒，原输出保留。");
                }
                string error = stderr.GetAwaiter().GetResult();
                if (process.ExitCode != 0) throw new InvalidOperationException("protoc 失败：\n" + error);
                return stdout.GetAwaiter().GetResult();
            }
        }

        private static string QuoteArgument(string value)
        {
            if (value.Contains("\"") || value.EndsWith("\\", StringComparison.Ordinal))
                throw new InvalidOperationException("protoc 参数包含不支持的字符。");
            return "\"" + value + "\"";
        }

        internal static string Hash(byte[] bytes)
        {
            using (var algorithm = SHA256.Create())
                return BitConverter.ToString(algorithm.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }
    }
}
