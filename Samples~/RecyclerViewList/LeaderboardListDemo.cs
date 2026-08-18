using UnityEngine;

namespace Sinkii09.UIFramework.Samples
{
    /// <summary>
    /// Minimum viable consumer of <see cref="RecyclerView"/>: 50,000 rows, a handful of cells.
    ///
    /// <para>Attach to any GameObject, point <see cref="_list"/> at a Recycler View created from
    /// <c>GameObject > UI > UIFramework > Recycler View</c>, and assign a cell prefab with a
    /// <see cref="LeaderboardRowCell"/> on it to that view's Cell Prefabs array.</para>
    /// </summary>
    public class LeaderboardListDemo : MonoBehaviour
    {
        [SerializeField] private RecyclerView _list;
        [SerializeField] private int _rowCount = 50000;

        private string[] _names;
        private int[] _scores;

        private void Start()
        {
            GenerateData();

            // Order does not matter — SetCellProvider pumps on its own — but a provider must exist
            // before any index can bind.
            _list.SetCellProvider(BindRow);
            _list.SetItemCount(_rowCount);
        }

        /// <summary>
        /// The provider's whole job: rent a cell of the right prefab, fill it, return it.
        ///
        /// <para>Two rules the view enforces rather than trusts. The cell must come from
        /// <c>RentCell</c> — one obtained any other way is never recycled. And exactly one cell may
        /// be rented per call, because only the returned one is tracked.</para>
        /// </summary>
        private RecyclerCell BindRow(int index)
        {
            LeaderboardRowCell cell = _list.RentCell<LeaderboardRowCell>(0);
            cell.Bind(index + 1, _names[index], _scores[index]);
            return cell;
        }

        private void GenerateData()
        {
            _names = new string[_rowCount];
            _scores = new int[_rowCount];

            var random = new System.Random(1337);
            for (int i = 0; i < _rowCount; i++)
            {
                _names[i] = $"Player {random.Next(1000, 9999)}";
                _scores[i] = (_rowCount - i) * 10 + random.Next(0, 9);
            }
        }

        /// <summary>Jumps the list without walking the window across everything in between.</summary>
        public void JumpToTop() => _list.ScrollToIndex(0);

        /// <summary>Re-asks the provider for every visible row, keeping the scroll position.</summary>
        public void RefreshVisible() => _list.RefreshAll();
    }
}
