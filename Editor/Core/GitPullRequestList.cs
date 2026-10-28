using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 열려 있는 Pull Request 하나(<c>GET /repos/{owner}/{repo}/pulls</c> 의 한 줄).
    /// </summary>
    public sealed class GitPullRequestInfo
    {
        public int Number;
        public string Title;
        public string Author;
        public string Url;
        public bool IsDraft;

        /// <summary>보내는 브랜치 이름("feature/login").</summary>
        public string HeadRef;

        /// <summary>보내는 저장소("me/Game"). 지워진 포크면 null.</summary>
        public string HeadRepository;

        public string HeadSha;

        /// <summary>받는 브랜치 이름("main").</summary>
        public string BaseRef;

        public string[] Assignees = Array.Empty<string>();
        public string[] Reviewers = Array.Empty<string>();
    }

    /// <summary>
    /// 사이드바 Pull Requests 구역이 그릴 상태. 읽는 중 · 로그인 필요 · GitHub 원격 없음 · 오류 · 목록 중 하나다.
    /// </summary>
    public sealed class GitPullRequestSet
    {
        public bool IsLoading;
        public bool NeedsSignIn;
        public bool NoGitHubRemote;
        public string Error;

        /// <summary>목록을 읽은 저장소("Org/Game").</summary>
        public string Repository;

        /// <summary>로그인한 계정 이름. "내 PR" 등을 가를 때 쓴다.</summary>
        public string Login;

        public List<GitPullRequestInfo> All = new();
    }

    /// <summary>
    /// Pull Request 목록 읽기와 묶음(내 PR · 나에게 배정됨 · 내 리뷰 대기 · 전체). 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitPullRequestList
    {
        public enum Group
        {
            Mine,
            AssignedToMe,
            AwaitingMyReview,
            All,
        }

        public static readonly Group[] Groups = (Group[])Enum.GetValues(typeof(Group));

        public static IEnumerable<GitPullRequestInfo> InGroup(GitPullRequestSet set, Group group)
        {
            bool Me(string login) => string.Equals(login, set.Login, StringComparison.OrdinalIgnoreCase);
            return group switch
            {
                Group.Mine => set.All.Where(pr => Me(pr.Author)),
                Group.AssignedToMe => set.All.Where(pr => pr.Assignees.Any(Me)),
                Group.AwaitingMyReview => set.All.Where(pr => pr.Reviewers.Any(Me)),
                _ => set.All,
            };
        }

        /// <summary>
        /// <c>/pulls</c> 응답(JSON 배열) 한 쪽. JsonUtility 는 맨 위 배열을 못 읽어 객체로 감싸 읽는다.
        /// </summary>
        public static List<GitPullRequestInfo> Parse(string json)
        {
            List<GitPullRequestInfo> pulls = new();
            if (string.IsNullOrWhiteSpace(json))
            {
                return pulls;
            }
            Page page = JsonUtility.FromJson<Page>("{\"items\":" + json + "}");
            foreach (PullJson item in page?.items ?? Array.Empty<PullJson>())
            {
                if (item == null || item.number <= 0)
                {
                    continue;
                }
                pulls.Add(new GitPullRequestInfo
                {
                    Number = item.number,
                    Title = item.title ?? string.Empty,
                    Author = item.user?.login,
                    Url = item.html_url,
                    IsDraft = item.draft,
                    HeadRef = item.head?.@ref,
                    HeadRepository = string.IsNullOrEmpty(item.head?.repo?.full_name) ? null : item.head.repo.full_name,
                    HeadSha = item.head?.sha,
                    BaseRef = item.@base?.@ref,
                    Assignees = Logins(item.assignees),
                    Reviewers = Logins(item.requested_reviewers),
                });
            }
            return pulls;
        }

        private static string[] Logins(UserJson[] users)
        {
            return (users ?? Array.Empty<UserJson>()).Select(u => u?.login).Where(l => string.IsNullOrEmpty(l) == false).ToArray();
        }

        [Serializable]
        private sealed class Page
        {
            public PullJson[] items;
        }

        [Serializable]
        private sealed class PullJson
        {
            public int number;
            public string title;
            public string html_url;
            public bool draft;
            public UserJson user;
            public UserJson[] assignees;
            public UserJson[] requested_reviewers;
            public RefJson head;
            public RefJson @base;
        }

        [Serializable]
        private sealed class UserJson
        {
            public string login;
        }

        [Serializable]
        private sealed class RefJson
        {
            public string @ref;
            public string sha;
            public RepoJson repo;
        }

        [Serializable]
        private sealed class RepoJson
        {
            public string full_name;
        }
    }
}
