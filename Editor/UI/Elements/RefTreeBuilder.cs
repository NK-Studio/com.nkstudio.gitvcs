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
        PullRequests,
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

        /// <summary>Pull Requests 구역의 묶음(내 PR · 나에게 배정됨 · 내 리뷰 대기 · 전체). 펼치고 접을 수 있다.</summary>
        PullRequestGroup,

        PullRequest,

        /// <summary>안내 한 줄(읽는 중 · 로그인 필요 · 오류). 참조가 아니다.</summary>
        Info,
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

        /// <summary>
        /// github.com 원격(원격 줄만). 구름 대신 GitHub 아이콘을 쓴다.
        /// </summary>
        public bool IsGitHub;

        public GitBranchRef Branch;
        public GitTagRef Tag;
        public GitStashRef Stash;
        public GitWorktreeRef Worktree;
        public GitSubmoduleRef Submodule;
        public GitPullRequestInfo PullRequest;

        public readonly List<RefNode> Children = new();

        /// <summary>
        /// 그래프에서 보일 커밋. 없으면 null(워크트리·서브모듈·구역).
        /// </summary>
        public string Commit => Branch?.Hash ?? Tag?.Commit ?? Stash?.Hash ?? PullRequest?.HeadSha;

        public int LeafCount => Children.Count == 0 ? (IsContainer || Kind == RefNodeKind.Info ? 0 : 1) : Children.Sum(c => c.LeafCount);

        /// <summary>
        /// 아이를 갖는 줄(구역 · 폴더 · 원격 · PR 묶음).
        /// </summary>
        public bool IsContainer => Kind is RefNodeKind.Section or RefNodeKind.Folder or RefNodeKind.Remote or RefNodeKind.PullRequestGroup;
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

        /// <summary>
        /// 이 저장소에서 보일 구역인지. GitHub 원격이 없는(로컬뿐인) 저장소는 GitHub 에서 읽는 Pull Requests 구역을 숨기고,
        /// GitKraken 처럼 git 만으로 다루는 구역(로컬·원격·워크트리·스태시·서브모듈·태그)만 둔다.
        /// </summary>
        public static bool IsShown(RefSection section, GitRefSet refs)
        {
            return section != RefSection.PullRequests || GitPullRequestForm.GitHubRemotes(refs.RemoteUrls).Count > 0;
        }

        /// <param name="sectionTitle">구역 이름(번역). 테스트는 enum 이름을 넘긴다.</param>
        /// <param name="localize">PR 묶음 이름·안내 문구 번역(키 → 글자). null 이면 키 그대로(테스트).</param>
        public static List<RefNode> Build(GitRefSet refs, string filter, Func<RefSection, string> sectionTitle, Func<string, string> localize = null)
        {
            localize ??= key => key;
            string needle = string.IsNullOrWhiteSpace(filter) ? null : filter.Trim();
            bool Matches(string text) => needle == null || (text ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

            List<RefNode> roots = new();
            foreach (RefSection section in Sections)
            {
                if (IsShown(section, refs) == false)
                {
                    continue;
                }
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
                                refs.RemoteUrls.TryGetValue(remote, out string url);
                                remoteNode = new RefNode
                                {
                                    Kind = RefNodeKind.Remote, Section = section, Id = "remote:" + remote, Label = remote, Tooltip = url, IsGitHub = GitRefs.IsGitHubUrl(url),
                                };
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
                    case RefSection.PullRequests:
                        AddPullRequests(root, refs.PullRequests, needle, Matches, localize);
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
                // PR 은 여러 묶음에 겹쳐 나오므로 구역 개수는 "전체" 묶음 수다.
                root.Badge = (section == RefSection.PullRequests ? PullRequestCount(refs.PullRequests) : root.LeafCount).ToString();
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
        /// Pull Requests 구역 머리의 개수(열린 PR 수). 목록을 아직 못 읽었으면 0.
        /// </summary>
        public static int PullRequestCount(GitPullRequestSet set)
        {
            return set == null || set.IsLoading || set.NeedsSignIn || set.NoGitHubRemote || set.Error != null ? 0 : set.All.Count;
        }

        /// <summary>
        /// 목록이 있으면 네 묶음과 그 아래 PR, 없으면 안내 한 줄(검색 중에는 안내를 뺀다).
        /// </summary>
        private static void AddPullRequests(RefNode root, GitPullRequestSet set, string needle, Func<string, bool> matches, Func<string, string> localize)
        {
            string info = set == null || set.IsLoading ? localize("common.loading")
                : set.NeedsSignIn ? localize("prList.signIn")
                : set.NoGitHubRemote ? localize("prList.noRemote")
                : set.Error != null ? localize("prList.error") + " " + set.Error
                : null;
            if (info != null)
            {
                if (needle == null)
                {
                    root.Children.Add(new RefNode { Kind = RefNodeKind.Info, Section = RefSection.PullRequests, Id = "prinfo", Label = info, Tooltip = info });
                }
                return;
            }
            foreach (GitPullRequestList.Group group in GitPullRequestList.Groups)
            {
                List<GitPullRequestInfo> pulls = GitPullRequestList.InGroup(set, group)
                    .Where(pr => matches(pr.Title) || matches("#" + pr.Number) || matches(pr.Author) || matches(pr.HeadRef))
                    .ToList();
                if (needle != null && pulls.Count == 0)
                {
                    continue;
                }
                RefNode groupNode = new()
                {
                    Kind = RefNodeKind.PullRequestGroup,
                    Section = RefSection.PullRequests,
                    Id = "prgroup:" + group,
                    Label = localize("prList.group" + group),
                    Badge = pulls.Count.ToString(),
                };
                foreach (GitPullRequestInfo pr in pulls)
                {
                    groupNode.Children.Add(new RefNode
                    {
                        Kind = RefNodeKind.PullRequest,
                        Section = RefSection.PullRequests,
                        Id = "pr:" + group + ":" + pr.Number,
                        Label = $"#{pr.Number} {pr.Title}",
                        Badge = pr.IsDraft ? localize("prList.draft") : pr.Author,
                        Tooltip = $"#{pr.Number} {pr.Title}\n{pr.Author}  ·  {pr.HeadRef} → {pr.BaseRef}",
                        PullRequest = pr,
                    });
                }
                root.Children.Add(groupNode);
            }
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
