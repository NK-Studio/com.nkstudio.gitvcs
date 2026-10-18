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
