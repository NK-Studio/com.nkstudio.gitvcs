using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>파일 트리의 한 마디. <see cref="Item"/> 이 null 이면 폴더.</summary>
    public sealed class FileTreeNode
    {
        /// <summary>트리에 보이는 이름(폴더는 그 단계의 이름 하나).</summary>
        public string Name;

        /// <summary>폴더는 그 폴더의 경로, 파일은 표시 경로. 펼침 상태를 기억하는 열쇠로 쓴다.</summary>
        public string Path;

        public FileListItem Item;
        public readonly List<FileTreeNode> Children = new();

        /// <summary>아래에 있는 파일 수(폴더 체크박스의 전체·일부 판단에 쓴다).</summary>
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
    /// - 폴더는 한 단계에 한 줄이다. 하위 폴더 하나만 가진 폴더도 "StatusTest/Scripts" 처럼 합치지 않는다(GitKraken 과 같다).
    /// </summary>
    public static class FileTreeBuilder
    {
        /// <summary>
        /// 접힌 폴더 옆에 보일 상태별 파일 수(GitKraken 처럼 ✏ 1 + 1 − 1). 수정·추가·삭제·이름 변경·충돌 순이고 0 인 상태는 뺀다.
        /// untracked 는 추가와 같은 + 로 보이므로 추가에 더한다.
        /// </summary>
        public static List<(GitFileState state, int count)> StateSummary(IEnumerable<FileListItem> files)
        {
            int[] counts = new int[Enum.GetValues(typeof(GitFileState)).Length];
            foreach (FileListItem file in files)
            {
                GitFileState state = file.State == GitFileState.Untracked ? GitFileState.Added : file.State;
                counts[(int)state]++;
            }
            List<(GitFileState, int)> summary = new();
            foreach (GitFileState state in SummaryOrder)
            {
                if (counts[(int)state] > 0)
                {
                    summary.Add((state, counts[(int)state]));
                }
            }
            return summary;
        }

        private static readonly GitFileState[] SummaryOrder =
        {
            GitFileState.Modified, GitFileState.Added, GitFileState.Deleted, GitFileState.Renamed, GitFileState.Conflicted,
        };

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
            return root.Children;
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
    }
}
