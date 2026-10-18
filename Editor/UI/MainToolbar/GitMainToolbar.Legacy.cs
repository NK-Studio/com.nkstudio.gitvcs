#if UNITY_6000_3_OR_NEWER == false
using System.Reflection;
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Unity 6000.3 미만: 공개 도구 막대 API 가 없어, 내부 <c>UnityEditor.Toolbar.get.m_Root</c> 의 왼쪽 칸(<c>ToolbarZoneLeftAlign</c>)에
    /// <see cref="EditorToolbarDropdown"/> 을 끼운다(Unity VCS 버튼이 있는 칸).
    /// 도구 막대는 레이아웃을 바꾸거나 창을 다시 만들 때 통째로 새로 지어지므로, 1초마다 버튼이 아직 붙어 있는지 보고 떨어졌으면 다시 붙인다.
    /// </summary>
    public static partial class GitMainToolbar
    {
        private const string ElementName = "git-main-toolbar-button";
        private const string ToolbarTypeName = "UnityEditor.Toolbar";
        private const string LeftZoneName = "ToolbarZoneLeftAlign";

        /// <summary>
        /// 붙어 있는지 확인하는 간격(초). 레이아웃을 바꾼 뒤 버튼이 다시 나타나기까지의 최대 지연이다.
        /// </summary>
        private const double AttachCheckIntervalSecs = 1.0;

        private static EditorToolbarDropdown s_button;
        private static double s_nextCheckTime;

        [InitializeOnLoadMethod]
        private static void Install()
        {
            SubscribeRepaint();
            EditorApplication.update -= KeepAttached;
            EditorApplication.update += KeepAttached;
        }

        private static void KeepAttached()
        {
            if (EditorApplication.timeSinceStartup < s_nextCheckTime)
            {
                return;
            }
            s_nextCheckTime = EditorApplication.timeSinceStartup + AttachCheckIntervalSecs;
            if (s_button != null && s_button.panel != null)
            {
                return;
            }
            if (TryFindLeftZone(out VisualElement zone, out string failure) == false)
            {
                if (failure != null)
                {
                    // 내부 구조가 바뀐 버전이다 — 다시 시도해도 소용없으니 경고를 한 번만 남기고 그만둔다.
                    EditorApplication.update -= KeepAttached;
                    Debug.LogWarning(GitText.Format("mainToolbar.attachFailed", failure));
                }
                return;
            }
            zone.Q(ElementName)?.RemoveFromHierarchy();
            s_button = new EditorToolbarDropdown(CurrentText, Icon, () => ShowMenu(s_button.worldBound)) { name = ElementName };
            zone.Add(s_button);
            ApplyToToolbar();
        }

        /// <summary>
        /// 도구 막대의 왼쪽 칸을 찾는다. 에디터가 막 켜져 도구 막대가 아직 없으면 false 와 null 실패(다음에 다시)를,
        /// 타입·필드·칸 이름이 이 버전과 맞지 않으면 false 와 실패 이유를 돌려준다.
        /// </summary>
        private static bool TryFindLeftZone(out VisualElement zone, out string failure)
        {
            zone = null;
            failure = null;
            System.Type toolbarType = typeof(Editor).Assembly.GetType(ToolbarTypeName);
            if (toolbarType == null)
            {
                failure = $"type {ToolbarTypeName}";
                return false;
            }
            FieldInfo getField = toolbarType.GetField("get", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            FieldInfo rootField = toolbarType.GetField("m_Root", BindingFlags.NonPublic | BindingFlags.Instance);
            if (getField == null || rootField == null)
            {
                failure = $"{ToolbarTypeName}.get / m_Root";
                return false;
            }
            Object toolbar = getField.GetValue(null) as Object;
            if (toolbar == null)
            {
                return false;
            }
            if (rootField.GetValue(toolbar) is not VisualElement root)
            {
                return false;
            }
            zone = root.Q(LeftZoneName);
            if (zone == null)
            {
                failure = $"#{LeftZoneName}";
                return false;
            }
            return true;
        }

        private static void ApplyToToolbar()
        {
            if (s_button == null)
            {
                return;
            }
            s_button.text = CurrentText;
            s_button.icon = Icon;
            s_button.tooltip = CurrentTooltip;
            s_button.style.display = GitVcsSettings.instance.ShowInMainToolbar ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
#endif
