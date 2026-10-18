using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>파일 트리의 한 마디. <see cref="Item"/> 이 null 이면 폴더.</summary>
    public sealed class FileTreeNode
    {
        /// <summary>트리에 보이는 이름. 합쳐진 폴더는 "Assets/Plugins/Android" 처럼 여러 단계가 한 이름이 된다.</summary>
        public string Name;

        /// <summary>폴더는 그 폴더의 경로, 파일은 표시 경로. 펼침 상태를 기억하는 열쇠로 쓴다.</summary>
        public string Path;

        public FileListItem Item;
        public readonly List<FileTreeNode> Children = new();

        /// <summary>아래에 있는 파일 수(폴더 이름 옆 숫자).</summary>
        public int FileCount;

        public bool IsFolder => Item == null;

        /// <summary>아래의 모든 파일(폴더 체크·되돌리기에 쓴다).</summary>
        public List<FileListItem> Files()
        {
            List<FileListItem> files = new();
            Collect(this, files);
            return files;
        }

        private static void Collect(FileTreeNode node, List<FileListItem> files)
        {
            if (node.Item != null)
            {
                files.Add(node.Item);
                return;
            }
            foreach (FileTreeNode child in node.Children)
            {
                Collect(child, files);
            }
        }
    }

    /// <summary>
    /// 파일 목록을 폴더 트리로 바꾼다(UI 와 무관한 순수 로직).
    /// - 폴더가 먼저, 그다음 파일. 각각 이름순(대소문자 무시).
    /// - 파일 없이 하위 폴더 하나만 가진 폴더는 그 하위 폴더와 한 줄로 합친다("Assets" › "Plugins" › "Android" → "Assets/Plugins/Android").
    ///   변경이 깊은 곳 하나에만 있을 때 펼치기를 여러 번 하지 않아도 되게.
    /// </summary>
    public static class FileTreeBuilder
    {
        public static List<FileTreeNode> Build(IEnumerable<FileListItem> items)
        {
            FileTreeNode root = new() { Name = string.Empty, Path = string.Empty };
            foreach (FileListItem item in items)
            {
                Insert(root, item);
            }
            foreach (FileTreeNode child in root.Children)
            {
                Finish(child);
            }
            Sort(root);
            return root.Children.Select(Compact).ToList();
        }

        private static void Insert(FileTreeNode root, FileListItem item)
        {
            string[] parts = item.DisplayPath.Split('/');
            FileTreeNode current = root;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                string folderPath = string.Join("/", parts, 0, i + 1);
                FileTreeNode folder = current.Children.FirstOrDefault(c => c.IsFolder && string.Equals(c.Name, parts[i], StringComparison.OrdinalIgnoreCase));
                if (folder == null)
                {
                    folder = new FileTreeNode { Name = parts[i], Path = folderPath };
                    current.Children.Add(folder);
                }
                current = folder;
            }
            current.Children.Add(new FileTreeNode { Name = parts[parts.Length - 1], Path = item.DisplayPath, Item = item });
        }

        /// <summary>아래 파일 수를 센다.</summary>
        private static int Finish(FileTreeNode node)
        {
            if (node.IsFolder == false)
            {
                return 1;
            }
            node.FileCount = node.Children.Sum(Finish);
            return node.FileCount;
        }

        private static void Sort(FileTreeNode node)
        {
            node.Children.Sort((a, b) =>
            {
                if (a.IsFolder != b.IsFolder)
                {
                    return a.IsFolder ? -1 : 1;
                }
                return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            foreach (FileTreeNode child in node.Children)
            {
                Sort(child);
            }
        }

        private static FileTreeNode Compact(FileTreeNode node)
        {
            if (node.IsFolder == false)
            {
                return node;
            }
            while (node.Children.Count == 1 && node.Children[0].IsFolder)
            {
                FileTreeNode only = node.Children[0];
                FileTreeNode merged = new() { Name = node.Name + "/" + only.Name, Path = only.Path, FileCount = only.FileCount };
                merged.Children.AddRange(only.Children);
                node = merged;
            }
            for (int i = 0; i < node.Children.Count; i++)
            {
                node.Children[i] = Compact(node.Children[i]);
            }
            return node;
        }
    }
}
