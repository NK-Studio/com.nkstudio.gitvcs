using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// Fork 의 "Interactive Rebase" 빠른 작업. 편집기를 띄우는 <c>git rebase -i</c> 대신 결과가 같은 명령으로 바꿨다.
    /// </summary>
    public enum GitRewriteKind
    {
        /// <summary>
        /// 메시지만 바꾼다.
        /// </summary>
        Reword,

        /// <summary>
        /// 부모 커밋과 합치고 두 메시지를 잇는다.
        /// </summary>
        Squash,

        /// <summary>
        /// 부모 커밋과 합치고 부모 메시지만 남긴다.
        /// </summary>
        Fixup,

        /// <summary>
        /// 커밋을 히스토리에서 뺀다.
        /// </summary>
        Drop,
    }

    public sealed class GitRewriteCommit
    {
        public string Hash;
        public string[] Parents;
        public string Tree;
        public string AuthorName;
        public string AuthorEmail;

        /// <summary>
        /// <c>--date=raw</c> 형식("1700000000 +0900"). GIT_AUTHOR_DATE 에 그대로 넣는다.
        /// </summary>
        public string AuthorDate;

        /// <summary>
        /// 저장된 메시지 그대로(<c>%B</c>).
        /// </summary>
        public string Message;
    }

    /// <summary>
    /// 현재 브랜치 히스토리의 커밋 하나를 고친다(메시지 바꾸기·부모와 합치기·빼기).
    ///
    /// 메시지 바꾸기·합치기는 뒤 커밋들의 파일 내용(tree)이 바뀌지 않는다. 그래서 rebase 로 다시 적용하지 않고
    /// <c>git commit-tree</c> 로 같은 tree·작성자·메시지에 부모만 바꾼 커밋을 차례로 새로 만든 뒤 <c>reset --soft</c> 로 브랜치를 옮긴다.
    /// 작업 트리·stage 를 건드리지 않고, 병합 커밋도 원래 모양 그대로 남으며, 충돌이 날 수 없다.
    /// 빼기는 뒤 커밋들의 내용이 바뀌므로 <c>git rebase --onto</c> 로 다시 적용한다. 충돌하면 rebase 를 취소해 원래대로 돌려놓는다.
    /// 어느 쪽이든 실행 취소 목록에 "HEAD 이동" 으로 남는다.
    /// </summary>
    public static class GitRewrite
    {
        private const char RecordSeparator = '\x1e';
        private const char FieldSeparator = '\x1f';
        private const string Format = "--format=%H%x1f%P%x1f%T%x1f%an%x1f%ae%x1f%ad%x1f%B%x1e";

        /// <summary>
        /// 이 커밋에 쓸 수 있는지. 안 되면 이유(번역 키), 되면 null. 커밋이 현재 브랜치 히스토리에 있는지는 실행할 때 확인한다.
        /// </summary>
        public static string Unsupported(GitRewriteKind kind, GitCommitInfo commit)
        {
            switch (kind)
            {
                case GitRewriteKind.Reword:
                    return null;
                case GitRewriteKind.Squash:
                case GitRewriteKind.Fixup:
                case GitRewriteKind.Drop:
                    if (commit.Parents.Length == 0)
                    {
                        return "rewrite.rootCommit";
                    }
                    return commit.IsMerge ? "rewrite.mergeCommit" : null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
            }
        }

        /// <summary>
        /// <see cref="Format"/> 출력을 커밋 목록으로 바꾼다.
        /// </summary>
        public static List<GitRewriteCommit> Parse(string output)
        {
            List<GitRewriteCommit> commits = new();
            foreach (string record in (output ?? string.Empty).Split(RecordSeparator))
            {
                string trimmed = record.TrimStart('\r', '\n');
                if (trimmed.Length == 0)
                {
                    continue;
                }
                string[] fields = trimmed.Split(new[] { FieldSeparator }, 7);
                if (fields.Length < 7)
                {
                    continue;
                }
                commits.Add(new GitRewriteCommit
                {
                    Hash = fields[0],
                    Parents = fields[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries),
                    Tree = fields[2],
                    AuthorName = fields[3],
                    AuthorEmail = fields[4],
                    AuthorDate = fields[5],
                    Message = fields[6],
                });
            }
            return commits;
        }

        /// <summary>
        /// 합치기의 메시지. squash 는 부모 메시지 뒤에 빈 줄을 두고 이 커밋 메시지를 잇고, fixup 은 부모 메시지만 쓴다.
        /// </summary>
        public static string CombinedMessage(GitRewriteKind kind, string parentMessage, string commitMessage)
        {
            string parent = (parentMessage ?? string.Empty).TrimEnd();
            return kind == GitRewriteKind.Squash
                ? parent + "\n\n" + (commitMessage ?? string.Empty).TrimEnd() + "\n"
                : parent + "\n";
        }

        /// <summary>
        /// 부모를 <paramref name="replaced"/> 대로 바꾼 부모 목록. 바뀐 것이 없으면 null.
        /// </summary>
        public static string[] RemapParents(string[] parents, IReadOnlyDictionary<string, string> replaced)
        {
            bool changed = false;
            string[] result = new string[parents.Length];
            for (int i = 0; i < parents.Length; i++)
            {
                if (replaced.TryGetValue(parents[i], out string replacement))
                {
                    result[i] = replacement;
                    changed = true;
                }
                else
                {
                    result[i] = parents[i];
                }
            }
            return changed ? result : null;
        }

        public static string[] CommitTree(string tree, IEnumerable<string> parents, string messageFile)
        {
            List<string> args = new() { "commit-tree", tree };
            foreach (string parent in parents)
            {
                args.Add("-p");
                args.Add(parent);
            }
            args.Add("-F");
            args.Add(messageFile);
            return args.ToArray();
        }

        /// <summary>
        /// <paramref name="commit"/> 를 빼고 그 뒤 커밋들을 부모 위로 다시 쌓는다. 병합 커밋 모양은 지키고(<c>--rebase-merges</c>),
        /// 커밋하지 않은 변경은 잠깐 stash 했다가 되돌린다(<c>--autostash</c>).
        /// </summary>
        public static string[] DropRebase(string commit)
        {
            return new[] { "rebase", "--rebase-merges", "--autostash", "--onto", commit + "^", commit };
        }

        public static async Task<GitResult> RunAsync(GitRewriteKind kind, string commit, string newMessage, string undoTitle)
        {
            string head = await RevParseAsync("HEAD");
            if (head == null)
            {
                return Fail("rewrite.noHead");
            }
            GitResult ancestor = await GitRepository.RunAsync("merge-base", "--is-ancestor", commit, "HEAD");
            if (ancestor.Success == false)
            {
                return Fail("rewrite.notInHistory");
            }

            GitRewriteCommit target = await ReadCommitAsync(commit);
            if (target == null)
            {
                return Fail("rewrite.readFailed");
            }

            if (kind == GitRewriteKind.Drop)
            {
                return await DropAsync(target, head, undoTitle);
            }

            string temp = Path.Combine(GitRepository.ProjectRoot, "Temp", "GitVersionControl", "Rewrite");
            Directory.CreateDirectory(temp);
            Dictionary<string, string> replaced = new();

            // 1) 고칠 커밋을 새로 만든다.
            string rewritten;
            if (kind == GitRewriteKind.Reword)
            {
                rewritten = await CommitTreeAsync(temp, target.Tree, target.Parents, EnsureNewline(newMessage), target);
            }
            else
            {
                if (target.Parents.Length != 1)
                {
                    return Fail("rewrite.mergeCommit");
                }
                GitRewriteCommit parent = await ReadCommitAsync(target.Parents[0]);
                if (parent == null)
                {
                    return Fail("rewrite.readFailed");
                }
                if (parent.Parents.Length > 1)
                {
                    return Fail("rewrite.parentIsMerge");
                }
                string message = newMessage == null ? CombinedMessage(kind, parent.Message, target.Message) : EnsureNewline(newMessage);
                // 합친 커밋은 부모 커밋의 작성자·날짜를 이어받는다(Fork·git rebase 와 같다).
                rewritten = await CommitTreeAsync(temp, target.Tree, parent.Parents, message, parent);
            }
            if (rewritten == null)
            {
                return Fail("rewrite.commitFailed");
            }
            replaced[target.Hash] = rewritten;

            // 2) 그 뒤 커밋들을 부모만 바꿔 차례로 새로 만든다(--reverse: 부모가 먼저).
            if (target.Hash != head)
            {
                GitResult log = await GitRepository.RunAsync("log", "--ancestry-path", "--topo-order", "--reverse", "--date=raw", Format, target.Hash + "..HEAD");
                if (log.Success == false)
                {
                    return log;
                }
                foreach (GitRewriteCommit descendant in Parse(log.Output))
                {
                    string[] parents = RemapParents(descendant.Parents, replaced);
                    if (parents == null)
                    {
                        continue;
                    }
                    string copy = await CommitTreeAsync(temp, descendant.Tree, parents, descendant.Message, descendant);
                    if (copy == null)
                    {
                        return Fail("rewrite.commitFailed");
                    }
                    replaced[descendant.Hash] = copy;
                }
            }
            if (replaced.TryGetValue(head, out string newHead) == false)
            {
                return Fail("rewrite.commitFailed");
            }

            // 3) 브랜치를 옮긴다. 새 HEAD 의 tree 는 원래 HEAD 와 같으므로 stage·작업 트리는 그대로다.
            GitResult reset = await GitRepository.RunAsync("reset", "--soft", newHead);
            if (reset.Success)
            {
                Record(undoTitle, head, newHead, "--soft");
            }
            return reset;
        }

        private static async Task<GitResult> DropAsync(GitRewriteCommit target, string head, string undoTitle)
        {
            if (target.Parents.Length != 1)
            {
                return Fail("rewrite.mergeCommit");
            }
            // 터미널에서 시작해 멈춘 rebase 가 있으면 git 이 새 rebase 를 거부하는데, 그 뒤 아래에서 "우리 것" 으로 알고 취소하면 사용자의 작업을 날린다.
            if (GitRepository.IsRebaseInProgress)
            {
                return Fail("rebase.alreadyInProgress");
            }
            string stashBefore = await GitUndo.StashTopAsync();
            GitResult result = target.Hash == head
                // 맨 위 커밋을 빼는 것은 부모로 옮기는 것과 같다. --keep 은 로컬 수정과 겹치면 거부한다.
                ? await GitRepository.RunAsync("reset", "--keep", target.Parents[0])
                : await GitCommand.RunAsync(GitRepository.RepoRoot, DropRebase(target.Hash), GitUndo.NoEditor);
            if (result.Success == false)
            {
                // 충돌로 멈췄으면 시작 전으로 되돌린다(autostash 한 변경도 돌아온다).
                if (GitRepository.IsRebaseInProgress)
                {
                    await GitRepository.RunAsync("rebase", "--abort");
                    return new GitResult(result.ExitCode, result.Output, GitText.Format("rewrite.dropConflict", result.Message));
                }
                return result;
            }
            string after = await RevParseAsync("HEAD");
            if (after != null && after != head)
            {
                Record(undoTitle, head, after, null);
            }
            // 성공이면 Error 는 비우고, 커밋하지 않은 변경을 되돌리다 충돌했을 때만 안내문을 담는다(GitUndo.RebaseAsync 와 같다).
            return await GitUndo.AutostashConflicted(stashBefore)
                ? new GitResult(0, result.Output, GitText.Get("rebase.autostashConflict"))
                : new GitResult(0, result.Output, string.Empty);
        }

        private static async Task<GitRewriteCommit> ReadCommitAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync("log", "-1", "--date=raw", Format, revision);
            return result.Success ? Parse(result.Output).FirstOrDefault() : null;
        }

        /// <returns>새 커밋 해시. 실패하면 null.</returns>
        private static async Task<string> CommitTreeAsync(string tempDirectory, string tree, IEnumerable<string> parents, string message, GitRewriteCommit author)
        {
            string messageFile = Path.Combine(tempDirectory, "message.txt");
            File.WriteAllText(messageFile, message ?? string.Empty, new UTF8Encoding(false));
            Dictionary<string, string> environment = new()
            {
                ["GIT_AUTHOR_NAME"] = author.AuthorName,
                ["GIT_AUTHOR_EMAIL"] = author.AuthorEmail,
                ["GIT_AUTHOR_DATE"] = author.AuthorDate,
            };
            GitResult result = await GitCommand.RunAsync(GitRepository.RepoRoot, CommitTree(tree, parents, messageFile), environment);
            return result.Success ? result.Output.Trim() : null;
        }

        private static async Task<string> RevParseAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync("rev-parse", "--verify", "--quiet", revision);
            return result.Success ? result.Output.Trim() : null;
        }

        private static void Record(string title, string before, string after, string resetMode)
        {
            GitUndo.Journal.Record(new GitUndoEntry
            {
                Kind = GitUndoKind.MoveHead,
                Title = title,
                HeadBefore = before,
                HeadAfter = after,
                ResetMode = resetMode,
            });
        }

        private static string EnsureNewline(string message)
        {
            string trimmed = (message ?? string.Empty).Replace("\r\n", "\n").TrimEnd();
            return trimmed + "\n";
        }

        private static GitResult Fail(string key)
        {
            return new GitResult(1, string.Empty, GitText.Get(key));
        }
    }
}
