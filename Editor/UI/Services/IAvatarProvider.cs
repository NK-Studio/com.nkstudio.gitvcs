using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>커밋 작성자의 아바타 그림. 없거나 아직 받는 중이면 null 이고, 받으면 <see cref="AvatarLoaded"/> 로 알린다.</summary>
    public interface IAvatarProvider
    {
        bool IsEnabled { get; }

        /// <param name="commitHash">GitHub 커밋 API 로 찾을 때 쓸, 이 작성자의 커밋 하나.</param>
        Texture2D TryGet(string email, string commitHash);

        /// <summary>
        /// 이 작성자의 사진을 아직 받는 중인지(<see cref="TryGet"/> 이 받기를 시작한 뒤). 받는 중이면 머리글자 대신 빈 동그라미로 두었다가,
        /// 받으면 사진·못 받으면 머리글자로 바꾼다(<see cref="AvatarLoaded"/> 가 어느 쪽이든 알린다).
        /// </summary>
        bool IsLoading(string email);

        event Action AvatarLoaded;
    }

    /// <summary>
    /// GitHub·Gravatar 에서 아바타를 받아 메모리와 <c>Library/GitVersionControl/Avatars</c> 에 7일 동안 둔다.
    ///
    /// 작성자 이메일(또는 그 해시)과 커밋 해시를 외부 서비스로 보내므로 사용자가 설정에서 켰을 때만 동작한다.
    /// 받을 곳은 <see cref="GitAvatarSources"/> 의 순서를 따르고, 모두 실패하면 머리글자로 남는다(그 결과도 7일 동안 기억해 다시 묻지 않는다).
    /// 네트워크 요청은 메인 스레드를 막지 않는 UnityWebRequest 로 한다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class GitHubAvatarProvider : IAvatarProvider
    {
        private const string UserAgent = "com.nkstudio.gitvcs";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);

        /// <summary>
        /// "아바타 없음" 기록 파일. 예전(.none)에는 비공개 저장소·요청 제한도 "없음" 으로 기록해 7일 동안 머리글자로 남았다 —
        /// 이름을 바꿔 그 기록은 무시한다(2026-10-18).
        /// </summary>
        private const string MissingExtension = ".missing";
        private static readonly string CacheFolder = Path.Combine(GitRepository.ProjectRoot ?? ".", "Library", "GitVersionControl", "Avatars");

        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
        private string _remoteKey;
        private (string owner, string repository)? _gitHubRemote;

        /// <summary>
        /// 마지막으로 본 이 저장소의 계정(<see cref="GitAccounts.ApiLogin"/> — Git 창 아래쪽에 보이는 계정). 바뀌면 이번 세션의 "없음" 판단을 버리고 다시 찾는다.
        /// 예전에는 패키지 로그인(Preferences > Git)을 봐서, 아래쪽은 저장소 계정인데 내 커밋 사진은 패키지 로그인 계정으로 보였다.
        /// </summary>
        private string _signedInLogin;

        public GitHubAvatarProvider()
        {
            // 로그인·로그아웃하면 보이는 그래프가 바로 다시 그려지게 한다(다시 그릴 때 위의 판단이 새로 된다).
            GitHubAccount.Changed += () => AvatarLoaded?.Invoke();
            // 이 저장소의 계정(GitAccounts.ApiToken)이 정해지거나 바뀌면 그 전에 못 받은 사진을 다시 찾는다.
            // 에디터를 열면 계정이 비동기로 정해져, 그 전에 다른 계정(또는 토큰 없이)으로 물어 실패한 작성자가 세션 내내 머리글자로 남았다.
            GitAccounts.Changed += OnApiAccountChanged;
        }

        /// <summary>
        /// 계정이 바뀐 횟수. 바뀌기 전에 시작한 요청이 실패해도 "없음" 으로 접어 두지 않는다(새 계정으로 다시 물어야 한다).
        /// </summary>
        private int _accountGeneration;

        private void OnApiAccountChanged()
        {
            _accountGeneration++;
            _missing.Clear();
            // 내 커밋 사진은 계정을 따르므로, 앞 계정으로 받아 둔 사진을 메모리에서 버린다(디스크는 계정별 키라 그대로 둔다).
            ForgetOwnAvatar();
            AvatarLoaded?.Invoke();
        }

        public bool IsEnabled => GitVcsSettings.instance.ShowAvatars;

        public event Action AvatarLoaded;

        public bool IsLoading(string email)
        {
            return IsEnabled && string.IsNullOrWhiteSpace(email) == false && _pending.Contains(email);
        }

        public Texture2D TryGet(string email, string commitHash)
        {
            if (IsEnabled == false || string.IsNullOrWhiteSpace(email))
            {
                return null;
            }
            string signedInLogin = GitAccounts.ApiLogin ?? string.Empty;
            if (_signedInLogin != signedInLogin)
            {
                bool isFirstLook = _signedInLogin == null;
                _signedInLogin = signedInLogin;
                _missing.Clear();
                // 계정을 바꾸면 예전 계정 사진이 7일 동안 남지 않게 내 이메일 것만 지운다.
                if (isFirstLook == false)
                {
                    ForgetOwnAvatar();
                }
            }
            // AI 도구(Claude·OpenAI·Gemini)는 사람 사진이 없으니 바로 그 로고를 쓴다. 예전에 잘못 받아 둔 사진(공동 작성자 자리에 들어간
            // 커밋 작성자 얼굴 등)이 캐시에 남아 있어도 쓰지 않는다.
            Texture2D aiLogo = AiFallback(email);
            if (aiLogo != null)
            {
                return aiLogo;
            }
            if (_textures.TryGetValue(email, out Texture2D texture) && texture != null)
            {
                return texture;
            }
            if (_pending.Contains(email))
            {
                return null;
            }
            if (_missing.Contains(email))
            {
                return AiFallback(email);
            }

            string key = CacheKeyFor(email);
            if (TryLoadFromDisk(key, out texture, out bool knownMissing))
            {
                _textures[email] = texture;
                return texture;
            }
            // 내 커밋은 "없음" 기록을 믿지 않는다 — GitHub 에 로그인하면 언제든 사진이 생길 수 있다.
            if (knownMissing && IsOwnEmail(email) == false)
            {
                _missing.Add(email);
                return AiFallback(email);
            }

            _pending.Add(email);
            FetchAsync(email, commitHash, key);
            return null;
        }

        private async void FetchAsync(string email, string commitHash, string key)
        {
            Texture2D texture = null;
            byte[] bytes = null;
            bool temporaryFailure = false;
            int generation = _accountGeneration;
            try
            {
                (List<string> urls, bool gitHubAnswered) = await CandidateUrlsAsync(email, commitHash);
                // GitHub 가 작성자를 알려 주지 못한 경우(비공개 저장소·요청 제한·네트워크)는 "없음" 으로 단정하지 않는다.
                temporaryFailure = gitHubAnswered == false;
                foreach (string url in urls)
                {
                    (byte[] downloaded, bool downloadTemporary) = await DownloadAsync(url);
                    temporaryFailure |= downloadTemporary;
                    if (downloaded != null)
                    {
                        bytes = downloaded;
                        break;
                    }
                }
                if (bytes != null)
                {
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (texture.LoadImage(bytes) == false)
                    {
                        texture = null;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Git] avatar: {e.Message}");
                temporaryFailure = true;
            }
            finally
            {
                _pending.Remove(email);
            }

            if (texture != null)
            {
                // 디스크 키에 계정이 들어 있어 앞 계정 사진도 그 계정 몫으로는 맞다. 다만 묻는 사이 계정이 바뀌었으면 내 사진은 지금 계정 것이 아니니 화면에는 쓰지 않는다.
                SaveToDisk(key, bytes);
                if (generation == _accountGeneration || IsOwnEmail(email) == false)
                {
                    _textures[email] = texture;
                }
                AvatarLoaded?.Invoke();
            }
            else if (generation != _accountGeneration)
            {
                // 묻는 사이 계정이 바뀌었다 — 접어 두지 않고 다시 그려 새 계정으로 다시 묻게 한다.
                AvatarLoaded?.Invoke();
            }
            else
            {
                // 제한(403·429)이나 네트워크 오류는 이번 세션에만 접어 둔다. 정말 없는 경우만 디스크에 기억한다.
                _missing.Add(email);
                if (temporaryFailure == false && IsOwnEmail(email) == false)
                {
                    SaveMissingToDisk(key);
                }
                // 받는 동안 빈 동그라미로 두었으니 못 받았어도 다시 그리게 알린다(사람은 머리글자, AI 작성자는 로고).
                AvatarLoaded?.Invoke();
            }
        }

        private const string IconFolder = "Packages/com.nkstudio.gitvcs/Editor/UI/Icons/";
        private static readonly Dictionary<AiAuthor, Texture2D> AiLogos = new();

        /// <summary>
        /// 아바타를 못 받았을 때의 대신 그림. AI 도구(Claude·OpenAI·Gemini)의 이메일이면 그 로고(96px), 사람이면 null(머리글자).
        /// 먼저 GitHub 등에서 받아 보고(그 도구 계정 사진이 있으면 그것을 쓴다), 아직 푸시하지 않은 로컬 커밋처럼 못 받는 경우에만 쓴다.
        /// </summary>
        private static Texture2D AiFallback(string email)
        {
            AiAuthor author = GitAvatarSources.AiAuthorOf(email);
            if (author == AiAuthor.None)
            {
                return null;
            }
            if (AiLogos.TryGetValue(author, out Texture2D logo) && logo != null)
            {
                return logo;
            }
            logo = UnityEditor.AssetDatabase.LoadAssetAtPath<Texture2D>(IconFolder + "Ai" + author + ".png");
            AiLogos[author] = logo;
            return logo;
        }

        /// <returns>시도할 주소들, GitHub 커밋 API 가 답을 줬는지(실패했으면 false — 그때는 "없음" 을 디스크에 기억하지 않는다).</returns>
        private async Task<(List<string> urls, bool gitHubAnswered)> CandidateUrlsAsync(string email, string commitHash)
        {
            List<string> urls = new();
            // GitHub noreply 이메일이면 그 주소가 가리키는 계정이 곧 작성자다 — 가장 먼저 쓴다.
            string noReply = GitAvatarSources.NoReplyAvatarUrl(email);
            if (noReply != null)
            {
                urls.Add(noReply);
            }
            // 내 커밋: 아직 푸시하지 않았어도 이 저장소의 계정(아래쪽에 보이는 계정) 사진을 쓴다(GitKraken 과 같다).
            if (IsOwnEmail(email))
            {
                string ownAvatar = OwnAccountAvatarUrl();
                if (ownAvatar != null)
                {
                    urls.Add(ownAvatar);
                }
            }

            bool gitHubAnswered = true;
            (string owner, string repository)? remote = await GitHubRemoteAsync();
            if (remote.HasValue && string.IsNullOrEmpty(commitHash) == false)
            {
                (string avatarUrl, bool answered) = await GitHubCommitAvatarAsync(remote.Value.owner, remote.Value.repository, commitHash, GitAccounts.ApiToken);
                gitHubAnswered = answered;
                if (string.IsNullOrEmpty(avatarUrl) == false)
                {
                    urls.Add(WithSize(avatarUrl));
                }
            }

            urls.Add(GitAvatarSources.GravatarUrl(email));
            return (urls, gitHubAnswered);
        }

        private static string WithSize(string avatarUrl)
        {
            return avatarUrl + (avatarUrl.Contains("?") ? "&" : "?") + "s=" + GitAvatarSources.Size;
        }

        /// <summary>
        /// 이 저장소의 계정(<see cref="GitAccounts.ApiLogin"/>) 사진 주소. 패키지 로그인과 같은 계정이면 로그인 때 받은 주소, 다르면 github.com/이름.png.
        /// </summary>
        private static string OwnAccountAvatarUrl()
        {
            string login = GitAccounts.ApiLogin;
            if (string.IsNullOrEmpty(login))
            {
                return null;
            }
            if (string.Equals(login, GitHubAccount.Login, StringComparison.OrdinalIgnoreCase) && string.IsNullOrEmpty(GitHubAccount.AvatarUrl) == false)
            {
                return WithSize(GitHubAccount.AvatarUrl);
            }
            return $"https://github.com/{Uri.EscapeDataString(login)}.png?size={GitAvatarSources.Size}";
        }

        /// <summary>
        /// 디스크 캐시 키. 내 이메일은 계정에 따라 사진이 달라지므로 계정 이름을 섞는다 — 계정을 바꿔도 앞 계정 사진이 7일 동안 남지 않는다.
        /// </summary>
        private static string CacheKeyFor(string email)
        {
            return IsOwnEmail(email)
                ? GitAvatarSources.CacheKey(email + "\n" + (GitAccounts.ApiLogin ?? string.Empty))
                : GitAvatarSources.CacheKey(email);
        }

        private static bool IsOwnEmail(string email)
        {
            return GitAvatarSources.IsSameEmail(email, GitRepository.UserEmail);
        }

        private void ForgetOwnAvatar()
        {
            string email = GitRepository.UserEmail;
            if (string.IsNullOrWhiteSpace(email))
            {
                return;
            }
            _textures.Remove(email);
            // 예전 키(계정을 섞지 않은 키)로 남은 사진은 어느 계정 것인지 모르니 지운다.
            string key = GitAvatarSources.CacheKey(email);
            try
            {
                File.Delete(Path.Combine(CacheFolder, key + ".img"));
                File.Delete(Path.Combine(CacheFolder, key + MissingExtension));
            }
            catch (IOException)
            {
            }
        }

        /// <summary>origin 이 GitHub 저장소면 (owner, repo). 저장소가 바뀌지 않는 한 한 번만 묻는다.</summary>
        private async Task<(string owner, string repository)?> GitHubRemoteAsync()
        {
            string repoKey = GitRepository.RepoRoot ?? string.Empty;
            if (_remoteKey == repoKey)
            {
                return _gitHubRemote;
            }
            GitResult result = await GitRepository.RunAsync("remote", "get-url", "origin");
            _remoteKey = repoKey;
            _gitHubRemote = result.Success && GitAvatarSources.TryParseGitHubRemote(result.Output, out string owner, out string repository)
                ? (owner, repository)
                : null;
            return _gitHubRemote;
        }

        [Serializable]
        private sealed class CommitResponse
        {
            public Author author;
        }

        [Serializable]
        private sealed class Author
        {
            public string avatar_url;
        }

        /// <param name="token">GitHub 토큰. 비어 있으면 로그인하지 않고 묻는다(공개 저장소만 답한다).</param>
        /// <returns>(아바타 주소 또는 null, 답을 받았는지). 커밋은 찾았는데 GitHub 계정과 이어지지 않은 이메일이면 (null, true).</returns>
        private static async Task<(string avatarUrl, bool answered)> GitHubCommitAvatarAsync(string owner, string repository, string commitHash, string token)
        {
            using UnityWebRequest request = UnityWebRequest.Get(GitAvatarSources.GitHubCommitApiUrl(owner, repository, commitHash));
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            if (string.IsNullOrEmpty(token) == false)
            {
                request.SetRequestHeader("Authorization", "Bearer " + token);
            }
            await GitHubAccount.SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                // 404(비공개 저장소에 로그인 없이)·403/429(요청 제한)·네트워크 오류: 작성자를 모른다.
                return (null, false);
            }
            CommitResponse response = JsonUtility.FromJson<CommitResponse>(request.downloadHandler.text);
            return (response?.author?.avatar_url, true);
        }

        /// <returns>(받은 바이트 또는 null, 일시적 실패인지)</returns>
        private static async Task<(byte[] bytes, bool temporaryFailure)> DownloadAsync(string url)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", UserAgent);
            await GitHubAccount.SendAsync(request);
            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.data is { Length: > 0 } data)
            {
                return (data, false);
            }
            long code = request.responseCode;
            bool temporary = request.result == UnityWebRequest.Result.ConnectionError || code == 403 || code == 429 || code >= 500;
            return (null, temporary);
        }

        private static bool TryLoadFromDisk(string key, out Texture2D texture, out bool knownMissing)
        {
            texture = null;
            knownMissing = false;
            string imagePath = Path.Combine(CacheFolder, key + ".img");
            string missingPath = Path.Combine(CacheFolder, key + MissingExtension);
            try
            {
                if (File.Exists(imagePath) && DateTime.Now - File.GetLastWriteTime(imagePath) < CacheLifetime)
                {
                    texture = new Texture2D(2, 2) { hideFlags = HideFlags.HideAndDontSave };
                    if (texture.LoadImage(File.ReadAllBytes(imagePath)))
                    {
                        return true;
                    }
                    texture = null;
                }
                knownMissing = File.Exists(missingPath) && DateTime.Now - File.GetLastWriteTime(missingPath) < CacheLifetime;
            }
            catch (IOException)
            {
            }
            return false;
        }

        private static void SaveToDisk(string key, byte[] bytes)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                File.WriteAllBytes(Path.Combine(CacheFolder, key + ".img"), bytes);
                File.Delete(Path.Combine(CacheFolder, key + MissingExtension));
            }
            catch (IOException)
            {
                // 캐시는 없어도 동작한다.
            }
        }

        private static void SaveMissingToDisk(string key)
        {
            try
            {
                Directory.CreateDirectory(CacheFolder);
                File.WriteAllText(Path.Combine(CacheFolder, key + MissingExtension), string.Empty);
            }
            catch (IOException)
            {
            }
        }
    }
}
