using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// .gitignore 에 넣을 패턴 만들기. 저장소 루트의 .gitignore 기준이라 경로 패턴은 '/' 로 시작해 그 자리 하나만 가리킨다.
    /// </summary>
    public static class GitIgnorePatterns
    {
        /// <summary>
        /// 이 파일 하나. 예: "Assets/A.psd" → "/Assets/A.psd".
        /// </summary>
        public static string ForFile(string repoPath) => "/" + repoPath.TrimStart('/');

        /// <summary>
        /// 같은 확장자 전부. 확장자가 없으면 null. 예: "Assets/A.psd" → "*.psd".
        /// 점으로 시작하는 이름(".gitignore")은 이름 전체가 이름이지 확장자가 아니다.
        /// </summary>
        public static string ForExtension(string repoPath)
        {
            string name = Path.GetFileName(repoPath);
            int dot = name.LastIndexOf('.');
            return dot <= 0 ? null : "*" + name.Substring(dot);
        }

        /// <summary>
        /// 폴더 아래 전부. 예: "Assets/Art" → "/Assets/Art/".
        /// </summary>
        public static string ForFolder(string repoFolder) => "/" + repoFolder.Trim('/') + "/";

        /// <summary>
        /// 패턴을 .gitignore 내용 끝에 더한다. 이미 같은 줄이 있으면 그대로 돌려준다(두 번 눌러도 한 줄).
        /// 마지막 줄에 줄바꿈이 없으면 붙인 뒤 더한다.
        /// </summary>
        public static string Append(string existing, string pattern)
        {
            string text = existing ?? string.Empty;
            bool exists = text.Replace("\r\n", "\n").Split('\n').Any(line => line.Trim() == pattern);
            if (exists)
            {
                return text;
            }
            string newline = text.Contains("\r\n") ? "\r\n" : "\n";
            if (text.Length > 0 && text.EndsWith("\n") == false)
            {
                text += newline;
            }
            return text + pattern + newline;
        }
    }

    /// <summary>
    /// blame 한 줄: 그 줄을 마지막으로 바꾼 커밋과 내용.
    /// </summary>
    public sealed class GitBlameLine
    {
        public string Hash;
        public string AuthorName;
        public DateTime Date;
        public string Summary;
        public int LineNumber;
        public string Text;

        /// <summary>
        /// 아직 커밋하지 않은 줄(해시가 0 으로만 된 줄).
        /// </summary>
        public bool IsUncommitted => Hash.All(c => c == '0');

        public string ShortHash => Hash.Length > 8 ? Hash.Substring(0, 8) : Hash;
    }

    /// <summary>
    /// <c>git blame --line-porcelain</c> 해석. 줄마다 머리(해시·author·author-time·summary …)가 반복되고, 탭으로 시작하는 줄이 내용이다.
    /// </summary>
    public static class GitBlameParser
    {
        public static List<GitBlameLine> Parse(string output)
        {
            List<GitBlameLine> lines = new();
            GitBlameLine current = null;
            foreach (string raw in (output ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
            {
                if (raw.StartsWith("\t", StringComparison.Ordinal))
                {
                    if (current != null)
                    {
                        current.Text = raw.Substring(1);
                        lines.Add(current);
                        current = null;
                    }
                    continue;
                }
                if (current == null)
                {
                    current = TryStartRecord(raw);
                    continue;
                }
                int space = raw.IndexOf(' ');
                string key = space < 0 ? raw : raw.Substring(0, space);
                string value = space < 0 ? string.Empty : raw.Substring(space + 1);
                switch (key)
                {
                    case "author":
                        current.AuthorName = value;
                        break;
                    case "author-time":
                        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long seconds))
                        {
                            current.Date = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime;
                        }
                        break;
                    case "summary":
                        current.Summary = value;
                        break;
                }
            }
            return lines;
        }

        /// <summary>
        /// "해시 원래줄 지금줄 [묶음수]" 머리 줄. 아니면 null.
        /// </summary>
        private static GitBlameLine TryStartRecord(string line)
        {
            string[] parts = line.Split(' ');
            if (parts.Length < 3 || parts[0].Length < 40 || parts[0].All(Uri.IsHexDigit) == false)
            {
                return null;
            }
            int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int lineNumber);
            return new GitBlameLine { Hash = parts[0], LineNumber = lineNumber, AuthorName = string.Empty, Summary = string.Empty };
        }
    }

    /// <summary>
    /// 파일·폴더 우클릭 메뉴가 쓰는 git 명령: 일부만 stash, LFS 추적, 패치 만들기, 파일 히스토리·blame, .gitignore 에 더하기.
    /// </summary>
    public static class GitFileOperations
    {
        /// <summary>
        /// 고른 경로만 stash 에 넣는다(<c>git stash push -- 경로</c>). 새 파일도 같이 넣도록 <c>--include-untracked</c>.
        /// </summary>
        public static Task<GitResult> StashPathsAsync(IReadOnlyCollection<string> repoPaths, string message)
        {
            List<string> args = new() { "stash", "push", "--include-untracked", "-m", message, "--" };
            args.AddRange(repoPaths);
            return GitRepository.RunAsync(args.ToArray());
        }

        /// <summary>
        /// LFS 로 추적한다(.gitattributes 에 줄이 생긴다). git-lfs 가 없으면 git 이 실패를 돌려준다.
        /// </summary>
        public static Task<GitResult> LfsTrackAsync(string pattern)
        {
            return GitRepository.RunAsync("lfs", "track", pattern);
        }

        /// <summary>
        /// 저장소 루트 .gitignore 에 패턴을 더한다. 이미 추적 중인 파일은 무시 목록에 넣어도 계속 추적된다(git 규칙).
        /// </summary>
        public static GitResult AddToGitignore(string pattern)
        {
            string path = Path.Combine(GitRepository.RepoRoot, ".gitignore");
            try
            {
                string existing = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
                string updated = GitIgnorePatterns.Append(existing, pattern);
                if (updated != existing)
                {
                    File.WriteAllText(path, updated, new UTF8Encoding(false));
                }
                return new GitResult(0, string.Empty, string.Empty);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return new GitResult(1, string.Empty, e.Message);
            }
        }

        /// <summary>
        /// 고른 파일들의 변경을 패치(<c>git apply</c> 로 적용할 수 있는 형식) 글로 만든다. 바이너리도 담도록 <c>--binary</c>.
        /// Staged 는 인덱스 대비, Unstaged 는 인덱스 ↔ 작업 트리, WorkingTree 는 HEAD ↔ 작업 트리.
        /// untracked 파일은 git diff 에 안 나오므로 하나씩 빈 파일과 비교해 이어 붙인다.
        /// </summary>
        public static async Task<(string patch, string error)> CreatePatchAsync(IReadOnlyCollection<GitStatusEntry> entries, GitDiffSource source)
        {
            StringBuilder patch = new();
            List<string> tracked = new();
            foreach (GitStatusEntry entry in entries)
            {
                if (entry.State == GitFileState.Untracked)
                {
                    GitResult untracked = await GitRepository.RunAsync("diff", "--no-color", "--no-ext-diff", "--binary", "--no-index", "--", "/dev/null", entry.RepoPath);
                    // --no-index 는 차이가 있으면 1 로 끝난다.
                    if (untracked.ExitCode > 1)
                    {
                        return (null, untracked.Message);
                    }
                    patch.Append(untracked.Output);
                    continue;
                }
                tracked.Add(entry.RepoPath);
                if (entry.OriginalRepoPath != null)
                {
                    tracked.Add(entry.OriginalRepoPath);
                }
            }
            if (tracked.Count > 0)
            {
                List<string> args = new() { "diff", "--no-color", "--no-ext-diff", "--binary", "-M" };
                if (source == GitDiffSource.Staged)
                {
                    args.Add("--cached");
                }
                else if (source == GitDiffSource.WorkingTree)
                {
                    args.Add("HEAD");
                }
                args.Add("--");
                args.AddRange(tracked);
                GitResult result = await GitRepository.RunAsync(args.ToArray());
                if (result.Success == false)
                {
                    return (null, result.Message);
                }
                patch.Insert(0, result.Output);
            }
            return (patch.ToString(), null);
        }

        /// <summary>
        /// 파일을 바꾼 커밋들(이름 변경을 따라간다). 최신부터.
        /// </summary>
        public static Task<(List<GitCommitInfo> commits, string error)> FileLogAsync(string repoPath, int maxCount)
        {
            return GitHistory.FileLogAsync(repoPath, maxCount);
        }

        /// <summary>
        /// 커밋마다 그 시점의 파일 경로(이름 변경을 따라간 히스토리에서 옛 커밋의 diff 를 열 때 필요).
        /// </summary>
        public static async Task<Dictionary<string, string>> FollowPathsAsync(string repoPath, int maxCount)
        {
            GitResult result = await GitRepository.RunAsync("log", "--follow", "--format=%x1e%H", "--name-only", $"--max-count={maxCount}", "--", repoPath);
            return result.Success ? ParseFollowPaths(result.Output) : new Dictionary<string, string>();
        }

        /// <summary>
        /// <c>log --format=%x1e%H --name-only</c> 출력에서 해시 → 경로. 레코드 구분자(0x1E) 뒤 첫 줄이 해시, 마지막 줄이 그 커밋의 경로.
        /// </summary>
        public static Dictionary<string, string> ParseFollowPaths(string output)
        {
            Dictionary<string, string> paths = new();
            foreach (string record in (output ?? string.Empty).Split('\x1e'))
            {
                string[] lines = record.Replace("\r\n", "\n").Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray();
                if (lines.Length >= 2)
                {
                    paths[lines[0]] = lines[lines.Length - 1];
                }
            }
            return paths;
        }

        /// <summary>
        /// 작업 트리 파일의 blame. 아직 커밋하지 않은 줄도 나온다(해시가 0).
        /// </summary>
        public static async Task<(List<GitBlameLine> lines, string error)> BlameAsync(string repoPath)
        {
            GitResult result = await GitRepository.RunAsync("blame", "--line-porcelain", "--", repoPath);
            return result.Success ? (GitBlameParser.Parse(result.Output), null) : (new List<GitBlameLine>(), result.Message);
        }
    }
}
