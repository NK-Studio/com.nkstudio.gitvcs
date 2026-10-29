using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 사이드바 Pull Requests 구역의 묶음 하나(GitKraken 의 PR 필터). 이름과 GitHub 검색 문법 조건(<c>author:@me draft:false</c>)을 갖는다.
    /// </summary>
    [Serializable]
    public sealed class GitPullRequestFilter
    {
        public string Name;
        public string Query;

        /// <summary>기본 묶음의 번역 키(사용자가 이름을 바꾸지 않았으면 언어를 따라간다). 직접 만든 묶음은 null.</summary>
        public string NameKey;

        public string DisplayName => string.IsNullOrEmpty(NameKey) ? Name : GitText.Get(NameKey);
    }

    /// <summary>
    /// PR 필터 목록(저장·순서 바꾸기)과 조건 평가. 평가는 순수 함수라 테스트로 확인한다.
    ///
    /// 조건은 두 가지로 푼다:
    /// <list type="bullet">
    /// <item>이 패키지가 받은 PR 정보로 알 수 있는 것(author·assignee·involves·review-requested·base·head·draft·label·no:assignee·no:label·글자)은 바로 거른다.</item>
    /// <item>그 밖(review:·reviewed-by:·status:·created:·updated:·milestone 등)은 GitHub 검색 API 의 결과 번호로 거른다(<see cref="NeedsSearch"/>).</item>
    /// </list>
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitPullRequestFilters
    {
        private const string PrefsKey = "GitVersionControl.PullRequestFilters";

        private static readonly HashSet<string> LocalQualifiers = new(StringComparer.OrdinalIgnoreCase)
        {
            "author", "assignee", "involves", "review-requested", "base", "head", "draft", "label", "no", "is",
        };

        public static event Action Changed;

        public static List<GitPullRequestFilter> Defaults()
        {
            return new List<GitPullRequestFilter>
            {
                new() { NameKey = "prList.groupMine", Query = "author:@me" },
                new() { NameKey = "prList.groupAssignedToMe", Query = "assignee:@me" },
                new() { NameKey = "prList.groupAwaitingMyReview", Query = "review-requested:@me" },
                new() { NameKey = "prList.groupAll", Query = string.Empty },
            };
        }

        [Serializable]
        private sealed class Store
        {
            public List<GitPullRequestFilter> Items = new();
        }

        public static List<GitPullRequestFilter> Load()
        {
            string json = EditorPrefs.GetString(PrefsKey, string.Empty);
            if (string.IsNullOrEmpty(json) == false)
            {
                try
                {
                    Store store = JsonUtility.FromJson<Store>(json);
                    if (store?.Items != null)
                    {
                        return store.Items;
                    }
                }
                catch (ArgumentException)
                {
                    // 망가진 값이면 기본으로 돌아간다.
                }
            }
            return Defaults();
        }

        public static void Save(List<GitPullRequestFilter> filters)
        {
            EditorPrefs.SetString(PrefsKey, JsonUtility.ToJson(new Store { Items = filters }));
            Changed?.Invoke();
        }

        public static void Add(GitPullRequestFilter filter)
        {
            List<GitPullRequestFilter> filters = Load();
            filters.Add(filter);
            Save(filters);
        }

        public static void Replace(int index, GitPullRequestFilter filter)
        {
            List<GitPullRequestFilter> filters = Load();
            if (index >= 0 && index < filters.Count)
            {
                filters[index] = filter;
                Save(filters);
            }
        }

        /// <summary>
        /// 편집하며 기본 묶음의 이름을 그대로 두었는가(그러면 번역 키를 남긴다). 저장(JsonUtility)을 거치면 null 키가 "" 가 되므로 빈 키도 직접 만든 묶음이다.
        /// </summary>
        public static bool KeepsDefaultName(GitPullRequestFilter old, string name)
        {
            return old != null && string.IsNullOrEmpty(old.NameKey) == false && name == old.DisplayName;
        }

        public static void Remove(int index)
        {
            List<GitPullRequestFilter> filters = Load();
            if (index >= 0 && index < filters.Count)
            {
                filters.RemoveAt(index);
                Save(filters);
            }
        }

        /// <param name="delta">-1 이면 위로, +1 이면 아래로.</param>
        public static void Move(int index, int delta)
        {
            List<GitPullRequestFilter> filters = Load();
            int target = index + delta;
            if (index < 0 || index >= filters.Count || target < 0 || target >= filters.Count)
            {
                return;
            }
            (filters[index], filters[target]) = (filters[target], filters[index]);
            Save(filters);
        }

        // ── 조건 풀기 ──

        public readonly struct Term
        {
            public readonly string Key;
            public readonly string Value;

            public Term(string key, string value)
            {
                Key = key;
                Value = value;
            }
        }

        /// <summary>
        /// 조건을 조각으로 나눈다. <c>label:"needs review"</c> 처럼 따옴표 안의 띄어쓰기는 한 값이다. 조건 없는 낱말은 Key 가 null.
        /// </summary>
        public static List<Term> Parse(string query)
        {
            List<Term> terms = new();
            string text = query ?? string.Empty;
            int i = 0;
            while (i < text.Length)
            {
                while (i < text.Length && char.IsWhiteSpace(text[i]))
                {
                    i++;
                }
                if (i >= text.Length)
                {
                    break;
                }
                int start = i;
                bool quoted = false;
                while (i < text.Length && (quoted || char.IsWhiteSpace(text[i]) == false))
                {
                    if (text[i] == '"')
                    {
                        quoted = quoted == false;
                    }
                    i++;
                }
                string token = text.Substring(start, i - start);
                int colon = token.IndexOf(':');
                if (colon > 0)
                {
                    terms.Add(new Term(token.Substring(0, colon).ToLowerInvariant(), token.Substring(colon + 1).Trim('"')));
                }
                else
                {
                    terms.Add(new Term(null, token.Trim('"')));
                }
            }
            return terms;
        }

        /// <summary>이 패키지가 받은 정보만으로 풀 수 없어 GitHub 검색이 필요한 조건인지.</summary>
        public static bool NeedsSearch(string query)
        {
            foreach (Term term in Parse(query))
            {
                if (term.Key == null)
                {
                    continue;
                }
                if (LocalQualifiers.Contains(term.Key) == false)
                {
                    return true;
                }
                if (term.Key == "no" && term.Value is not ("assignee" or "label"))
                {
                    return true;
                }
                if (term.Key == "is" && term.Value is not ("pr" or "open" or "draft"))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// PR 하나가 조건에 맞는지(로컬로 풀 수 있는 조건만). <c>@me</c> 는 <paramref name="login"/>.
        /// </summary>
        public static bool Matches(GitPullRequestInfo pr, string query, string login)
        {
            foreach (Term term in Parse(query))
            {
                if (TermMatches(pr, term, login) == false)
                {
                    return false;
                }
            }
            return true;
        }

        private static bool TermMatches(GitPullRequestInfo pr, Term term, string login)
        {
            string value = term.Value == "@me" ? login : term.Value;
            bool Same(string a) => string.Equals(a, value, StringComparison.OrdinalIgnoreCase);
            switch (term.Key)
            {
                case null:
                    return Contains(pr.Title, value) || Contains(pr.HeadRef, value) || ("#" + pr.Number) == value || pr.Number.ToString() == value;
                case "author":
                    return Same(pr.Author);
                case "assignee":
                    return pr.Assignees.Any(Same);
                case "review-requested":
                    return pr.Reviewers.Any(Same);
                case "involves":
                    return Same(pr.Author) || pr.Assignees.Any(Same) || pr.Reviewers.Any(Same);
                case "base":
                    return Same(pr.BaseRef);
                case "head":
                    return Same(pr.HeadRef);
                case "draft":
                    return pr.IsDraft == string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                case "label":
                    return pr.Labels.Any(Same);
                case "no":
                    return value switch
                    {
                        "assignee" => pr.Assignees.Length == 0,
                        "label" => pr.Labels.Length == 0,
                        _ => true,
                    };
                case "is":
                    return value != "draft" || pr.IsDraft;
                default:
                    // 검색 API 로 거르는 조건. 여기서는 막지 않는다.
                    return true;
            }
        }

        private static bool Contains(string text, string needle)
        {
            return string.IsNullOrEmpty(needle) || (text ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 묶음에 들어갈 PR. 검색이 필요한 조건이면 <paramref name="searchResults"/>(조건 → 맞는 번호)에 있는 것만.
        /// 아직 검색 결과가 없으면 로컬로 풀 수 있는 조건만 적용한다.
        /// </summary>
        public static List<GitPullRequestInfo> InFilter(GitPullRequestSet set, GitPullRequestFilter filter)
        {
            IEnumerable<GitPullRequestInfo> pulls = set.All.Where(pr => Matches(pr, filter.Query, set.Login));
            if (NeedsSearch(filter.Query) && set.SearchResults.TryGetValue(filter.Query ?? string.Empty, out HashSet<int> numbers))
            {
                pulls = pulls.Where(pr => numbers.Contains(pr.Number));
            }
            return pulls.ToList();
        }
    }
}
