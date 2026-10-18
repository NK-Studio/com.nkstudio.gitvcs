using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 모양. 설정 파일에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum GitThemeStyle
    {
        JetBrains,
        Unity,
    }

    /// <summary>
    /// 테마 적용. 스타일(JetBrains·Unity)은 사용자가 고르고, 어둡게·밝게는 Unity 에디터 스킨을 따른다.
    /// 테마마다 색·모서리 토큰만 담은 시트(Themes/{스타일}{Dark|Light}.uss) 하나를 창 루트에 붙이고,
    /// 모양 차이는 루트 클래스(git-theme--jetbrains 등)를 보고 Styles/GitThemes.uss 가 맡는다.
    /// </summary>
    public static class GitTheme
    {
        private const string ThemeFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Themes/";
        private const string ShapeSheetPath = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles/GitThemes.uss";
        private const string ThemedClass = "git-themed";
        private const string ClassPrefix = "git-theme--";

        private static readonly string[] SheetNames = { "JetBrainsDark", "JetBrainsLight", "UnityDark", "UnityLight" };

        /// <summary>
        /// 스타일이 바뀌었을 때. 열린 창이 다시 칠한다.
        /// </summary>
        public static event Action Changed;

        public static GitThemeStyle Style
        {
            get => GitVcsSettings.instance.Theme;
            set
            {
                if (GitVcsSettings.instance.Theme == value)
                {
                    return;
                }
                GitVcsSettings.instance.Theme = value;
                Changed?.Invoke();
            }
        }

        public static bool IsDark => EditorGUIUtility.isProSkin;

        public static string DisplayName(GitThemeStyle style) => style == GitThemeStyle.JetBrains ? "JetBrains" : "Unity";

        /// <summary>
        /// 스타일·밝기에 맞는 토큰 시트 이름. 예: (JetBrains, 어둡게) → "JetBrainsDark".
        /// </summary>
        public static string SheetName(GitThemeStyle style, bool isDark)
        {
            return (style == GitThemeStyle.JetBrains ? "JetBrains" : "Unity") + (isDark ? "Dark" : "Light");
        }

        public static void Apply(VisualElement root)
        {
            Apply(root, Style, IsDark);
        }

        /// <summary>
        /// root 아래에만 테마를 입힌다. 이전 테마 시트는 떼어 내므로 여러 번 불러도 된다.
        /// </summary>
        public static void Apply(VisualElement root, GitThemeStyle style, bool isDark)
        {
            string name = SheetName(style, isDark);
            // 이미 같은 테마면 손대지 않는다. 시트를 떼고 붙이면 창 전체 스타일을 다시 계산해서, 포커스마다 부르면 클릭이 굼떠진다.
            if (HasSheet(root, name) && root.ClassListContains(ClassPrefix + (style == GitThemeStyle.JetBrains ? "jetbrains" : "unity")))
            {
                return;
            }

            for (int i = root.styleSheets.count - 1; i >= 0; i--)
            {
                StyleSheet existing = root.styleSheets[i];
                if (existing != null && Array.IndexOf(SheetNames, existing.name) >= 0)
                {
                    root.styleSheets.Remove(existing);
                }
            }

            root.styleSheets.Add(Load(ThemeFolder + name + ".uss"));
            StyleSheet shapes = Load(ShapeSheetPath);
            if (root.styleSheets.Contains(shapes) == false)
            {
                root.styleSheets.Add(shapes);
            }

            root.AddToClassList(ThemedClass);
            root.EnableInClassList(ClassPrefix + "jetbrains", style == GitThemeStyle.JetBrains);
            root.EnableInClassList(ClassPrefix + "unity", style == GitThemeStyle.Unity);
            root.EnableInClassList(ClassPrefix + "dark", isDark);
            root.EnableInClassList(ClassPrefix + "light", isDark == false);
        }

        private static bool HasSheet(VisualElement root, string name)
        {
            for (int i = 0; i < root.styleSheets.count; i++)
            {
                if (root.styleSheets[i] != null && root.styleSheets[i].name == name)
                {
                    return true;
                }
            }
            return false;
        }

        private static StyleSheet Load(string path)
        {
            StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
            if (sheet == null)
            {
                throw new InvalidOperationException($"USS not found: {path}");
            }
            return sheet;
        }
    }
}
