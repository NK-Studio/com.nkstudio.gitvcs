using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 밝기. 설정 파일에 숫자로 저장되므로 끝에만 추가한다.
    /// </summary>
    public enum GitThemeMode
    {
        /// <summary>
        /// Unity 에디터 스킨(어둡게·밝게)을 따른다.
        /// </summary>
        System,
        Dark,
        Light,
    }

    /// <summary>
    /// 테마 적용. 모양은 JetBrains 하나이고, 어둡게·밝게는 사용자가 고른다(System 이면 Unity 에디터 스킨을 따른다).
    /// 밝기마다 테마 시트(Themes/JetBrains{Dark|Light}.tss) 하나를 창 루트에 붙인다. TSS 는 색·모서리 토큰을 정의하고
    /// Styles/GitThemes.uss(Unity 기본 컨트롤이 토큰을 따르게 하는 규칙)를 @import 로 함께 묶는다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitTheme
    {
        private const string ThemeFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Themes/";
        private const string ThemedClass = "git-themed";
        private const string ClassPrefix = "git-theme--";

        private static readonly string[] SheetNames = { "JetBrainsDark", "JetBrainsLight" };

        /// <summary>
        /// (아이콘 이름, 어둡게) → 창 밝기에 맞는 에디터 아이콘. 없는 변형은 원래 아이콘을 넣어 두어 다시 찾지 않는다.
        /// </summary>
        private static readonly Dictionary<(string, bool), Texture> SkinIcons = new();

        /// <summary>
        /// 메뉴·설정 화면에 보이는 순서.
        /// </summary>
        public static readonly GitThemeMode[] MenuOrder = { GitThemeMode.Dark, GitThemeMode.Light, GitThemeMode.System };

        /// <summary>
        /// 밝기가 바뀌었을 때. 열린 창이 다시 칠한다.
        /// </summary>
        public static event Action Changed;

        public static GitThemeMode Mode
        {
            get => GitVcsSettings.instance.ThemeMode;
            set
            {
                if (GitVcsSettings.instance.ThemeMode == value)
                {
                    return;
                }
                GitVcsSettings.instance.ThemeMode = value;
                Changed?.Invoke();
            }
        }

        public static bool IsDark => ResolveIsDark(Mode, EditorGUIUtility.isProSkin);

        public static bool ResolveIsDark(GitThemeMode mode, bool editorIsDark)
        {
            return mode switch
            {
                GitThemeMode.Dark => true,
                GitThemeMode.Light => false,
                _ => editorIsDark,
            };
        }

        public static string DisplayName(GitThemeMode mode)
        {
            return mode switch
            {
                GitThemeMode.Dark => GitText.Get("theme.dark"),
                GitThemeMode.Light => GitText.Get("theme.light"),
                _ => GitText.Get("theme.system"),
            };
        }

        /// <summary>
        /// 에디터 아이콘(폴더·에셋 종류)을 창 밝기에 맞는 변형으로 바꾼다. Unity 는 에디터 스킨에 맞는 그림만 주므로,
        /// 스킨과 창 밝기가 다르면 어두운 스킨용 "d_" 이름과 밝은 스킨용 이름을 서로 바꿔 찾는다. 변형이 없거나(사용자 아이콘·썸네일) 이미 맞으면 그대로 둔다.
        /// </summary>
        public static Texture SkinIcon(Texture icon)
        {
            return SkinIcon(icon, IsDark);
        }

        public static Texture SkinIcon(Texture icon, bool isDark)
        {
            if (icon == null)
            {
                return null;
            }
            string name = icon.name;
            bool named = name.StartsWith("d_", StringComparison.Ordinal);
            if (named == isDark || string.IsNullOrEmpty(name))
            {
                return icon;
            }
            if (SkinIcons.TryGetValue((name, isDark), out Texture cached) && cached != null)
            {
                return cached;
            }
            string wanted = isDark ? "d_" + name : name.Substring(2);
            Texture found = EditorGUIUtility.FindTexture(wanted);
            Texture result = found != null ? found : icon;
            SkinIcons[(name, isDark)] = result;
            return result;
        }

        /// <summary>
        /// 밝기에 맞는 테마 시트(.tss) 이름. 예: 어둡게 → "JetBrainsDark".
        /// </summary>
        public static string SheetName(bool isDark)
        {
            return isDark ? "JetBrainsDark" : "JetBrainsLight";
        }

        public static void Apply(VisualElement root)
        {
            Apply(root, IsDark);
        }

        /// <summary>
        /// root 아래에만 테마를 입힌다. 이전 테마 시트는 떼어 내므로 여러 번 불러도 된다.
        /// </summary>
        public static void Apply(VisualElement root, bool isDark)
        {
            // 테마를 입히는 창 루트마다 한글 IME 깨우기를 붙인다(코드로 포커스를 준 입력란의 첫 키, GitIme).
            GitIme.Attach(root);
            string name = SheetName(isDark);
            // 이미 같은 테마면 손대지 않는다. 시트를 떼고 붙이면 창 전체 스타일을 다시 계산해서, 포커스마다 부르면 클릭이 굼떠진다.
            if (HasSheet(root, name) && root.ClassListContains(ThemedClass))
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

            bool switching = root.ClassListContains(ThemedClass);
            root.styleSheets.Add(Load(ThemeFolder + name + ".tss"));

            root.AddToClassList(ThemedClass);
            root.AddToClassList(ClassPrefix + "jetbrains");
            root.EnableInClassList(ClassPrefix + "dark", isDark);
            root.EnableInClassList(ClassPrefix + "light", isDark == false);

            // 줄 아이콘(폴더·에셋 종류·상태)은 코드가 밝기에 맞춰 고르므로, 밝기를 바꿨으면 보이는 줄을 다시 묶는다.
            if (switching)
            {
                root.Query<BaseVerticalCollectionView>().ForEach(view => view.RefreshItems());
            }
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

        private static ThemeStyleSheet Load(string path)
        {
            ThemeStyleSheet sheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(path);
            if (sheet == null)
            {
                throw new InvalidOperationException($"TSS not found: {path}");
            }
            return sheet;
        }
    }
}
