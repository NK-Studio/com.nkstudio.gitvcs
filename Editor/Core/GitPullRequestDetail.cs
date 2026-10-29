using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Pull Request 하나의 자세한 정보(<c>GET /repos/{owner}/{repo}/pulls/{number}</c>). PR 상세 서랍(<see cref="PullRequestDetailDrawer"/>)이 그린다.
    /// </summary>
    public sealed class GitPullRequestDetail
    {
        public int Number;
        public string Title = string.Empty;
        public string Body = string.Empty;
        public string Url;
        public string Author;
        public bool IsOpen;
        public bool IsMerged;
        public bool IsDraft;
        public string HeadRef;
        public string HeadSha;
        public string HeadRepository;
        public string BaseRef;

        /// <summary>GitHub 가 병합 가능 여부를 아직 계산 중이면 null. 잠시 뒤 다시 읽으면 값이 생긴다.</summary>
        public bool? Mergeable;

        /// <summary>clean · dirty(충돌) · blocked(보호 규칙) · behind · unstable(검사 실패) · draft · unknown.</summary>
        public string MergeableState = string.Empty;

        public int Commits;
        public int Additions;
        public int Deletions;
        public int ChangedFiles;
        public int Comments;
        public string CreatedAt;
        public string[] Assignees = Array.Empty<string>();
        public string[] Reviewers = Array.Empty<string>();
        public string[] Labels = Array.Empty<string>();
    }

    /// <summary>PR 이 바꾼 파일 한 줄(<c>GET …/pulls/{number}/files</c>).</summary>
    public sealed class GitPullRequestFile
    {
        public string Path;
        public string PreviousPath;

        /// <summary>added · removed · modified · renamed · copied · changed · unchanged.</summary>
        public string Status;

        public int Additions;
        public int Deletions;
    }

    /// <summary>GitHub 의 세 가지 병합 방식(GitKraken PR 화면의 Merge 드롭다운과 같다).</summary>
    public enum GitPullRequestMergeMethod
    {
        Merge,
        Squash,
        Rebase,
    }

    /// <summary>지금 Merge 버튼을 눌러도 되는지와 그 까닭.</summary>
    public enum GitPullRequestReadiness
    {
        Ready,

        /// <summary>병합은 되지만 필수 아닌 검사가 실패했다.</summary>
        Unstable,

        /// <summary>GitHub 가 아직 계산 중이다.</summary>
        Checking,
        Conflicts,

        /// <summary>브랜치 보호 규칙(리뷰·필수 검사)이 막는다.</summary>
        Blocked,

        /// <summary>받는 브랜치보다 뒤처져 먼저 갱신해야 한다.</summary>
        Behind,
        Draft,
        Closed,
        Merged,
    }

    /// <summary>
    /// PR 상세 응답 읽기·병합 요청 만들기·병합 가능 판정. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitPullRequestDetails
    {
        private static readonly Regex MergeableNull = new("\"mergeable\"\\s*:\\s*null", RegexOptions.Compiled);

        public static GitPullRequestDetail Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            DetailJson item = JsonUtility.FromJson<DetailJson>(json);
            if (item == null || item.number <= 0)
            {
                return null;
            }
            return new GitPullRequestDetail
            {
                Number = item.number,
                Title = item.title ?? string.Empty,
                Body = (item.body ?? string.Empty).Replace("\r\n", "\n").Trim(),
                Url = item.html_url,
                Author = item.user?.login,
                IsOpen = string.Equals(item.state, "open", StringComparison.OrdinalIgnoreCase),
                IsMerged = item.merged,
                IsDraft = item.draft,
                HeadRef = item.head?.@ref,
                HeadSha = item.head?.sha,
                HeadRepository = string.IsNullOrEmpty(item.head?.repo?.full_name) ? null : item.head.repo.full_name,
                BaseRef = item.@base?.@ref,
                // JsonUtility 는 null 을 false 로 읽는다. "아직 계산 중" 과 "충돌" 을 가르려고 원문에서 null 을 따로 본다.
                Mergeable = MergeableNull.IsMatch(json) ? null : item.mergeable,
                MergeableState = item.mergeable_state ?? string.Empty,
                Commits = item.commits,
                Additions = item.additions,
                Deletions = item.deletions,
                ChangedFiles = item.changed_files,
                Comments = item.comments + item.review_comments,
                CreatedAt = item.created_at,
                Assignees = Logins(item.assignees),
                Reviewers = Logins(item.requested_reviewers),
                Labels = (item.labels ?? Array.Empty<LabelJson>()).Select(l => l?.name).Where(n => string.IsNullOrEmpty(n) == false).ToArray(),
            };
        }

        /// <summary><c>/pulls/{number}/files</c> 응답(JSON 배열).</summary>
        public static List<GitPullRequestFile> ParseFiles(string json)
        {
            List<GitPullRequestFile> files = new();
            if (string.IsNullOrWhiteSpace(json))
            {
                return files;
            }
            FilesJson page = JsonUtility.FromJson<FilesJson>("{\"items\":" + json + "}");
            foreach (FileJson item in page?.items ?? Array.Empty<FileJson>())
            {
                if (item == null || string.IsNullOrEmpty(item.filename))
                {
                    continue;
                }
                files.Add(new GitPullRequestFile
                {
                    Path = item.filename,
                    PreviousPath = string.IsNullOrEmpty(item.previous_filename) ? null : item.previous_filename,
                    Status = item.status ?? string.Empty,
                    Additions = item.additions,
                    Deletions = item.deletions,
                });
            }
            return files;
        }

        public static GitPullRequestReadiness Readiness(GitPullRequestDetail pr)
        {
            if (pr.IsMerged)
            {
                return GitPullRequestReadiness.Merged;
            }
            if (pr.IsOpen == false)
            {
                return GitPullRequestReadiness.Closed;
            }
            if (pr.IsDraft)
            {
                return GitPullRequestReadiness.Draft;
            }
            if (pr.Mergeable == null)
            {
                return GitPullRequestReadiness.Checking;
            }
            switch (pr.MergeableState.ToLowerInvariant())
            {
                case "dirty":
                    return GitPullRequestReadiness.Conflicts;
                case "blocked":
                    return GitPullRequestReadiness.Blocked;
                case "behind":
                    return GitPullRequestReadiness.Behind;
                case "unstable":
                    return GitPullRequestReadiness.Unstable;
                case "unknown":
                    return GitPullRequestReadiness.Checking;
            }
            return pr.Mergeable == true ? GitPullRequestReadiness.Ready : GitPullRequestReadiness.Conflicts;
        }

        public static bool CanMerge(GitPullRequestReadiness readiness)
        {
            return readiness is GitPullRequestReadiness.Ready or GitPullRequestReadiness.Unstable;
        }

        public static string MethodName(GitPullRequestMergeMethod method)
        {
            return method switch
            {
                GitPullRequestMergeMethod.Squash => "squash",
                GitPullRequestMergeMethod.Rebase => "rebase",
                _ => "merge",
            };
        }

        /// <summary>
        /// <c>PUT …/pulls/{number}/merge</c> 본문. <paramref name="headSha"/> 를 주면 그 사이 누가 더 푸시했을 때 GitHub 가 거절한다(본 것과 다른 것을 병합하지 않게).
        /// </summary>
        public static string MergeRequestJson(GitPullRequestMergeMethod method, string headSha)
        {
            MergeJson body = new() { merge_method = MethodName(method), sha = headSha ?? string.Empty };
            string json = JsonUtility.ToJson(body);
            return string.IsNullOrEmpty(headSha) ? json.Replace(",\"sha\":\"\"", string.Empty) : json;
        }

        private static string[] Logins(UserJson[] users)
        {
            return (users ?? Array.Empty<UserJson>()).Select(u => u?.login).Where(l => string.IsNullOrEmpty(l) == false).ToArray();
        }

        [Serializable]
        private sealed class DetailJson
        {
            public int number;
            public string title;
            public string body;
            public string state;
            public bool merged;
            public bool draft;
            public bool mergeable;
            public string mergeable_state;
            public string html_url;
            public string created_at;
            public int commits;
            public int additions;
            public int deletions;
            public int changed_files;
            public int comments;
            public int review_comments;
            public UserJson user;
            public UserJson[] assignees;
            public UserJson[] requested_reviewers;
            public LabelJson[] labels;
            public RefJson head;
            public RefJson @base;
        }

        [Serializable]
        private sealed class UserJson
        {
            public string login;
        }

        [Serializable]
        private sealed class LabelJson
        {
            public string name;
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

        [Serializable]
        private sealed class FilesJson
        {
            public FileJson[] items;
        }

        [Serializable]
        private sealed class FileJson
        {
            public string filename;
            public string previous_filename;
            public string status;
            public int additions;
            public int deletions;
        }

        [Serializable]
        private sealed class MergeJson
        {
            public string merge_method;
            public string sha;
        }
    }
}
