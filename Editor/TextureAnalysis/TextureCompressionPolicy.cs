using UnityEditor;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 将复杂度映射到 ASTC 候选格式，并估算块压缩存储量。
    /// </summary>
    internal static class TextureCompressionPolicy
    {
        internal static TextureImporterFormat Recommend(float score, float low, float medium, float high)
        {
            if (score < low) return TextureImporterFormat.ASTC_8x8;
            if (score < medium) return TextureImporterFormat.ASTC_6x6;
            if (score < high) return TextureImporterFormat.ASTC_5x5;
            return TextureImporterFormat.ASTC_4x4;
        }

        internal static long EstimateBytes(int width, int height, int blockSize, bool mipmaps)
        {
            // ASTC 每块固定占用十六字节，每级 mip 分别向上对齐。
            long bytes = 0;
            while (true)
            {
                bytes += ((width + blockSize - 1L) / blockSize)
                    * ((height + blockSize - 1L) / blockSize) * 16;
                if (!mipmaps || (width == 1 && height == 1)) return bytes;
                width = System.Math.Max(1, width / 2);
                height = System.Math.Max(1, height / 2);
            }
        }
    }
}
