namespace AlloyFramework.UI
{
    internal sealed class UINavigationRecord
    {
        internal int JumpID;
        internal EUIJumpMode JumpMode;
        internal EUIBackMode BackMode;
        internal int BackJumpID;
        internal UIHandle SourceHandle;
        internal UIHandle ReturnHandle;
        internal UIHandle TargetHandle;
    }
}
