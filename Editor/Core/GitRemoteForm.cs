using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 저장소 목록(<c>GET /user/repos</c>)의 한 줄.
    /// </summary>
    public sealed class GitHubRepository
    {
        /// <summary>"owner/name".</summary>
        public string FullName;

        public string Owner;
        public string CloneUrl;
        public bool IsPrivate;
    }

    /// <summary>
    /// 원격 추가 서랍(<see cref="AddRemoteDrawer"/>)의 규칙: 이름 검사, 처음 채울 이름, GitHub 저장소 목록 읽기. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitRemoteForm
    {
        /// <summary>
        /// 원격 이름 검사. 규칙은 브랜치 이름과 같고(git 이 refs/remotes/&lt;이름&gt;/… 로 쓴다), 이미 있는 원격과 겹치면 <see cref="BranchNameProblem.Exists"/>.
        /// </summary>
        public static BranchNameProblem CheckName(string name, IEnumerable<string> existingRemotes)
        {
            return GitBranchNames.Check(name, existingRemotes);
        }

        /// <summary>
        /// git 이 원격 주소로 받을 만한 글인가: <c>https://…</c>·<c>ssh://…</c>·<c>file://…</c> 같은 URL, <c>git@host:owner/repo.git</c> 같은 scp 꼴,
        /// 또는 있는 폴더·파일 경로(<paramref name="pathExists"/>, 상대 경로는 부른 쪽이 저장소 기준으로 푼다). "/" 처럼 뜻 없는 글은 막는다.
        /// </summary>
        /// <summary>로컬 경로 원격이 있는지(상대 경로는 저장소 루트 기준).</summary>
        public static bool PathExistsFromRepo(string path)
        {
            string root = GitRepository.RepoRoot ?? GitRepository.ProjectRoot ?? string.Empty;
            string full = System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(root, path);
            return System.IO.Directory.Exists(full) || System.IO.File.Exists(full);
        }

        public static bool IsPlausibleUrl(string url, Func<string, bool> pathExists)
        {
            string text = url?.Trim() ?? string.Empty;
            if (text.Length == 0 || text.Any(char.IsWhiteSpace))
            {
                return false;
            }
            int scheme = text.IndexOf("://", StringComparison.Ordinal);
            if (scheme > 0)
            {
                string name = text.Substring(0, scheme);
                string rest = text.Substring(scheme + 3).Trim('/');
                return char.IsLetter(name[0]) && name.All(c => char.IsLetterOrDigit(c) || c is '+' or '-' or '.') && rest.Length > 0;
            }
            // scp 꼴: 콜론 앞에 '/' 가 없고, 한 글자(Windows 드라이브 "C:")가 아니며, 콜론 뒤에 경로가 있다.
            int colon = text.IndexOf(':');
            if (colon > 1 && text.IndexOf('/') is var slash && (slash < 0 || slash > colon) && colon < text.Length - 1)
            {
                return true;
            }
            string trimmed = text.TrimEnd('/', '\\');
            return trimmed.Length > 0 && trimmed != "." && pathExists != null && pathExists(text);
        }

        /// <summary>
        /// 처음 채울 원격 이름. 원격이 하나도 없으면 origin, 있으면 저장소 주인(겹치면 뒤에 2, 3…), 주인을 모르면 remote.
        /// </summary>
        public static string SuggestName(ICollection<string> existingRemotes, string owner)
        {
            bool Taken(string name) => existingRemotes.Any(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase));
            if (Taken("origin") == false)
            {
                return "origin";
            }
            string stem = string.IsNullOrWhiteSpace(owner) || GitBranchNames.Check(owner, null) != BranchNameProblem.None ? "remote" : owner;
            if (Taken(stem) == false)
            {
                return stem;
            }
            for (int i = 2;; i++)
            {
                if (Taken(stem + i) == false)
                {
                    return stem + i;
                }
            }
        }

        /// <summary>
        /// <c>/user/repos</c> 응답(JSON 배열) 한 쪽. JsonUtility 는 맨 위 배열을 못 읽어 객체로 감싸 읽는다.
        /// </summary>
        public static List<GitHubRepository> ParseRepositories(string json)
        {
            List<GitHubRepository> repositories = new();
            if (string.IsNullOrWhiteSpace(json))
            {
                return repositories;
            }
            RepositoryPage page = JsonUtility.FromJson<RepositoryPage>("{\"items\":" + json + "}");
            foreach (RepositoryJson item in page?.items ?? Array.Empty<RepositoryJson>())
            {
                if (string.IsNullOrEmpty(item?.full_name) || string.IsNullOrEmpty(item.clone_url))
                {
                    continue;
                }
                repositories.Add(new GitHubRepository
                {
                    FullName = item.full_name,
                    Owner = item.owner?.login ?? item.full_name.Split('/')[0],
                    CloneUrl = item.clone_url,
                    IsPrivate = item.@private,
                });
            }
            return repositories;
        }

        [Serializable]
        private sealed class RepositoryPage
        {
            public RepositoryJson[] items;
        }

        [Serializable]
        private sealed class RepositoryJson
        {
            public string full_name;
            public string clone_url;
            public bool @private;
            public OwnerJson owner;
        }

        [Serializable]
        private sealed class OwnerJson
        {
            public string login;
        }
    }
}
