namespace AlloyFramework
{
    /// <summary>
    /// 所有业务 Model 和 UIModel 的共同基类，生命周期由 ModelManager 管理。
    /// </summary>
    public abstract class ModelBase
    {
        /// <summary>创建后初始化数据和订阅，不执行异步加载。</summary>
        protected virtual void OnInitialize() { }

        /// <summary>恢复初始业务数据，保留实例和长期订阅。</summary>
        protected virtual void OnReset() { }

        /// <summary>取消订阅、停止任务并释放资源；初始化失败时也可能调用。</summary>
        protected virtual void OnDispose() { }

        internal void Initialize() => OnInitialize();

        internal void Reset() => OnReset();

        internal void Dispose() => OnDispose();
    }
}
