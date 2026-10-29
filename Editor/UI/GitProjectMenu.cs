using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;

namespace GitVersionControl
{
    /// <summary>
    /// Project 창 우클릭(Assets 메뉴)의 Git 묶음. Git 창의 변경 목록과 같은 작업을 같은 길로 한다:
    /// Stage(충돌 표시·별도 저장소 확인, 열린 씬 저장) · Unstage · 변경 버리기(확인 카드) · Diff(이미지는 이미지 diff) · Git 창에서 보기 · 히스토리 · Blame · .gitignore 에 추가.
    /// 고른 에셋의 .meta, 폴더 아래 전부와 폴더의 .meta 를 한 묶음으로 다룬다(<see cref="GitProjectSelection"/>).
    /// 메뉴를 열 때 도는 검사는 이미 읽어 둔 상태만 본다(git 을 부르지 않는다). 메뉴 글은 Unity 메뉴처럼 영어로 고정이다(MenuItem 은 컴파일 때 정해진다).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class GitProjectMenu
    {
        private const string Root = "Assets/Git/";

        // 우선순위가 11 이상 벌어지면 Unity 가 그 사이에 구분선을 넣는다.
        private const int StagePriority = 1000;
        private const int ViewPriority = 1020;
        private const int IgnorePriority = 1040;

        private static GitServices s_services;

        /// <summary>
        /// 열린 Git 창이 있으면 그 서비스(같은 실행기라 창의 작업과 겹치지 않는다), 없으면 이 메뉴 몫 하나. 확인 카드는 Git 창 위에 뜬다.
        /// </summary>
        private static GitServices Services => GitWindow.OpenWindowServices() ?? (s_services ??= GitServices.CreateDefault());

        // ── 고른 것 ───────────────────────────────────────

        private readonly struct Picked
        {
            public readonly string RepoPath;
            public readonly bool IsFolder;

            public Picked(string repoPath, bool isFolder)
            {
                RepoPath = repoPath;
                IsFolder = isFolder;
            }
        }

        private static List<Picked> SelectedPaths()
        {
            List<Picked> picked = new();
            if (GitRepository.IsRepository == false)
            {
                return picked;
            }
            foreach (string guid in Selection.assetGUIDs)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string repoPath = GitProjectSelection.RepoPathOf(assetPath, GitRepository.ProjectRoot, GitRepository.RepoRoot);
                if (repoPath != null)
                {
                    picked.Add(new Picked(repoPath, AssetDatabase.IsValidFolder(assetPath)));
                }
            }
            return picked;
        }

        private static List<GitStatusEntry> SelectedEntries()
        {
            return GitProjectSelection.EntriesUnder(SelectedPaths().Select(p => p.RepoPath), GitRepository.Entries);
        }

        private static bool IsIdle => GitRepository.IsRepository && Services.Runner.IsBusy == false;

        // ── Stage · Unstage · 버리기 ──────────────────────

        [MenuItem(Root + "Stage", false, StagePriority)]
        private static void Stage()
        {
            _ = StageAsync(GitStaging.Unstaged(SelectedEntries()));
        }

        [MenuItem(Root + "Stage", true)]
        private static bool CanStage() => IsIdle && GitStaging.Unstaged(SelectedEntries()).Count > 0;

        [MenuItem(Root + "Unstage", false, StagePriority + 1)]
        private static void Unstage()
        {
            List<GitStatusEntry> targets = GitStaging.Staged(SelectedEntries());
            _ = Services.Runner.RunAsync(GitText.Get("op.unstage"), () => Services.Commands.UnstageAsync(targets), refreshAssets: false, workingTreeOnly: true);
        }

        [MenuItem(Root + "Unstage", true)]
        private static bool CanUnstage() => IsIdle && GitStaging.Staged(SelectedEntries()).Count > 0;

        [MenuItem(Root + "Discard Changes...", false, StagePriority + 2)]
        private static void Discard()
        {
            _ = DiscardAsync(SelectedEntries());
        }

        /// <summary>충돌 중인 파일은 버리기 대신 Git 창에서 한쪽을 골라 푼다.</summary>
        [MenuItem(Root + "Discard Changes...", true)]
        private static bool CanDiscard()
        {
            List<GitStatusEntry> entries = SelectedEntries();
            return IsIdle && entries.Count > 0 && entries.All(e => e.State != GitFileState.Conflicted);
        }

