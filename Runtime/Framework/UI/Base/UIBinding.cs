using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    [DisallowMultipleComponent]
    public sealed class UIBinding : MonoBehaviour
    {
        [SerializeField] private UnityEngine.Object[] m_references = Array.Empty<UnityEngine.Object>();
        [SerializeField] private string[] m_paths = Array.Empty<string>();

        public int Count => m_references?.Length ?? 0;

        public T Get<T>(int index, string fieldName) where T : UnityEngine.Object
        {
            if (m_references == null || index < 0 || index >= m_references.Length)
                throw Error(index, fieldName, $"index is outside binding array ({Count}).");

            var reference = m_references[index];
            if (reference == null)
                throw Error(index, fieldName, "reference is missing.");

            if (!(reference is T typed))
                throw Error(index, fieldName,
                    $"expected {typeof(T).FullName}, got {reference.GetType().FullName}.");

            return typed;
        }

        private InvalidOperationException Error(int index, string fieldName, string detail)
        {
            var path = m_paths != null && index >= 0 && index < m_paths.Length
                ? m_paths[index] : "<unknown>";
            return new InvalidOperationException(
                $"UI binding error in {gameObject.name}, field {fieldName}, node {path}: {detail}");
        }
    }
}
