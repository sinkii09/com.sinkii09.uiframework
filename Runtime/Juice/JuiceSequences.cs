using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;
using UnityEngine;

namespace Sinkii09.UIFramework
{
    /// <summary>
    /// Staggered entrances: items pop in one after another instead of all at once, which reads as
    /// "built for you" rather than "loaded". Intended for a view's <c>OnShowAsync</c>.
    /// </summary>
    /// <remarks>
    /// Usage: <see cref="PreparePopIn"/> in <c>OnPrepareForShow</c> (before activation, so there is
    /// no one-frame flash at full size), then <c>await PlayStaggerPopInAsync(this, items, ct)</c> in
    /// <c>OnShowAsync</c>. Items rest at <c>Vector3.one</c> — wrap anything authored at another scale
    /// in a container and pass the container. Scale only, so LayoutGroup children are safe.
    /// </remarks>
    public static class JuiceSequences
    {
        public static void PreparePopIn(IReadOnlyList<Transform> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
                if (items[i] != null) items[i].localScale = Vector3.zero;
        }

        public static void RestorePopIn(IReadOnlyList<Transform> items)
        {
            if (items == null) return;
            for (int i = 0; i < items.Count; i++)
            {
                // Unity null check also skips items destroyed with their owner mid-sequence.
                if (items[i] == null) continue;
                items[i].DOKill();
                items[i].localScale = Vector3.one;
            }
        }

        /// <summary>
        /// Pops <paramref name="items"/> in at <paramref name="interval"/> spacing. The sequence is
        /// linked to <paramref name="owner"/> and awaited here, so callers never hold a raw tween.
        /// Cancellation restores every item to its resting scale and rethrows.
        /// </summary>
        public static async UniTask PlayStaggerPopInAsync(
            Component owner,
            IReadOnlyList<Transform> items,
            CancellationToken ct = default,
            float interval = 0.04f,
            float duration = 0.25f)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (items == null || items.Count == 0) return;

            var sequence = DOTween.Sequence().SetLink(owner.gameObject);
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i] == null) continue;
                // From(zero) makes the start deterministic even if the caller skipped PreparePopIn.
                sequence.Insert(i * interval,
                    items[i].DOScale(Vector3.one, duration).From(Vector3.zero).SetEase(Ease.OutBack));
            }

            try
            {
                await sequence.AwaitAsync(ct);
            }
            catch (OperationCanceledException)
            {
                RestorePopIn(items);
                throw;
            }
        }
    }
}
