using AlloyFramework.UI;
using UnityEditor;
using UnityEngine;

namespace AlloyFramework.Editor
{
    internal static class UICreationMenu
    {
        private const string MenuPath = "GameObject/★AlloyFramework★/UI/创建界面";

        [MenuItem(MenuPath, false, 10)]
        private static void CreateUI(MenuCommand menuCommand)
        {
            var gameObject = new GameObject(
                "NewUI",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UIAnimationPlayer));

            GameObjectUtility.SetParentAndAlign(gameObject, menuCommand.context as GameObject);
            GameObjectUtility.EnsureUniqueNameForSibling(gameObject);

            var rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;

            // 全屏背景保持在安全区外，交互内容由安全区容器统一收缩。
            CreateFullScreenChild("Background", gameObject.transform);
            var safeAreaContent = CreateFullScreenChild("SafeAreaContent", gameObject.transform);
            safeAreaContent.AddComponent<UISafeAreaFitter>();

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                gameObject.layer = uiLayer;

            Undo.RegisterCreatedObjectUndo(gameObject, "创建 UI 界面");
            Selection.activeGameObject = gameObject;
        }

        private static GameObject CreateFullScreenChild(string name, Transform parent)
        {
            var child = new GameObject(name, typeof(RectTransform));
            var rectTransform = child.GetComponent<RectTransform>();
            rectTransform.SetParent(parent, false);
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;
            return child;
        }
    }
}
