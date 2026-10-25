#if UNITY_6000_3_OR_NEWER
using UnityEditor;
using UnityEditor.Toolbars;

namespace GitVersionControl
{
    /// <summary>
    /// Unity 6000.3 이상: 공개 MainToolbar API 로 버튼을 등록한다.
    /// 도구 막대가 요소를 만들 때 <see cref="CreateElement"/> 를 부르고, 상태가 바뀌면 <see cref="MainToolbar.Refresh"/> 로 다시 만들게 한다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static partial class GitMainToolbar
    {
        private const string ElementPath = "Git/Git";

        [InitializeOnLoadMethod]
        private static void Install()
        {
            SubscribeRepaint();
        }

        [MainToolbarElement(ElementPath, defaultDockPosition = MainToolbarDockPosition.Left)]
        private static MainToolbarElement CreateElement()
        {
            MainToolbarContent content = new(CurrentText, Icon, CurrentTooltip);
            return new MainToolbarDropdown(content, ShowMenu)
            {
                displayed = GitVcsSettings.instance.ShowInMainToolbar,
            };
        }

        private static void ApplyToToolbar()
        {
            MainToolbar.Refresh(ElementPath);
        }
    }
}
#endif
