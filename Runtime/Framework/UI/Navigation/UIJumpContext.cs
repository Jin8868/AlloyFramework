using System.Threading;
using Cysharp.Threading.Tasks;

namespace AlloyFramework.UI
{
    public delegate UniTask UIJumpPrepareHandler(
        UIJumpContext context,
        CancellationToken cancellationToken);

    public sealed class UIJumpContext
    {
        /// <summary>本次使用的跳转配置 ID。</summary>
        public int JumpID { get; internal set; }
        /// <summary>本次跳转解析出的只读配置。</summary>
        public UIJumpConfig Config { get; internal set; }
        /// <summary>调用方传入的原始业务数据。</summary>
        public object InputData { get; internal set; }
        /// <summary>最终传递给目标 UI 的数据。</summary>
        public object TargetData { get; set; }
    }
}
