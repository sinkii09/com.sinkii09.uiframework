using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using static Sinkii09.UIFramework.Editor.UIControlMenuItemUtility;

namespace Sinkii09.UIFramework.Editor
{
    // GameObject/UI/UIFramework/* quick-create menu items — pre-wired Core control widgets.
    public static class CreateUIControlMenuItems
    {
        [MenuItem("GameObject/UI/UIFramework/Badge", false, 10)]
        public static void CreateBadge(MenuCommand menuCommand)
        {
            var parent = (menuCommand.context as GameObject) ?? GetOrCreateCanvas();
            var root = CreateRoot("Badge", parent, typeof(Badge));
            SetRootSize(root, 40f, 24f);

            var label = CreateChild("Label", root, true, typeof(TextMeshProUGUI));
            SetPrivateField(root.GetComponent<Badge>(), "_label", label.GetComponent<TextMeshProUGUI>());

            Finish(root);
        }

        [MenuItem("GameObject/UI/UIFramework/Icon Label", false, 11)]
        public static void CreateIconLabel(MenuCommand menuCommand)
        {
            var parent = (menuCommand.context as GameObject) ?? GetOrCreateCanvas();
            var root = CreateRoot("Icon Label", parent, typeof(IconLabel));
            SetRootSize(root, 200f, 32f);
            root.AddComponent<HorizontalLayoutGroup>();

            var icon = CreateChild("Icon", root, false, typeof(Image));
            var label = CreateChild("Label", root, false, typeof(TextMeshProUGUI));
            ConfigureRow(root, icon, label, iconSize: 24f);

            var control = root.GetComponent<IconLabel>();
            SetPrivateField(control, "_icon", icon.GetComponent<Image>());
            SetPrivateField(control, "_label", label.GetComponent<TextMeshProUGUI>());

            Finish(root);
        }

        [MenuItem("GameObject/UI/UIFramework/Progress Bar", false, 12)]
        public static void CreateProgressBar(MenuCommand menuCommand)
        {
            var parent = (menuCommand.context as GameObject) ?? GetOrCreateCanvas();
            var root = CreateRoot("Progress Bar", parent, typeof(ProgressBar));
            SetRootSize(root, 200f, 20f);

            var fill = CreateChild("Fill", root, true, typeof(Image));
            var fillImage = fill.GetComponent<Image>();
            fillImage.type = Image.Type.Filled;
            fillImage.fillMethod = Image.FillMethod.Horizontal;
            fillImage.fillAmount = 0f;

            var label = CreateChild("Label", root, true, typeof(TextMeshProUGUI));

            var control = root.GetComponent<ProgressBar>();
            SetPrivateField(control, "_fill", fillImage);
            SetPrivateField(control, "_label", label.GetComponent<TextMeshProUGUI>());

            Finish(root);
        }

        [MenuItem("GameObject/UI/UIFramework/Resource Counter", false, 13)]
        public static void CreateResourceCounter(MenuCommand menuCommand)
        {
            var parent = (menuCommand.context as GameObject) ?? GetOrCreateCanvas();
            var root = CreateRoot("Resource Counter", parent, typeof(ResourceCounter));
            SetRootSize(root, 160f, 32f);
            root.AddComponent<HorizontalLayoutGroup>();

            var icon = CreateChild("Icon", root, false, typeof(Image));
            var label = CreateChild("Label", root, false, typeof(TextMeshProUGUI));
            ConfigureRow(root, icon, label, iconSize: 24f);

            var control = root.GetComponent<ResourceCounter>();
            SetPrivateField(control, "_icon", icon.GetComponent<Image>());
            SetPrivateField(control, "_label", label.GetComponent<TextMeshProUGUI>());
            SetPrivateField(control, "_button", root.GetComponent<Button>());

            Finish(root);
        }

        private static void SetRootSize(GameObject root, float width, float height) =>
            ((RectTransform)root.transform).sizeDelta = new Vector2(width, height);

        private static void ConfigureRow(GameObject root, GameObject icon, GameObject label, float iconSize)
        {
            var layout = root.GetComponent<HorizontalLayoutGroup>();
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.spacing = 4f;

            var iconLayout = icon.AddComponent<LayoutElement>();
            iconLayout.preferredWidth = iconSize;
            iconLayout.preferredHeight = iconSize;

            label.AddComponent<LayoutElement>().flexibleWidth = 1f;
        }
    }
}
