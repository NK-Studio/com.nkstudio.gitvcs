using UnityEditor;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>에디터 내장 아이콘을 도구 막대 버튼에 단다(UXML 에서는 내장 아이콘 이름을 쓸 수 없어서).</summary>
    public static class ToolbarIcons
    {
        public static void SetRefreshIcon(Button button)
        {
            Image icon = new() { image = EditorGUIUtility.IconContent("Refresh").image };
            icon.AddToClassList("git-toolbar__icon");
            button.text = string.Empty;
            button.Add(icon);
        }
    }
}
