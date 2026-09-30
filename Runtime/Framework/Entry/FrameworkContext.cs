using System;
using System.Collections.Generic;

namespace AlloyFramework
{
    public sealed class FrameworkContext
    {
        private readonly Dictionary<Type, object> _services =
            new Dictionary<Type, object>();

        public TService GetRequired<TService>() where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out var service))
            {
                return (TService)service;
            }

            throw new InvalidOperationException(
                $"Framework service is not initialized: {typeof(TService).FullName}");
        }

        public bool TryGet<TService>(out TService service) where TService : class
        {
            if (_services.TryGetValue(typeof(TService), out var value))
            {
                service = (TService)value;
                return true;
            }

            service = null;
            return false;
        }

        internal void Register(FrameworkSystem system)
        {
            RegisterService(system.GetType(), system);

            var interfaces = system.GetType().GetInterfaces();
            for (var index = 0; index < interfaces.Length; index++)
            {
                var serviceType = interfaces[index];
                if (serviceType == typeof(IFrameworkSystem) ||
                    !typeof(IFrameworkSystem).IsAssignableFrom(serviceType))
                {
                    continue;
                }

                RegisterService(serviceType, system);
            }
        }

        internal void Clear()
        {
            _services.Clear();
        }

        private void RegisterService(Type serviceType, object service)
        {
            if (_services.ContainsKey(serviceType))
            {
                throw new InvalidOperationException(
                    $"Framework service has already been initialized: {serviceType.FullName}");
            }

            _services.Add(serviceType, service);
        }
    }
}
