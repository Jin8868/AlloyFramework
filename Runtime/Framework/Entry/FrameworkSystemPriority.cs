namespace AlloyFramework
{
    public enum FrameworkSystemPriority
    {
        GameLoop = 0,
        Timer = 100,
        Resource = 1000,
        Config = 2000,
        /// <summary>在 UI 前初始化，在 UI 关闭后释放业务 Model。</summary>
        Model = 2500,
        UI = 3000
    }
}
