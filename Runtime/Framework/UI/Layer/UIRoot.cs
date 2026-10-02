using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UIRoot : MonoBehaviour
    {
        private Dictionary<UILayer, UILayerRoot> m_layers;
        private CanvasScaler m_scaler;

        public CanvasScaler Scaler => m_scaler;

        internal void Initialize(GameObject prefabRoot)
        {
            if (prefabRoot == null) throw new ArgumentNullException(nameof(prefabRoot));
            var windowRoot = prefabRoot.transform.Find("WindowRoot");
            if (windowRoot == null)
                throw new InvalidOperationException("UIRoot needs a direct child named WindowRoot.");
            var canvas = windowRoot.GetComponent<Canvas>();
            if (canvas == null)
                throw new InvalidOperationException("WindowRoot needs a Canvas.");
            m_scaler = windowRoot.GetComponent<CanvasScaler>();
            if (m_scaler == null)
                throw new InvalidOperationException("WindowRoot needs a CanvasScaler.");
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                throw new InvalidOperationException("WindowRoot Screen Space - Camera canvas has no Render Camera.");
            var eventSystems = prefabRoot.GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true);
            if (eventSystems.Length != 1 ||
                !eventSystems[0].transform.IsChildOf(windowRoot) ||
                eventSystems[0].GetComponent<BaseInputModule>() == null)
                throw new InvalidOperationException("WindowRoot needs exactly one EventSystem with an input module.");

            var found = prefabRoot.GetComponentsInChildren<UILayerRoot>(true);
            var layers = new Dictionary<UILayer, UILayerRoot>();
            foreach (var layer in found)
            {
                if (layer.transform.parent != windowRoot)
                    throw new InvalidOperationException($"UIRoot layer {layer.name} must be a direct child of WindowRoot.");
                if (!Enum.IsDefined(typeof(UILayer), layer.Layer))
                    throw new InvalidOperationException($"UIRoot has an unknown layer value on {layer.name}.");
                if (layer.GetComponent<GraphicRaycaster>() == null)
                    throw new InvalidOperationException($"UIRoot layer {layer.Layer} needs a GraphicRaycaster.");
                if (layers.ContainsKey(layer.Layer))
                    throw new InvalidOperationException($"UIRoot has duplicate {layer.Layer} layers.");
                layers.Add(layer.Layer, layer);
                layer.Configure();
            }

            foreach (UILayer value in Enum.GetValues(typeof(UILayer)))
                if (!layers.ContainsKey(value))
                    throw new InvalidOperationException($"UIRoot is missing the {value} layer.");
            m_layers = layers;
        }

        internal RectTransform GetLayer(UILayer layer)
        {
            if (m_layers == null)
                throw new InvalidOperationException("UIRoot is not initialized.");
            return m_layers[layer].Content;
        }
    }
}
