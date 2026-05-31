using System;

namespace Sinkii09.UIFramework
{
    // Immutable snapshot of navigation state for debugging and history inspection.
    public readonly struct NavigationContext
    {
        public IUIView[] Stack { get; }
        public Type CurrentStateType { get; }
        public int Depth => Stack?.Length ?? 0;

        public NavigationContext(IUIView[] stack, Type currentStateType)
        {
            Stack = stack;
            CurrentStateType = currentStateType;
        }

        public override string ToString() =>
            $"NavigationContext[Depth={Depth}, State={CurrentStateType?.Name ?? "none"}]";
    }
}
