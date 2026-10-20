using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// 계정 후보가 어디서 왔는지. 같은 이름이 여러 곳에 있으면 먼저 찾은 곳 하나만 남긴다.
    /// </summary>
    public enum GitAccountSource
    {
        /// <summary>지금 git 이 이 저장소에 쓰는 계정(어느 도우미에서 왔는지 모른다).</summary>
        Current,

        /// <summary>GitHub CLI(<c>gh auth status</c>).</summary>
        GitHubCli,

        /// <summary>Git Credential Manager(<c>git credential-manager github list</c>) — Windows 의 기본 도우미.</summary>
        CredentialManager,

        /// <summary>패키지 로그인(Preferences > Git).</summary>
        UnityLogin,
    }

    public sealed class GitAccountCandidate
    {
        public string Login;
        public GitAccountSource Source;

        /// <summary>이 저장소를 읽을 수 있는지. null 이면 확인하지 못했다(토큰을 못 꺼냄·네트워크).</summary>
        public bool? CanAccess;

        /// <summary>지금 이 저장소에 쓰는 계정인지.</summary>
        public bool IsCurrent;
    }

    /// <summary>
    /// 이 프로젝트 저장소의 GitHub 계정: 지금 쓰는 계정 알아보기, 이 PC 의 계정 후보 모으기·접근 확인, 고른 계정 저장. 메인 스레드에서 쓴다.
    /// 규칙(무엇을 어디에 적는지)은 <see cref="GitAuth"/>.
    /// </summary>
    public static class GitAccounts
    {
        private const int ToolTimeoutMs = 15_000;
        private const string UserAgent = "com.nkstudio.gitvcs";

        /// <summary>
        /// 백그라운드에서 꺼내 볼 때 로그인 창(GCM)·암호 프롬프트를 띄우지 않는다.
        /// </summary>
        private static readonly Dictionary<string, string> QuietEnvironment = new() { ["GCM_INTERACTIVE"] = "never" };

        /// <summary>
        /// 이 저장소에 쓸 계정을 고르거나 되돌렸을 때(설정 화면이 다시 읽는다).
        /// </summary>
        public static event System.Action Changed;

        private const int SourceDefault = 0;
        private const int SourceGitHubCli = 1;
        private const int SourceUnityLogin = 2;

        /// <summary>
        /// 고른 계정의 토큰을 git 에 넘길지 다시 정한다(<see cref="GitAuth.TokenOverride"/>). 설정·로그인이 바뀔 때와 에디터를 열 때 부른다.
        /// gh 계정은 <c>gh auth token --user</c> 로 꺼낸다 — gh 의 자격 증명 도우미는 저장소에 적은 이름을 무시하고 늘 활성 계정을 돌려주기 때문이다.
        /// </summary>
        public static async void ApplyTokenOverride()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            switch (settings.AccountSource)
            {
                case SourceUnityLogin:
                    GitAuth.TokenOverride = GitHubAccount.IsSignedIn ? GitHubAccount.Token : null;
                    break;
                case SourceGitHubCli:
                    string name = settings.AccountName;
                    string token = await GhTokenAsync(name);
                    // 기다리는 사이 다른 계정을 골랐으면 버린다.
                    if (settings.AccountSource == SourceGitHubCli && settings.AccountName == name)
                    {
                        GitAuth.TokenOverride = token;
                    }
                    break;
                default:
                    GitAuth.TokenOverride = null;
                    break;
            }
        }

        /// <summary>
        /// origin 이 GitHub https/ssh 원격이면 (owner, repo). 아니면 null(계정 고르기를 보이지 않는다).
        /// </summary>
        public static async Task<(string owner, string repository)?> GitHubRemoteAsync()
        {
            GitResult result = await GitRepository.RunAsync("remote", "get-url", "origin");
            return result.Success && GitAvatarSources.TryParseGitHubRemote(result.Output, out string owner, out string repository)
                ? (owner, repository)
                : null;
        }

        /// <summary>
        /// 이 저장소에 따로 정한 계정이 있으면 그 설명(설정 화면). 없으면 null(git 기본).
        /// </summary>
        public static async Task<string> ChosenDescriptionAsync()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            if (settings.AccountSource == SourceUnityLogin)
            {
                return GitText.Get("account.sourceUnityLogin");
            }
            if (settings.AccountSource == SourceGitHubCli)
            {
                return GitText.Get("account.sourceGitHubCli");
            }
            GitResult result = await GitRepository.RunAsync("config", "--local", "--get", GitAuth.UsernameConfigKey);
            return result.Success && string.IsNullOrWhiteSpace(result.Output) == false ? GitText.Get("settings.accountChosen") : null;
        }

        /// <summary>
        /// 지금 이 저장소에 쓰는 계정 이름. 모르면 null.
        /// </summary>
        public static async Task<string> CurrentUsernameAsync(string owner, string repository)
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            if (settings.AccountSource == SourceUnityLogin && GitHubAccount.IsSignedIn)
            {
                return GitHubAccount.Login;
            }
            if (settings.AccountSource == SourceGitHubCli && string.IsNullOrEmpty(settings.AccountName) == false)
            {
                return settings.AccountName;
            }
            (string username, _) = await FillAsync(owner, repository, null);
            return username;
        }

        /// <summary>
        /// 이 PC 의 GitHub 계정 후보와 각 계정이 이 저장소를 읽을 수 있는지. gh·GCM 이 없으면 그 출처만 건너뛴다.
        /// </summary>
        public static async Task<List<GitAccountCandidate>> ListAsync(string owner, string repository)
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            string current = await CurrentUsernameAsync(owner, repository);
            List<GitAccountCandidate> candidates = new();
            void Add(string login, GitAccountSource source)
            {
                if (string.IsNullOrEmpty(login) || login == "x-access-token")
                {
                    return;
                }
                // 같은 이름이 여러 도구에 있으면 먼저 찾은 쪽 하나만(패키지 로그인은 따로 보인다 — 토큰을 넘기는 방법이 달라서).
                if (source != GitAccountSource.UnityLogin && candidates.Any(c => c.Source != GitAccountSource.UnityLogin && string.Equals(c.Login, login, System.StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
                candidates.Add(new GitAccountCandidate { Login = login, Source = source });
            }

            foreach (string login in await GhAccountsAsync())
            {
                Add(login, GitAccountSource.GitHubCli);
            }
            foreach (string login in await CredentialManagerAccountsAsync())
            {
                Add(login, GitAccountSource.CredentialManager);
            }
            if (settings.AccountSource == SourceDefault)
            {
                Add(current, GitAccountSource.Current);
            }
            if (GitHubAccount.IsSignedIn)
            {
                Add(GitHubAccount.Login, GitAccountSource.UnityLogin);
            }

            foreach (GitAccountCandidate candidate in candidates)
            {
                candidate.IsCurrent = IsCurrent(candidate, settings, current);
                string token = candidate.Source switch
                {
                    GitAccountSource.UnityLogin => GitHubAccount.Token,
                    GitAccountSource.GitHubCli => await GhTokenAsync(candidate.Login),
                    _ => (await FillAsync(owner, repository, candidate.Login)).password,
                };
                candidate.CanAccess = string.IsNullOrEmpty(token) ? null : await CanAccessAsync(owner, repository, token);
            }
            return candidates;
        }

        private static bool IsCurrent(GitAccountCandidate candidate, GitVcsSettings settings, string current)
        {
            return settings.AccountSource switch
            {
                SourceUnityLogin => candidate.Source == GitAccountSource.UnityLogin,
                SourceGitHubCli => candidate.Source == GitAccountSource.GitHubCli && candidate.Login == settings.AccountName,
                _ => candidate.Source != GitAccountSource.UnityLogin && string.Equals(candidate.Login, current, System.StringComparison.OrdinalIgnoreCase),
            };
        }

        /// <summary>
        /// 이 저장소에 쓸 계정을 정한다. gh 계정·패키지 로그인은 그 토큰을 git 에 직접 넘기고(<see cref="ApplyTokenOverride"/>),
        /// Git Credential Manager·키체인 계정은 저장소 설정에 이름을 적는다(이 도우미들은 이름으로 계정을 고른다).
        /// </summary>
        public static async Task<GitResult> ChooseAsync(GitAccountCandidate candidate)
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            GitResult result = new(0, string.Empty, string.Empty);
            switch (candidate.Source)
            {
                case GitAccountSource.UnityLogin:
                    settings.AccountSource = SourceUnityLogin;
                    settings.AccountName = string.Empty;
                    break;
                case GitAccountSource.GitHubCli:
                    string token = await GhTokenAsync(candidate.Login);
                    if (string.IsNullOrEmpty(token))
                    {
                        return new GitResult(1, string.Empty, GitText.Format("account.ghTokenFailed", candidate.Login));
                    }
                    settings.AccountSource = SourceGitHubCli;
                    settings.AccountName = candidate.Login;
                    GitAuth.TokenOverride = token;
                    break;
                default:
                    settings.AccountSource = SourceDefault;
                    settings.AccountName = string.Empty;
                    result = await GitRepository.RunAsync("config", "--local", GitAuth.UsernameConfigKey, candidate.Login);
                    break;
            }
            if (candidate.Source != GitAccountSource.GitHubCli)
            {
                ApplyTokenOverride();
            }
            Changed?.Invoke();
            return result;
        }

        /// <summary>
        /// 고른 계정을 지우고 git 기본(도우미가 정하는 계정)으로 되돌린다.
        /// </summary>
        public static async Task ResetAsync()
        {
            GitVcsSettings.instance.AccountSource = SourceDefault;
            GitVcsSettings.instance.AccountName = string.Empty;
            ApplyTokenOverride();
            await GitRepository.RunAsync("config", "--local", "--unset", GitAuth.UsernameConfigKey);
            Changed?.Invoke();
        }

        /// <summary>
        /// gh 에 로그인된 그 계정의 토큰(<c>gh auth token --user</c>). gh 가 없거나 그 계정이 없으면 null. 저장하지 않는다.
        /// </summary>
        private static async Task<string> GhTokenAsync(string login)
        {
            string gh = GitAuth.FindExecutable("gh", GitAuth.GhInstallDirectories());
            if (gh == null || string.IsNullOrEmpty(login))
            {
                return null;
            }
            GitResult result = await Task.Run(() => GitCommand.RunTool(gh, GitRepository.RepoRoot, new[] { "auth", "token", "--hostname", "github.com", "--user", login }, ToolTimeoutMs));
            string token = result.Output?.Trim();
            return result.Success && string.IsNullOrEmpty(token) == false ? token : null;
        }

        /// <summary>
        /// <c>git credential fill</c> 로 이 저장소의 자격 증명을 꺼낸다(<paramref name="username"/> 이면 그 계정의 것). 없으면 (null, null).
        /// 토큰은 부른 쪽이 바로 쓰고 버린다 — 저장하거나 로그에 남기지 않는다.
        /// </summary>
        private static async Task<(string username, string password)> FillAsync(string owner, string repository, string username)
        {
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string root = GitRepository.RepoRoot;
            string request = GitAuth.CredentialRequest(owner, repository, username);
            GitResult result = await Task.Run(() => GitCommand.Run(gitPath, root, new[] { "credential", "fill" }, ToolTimeoutMs, QuietEnvironment, request, applyTokenOverride: false));
            return result.Success ? GitAuth.ParseCredential(result.Output) : (null, null);
        }

        private static async Task<List<string>> GhAccountsAsync()
        {
            string gh = GitAuth.FindExecutable("gh", GitAuth.GhInstallDirectories());
            if (gh == null)
            {
                return new List<string>();
            }
            // gh 는 git 이 아니라 GitCommand 를 못 쓴다. 상태 글은 버전에 따라 stdout·stderr 어느 쪽에도 나와 둘 다 읽는다.
            GitResult result = await Task.Run(() => GitCommand.RunTool(gh, GitRepository.RepoRoot, new[] { "auth", "status", "--hostname", "github.com" }, ToolTimeoutMs));
            return GitAuth.ParseGhAccounts(result.Output + "\n" + result.Error);
        }

        private static async Task<List<string>> CredentialManagerAccountsAsync()
        {
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string root = GitRepository.RepoRoot;
            GitResult result = await Task.Run(() => GitCommand.Run(gitPath, root, new[] { "credential-manager", "github", "list" }, ToolTimeoutMs, QuietEnvironment, applyTokenOverride: false));
            return result.Success ? GitAuth.ParseGcmAccounts(result.Output) : new List<string>();
        }

        /// <returns>읽을 수 있으면 true, GitHub 가 없다·금지라고 답하면 false, 그 밖(네트워크·요청 제한)은 null.</returns>
        private static async Task<bool?> CanAccessAsync(string owner, string repository, string token)
        {
            using UnityWebRequest request = UnityWebRequest.Get($"https://api.github.com/repos/{owner}/{repository}");
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            request.SetRequestHeader("Authorization", "Bearer " + token);
            await GitHubAccount.SendAsync(request);
            if (request.result == UnityWebRequest.Result.Success)
            {
                return true;
            }
            return request.responseCode == 404 || request.responseCode == 403 || request.responseCode == 401 ? false : null;
        }
    }
}
