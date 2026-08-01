using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;
using static Sinkii09.UIFramework.Tests.SaveTestHelpers;

namespace Sinkii09.UIFramework.Tests
{
    // Regression tests for the P2 finding: UIStateMachine.ChangeStateAsync's cancellation catch
    // used to unconditionally restore `previous` as CurrentState, even when previous.OnExitAsync
    // had already completed — so a follow-up transition would call OnExitAsync on it a second time.
    // See plans/260801-2148-correctness-cluster/phase-04-cancellation-semantics.md.
    public class UIStateMachineTests
    {
        // Distinct C# types per participant — RegisterState<T> keys off typeof(T), inferred from
        // the STATIC type of the argument.
        private sealed class StateA : FakeViewState { }
        private sealed class StateB : FakeViewState { }
        private sealed class StateC : FakeViewState { }

        [UnityTest]
        public IEnumerator ChangeStateAsync_CancelledAfterExit_DoesNotDoubleExitPrevious() => UniTask.ToCoroutine(async () =>
        {
            var sm = new UIStateMachine();
            var stateA = new StateA();
            var stateB = new StateB { CancelDuringEnter = true };
            var stateC = new StateC();
            sm.RegisterState(stateA);
            sm.RegisterState(stateB);
            sm.RegisterState(stateC);

            await sm.ChangeStateAsync<StateA>();

            // P2-1: the P2 regression. A cancellation AFTER previous.OnExitAsync completed must
            // leave CurrentState null, not restore `previous` — otherwise the follow-up transition
            // below would call OnExitAsync on stateA a second time.
            var error = await CaptureAsync(async () => await sm.ChangeStateAsync<StateB>());
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.IsNull(sm.CurrentState);
            Assert.AreEqual(1, stateA.OnExitCount);

            await sm.ChangeStateAsync<StateC>();
            Assert.AreEqual(1, stateA.OnExitCount, "Previous state must not be exited twice after a null-recovery.");
        });

        [UnityTest]
        public IEnumerator ChangeStateAsync_CancelledBeforeExitCompletes_KeepsPreviousCurrent() => UniTask.ToCoroutine(async () =>
        {
            var sm = new UIStateMachine();
            var stateA = new StateA { CancelDuringExit = true };
            var stateB = new StateB();
            sm.RegisterState(stateA);
            sm.RegisterState(stateB);

            await sm.ChangeStateAsync<StateA>();

            // P2-2: the exitCompleted == false half is not regressed — a cancellation DURING
            // previous.OnExitAsync (before it completes) must restore `previous` as CurrentState,
            // since it genuinely never finished exiting.
            var error = await CaptureAsync(async () => await sm.ChangeStateAsync<StateB>());
            Assert.IsInstanceOf<OperationCanceledException>(error);
            Assert.AreSame(stateA, sm.CurrentState);
            Assert.AreEqual(0, stateB.OnEnterCount, "OnEnterAsync must not run if OnExitAsync did not complete.");
        });

        [UnityTest]
        public IEnumerator ChangeStateAsync_ThrowsAfterExit_SetsCurrentNull() => UniTask.ToCoroutine(async () =>
        {
            var sm = new UIStateMachine();
            var stateA = new StateA();
            var boom = new InvalidOperationException("boom");
            var stateB = new StateB { ThrowDuringEnter = boom };
            sm.RegisterState(stateA);
            sm.RegisterState(stateB);

            await sm.ChangeStateAsync<StateA>();

            // P2-3: June's general-exception fix still holds — a non-cancellation throw from
            // OnEnterAsync after OnExitAsync succeeded must also null CurrentState, not restore
            // `previous` (mirrors P2-1 but for the general `catch` branch, not the OCE branch).
            var error = await CaptureAsync(async () => await sm.ChangeStateAsync<StateB>());
            Assert.AreSame(boom, error);
            Assert.IsNull(sm.CurrentState);
            Assert.AreEqual(1, stateA.OnExitCount);
        });
    }
}
