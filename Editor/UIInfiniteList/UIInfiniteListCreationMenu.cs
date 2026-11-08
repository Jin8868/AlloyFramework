using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AlloyFramework.Editor
{
    /// <summary>创建已配置视口和内容节点的无限列表。</summary>
    internal static class UIInfiniteListCreationMenu
    {
        [MenuItem("GameObject/★AlloyFramework★/UI/无限列表", false, 11)]
        private static void CreateList(MenuCommand menuCommand)
        {
            CreateConfiguredList(menuCommand, false);
        }

        [MenuItem("GameObject/★AlloyFramework★/UI/异形无限列表", false, 12)]
        private static void CreateIrregularList(MenuCommand menuCommand)
        {
            CreateConfiguredList(menuCommand, true);
        }

        private static void CreateConfiguredList(MenuCommand menuCommand, bool irregular)
        {
            // 将新增 Canvas 和列表合并到一次撤销操作中。
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("创建无限列表");
            var parent = menuCommand.context as GameObject;
            if (parent == null) parent = Selection.activeGameObject;
            if (parent == null || parent.GetComponentInParent<Canvas>() == null)
            {
                var canvasObject = new GameObject(
                    "Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                GameObjectUtility.SetParentAndAlign(canvasObject, parent);
                canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                int uiLayer = LayerMask.NameToLayer("UI");
                if (uiLayer >= 0) canvasObject.layer = uiLayer;
                Undo.RegisterCreatedObjectUndo(canvasObject, "创建列表 Canvas");
                parent = canvasObject;
            }

            // 根节点保留射线目标，使空白区域也能接收拖拽和滚轮。
            System.Type listType = irregular ? typeof(UIIrregularList) : typeof(UIInfiniteList);
            string nodeName = irregular ? "rlist_New" : "list_New";
            var root = new GameObject(nodeName, typeof(RectTransform), typeof(Image), listType);
            GameObjectUtility.SetParentAndAlign(root, parent);
            GameObjectUtility.EnsureUniqueNameForSibling(root);
            var rootRect = (RectTransform)root.transform;
            rootRect.sizeDelta = new Vector2(400f, 400f);
            root.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.05f);

            var viewport = CreateChild("Viewport", root.transform);
            viewport.gameObject.AddComponent<RectMask2D>();
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            var content = CreateChild("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = Vector2.zero;

            // 默认纵向单列，直接满足虚拟列表的结构和滚动轴约束。
            var list = root.GetComponent<UIInfiniteList>();
            list.viewport = viewport;
            list.content = content;
            list.horizontal = false;
            list.vertical = true;
            list.ItemSize = new Vector2(400f, 100f);
            list.movementType = ScrollRect.MovementType.Clamped;
            if (irregular)
            {
                var layout = root.AddComponent<UISequentialListLayout>();
                var serializedList = new SerializedObject(list);
                serializedList.FindProperty("m_layoutBehaviour").objectReferenceValue = layout;
                serializedList.ApplyModifiedPropertiesWithoutUndo();
            }
            Undo.RegisterCreatedObjectUndo(root, "创建无限列表");
            Undo.CollapseUndoOperations(undoGroup);
            Selection.activeGameObject = root;
        }

        private static RectTransform CreateChild(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.layer = parent.gameObject.layer;
            var rectTransform = (RectTransform)child.transform;
            rectTransform.SetParent(parent, false);
            rectTransform.anchoredPosition = Vector2.zero;
            return rectTransform;
        }
    }
}