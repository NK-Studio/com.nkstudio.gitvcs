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

    /// <summary>새 GitHub 저장소 이름의 문제(<see cref="GitRemoteForm.CheckRepositoryName"/>).</summary>
    public enum GitHubRepoNameProblem
    {
        None,
        Empty,
        TooLong,

        /// <summary>영문·숫자·<c>. - _</c> 밖의 글자.</summary>
        InvalidCharacter,

        /// <summary><c>.</c>·<c>..</c> 처럼 쓸 수 없는 이름.</summary>
        Reserved,

        /// <summary>그 소유자에게 같은 이름의 저장소가 이미 있다.</summary>
        Exists,
    }

    /// <summary>
    /// 원격 추가 서랍(<see cref="AddRemoteDrawer"/>)의 규칙: 이름 검사, 처음 채울 이름, GitHub 저장소 목록 읽기, 새 저장소 만들기 요청. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitRemoteForm
    {
        /// <summary>GitHub 저장소 이름 최대 길이.</summary>
        public const int MaxRepositoryNameLength = 100;

        /// <summary>
        /// 새 GitHub 저장소 이름 검사. GitHub 는 영문·숫자·<c>. - _</c> 만 받고(다른 글자는 '-' 로 바꿔 버린다) 100자까지다.
        /// 바꿔 만들면 원격 주소가 사용자가 적은 이름과 달라지므로, 바꾸지 않고 여기서 막는다. 이미 아는 저장소(<paramref name="known"/>)와 겹치면 <see cref="GitHubRepoNameProblem.Exists"/>.
        /// </summary>
        public static GitHubRepoNameProblem CheckRepositoryName(string name, string owner, IEnumerable<GitHubRepository> known)
        {
            if (string.IsNullOrEmpty(name))
            {
                return GitHubRepoNameProblem.Empty;
            }
            if (name.Length > MaxRepositoryNameLength)
            {
                return GitHubRepoNameProblem.TooLong;
            }
            if (name.Any(c => IsRepositoryNameChar(c) == false))
            {
                return GitHubRepoNameProblem.InvalidCharacter;
            }
            if (name == "." || name == "..")
            {
                return GitHubRepoNameProblem.Reserved;
            }
            string fullName = owner + "/" + name;
            if (known != null && known.Any(r => string.Equals(r.FullName, fullName, StringComparison.OrdinalIgnoreCase)))
            {
                return GitHubRepoNameProblem.Exists;
            }
            return GitHubRepoNameProblem.None;
        }

        private static bool IsRepositoryNameChar(char c)
        {
            return c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '-' or '_';
        }

        /// <summary>
        /// 처음 채울 새 저장소 이름: 프로젝트 폴더 이름에서 쓸 수 없는 글자(공백·한글 등)를 '-' 로 바꾸고 겹친 '-' 와 앞뒤 '-'·'.' 를 정리한다.
        /// 남는 글자가 없으면(폴더 이름이 모두 한글 등) "unity-project".
        /// </summary>
        public static string SuggestRepositoryName(string projectFolder)
        {
            System.Text.StringBuilder builder = new();
            foreach (char c in projectFolder ?? string.Empty)
            {
                char next = IsRepositoryNameChar(c) ? c : '-';
                if (next == '-' && builder.Length > 0 && builder[builder.Length - 1] == '-')
                {
                    continue;
                }
                builder.Append(next);
            }
            string name = builder.ToString().Trim('-', '.');
            if (name.Length > MaxRepositoryNameLength)
            {
                name = name.Substring(0, MaxRepositoryNameLength).TrimEnd('-', '.');
            }
            return name.Length == 0 ? "unity-project" : name;
        }

        /// <summary>새 저장소 만들기 요청 본문(<c>POST /user/repos</c>·<c>POST /orgs/{org}/repos</c>).</summary>
        public static string CreateRepositoryJson(string name, bool isPrivate)
        {
            return JsonUtility.ToJson(new CreateRepositoryRequest { name = name, @private = isPrivate });
        }

        /// <summary>
        /// 새 저장소에 처음 올릴 기본 브랜치: 로컬에 main 이 있으면 main, 없고 master 가 있으면 master, 그다음 git 설정 init.defaultBranch,
        /// 그것도 없으면 현재 브랜치. 빈 저장소에 처음 올린 브랜치가 GitHub 의 기본 브랜치가 되므로, 작업 브랜치(feature/…)가 기본이 되지 않게 한다.
        /// </summary>
        public static string PickDefaultBranch(IList<string> localBranches, string configuredDefault, string current)
        {
            foreach (string candidate in new[] { "main", "master", configuredDefault })
            {
                if (string.IsNullOrEmpty(candidate) == false && localBranches.Contains(candidate))
                {
                    return candidate;
                }
            }
            return string.IsNullOrEmpty(current) == false ? current : localBranches.FirstOrDefault();
        }

        /// <summary>
        /// 새 저장소에 올릴 브랜치를 올릴 순서대로: 기본 브랜치가 언제나 맨 앞(첫 Push 가 GitHub 기본 브랜치가 된다),
        /// 그 뒤 현재 브랜치(<paramref name="includeCurrent"/>), 그 뒤 나머지 로컬 브랜치(<paramref name="includeOthers"/>, 받은 순서). 겹치는 것은 한 번만.
        /// </summary>
        public static List<string> PublishBranches(string defaultBranch, string current, bool includeCurrent, bool includeOthers, IEnumerable<string> localBranches)
        {
            List<string> branches = new();
            void AddOnce(string branch)
            {
                if (string.IsNullOrEmpty(branch) == false && branches.Contains(branch) == false)
                {
                    branches.Add(branch);
                }
            }
            AddOnce(defaultBranch);
            if (includeCurrent)
            {
                AddOnce(current);
            }
            if (includeOthers)
            {
                foreach (string branch in localBranches ?? Enumerable.Empty<string>())
                {
                    AddOnce(branch);
                }
            }
            return branches;
        }

        /// <summary>기본 브랜치 바꾸기 요청 본문(<c>PATCH /repos/{owner}/{repo}</c>).</summary>
        public static string DefaultBranchJson(string branch)
        {
            return JsonUtility.ToJson(new DefaultBranchRequest { default_branch = branch });
        }

        /// <summary>저장소 하나(만들기 응답). 읽지 못하면 null.</summary>
        public static GitHubRepository ParseRepository(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            List<GitHubRepository> one = ParseRepositories("[" + json + "]");
            return one.Count > 0 ? one[0] : null;
        }

        /// <summary><c>GET /user/orgs</c> 응답에서 조직 이름들.</summary>
        public static List<string> ParseOrganizations(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return new List<string>();
            }
            OrganizationPage page = JsonUtility.FromJson<OrganizationPage>("{\"items\":" + json + "}");
            return (page?.items ?? Array.Empty<OwnerJson>()).Select(o => o?.login).Where(login => string.IsNullOrEmpty(login) == false).ToList();
        }
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
        private sealed class CreateRepositoryRequest
        {
            public string name;
            public bool @private;
        }

        [Serializable]
        private sealed class DefaultBranchRequest
        {
            public string default_branch;
        }

        [Serializable]
        private sealed class OrganizationPage
        {
            public OwnerJson[] items;
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
