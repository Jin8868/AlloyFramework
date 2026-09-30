using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    public interface IFrameworkSystem
    {
        UniTask InitializeAsync(
            FrameworkContext context,
            CancellationToken cancellationToken);

        void Shutdown();
    }

    public abstract class FrameworkSystem : IFrameworkSystem
    {
        public abstract UniTask InitializeAsync(
            FrameworkContext context,
            CancellationToken cancellationToken);

        public virtual void Shutdown()
        {
        }
    }
}
