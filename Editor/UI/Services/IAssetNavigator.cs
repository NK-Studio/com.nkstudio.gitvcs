using UnityEditor;

namespace GitVersionControl
{
    /// <summary>목록에서 고른 파일을 Project 창·탐색기에서 보여 주고 경로를 복사한다.</summary>
    public interface IAssetNavigator
    {
        bool CanPing(string projectPath);
        void Ping(string projectPath);
        void Reveal(string projectPath);
        void CopyToClipboard(string text);

        /// <summary>
        /// 실제로 연다. Unity 에셋이면 Project 창에서 더블클릭한 것과 같게(스크립트는 코드 편집기, 프리팹은 프리팹 모드, 씬은 씬 열기),
        /// 에셋이 아니면(.gitignore·프로젝트 밖 파일) OS 기본 프로그램으로 연다.
        /// </summary>
        /// <param name="projectPath">Unity 프로젝트 기준 경로. 프로젝트 밖이면 null.</param>
        /// <param name="fullPath">디스크의 전체 경로(에셋이 아닐 때 연다).</param>
        void Open(string projectPath, string fullPath);
    }

    public sealed class EditorAssetNavigator : IAssetNavigator
    {
        public bool CanPing(string projectPath)
        {
            return string.IsNullOrEmpty(projectPath) == false
                   && AssetDatabase.LoadMainAssetAtPath(GitDisplay.AssetPathOf(projectPath)) != null;
        }

        public void Ping(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath))
            {
                return;
            }
            UnityEngine.Object asset = AssetDatabase.LoadMainAssetAtPath(GitDisplay.AssetPathOf(projectPath));
            if (asset != null)
            {
                Selection.activeObject = asset;
                EditorGUIUtility.PingObject(asset);
            }
        }

        public void Reveal(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath) == false)
            {
                EditorUtility.RevealInFinder(projectPath);
            }
        }

        public void CopyToClipboard(string text)
        {
            EditorGUIUtility.systemCopyBuffer = text;
        }

        public void Open(string projectPath, string fullPath)
        {
            UnityEngine.Object asset = string.IsNullOrEmpty(projectPath) ? null : AssetDatabase.LoadMainAssetAtPath(GitDisplay.AssetPathOf(projectPath));
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
                return;
            }
            if (string.IsNullOrEmpty(fullPath) == false && System.IO.File.Exists(fullPath))
            {
                EditorUtility.OpenWithDefaultApp(fullPath);
            }
        }
    }
}
