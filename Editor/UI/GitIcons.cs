using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 패키지를 나타내는 브랜치 모양 아이콘. 어두운 스킨은 밝은 회색(d_), 밝은 스킨은 어두운 회색.
    /// Unity 내장 VersionControl 아이콘은 밝은 스킨에도 밝은 회색만 있어 밝은 도구 막대·탭에서 잘 보이지 않았다. 파일을 못 찾으면 내장 아이콘으로 돌아간다.
    /// 메인 도구 막대와 창 탭은 같은 16pt 칸에도 그려지는 크기가 달라(탭은 글자 옆에 작게) 그림 여백이 다른 두 벌을 쓴다.
    /// </summary>
    public static class GitIcons
    {
        private const string Folder = "Packages/com.nkstudio.gitvcs/Editor/UI/Icons/";

        /// <summary>메인 도구 막대 버튼(옆 AI·Unity VCS 아이콘과 같은 크기).</summary>
        public static Texture2D Toolbar => Load("GitToolbar.png");

        /// <summary>Git 창·보조 창 탭(탭 글자 높이에 맞춘 작은 그림).</summary>
        public static Texture2D Window => Load("GitWindow.png");

        /// <summary>
        /// 창 탭 아이콘을 지금 스킨에 맞춘다. 탭 제목은 레이아웃과 함께 저장되므로, 창을 다시 열 때(CreateGUI) 불러 예전 그림·다른 스킨 그림을 바꾼다.
        /// </summary>
        public static void ApplyTo(EditorWindow window)
        {
            window.titleContent = new GUIContent(window.titleContent.text, Window, window.titleContent.tooltip);
        }

        private static Texture2D Load(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + (EditorGUIUtility.isProSkin ? "d_" + name : name))
                ?? EditorGUIUtility.IconContent("d_UnityEditor.VersionControl").image as Texture2D;
        }
    }
}
