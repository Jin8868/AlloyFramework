using System;
using System.Collections.Generic;

namespace AlloyFramework.Net
{
    /// <summary>按类型管理项目创建的网络服务实例及其释放顺序。</summary>
    public sealed class NetworkServiceRegistry
    {
        private readonly Dictionary<Type, object> m_services = new Dictionary<Type, object>(); // 当前服务实例。
        private readonly List<IDisposable> m_disposalOrder = new List<IDisposable>(); // 实例的创建顺序。

        /// <summary>注册已经创建的服务，成功后由注册表负责释放。</summary>
        /// <typeparam name="TService">服务的获取类型。</typeparam>
        /// <param name="service">项目创建的服务实例。</param>
        /// <exception cref="ArgumentNullException">服务为空。</exception>
        /// <exception cref="InvalidOperationException">服务类型已经注册。</exception>
        public void Register<TService>(TService service) where TService : class
        {
            // 注册表只管理实例，不参与具体服务的构造。
            if (service == null)
            {
                throw new ArgumentNullException(nameof(service));
            }

            if (m_services.ContainsKey(typeof(TService)))
            {
                throw new InvalidOperationException($"网络服务重复注册：{typeof(TService).Name}。");
            }

            m_services.Add(typeof(TService), service);
            if (service is IDisposable disposable)
            {
                m_disposalOrder.Add(disposable);
            }
        }

        /// <summary>获取已注册的共享网络服务。</summary>
        /// <typeparam name="TService">注册的服务类型。</typeparam>
        /// <returns>当前生命周期的服务实例。</returns>
        /// <exception cref="InvalidOperationException">服务类型未注册。</exception>
        public TService Get<TService>() where TService : class
        {
            if (!m_services.TryGetValue(typeof(TService), out var service))
            {
                throw new InvalidOperationException($"网络服务未注册：{typeof(TService).Name}。");
            }

            return (TService)service;
        }

        /// <summary>清空注册表并按注册逆序释放服务。</summary>
        public void ReleaseServices()
        {
            // 先禁止获取，再逆序释放；单个异常不阻止其他服务清理。
            m_services.Clear();
            for (int index = m_disposalOrder.Count - 1; index >= 0; index--)
            {
                try
                {
                    m_disposalOrder[index].Dispose();
                }
                catch (Exception exception)
                {
                    AlloyFramework.AlloyDebug.Error(exception);
                }
            }

            m_disposalOrder.Clear();
        }
    }
}
