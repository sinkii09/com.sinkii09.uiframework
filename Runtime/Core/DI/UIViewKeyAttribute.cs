using System;

namespace Sinkii09.UIFramework
{
    // Optional: override the default addressable key (class name) for a UIView subclass.
    // Usage: [UIViewKey("main-menu")] on a UIView<T> subclass.
    // Without this attribute, UIViewRegistry uses type.Name as the key.
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class UIViewKeyAttribute : Attribute
    {
        public string Key { get; }
        public UIViewKeyAttribute(string key) => Key = key;
    }
}
