using System.Collections.Generic;

namespace GitVersionControl
{
    /// <summary>
    /// <c>git status</c> 항목을 Fork·GitKraken 처럼 "Unstaged(작업 트리 ↔ 인덱스)"와 "Staged(인덱스 ↔ HEAD)" 두 목록으로 나눈다.
    ///
    /// 한 파일이 양쪽에 다 나올 수 있다(MM: 일부만 stage 한 뒤 또 고친 파일). 그래서 쪽마다 상태를 다시 매긴 복사본을 만든다 —
    /// 예: "AM" 은 Staged 쪽에서는 Added, Unstaged 쪽에서는 Modified. 충돌(U·AA·DD)은 해결 전에는 stage 할 대상이 아니라 Unstaged 에만 둔다.
    /// </summary>
    public static class GitStaging
    {
        public static List<GitStatusEntry> Unstaged(IEnumerable<GitStatusEntry> entries)
        {
            List<GitStatusEntry> result = new();
            foreach (GitStatusEntry entry in entries)
            {
                if (entry.State == GitFileState.Conflicted || entry.State == GitFileState.Untracked)
                {
                    result.Add(Copy(entry, entry.State, keepOriginal: false));
                    continue;
                }
                if (entry.WorkTreeCode == ' ' || entry.WorkTreeCode == '!')
                {
                    continue;
                }
                result.Add(Copy(entry, StateOf(entry.WorkTreeCode), keepOriginal: false));
            }
            return result;
        }

        public static List<GitStatusEntry> Staged(IEnumerable<GitStatusEntry> entries)
        {
            List<GitStatusEntry> result = new();
            foreach (GitStatusEntry entry in entries)
            {
                if (entry.State == GitFileState.Conflicted || entry.State == GitFileState.Untracked)
                {
                    continue;
                }
                if (entry.IndexCode == ' ' || entry.IndexCode == '!')
                {
                    continue;
                }
                result.Add(Copy(entry, StateOf(entry.IndexCode), keepOriginal: true));
            }
            return result;
        }

        /// <summary>
        /// 고른 항목에 같은 목록의 .meta 짝을 더한다. Unity 에셋은 파일과 .meta 가 한 쌍이라, 한쪽만 stage 하면
        /// 다른 사람 프로젝트에서 GUID 가 어긋난다. 예: [A.cs] + 목록에 A.cs.meta → [A.cs, A.cs.meta].
        /// </summary>
        public static List<GitStatusEntry> WithMetaPairs(IEnumerable<GitStatusEntry> picked, IReadOnlyList<GitStatusEntry> sameSide)
        {
            Dictionary<string, GitStatusEntry> byPath = new(System.StringComparer.OrdinalIgnoreCase);
            foreach (GitStatusEntry entry in sameSide)
            {
                byPath[entry.RepoPath] = entry;
            }
            List<GitStatusEntry> result = new();
            HashSet<string> added = new(System.StringComparer.OrdinalIgnoreCase);
            foreach (GitStatusEntry entry in picked)
            {
                AddOnce(entry);
                string pairPath = entry.RepoPath.EndsWith(".meta", System.StringComparison.OrdinalIgnoreCase)
                    ? entry.RepoPath.Substring(0, entry.RepoPath.Length - ".meta".Length)
                    : entry.RepoPath + ".meta";
                if (byPath.TryGetValue(pairPath, out GitStatusEntry pair))
                {
                    AddOnce(pair);
                }
            }
            return result;

            void AddOnce(GitStatusEntry entry)
            {
                if (added.Add(entry.RepoPath))
                {
                    result.Add(entry);
                }
            }
        }

        /// <summary>
        /// <c>git add</c> 뒤의 status 를 미리 짐작한다(누르자마자 목록을 옮겨 보이려고). 진짜 status 가 오면 그것으로 바뀐다.
        /// 작업 트리 쪽 변경을 인덱스로 올린다: "??"→"A ", " M"→"M ", " D"→"D ", "AM"→"A ", "MD"→"D ". 새로 만든 뒤 지운 파일("AD")은 사라진다.
        /// 충돌은 stage 대상이 아니라 그대로 둔다.
        /// </summary>
        /// <param name="paths">올릴 저장소 경로. null 이면 전부(Stage All).</param>
        public static List<GitStatusEntry> PredictStage(IEnumerable<GitStatusEntry> entries, ICollection<string> paths)
        {
            List<GitStatusEntry> result = new();
            foreach (GitStatusEntry entry in entries)
            {
                if ((paths != null && paths.Contains(entry.RepoPath) == false) || entry.State == GitFileState.Conflicted || entry.WorkTreeCode == ' ')
                {
                    result.Add(entry);
                    continue;
                }
                char index;
                if (entry.IndexCode == '?')
                {
                    index = 'A';
                }
                else if (entry.WorkTreeCode == 'D')
                {
                    if (entry.IndexCode == 'A')
                    {
                        continue;
                    }
                    index = 'D';
                }
                else
                {
                    index = entry.IndexCode == ' ' ? 'M' : entry.IndexCode;
                }
                result.Add(Raw(entry, index, ' ', entry.OriginalRepoPath));
            }
            return result;
        }

