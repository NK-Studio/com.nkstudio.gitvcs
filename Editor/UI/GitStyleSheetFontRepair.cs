using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 패키지를 새로 받으면 USS 가 글꼴 파일보다 먼저 가져와져, <c>-unity-font-definition: url("../Fonts/…")</c> 참조가 빈 채로 남을 수 있다
    /// (2026-10-19 실측: ArrowsAndCats 에서 Git 창 글자가 하나도 안 보였고, 그 USS 만 다시 가져오자 고쳐졌다).
    /// 에디터 세션마다 한 번, 원문의 글꼴 url 수보다 실제로 잡힌 글꼴(Font)이 적은 USS 만 골라 다시 가져온다. 정상이면 아무것도 하지 않는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class GitStyleSheetFontRepair
    {
        private const string StyleFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Styles";
        private const string SessionKey = "GitVersionControl.StyleSheetFontRepair";
        private static readonly Regex FontUrl = new(@"url\(""\.\./Fonts/", RegexOptions.Compiled);

        static GitStyleSheetFontRepair()
        {
            EditorApplication.delayCall += Repair;
        }

        private static void Repair()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }
            SessionState.SetBool(SessionKey, true);

            List<string> broken = FindBrokenSheets();
            if (broken.Count == 0)
            {
                return;
            }
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (string path in broken)
                {
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
        }

        private static List<string> FindBrokenSheets()
        {
            // StyleSheet 가 참조하는 에셋 목록은 공개 API 가 없어 내부 필드로 읽는다. 못 찾으면(Unity 가 바꾸면) 글꼴을 쓰는 USS 를 모두 다시 가져온다.
            FieldInfo assetsField = typeof(StyleSheet).GetField("assets", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            List<string> broken = new();
            foreach (string guid in AssetDatabase.FindAssets("t:StyleSheet", new[] { StyleFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                int expected = FontUrl.Matches(File.ReadAllText(Path.GetFullPath(path))).Count;
                if (expected == 0)
                {
                    continue;
                }
                StyleSheet sheet = AssetDatabase.LoadAssetAtPath<StyleSheet>(path);
                int resolved = assetsField?.GetValue(sheet) is UnityEngine.Object[] assets ? assets.Count(asset => asset != null && asset is UnityEngine.Font) : -1;
                if (resolved < expected)
                {
                    broken.Add(path);
                }
            }
            return broken;
        }
    }
}
