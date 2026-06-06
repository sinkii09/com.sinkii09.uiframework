using System;
using System.Collections.Generic;
using UnityEngine;
using VContainer;

namespace Sinkii09.UIFramework
{
    public readonly struct UIViewRegistration
    {
        public readonly Type ViewType;
        public readonly Type VmType;
        public readonly string Key;

        public UIViewRegistration(Type viewType, Type vmType, string key)
        {
            ViewType = viewType;
            VmType = vmType;
            Key = key;
        }
    }

    // Scans all loaded assemblies for concrete UIView<TViewModel> subclasses and populates
    // UIViewRegistration list consumed by UINavigator. ViewModels are registered Scoped inside
    // UIViewFactory child scopes — NOT at the root container level.
    //
    // IL2CPP NOTE: Reflection-based type discovery is silently stripped on iOS/Android release builds.
    // Add every concrete UIView<T> subclass to a link.xml or annotate with [UnityEngine.Scripting.Preserve]
    // to ensure they survive the IL2CPP managed code stripping pass.
    public static class UIViewRegistry
    {
        private static readonly List<UIViewRegistration> _registrations = new();

        public static IReadOnlyList<UIViewRegistration> Registrations => _registrations;

        // Unity Editor domain reloads (script recompile, Enter Play Mode) reinitialize static
        // fields inconsistently across Unity versions. Clearing here guarantees a fresh scan
        // on every Play session so newly added view types are never silently missed.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnDomainReload() => _registrations.Clear();

        public static void AutoRegister(IContainerBuilder builder)
        {
            // Guard: already scanned (e.g. called twice from hot-reload path).
            if (_registrations.Count > 0) return;

#if ENABLE_IL2CPP
            UnityEngine.Debug.LogWarning(
                "[UIViewRegistry] IL2CPP detected. Reflection-based view discovery may silently miss types " +
                "stripped by the managed linker. Add every concrete UIView<T> and ViewModel class to a " +
                "link.xml, or annotate them with [UnityEngine.Scripting.Preserve].");
#endif

            var uiViewGenericBase = typeof(UIView<>);

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

                    _registrations.Add(new UIViewRegistration(type, vmType, key));
                    // ViewModels are NOT registered here — they are registered Scoped inside
                    // UIViewFactory.CreateAsync child scopes to prevent root-container leaks.
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
