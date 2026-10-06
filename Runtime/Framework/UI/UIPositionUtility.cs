using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>提供 UI 跨层定位、屏幕位置转换和世界位置投影。</summary>
    public static class UIPositionUtility
    {
        /// <summary>将来源节点局部点转换到目标容器局部坐标。</summary>
        /// <param name="sourceRect">来源 UI 节点。</param>
        /// <param name="sourceLocalPoint">来源节点局部点。</param>
        /// <param name="targetRect">接收坐标的目标容器。</param>
        /// <param name="targetLocalPoint">成功时返回目标局部坐标。</param>
        /// <returns>两个节点的相机环境有效且投影成功时返回 true。</returns>
        public static bool TryConvertLocalPoint(
            RectTransform sourceRect,
            Vector2 sourceLocalPoint,
            RectTransform targetRect,
            out Vector2 targetLocalPoint)
        {
            targetLocalPoint = default;
            if (!TryGetCanvasCamera(sourceRect, out var sourceCamera))
            {
                return false;
            }

            var worldPoint = sourceRect.TransformPoint(sourceLocalPoint);
            return TryProjectPoint(sourceCamera, worldPoint, targetRect, out targetLocalPoint);
        }

        /// <summary>将来源节点四角转换到目标容器局部坐标，保留旋转后的四边形。</summary>
        /// <param name="sourceRect">来源 UI 节点。</param>
        /// <param name="targetRect">接收坐标的目标容器。</param>
        /// <param name="corners">调用方缓存的至少四元素数组，依次为左下、左上、右上、右下。</param>
        /// <returns>四角全部转换成功时返回 true；失败时数组可能包含部分结果。</returns>
        public static bool TryGetLocalCorners(
            RectTransform sourceRect,
            RectTransform targetRect,
            Vector3[] corners)
        {
            if (corners == null || corners.Length < 4 ||
                !TryGetCanvasCamera(sourceRect, out var sourceCamera) ||
                !TryGetCanvasCamera(targetRect, out var targetCamera))
            {
                return false;
            }

            // 复用调用方数组，避免跨层跟踪时每帧分配四角缓冲。
            sourceRect.GetWorldCorners(corners);
            for (var index = 0; index < 4; index++)
            {
                if (!TryProjectPoint(
                        sourceCamera, corners[index], targetRect, targetCamera, out var localPoint))
                {
                    return false;
                }

                corners[index] = new Vector3(localPoint.x, localPoint.y, 0f);
            }

            return true;
        }

        /// <summary>取得来源节点在目标容器中的轴对齐包围矩形，不计算遮罩裁剪。</summary>
        /// <param name="sourceRect">来源 UI 节点。</param>
        /// <param name="targetRect">接收坐标的目标容器。</param>
        /// <param name="bounds">成功时返回目标局部坐标中的包围矩形。</param>
        /// <returns>四角全部转换成功时返回 true。</returns>
        public static bool TryGetLocalBounds(
            RectTransform sourceRect,
            RectTransform targetRect,
            out Rect bounds)
        {
            bounds = default;
            if (!TryGetCanvasCamera(sourceRect, out var sourceCamera) ||
                !TryGetCanvasCamera(targetRect, out var targetCamera))
            {
                return false;
            }

            // 从局部矩形逐角投影，无需创建临时数组。
            var sourceBounds = sourceRect.rect;
            var minimum = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
            var maximum = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
            for (var index = 0; index < 4; index++)
            {
                var sourceCorner = new Vector3(
                    index < 2 ? sourceBounds.xMin : sourceBounds.xMax,
                    index == 0 || index == 3 ? sourceBounds.yMin : sourceBounds.yMax,
                    0f);
                var worldCorner = sourceRect.TransformPoint(sourceCorner);
                if (!TryProjectPoint(
                        sourceCamera, worldCorner, targetRect, targetCamera, out var localCorner))
                {
                    return false;
                }

                minimum = Vector2.Min(minimum, localCorner);
                maximum = Vector2.Max(maximum, localCorner);
            }

            bounds = Rect.MinMaxRect(minimum.x, minimum.y, maximum.x, maximum.y);
            return true;
        }

        /// <summary>将屏幕像素位置转换到 UI 容器局部坐标。</summary>
        /// <param name="targetRect">接收坐标的目标容器。</param>
        /// <param name="screenPoint">屏幕像素位置。</param>
        /// <param name="localPoint">成功时返回目标局部坐标。</param>
        /// <returns>射线与目标平面相交时返回 true，不代表位置位于矩形内部。</returns>
        public static bool TryScreenToLocalPoint(
            RectTransform targetRect,
            Vector2 screenPoint,
            out Vector2 localPoint)
        {
            localPoint = default;
            return TryGetCanvasCamera(targetRect, out var targetCamera) &&
                   RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       targetRect, screenPoint, targetCamera, out localPoint);
        }

        /// <summary>将场景世界位置投影到 UI 容器局部坐标。</summary>
        /// <param name="worldCamera">渲染场景位置的相机。</param>
        /// <param name="worldPosition">场景世界位置。</param>
        /// <param name="targetRect">接收坐标的 UI 容器。</param>
        /// <param name="localPoint">成功时返回目标局部坐标。</param>
        /// <returns>位置位于相机前方且投影成功时返回 true。</returns>
        public static bool TryWorldToLocalPoint(
            Camera worldCamera,
            Vector3 worldPosition,
            RectTransform targetRect,
            out Vector2 localPoint)
        {
            localPoint = default;
            return worldCamera != null &&
                   TryProjectPoint(worldCamera, worldPosition, targetRect, out localPoint);
        }

        /// <summary>将目标节点的 Pivot 放置到指定屏幕位置，保持锚点、尺寸和旋转。</summary>
        /// <param name="targetRect">需要移动的 UI 节点。</param>
        /// <param name="screenPoint">目标屏幕像素位置。</param>
        /// <returns>存在 RectTransform 父容器且位置转换成功时返回 true。</returns>
        public static bool TrySetScreenPosition(RectTransform targetRect, Vector2 screenPoint)
        {
            if (targetRect == null || !(targetRect.parent is RectTransform parentRect) ||
                !TryScreenToLocalPoint(parentRect, screenPoint, out var localPoint))
            {
                return false;
            }

            // 写入父空间 Pivot 位置，Unity 会按现有锚点推导 anchoredPosition。
            var localPosition = targetRect.localPosition;
            targetRect.localPosition = new Vector3(localPoint.x, localPoint.y, localPosition.z);
            return true;
        }

        private static bool TryGetCanvasCamera(RectTransform rectTransform, out Camera camera)
        {
            camera = null;
            if (rectTransform == null)
            {
                return false;
            }

            var canvas = rectTransform.GetComponentInParent<Canvas>();
            if (canvas == null)
            {
                return false;
            }

            // 嵌套 Canvas 继承根画布的渲染方式和相机，Overlay 使用空相机。
            var rootCanvas = canvas.rootCanvas;
            if (rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                return true;
            }

            camera = rootCanvas.worldCamera;
            return camera != null;
        }

        private static bool TryProjectPoint(
            Camera sourceCamera,
            Vector3 worldPoint,
            RectTransform targetRect,
            out Vector2 localPoint)
        {
            localPoint = default;
            return TryGetCanvasCamera(targetRect, out var targetCamera) &&
                   TryProjectPoint(sourceCamera, worldPoint, targetRect, targetCamera, out localPoint);
        }

        private static bool TryProjectPoint(
            Camera sourceCamera,
            Vector3 worldPoint,
            RectTransform targetRect,
            Camera targetCamera,
            out Vector2 localPoint)
        {
            localPoint = default;
            Vector2 screenPoint;
            if (sourceCamera != null)
            {
                var projectedPoint = sourceCamera.WorldToScreenPoint(worldPoint);
                if (projectedPoint.z <= 0f)
                {
                    return false;
                }

                screenPoint = new Vector2(projectedPoint.x, projectedPoint.y);
            }
            else
            {
                screenPoint = RectTransformUtility.WorldToScreenPoint(null, worldPoint);
            }

            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                targetRect, screenPoint, targetCamera, out localPoint);
        }
    }
}
