using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 从 Player 已加载的程序集中查找并创建唯一的业务入口。
    /// </summary>
    public sealed class DefaultGameEntryLoader : IGameEntryLoader
    {
        public UniTask<IGameEntry> LoadAsync(
            FrameworkContext context,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return UniTask.FromResult(GameEntryActivator.CreateFromLoadedAssemblies());
        }
    }
}