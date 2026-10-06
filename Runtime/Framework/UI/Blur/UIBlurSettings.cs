using System;

namespace AlloyFramework.UI
{
    /// <summary>背景模糊纹理的更新方式。</summary>
    public enum EUIBlurUpdateMode
    {
        /// <summary>每个渲染帧更新背景。</summary>
        Realtime,
        /// <summary>打开时捕获一次；边界、配置或尺寸变化时重新捕获。</summary>
        Static
    }

    /// <summary>项目级背景模糊配置，不修改预制体。</summary>
    [Serializable]
    public sealed class UIBlurSettings
    {
        /// <summary>更新模式，低性能设备可由业务切换为 Static。</summary>
        public EUIBlurUpdateMode UpdateMode = EUIBlurUpdateMode.Realtime;
        /// <summary>宽高分别缩小的倍数，只支持 2 或 4。</summary>
        public int Downsample = 4;
        /// <summary>横向和纵向模糊的迭代次数，范围为 1 至 4。</summary>
        public int Iterations = 2;
        /// <summary>在降采样纹理上的采样间距，范围为 0.5 至 4。</summary>
        public float Radius = 1f;
        /// <summary>背景 UI 使用的专用 Unity Layer 名称。</summary>
        public string BackgroundLayerName = "UI";
        /// <summary>需要项目手动创建的前景专用 Unity Layer 名称。</summary>
        public string ForegroundLayerName = "UIBlurForeground";
        /// <summary>等待首张纹理的最大实际秒数，超时提示检查 Renderer Feature。</summary>
        public float CaptureTimeoutSeconds = 5f;
        /// <summary>URP 前景相机的 Renderer 索引，-1 使用默认；必须与源 UICamera 一致。</summary>
        public int URPRendererIndex = -1;

        internal UIBlurSettings CopyValidated()
        {
            if (!Enum.IsDefined(typeof(EUIBlurUpdateMode), UpdateMode) ||
                (Downsample != 2 && Downsample != 4) || Iterations < 1 || Iterations > 4 ||
                float.IsNaN(Radius) || Radius < 0.5f || Radius > 4f ||
                float.IsNaN(CaptureTimeoutSeconds) || float.IsInfinity(CaptureTimeoutSeconds) ||
                CaptureTimeoutSeconds <= 0f || URPRendererIndex < -1 ||
                string.IsNullOrWhiteSpace(BackgroundLayerName) ||
                string.IsNullOrWhiteSpace(ForegroundLayerName) || BackgroundLayerName == ForegroundLayerName)
            {
                throw new ArgumentException("UI 模糊配置无效，请检查更新模式、降采样、迭代、半径、超时和专用 Layer。");
            }

            return (UIBlurSettings)MemberwiseClone();
        }
    }
}
