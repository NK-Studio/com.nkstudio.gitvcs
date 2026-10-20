using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitVersionControl
{
    /// <summary>
    /// 원격 인증 실패를 알아보고, 이 PC 에 로그인된 GitHub 계정을 찾고, 저장소마다 쓸 계정을 정하는 규칙.
    ///
    /// 계정은 플랫폼마다 다른 곳에 있다: macOS 는 gh·osxkeychain, Windows 는 Git for Windows 에 들어 있는 Git Credential Manager(GCM).
    /// 그래서 계정 목록은 여러 곳(gh·GCM·패키지 로그인)에서 모으고, 토큰은 도우미와 상관없이 <c>git credential fill</c> 로 꺼낸다.
    /// 저장소에 쓸 계정: Git Credential Manager·osxkeychain 계정은 그 저장소의 <c>credential.https://github.com.username</c> 에 이름을 적는다(이 도우미들은 이름으로 계정을 고른다).
    /// gh 의 도우미는 이름을 무시하고 늘 활성 계정을 돌려주므로, gh 계정·패키지 로그인은 토큰을 직접 넘긴다(<see cref="TokenOverride"/>).
    /// </summary>
    public static class GitAuth
    {
        /// <summary>
        /// 이 저장소에서 GitHub 에 쓸 계정 이름을 적는 git 설정(<c>.git/config</c>).
        /// </summary>
        public const string UsernameConfigKey = "credential.https://github.com.username";

        /// <summary>
        /// 패키지 로그인(Preferences > Git)의 토큰을 git 에 넘기는 환경 변수. 명령줄에는 토큰을 넣지 않는다.
        /// </summary>
        public const string TokenEnvironmentVariable = "GITVCS_GITHUB_TOKEN";

        /// <summary>
        /// 원격이 접근을 거부할 때 git 이 남기는 글. GitHub 는 권한 없는 계정에게 비공개 저장소를 "없다(Repository not found)" 고 답한다.
        /// </summary>
        private static readonly string[] AuthFailurePatterns =
        {
            "repository not found",
            "authentication failed",
            "could not read username",
            "could not read password",
            "terminal prompts disabled",
            "invalid username or password",
            "returned error: 401",
            "returned error: 403",
            "permission to ",
            "the requested url returned error: 404",
        };

        // gh 2.40+ "Logged in to github.com account NAME", 그 전 "Logged in to github.com as NAME".
        private static readonly Regex GhAccountLine = new(@"Logged in to (?<host>\S+) (?:account|as) (?<name>[^\s(]+)", RegexOptions.Compiled);

        /// <summary>
        /// git 쓰기·네트워크 작업이 인증 문제로 실패했는지(계정이 틀렸거나 로그인이 없거나 만료).
        /// </summary>
        public static bool IsAuthFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            string lower = message.ToLowerInvariant();
            return AuthFailurePatterns.Any(lower.Contains);
        }

        /// <summary>
        /// <c>gh auth status</c> 글에서 github.com 에 로그인된 계정 이름들(나온 순서대로).
        /// </summary>
        public static List<string> ParseGhAccounts(string statusOutput)
        {
            List<string> names = new();
            foreach (Match match in GhAccountLine.Matches(statusOutput ?? string.Empty))
            {
                string name = match.Groups["name"].Value;
                if (string.Equals(match.Groups["host"].Value, "github.com", StringComparison.OrdinalIgnoreCase) && names.Contains(name) == false)
                {
                    names.Add(name);
                }
            }
            return names;
        }

        /// <summary>
        /// <c>git credential-manager github list</c> 글(한 줄에 계정 하나)에서 계정 이름들.
        /// </summary>
        public static List<string> ParseGcmAccounts(string listOutput)
        {
            return (listOutput ?? string.Empty)
                .Split('\n')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && line.Contains(' ') == false)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// <c>git credential fill</c> 출력에서 (사용자 이름, 비밀번호·토큰). 없으면 null. 토큰은 저장하지 말고 바로 쓰고 버린다.
        /// </summary>
        public static (string username, string password) ParseCredential(string fillOutput)
        {
            string username = null;
            string password = null;
            foreach (string raw in (fillOutput ?? string.Empty).Split('\n'))
            {
                string line = raw.TrimEnd('\r');
                if (line.StartsWith("username=", StringComparison.Ordinal))
                {
                    username = line.Substring("username=".Length);
                }
                else if (line.StartsWith("password=", StringComparison.Ordinal))
                {
                    password = line.Substring("password=".Length);
                }
            }
            return (username, password);
        }

        /// <summary>
        /// <c>git credential fill</c> 에 넣을 요청. <paramref name="username"/> 을 주면 그 계정의 자격 증명을 꺼내 달라고 한다.
        /// </summary>
        public static string CredentialRequest(string owner, string repository, string username)
        {
            string request = $"protocol=https\nhost=github.com\npath={owner}/{repository}.git\n";
            if (string.IsNullOrEmpty(username) == false)
            {
                request += $"username={username}\n";
            }
            return request + "\n";
        }

        /// <summary>
        /// 패키지 로그인 토큰을 쓸 때 git 앞에 붙이는 설정. 다른 도우미를 비우고, 환경 변수(<see cref="TokenEnvironmentVariable"/>)의 토큰을 돌려주는 도우미 하나만 둔다.
        /// github.com 의 https 원격에만 걸린다.
        /// </summary>
        public static IReadOnlyList<string> TokenHelperArguments()
        {
            return new[]
            {
                "-c", "credential.https://github.com.helper=",
                "-c", "credential.https://github.com.helper=!f() { test \"$1\" = get && echo username=x-access-token && echo \"password=$" + TokenEnvironmentVariable + "\"; }; f",
            };
        }

        /// <summary>
        /// 지금 PATH(<see cref="GitLocator.SearchPath"/>)와 흔한 설치 위치에서 실행 파일을 찾는다. Windows 는 .exe 를 붙여 본다. 없으면 null.
        /// </summary>
        public static string FindExecutable(string name, IEnumerable<string> extraDirectories = null)
        {
            bool windows = Path.DirectorySeparatorChar == '\\';
            string fileName = windows ? name + ".exe" : name;
            IEnumerable<string> directories = (GitLocator.SearchPath ?? string.Empty).Split(Path.PathSeparator);
            if (extraDirectories != null)
            {
                directories = directories.Concat(extraDirectories);
            }
            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }
                try
                {
                    string candidate = Path.Combine(directory.Trim(), fileName);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // PATH 에 잘못된 글자가 있는 항목은 건너뛴다.
                }
            }
            return null;
        }

        /// <summary>
        /// gh 의 흔한 설치 위치(PATH 에 없을 때). macOS Homebrew, Windows winget·설치 프로그램·scoop.
        /// </summary>
        public static IEnumerable<string> GhInstallDirectories()
        {
            yield return "/opt/homebrew/bin";
            yield return "/usr/local/bin";
            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            if (string.IsNullOrEmpty(programFiles) == false)
            {
                yield return Path.Combine(programFiles, "GitHub CLI");
            }
            string userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
            if (string.IsNullOrEmpty(userProfile) == false)
            {
                yield return Path.Combine(userProfile, "scoop", "shims");
            }
        }

        /// <summary>
        /// 이 저장소에 고른 gh 계정·패키지 로그인의 토큰. 그런 계정을 골랐을 때만 값이 있다. <see cref="GitCommand"/> 가 백그라운드 스레드에서 읽으므로
        /// 메인 스레드에서 EditorPrefs·설정을 읽어 여기에 옮겨 둔다(<c>GitAccounts.ApplyTokenOverride</c>).
        /// </summary>
        public static volatile string TokenOverride;
    }
}
