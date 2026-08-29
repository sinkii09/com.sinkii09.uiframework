using System;

namespace Sinkii09.UIFramework
{
    // Single source of truth for deriving a view type's load key.
    //
    // This rule used to be written out twice — once in UIViewFactory.GetKey and once inline
    // in UIViewRegistry.AutoRegister. They agreed, but nothing enforced that they kept
    // agreeing, and a divergence would surface as a view loading the wrong prefab (or
    // failing to load) only for types carrying a [UIViewKey] attribute.
    //
    // It is also the bridge between the two identities the framework uses for a view:
    // UIViewFactory caches by System.Type, but IUILoader loads/unloads by string key, and
    // UIViewPolicyConfig (a ScriptableObject, which cannot serialize a Type) declares policy
    // by string key. Every Type -> key crossing goes through here.
    public static class UIViewKeys
    {
        public static string For(Type viewType)
        {
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));

            var keyAttr = (UIViewKeyAttribute)Attribute.GetCustomAttribute(viewType, typeof(UIViewKeyAttribute));
            return keyAttr?.Key ?? viewType.Name;
        }
    }
}
