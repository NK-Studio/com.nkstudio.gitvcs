using System;
using System.Collections.Generic;

namespace GitVersionControl
{
    public sealed class GitPathNode
    {
        public string Name;

        /// <summary>
        /// 저장소 기준 경로(폴더는 끝에 '/' 없이).
        /// </summary>
        public string Path;

        public bool IsFolder;
        public readonly List<GitPathNode> Children = new();
    }

    /// <summary>
    /// 경로 목록 → 폴더 트리(Fork 의 File Tree 처럼 폴더 먼저, 그다음 파일, 각각 이름 순). 상태가 없는 순수 경로라
    /// 변경 목록용 <see cref="FileTreeBuilder"/> 대신 쓴다.
    /// </summary>
    public static class GitPathTree
    {
        public static List<GitPathNode> Build(IEnumerable<string> paths)
        {
            GitPathNode root = new() { IsFolder = true, Path = string.Empty };
            Dictionary<string, GitPathNode> folders = new(StringComparer.Ordinal) { [string.Empty] = root };
            foreach (string path in paths)
            {
                if (string.IsNullOrEmpty(path))
                {
                    continue;
                }
                string[] parts = path.Split('/');
                GitPathNode parent = root;
                string folderPath = string.Empty;
                for (int i = 0; i < parts.Length - 1; i++)
                {
                    folderPath = folderPath.Length == 0 ? parts[i] : folderPath + "/" + parts[i];
                    if (folders.TryGetValue(folderPath, out GitPathNode folder) == false)
                    {
                        folder = new GitPathNode { Name = parts[i], Path = folderPath, IsFolder = true };
                        folders[folderPath] = folder;
                        parent.Children.Add(folder);
                    }
                    parent = folder;
                }
                parent.Children.Add(new GitPathNode { Name = parts[^1], Path = path });
            }
            Sort(root);
            return root.Children;
        }

        private static void Sort(GitPathNode node)
        {
            node.Children.Sort((a, b) => a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            foreach (GitPathNode child in node.Children)
            {
                if (child.IsFolder)
                {
                    Sort(child);
                }
            }
        }
    }
}
