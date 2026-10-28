using System;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 커밋에서 바뀐 파일 한 줄의 우클릭 메뉴(Fork 의 Changes 메뉴). 브랜치 탐색기(Fork 형태)의 Commit · Changes 칸과
    /// GitKraken 형태 Graph 탭 오른쪽 커밋 상세가 같이 쓴다.
    /// 열기 · 외부 diff · Finder · Project 창에서 찾기 / 파일을 커밋 시점·이전 상태로 되돌리기 / Blame · 히스토리 / 이 버전 저장 · (있으면) File Tree 에서 보기 / 경로 복사.
    /// 여는 항목은 작업 폴더에 그 파일이 지금 있을 때만 켠다.
    /// </summary>
    public sealed class CommitFileMenu
    {
        private readonly GitServices _services;

        /// <summary>
        /// "File Tree 에서 보기". 그 칸이 없는 곳(GitKraken 커밋 상세)은 null 이라 항목을 빼다.
        /// </summary>
        public Action<string> ShowInFileTree { get; set; }

        /// <summary>
        /// Fork 의 Commit 칸 메뉴처럼 줄인다(외부 diff · Project 창에서 선택을 뺀다). 파일을 누르면 Changes 칸에서 diff 를 보기 때문이다.
        /// </summary>
        public bool Compact { get; set; }

        public CommitFileMenu(GitServices services)
        {
            _services = services;
        }

        public void Populate(DropdownMenu menu, GitCommitInfo commit, FileListItem item)
        {
            if (commit == null || item?.Source is not GitFileChange change)
            {
                return;
            }
            string fullPath = FullPath(change.RepoPath);
            bool exists = File.Exists(fullPath);
            bool idle = _services.Runner.IsBusy == false;
            bool inCommit = change.State != GitFileState.Deleted;

            menu.AppendAction(GitText.Get("menu.externalEditor"), _ => InternalEditorUtility.OpenFileAtLineExternal(fullPath, 1), StatusOf(exists));
            menu.AppendAction(GitText.Get("menu.defaultProgram"), _ => EditorUtility.OpenWithDefaultApp(fullPath), StatusOf(exists));
            // 내장 뷰어를 쓰는 중이면 "외부"는 git difftool 이다.
            DiffEngine engine = GitVcsSettings.instance.DiffEngine == DiffEngine.BuiltIn ? DiffEngine.GitDifftool : GitVcsSettings.instance.DiffEngine;
            if (Compact == false)
            {
                menu.AppendAction(GitText.Get("menu.externalDiff"), _ => _services.Diff.OpenExternally(GitDiffTarget.ForCommit(commit.Hash, change), engine));
            }
            menu.AppendAction(GitFileManager.RevealLabel, _ => GitFileManager.Reveal(fullPath), StatusOf(exists));
            if (Compact == false)
            {
                // Unity 안에서는 Project 창에서 그 에셋을 바로 찾는 것이 가장 잦다.
                menu.AppendAction(GitText.Get("common.pingAsset"), _ => _services.Assets.Ping(item.ProjectPath), StatusOf(exists && _services.Assets.CanPing(item.ProjectPath)));
            }
            menu.AppendSeparator();

            string reset = GitText.Get("history.resetFileTo");
            menu.AppendAction($"{reset}/{GitText.Get("history.resetAtCommit")}", _ => ResetFile(change.RepoPath, commit.Hash, commit.ShortHash), StatusOf(idle && inCommit));
            // 이름을 바꾼 파일은 커밋 이전에는 원래 이름이었다.
            string beforePath = string.IsNullOrEmpty(change.OriginalRepoPath) ? change.RepoPath : change.OriginalRepoPath;
            bool hasParent = commit.Parents.Length > 0;
            menu.AppendAction($"{reset}/{GitText.Get("history.resetBeforeCommit")}",
                _ => ResetFile(beforePath, commit.Hash + "^", GitDisplay.ShortRevision(commit.Parents[0])), StatusOf(idle && hasParent));
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("menu.fileBlame"), _ => GitBlameWindow.Open(change.RepoPath), StatusOf(exists));
            menu.AppendAction(GitText.Get("menu.fileHistory"), _ => _services.OpenFileHistory(change.RepoPath));
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("history.saveRevisionAs"), _ => SaveRevisionAs(commit, change.RepoPath), StatusOf(inCommit));
            if (ShowInFileTree != null)
            {
                menu.AppendAction(GitText.Get("history.showInFileTree"), _ => ShowInFileTree(change.RepoPath), StatusOf(inCommit));
            }
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("menu.copyPath"), _ => _services.Assets.CopyToClipboard(change.RepoPath));
        }

        /// <summary>
        /// 그 커밋 시점의 파일을 원하는 곳에 저장한다. 이진 파일도 그대로 쓰도록 git 출력을 파일로 바로 받는다.
        /// </summary>
        public async void SaveRevisionAs(GitCommitInfo commit, string repoPath)
        {
            string name = Path.GetFileNameWithoutExtension(repoPath);
            string extension = Path.GetExtension(repoPath).TrimStart('.');
            string target = EditorUtility.SaveFilePanel(GitText.Get("history.saveRevisionAs"), string.Empty,
                $"{name}@{commit.ShortHash}" + (extension.Length > 0 ? "." + extension : string.Empty), extension);
            if (string.IsNullOrEmpty(target))
            {
                return;
            }
            GitResult result = await GitCommand.RunToFileAsync(GitRepository.RepoRoot, new[] { "show", $"{commit.Hash}:{repoPath}" }, target);
            _services.Runner.SetStatus(result.Success ? GitText.Format("history.savedRevision", target) : result.Message);
        }

        /// <summary>
        /// 파일 하나를 그 시점 내용으로 되돌린다. 작업 폴더의 고친 내용을 덮어쓰므로 먼저 묻는다.
        /// </summary>
        private void ResetFile(string repoPath, string revision, string revisionLabel)
        {
            if (_services.Dialogs.Confirm(GitText.Get("history.resetFileTo"), GitText.Format("history.resetConfirm", repoPath, revisionLabel), GitText.Get("history.resetOk")) == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("history.opReset", Path.GetFileName(repoPath), revisionLabel),
                () => GitCommitTree.ResetFileToAsync(revision, repoPath), refreshAssets: true);
        }

        public static string FullPath(string repoPath)
        {
            return Path.GetFullPath(Path.Combine(GitRepository.RepoRoot, repoPath));
        }

        public static DropdownMenuAction.Status StatusOf(bool enabled)
        {
            return enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
        }
    }
}
