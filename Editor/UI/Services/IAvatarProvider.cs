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

        event Action AvatarLoaded;
    }

    /// <summary>
    /// GitHub·Gravatar 에서 아바타를 받아 메모리와 <c>Library/GitVersionControl/Avatars</c> 에 7일 동안 둔다.
    ///
    /// 작성자 이메일(또는 그 해시)과 커밋 해시를 외부 서비스로 보내므로 사용자가 설정에서 켰을 때만 동작한다.
    /// 받을 곳은 <see cref="GitAvatarSources"/> 의 순서를 따르고, 모두 실패하면 머리글자로 남는다(그 결과도 7일 동안 기억해 다시 묻지 않는다).
    /// 네트워크 요청은 메인 스레드를 막지 않는 UnityWebRequest 로 한다.
    /// </summary>
    public sealed class GitHubAvatarProvider : IAvatarProvider
    {
        private const string UserAgent = "com.nkstudio.gitvcs";
        private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(7);
        private static readonly string CacheFolder = Path.Combine(GitRepository.ProjectRoot ?? ".", "Library", "GitVersionControl", "Avatars");

        private readonly Dictionary<string, Texture2D> _textures = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _missing = new(StringComparer.OrdinalIgnoreCase);
        private string _remoteKey;
        private (string owner, string repository)? _gitHubRemote;

        public bool IsEnabled => GitVcsSettings.instance.ShowAvatars;

        public event Action AvatarLoaded;

        public Texture2D TryGet(string email, string commitHash)
        {
            if (IsEnabled == false || string.IsNullOrWhiteSpace(email))
            {
                return null;
            }
            if (_textures.TryGetValue(email, out Texture2D texture) && texture != null)
            {
                return texture;
            }
            if (_missing.Contains(email) || _pending.Contains(email))
            {
                return null;
            }

            string key = GitAvatarSources.CacheKey(email);
            if (TryLoadFromDisk(key, out texture, out bool knownMissing))
            {
                _textures[email] = texture;
                return texture;
            }
            if (knownMissing)
            {
                _missing.Add(email);
                return null;
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
            try
            {
                foreach (string url in await CandidateUrlsAsync(email, commitHash))
                {
                    (bytes, temporaryFailure) = await DownloadAsync(url);
                    if (bytes != null)
                    {
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
                _textures[email] = texture;
                SaveToDisk(key, bytes);
                AvatarLoaded?.Invoke();
            }
            else
            {
                // 제한(403·429)이나 네트워크 오류는 이번 세션에만 접어 둔다. 정말 없는 경우만 디스크에 기억한다.
                _missing.Add(email);
                if (temporaryFailure == false)
                {
                    SaveMissingToDisk(key);
                }
            }
        }

        private async Task<List<string>> CandidateUrlsAsync(string email, string commitHash)
        {
            List<string> urls = new();
            string noReply = GitAvatarSources.NoReplyAvatarUrl(email);
            if (noReply != null)
            {
                urls.Add(noReply);
            }

            (string owner, string repository)? remote = await GitHubRemoteAsync();
            if (remote.HasValue && string.IsNullOrEmpty(commitHash) == false)
            {
                string avatarUrl = await GitHubCommitAvatarAsync(remote.Value.owner, remote.Value.repository, commitHash);
                if (string.IsNullOrEmpty(avatarUrl) == false)
                {
                    urls.Add(avatarUrl + (avatarUrl.Contains("?") ? "&" : "?") + "s=" + GitAvatarSources.Size);
                }
            }

            urls.Add(GitAvatarSources.GravatarUrl(email));
            return urls;
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

        private static async Task<string> GitHubCommitAvatarAsync(string owner, string repository, string commitHash)
        {
            using UnityWebRequest request = UnityWebRequest.Get(GitAvatarSources.GitHubCommitApiUrl(owner, repository, commitHash));
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/vnd.github+json");
            await SendAsync(request);
            if (request.result != UnityWebRequest.Result.Success)
            {
                return null;
            }
            CommitResponse response = JsonUtility.FromJson<CommitResponse>(request.downloadHandler.text);
            return response?.author?.avatar_url;
        }

        /// <returns>(받은 바이트 또는 null, 일시적 실패인지)</returns>
        private static async Task<(byte[] bytes, bool temporaryFailure)> DownloadAsync(string url)
        {
            using UnityWebRequest request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", UserAgent);
            await SendAsync(request);
            if (request.result == UnityWebRequest.Result.Success && request.downloadHandler.data is { Length: > 0 } data)
            {
                return (data, false);
            }
            long code = request.responseCode;
            bool temporary = request.result == UnityWebRequest.Result.ConnectionError || code == 403 || code == 429 || code >= 500;
            return (null, temporary);
        }

        /// <summary>UnityWebRequest 의 완료 콜백은 메인 스레드에서 오므로, await 뒤도 메인 스레드다.</summary>
        private static Task SendAsync(UnityWebRequest request)
        {
            TaskCompletionSource<bool> completion = new();
            request.timeout = 15;
            request.SendWebRequest().completed += _ => completion.TrySetResult(true);
            return completion.Task;
        }

        private static bool TryLoadFromDisk(string key, out Texture2D texture, out bool knownMissing)
        {
            texture = null;
            knownMissing = false;
            string imagePath = Path.Combine(CacheFolder, key + ".img");
            string missingPath = Path.Combine(CacheFolder, key + ".none");
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
                File.Delete(Path.Combine(CacheFolder, key + ".none"));
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
                File.WriteAllText(Path.Combine(CacheFolder, key + ".none"), string.Empty);
            }
            catch (IOException)
            {
            }
        }
    }
}
