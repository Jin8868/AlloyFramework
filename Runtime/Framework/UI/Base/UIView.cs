using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public abstract class UIView : MonoBehaviour
    {
        internal void Bind()
        {
            var binding = GetComponent<UIBinding>();
            if (binding == null)
                throw new InvalidOperationException($"UI prefab {name} has no UIBinding component.");
            BindComponents(binding);
        }

        protected abstract void BindComponents(UIBinding binding);
    }
}
