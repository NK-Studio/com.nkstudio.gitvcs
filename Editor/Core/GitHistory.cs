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

        /// <summary>
        /// 브랜치 목록 형식. <c>%(refname:short)</c>·<c>%(upstream…)</c> 은 일부러 뺐다 — 브랜치 3000개 저장소에서
        /// <c>:short</c> 는 ref 마다 "가장 짧은 모호하지 않은 이름"을 찾느라 전체 ~1.4초, <c>upstream</c> 은 설정이 없어도
        /// ref 마다 찾느라 ~0.4초 걸린다(2026-10-18 실측, 이 형식만으로는 ~0.1초). 짧은 이름은 접두를 떼어 만들고,
        /// 추적 정보는 upstream 이 설정된 브랜치만 따로 묻는다(<see cref="TrackingFormat"/>).
        /// </summary>
        private const string BranchFormat = "--format=%(refname)%1f%(objectname)%1f%(committerdate:unix)%1f%(authorname)%1f%(contents:subject)%1f%(HEAD)";
        private const string TrackingFormat = "--format=%(refname)%1f%(upstream:short)%1f%(upstream:track)";
        private const string LocalBranchPrefix = "refs/heads/";
        private const string RemoteBranchPrefix = "refs/remotes/";

        /// <summary>
        /// 추적 정보를 물을 때 한 명령에 넣는 ref 수. Windows 명령줄 길이(32K) 안에 넉넉히 들어가는 값.
        /// </summary>
        private const int TrackingBatchSize = 100;
        private const int ShortHashLength = 7;

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
            return result.Success ? GitDisplay.Nfc(result.Output.TrimEnd()) : result.Message;
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

        /// <summary>
        /// 두 커밋 사이에서 바뀐 파일(two-dot: <paramref name="from"/> 의 내용 ↔ <paramref name="to"/> 의 내용).
        /// </summary>
        /// <param name="to">null 이면 작업 트리.</param>
        public static async Task<(List<GitFileChange> files, string error)> ChangedBetweenAsync(string from, string to)
        {
            GitResult result = to == null
                ? await GitRepository.RunAsync("diff", "-z", "--name-status", "-M", from, "--")
                : await GitRepository.RunAsync("diff", "-z", "--name-status", "-M", from, to, "--");
            return result.Success ? (ParseNameStatus(result.Output), null) : (new List<GitFileChange>(), result.Message);
        }

        public static async Task<(List<GitBranchRef> branches, string error)> BranchesAsync()
        {
            Task<GitResult> refsTask = GitRepository.RunAsync("for-each-ref", BranchFormat, "--sort=-committerdate", "refs/heads", "refs/remotes");
            Task<GitResult> configTask = GitRepository.RunAsync("config", "-z", "--get-regexp", @"^branch\..*\.merge$");
            GitResult refs = await refsTask;
            GitResult config = await configTask;
            if (refs.Success == false)
            {
                return (new List<GitBranchRef>(), refs.Message);
            }

            List<GitBranchRef> branches = ParseBranches(refs.Output);
            // 설정이 하나도 없으면 git config 는 종료 코드 1 로 끝난다 — 추적 브랜치가 없다는 뜻.
            List<string> trackedRefs = config.Success ? TrackedBranchRefs(config.Output) : new List<string>();
            for (int start = 0; start < trackedRefs.Count; start += TrackingBatchSize)
            {
                List<string> args = new() { "for-each-ref", TrackingFormat };
                args.AddRange(trackedRefs.GetRange(start, Math.Min(TrackingBatchSize, trackedRefs.Count - start)));
                GitResult tracking = await GitRepository.RunAsync(args.ToArray());
                if (tracking.Success)
                {
                    ApplyTracking(branches, tracking.Output);
                }
            }
            return (branches, null);
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
                    AuthorName = GitDisplay.Nfc(fields[3]),
                    AuthorEmail = fields[4],
                    Date = FromUnix(fields[5]),
                    Refs = fields[6],
                    Subject = GitDisplay.Nfc(fields[7]),
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

        /// <summary>
        /// <see cref="BranchFormat"/> 출력. 추적 정보(<see cref="GitBranchRef.Upstream"/>·<see cref="GitBranchRef.Track"/>)는
        /// 빈 문자열로 두고 <see cref="ApplyTracking"/> 이 채운다.
        /// </summary>
        public static List<GitBranchRef> ParseBranches(string output)
        {
            List<GitBranchRef> branches = new();
            foreach (string rawLine in output.Split('\n'))
            {
                string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
                if (fields.Length < 6)
                {
                    continue;
                }
                string refName = fields[0];
                // refs/remotes/origin/HEAD 는 기본 브랜치를 가리키는 별칭이라 목록에서 뺀다.
                if (refName.EndsWith("/HEAD", StringComparison.Ordinal))
                {
                    continue;
                }
                string hash = fields[1];
                branches.Add(new GitBranchRef
                {
                    RefName = refName,
                    Name = ShortRefName(refName),
                    IsRemote = refName.StartsWith(RemoteBranchPrefix, StringComparison.Ordinal),
                    Hash = hash,
                    ShortHash = hash.Length > ShortHashLength ? hash.Substring(0, ShortHashLength) : hash,
                    Upstream = string.Empty,
                    Track = string.Empty,
                    Date = FromUnix(fields[2]),
                    AuthorName = GitDisplay.Nfc(fields[3]),
                    Subject = GitDisplay.Nfc(fields[4]),
                    IsCurrent = fields[5].Trim() == "*",
                });
            }
            return branches;
        }

        /// <summary>
        /// refs/heads/main → main, refs/remotes/origin/main → origin/main. 그 밖의 ref 는 그대로.
        /// </summary>
        public static string ShortRefName(string refName)
        {
            if (refName.StartsWith(LocalBranchPrefix, StringComparison.Ordinal))
            {
                return refName.Substring(LocalBranchPrefix.Length);
            }
            if (refName.StartsWith(RemoteBranchPrefix, StringComparison.Ordinal))
            {
                return refName.Substring(RemoteBranchPrefix.Length);
            }
            return refName;
        }

        /// <summary>
        /// <c>git config -z --get-regexp ^branch\..*\.merge$</c> 출력("branch.{이름}.merge\n{값}\0")에서 upstream 이 설정된 로컬 브랜치 ref.
        /// 브랜치 이름에 점이 있어도 되도록 앞의 "branch." 과 뒤의 ".merge" 만 뗀다.
        /// </summary>
        public static List<string> TrackedBranchRefs(string configOutput)
        {
            const string KeyPrefix = "branch.";
            const string KeySuffix = ".merge";
            List<string> refs = new();
            foreach (string entry in configOutput.Split('\0'))
            {
                int newline = entry.IndexOf('\n');
                string key = newline < 0 ? entry : entry.Substring(0, newline);
                bool isMergeKey = key.Length > KeyPrefix.Length + KeySuffix.Length
                    && key.StartsWith(KeyPrefix, StringComparison.Ordinal)
                    && key.EndsWith(KeySuffix, StringComparison.Ordinal);
                if (isMergeKey == false)
                {
                    continue;
                }
                refs.Add(LocalBranchPrefix + key.Substring(KeyPrefix.Length, key.Length - KeyPrefix.Length - KeySuffix.Length));
            }
            return refs;
        }

        /// <summary>
        /// <see cref="TrackingFormat"/> 출력으로 브랜치의 upstream·ahead/behind 를 채운다. 목록에 없는 ref 는 무시한다
        /// (ref 패턴은 접두 일치라 refs/heads/a 를 물으면 refs/heads/a/b 도 함께 온다).
        /// </summary>
        public static void ApplyTracking(List<GitBranchRef> branches, string trackingOutput)
        {
            Dictionary<string, GitBranchRef> branchDict = new(StringComparer.Ordinal);
            foreach (GitBranchRef branch in branches)
            {
                branchDict[branch.RefName] = branch;
            }
            foreach (string rawLine in trackingOutput.Split('\n'))
            {
                string[] fields = rawLine.TrimEnd('\r').Split(FieldSeparator);
                if (fields.Length < 3 || branchDict.TryGetValue(fields[0], out GitBranchRef branch) == false)
                {
                    continue;
                }
                branch.Upstream = fields[1];
                branch.Track = fields[2];
            }
        }

        private static DateTime FromUnix(string seconds)
        {
            return long.TryParse(seconds, out long value)
                ? DateTimeOffset.FromUnixTimeSeconds(value).LocalDateTime
                : DateTime.MinValue;
        }
    }
}
