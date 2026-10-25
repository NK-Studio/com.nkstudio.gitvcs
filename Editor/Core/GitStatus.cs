using System;
using System.Collections.Generic;

namespace GitVersionControl
{
    public enum GitFileState
    {
        Modified,
        Added,
        Deleted,
        Renamed,
        Untracked,
        Conflicted,
    }

    public sealed class GitStatusEntry
    {
        /// <summary>저장소 루트 기준 경로(슬래시 구분). git 명령에 그대로 넘긴다.</summary>
        public string RepoPath;

        /// <summary>이름 변경·복사의 원래 경로. 아니면 null.</summary>
        public string OriginalRepoPath;

        /// <summary>Unity 프로젝트 기준 경로(Assets/…, Packages/…). 프로젝트 밖 파일이면 null.</summary>
        public string ProjectPath;

        public GitFileState State;
        public char IndexCode;
        public char WorkTreeCode;
    }

    public sealed class GitBranchInfo
    {
        public string Name;
        public string Upstream;
        public int Ahead;
        public int Behind;
        public bool HasCommits = true;
        public bool Detached;
    }

    /// <summary>
    /// <c>git status --porcelain=v1 -z --branch</c> 출력 파서.
    ///
    /// -z 를 쓰는 이유: 비 ASCII(한글) 경로가 따옴표·8진 이스케이프 없이 그대로 나오고, 공백이 든 경로도 안전하다.
    /// -z 에서 이름 변경 레코드는 "XY 새경로\0원래경로\0" 순서다.
    /// </summary>
    public static class GitStatusParser
    {
        /// <param name="repoInProject">저장소가 Unity 프로젝트 안의 하위 폴더일 때(서브모듈) 그 경로("Packages/x/"). 프로젝트 밖이면 null.</param>
        public static List<GitStatusEntry> Parse(string output, string projectPrefix, out GitBranchInfo branch, string repoInProject = "")
        {
            branch = new GitBranchInfo();
            List<GitStatusEntry> entries = new();
            string[] records = output.Split('\0');

            for (int i = 0; i < records.Length; i++)
            {
                string record = records[i];
                if (record.Length == 0)
                {
                    continue;
                }
                if (record.StartsWith("## "))
                {
                    branch = ParseBranch(record.Substring(3));
                    continue;
                }
                if (record.Length < 4)
                {
                    continue;
                }

                char x = record[0];
                char y = record[1];
                GitStatusEntry entry = new()
                {
                    IndexCode = x,
                    WorkTreeCode = y,
                    RepoPath = record.Substring(3),
                    State = ToState(x, y),
                };
                if ((x == 'R' || x == 'C') && i + 1 < records.Length)
                {
                    entry.OriginalRepoPath = records[++i];
                }
                entry.ProjectPath = ToProjectPath(entry.RepoPath, projectPrefix, repoInProject);
                entries.Add(entry);
            }
            return entries;
        }

        public static GitFileState ToState(char x, char y)
        {
            if (x == '?' && y == '?')
            {
                return GitFileState.Untracked;
            }
            // 병합 충돌: DD, AU, UD, UA, DU, AA, UU
            if (x == 'U' || y == 'U' || (x == 'A' && y == 'A') || (x == 'D' && y == 'D'))
            {
                return GitFileState.Conflicted;
            }
            if (x == 'D' || y == 'D')
            {
                return GitFileState.Deleted;
            }
            if (x == 'R' || x == 'C')
            {
                return GitFileState.Renamed;
            }
            if (x == 'A')
            {
                return GitFileState.Added;
            }
            return GitFileState.Modified;
        }

        /// <summary>
        /// 저장소 루트와 Unity 프로젝트 루트가 다를 수 있다(모노레포의 하위 폴더 등).
        /// <paramref name="projectPrefix"/> 는 <c>git rev-parse --show-prefix</c> 결과("" 또는 "client/unity/").
        /// </summary>
        /// <param name="repoInProject">
        /// 저장소가 Unity 프로젝트 안의 하위 폴더일 때(서브모듈을 열었을 때) 프로젝트에서 저장소까지의 경로("Packages/x/"). 그 앞에 붙인다.
        /// 저장소가 프로젝트 밖이면 null — 프로젝트 경로가 없다.
        /// </param>
        public static string ToProjectPath(string repoPath, string projectPrefix, string repoInProject = "")
        {
            if (repoInProject == null)
            {
                return null;
            }
            if (string.IsNullOrEmpty(projectPrefix))
            {
                return repoInProject + repoPath;
            }
            if (repoPath.StartsWith(projectPrefix, StringComparison.Ordinal))
            {
                return repoInProject + repoPath.Substring(projectPrefix.Length);
            }
            return null;
        }

        private static GitBranchInfo ParseBranch(string header)
        {
            GitBranchInfo info = new();

            const string noCommits = "No commits yet on ";
            const string initialCommit = "Initial commit on ";
            if (header.StartsWith(noCommits) || header.StartsWith(initialCommit))
            {
                info.HasCommits = false;
                info.Name = header.Substring(noCommits.Length);
                return info;
            }
            if (header.StartsWith("HEAD (no branch)"))
            {
                info.Detached = true;
                info.Name = "HEAD";
                return info;
            }

            // "main...origin/main [ahead 1, behind 2]"
            string tracking = null;
            int bracket = header.IndexOf(" [", StringComparison.Ordinal);
            if (bracket >= 0)
            {
                tracking = header.Substring(bracket + 2).TrimEnd(']');
                header = header.Substring(0, bracket);
            }
            int dots = header.IndexOf("...", StringComparison.Ordinal);
            if (dots >= 0)
            {
                info.Name = header.Substring(0, dots);
                info.Upstream = header.Substring(dots + 3);
            }
            else
            {
                info.Name = header;
            }

            if (tracking != null)
            {
                foreach (string part in tracking.Split(','))
                {
                    string trimmed = part.Trim();
                    if (trimmed.StartsWith("ahead ") && int.TryParse(trimmed.Substring(6), out int ahead))
                    {
                        info.Ahead = ahead;
                    }
                    else if (trimmed.StartsWith("behind ") && int.TryParse(trimmed.Substring(7), out int behind))
                    {
                        info.Behind = behind;
                    }
                }
            }
            return info;
        }
    }
}
