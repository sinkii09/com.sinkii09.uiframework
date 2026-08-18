using TMPro;
using UnityEngine;

namespace Sinkii09.UIFramework.Samples
{
    /// <summary>
    /// A cell for <see cref="LeaderboardListDemo"/>.
    ///
    /// <para>Cells own their own <c>Bind</c> signature — the <see cref="RecyclerView"/> never calls
    /// it, the cell provider does. That is deliberate: the view stays free of your data types, and
    /// you stay free to pass whatever you like.</para>
    /// </summary>
    public class LeaderboardRowCell : RecyclerCell
    {
        [SerializeField] private TextMeshProUGUI _rank;
        [SerializeField] private TextMeshProUGUI _playerName;
        [SerializeField] private TextMeshProUGUI _score;

        public void Bind(int rank, string playerName, int score)
        {
            _rank.text = $"#{rank}";
            _playerName.text = playerName;
            _score.text = score.ToString("N0");
        }

        /// <summary>
        /// Called just before the cell returns to the pool. Anything that would otherwise survive
        /// into the next binding — a subscription, a tween, a loaded sprite — is released here.
        /// A cell is reused thousands of times in a long list, so a leak here is a leak per scroll.
        /// </summary>
        public override void OnRecycled()
        {
            // Nothing to release in this sample; override exists to show where it goes.
        }
    }
}
