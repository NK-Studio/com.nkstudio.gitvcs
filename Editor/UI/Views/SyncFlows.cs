using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace GitVersionControl
{
    /// <summary>
    /// 원격과 주고받는 흐름(Pull·Push)과 stash 되돌리기. 실패하면 오류 글만 보이지 않고 다음에 할 일을 묻는다:
    /// <list type="bullet">
    /// <item>Pull 이 갈라져 앞으로 감기지 않으면 → 병합 / 리베이스</item>
    /// <item>Push 할 원격이 없으면 → 리모트 추가, 여럿이면 → 고르기</item>
    /// <item>Push 가 거절되면(원격에 내가 없는 커밋) → Pull</item>
    /// <item>새 브랜치를 처음 올렸으면 → Pull Request 만들기</item>
    /// <item>Pop 이 로컬 변경과 겹치면 → 로컬 변경을 버리고 Pop</item>
    /// </list>
    /// </summary>
    public sealed class SyncFlows
    {
        /// <summary>원격이 여럿일 때 지난번에 고른 원격(저장소마다).</summary>
        private const string LastRemoteKeyPrefix = "GitVersionControl.LastPushRemote.";

        private readonly GitServices _services;

        public SyncFlows(GitServices services)
        {
            _services = services;
        }

        private IDialogService Dialogs => _services.Dialogs;

        // ── Pull ──────────────────────────────────────────

        public async Task<GitResult> PullAsync()
        {
            if (Dialogs.SaveOpenWork() == false)
            {
                return GitResult.Cancelled;
            }
            GitPullMode? configured = await _services.Commands.ConfiguredPullModeAsync();
            GitPullMode mode = configured ?? GitPullMode.FastForwardOnly;
            string title = GitText.Get("op.pull");
            GitResult result = await _services.Runner.RunAsync(title, () => _services.Commands.PullAsync(mode), refreshAssets: true,
                showErrorDialog: mode != GitPullMode.FastForwardOnly);
            if (result.Success || mode != GitPullMode.FastForwardOnly)
            {
                return result;
            }
            if (await _services.Commands.IsDivergedFromUpstreamAsync() == false)
            {
                await ShowErrorAsync(title, result.Message);
                return result;
            }
            // 앞으로 감을 수 없다: 양쪽에 서로 없는 커밋이 있다. 어떻게 합칠지 묻는다.
            int choice = await Dialogs.ChooseAsync(GitText.Get("pull.divergedTitle"), GitText.Get("pull.divergedMessage"),
                GitText.Get("pull.merge"), GitText.Get("pull.rebase"), icon: ActionGlyphKind.Pull);
            if (choice == 1)
            {
                return result;
            }
            GitPullMode chosen = choice == 0 ? GitPullMode.Merge : GitPullMode.Rebase;
            return await _services.Runner.RunAsync(GitText.Get(chosen == GitPullMode.Merge ? "op.pullMerge" : "op.pullRebase"),
                () => _services.Commands.PullAsync(chosen), refreshAssets: true);
        }

        // ── Push ──────────────────────────────────────────

        /// <summary>
        /// 원격 하나를 고른다. 없으면 리모트 추가를 권하고 null, 하나면 그것, 여럿이면 묻는다(지난번 고른 것이 기본).
        /// </summary>
        public async Task<string> ChooseRemoteAsync(string title)
        {
            List<string> remotes = await _services.Commands.RemotesAsync();
            if (remotes.Count == 0)
            {
                await OfferAddRemoteAsync(title);
                return null;
            }
            if (remotes.Count == 1)
            {
                return remotes[0];
            }
            string key = LastRemoteKeyPrefix + (GitRepository.RepoRoot ?? string.Empty);
            string last = EditorPrefs.GetString(key, "origin");
            int selected = remotes.IndexOf(last);
            int picked = await Dialogs.PickAsync(title, GitText.Get("push.chooseRemote"), remotes, selected < 0 ? 0 : selected, ActionGlyphKind.Cloud);
            if (picked < 0)
            {
                return null;
            }
            EditorPrefs.SetString(key, remotes[picked]);
            return remotes[picked];
        }

        /// <summary>리모트가 없어서 못 한다고 알리고, 리모트 추가 서랍을 열지 묻는다.</summary>
        public async Task OfferAddRemoteAsync(string title)
        {
            if (_services.AddRemoteRequested == null)
            {
                await Dialogs.AlertAsync(title, GitText.Get("push.noRemote"), ActionGlyphKind.Cloud);
                return;
            }
            if (await Dialogs.ConfirmAsync(title, GitText.Get("push.noRemote"), GitText.Get("refs.addRemote"), icon: ActionGlyphKind.Cloud))
            {
                _services.AddRemoteRequested();
            }
        }

        /// <summary>
        /// 현재 브랜치를 올린다. upstream 이 없으면 원격을 고르고 추적을 건다.
        /// </summary>
        public async Task<GitResult> PushAsync(GitBranchInfo branch)
        {
            string title = GitText.Get("op.push");
            bool isNew = branch == null || string.IsNullOrEmpty(branch.Upstream);
            string remote = null;
            if (isNew)
            {
                remote = await ChooseRemoteAsync(title);
                if (remote == null)
                {
                    return GitResult.Cancelled;
                }
            }
            GitResult result = await _services.Runner.RunAsync(title, () => _services.Commands.PushAsync(branch, remote), refreshAssets: false, showErrorDialog: false);
            await AfterPushAsync(title, result, isNew ? branch?.Name : null);
            return result;
        }

        /// <summary>
        /// Push 결과를 마무리한다: 거절이면 Pull 을 권하고, 새 브랜치를 처음 올렸으면 Pull Request 를 권하고, 그 밖의 실패는 알린다.
        /// </summary>
        /// <param name="newBranch">처음 올린 브랜치 이름. 이미 추적하던 브랜치면 null.</param>
        public async Task AfterPushAsync(string title, GitResult result, string newBranch)
        {
            if (result.Success)
            {
                if (string.IsNullOrEmpty(newBranch) == false && _services.CreatePullRequestRequested != null
                    && await Dialogs.ConfirmAsync(GitText.Get("push.doneTitle"), GitText.Format("push.offerPullRequest", newBranch), GitText.Get("pr.create"),
                        cancel: GitText.Get("common.close"), icon: ActionGlyphKind.PullRequest))
                {
                    _services.CreatePullRequestRequested(new GitBranchRef { Name = newBranch, IsRemote = false });
                }
                return;
            }
            if (result.IsCancelled)
            {
                return;
            }
            if (GitErrorAdvice.Explain(result.Message, newBranch).Kind == GitErrorKind.RemoteAhead)
            {
                if (await Dialogs.ConfirmAsync(GitText.Format("runner.failed", title), GitText.Get("push.rejectedMessage"), GitText.Get("op.pull"), icon: ActionGlyphKind.Pull))
                {
                    await PullAsync();
                }
                return;
            }
            await ShowErrorAsync(title, result.Message);
        }

        // ── Stash Pop ─────────────────────────────────────

        public async Task<GitResult> StashPopAsync()
        {
            string title = GitText.Get("op.pop");
            // stash 에 든 새 파일(untracked)이 작업 폴더에 이미 있으면 git 은 추적 파일만 되돌린 채 멈춰 반쪽 상태가 된다.
            // 그래서 Pop 전에 겹치는 파일을 찾는다: 내용이 같으면 조용히 치우고, 다르면 버릴지 묻는다.
            (List<string> same, List<string> differ) = await UntrackedCollisionsAsync();
            if (differ.Count > 0 && await Dialogs.ConfirmAsync(GitText.Get("pop.blockedTitle"), GitText.Format("pop.blockedMessage", string.Join("\n", differ)),
                    GitText.Get("pop.discardAndPop"), danger: true, icon: ActionGlyphKind.Pop) == false)
            {
                return GitResult.Cancelled;
            }
            List<string> clear = same.Concat(differ).ToList();
            GitResult result = await _services.Runner.RunAsync(title, () =>
            {
                DeleteWorkingFiles(clear);
                return _services.Commands.StashPopAsync();
            }, refreshAssets: true, showErrorDialog: false);
            if (result.Success || result.IsCancelled)
            {
                return result;
            }
            List<string> blocking = GitErrorAdvice.OverwrittenPaths(result.Message);
            if (blocking.Count == 0)
            {
                await ShowErrorAsync(title, result.Message);
                return result;
            }
            // 에디터가 다시 저장한 파일(stash 한 뒤 Unity 가 메모리 상태로 쓴 에셋)과 겹치는 경우가 흔하다. 그 변경을 버리고 다시 한다.
            string list = string.Join("\n", blocking);
            if (await Dialogs.ConfirmAsync(GitText.Get("pop.blockedTitle"), GitText.Format("pop.blockedMessage", list), GitText.Get("pop.discardAndPop"),
                    danger: true, icon: ActionGlyphKind.Pop) == false)
            {
                return result;
            }
            return await _services.Runner.RunAsync(title, async () =>
            {
                GitResult discard = await GitRepository.RunAsync(new[] { "checkout", "--" }.Concat(blocking).ToArray());
                if (discard.Success == false)
                {
                    return discard;
                }
                // 멈춘 Pop 이 새 파일만 먼저 되돌려 놓았을 수 있다(stash 는 남는다). 다시 겹치지 않게 치운다 — 내용은 stash 에 그대로 있다.
                (List<string> restoredSame, List<string> restoredDiffer) = await UntrackedCollisionsAsync();
                DeleteWorkingFiles(restoredSame.Concat(restoredDiffer));
                return await _services.Commands.StashPopAsync();
            }, refreshAssets: true);
        }

        /// <summary>
        /// 맨 위 stash 의 새 파일(stash@{0}^3) 중 작업 폴더에 이미 있는 것. 내용이 stash 와 같은 것과 다른 것으로 나눈다.
        /// </summary>
        private static async Task<(List<string> same, List<string> differ)> UntrackedCollisionsAsync()
        {
            List<string> same = new();
            List<string> differ = new();
            GitResult tree = await GitRepository.RunAsync("ls-tree", "-r", "-z", "stash@{0}^3");
            if (tree.Success == false)
            {
                // 새 파일 없이 만든 stash 다.
                return (same, differ);
            }
            List<(string path, string blob)> existing = new();
            foreach (string entry in tree.Output.Split('\0'))
            {
                // "<mode> blob <hash>\t<path>"
                int tab = entry.IndexOf('\t');
                string[] head = tab > 0 ? entry.Substring(0, tab).Split(' ') : null;
                if (head == null || head.Length < 3)
                {
                    continue;
                }
                string path = entry.Substring(tab + 1);
                if (System.IO.File.Exists(System.IO.Path.Combine(GitRepository.RepoRoot, path)))
                {
                    existing.Add((path, head[2].Trim()));
                }
            }
            if (existing.Count == 0)
            {
                return (same, differ);
            }
            GitResult hashes = await GitRepository.RunAsync(new[] { "hash-object", "--" }.Concat(existing.Select(e => e.path)).ToArray());
            string[] lines = hashes.Success ? hashes.Output.Split('\n').Select(line => line.Trim()).Where(line => line.Length > 0).ToArray() : new string[0];
            for (int i = 0; i < existing.Count; i++)
            {
                bool identical = i < lines.Length && lines[i] == existing[i].blob;
                (identical ? same : differ).Add(existing[i].path);
            }
            return (same, differ);
        }

        private static void DeleteWorkingFiles(IEnumerable<string> repoPaths)
        {
            foreach (string path in repoPaths)
            {
                string full = System.IO.Path.Combine(GitRepository.RepoRoot, path);
                try
                {
                    System.IO.File.Delete(full);
                }
                catch (System.Exception e) when (e is System.IO.IOException || e is System.UnauthorizedAccessException)
                {
                    // 못 지운 파일은 git 이 Pop 에서 "already exists" 로 알린다.
                }
            }
        }

        private Task ShowErrorAsync(string title, string message)
        {
            return GitErrorDialog.ShowAsync(_services, GitText.Format("runner.failed", title), message);
        }
    }
}
