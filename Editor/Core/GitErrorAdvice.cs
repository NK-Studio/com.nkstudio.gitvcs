using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitVersionControl
{
    /// <summary>
    /// git 오류 글이 어떤 문제인지. <see cref="GitErrorAdvice.Classify"/> 가 정한다.
    /// </summary>
    public enum GitErrorKind
    {
        Unknown,
        NoRemote,
        NoUpstream,
        RemoteAhead,
        Network,
        Auth,
        SshKey,
        LocalChanges,
        UntrackedFiles,
        Conflict,
        NoIdentity,
        FileTooLarge,
        ProtectedBranch,
        LockFile,
    }

    /// <summary>
    /// git 오류 글을 짧은 해설(원인 한 줄·해결 한 줄)로 바꾼다. 오류 팝업의 "설명" 이 쓴다(<see cref="GitErrorDialog"/>).
    /// <see cref="GitAuth.IsAuthFailure"/> 처럼 소문자로 바꾼 글에 든 문구로 알아본다. 겹치는 문구가 있어 순서가 중요하다
    /// (보호된 브랜치·큰 파일 거절도 "[remote rejected]" 이고, 403 도 "unable to access" 다).
    /// </summary>
    public sealed class GitErrorAdvice
    {
        /// <summary>해설에 보일 파일 이름의 최대 개수. 나머지는 "외 N개" 로 줄인다.</summary>
        private const int MaxFiles = 5;

        private static readonly Regex RemoteName = new(@"'(?<remote>[^']+)' does not appear to be a git repository", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex SetUpstream = new(@"git push --set-upstream (?<remote>\S+) (?<branch>\S+)", RegexOptions.Compiled);
        private static readonly Regex NoUpstreamBranch = new(@"current branch (?<branch>\S+) has no upstream branch", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex RejectedBranch = new(@"\[rejected\]\s+(?<branch>\S+)\s+->", RegexOptions.Compiled);
        private static readonly Regex ConflictFile = new(@"Merge conflict in (?<file>.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex LargeFile = new(@"File (?<file>.+?) is [\d.]+ ?MB; this exceeds", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static readonly Regex LockPath = new(@"'(?<path>[^']*\.lock)'", RegexOptions.Compiled);

        public GitErrorKind Kind { get; private set; }

        /// <summary>무엇이 문제인지 한 줄.</summary>
        public string Cause { get; private set; }

        /// <summary>어떻게 고치는지 한 줄.</summary>
        public string Fix { get; private set; }

        /// <summary>오류 글에서 뽑은 원격 이름(없으면 null).</summary>
        public string Remote { get; private set; }

        /// <summary>오류 글에서 뽑은 브랜치 이름(없으면 null).</summary>
        public string Branch { get; private set; }

        /// <summary>오류 글에 나온 파일들(덮어써질 파일·충돌 파일·큰 파일).</summary>
        public IReadOnlyList<string> Files { get; private set; }

        /// <summary>
        /// 오류 글이 어떤 문제인지 정한다. 알아볼 수 없으면 <see cref="GitErrorKind.Unknown"/>.
        /// </summary>
        public static GitErrorKind Classify(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return GitErrorKind.Unknown;
            }
            string lower = message.ToLowerInvariant();
            if (lower.Contains("permission denied (publickey)"))
            {
                return GitErrorKind.SshKey;
            }
            if (lower.Contains("does not appear to be a git repository") || lower.Contains("no configured push destination"))
            {
                return GitErrorKind.NoRemote;
            }
            if (lower.Contains("has no upstream branch"))
            {
                return GitErrorKind.NoUpstream;
            }
            if (lower.Contains("gh006") || lower.Contains("protected branch"))
            {
                return GitErrorKind.ProtectedBranch;
            }
            if (lower.Contains("gh001") || lower.Contains("exceeds github's file size limit"))
            {
                return GitErrorKind.FileTooLarge;
            }
            if (lower.Contains("[rejected]") && (lower.Contains("fetch first") || lower.Contains("non-fast-forward")))
            {
                return GitErrorKind.RemoteAhead;
            }
            if (GitAuth.IsAuthFailure(message))
            {
                return GitErrorKind.Auth;
            }
            if (lower.Contains("could not resolve host") || lower.Contains("failed to connect") || lower.Contains("timed out")
                || lower.Contains("unable to access") || lower.Contains("could not connect"))
            {
                return GitErrorKind.Network;
            }
            if (lower.Contains("please tell me who you are") || lower.Contains("unable to auto-detect email address"))
            {
                return GitErrorKind.NoIdentity;
            }
            if (lower.Contains("untracked working tree files would be overwritten"))
            {
                return GitErrorKind.UntrackedFiles;
            }
            if (lower.Contains("local changes to the following files would be overwritten"))
            {
                return GitErrorKind.LocalChanges;
            }
            if (lower.Contains(".lock': file exists"))
            {
                return GitErrorKind.LockFile;
            }
            if (lower.Contains("conflict (") || lower.Contains("automatic merge failed") || lower.Contains("could not apply"))
            {
                return GitErrorKind.Conflict;
            }
            return GitErrorKind.Unknown;
        }

        /// <summary>
        /// 오류 글의 해설. 알아볼 수 없어도 null 이 아니다(<see cref="GitErrorKind.Unknown"/> 의 일반 안내).
        /// </summary>
        /// <param name="currentBranch">지금 브랜치. 오류 글에 브랜치 이름이 없을 때 쓴다.</param>
        public static GitErrorAdvice Explain(string message, string currentBranch = null)
        {
            message ??= string.Empty;
            GitErrorAdvice advice = new() { Kind = Classify(message), Files = FindFiles(message) };
            string key = "error." + advice.Kind;
            switch (advice.Kind)
            {
                case GitErrorKind.NoRemote:
                    advice.Remote = MatchGroup(RemoteName, message, "remote") ?? "origin";
                    advice.Cause = GitText.Format(key + ".cause", advice.Remote);
                    break;
                case GitErrorKind.NoUpstream:
                    advice.Remote = MatchGroup(SetUpstream, message, "remote") ?? "origin";
                    advice.Branch = MatchGroup(SetUpstream, message, "branch") ?? MatchGroup(NoUpstreamBranch, message, "branch") ?? currentBranch;
                    advice.Cause = GitText.Format(key + ".cause", advice.Branch);
                    break;
                case GitErrorKind.RemoteAhead:
                    advice.Branch = MatchGroup(RejectedBranch, message, "branch") ?? currentBranch;
                    advice.Cause = GitText.Format(key + ".cause", advice.Branch);
                    break;
                case GitErrorKind.LocalChanges:
                case GitErrorKind.UntrackedFiles:
                case GitErrorKind.Conflict:
                case GitErrorKind.FileTooLarge:
                    advice.Cause = GitText.Format(key + ".cause", FileList(advice.Files));
                    break;
                case GitErrorKind.LockFile:
                    advice.Cause = GitText.Format(key + ".cause", MatchGroup(LockPath, message, "path") ?? "index.lock");
                    break;
                default:
                    advice.Cause = GitText.Get(key + ".cause");
                    break;
            }
            advice.Fix = GitText.Get(key + ".fix");
            return advice;
        }

        private static string MatchGroup(Regex regex, string message, string group)
        {
            Match match = regex.Match(message);
            return match.Success ? match.Groups[group].Value.Trim() : null;
        }

        /// <summary>
        /// 오류 글에 나온 파일: "would be overwritten by …:" 아래 탭으로 들인 줄, "Merge conflict in …", 큰 파일 이름.
        /// </summary>
        private static List<string> FindFiles(string message)
        {
            List<string> files = new();
            bool inList = false;
            foreach (string rawLine in message.Split('\n'))
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Contains("would be overwritten by"))
                {
                    inList = true;
                    continue;
                }
                if (inList)
                {
                    if (line.StartsWith("\t") || line.StartsWith("    "))
                    {
                        files.Add(line.Trim());
                        continue;
                    }
                    inList = false;
                }
            }
            files.AddRange(ConflictFile.Matches(message).Select(m => m.Groups["file"].Value.Trim()));
            files.AddRange(LargeFile.Matches(message).Select(m => m.Groups["file"].Value.Trim()));
            return files.Distinct().ToList();
        }

        private static string FileList(IReadOnlyList<string> files)
        {
            if (files.Count == 0)
            {
                return string.Empty;
            }
            string shown = string.Join(", ", files.Take(MaxFiles));
            return files.Count > MaxFiles ? GitText.Format("error.moreFiles", shown, files.Count - MaxFiles) : shown;
        }
    }
}
