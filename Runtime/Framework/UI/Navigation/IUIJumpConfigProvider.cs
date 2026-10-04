using System.Collections.Generic;

namespace AlloyFramework.UI
{
    public interface IUIJumpConfigProvider
    {
        /// <summary>
        /// 尝试按跳转配置 ID 获取配置。
        /// </summary>
        /// <param name="jumpID">跳转配置的唯一标识。</param>
        /// <param name="config">成功时返回跳转配置。</param>
        /// <returns>找到配置时返回 true，否则返回 false。</returns>
        bool TryGet(int jumpID, out UIJumpConfig config);

        /// <summary>
        /// 获取全部跳转配置，用于启动阶段校验。
        /// </summary>
        /// <returns>全部跳转配置。</returns>
        IEnumerable<UIJumpConfig> GetAll();
    }
}
