using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Canvas))]
    [RequireComponent(typeof(CanvasScaler))]
    public sealed class UIRoot : MonoBehaviour
    {
        private Dictionary<UILayer, UILayerRoot> m_layers;

        public CanvasScaler Scaler => GetComponent<CanvasScaler>();

        internal void Initialize()
        {
            var canvas = GetComponent<Canvas>();
            if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera == null)
                throw new InvalidOperationException("UIRoot Screen Space - Camera canvas has no Render Camera.");
            var eventSystems = GetComponentsInChildren<UnityEngine.EventSystems.EventSystem>(true);
            if (eventSystems.Length != 1 ||
                eventSystems[0].gameObject.GetComponent<BaseInputModule>() == null)
                throw new InvalidOperationException("UIRoot needs exactly one EventSystem with an input module.");

            var found = GetComponentsInChildren<UILayerRoot>(true);
            var layers = new Dictionary<UILayer, UILayerRoot>();
            foreach (var layer in found)
            {
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
