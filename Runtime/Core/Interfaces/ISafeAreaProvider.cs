using R3;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    public interface ISafeAreaProvider
    {
        Rect SafeArea { get; }
        Observable<Rect> OnChanged { get; }
    }
}
