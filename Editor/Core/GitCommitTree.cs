using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋을 만든 사람(작성자와 다를 수 있다 — GitHub 에서 병합하면 커미터는 GitHub).
    /// </summary>
    public sealed class GitCommitPerson
    {
        public string Name;
        public string Email;
        public DateTime Date;
    }

    /// <summary>
    /// 커밋 시점의 파일 내용 한 개. 너무 크거나 이진 파일이면 글을 담지 않는다.
    /// </summary>
    public sealed class GitFileContent
    {
        public string Text;
        public bool IsBinary;

        /// <summary>
        /// <see cref="GitCommitTree.MaxPreviewChars"/> 를 넘어 앞부분만 담았다.
        /// </summary>
        public bool IsTruncated;

        public string Error;
    }

    /// <summary>
    /// Fork 형태 브랜치 탐색기의 아래 칸(Commit · File Tree)이 읽는 것: 커미터, 커밋 시점의 전체 파일 목록, 파일 내용.
    /// 출력 해석은 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitCommitTree
    {
        private const char FieldSeparator = '\x1f';

        /// <summary>
        /// 파일 보기에 담는 최대 글자 수. 이보다 크면 앞부분만 보인다(큰 씬·로그 파일이 창을 멈추지 않게).
        /// </summary>
        public const int MaxPreviewChars = 1_000_000;

        /// <summary>
        /// 이진 파일인지 볼 앞부분 길이(git 과 같은 8000 바이트 어림).
        /// </summary>
        private const int BinaryProbeChars = 8000;

        public static async Task<GitCommitPerson> CommitterAsync(string hash)
        {
            GitResult result = await GitRepository.RunAsync("show", "-s", "--format=%cn%x1f%ce%x1f%ct", hash);
            return result.Success ? ParseCommitter(result.Output) : null;
        }

        /// <summary>
        /// <c>%cn %ce %ct</c>(0x1F 로 나눔). 모양이 다르면 null.
        /// </summary>
        public static GitCommitPerson ParseCommitter(string output)
        {
            string[] fields = (output ?? string.Empty).Trim('\n', '\r').Split(FieldSeparator);
            if (fields.Length < 3 || long.TryParse(fields[2].Trim(), out long seconds) == false)
            {
                return null;
            }
            return new GitCommitPerson
            {
                Name = GitDisplay.Nfc(fields[0]),
                Email = fields[1],
                Date = DateTimeOffset.FromUnixTimeSeconds(seconds).LocalDateTime,
            };
        }

        /// <summary>
        /// 커밋 시점에 들어 있던 모든 파일 경로(저장소 기준). 서브모듈은 gitlink 라 빠진다.
        /// </summary>
        public static async Task<(List<string> paths, string error)> ListFilesAsync(string hash)
        {
            GitResult result = await GitRepository.RunAsync("ls-tree", "-r", "-z", "--name-only", hash);
            return result.Success ? (ParseTreePaths(result.Output), null) : (new List<string>(), result.Message);
        }

        /// <summary>
        /// <c>ls-tree -z --name-only</c> 출력(NUL 로 나눈 경로). 빈 칸은 뺀다.
        /// </summary>
        public static List<string> ParseTreePaths(string output)
        {
            return (output ?? string.Empty).Split('\0').Where(path => path.Length > 0).Select(GitDisplay.Nfc).ToList();
        }

        public static async Task<GitFileContent> FileContentAsync(string hash, string repoPath)
        {
            GitResult result = await GitRepository.RunAsync("show", $"{hash}:{repoPath}");
            return result.Success ? ToContent(result.Output) : new GitFileContent { Error = result.Message };
        }

        /// <summary>
        /// 앞부분에 NUL 이 있으면 이진 파일로 보고 글을 담지 않는다. 너무 길면 <see cref="MaxPreviewChars"/> 까지만.
        /// </summary>
        public static GitFileContent ToContent(string text)
        {
            text ??= string.Empty;
            if (text.IndexOf('\0', 0, Math.Min(text.Length, BinaryProbeChars)) >= 0)
            {
                return new GitFileContent { IsBinary = true };
            }
            bool truncated = text.Length > MaxPreviewChars;
            return new GitFileContent { Text = truncated ? text.Substring(0, MaxPreviewChars) : text, IsTruncated = truncated };
        }

        /// <summary>
        /// 파일 하나를 <paramref name="revision"/> 시점 내용으로 되돌린다(인덱스와 작업 폴더 모두 — Fork 의 Reset File to).
        /// 그 시점에 없던 파일이면(커밋에서 새로 만든 파일을 "커밋 이전" 으로) 지운다.
        /// </summary>
        public static async Task<GitResult> ResetFileToAsync(string revision, string repoPath)
        {
            GitResult exists = await GitRepository.RunAsync("cat-file", "-e", $"{revision}:{repoPath}");
            return exists.Success
                ? await GitRepository.RunAsync("restore", $"--source={revision}", "--staged", "--worktree", "--", repoPath)
                : await GitRepository.RunAsync("rm", "-f", "-q", "--ignore-unmatch", "--", repoPath);
        }

        /// <summary>
        /// 줄 단위로 나눈다(\r\n·\n). 끝의 줄바꿈 하나는 빈 줄로 세지 않는다.
        /// </summary>
        public static string[] SplitLines(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return Array.Empty<string>();
            }
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            return lines.Length > 1 && lines[^1].Length == 0 ? lines[..^1] : lines;
        }
    }
}
