using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    public interface IGameEntry
    {
        UniTask MainAsync(CancellationToken cancellationToken);

        void Shutdown();
    }
}
