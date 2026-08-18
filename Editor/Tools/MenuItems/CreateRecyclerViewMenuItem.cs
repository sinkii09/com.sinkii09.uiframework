using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Sinkii09.UIFramework.Editor.UIControlMenuItemUtility;

namespace Sinkii09.UIFramework.Editor
{
    // GameObject/UI/UIFramework/Recycler View — kept out of CreateUIControlMenuItems because this
    // one builds a whole ScrollRect scaffold rather than a single widget, and because it has to
    // defer Awake (see below) where every other control can be created active.
    public static class CreateRecyclerViewMenuItem
    {
        private const float DefaultWidth = 400f;
        private const float DefaultHeight = 600f;

        [MenuItem("GameObject/UI/UIFramework/Recycler View", false, 14)]
        public static void CreateRecyclerView(MenuCommand menuCommand)
        {
            var parent = (menuCommand.context as GameObject) ?? GetOrCreateCanvas();

            // Built inactive on purpose. UIControlBase.Awake runs OnInitialize immediately in the
            // Editor, and RecyclerView.OnInitialize throws if the ScrollRect has no content yet — so
            // the object cannot be live until the viewport and content below are wired up. Finish()
            // reactivates it, which is what actually runs OnInitialize.
            var root = new GameObject("Recycler View", typeof(RectTransform));
            root.SetActive(false);
            GameObjectUtility.SetParentAndAlign(root, parent);

            var rootRect = (RectTransform)root.transform;
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.anchoredPosition = Vector2.zero;
            rootRect.sizeDelta = new Vector2(DefaultWidth, DefaultHeight);

            var scrollRect = root.AddComponent<ScrollRect>();

            // RectMask2D rather than Mask: no Image and no extra draw call, and it clips rects,
            // which is all a uniform list needs.
            var viewport = CreateChild("Viewport", root, true, typeof(RectMask2D));
            var content = CreateChild("Content", viewport, false);

            scrollRect.viewport = (RectTransform)viewport.transform;
            scrollRect.content = (RectTransform)content.transform;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;

            // Added last so its RequireComponent finds the ScrollRect already configured.
            root.AddComponent<RecyclerView>();

            Finish(root);

            Debug.Log("[UIFramework] Recycler View created. Assign at least one cell prefab, then " +
                      "call SetCellProvider and SetItemCount from your ViewModel.", root);
        }
    }
}
