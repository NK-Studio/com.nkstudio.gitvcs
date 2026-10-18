using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 히스토리·blame 같은 보조 창의 공통 바탕: Git 창과 같은 스타일 시트·테마를 붙인 내용 루트(<see cref="Content"/>).
    /// </summary>
    public abstract class GitToolWindowBase : EditorWindow
    {
        private const string StyleFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles/";
        private static readonly string[] Sheets = { "GitCommon", "GitRows", "GitViews", "GitDiff", "GitStaging" };

        protected VisualElement Content { get; private set; }

        protected void CreateContent()
        {
            Content = new VisualElement();
            Content.style.flexGrow = 1;
            Content.AddToClassList("git-window");
            foreach (string sheet in Sheets)
            {
                Content.styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(StyleFolder + sheet + ".uss"));
            }
            rootVisualElement.Add(Content);
            GitTheme.Apply(Content);
            GitTheme.Changed -= ApplyTheme;
            GitTheme.Changed += ApplyTheme;
        }

        protected virtual void OnDisable()
        {
            GitTheme.Changed -= ApplyTheme;
        }

        private void OnFocus()
        {
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            if (Content != null)
            {
                GitTheme.Apply(Content);
            }
        }

        protected static GUIContent TitleWithIcon(string title)
        {
            return new GUIContent(title, EditorGUIUtility.IconContent("d_UnityEditor.VersionControl").image);
        }
    }
}
