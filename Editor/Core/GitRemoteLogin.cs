using System;
using System.Collections.Specialized;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;

namespace GitVersionControl
{
    /// <summary>어느 방식으로 로그인할 원격인가.</summary>
    public enum RemoteLoginKind
    {
        /// <summary>SSH 계정 비밀번호(회사 NAS·사내 서버). <c>SSH_ASKPASS</c> 로 넘긴다.</summary>
        Ssh,

        /// <summary>https 사용자 이름·비밀번호(또는 토큰)(GitLab·Gitea 등 GitHub 가 아닌 서버). 자격 증명 저장소에 넣는다.</summary>
        Https,
    }

    /// <summary>로그인할 원격. <see cref="Host"/> 가 null 이면 오류 글에 서버가 없어 원격 주소에서 찾아야 한다.</summary>
    public readonly struct RemoteLoginTarget
    {
        public readonly RemoteLoginKind Kind;

        /// <summary>서버 이름. https 는 포트까지(<c>git.company.com:8443</c>) — 자격 증명 저장소가 그렇게 나눈다.</summary>
        public readonly string Host;

        /// <summary>계정 이름(주소·오류 글에 있으면). SSH 는 원격 주소가 정해서 바꿀 수 없다.</summary>
        public readonly string User;

        public RemoteLoginTarget(RemoteLoginKind kind, string host, string user)
        {
            Kind = kind;
            Host = host;
            User = user;
        }
    }

