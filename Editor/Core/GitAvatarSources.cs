using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GitVersionControl
{
    /// <summary>
    /// 작성자 아바타를 어디서 받을지 정하는 규칙(네트워크 없는 순수 로직).
    /// 1) GitHub noreply 이메일이면 그 계정 아바타. 2) 원격이 GitHub 이면 커밋 API 의 author.avatar_url. 3) Gravatar.
    /// </summary>
    public static class GitAvatarSources
    {
        public const int Size = 64;

        // 12345678+login@users.noreply.github.com, 예전 형식 login@users.noreply.github.com
        private static readonly Regex NoReply = new(@"^(?:(\d+)\+)?([^@]+)@users\.noreply\.github\.com$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // git@github.com:owner/repo.git, ssh://git@github.com/owner/repo, https://github.com/owner/repo(.git)
        private static readonly Regex GitHubRemote = new(@"github\.com[:/]([^/\s]+)/([^/\s]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>noreply 이메일이면 그 계정의 아바타 주소. 아니면 null.</summary>
        public static string NoReplyAvatarUrl(string email)
        {
            Match match = NoReply.Match(email?.Trim() ?? string.Empty);
            if (match.Success == false)
            {
                return null;
            }
            return match.Groups[1].Success
                ? $"https://avatars.githubusercontent.com/u/{match.Groups[1].Value}?s={Size}"
                : $"https://github.com/{match.Groups[2].Value}.png?size={Size}";
        }

        public static bool TryParseGitHubRemote(string remoteUrl, out string owner, out string repository)
        {
            Match match = GitHubRemote.Match(remoteUrl?.Trim() ?? string.Empty);
            owner = match.Success ? match.Groups[1].Value : null;
            repository = match.Success ? match.Groups[2].Value : null;
            return match.Success;
        }

        public static string GitHubCommitApiUrl(string owner, string repository, string commitHash)
        {
            return $"https://api.github.com/repos/{owner}/{repository}/commits/{commitHash}";
        }

        // GitHub 아이디: 영문·숫자·하이픈, 39자까지.
        private static readonly Regex GitHubLogin = new(@"^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$", RegexOptions.Compiled);

        /// <summary>
        /// GitHub 계정의 프로필 사진 주소(로그인 없이 받을 수 있다). 아이디가 비었거나 형식이 틀리면 null.
        /// 앞의 '@' 는 떼어 준다("@kim" → kim).
        /// </summary>
        public static string GitHubProfileAvatarUrl(string login)
        {
            string trimmed = login?.Trim().TrimStart('@') ?? string.Empty;
            return GitHubLogin.IsMatch(trimmed) ? $"https://github.com/{trimmed}.png?size={Size}" : null;
        }

        /// <summary>
        /// 같은 이메일인지(앞뒤 공백·대소문자 무시). 둘 중 하나라도 비면 false.
        /// </summary>
        public static bool IsSameEmail(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
            {
                return false;
            }
            return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// <c>git credential fill</c> 에 넘길 입력: GitHub 의 https 자격 증명을 묻는다.
        /// </summary>
        public const string GitHubCredentialQuery = "protocol=https\nhost=github.com\n\n";

        /// <summary>
        /// <c>git credential fill</c> 출력("key=value" 줄들)에서 password(=토큰). 없으면 null.
        /// </summary>
        public static string ParseCredentialPassword(string output)
        {
            foreach (string rawLine in (output ?? string.Empty).Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.StartsWith("password=", StringComparison.Ordinal) && line.Length > "password=".Length)
                {
                    return line.Substring("password=".Length);
                }
            }
            return null;
        }

        /// <summary>d=404: 등록된 그림이 없으면 기본 그림 대신 404 를 받아 머리글자로 둔다.</summary>
        public static string GravatarUrl(string email)
        {
            return $"https://www.gravatar.com/avatar/{Md5Hex(email.Trim().ToLowerInvariant())}?s={Size}&d=404";
        }

        /// <summary>디스크 캐시 파일 이름. 이메일 자체를 파일 이름에 남기지 않는다.</summary>
        public static string CacheKey(string email)
        {
            using SHA1 sha1 = SHA1.Create();
            return Hex(sha1.ComputeHash(Encoding.UTF8.GetBytes(email.Trim().ToLowerInvariant())));
        }

        private static string Md5Hex(string text)
        {
            using MD5 md5 = MD5.Create();
            return Hex(md5.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        private static string Hex(byte[] bytes)
        {
            StringBuilder builder = new(bytes.Length * 2);
            foreach (byte b in bytes)
            {
                builder.Append(b.ToString("x2"));
            }
            return builder.ToString();
        }
    }
}
