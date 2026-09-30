using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    public sealed class HotUpdateRunner
    {
        private readonly List<IHotUpdateProvider> _providers = new List<IHotUpdateProvider>();
        private bool _running;

        public event Action<HotUpdateProgress> PackageProgressChanged;
        public event Action<float> TotalProgressChanged;

        public void AddPackage(string packageName)
        {
            if (_running) throw new InvalidOperationException("Cannot add a package while updating.");
            for (var index = 0; index < _providers.Count; index++)
            {
                if (_providers[index].PackageName == packageName)
                    throw new InvalidOperationException($"Package is already in the update list: {packageName}");
            }

            _providers.Add(ResourceManager.Instance.CreateHotUpdateProvider(packageName));
        }

        public async UniTask RunAllAsync(CancellationToken cancellationToken = default)
        {
            if (_running) throw new InvalidOperationException("Hot update is already running.");
            _running = true;

            try
            {
                if (_providers.Count == 0)
                {
                    TotalProgressChanged?.Invoke(1f);
                    return;
                }

                for (var index = 0; index < _providers.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var completedCount = index;
                    var provider = _providers[index];
                    void Report(HotUpdateProgress progress)
                    {
                        PackageProgressChanged?.Invoke(progress);
                        TotalProgressChanged?.Invoke((completedCount + progress.Fraction) / _providers.Count);
                    }

                    provider.ProgressChanged += Report;
                    try
                    {
                        await provider.RunAsync(cancellationToken);
                    }
                    finally
                    {
                        provider.ProgressChanged -= Report;
                    }

                    TotalProgressChanged?.Invoke((index + 1f) / _providers.Count);
                }
            }
            finally
            {
                _running = false;
            }
        }
    }
}