        /// <summary>
        /// Git 창의 Stage 와 같은 확인: 별도 git 저장소 폴더는 빼고, 충돌 표시가 남은 파일은 묻는다. 열린 씬·프리팹은 먼저 저장한다.
        /// </summary>
        private static async Task StageAsync(List<GitStatusEntry> targets)
        {
            GitServices services = Services;
            targets = targets.Where(e => GitStaging.IsEmbeddedRepository(e, GitRepository.RepoRoot) == false).ToList();
            if (targets.Count == 0)
            {
                return;
            }
            List<string> marked = targets.Where(e => GitInProgress.HasConflictMarkers(FileActionMenu.FullPath(e.RepoPath))).Select(e => e.RepoPath).ToList();
            if (marked.Count > 0
                && await services.Dialogs.ConfirmAsync(GitText.Get("staging.markersTitle"), GitText.Format("staging.markersMessage", string.Join("\n", marked.Take(10))),
                    GitText.Get("staging.markersOk"), danger: true) == false)
            {
                return;
            }
            if (services.Runner.IsBusy || services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            await services.Runner.RunAsync(GitText.Get("op.stage"), () => services.Commands.StageAsync(targets), refreshAssets: false, workingTreeOnly: true);
        }

        /// <summary>
        /// Stage 한 것까지 마지막 커밋 상태로 되돌린다. 되돌릴 수 없으므로 Git 창 위 확인 카드로 목록을 보이고 묻는다.
        /// </summary>
        private static async Task DiscardAsync(List<GitStatusEntry> targets)
        {
            GitServices services = Services;
            string list = string.Join("\n", targets.Take(10).Select(e => e.ProjectPath ?? e.RepoPath))
                          + (targets.Count > 10 ? GitText.Format("pending.discardMore", targets.Count - 10) : string.Empty);
            string title = targets.Count > 1 ? GitText.Format("staging.discardSelected", targets.Count) : GitText.Get("staging.discard");
            if (await GitConfirmDialog.ShowAsync(services, ActionGlyphKind.Undo, title, GitText.Format("staging.discardStagedMessage", list), GitText.Get("op.discard")) == false
                || services.Runner.IsBusy)
            {
                return;
            }
            await services.Runner.RunAsync(GitText.Get("op.discard"), () => services.Commands.DiscardAsync(targets), refreshAssets: true);
        }

        // ── 보기 ──────────────────────────────────────────

        /// <summary>
        /// 마지막 커밋 ↔ 지금 파일(stage 한 것까지)을 Preferences 의 Diff 엔진으로 연다. 내장 뷰어면 이미지는 이미지 diff(나란히·스와이프·어니언 스킨·차이)로 보인다.
        /// </summary>
        [MenuItem(Root + "Diff", false, ViewPriority - 1)]
        private static void Diff()
        {
            Services.Diff.Open(GitDiffTarget.ForWorkingTree(SingleChangedFile()));
        }

        [MenuItem(Root + "Diff", true)]
        private static bool CanDiff() => SingleChangedFile() != null;

        /// <summary>
        /// 파일 하나를 골랐고 그 파일(또는 그 .meta 만)이 바뀌었으면 그 변경. 폴더·여러 개·바뀌지 않은 파일이면 null.
        /// </summary>
        private static GitStatusEntry SingleChangedFile()
        {
            List<Picked> picked = SelectedPaths();
            if (picked.Count != 1 || picked[0].IsFolder)
            {
                return null;
            }
            string repoPath = picked[0].RepoPath;
            return GitRepository.Entries.FirstOrDefault(e => e.RepoPath == repoPath)
                   ?? GitRepository.Entries.FirstOrDefault(e => e.RepoPath == repoPath + ".meta");
        }

        [MenuItem(Root + "Show in Git Window", false, ViewPriority)]
        private static void ShowInGitWindow()
        {
            GitWindow.RevealChange(SelectedEntries().First().RepoPath);
        }

        [MenuItem(Root + "Show in Git Window", true)]
        private static bool CanShowInGitWindow() => GitRepository.IsRepository && SelectedEntries().Count > 0;

        [MenuItem(Root + "File History", false, ViewPriority + 1)]
        private static void FileHistory()
        {
            Services.OpenFileHistory(SingleTrackedFile());
        }

        [MenuItem(Root + "File History", true)]
        private static bool CanFileHistory() => SingleTrackedFile() != null;

        [MenuItem(Root + "Blame", false, ViewPriority + 2)]
        private static void Blame()
        {
            GitBlameWindow.Open(SingleTrackedFile());
        }

        [MenuItem(Root + "Blame", true)]
        private static bool CanBlame()
        {
            string repoPath = SingleTrackedFile();
            return repoPath != null && File.Exists(FileActionMenu.FullPath(repoPath));
        }

        /// <summary>
        /// 파일 하나를 골랐고 그 파일이 커밋된 적이 있으면 그 경로(히스토리·Blame 을 볼 수 있다). 새 파일·폴더·여러 개면 null.
        /// </summary>
        private static string SingleTrackedFile()
        {
            List<Picked> picked = SelectedPaths();
            if (picked.Count != 1 || picked[0].IsFolder)
            {
                return null;
            }
            string repoPath = picked[0].RepoPath;
            GitStatusEntry change = GitRepository.Entries.FirstOrDefault(e => e.RepoPath == repoPath);
            bool isNew = change != null && (change.State == GitFileState.Untracked || change.State == GitFileState.Added);
            return isNew ? null : repoPath;
        }

        // ── 무시 ──────────────────────────────────────────

        [MenuItem(Root + "Add to .gitignore", false, IgnorePriority)]
        private static void Ignore()
        {
            List<string> patterns = SelectedPaths().SelectMany(p => GitProjectSelection.IgnorePatternsOf(p.RepoPath, p.IsFolder)).Distinct().ToList();
            _ = Services.Runner.RunAsync(GitText.Format("menu.ignoreOp", string.Join(", ", patterns)), () => Task.FromResult(AddAll(patterns)), refreshAssets: false);
        }

        /// <summary>
        /// 아직 git 이 모르는 새 파일·폴더만 넣는다. 이미 커밋된 파일은 .gitignore 에 넣어도 계속 추적되므로(git 규칙) 끈다.
        /// </summary>
        [MenuItem(Root + "Add to .gitignore", true)]
        private static bool CanIgnore()
        {
            List<Picked> picked = SelectedPaths();
            if (IsIdle == false || picked.Count == 0)
            {
                return false;
            }
            return picked.All(p =>
            {
                List<GitStatusEntry> under = GitProjectSelection.EntriesUnder(new[] { p.RepoPath }, GitRepository.Entries);
                return under.Count > 0 && under.All(e => e.State == GitFileState.Untracked);
            });
        }

        private static GitResult AddAll(IEnumerable<string> patterns)
        {
            foreach (string pattern in patterns)
            {
                GitResult result = GitFileOperations.AddToGitignore(pattern);
                if (result.Success == false)
                {
                    return result;
                }
            }
            return new GitResult(0, string.Empty, string.Empty);
        }
    }
}
