using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 창 글꼴(IBM Plex Sans KR) 파일. 보통은 USS(GitCommon.uss 의 -unity-font-definition)가 입히고,
    /// USS 가 닿지 않는 곳(창 바깥 호스트에 붙는 배율 표시 등)만 코드에서 이것을 쓴다.
    /// </summary>
    public static class GitFonts
    {
        public const string FontFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Fonts/";

        private static Font s_bold;

        public static Font Bold => s_bold != null ? s_bold : s_bold = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "IBMPlexSansKR-Bold.ttf");
    }
}
