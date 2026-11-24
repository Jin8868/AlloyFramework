using System;

namespace AlloyFramework.GM
{
    /// <summary>将方法注册为 GM 命令，方法签名决定控件类型。</summary>
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    public sealed class GMCommandAttribute : Attribute
    {
        /// <summary>创建指定显示名称的命令声明。</summary>
        /// <param name="label">GM 面板上显示的名称。</param>
        public GMCommandAttribute(string label)
        {
            Label = label;
        }

        /// <summary>GM 面板上显示的名称。</summary>
        public string Label { get; }
    }
}
