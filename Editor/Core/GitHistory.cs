using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace GitVersionControl
{
    public sealed class GitCommitInfo
    {
        public string Hash;
        public string ShortHash;
        public string[] Parents;
        public string AuthorName;
        public string AuthorEmail;
        public DateTime Date;

        /// <summary><c>%D</c> 그대로("HEAD -> main, origin/main, tag: v1.0"). 없으면 빈 문자열.</summary>
        public string Refs;
        public string Subject;

        public bool IsMerge => Parents.Length > 1;
    }

    public sealed class GitFileChange
    {
        /// <summary>A, M, D, R, C, T 중 하나.</summary>
        public char Status;
        public string RepoPath;
        public string OriginalRepoPath;
        public string ProjectPath;

        public GitFileState State
        {
            get
            {
                switch (Status)
                {
                    case 'A': return GitFileState.Added;
                    case 'D': return GitFileState.Deleted;
                    case 'R':
                    case 'C': return GitFileState.Renamed;
                    default: return GitFileState.Modified;
                }
            }
        }
    }

    public sealed class GitBranchRef
    {
        /// <summary>refs/heads/main, refs/remotes/origin/main</summary>
        public string RefName;

        /// <summary>main, origin/main</summary>
        public string Name;
        public bool IsRemote;
        public bool IsCurrent;
        public string ShortHash;

        /// <summary>전체 해시. 그래프에서 브랜치 끝 커밋을 찾을 때 쓴다.</summary>
        public string Hash;
        public string Upstream;

        /// <summary><c>%(upstream:track)</c> 그대로("[ahead 1, behind 2]", "[gone]"). 없으면 빈 문자열.</summary>
        public string Track;
        public DateTime Date;
        public string AuthorName;
        public string Subject;
    }

    /// <summary>
    /// 히스토리·브랜치 조회. 출력 구분자로 제어 문자(0x1E 레코드, 0x1F 필드)를 써서 커밋 메시지의 어떤 글자와도
    /// 섞이지 않게 한다.
    /// </summary>
    public static class GitHistory
    {
        private const char RecordSeparator = '\x1e';
        private const char FieldSeparator = '\x1f';
        private const string LogFormat = "--format=%H%x1f%h%x1f%P%x1f%an%x1f%ae%x1f%at%x1f%D%x1f%s%x1e";
        private const string BranchFormat = "--format=%(refname)%1f%(refname:short)%1f%(objectname:short)%1f%(upstream:short)%1f%(upstream:track)%1f%(committerdate:unix)%1f%(authorname)%1f%(contents:subject)%1f%(HEAD)%1f%(objectname)";

        /// <summary>
        /// <paramref name="revisionRange"/> 예: null(HEAD), "HEAD..@{u}". <paramref name="allRefs"/> 면 모든 브랜치.
        /// </summary>
        public static async Task<(List<GitCommitInfo> commits, string error)> LogAsync(string revisionRange, bool allRefs, int skip, int maxCount)
        {
            List<string> args = new() { "log", LogFormat, "--date-order", $"--max-count={maxCount}", $"--skip={skip}" };
            if (allRefs)
            {
                args.Add("--all");
            }
            if (string.IsNullOrEmpty(revisionRange) == false)
            {
                args.Add(revisionRange);
            }
            // 리비전과 경로가 같은 이름일 때 경로로 오해하지 않게 끝을 표시한다.
            args.Add("--");

            GitResult result = await GitRepository.RunAsync(args.ToArray());
            return result.Success ? (ParseCommits(result.Output), null) : (new List<GitCommitInfo>(), result.Message);
        }

        /// <summary>
        /// Branch Explorer 용: 모든 브랜치·태그에서 닿는 커밋. <paramref name="since"/> 이전 커밋은 빼서 그래프 크기를 제한한다.
        /// </summary>
        public static async Task<(List<GitCommitInfo> commits, string error)> GraphLogAsync(DateTime? since, int maxCount)
        {
            List<string> args = new() { "log", LogFormat, "--date-order", "--all", $"--max-count={maxCount}" };
            if (since.HasValue)
            {
                args.Add($"--since={since.Value:yyyy-MM-dd}");
            }
            args.Add("--");

            GitResult result = await GitRepository.RunAsync(args.ToArray());
            return result.Success ? (ParseCommits(result.Output), null) : (new List<GitCommitInfo>(), result.Message);
        }

        /// <summary>커밋이 부모(병합이면 첫 부모) 대비 바꾼 파일. 최초 커밋은 빈 트리 대비.</summary>
        public static async Task<List<GitFileChange>> CommitFilesAsync(string hash)
        {
            GitResult result = await GitRepository.RunAsync("diff-tree", "-r", "-z", "--no-commit-id", "--name-status", "-M", "--root", "--diff-merges=first-parent", hash);
            return result.Success ? ParseNameStatus(result.Output) : new List<GitFileChange>();
        }

        public static async Task<string> CommitMessageAsync(string hash)
        {
            GitResult result = await GitRepository.RunAsync("show", "-s", "--format=%B", hash);
            return result.Success ? result.Output.TrimEnd() : result.Message;
        }

        /// <summary>
        /// 두 리비전의 공통 조상 이후 <paramref name="to"/> 쪽에서 바뀐 파일(three-dot diff).
        /// Incoming 에서는 "원격이 바꾼 것" 만 보여야 하므로 two-dot 이 아니라 이것을 쓴다.
        /// </summary>
        public static async Task<List<GitFileChange>> ChangedSinceMergeBaseAsync(string from, string to)
        {
            GitResult result = await GitRepository.RunAsync("diff", "-z", "--name-status", "-M", $"{from}...{to}", "--");
            return result.Success ? ParseNameStatus(result.Output) : new List<GitFileChange>();
        }

        public static async Task<(List<GitBranchRef> branches, string error)> BranchesAsync()
        {
            GitResult result = await GitRepository.RunAsync("for-each-ref", BranchFormat, "--sort=-committerdate", "refs/heads", "refs/remotes");
            return result.Success ? (ParseBranches(result.Output), null) : (new List<GitBranchRef>(), result.Message);
        }

        /// <summary>
        /// 한 파일을 바꾼 커밋. <c>--follow</c> 로 이름이 바뀌기 전 히스토리까지 따라간다.
        /// </summary>
        public static async Task<(List<GitCommitInfo> commits, string error)> FileLogAsync(string repoPath, int maxCount)
        {
            GitResult result = await GitRepository.RunAsync("log", LogFormat, "--follow", $"--max-count={maxCount}", "--", repoPath);
            return result.Success ? (ParseCommits(result.Output), null) : (new List<GitCommitInfo>(), result.Message);
        }

        public static List<GitCommitInfo> ParseCommits(string output)
        {
            List<GitCommitInfo> commits = new();
            foreach (string rawRecord in output.Split(RecordSeparator))
            {
                string record = rawRecord.TrimStart('\r', '\n');
                if (record.Length == 0)
                {
                    continue;
                }
                string[] fields = record.Split(FieldSeparator);
                if (fields.Length < 8)
                {
                    continue;
                }
                commits.Add(new GitCommitInfo
                {
                    Hash = fields[0],
                    ShortHash = fields[1],
                    Parents = fields[2].Length == 0 ? Array.Empty<string>() : fields[2].Split(' '),
                    AuthorName = fields[3],
                    AuthorEmail = fields[4],
                    Date = FromUnix(fields[5]),
                    Refs = fields[6],
                    Subject = fields[7],
                });
            }
            return commits;
        }

        /// <summary>
        /// <c>--name-status -z</c>: "M\0경로\0", 이름 변경·복사는 "R100\0원래경로\0새경로\0".
        /// </summary>
        public static List<GitFileChange> ParseNameStatus(string output)
        {
            List<GitFileChange> changes = new();
            string[] tokens = output.Split('\0');
            int i = 0;
            while (i < tokens.Length)
            {
                string status = tokens[i++].Trim();
                if (status.Length == 0 || i >= tokens.Length)
                {
                    continue;
                }

                GitFileChange change = new() { Status = status[0] };
                if ((change.Status == 'R' || change.Status == 'C') && i + 1 < tokens.Length)
                {
                    change.OriginalRepoPath = tokens[i++];
                }
                change.RepoPath = tokens[i++];
                change.ProjectPath = GitStatusParser.ToProjectPath(change.RepoPath, GitRepository.ProjectPrefix);
                changes.Add(change);
            }
            return changes;
        }

        public static List<GitBranchRef> ParseBranches(string output)
        {
            List<GitBranchRef> branches = new();
            foreach (string rawLine in output.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                string[] fields = line.Split(FieldSeparator);
                if (fields.Length < 9)
                {
                    continue;
                }
                // refs/remotes/origin/HEAD 는 기본 브랜치를 가리키는 별칭이라 목록에서 뺀다.
                if (fields[0].EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    continue;
                }
                branches.Add(new GitBranchRef
                {
                    RefName = fields[0],
                    Name = fields[1],
                    IsRemote = fields[0].StartsWith("refs/remotes/", StringComparison.Ordinal),
                    ShortHash = fields[2],
                    Upstream = fields[3],
                    Track = fields[4],
                    Date = FromUnix(fields[5]),
                    AuthorName = fields[6],
                    Subject = fields[7],
                    IsCurrent = fields[8].Trim() == "*",
                    Hash = fields.Length > 9 ? fields[9].Trim() : null,
                });
            }
            return branches;
        }

        private static DateTime FromUnix(string seconds)
        {
            return long.TryParse(seconds, out long value)
                ? DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime
                : DateTime.MinValue;
        }
    }
}
