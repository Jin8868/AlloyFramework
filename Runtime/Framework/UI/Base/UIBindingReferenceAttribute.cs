using System;
using UnityEngine;

namespace AlloyFramework.UI
{
    /// <summary>
    /// 标记由 UI 生成器维护的序列化引用。该引用会显示在 Inspector 中，但不允许手动修改。
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class UIBindingReferenceAttribute : PropertyAttribute
    {
    }
}