        /// <summary>
        /// <c>git restore --staged</c> 뒤의 status 를 미리 짐작한다. 인덱스를 HEAD 로 되돌린다:
        /// "A "·"AM"→"??", "M "·"MM"→" M", "D "→" D", "MD"→" D". 새로 올린 뒤 지운 파일("AD")은 사라진다.
        /// 이름 변경·복사(R·C)는 새 경로·원래 경로 두 줄로 갈라져 맞히기 어려우므로 그대로 두고 진짜 status 를 기다린다.
        /// </summary>
        /// <param name="paths">내릴 저장소 경로. null 이면 전부(Unstage All).</param>
        public static List<GitStatusEntry> PredictUnstage(IEnumerable<GitStatusEntry> entries, ICollection<string> paths)
        {
            List<GitStatusEntry> result = new();
            foreach (GitStatusEntry entry in entries)
            {
                char x = entry.IndexCode;
                if ((paths != null && paths.Contains(entry.RepoPath) == false) || entry.State == GitFileState.Conflicted
                    || x == ' ' || x == '?' || x == '!' || x == 'R' || x == 'C')
                {
                    result.Add(entry);
                    continue;
                }
                if (x == 'A')
                {
                    if (entry.WorkTreeCode == 'D')
                    {
                        continue;
                    }
                    result.Add(Raw(entry, '?', '?', null));
                    continue;
                }
                char workTree = x == 'D' || entry.WorkTreeCode == 'D' ? 'D' : 'M';
                result.Add(Raw(entry, ' ', workTree, null));
            }
            return result;
        }

        /// <summary>
        /// status 원본 항목(양쪽 글자)을 새 글자로 만든다. 상태는 파서와 같은 규칙으로 다시 매긴다.
        /// </summary>
        private static GitStatusEntry Raw(GitStatusEntry entry, char index, char workTree, string original)
        {
            return new GitStatusEntry
            {
                RepoPath = entry.RepoPath,
                OriginalRepoPath = original,
                ProjectPath = entry.ProjectPath,
                IndexCode = index,
                WorkTreeCode = workTree,
                State = GitStatusParser.ToState(index, workTree),
            };
        }

        /// <summary>
        /// 별도 git 저장소가 든 폴더인지(패키지 폴더를 따로 clone 해 둔 경우 등). git status 는 그런 폴더를 들어가 보지 않고
        /// "?? 경로/" 한 줄로만 보이고, 그대로 git add 하면 파일이 아니라 그 저장소 커밋을 가리키는 링크(gitlink)가 올라간다.
        /// </summary>
        public static bool IsEmbeddedRepository(GitStatusEntry entry, string repoRoot)
        {
            if (entry.State != GitFileState.Untracked || entry.RepoPath.EndsWith("/", System.StringComparison.Ordinal) == false || string.IsNullOrEmpty(repoRoot))
            {
                return false;
            }
            // .git 은 폴더(일반 clone)이거나 파일(worktree·submodule) 이다.
            string gitPath = System.IO.Path.Combine(repoRoot, entry.RepoPath, ".git");
            return System.IO.Directory.Exists(gitPath) || System.IO.File.Exists(gitPath);
        }

        /// <summary>
        /// 한쪽 상태 글자(M·A·D·R·C·T)의 뜻.
        /// </summary>
        public static GitFileState StateOf(char code)
        {
            switch (code)
            {
                case 'A':
                    return GitFileState.Added;
                case 'D':
                    return GitFileState.Deleted;
                case 'R':
                case 'C':
                    return GitFileState.Renamed;
                case '?':
                    return GitFileState.Untracked;
                case 'U':
                    return GitFileState.Conflicted;
                default:
                    return GitFileState.Modified;
            }
        }

        /// <summary>
        /// 이름 변경은 인덱스에 기록된 것이라 Staged 쪽에만 원래 경로를 둔다. Unstaged 쪽은 새 경로의 수정일 뿐이다.
        /// </summary>
        private static GitStatusEntry Copy(GitStatusEntry entry, GitFileState state, bool keepOriginal)
        {
            return new GitStatusEntry
            {
                RepoPath = entry.RepoPath,
                OriginalRepoPath = keepOriginal ? entry.OriginalRepoPath : null,
                ProjectPath = entry.ProjectPath,
                State = state,
                IndexCode = entry.IndexCode,
                WorkTreeCode = entry.WorkTreeCode,
            };
        }
    }
}
