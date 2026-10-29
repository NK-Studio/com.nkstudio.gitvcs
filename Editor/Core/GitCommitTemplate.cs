using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// git 의 커밋 템플릿(<c>git config commit.template</c>, 예: .gitmessage.txt). 터미널에서 <c>git commit</c> 을 하면 편집기에 뜨는
    /// 그 안내문을 커밋 입력란에 미리 채우고(Fork 와 같다), 커밋할 때는 git 처럼 '#' 로 시작하는 줄을 빼고 남긴다.
    /// </summary>
    public static class GitCommitTemplate
    {
        public const char CommentChar = '#';

        /// <summary>
        /// 템플릿 파일 내용. 설정이 없거나 파일을 못 읽으면 null. 상대 경로는 저장소 루트 기준이다(git 이 루트에서 돌 때와 같다).
        /// </summary>
        public static async Task<string> ReadAsync()
        {
            GitResult result = await GitRepository.RunAsync("config", "--path", "--get", "commit.template");
            string path = result.Success ? result.Output.Trim() : string.Empty;
            if (path.Length == 0)
            {
                return null;
            }
            string fullPath = ResolvePath(path, GitRepository.RepoRoot ?? GitRepository.ProjectRoot);
            try
            {
                return File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static string ResolvePath(string path, string repoRoot)
        {
            return IsAbsolute(path) || string.IsNullOrEmpty(repoRoot) ? path : Path.Combine(repoRoot, path);
        }

        /// <summary>
        /// 절대 경로인가. "C:/…" 처럼 드라이브 문자로 시작하는 경로는 macOS·Linux 의 <see cref="Path.IsPathRooted"/> 가 상대 경로로 보므로 따로 본다
        /// (Windows 에서 만든 설정을 함께 쓰는 저장소).
        /// </summary>
        private static bool IsAbsolute(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }
            bool drive = path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '/' || path[2] == '\\');
            return drive || Path.IsPathRooted(path);
        }

        /// <summary>
        /// 템플릿을 제목·설명 칸으로 나눈다(Fork 와 같다). 첫 줄은 주석이어도 제목 칸, 나머지는 설명 칸.
        /// 예: "# 제목\n#\n# 안내" → ("# 제목", "#\n# 안내"), "Feat: \n\n# 안내" → ("Feat:", "# 안내").
        /// </summary>
        public static (string subject, string description) Split(string template)
        {
            string normalized = Normalize(template).Trim('\n').TrimEnd();
            int newline = normalized.IndexOf('\n');
            if (newline < 0)
            {
                return (normalized.Trim(), string.Empty);
            }
            return (normalized.Substring(0, newline).Trim(), normalized.Substring(newline + 1).TrimStart('\n'));
        }

        /// <summary>
        /// git 의 <c>--cleanup=strip</c> 과 같은 정리: 주석 줄을 빼고, 줄 끝 공백을 지우고, 이어진 빈 줄은 하나로, 앞뒤 빈 줄은 없앤다.
        /// 남는 글이 없으면 빈 문자열(= 커밋할 메시지가 없다).
        /// </summary>
        public static string StripComments(string message)
        {
            List<string> lines = new();
            bool pendingBlank = false;
            foreach (string raw in Normalize(message).Split('\n'))
            {
                if (IsComment(raw))
                {
                    continue;
                }
                string line = raw.TrimEnd();
                if (line.Length == 0)
                {
                    pendingBlank = lines.Count > 0;
                    continue;
                }
                if (pendingBlank)
                {
                    lines.Add(string.Empty);
                    pendingBlank = false;
                }
                lines.Add(line);
            }
            return string.Join("\n", lines);
        }

        private static bool IsComment(string line)
        {
            return line.Length > 0 && line[0] == CommentChar;
        }

        private static string Normalize(string text)
        {
            return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        }
    }
}
