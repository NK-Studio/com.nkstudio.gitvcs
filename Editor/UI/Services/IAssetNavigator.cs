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
    }
}
