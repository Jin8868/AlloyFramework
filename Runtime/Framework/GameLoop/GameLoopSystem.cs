using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    [FrameworkSystem(FrameworkSystemPriority.GameLoop)]
    internal sealed class GameLoopSystem : FrameworkSystem
    {
        public override UniTask InitializeAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            GameLoop.Initialize();
            return UniTask.CompletedTask;
        }

        public override void Shutdown()
        {
            GameLoop.Shutdown();
        }
    }
}
