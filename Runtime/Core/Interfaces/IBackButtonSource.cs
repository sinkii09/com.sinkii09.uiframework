using R3;

namespace Sinkii09.UIFramework
{
    public interface IBackButtonSource
    {
        Observable<Unit> OnBack { get; }
    }
}
