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

        /// <summary>GraphQL 노드 id. Draft ↔ Ready 전환은 REST 가 없어 GraphQL 로 한다.</summary>
        public string NodeId;
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

    public enum GitPullRequestTimelineKind
    {
        /// <summary>대화 칸의 일반 코멘트(<c>issues/{n}/comments</c>).</summary>
        Comment,

        /// <summary>리뷰 제출(승인·변경 요청·코멘트).</summary>
        Review,

        /// <summary>코드 줄에 단 코멘트(<c>pulls/{n}/comments</c>).</summary>
        ReviewComment,
    }

    /// <summary>PR 대화 한 줄. 세 종류를 시간순으로 섞어 GitHub 의 Conversation 처럼 보인다.</summary>
    public sealed class GitPullRequestTimelineItem
    {
        public GitPullRequestTimelineKind Kind;
        public string Author;
        public string Body = string.Empty;

        /// <summary>리뷰 상태: APPROVED · CHANGES_REQUESTED · COMMENTED · DISMISSED. 그 밖에는 빈 글.</summary>
        public string ReviewState = string.Empty;

        /// <summary>코드 줄 코멘트의 파일과 줄(없으면 0).</summary>
        public string Path;
        public int Line;
        public DateTime Time;
        public string Url;
    }

    /// <summary>리뷰를 낼 때 고르는 결정(GitHub 의 Comment · Approve · Request changes).</summary>
    public enum GitPullRequestReviewEvent
    {
        Comment,
        Approve,
        RequestChanges,
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
                NodeId = item.node_id,
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

        // ── 대화 ──

        /// <summary>세 응답(코멘트·리뷰·코드 줄 코멘트)을 시간순 한 줄로 합친다. 본문 없는 "코멘트" 리뷰(줄 코멘트만 단 리뷰)는 뺀다.</summary>
        public static List<GitPullRequestTimelineItem> Timeline(string commentsJson, string reviewsJson, string reviewCommentsJson)
        {
            List<GitPullRequestTimelineItem> items = new();
            foreach (CommentJson c in ParseArray<CommentJson>(commentsJson))
            {
                items.Add(new GitPullRequestTimelineItem
                {
                    Kind = GitPullRequestTimelineKind.Comment, Author = c.user?.login, Body = Clean(c.body), Time = ParseTime(c.created_at), Url = c.html_url,
                });
            }
            foreach (ReviewJson r in ParseArray<ReviewJson>(reviewsJson))
            {
                string state = (r.state ?? string.Empty).ToUpperInvariant();
                if (state == "PENDING" || (state == "COMMENTED" && string.IsNullOrWhiteSpace(r.body)))
                {
                    continue;
                }
                items.Add(new GitPullRequestTimelineItem
                {
                    Kind = GitPullRequestTimelineKind.Review, Author = r.user?.login, Body = Clean(r.body), ReviewState = state, Time = ParseTime(r.submitted_at), Url = r.html_url,
                });
            }
            foreach (ReviewCommentJson c in ParseArray<ReviewCommentJson>(reviewCommentsJson))
            {
                items.Add(new GitPullRequestTimelineItem
                {
                    Kind = GitPullRequestTimelineKind.ReviewComment, Author = c.user?.login, Body = Clean(c.body), Path = c.path, Line = c.line > 0 ? c.line : c.original_line,
                    Time = ParseTime(c.created_at), Url = c.html_url,
                });
            }
            // 같은 시각이면 리뷰가 그 리뷰의 줄 코멘트보다 앞에 오게 종류 순서로 한 번 더 가른다.
            return items.OrderBy(i => i.Time).ThenBy(i => i.Kind == GitPullRequestTimelineKind.ReviewComment ? 1 : 0).ToList();
        }

        public static string CommentRequestJson(string body)
        {
            return JsonUtility.ToJson(new BodyJson { body = body ?? string.Empty });
        }

        public static string ReviewRequestJson(GitPullRequestReviewEvent decision, string body)
        {
            string name = decision switch
            {
                GitPullRequestReviewEvent.Approve => "APPROVE",
                GitPullRequestReviewEvent.RequestChanges => "REQUEST_CHANGES",
                _ => "COMMENT",
            };
            return JsonUtility.ToJson(new ReviewRequest { body = body ?? string.Empty, @event = name });
        }

        /// <summary>변경 요청·코멘트 리뷰는 GitHub 가 본문을 요구한다. 승인은 본문 없이 된다.</summary>
        public static bool ReviewNeedsBody(GitPullRequestReviewEvent decision)
        {
            return decision != GitPullRequestReviewEvent.Approve;
        }

        /// <summary>닫기·다시 열기(<c>PATCH …/pulls/{number}</c>) 본문.</summary>
        public static string StateRequestJson(bool open)
        {
            return JsonUtility.ToJson(new StateJson { state = open ? "open" : "closed" });
        }

        /// <summary>Draft ↔ Ready 전환 GraphQL 요청. REST API 에는 이 기능이 없다.</summary>
        public static string DraftMutationJson(string nodeId, bool toDraft)
        {
            string mutation = toDraft ? "convertPullRequestToDraft" : "markPullRequestReadyForReview";
            return JsonUtility.ToJson(new GraphQlJson
            {
                query = $"mutation($id: ID!) {{ {mutation}(input: {{ pullRequestId: $id }}) {{ pullRequest {{ isDraft }} }} }}",
                variables = new GraphQlVariables { id = nodeId ?? string.Empty },
            });
        }

        /// <summary>GraphQL 응답의 첫 오류 문구. 오류가 없으면 null(GraphQL 은 실패해도 HTTP 200 을 준다).</summary>
        public static string GraphQlError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            GraphQlResponse response = JsonUtility.FromJson<GraphQlResponse>(json);
            return response?.errors != null && response.errors.Length > 0 ? response.errors[0]?.message : null;
        }

        private static T[] ParseArray<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return System.Array.Empty<T>();
            }
            return JsonUtility.FromJson<ArrayJson<T>>("{\"items\":" + json + "}")?.items ?? System.Array.Empty<T>();
        }

        private static string Clean(string body)
        {
            return (body ?? string.Empty).Replace("\r\n", "\n").Trim();
        }

        public static DateTime ParseTime(string iso)
        {
            return DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out DateTime time)
                ? DateTime.SpecifyKind(time, DateTimeKind.Utc)
                : DateTime.MinValue;
        }

        [Serializable]
        private sealed class ArrayJson<T>
        {
            public T[] items;
        }

        [Serializable]
        private sealed class CommentJson
        {
            public string body;
            public string created_at;
            public string html_url;
            public UserJson user;
        }

        [Serializable]
        private sealed class ReviewJson
        {
            public string body;
            public string state;
            public string submitted_at;
            public string html_url;
            public UserJson user;
        }

        [Serializable]
        private sealed class ReviewCommentJson
        {
            public string body;
            public string path;
            public int line;
            public int original_line;
            public string created_at;
            public string html_url;
            public UserJson user;
        }

        [Serializable]
        private sealed class BodyJson
        {
            public string body;
        }

        [Serializable]
        private sealed class ReviewRequest
        {
            public string body;
            public string @event;
        }

        [Serializable]
        private sealed class StateJson
        {
            public string state;
        }

        [Serializable]
        private sealed class GraphQlJson
        {
            public string query;
            public GraphQlVariables variables;
        }

        [Serializable]
        private sealed class GraphQlVariables
        {
            public string id;
        }

        [Serializable]
        private sealed class GraphQlResponse
        {
            public GraphQlErrorJson[] errors;
        }

        [Serializable]
        private sealed class GraphQlErrorJson
        {
            public string message;
        }

        private static string[] Logins(UserJson[] users)
        {
            return (users ?? Array.Empty<UserJson>()).Select(u => u?.login).Where(l => string.IsNullOrEmpty(l) == false).ToArray();
        }

        [Serializable]
        private sealed class DetailJson
        {
            public int number;
            public string node_id;
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
