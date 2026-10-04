using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 提供已加载业务配置表的统一注册和获取入口。
    /// </summary>
    public sealed class AlloyConfig
    {
        private static readonly AlloyConfig Singleton = new AlloyConfig();

        private readonly Dictionary<Type, object> m_tables = new Dictionary<Type, object>(); // 已注册的配置表适配器。
        private IAlloyConfigLoader m_loader; // 当前项目提供的配置加载器。

        private AlloyConfig()
        {
        }

        /// <summary>
        /// 获取全局配置表访问入口。
        /// </summary>
        public static AlloyConfig Instance => Singleton;

        /// <summary>
        /// 获取全部配置是否已经完成加载。
        /// </summary>
        public bool IsLoaded { get; private set; }

        /// <summary>
        /// 设置当前项目的配置加载器。
        /// </summary>
        /// <param name="loader">负责加载项目配置的业务加载器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="loader"/> 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">当配置已经加载时抛出。</exception>
        public void SetLoader(IAlloyConfigLoader loader)
        {
            if (loader == null)
            {
                throw new ArgumentNullException(nameof(loader));
            }

            if (IsLoaded)
            {
                throw new InvalidOperationException("配置已经加载，不能替换配置加载器。");
            }

            m_loader = loader;
        }

        /// <summary>
        /// 注册已加载的配置表。
        /// </summary>
        /// <typeparam name="TConfig">配置记录的运行时类型。</typeparam>
        /// <param name="table">要注册的配置表适配器。</param>
        /// <exception cref="ArgumentNullException">当 <paramref name="table"/> 为 null 时抛出。</exception>
        /// <exception cref="InvalidOperationException">当同类型配置表已经注册时抛出。</exception>
        public void Register<TConfig>(IAlloyConfigTable<TConfig> table) where TConfig : class
        {
            if (table == null)
            {
                throw new ArgumentNullException(nameof(table));
            }

            var configType = typeof(TConfig);
            if (m_tables.ContainsKey(configType))
            {
                throw new InvalidOperationException($"配置表已经注册：{configType.FullName}。");
            }

            m_tables.Add(configType, table);
        }

        /// <summary>
        /// 获取指定 ID 的配置记录。
        /// </summary>
        /// <typeparam name="TConfig">配置记录的运行时类型。</typeparam>
        /// <param name="configID">配置记录的唯一标识。</param>
        /// <returns>指定 ID 的配置记录。</returns>
        /// <exception cref="InvalidOperationException">当配置未加载或表未注册时抛出。</exception>
        /// <exception cref="KeyNotFoundException">当配置记录不存在时抛出。</exception>
        public TConfig Get<TConfig>(int configID) where TConfig : class
        {
            if (!IsLoaded)
            {
                throw new InvalidOperationException("配置尚未加载完成。");
            }

            if (!m_tables.TryGetValue(typeof(TConfig), out var registeredTable))
            {
                throw new InvalidOperationException($"未注册配置表：{typeof(TConfig).FullName}。");
            }

            var table = (IAlloyConfigTable<TConfig>)registeredTable;
            if (table.TryGet(configID, out var config))
            {
                return config;
            }

            throw new KeyNotFoundException($"配置不存在：{typeof(TConfig).FullName}，ID={configID}。");
        }

        /// <summary>
        /// 尝试获取指定 ID 的配置记录。
        /// </summary>
        /// <typeparam name="TConfig">配置记录的运行时类型。</typeparam>
        /// <param name="configID">配置记录的唯一标识。</param>
        /// <param name="config">成功时返回配置记录。</param>
        /// <returns>找到且类型匹配时返回 true，否则返回 false。</returns>
        public bool TryGet<TConfig>(int configID, out TConfig config) where TConfig : class
        {
            if (IsLoaded &&
                m_tables.TryGetValue(typeof(TConfig), out var registeredTable) &&
                registeredTable is IAlloyConfigTable<TConfig> table &&
                table.TryGet(configID, out config))
            {
                return true;
            }

            config = null;
            return false;
        }

        internal async UniTask LoadAllAsync(CancellationToken cancellationToken)
        {
            if (IsLoaded)
            {
                return;
            }

            if (m_loader == null)
            {
                throw new InvalidOperationException("项目尚未注册配置加载器。");
            }

            try
            {
                // 由业务加载器一次性加载数据并注册所有需要公开的配置表。
                await m_loader.LoadAsync(this, cancellationToken);
                IsLoaded = true;
            }
            catch
            {
                // 加载失败时不保留半成品配置表或资源。
                m_tables.Clear();
                m_loader.Unload();
                throw;
            }
        }

        internal void UnloadAll()
        {
            // 先释放业务加载器持有的资源，再清空对配置表的引用。
            m_loader?.Unload();
            m_tables.Clear();
            IsLoaded = false;
        }
    }
}
