using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace GitVersionControl
{
    /// <summary>
    /// 회사망처럼 SSL 을 가로채는 환경(보안 프록시가 자기 인증서로 다시 서명)에서 git 이 원격에 붙게 하는 설정.
    ///
    /// git 은 운영체제 인증서 저장소가 아니라 자기 CA 묶음을 보는 일이 많아(Git for Windows 의 OpenSSL, Homebrew git),
    /// 회사 루트 인증서를 PC 에 깔아도 push·pull·fetch 가 "SSL certificate problem: unable to get local issuer certificate" 로 실패한다.
    /// 그래서 Preferences > Git 의 SSL 설정을 git 호출마다 <c>-c</c> 로 앞에 붙인다(<see cref="GitCommand"/>).
    /// <c>-c</c> 는 git 이 부르는 git-lfs·credential helper 에도 <c>GIT_CONFIG_PARAMETERS</c> 로 넘어간다.
    /// 사용자의 <c>.gitconfig</c> 는 건드리지 않는다 — 터미널 git 에는 영향이 없다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitSsl
    {
        /// <summary>
        /// SSL·인증서 문제로 연결이 끊겼을 때 git·curl·git-lfs(Go) 가 남기는 글. 소문자로 비교한다.
        /// </summary>
        private static readonly string[] FailurePatterns =
        {
            "ssl certificate problem",
            "server certificate verification failed",
            "unable to get local issuer certificate",
            "self signed certificate",
            "self-signed certificate",
            "certificate verify failed",
            "certificate has expired",
            "x509: certificate",
            "tls: failed to verify certificate",
            // CA 인증서 파일 경로가 틀렸거나 읽을 수 없을 때.
            "error setting certificate verify locations",
            "problem with the ssl ca cert",
            // Windows schannel: 회사 인증서에 해지 목록 주소가 없거나 닿지 않을 때(CRYPT_E_NO_REVOCATION_CHECK·CRYPT_E_REVOCATION_OFFLINE).
            "revocation function was unable to check revocation",
            "0x80092012",
            "0x80092013",
            "sec_e_untrusted_root",
            "schannel: next initializesecuritycontext failed",
            "schannel: sec_e",
        };

        /// <summary>
        /// 지금 git 앞에 붙일 <c>-c</c> 인자. 메인 스레드에서 설정을 읽어 옮겨 둔다(<see cref="Refresh"/>) —
        /// <see cref="GitCommand"/> 가 백그라운드 스레드에서 읽는다.
        /// </summary>
        public static volatile IReadOnlyList<string> CurrentArguments = Array.Empty<string>();

        /// <summary>
        /// 인증서 검증을 끈 상태인가. GitHub API·아바타 요청(UnityWebRequest)도 같이 따른다(<c>GitHubAccount.SendAsync</c>).
        /// </summary>
        public static volatile bool VerifyDisabled;

        private static bool IsWindows => Path.DirectorySeparatorChar == '\\';

        /// <summary>
        /// git 오류 글이 SSL·인증서 문제인가.
        /// </summary>
        public static bool IsSslFailure(string message)
        {
            if (string.IsNullOrEmpty(message))
            {
                return false;
            }
            string lower = message.ToLowerInvariant();
            return FailurePatterns.Any(lower.Contains);
        }

        /// <summary>
        /// 설정 값으로 git 앞에 붙일 <c>-c</c> 인자를 만든다. 순수 함수라 테스트로 확인한다.
        /// </summary>
        /// <param name="verify">false 면 인증서를 확인하지 않는다(<c>http.sslVerify=false</c>).</param>
        /// <param name="caFile">회사 루트 인증서가 든 PEM 파일. 비우면 git 기본값.</param>
        /// <param name="systemStore">Windows: 운영체제 인증서 저장소를 쓴다(<c>http.sslBackend=schannel</c>).</param>
        /// <param name="skipRevocationCheck">Windows schannel: 해지 확인을 끈다(<c>http.schannelCheckRevoke=false</c>).</param>
        /// <param name="windows">Windows 에서만 schannel 설정을 붙인다(다른 git 은 schannel 을 모른다).</param>
        public static IReadOnlyList<string> BuildArguments(bool verify, string caFile, bool systemStore, bool skipRevocationCheck, bool windows)
        {
            List<string> args = new();
            if (verify == false)
            {
                args.Add("-c");
                args.Add("http.sslVerify=false");
                return args;
            }
            if (windows && systemStore)
            {
                args.Add("-c");
                args.Add("http.sslBackend=schannel");
                if (skipRevocationCheck)
                {
                    args.Add("-c");
                    args.Add("http.schannelCheckRevoke=false");
                }
            }
            // schannel 은 CA 파일을 보지 않으므로 둘 중 하나만 붙인다.
            else if (string.IsNullOrWhiteSpace(caFile) == false)
            {
                args.Add("-c");
                // git 은 Windows 에서도 슬래시 경로를 읽는다. 백슬래시는 설정 값에서 이스케이프로 읽힐 수 있어 바꾼다.
                args.Add("http.sslCAInfo=" + caFile.Trim().Replace('\\', '/'));
            }
            return args;
        }

        /// <summary>
        /// 설정을 읽어 <see cref="CurrentArguments"/>·<see cref="VerifyDisabled"/> 를 새로 둔다. 메인 스레드에서만 부른다.
        /// </summary>
        public static void Refresh()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            CurrentArguments = BuildArguments(settings.SslVerify, settings.SslCaFile, settings.SslUseSystemStore, settings.SslSkipRevocationCheck, IsWindows);
            VerifyDisabled = settings.SslVerify == false;
        }

        /// <summary>
        /// macOS 키체인(로그인·시스템·시스템 루트)의 인증서를 PEM 하나로 모은다. 회사 루트 인증서를 키체인에 넣어 두었으면 git 이 그 파일로 확인할 수 있다.
        /// </summary>
        /// <returns>(만든 파일 경로, 오류). 성공이면 오류는 null.</returns>
        public static (string path, string error) ExportMacKeychainCertificates()
        {
            const string security = "/usr/bin/security";
            if (File.Exists(security) == false)
            {
                return (null, security + " not found");
            }
            StringBuilder pem = new();
            // 인자 없이 부르면 키체인 검색 목록(로그인·System)을, 그 다음은 Apple 이 넣어 둔 공개 루트 인증서들을 꺼낸다.
            foreach (string[] args in new[]
            {
                new[] { "find-certificate", "-a", "-p" },
                new[] { "find-certificate", "-a", "-p", "/System/Library/Keychains/SystemRootCertificates.keychain" },
            })
            {
                GitResult result = GitCommand.RunTool(security, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), args);
                if (result.Success)
                {
                    pem.Append(result.Output);
                }
            }
            if (pem.ToString().Contains("BEGIN CERTIFICATE") == false)
            {
                return (null, "no certificates exported");
            }
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "GitVersionControl");
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "ca-bundle.pem");
                File.WriteAllText(path, pem.ToString(), new UTF8Encoding(false));
                return (path, null);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                return (null, e.Message);
            }
        }
    }
}
