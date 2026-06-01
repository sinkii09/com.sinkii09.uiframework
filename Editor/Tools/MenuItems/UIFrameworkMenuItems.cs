using UnityEditor;

namespace Sinkii09.UIFramework.Editor
{
    public static class UIFrameworkMenuItems
    {
        [MenuItem("Tools/UIFramework/Create View + ViewModel", priority = 10)]
        public static void OpenCreator() =>
            EditorWindow.GetWindow<ViewViewModelCreatorWizard>("Create View + ViewModel").Show();

        [MenuItem("Tools/UIFramework/Run Setup Wizard", priority = 11)]
        public static void OpenSetup() =>
            EditorWindow.GetWindow<UIFrameworkSetupWizard>("UIFramework Setup").Show();
    }
}
