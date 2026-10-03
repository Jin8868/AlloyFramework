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
                typeof(CanvasGroup));

            GameObjectUtility.SetParentAndAlign(gameObject, menuCommand.context as GameObject);
            GameObjectUtility.EnsureUniqueNameForSibling(gameObject);

            var rectTransform = gameObject.GetComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = Vector2.zero;
            rectTransform.localRotation = Quaternion.identity;
            rectTransform.localScale = Vector3.one;

            var uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
                gameObject.layer = uiLayer;

            Undo.RegisterCreatedObjectUndo(gameObject, "创建 UI 界面");
            Selection.activeGameObject = gameObject;
        }
    }
}