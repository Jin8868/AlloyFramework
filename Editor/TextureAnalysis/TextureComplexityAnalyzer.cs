using System;
using UnityEngine;

namespace AlloyFramework.Editor
{
    /// <summary>
    /// 根据多尺度颜色变化、二阶细节和局部高频区域估算纹理压缩难度。
    /// </summary>
    public static class TextureComplexityAnalyzer
    {
        private const int BLOCKSIZE = 8; // 与压缩候选块相近的局部统计范围。
        private const int HISTOGRAMSIZE = 4096; // 局部高分尾部的统计精度。

        /// <summary>
        /// 计算零至一的启发式复杂度，时间为 O(像素数)，额外空间为固定大小。
        /// 分数反映原始分辨率下的细节压缩风险，不代表实际 ASTC 编码误差或语义复杂度。
        /// </summary>
        /// <param name="pixels">按行排列的原始像素。</param>
        /// <param name="width">图片宽度。</param>
        /// <param name="height">图片高度。</param>
        /// <param name="useAlpha">是否计入透明度并忽略完全透明区域的隐藏颜色。</param>
        /// <returns>范围为零至一的复杂度；越大表示细节保护需求越高。</returns>
        /// <exception cref="ArgumentException">尺寸与像素数组不匹配。</exception>
        public static float Analyze(Color32[] pixels, int width, int height, bool useAlpha)
        {
            // 先验证尺寸，使用长整数避免乘法溢出绕过检查。
            if (pixels == null || width <= 0 || height <= 0 || (long)width * height != pixels.Length)
            {
                throw new ArgumentException("图片尺寸必须与像素数组长度一致。");
            }

            int[] histogram = new int[HISTOGRAMSIZE];
            double total = 0;
            int activeCount = 0;
            for (int blockY = 0; blockY < height; blockY += BLOCKSIZE)
            {
                for (int blockX = 0; blockX < width; blockX += BLOCKSIZE)
                {
                    double blockTotal = 0;
                    double blockSquares = 0;
                    int blockCount = 0;
                    int endY = blockY + Math.Min(BLOCKSIZE, height - blockY);
                    int endX = blockX + Math.Min(BLOCKSIZE, width - blockX);
                    for (int y = blockY; y < endY; y++)
                    {
                        for (int x = blockX; x < endX; x++)
                        {
                            // 不让透明留白稀释主体，轮廓从可见像素一侧采样即可。
                            if (useAlpha && pixels[y * width + x].a == 0)
                            {
                                continue;
                            }

                            float fine = MeasureScale(pixels, width, height, x, y, 1, useAlpha);
                            float medium = MeasureScale(pixels, width, height, x, y, 2, useAlpha);
                            float coarse = MeasureScale(pixels, width, height, x, y, 4, useAlpha);
                            float detail = 0.6f * fine + 0.25f * medium + 0.15f * coarse;
                            blockTotal += detail;
                            blockSquares += detail * detail;
                            blockCount++;
                        }
                    }

                    if (blockCount == 0)
                    {
                        continue;
                    }

                    // 均方根保护细线和文字，按有效像素加权消除边缘残块的面积偏差。
                    float localScore = (float)(0.5 * blockTotal / blockCount
                        + 0.5 * Math.Sqrt(blockSquares / blockCount));
                    int bucket = Mathf.Clamp(
                        Mathf.RoundToInt(localScore * (HISTOGRAMSIZE - 1)), 0, HISTOGRAMSIZE - 1);
                    histogram[bucket] += blockCount;
                    total += blockTotal;
                    activeCount += blockCount;
                }
            }

            if (activeCount == 0)
            {
                return 0;
            }

            // 取最高百分之五区域的均值，避免普通 P90 完全漏掉面积较小的高频细节。
            int tailCount = Math.Max(1, (int)Math.Ceiling(activeCount * 0.05));
            int remaining = tailCount;
            double tailTotal = 0;
            for (int bucket = histogram.Length - 1; bucket >= 0 && remaining > 0; bucket--)
            {
                int count = Math.Min(histogram[bucket], remaining);
                tailTotal += count * (bucket / (double)(HISTOGRAMSIZE - 1));
                remaining -= count;
            }

            return Mathf.Clamp01((float)(0.55 * total / activeCount + 0.45 * tailTotal / tailCount));
        }

        private static float MeasureScale(
            Color32[] pixels, int width, int height, int x, int y, int stride, bool useAlpha)
        {
            Vector4 center = Read(pixels[y * width + x], useAlpha);
            float total = 0;
            int directionCount = 0;
            // 四个方向同时覆盖水平、垂直和斜线，避免仅沿坐标轴观察造成偏差。
            for (int direction = 0; direction < 4; direction++)
            {
                int offsetX = direction == 1 ? 0 : stride;
                int offsetY = direction == 0 ? 0 : direction == 3 ? -stride : stride;
                int firstX = x - offsetX;
                int firstY = y - offsetY;
                int secondX = x + offsetX;
                int secondY = y + offsetY;
                bool hasFirst = firstX >= 0 && firstX < width && firstY >= 0 && firstY < height;
                bool hasSecond = secondX >= 0 && secondX < width && secondY >= 0 && secondY < height;
                if (!hasFirst && !hasSecond)
                {
                    continue;
                }

                Vector4 first = hasFirst ? Read(pixels[firstY * width + firstX], useAlpha) : center;
                Vector4 second = hasSecond ? Read(pixels[secondY * width + secondX], useAlpha) : center;
                float gradient = (Magnitude(center - first) + Magnitude(second - center))
                    / (hasFirst && hasSecond ? 2f : 1f);
                // 线性渐变二阶差为零；尖锐边缘、交替纹理与噪声具有较大的预测残差。
                // 缺失邻居时禁用二阶项，避免把图片边界误判成细节。
                float residual = hasFirst && hasSecond ? Magnitude(center - (first + second) * 0.5f) : 0;
                total += 0.35f * gradient + 0.65f * residual;
                directionCount++;
            }

            return directionCount == 0 ? 0 : total / directionCount;
        }

        private static Vector4 Read(Color32 pixel, bool useAlpha)
        {
            // 预乘颜色排除不可见 RGB，透明度独立参与比较以保留透明轮廓。
            float alpha = useAlpha ? pixel.a / 255f : 1f;
            return new Vector4(pixel.r / 255f * alpha, pixel.g / 255f * alpha, pixel.b / 255f * alpha, alpha);
        }

        private static float Magnitude(Vector4 difference)
        {
            // 使用最敏感通道保护等亮度彩色边缘，避免 RGB 平均值稀释单通道变化。
            return Mathf.Max(Mathf.Max(Mathf.Abs(difference.x), Mathf.Abs(difference.y)),
                Mathf.Max(Mathf.Abs(difference.z), Mathf.Abs(difference.w)));
        }
    }
}
