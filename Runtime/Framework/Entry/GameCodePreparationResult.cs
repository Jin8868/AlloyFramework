using System.Reflection;

namespace AlloyFramework
{
    /// <summary>
    /// 已准备的业务程序集与发布身份，不持有 DLL 字节。
    /// </summary>
    public sealed class GameCodePreparationResult
    {
        public Assembly RuntimeAssembly { get; set; }
        public string ReleaseID { get; set; }
        public string BaselineID { get; set; }
        public bool IsCompleted { get; set; }
        public bool RequiresRestart { get; set; }
    }
}
