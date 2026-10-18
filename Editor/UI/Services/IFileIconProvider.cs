using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>파일 목록 줄 앞에 붙일 파일 종류 아이콘(스크립트·프리팹·폴더 등).</summary>
    public interface IFileIconProvider
    {
        Texture GetIcon(string projectPath, string repoPath);
    }

    /// <summary>
    /// Project 창과 같은 아이콘을 쓴다. 프로젝트 안 에셋은 AssetDatabase 의 아이콘, 폴더(.meta 만 바뀐 새 폴더 포함)는 폴더 아이콘,
    /// 삭제됐거나 프로젝트 밖 파일(.gitignore 등)은 확장자로 정한다. 같은 경로는 다시 찾지 않도록 기억한다.
    /// </summary>
    public sealed class EditorFileIconProvider : IFileIconProvider
    {
        private readonly Dictionary<string, Texture> _cache = new();
        private Texture _folderIcon;

        public Texture GetIcon(string projectPath, string repoPath)
        {
            string key = projectPath ?? repoPath ?? string.Empty;
            if (_cache.TryGetValue(key, out Texture cached) && cached != null)
            {
                return cached;
            }

            Texture icon = Find(projectPath, repoPath);
            _cache[key] = icon;
            return icon;
        }

        private Texture Find(string projectPath, string repoPath)
        {
            string assetPath = GitDisplay.AssetPathOf(projectPath);
            if (string.IsNullOrEmpty(assetPath) == false)
            {
                if (AssetDatabase.IsValidFolder(assetPath))
                {
                    _folderIcon ??= EditorGUIUtility.IconContent("Folder Icon").image;
                    return _folderIcon;
                }
                Texture assetIcon = AssetDatabase.GetCachedIcon(assetPath);
                if (assetIcon != null)
                {
                    return assetIcon;
                }
            }
            return InternalEditorUtility.GetIconForFile(repoPath ?? projectPath ?? string.Empty);
        }
    }
}
