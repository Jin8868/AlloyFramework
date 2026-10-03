using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework
{
    /// <summary>
    /// 在框架系统初始化完成后创建业务入口。实现必须位于 Player 可直接执行的 AOT 程序集中。
    /// </summary>
    public interface IGameEntryLoader
    {
        UniTask<IGameEntry> LoadAsync(
            FrameworkContext context,
            CancellationToken cancellationToken);
    }
}