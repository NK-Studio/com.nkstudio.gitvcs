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
