using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Timing
{
    internal sealed class TimerManager : FrameworkSystem
    {
        public override UniTask InitializeAsync(FrameworkContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AlloyTimer.Initialize();
            return UniTask.CompletedTask;
        }

        public override void Shutdown() => AlloyTimer.Shutdown();
    }
}
