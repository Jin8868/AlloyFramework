using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace AlloyFramework.GM
{
    /// <summary>从业务显式提供的对象中读取 GM 命令声明。</summary>
    public sealed class GMRegistry
    {
        private readonly List<GMCommand> m_commands = new List<GMCommand>(); // 当前注册的命令。
        private readonly List<object> m_sources = new List<object>(); // 已注册的命令对象。
        private bool m_isSealed; // 生成面板后不再接受注册。

        internal GMRegistry(IStartupLogViewer logViewer)
        {
            // 复用启动时安装的查看器，只有启用 GM 才绑定日志按钮。
            if (logViewer != null) m_commands.Add(new GMCommand("查看日志", logViewer.Show));
        }

        /// <summary>注册对象上的 GM 命令，同一对象只注册一次。命令方法应标注 Preserve。</summary>
        /// <param name="source">包含 GM 命令方法的业务对象。</param>
        /// <exception cref="ArgumentNullException">命令来源为空。</exception>
        /// <exception cref="InvalidOperationException">本轮注册已结束。</exception>
        public void Register(object source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (m_isSealed)
            {
                throw new InvalidOperationException("GM 命令注册已结束。");
            }

            // 按对象身份去重，不受业务类型的相等性定义影响。
            foreach (var registeredSource in m_sources)
            {
                if (ReferenceEquals(registeredSource, source))
                {
                    return;
                }
            }

            // 限制反射范围，只绑定业务显式提供的对象及类型上的命令。
            var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
            var commands = new List<GMCommand>();
            foreach (var method in source.GetType().GetMethods(flags))
            {
                var attribute = method.GetCustomAttribute<GMCommandAttribute>();
                if (attribute == null)
                {
                    continue;
                }

                // 单条声明无效时告警，其他命令仍可使用。
                var command = Bind(source, method, attribute.Label);
                if (command == null)
                {
                    Debug.LogWarning($"GM 命令声明无效：{method.DeclaringType.FullName}.{method.Name}");
                    continue;
                }
                commands.Add(command);
            }

            m_commands.AddRange(commands);
            m_sources.Add(source);
        }

        internal IReadOnlyList<GMCommand> Seal()
        {
            m_isSealed = true;
            m_sources.Clear();
            m_commands.Sort(CompareLabels);
            return m_commands.AsReadOnly();
        }

        private static int CompareLabels(GMCommand left, GMCommand right)
        {
            return string.CompareOrdinal(left.Label, right.Label);
        }

        private static GMCommand Bind(object source, MethodInfo method, string label)
        {
            if (string.IsNullOrWhiteSpace(label) || method.ContainsGenericParameters ||
                method.ReturnType != typeof(void))
            {
                return null;
            }

            // 只接受三种签名，将调用绑定为强类型委托供面板复用。
            var parameters = method.GetParameters();
            if (parameters.Length == 0)
            {
                return new GMCommand(label, BindDelegate<Action>(source, method));
            }
            if (parameters.Length != 1)
            {
                return null;
            }
            if (parameters[0].ParameterType == typeof(bool))
            {
                return new GMCommand(label, BindDelegate<Action<bool>>(source, method));
            }
            if (parameters[0].ParameterType == typeof(string))
            {
                return new GMCommand(label, BindDelegate<Action<string>>(source, method));
            }
            return null;
        }

        private static TDelegate BindDelegate<TDelegate>(object source, MethodInfo method) where TDelegate : Delegate
        {
            var delegateType = typeof(TDelegate);
            return (TDelegate)(method.IsStatic
                ? method.CreateDelegate(delegateType)
                : method.CreateDelegate(delegateType, source));
        }
    }
}
