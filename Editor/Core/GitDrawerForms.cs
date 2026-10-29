using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 워크트리 서랍이 체크아웃할 수 있는 참조 하나.
    /// </summary>
    public sealed class GitRefChoice
    {
        public enum RefKind
        {
            Local,
            Remote,
            Tag,
        }

        /// <summary>"main", "origin/feature", "v1.0".</summary>
        public string Name;

        public RefKind Kind;
    }

    /// <summary>
    /// 워크트리 만들기 서랍(<see cref="AddWorktreeDrawer"/>)의 규칙. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitWorktreeForm
    {
        /// <summary>
        /// 고를 수 있는 참조: 로컬 브랜치 → 원격 브랜치(origin/HEAD 제외) → 태그.
        /// </summary>
        public static List<GitRefChoice> Choices(IEnumerable<GitBranchRef> branches, IEnumerable<GitTagRef> tags)
        {
            List<GitBranchRef> all = branches.ToList();
            IEnumerable<GitRefChoice> locals = all.Where(b => b.IsRemote == false).OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .Select(b => new GitRefChoice { Name = b.Name, Kind = GitRefChoice.RefKind.Local });
            IEnumerable<GitRefChoice> remotes = all.Where(b => b.IsRemote && b.Name.EndsWith("/HEAD", StringComparison.Ordinal) == false)
                .OrderBy(b => b.Name, StringComparer.OrdinalIgnoreCase)
                .Select(b => new GitRefChoice { Name = b.Name, Kind = GitRefChoice.RefKind.Remote });
            IEnumerable<GitRefChoice> tagChoices = tags.Select(t => new GitRefChoice { Name = t.Name, Kind = GitRefChoice.RefKind.Tag });
            return locals.Concat(remotes).Concat(tagChoices).ToList();
        }

        /// <summary>
        /// 원격 브랜치를 고르면 새 브랜치 칸에 채울 로컬 이름("origin/feature/a" → "feature/a"). 같은 이름 로컬이 이미 있거나 원격이 아니면 null.
        /// </summary>
        public static string SuggestBranch(GitRefChoice choice, ICollection<string> localNames)
        {
            if (choice == null || choice.Kind != GitRefChoice.RefKind.Remote)
            {
                return null;
            }
            int slash = choice.Name.IndexOf('/');
            string local = slash < 0 ? choice.Name : choice.Name.Substring(slash + 1);
            return localNames.Any(n => string.Equals(n, local, StringComparison.OrdinalIgnoreCase)) ? null : local;
        }

        /// <summary>
        /// 처음 채울 폴더: 저장소 폴더 옆에 "&lt;저장소 이름&gt;-&lt;브랜치&gt;"("E:/Game" + "feature/a" → "E:/Game-feature-a").
        /// </summary>
        public static string SuggestPath(string repoRoot, string branchOrRef)
        {
            if (string.IsNullOrEmpty(repoRoot) || string.IsNullOrWhiteSpace(branchOrRef))
            {
                return string.Empty;
            }
            string root = repoRoot.Replace('\\', '/').TrimEnd('/');
            int slash = root.LastIndexOf('/');
            string parent = slash < 0 ? string.Empty : root.Substring(0, slash);
            string name = slash < 0 ? root : root.Substring(slash + 1);
            string suffix = new(branchOrRef.Trim().Select(c => c == '/' || c == '\\' || char.IsWhiteSpace(c) || System.IO.Path.GetInvalidFileNameChars().Contains(c) ? '-' : c).ToArray());
            return (parent.Length > 0 ? parent + "/" : string.Empty) + name + "-" + suffix;
        }

        /// <summary>
        /// 새 브랜치 없이 로컬 브랜치를 그대로 쓸 때, 그 브랜치를 이미 체크아웃한 워크트리 경로. 없으면 null(git 이 거부할 일을 미리 알린다).
        /// </summary>
        public static string CheckedOutIn(GitRefChoice choice, string newBranch, IEnumerable<GitWorktreeRef> worktrees)
        {
            if (choice == null || choice.Kind != GitRefChoice.RefKind.Local || string.IsNullOrWhiteSpace(newBranch) == false)
            {
                return null;
            }
            return worktrees.FirstOrDefault(w => w.Branch == choice.Name)?.Path;
        }
    }

    /// <summary>
    /// 서브모듈 추가 서랍(<see cref="AddSubmoduleDrawer"/>)의 규칙. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitSubmoduleForm
    {
        public enum PathProblem
        {
            None,
            Empty,
            Absolute,
            OutsideRepository,
            Exists,
        }

        /// <summary>
        /// 주소의 마지막 이름에서 ".git" 을 뗀 것("https://github.com/a/Core.git" → "Core", "git@host:a/b" → "b").
        /// </summary>
        public static string PathFromUrl(string url)
        {
            string trimmed = (url ?? string.Empty).Trim().TrimEnd('/', '\\');
            int cut = Math.Max(Math.Max(trimmed.LastIndexOf('/'), trimmed.LastIndexOf('\\')), trimmed.LastIndexOf(':'));
            string name = cut < 0 ? trimmed : trimmed.Substring(cut + 1);
            return name.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }

        /// <param name="isTaken">저장소 안에 그 경로가 이미 있는지(파일 또는 비어 있지 않은 폴더). 부르는 쪽이 디스크를 본다.</param>
        public static PathProblem CheckPath(string path, Func<string, bool> isTaken)
        {
            string value = path?.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(value))
            {
                return PathProblem.Empty;
            }
            if (value.StartsWith("/", StringComparison.Ordinal) || (value.Length > 1 && value[1] == ':'))
            {
                return PathProblem.Absolute;
            }
            if (value.Split('/').Any(part => part == ".."))
            {
                return PathProblem.OutsideRepository;
            }
            return isTaken != null && isTaken(value) ? PathProblem.Exists : PathProblem.None;
        }
    }

    /// <summary>
    /// GitHub 원격 하나(Pull Request 서랍의 From · To 저장소).
    /// </summary>
    public sealed class GitHubRemoteRepo
    {
        public string Remote;
        public string Owner;
        public string Repository;

        public string FullName => Owner + "/" + Repository;
    }

    /// <summary>
    /// Pull Request 만들기 서랍(<see cref="CreatePullRequestDrawer"/>)의 규칙: 고를 저장소·브랜치, 기본값, GitHub API 요청·응답. 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitPullRequestForm
    {
        /// <summary>
        /// GitHub 를 가리키는 원격들(원격 이름 순, origin 먼저).
        /// </summary>
        /// <summary>브랜치 이름으로 만든 PR 제목: 마지막 '/' 뒤, '-'·'_' 는 띄어쓰기("origin/feature/pr-menu" → "pr menu").</summary>
        public static string TitleFromBranch(string branch)
        {
            string name = branch ?? string.Empty;
            int slash = name.LastIndexOf('/');
            return (slash >= 0 ? name.Substring(slash + 1) : name).Replace('-', ' ').Replace('_', ' ').Trim();
        }

        public static List<GitHubRemoteRepo> GitHubRemotes(IDictionary<string, string> remoteUrls)
        {
            List<GitHubRemoteRepo> repos = new();
            foreach (KeyValuePair<string, string> pair in remoteUrls)
            {
                if (GitAvatarSources.TryParseGitHubRemote(pair.Value, out string owner, out string repository))
                {
                    repos.Add(new GitHubRemoteRepo { Remote = pair.Key, Owner = owner, Repository = repository });
                }
            }
            return repos.OrderBy(r => r.Remote == "origin" ? 0 : 1).ThenBy(r => r.Remote, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// 그 원격의 브랜치 이름(원격 이름 뗀 것, HEAD 제외).
        /// </summary>
        public static List<string> RemoteBranches(IEnumerable<GitBranchRef> branches, string remote)
        {
            string prefix = remote + "/";
            return branches.Where(b => b.IsRemote && b.Name.StartsWith(prefix, StringComparison.Ordinal) && b.Name != prefix + "HEAD")
                .Select(b => b.Name.Substring(prefix.Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// 대상 저장소의 기본 브랜치 짐작: origin/HEAD 가 가리키는 커밋과 같은 브랜치 → main → master → develop → 첫 브랜치.
        /// </summary>
        public static string DefaultBase(IEnumerable<GitBranchRef> branches, string remote)
        {
            List<GitBranchRef> all = branches.ToList();
            List<string> names = RemoteBranches(all, remote);
            string headHash = all.FirstOrDefault(b => b.IsRemote && b.Name == remote + "/HEAD")?.Hash;
            if (string.IsNullOrEmpty(headHash) == false)
            {
                string pointed = all.Where(b => b.IsRemote && b.Hash == headHash && b.Name != remote + "/HEAD" && b.Name.StartsWith(remote + "/", StringComparison.Ordinal))
                    .Select(b => b.Name.Substring(remote.Length + 1))
                    .OrderBy(n => n == "main" || n == "master" ? 0 : 1)
                    .FirstOrDefault();
                if (pointed != null)
                {
                    return pointed;
                }
            }
            foreach (string candidate in new[] { "main", "master", "develop" })
            {
                if (names.Contains(candidate))
                {
                    return candidate;
                }
            }
            return names.FirstOrDefault();
        }

        /// <summary>
        /// API 의 head: 같은 저장소면 브랜치 이름만, 포크에서 보내면 "owner:branch".
        /// </summary>
        public static string HeadSpec(GitHubRemoteRepo from, GitHubRemoteRepo to, string branch)
        {
            return string.Equals(from.Owner, to.Owner, StringComparison.OrdinalIgnoreCase) ? branch : from.Owner + ":" + branch;
        }

        public static string RequestJson(string title, string body, string head, string baseBranch, bool draft)
        {
            return JsonUtility.ToJson(new CreateRequest { title = title, body = body ?? string.Empty, head = head, @base = baseBranch, draft = draft });
        }

        /// <summary>
        /// 만든 Pull Request 의 번호와 주소. 읽지 못하면 (0, null).
        /// </summary>
        public static (int number, string url) ParseCreated(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return (0, null);
            }
            CreatedResponse response = JsonUtility.FromJson<CreatedResponse>(json);
            return response == null ? (0, null) : (response.number, response.html_url);
        }

        /// <summary>
        /// 오류 응답의 사람이 읽을 문구: errors[].message 가 있으면 그것들, 없으면 message. 읽지 못하면 null.
        /// </summary>
        public static string ParseError(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }
            ErrorResponse response;
            try
            {
                response = JsonUtility.FromJson<ErrorResponse>(json);
            }
            catch (ArgumentException)
            {
                return null;
            }
            List<string> details = (response?.errors ?? Array.Empty<ErrorDetail>()).Select(e => e?.message).Where(m => string.IsNullOrWhiteSpace(m) == false).ToList();
            return details.Count > 0 ? string.Join("\n", details) : string.IsNullOrWhiteSpace(response?.message) ? null : response.message;
        }

        [Serializable]
        private sealed class CreateRequest
        {
            public string title;
            public string body;
            public string head;
            public string @base;
            public bool draft;
        }

        [Serializable]
        private sealed class CreatedResponse
        {
            public int number;
            public string html_url;
        }

        [Serializable]
        private sealed class ErrorResponse
        {
            public string message;
            public ErrorDetail[] errors;
        }

        [Serializable]
        private sealed class ErrorDetail
        {
            public string message;
        }
    }
}
