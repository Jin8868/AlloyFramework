using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.Timing
{
    [FrameworkSystem(FrameworkSystemPriority.Timer)]
    internal sealed class TimerSystem : FrameworkSystem
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