    /// <summary>
    /// GitHub 가 아닌 서버(회사 NAS·사내 GitLab 등)에 로그인하게 한다.
    ///
    /// 패키지는 터미널이 없어 git 의 비밀번호 질문을 꺼 둔다(<c>GIT_TERMINAL_PROMPT=0</c>). 그래서 등록된 계정의 비밀번호로만 받는 서버는
    /// "Permission denied (publickey,password)"·"could not read Username" 으로 바로 실패한다. 실패하면 Git 창이 아이디·비밀번호를 받고
    /// (<c>GitLoginDialog</c>) 그 작업을 다시 한다.
    /// <list type="bullet">
    /// <item>SSH: ssh 는 git 자격 증명 저장소를 쓰지 않는다. 대신 비밀번호를 물을 때 부르는 프로그램(<c>SSH_ASKPASS</c>)에 패키지 스크립트를 두고,
    /// 비밀번호는 환경 변수로만 넘긴다(명령줄·파일에 남기지 않는다). 에디터를 끌 때까지 기억한다(<see cref="SessionState"/>).</item>
    /// <item>https: <c>git credential approve</c> 로 이 PC 의 자격 증명 저장소(키체인·자격 증명 관리자)에 넣는다. 다음부터 git 이 스스로 꺼낸다.</item>
    /// </list>
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitRemoteLogin
    {
        private const string SessionKey = "GitVersionControl.SshLogin";
        private const string HostVariable = "GITVCS_SSH_HOST";
        private const string PasswordVariable = "GITVCS_SSH_PASSWORD";

        // "tester@nas.local: Permission denied (publickey,password)."
        private static readonly Regex SshDenied = new(@"(?<user>[^\s@']+)@(?<host>[^\s:']+): Permission denied \((?<methods>[^)]*)\)", RegexOptions.Compiled);
        // "could not read Username for 'https://git.company.com': terminal prompts disabled", "Authentication failed for 'https://…/x.git/'"
        private static readonly Regex HttpsDenied = new(@"(?:could not read username for|authentication failed for) '(?<url>https?://[^']+)'", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // ssh://user@host:port/path
        private static readonly Regex SshUrl = new(@"^ssh://(?:(?<user>[^@/]+)@)?(?<host>\[[^\]]+\]|[^:/]+)(?::\d+)?(?:/|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // https://user:pw@host:port/path
        private static readonly Regex HttpsUrl = new(@"^https?://(?:(?<user>[^@/:]+)(?::[^@/]*)?@)?(?<host>[^/]+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        // user@host:path (scp 식). 드라이브 문자(C:\…)는 아니다.
        private static readonly Regex ScpUrl = new(@"^(?:(?<user>[^@/]+)@)?(?<host>[^@/:]{2,}):(?!//)", RegexOptions.Compiled);

        private sealed class SshLogin
        {
            public string Host;
            public string Password;
        }

        /// <summary>지금 기억하는 SSH 로그인. <see cref="GitCommand"/> 가 백그라운드 스레드에서 읽는다 — 메인 스레드에서만 바꾼다.</summary>
        private static volatile SshLogin s_ssh;

        private static string s_askPassPath;

        [InitializeOnLoadMethod]
        private static void Restore()
        {
            // 스크립트를 다시 컴파일해도(도메인 다시 읽기) 에디터를 끄기 전까지는 다시 묻지 않는다.
            string saved = SessionState.GetString(SessionKey, string.Empty);
            int split = saved.IndexOf('\n');
            if (split > 0)
            {
                s_ssh = new SshLogin { Host = saved.Substring(0, split), Password = saved.Substring(split + 1) };
            }
        }

        /// <summary>이 SSH 서버의 비밀번호를 이번 에디터 세션 동안 기억했는가.</summary>
        public static bool HasSshLogin(string host)
        {
            SshLogin login = s_ssh;
            return login != null && string.Equals(login.Host, host, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// git 오류 글이 로그인으로 풀 수 있는 실패인가. SSH 는 서버가 비밀번호를 받을 때만(키만 받으면 키를 등록해야 한다),
        /// https 는 GitHub 가 아닐 때만(GitHub 는 계정 고르기 <c>GitAccountDialog</c> 가 맡는다). 순수 함수라 테스트로 확인한다.
        /// </summary>
        /// <returns>로그인할 원격. 로그인으로 풀 수 없으면 null. <see cref="RemoteLoginTarget.Host"/> 가 null 이면 원격 주소에서 서버를 찾는다.</returns>
        public static RemoteLoginTarget? ParseFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return null;
            }
            string lower = message.ToLowerInvariant();
            // 알던 서버의 키가 바뀌었다 — 다른 서버로 이어졌을 수 있어 로그인을 권하지 않는다.
            if (lower.Contains("remote host identification has changed"))
            {
                return null;
            }
            Match ssh = SshDenied.Match(message);
            if (ssh.Success)
            {
                string methods = ssh.Groups["methods"].Value;
                return methods.Contains("password") || methods.Contains("keyboard-interactive")
                    ? new RemoteLoginTarget(RemoteLoginKind.Ssh, ssh.Groups["host"].Value, ssh.Groups["user"].Value)
                    : null;
            }
            // 처음 접속하는 SSH 서버: 터미널이 없어 "믿을까요?" 를 묻지 못했다. 로그인하면서 신뢰 목록에 넣는다.
            if (lower.Contains("host key verification failed"))
            {
                return new RemoteLoginTarget(RemoteLoginKind.Ssh, null, null);
            }
            Match https = HttpsDenied.Match(message);
            if (https.Success)
            {
                RemoteLoginTarget? target = ParseUrl(https.Groups["url"].Value);
                return target.HasValue && IsGitHub(target.Value.Host) == false ? target : null;
            }
            return null;
        }

        /// <summary>원격 주소에서 로그인할 서버. 로컬 경로·모르는 형식이면 null. 순수 함수라 테스트로 확인한다.</summary>
        public static RemoteLoginTarget? ParseUrl(string url)
        {
            url = (url ?? string.Empty).Trim();
            Match match = SshUrl.Match(url);
            if (match.Success)
            {
                return new RemoteLoginTarget(RemoteLoginKind.Ssh, match.Groups["host"].Value.Trim('[', ']'), NullIfEmpty(match.Groups["user"].Value));
            }
            match = HttpsUrl.Match(url);
            if (match.Success)
            {
                return new RemoteLoginTarget(RemoteLoginKind.Https, match.Groups["host"].Value, NullIfEmpty(Uri.UnescapeDataString(match.Groups["user"].Value)));
            }
            if (url.Contains("://") == false)
            {
                match = ScpUrl.Match(url);
                if (match.Success)
                {
                    return new RemoteLoginTarget(RemoteLoginKind.Ssh, match.Groups["host"].Value, NullIfEmpty(match.Groups["user"].Value));
                }
            }
            return null;
        }

        private static bool IsGitHub(string host)
        {
            string name = (host ?? string.Empty).ToLowerInvariant();
            return name == "github.com" || name.EndsWith(".github.com", StringComparison.Ordinal);
        }

        private static string NullIfEmpty(string value) => string.IsNullOrEmpty(value) ? null : value;

        /// <summary>
        /// SSH 서버 비밀번호를 이번 에디터 세션 동안 기억한다. 다음 git 부터 ssh 가 비밀번호를 물으면 이 값을 준다. 메인 스레드에서만 부른다.
        /// 처음 접속하는 서버면 "믿을까요?" 에도 그 서버에 한해 yes 로 답해 신뢰 목록(known_hosts)에 넣는다 — 사용자가 이 서버에 로그인하겠다고 고른 뒤라서다.
        /// 키가 바뀐 서버는 ssh 가 묻지 않고 거부하므로 그대로 막힌다.
        /// </summary>
        public static void RememberSsh(string host, string password)
        {
            s_ssh = new SshLogin { Host = host, Password = password };
            SessionState.SetString(SessionKey, host + "\n" + password);
        }

        /// <summary>기억한 SSH 비밀번호를 잊는다.</summary>
        public static void ForgetSsh()
        {
            s_ssh = null;
            SessionState.EraseString(SessionKey);
        }

        /// <summary>
        /// git 프로세스 환경에 SSH 비밀번호 도우미를 건다(<see cref="GitCommand"/>). 기억한 로그인이 없으면 아무것도 바꾸지 않는다.
        /// <c>SSH_ASKPASS_REQUIRE=force</c>: 에디터가 터미널에서 켜졌어도 ssh 가 그 터미널에서 기다리지 않고 늘 도우미에게 묻는다.
        /// </summary>
        public static void ApplyEnvironment(StringDictionary environment)
        {
            SshLogin login = s_ssh;
            if (login == null)
            {
                return;
            }
            string askPass = AskPassPath();
            if (askPass == null)
            {
                return;
            }
            environment["SSH_ASKPASS"] = askPass;
            environment["SSH_ASKPASS_REQUIRE"] = "force";
            // OpenSSH 8.3 이전은 DISPLAY 가 있어야 도우미를 부른다.
            if (string.IsNullOrEmpty(environment["DISPLAY"]))
            {
                environment["DISPLAY"] = ":0";
            }
            environment[HostVariable] = login.Host;
            environment[PasswordVariable] = login.Password;
        }

        /// <summary>
        /// ssh 가 묻는 글(첫 인자)을 보고 답하는 도우미. 기억한 서버의 비밀번호 질문과 "믿을까요?" 에만 답하고, 다른 질문(다른 서버·키 암호)은 거절한다.
        /// 서버 이름·비밀번호는 환경 변수로만 받는다.
        /// </summary>
        public const string AskPassScript =
            "#!/bin/sh\n" +
            "# Git Version Control(Unity): ssh 가 비밀번호를 물을 때 Git 창에서 받은 값을 돌려준다.\n" +
            "case \"$1\" in\n" +
            "  *\"$" + HostVariable + "\"*\"(yes/no\"*) echo yes; exit 0 ;;\n" +
            "  *\"$" + HostVariable + "\"*assword*) printf '%s\\n' \"$" + PasswordVariable + "\"; exit 0 ;;\n" +
            "esac\n" +
            "exit 1\n";

        /// <summary>도우미 스크립트 경로. 없으면 임시 폴더에 만든다. 만들 수 없으면 null.</summary>
        private static string AskPassPath()
        {
            string path = s_askPassPath;
            if (path != null && File.Exists(path))
            {
                return path;
            }
            try
            {
                string directory = Path.Combine(Path.GetTempPath(), "GitVersionControl");
                Directory.CreateDirectory(directory);
                path = Path.Combine(directory, "ssh-askpass.sh");
                File.WriteAllText(path, AskPassScript, new UTF8Encoding(false));
                if (Path.DirectorySeparatorChar != '\\')
                {
                    GitCommand.RunTool("/bin/chmod", directory, new[] { "700", path });
                }
                // Git for Windows 의 ssh 도 슬래시 경로를 읽는다.
                s_askPassPath = path.Replace('\\', '/');
                return s_askPassPath;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return null;
            }
        }

        /// <summary>
        /// https 계정을 이 PC 의 자격 증명 저장소에 넣는다. 예전에 저장한 (틀린) 값은 먼저 지운다. 백그라운드 스레드에서 불러도 된다.
        /// </summary>
        public static GitResult SaveHttps(string gitPath, string workingDirectory, string host, string username, string password)
        {
            string request = $"protocol=https\nhost={host}\nusername={username}\n";
            GitCommand.Run(gitPath, workingDirectory, new[] { "credential", "reject" }, input: request + "\n", applyTokenOverride: false);
            return GitCommand.Run(gitPath, workingDirectory, new[] { "credential", "approve" }, input: request + $"password={password}\n\n", applyTokenOverride: false);
        }
    }
}
