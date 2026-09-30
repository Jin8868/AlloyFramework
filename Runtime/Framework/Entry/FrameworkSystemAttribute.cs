using System;

namespace AlloyFramework
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class FrameworkSystemAttribute : Attribute
    {
        public FrameworkSystemAttribute(FrameworkSystemPriority priority)
        {
            Priority = priority;
        }

        public FrameworkSystemPriority Priority { get; }
    }
}
