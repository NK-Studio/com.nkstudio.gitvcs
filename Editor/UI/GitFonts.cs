using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 창 글꼴(IBM Plex Sans KR)과 코드 글꼴(Jetendard) 파일. 창 글꼴은 보통 USS(GitCommon.uss 의 -unity-font-definition)가 입히고,
    /// USS 가 닿지 않는 곳(창 바깥 호스트에 붙는 배율 표시 등)만 코드에서 이것을 쓴다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitFonts
    {
        public const string FontFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Fonts/";

        private static Font s_bold;
        private static Font s_monospace;

        public static Font Bold => s_bold != null ? s_bold : s_bold = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "IBMPlexSansKR-Bold.ttf");

        /// <summary>
        /// diff·blame 의 코드 줄에 쓰는 고정폭 글꼴(Jetendard = JetBrains Mono + Pretendard 한글, SIL OFL 1.1, Fonts/Jetendard-OFL.txt).
        /// </summary>
        public static Font Monospace => s_monospace != null ? s_monospace : s_monospace = AssetDatabase.LoadAssetAtPath<Font>(FontFolder + "Jetendard-Regular.ttf");
    }
}
