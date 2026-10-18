using System;

namespace AlloyFramework
{
    /// <summary>主包提供的启动类型和入口信息，不提前持有运行时服务实例。</summary>
    public sealed class GameStartupConfiguration
    {
        /// <summary>代码准备服务的具体实现类型。</summary>
        public Type CodePreparationType { get; }

        /// <summary>入口加载器的具体实现类型。</summary>
        public Type GameEntryLoaderType { get; }

        /// <summary>业务程序集名称。</summary>
        public string RuntimeAssemblyName { get; }

        /// <summary>业务安装入口的完整类型名称。</summary>
        public string InstallerTypeName { get; }

        /// <summary>声明由各启动步骤按需创建的类型及业务入口。</summary>
        /// <param name="codePreparationType">公开构造参数为准备结果、程序集名称和安装类型名称的代码准备类型。</param>
        /// <param name="gameEntryLoaderType">公开构造参数为准备结果的入口加载器类型。</param>
        /// <param name="runtimeAssemblyName">业务程序集名称，不含扩展名。</param>
        /// <param name="installerTypeName">业务安装入口的完整类型名称。</param>
        /// <exception cref="ArgumentNullException">实现类型为空。</exception>
        /// <exception cref="ArgumentException">实现类型、构造函数或业务入口名称不合法。</exception>
        public GameStartupConfiguration(
            Type codePreparationType,
            Type gameEntryLoaderType,
            string runtimeAssemblyName,
            string installerTypeName)
        {
            // 提前校验构造约定，避免资源初始化后才发现实现类型配置错误。
            ValidateImplementation(codePreparationType, typeof(IGameCodePreparation), nameof(codePreparationType));
            ValidateImplementation(gameEntryLoaderType, typeof(IGameEntryLoader), nameof(gameEntryLoaderType));
            if (codePreparationType.GetConstructor(new[]
                {
                    typeof(GameCodePreparationResult), typeof(string), typeof(string)
                }) == null)
            {
                throw new ArgumentException("代码准备类型缺少约定的公开构造函数。", nameof(codePreparationType));
            }

            if (gameEntryLoaderType.GetConstructor(new[] { typeof(GameCodePreparationResult) }) == null)
            {
                throw new ArgumentException("入口加载器类型缺少约定的公开构造函数。", nameof(gameEntryLoaderType));
            }

            if (string.IsNullOrWhiteSpace(runtimeAssemblyName) || string.IsNullOrWhiteSpace(installerTypeName))
            {
                throw new ArgumentException("业务程序集名称和安装入口类型名称不能为空。");
            }

            CodePreparationType = codePreparationType;
            GameEntryLoaderType = gameEntryLoaderType;
            RuntimeAssemblyName = runtimeAssemblyName;
            InstallerTypeName = installerTypeName;
        }

        private static void ValidateImplementation(Type implementationType, Type contractType, string parameterName)
        {
            if (implementationType == null)
            {
                throw new ArgumentNullException(parameterName);
            }

            if (!implementationType.IsClass || implementationType.IsAbstract ||
                implementationType.ContainsGenericParameters || !contractType.IsAssignableFrom(implementationType))
            {
                throw new ArgumentException("启动实现必须是对应接口的非抽象、非泛型类。", parameterName);
            }
        }
    }
}
