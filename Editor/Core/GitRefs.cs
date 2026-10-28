using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GitVersionControl
{
    public sealed class GitTagRef
    {
        public string Name;

        /// <summary>
        /// 태그가 가리키는 커밋. 주석 태그(annotated)면 태그 객체가 아니라 그 안의 커밋이다.
        /// </summary>
        public string Commit;

        public bool IsAnnotated;

        /// <summary>
        /// 주석 태그의 메시지 첫 줄. 가벼운 태그면 가리키는 커밋 메시지 첫 줄.
        /// </summary>
        public string Subject;
    }

    public sealed class GitStashRef
    {
        /// <summary>
        /// "stash@{0}". git 명령에 그대로 넘긴다.
        /// </summary>
        public string Name;

        /// <summary>
        /// stash 커밋 해시. 변경 보기(첫 부모 대비 = 넣을 때의 작업 트리 변경)에 쓴다.
        /// </summary>
        public string Hash;

        public DateTime Date;

        /// <summary>
        /// "WIP on main: abc1234 메시지" 또는 stash 할 때 준 메시지.
        /// </summary>
        public string Message;
    }

    public sealed class GitWorktreeRef
    {
        /// <summary>
        /// 슬래시로 쓴 절대 경로.
        /// </summary>
        public string Path;

        public string Head;

        /// <summary>
        /// "main". 분리된 HEAD·bare 면 빈 문자열.
        /// </summary>
        public string Branch = string.Empty;

        public bool IsBare;
        public bool IsDetached;
        public bool IsLocked;
        public bool IsPrunable;

        /// <summary>
        /// 이 Unity 프로젝트가 들어 있는 워크트리.
        /// </summary>
        public bool IsCurrent;

        public string DisplayName => System.IO.Path.GetFileName(Path.TrimEnd('/'));
    }

    public enum GitSubmoduleState
    {
        /// <summary>
        /// 기록된 커밋을 체크아웃해 둠.
        /// </summary>
        UpToDate,

        /// <summary>
        /// 아직 받지 않음(<c>-</c>). Update 로 받는다.
        /// </summary>
        NotInitialized,

        /// <summary>
        /// 체크아웃한 커밋이 상위 저장소에 기록된 것과 다름(<c>+</c>).
        /// </summary>
        Modified,

        /// <summary>
        /// 병합 충돌(<c>U</c>).
        /// </summary>
        Conflicted,
    }

    public sealed class GitSubmoduleRef
    {
        /// <summary>
        /// 저장소 루트 기준 경로.
        /// </summary>
        public string Path;

        public string Commit;
        public GitSubmoduleState State;

        /// <summary>
        /// <c>git describe</c> 결과("v1.2-3-gabc"). 없으면 빈 문자열.
        /// </summary>
        public string Describe = string.Empty;
    }

    /// <summary>
    /// 사이드바 참조 트리가 보이는 것 한 벌. 하나를 읽지 못해도 나머지는 보인다 — 실패 메시지는 <see cref="Error"/> 에 모은다.
    /// </summary>
    public sealed class GitRefSet
    {
        public List<GitBranchRef> Branches = new();
        public List<GitTagRef> Tags = new();
        public List<GitStashRef> Stashes = new();
        public List<GitWorktreeRef> Worktrees = new();
        public List<GitSubmoduleRef> Submodules = new();

        /// <summary>
        /// 원격 이름 → 주소(remote.&lt;이름&gt;.url). 원격이 없으면 비어 있다.
        /// </summary>
        public Dictionary<string, string> RemoteUrls = new();

        /// <summary>
        /// 사이드바 Pull Requests 구역 상태. git 이 아니라 GitHub 에서 따로 읽어 사이드바가 채운다. null 이면 아직 모른다(읽는 중으로 그린다).
        /// </summary>
        public GitPullRequestSet PullRequests;

        public string Error;
    }

    /// <summary>
    /// 브랜치 외 참조(태그·스태시·워크트리·서브모듈) 읽기. 출력 구분자로 제어 문자(0x1E 레코드, 0x1F 필드)를 써서
    /// 메시지의 어떤 글자와도 섞이지 않게 한다(<see cref="GitHistory"/> 와 같은 방식). 파서는 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitRefs
    {
        private const char RecordSeparator = '\x1e';
        private const char FieldSeparator = '\x1f';

        public static readonly string[] TagsCommand =
        {
            "for-each-ref", "refs/tags", "--sort=-creatordate",
            "--format=%(refname:short)%1f%(objectname)%1f%(*objectname)%1f%(objecttype)%1f%(contents:subject)%1e",
        };

        public static readonly string[] StashesCommand = { "stash", "list", "--format=%gd%x1f%H%x1f%ct%x1f%s%x1e" };
        public static readonly string[] WorktreesCommand = { "worktree", "list", "--porcelain" };
        public static readonly string[] SubmodulesCommand = { "submodule", "status" };

        /// <summary>
        /// 원격 주소 목록. 원격이 하나도 없으면 아무것도 찾지 못해 실패(종료 코드 1)하므로 오류로 모으지 않는다.
        /// </summary>
        public static readonly string[] RemoteUrlsCommand = { "config", "--get-regexp", @"^remote\..*\.url$" };

        /// <summary>
        /// 인덱스의 모든 항목. 서브모듈(gitlink) 경로만 빠르게 알 때 쓴다 — <c>submodule status</c> 는 셸 스크립트로 시작하고
        /// 서브모듈마다 <c>git describe</c> 를 여러 번 돌려 Windows 에서 1초 넘게 걸린다. 이건 프로세스 하나다.
        /// </summary>
        public static readonly string[] GitlinksCommand = { "ls-files", "--stage", "-z" };

        /// <summary>
        /// 다섯 가지를 동시에 읽는다. 메인 스레드에서 불러도 된다(git 은 작업 스레드에서 돈다).
        /// </summary>
        public static async Task<GitRefSet> LoadAsync()
        {
            Task<(List<GitBranchRef> branches, string error)> branchTask = GitHistory.BranchesAsync();
            Task<GitResult> tagTask = GitRepository.RunAsync(TagsCommand);
            Task<GitResult> stashTask = GitRepository.RunAsync(StashesCommand);
            Task<GitResult> worktreeTask = GitRepository.RunAsync(WorktreesCommand);
            Task<GitResult> submoduleTask = GitRepository.RunAsync(SubmodulesCommand);
            Task<GitResult> remoteTask = GitRepository.RunAsync(RemoteUrlsCommand);

            (List<GitBranchRef> branches, string branchError) = await branchTask;
            GitResult tags = await tagTask;
            GitResult stashes = await stashTask;
            GitResult worktrees = await worktreeTask;
            GitResult submodules = await submoduleTask;
            GitResult remotes = await remoteTask;

            List<string> errors = new();
            if (string.IsNullOrEmpty(branchError) == false)
            {
                errors.Add(branchError);
            }
            // 서브모듈이 없는 저장소에서도 submodule status 는 성공한다. 실패만 모은다.
            // 단, .gitmodules 없이 인덱스에 올라간 하위 저장소(gitlink — 폴더째 Stage 하다 들어간 패키지 저장소 등)가 있으면
            // submodule status 가 "no submodule mapping found" 로 통째로 실패한다. 이건 참조 목록을 못 읽을 일이 아니므로
            // 사이드바에 오류로 띄우지 않고 서브모듈 구역만 비운다.
            foreach (GitResult result in new[] { tags, stashes, worktrees, submodules })
            {
                if (result.Success || string.IsNullOrWhiteSpace(result.Message))
                {
                    continue;
                }
                if (ReferenceEquals(result, submodules) && IsUnmappedGitlinkError(result.Message))
                {
                    continue;
                }
                errors.Add(result.Message);
            }

            return new GitRefSet
            {
                Branches = branches,
                Tags = tags.Success ? ParseTags(tags.Output) : new List<GitTagRef>(),
                Stashes = stashes.Success ? ParseStashes(stashes.Output) : new List<GitStashRef>(),
                Worktrees = worktrees.Success ? ParseWorktrees(worktrees.Output, GitRepository.RepoRoot) : new List<GitWorktreeRef>(),
                Submodules = submodules.Success ? ParseSubmodules(submodules.Output) : new List<GitSubmoduleRef>(),
                RemoteUrls = remotes.Success ? ParseRemoteUrls(remotes.Output) : new Dictionary<string, string>(),
                Error = errors.Count == 0 ? null : string.Join("\n", errors),
            };
        }

        /// <summary>
        /// <c>config --get-regexp</c>: 줄마다 "remote.&lt;이름&gt;.url 주소". 이름에 점이 들어갈 수 있어 앞뒤를 잘라 낸다.
        /// </summary>
        public static Dictionary<string, string> ParseRemoteUrls(string output)
        {
            const string prefix = "remote.";
            const string suffix = ".url";
            Dictionary<string, string> urls = new();
            foreach (string line in (output ?? string.Empty).Split('\n'))
            {
                int space = line.IndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }
                string key = line.Substring(0, space);
                if (key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == false || key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) == false
                                                                                       || key.Length <= prefix.Length + suffix.Length)
                {
                    continue;
                }
                string name = key.Substring(prefix.Length, key.Length - prefix.Length - suffix.Length);
                // 같은 원격에 주소가 여럿이면 git 처럼 첫 주소를 쓴다.
                if (urls.ContainsKey(name) == false)
                {
                    urls[name] = line.Substring(space + 1).Trim();
                }
            }
            return urls;
        }

        /// <summary>
        /// github.com 원격인지(https://github.com/…, git@github.com:…, ssh://git@github.com/…).
        /// </summary>
        public static bool IsGitHubUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return false;
            }
            string host = url;
            int scheme = host.IndexOf("://", StringComparison.Ordinal);
            if (scheme >= 0)
            {
                host = host.Substring(scheme + 3);
            }
            int at = host.IndexOf('@');
            if (at >= 0)
            {
                host = host.Substring(at + 1);
            }
            int end = host.IndexOfAny(new[] { '/', ':' });
            if (end >= 0)
            {
                host = host.Substring(0, end);
            }
            return string.Equals(host, "github.com", StringComparison.OrdinalIgnoreCase) || host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
        }

        public static List<GitTagRef> ParseTags(string output)
        {
            List<GitTagRef> tags = new();
            foreach (string[] fields in Records(output, 5))
            {
                bool annotated = fields[3] == "tag";
                tags.Add(new GitTagRef
                {
                    Name = fields[0],
                    // 주석 태그는 *objectname(벗겨 낸 대상)이 커밋이다. 가벼운 태그는 비어 있다.
                    Commit = annotated && fields[2].Length > 0 ? fields[2] : fields[1],
                    IsAnnotated = annotated,
                    Subject = GitDisplay.Nfc(fields[4]),
                });
            }
            return tags;
        }

        public static List<GitStashRef> ParseStashes(string output)
        {
            List<GitStashRef> stashes = new();
            foreach (string[] fields in Records(output, 4))
            {
                long.TryParse(fields[2], out long seconds);
                stashes.Add(new GitStashRef
                {
                    Name = fields[0],
                    Hash = fields[1],
                    Date = seconds > 0 ? DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime : DateTime.MinValue,
                    Message = GitDisplay.Nfc(fields[3]),
                });
            }
            return stashes;
        }

        /// <summary>
        /// <c>worktree list --porcelain</c>: 빈 줄로 나뉜 덩어리마다 "worktree 경로", "HEAD 해시", "branch refs/heads/x" 또는 "detached",
        /// 그리고 "bare"·"locked [이유]"·"prunable [이유]" 가 붙을 수 있다.
        /// </summary>
        /// <param name="currentRoot">이 프로젝트가 들어 있는 저장소 루트. 같은 경로의 워크트리를 현재로 표시한다.</param>
        public static List<GitWorktreeRef> ParseWorktrees(string output, string currentRoot)
        {
            List<GitWorktreeRef> worktrees = new();
            GitWorktreeRef current = null;
            string normalizedRoot = NormalizePath(currentRoot);
            foreach (string rawLine in (output ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.TrimEnd();
                if (line.StartsWith("worktree ", StringComparison.Ordinal))
                {
                    current = new GitWorktreeRef { Path = line.Substring(9).Replace('\\', '/') };
                    current.IsCurrent = normalizedRoot != null && NormalizePath(current.Path) == normalizedRoot;
                    worktrees.Add(current);
                }
                else if (current == null)
                {
                    continue;
                }
                else if (line.StartsWith("HEAD ", StringComparison.Ordinal))
                {
                    current.Head = line.Substring(5);
                }
                else if (line.StartsWith("branch ", StringComparison.Ordinal))
                {
                    current.Branch = GitHistory.ShortRefName(line.Substring(7));
                }
                else if (line == "detached")
                {
                    current.IsDetached = true;
                }
                else if (line == "bare")
                {
                    current.IsBare = true;
                }
                else if (line == "locked" || line.StartsWith("locked ", StringComparison.Ordinal))
                {
                    current.IsLocked = true;
                }
                else if (line == "prunable" || line.StartsWith("prunable ", StringComparison.Ordinal))
                {
                    current.IsPrunable = true;
                }
            }
            return worktrees;
        }

        /// <summary>
        /// <c>submodule status</c>: 줄마다 상태 글자(' ', '-', '+', 'U'), 해시, 경로, 그리고 있으면 " (describe)".
        /// </summary>
        /// <summary>
        /// <c>git submodule status</c> 가 .gitmodules 에 없는 gitlink 때문에 실패했는지
        /// ("fatal: no submodule mapping found in .gitmodules for path '…'").
        /// </summary>
        public static bool IsUnmappedGitlinkError(string message)
        {
            return message != null && message.IndexOf("no submodule mapping found", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// <c>ls-files --stage -z</c>: NUL 로 나뉜 "모드 해시 단계\t경로". 모드가 160000(gitlink)인 경로만 순서대로 고른다.
        /// 충돌 중이면 같은 경로가 단계마다 나오므로 한 번만 넣는다.
        /// </summary>
        public static List<string> ParseGitlinkPaths(string output)
        {
            List<string> paths = new();
            foreach (string entry in (output ?? string.Empty).Split('\0'))
            {
                int tab = entry.IndexOf('\t');
                if (tab <= 0 || entry.StartsWith("160000 ", StringComparison.Ordinal) == false)
                {
                    continue;
                }
                string path = entry.Substring(tab + 1);
                if (paths.Count == 0 || paths[paths.Count - 1] != path)
                {
                    paths.Add(path);
                }
            }
            return paths;
        }

        public static List<GitSubmoduleRef> ParseSubmodules(string output)
        {
            List<GitSubmoduleRef> submodules = new();
            foreach (string rawLine in (output ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                if (rawLine.Length < 3)
                {
                    continue;
                }
                GitSubmoduleState state = rawLine[0] switch
                {
                    '-' => GitSubmoduleState.NotInitialized,
                    '+' => GitSubmoduleState.Modified,
                    'U' => GitSubmoduleState.Conflicted,
                    _ => GitSubmoduleState.UpToDate,
                };
                string rest = rawLine.Substring(1);
                int space = rest.IndexOf(' ');
                if (space <= 0)
                {
                    continue;
                }
                string commit = rest.Substring(0, space);
                string path = rest.Substring(space + 1);
                string describe = string.Empty;
                int paren = path.LastIndexOf(" (", StringComparison.Ordinal);
                if (paren > 0 && path.EndsWith(")", StringComparison.Ordinal))
                {
                    describe = path.Substring(paren + 2, path.Length - paren - 3);
                    path = path.Substring(0, paren);
                }
                submodules.Add(new GitSubmoduleRef { Path = path, Commit = commit, State = state, Describe = describe });
            }
            return submodules;
        }

        /// <summary>
        /// <c>%(upstream:track)</c>("[ahead 1, behind 2]")에서 앞섬·뒤처짐 수. 없거나 "[gone]" 이면 0.
        /// </summary>
        public static (int ahead, int behind) ParseTrack(string track)
        {
            int ahead = 0;
            int behind = 0;
            foreach (string part in (track ?? string.Empty).Trim('[', ']', ' ').Split(','))
            {
                string[] words = part.Trim().Split(' ');
                if (words.Length == 2 && int.TryParse(words[1], out int count))
                {
                    if (words[0] == "ahead")
                    {
                        ahead = count;
                    }
                    else if (words[0] == "behind")
                    {
                        behind = count;
                    }
                }
            }
            return (ahead, behind);
        }

        /// <summary>
        /// 사이드바 배지 "1↑ 2↓". 앞섬·뒤처짐이 없으면 빈 문자열.
        /// </summary>
        public static string TrackBadge(string track)
        {
            (int ahead, int behind) = ParseTrack(track);
            List<string> parts = new();
            if (ahead > 0)
            {
                parts.Add(ahead + "↑");
            }
            if (behind > 0)
            {
                parts.Add(behind + "↓");
            }
            return string.Join(" ", parts);
        }

        private static IEnumerable<string[]> Records(string output, int fieldCount)
        {
            foreach (string record in (output ?? string.Empty).Split(RecordSeparator))
            {
                string trimmed = record.Trim('\r', '\n');
                if (trimmed.Length == 0)
                {
                    continue;
                }
                string[] fields = trimmed.Split(new[] { FieldSeparator }, fieldCount);
                if (fields.Length == fieldCount)
                {
                    yield return fields;
                }
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return null;
            }
            string normalized = path.Replace('\\', '/').TrimEnd('/');
            // Windows 는 대소문자를 가리지 않는다(드라이브 글자 e: / E: 도 같은 곳).
            return UnityEngine.Application.platform == UnityEngine.RuntimePlatform.WindowsEditor ? normalized.ToLowerInvariant() : normalized;
        }
    }
}
