using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    internal sealed class GameLoopManager : FrameworkSystem
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
