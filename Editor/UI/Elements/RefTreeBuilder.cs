using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    public enum RefSection
    {
        Local,
        Remote,
        Worktrees,
        Stashes,
        Submodules,
        Tags,
    }

    public enum RefNodeKind
    {
        Section,
        Folder,
        Remote,
        Branch,
        Tag,
        Stash,
        Worktree,
        Submodule,
    }

    /// <summary>
    /// 사이드바 참조 트리의 한 줄. 잎(브랜치·태그…)은 해당 참조를 하나 들고, 구역·폴더·원격 줄은 아이만 가진다.
    /// </summary>
    public sealed class RefNode
    {
        public RefNodeKind Kind;
        public RefSection Section;

        /// <summary>
        /// 펼침 상태를 기억하는 키("section:Local", "folder:Local/feature", "branch:refs/heads/main").
        /// </summary>
        public string Id;

        public string Label;

        /// <summary>
        /// 오른쪽 작은 글자: 구역은 개수, 브랜치는 "1↑ 2↓", 워크트리는 브랜치 이름.
        /// </summary>
        public string Badge = string.Empty;

        public string Tooltip;
        public bool IsCurrent;

        public GitBranchRef Branch;
        public GitTagRef Tag;
        public GitStashRef Stash;
        public GitWorktreeRef Worktree;
        public GitSubmoduleRef Submodule;

        public readonly List<RefNode> Children = new();

        /// <summary>
        /// 그래프에서 보일 커밋. 없으면 null(워크트리·서브모듈·구역).
        /// </summary>
        public string Commit => Branch?.Hash ?? Tag?.Commit ?? Stash?.Hash;

        public int LeafCount => Children.Count == 0 ? (Kind == RefNodeKind.Section || Kind == RefNodeKind.Folder || Kind == RefNodeKind.Remote ? 0 : 1) : Children.Sum(c => c.LeafCount);
    }

    /// <summary>
    /// <see cref="GitRefSet"/> → 참조 트리. 로컬·태그는 '/' 로 폴더를 묶고(feature/a, feature/b → feature 아래 a, b),
    /// 원격은 원격 이름 → 브랜치 폴더로 묶는다. 필터가 있으면 이름에 그 글자가 들어간 잎과 그 조상만 남긴다.
    /// 화면과 상관없는 순수 규칙이라 테스트로 확인한다.
    /// </summary>
    public static class RefTreeBuilder
    {
        public static readonly RefSection[] Sections = (RefSection[])Enum.GetValues(typeof(RefSection));

        /// <summary>
        /// 처음 열었을 때 펼쳐 둘지. 로컬·리모트와 그 안의 폴더는 펼치고, 나머지 구역은 접는다.
        /// </summary>
        public static bool ExpandedByDefault(string id)
        {
            return id != "section:" + RefSection.Worktrees
                   && id != "section:" + RefSection.Stashes
                   && id != "section:" + RefSection.Submodules
                   && id != "section:" + RefSection.Tags;
        }

        /// <param name="sectionTitle">구역 이름(번역). 테스트는 enum 이름을 넘긴다.</param>
        public static List<RefNode> Build(GitRefSet refs, string filter, Func<RefSection, string> sectionTitle)
        {
            string needle = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
            bool Matches(string text) => needle == null || (text ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

            List<RefNode> roots = new();
            foreach (RefSection section in Sections)
            {
                RefNode root = new() { Kind = RefNodeKind.Section, Section = section, Id = "section:" + section, Label = sectionTitle(section) };
                switch (section)
                {
                    case RefSection.Local:
                        foreach (GitBranchRef branch in refs.Branches.Where(b => b.IsRemote == false && Matches(b.Name)).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            RefNode leaf = BranchLeaf(branch, section);
                            AddByPath(root, section, "Local", branch.Name, leaf);
                        }
                        break;
                    case RefSection.Remote:
                        foreach (GitBranchRef branch in refs.Branches.Where(b => b.IsRemote && b.Name.EndsWith("/HEAD", StringComparison.Ordinal) == false && Matches(b.Name))
                                     .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            (string remote, string name) = GitCommitCommands.SplitRemoteBranch(branch.Name);
                            RefNode remoteNode = root.Children.FirstOrDefault(c => c.Kind == RefNodeKind.Remote && c.Label == remote);
                            if (remoteNode == null)
                            {
                                remoteNode = new RefNode { Kind = RefNodeKind.Remote, Section = section, Id = "remote:" + remote, Label = remote };
                                root.Children.Add(remoteNode);
                            }
                            AddByPath(remoteNode, section, "Remote/" + remote, name, BranchLeaf(branch, section));
                        }
                        break;
                    case RefSection.Worktrees:
                        foreach (GitWorktreeRef worktree in refs.Worktrees.Where(w => Matches(w.Path) || Matches(w.Branch)))
                        {
                            root.Children.Add(new RefNode
                            {
                                Kind = RefNodeKind.Worktree,
                                Section = section,
                                Id = "worktree:" + worktree.Path,
                                Label = worktree.DisplayName,
                                Badge = worktree.IsDetached ? GitDisplay.ShortRevision(worktree.Head) : worktree.Branch,
                                Tooltip = worktree.Path,
                                IsCurrent = worktree.IsCurrent,
                                Worktree = worktree,
                            });
                        }
                        break;
                    case RefSection.Stashes:
                        foreach (GitStashRef stash in refs.Stashes.Where(s => Matches(s.Message) || Matches(s.Name)))
                        {
                            root.Children.Add(new RefNode
                            {
                                Kind = RefNodeKind.Stash,
                                Section = section,
                                Id = "stash:" + stash.Hash,
                                Label = stash.Message,
                                Tooltip = $"{stash.Name}  ·  {GitDisplay.FormatDate(stash.Date)}\n{stash.Message}",
                                Stash = stash,
                            });
                        }
                        break;
                    case RefSection.Submodules:
                        foreach (GitSubmoduleRef submodule in refs.Submodules.Where(s => Matches(s.Path)))
                        {
                            root.Children.Add(new RefNode
                            {
                                Kind = RefNodeKind.Submodule,
                                Section = section,
                                Id = "submodule:" + submodule.Path,
                                Label = submodule.Path,
                                Badge = SubmoduleBadge(submodule.State),
                                Tooltip = $"{submodule.Path}\n{GitDisplay.ShortRevision(submodule.Commit)} {submodule.Describe}".TrimEnd(),
                                Submodule = submodule,
                            });
                        }
                        break;
                    case RefSection.Tags:
                        foreach (GitTagRef tag in refs.Tags.Where(t => Matches(t.Name)))
                        {
                            RefNode leaf = new()
                            {
                                Kind = RefNodeKind.Tag,
                                Section = section,
                                Id = "tag:" + tag.Name,
                                Tooltip = string.IsNullOrEmpty(tag.Subject) ? tag.Name : $"{tag.Name}\n{tag.Subject}",
                                Tag = tag,
                            };
                            AddByPath(root, section, "Tags", tag.Name, leaf);
                        }
                        break;
                }
                root.Badge = root.LeafCount.ToString();
                // 검색 중에는 걸린 것이 있는 구역만 남긴다(Fork 처럼 일치하는 요소만 보인다). 검색하지 않을 때는 빈 구역도 둔다.
                if (needle != null && root.Children.Count == 0)
                {
                    continue;
                }
                roots.Add(root);
            }
            return roots;
        }

        /// <summary>
        /// "feature/x/y" 를 feature → x 폴더 아래 잎 "y" 로 넣는다. 잎의 <see cref="RefNode.Label"/> 은 마지막 조각.
        /// </summary>
        private static void AddByPath(RefNode parent, RefSection section, string idPrefix, string path, RefNode leaf)
        {
            string[] parts = path.Split('/');
            string folderPath = idPrefix;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                folderPath += "/" + parts[i];
                string id = "folder:" + folderPath;
                RefNode folder = parent.Children.FirstOrDefault(c => c.Kind == RefNodeKind.Folder && c.Id == id);
                if (folder == null)
                {
                    folder = new RefNode { Kind = RefNodeKind.Folder, Section = section, Id = id, Label = parts[i] };
                    // 폴더는 같은 높이의 잎보다 위에 둔다(Fork·GitKraken 과 같다).
                    int insertAt = parent.Children.FindIndex(c => c.Kind != RefNodeKind.Folder);
                    parent.Children.Insert(insertAt < 0 ? parent.Children.Count : insertAt, folder);
                }
                parent = folder;
            }
            leaf.Label = parts[parts.Length - 1];
            parent.Children.Add(leaf);
        }

        private static RefNode BranchLeaf(GitBranchRef branch, RefSection section)
        {
            string tooltip = branch.Name;
            if (string.IsNullOrEmpty(branch.Upstream) == false)
            {
                tooltip += $"  →  {branch.Upstream} {branch.Track}".TrimEnd();
            }
            if (string.IsNullOrEmpty(branch.Subject) == false)
            {
                tooltip += $"\n{branch.ShortHash}  {branch.Subject}";
            }
            return new RefNode
            {
                Kind = RefNodeKind.Branch,
                Section = section,
                Id = "branch:" + (branch.RefName ?? branch.Name),
                Badge = GitRefs.TrackBadge(branch.Track),
                Tooltip = tooltip,
                IsCurrent = branch.IsCurrent,
                Branch = branch,
            };
        }

        private static string SubmoduleBadge(GitSubmoduleState state)
        {
            return state switch
            {
                GitSubmoduleState.NotInitialized => "−",
                GitSubmoduleState.Modified => "+",
                GitSubmoduleState.Conflicted => "!",
                _ => string.Empty,
            };
        }
    }
}
