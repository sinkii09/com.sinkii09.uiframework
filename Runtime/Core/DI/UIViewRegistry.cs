using System;
using System.Collections.Generic;
using VContainer;

namespace Sinkii09.UIFramework
{
    // Scans all loaded assemblies for concrete UIView<TViewModel> subclasses and registers
    // each discovered ViewModel as Transient in VContainer so UIViewFactory can resolve them.
    //
    // IL2CPP NOTE: Reflection-based type discovery is silently stripped on iOS/Android release builds.
    // Add every concrete UIView<T> subclass to a link.xml or annotate with [UnityEngine.Scripting.Preserve]
    // to ensure they survive the IL2CPP managed code stripping pass.
    //
    // UINavigator wiring: UIViewRegistry does NOT call navigator.Register<TView, TViewModel>().
    // Game code must explicitly call navigator.Register<TView, TViewModel>() in its own LifetimeScope.
    public static class UIViewRegistry
    {
        private static readonly List<(Type ViewType, Type ViewModelType, string Key)> _registrations = new();

        public static IReadOnlyList<(Type ViewType, Type ViewModelType, string Key)> Registrations => _registrations;

        public static void AutoRegister(IContainerBuilder builder)
        {
            _registrations.Clear();
            var uiViewGenericBase = typeof(UIView<>);
            var seen = new HashSet<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch { continue; } // skip assemblies that fail reflection (obfuscated, native-only, etc.)

                foreach (var type in types)
                {
                    if (type.IsAbstract || type.IsInterface || !type.IsClass) continue;

                    var vmType = GetViewModelType(type, uiViewGenericBase);
                    if (vmType == null) continue;

                    var keyAttr = (UIViewKeyAttribute)Attribute.GetCustomAttribute(type, typeof(UIViewKeyAttribute));
                    var key = keyAttr?.Key ?? type.Name;

                    _registrations.Add((type, vmType, key));

                    // Deduplicate: multiple views can share a ViewModel base; register once per type.
                    if (seen.Add(vmType))
                        builder.Register(vmType, Lifetime.Transient);
                }
            }
        }

        private static Type GetViewModelType(Type type, Type genericBase)
        {
            var current = type.BaseType;
            while (current != null && current != typeof(object))
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == genericBase)
                    return current.GetGenericArguments()[0];
                current = current.BaseType;
            }
            return null;
        }
    }
}
