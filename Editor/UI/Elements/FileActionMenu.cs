using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태의 파일·폴더 우클릭 메뉴. 목록 쪽(<see cref="StagingLists"/>, <see cref="PendingChangesView"/>)은
    /// 맨 위 묶음(Stage·Unstage·되돌리기)만 정하고, 나머지(무시·LFS·Stash·히스토리·Blame·외부 프로그램·패치·편집·삭제)는 여기서 같은 순서로 채운다.
    /// </summary>
    public sealed class FileActionMenu
    {
        private readonly GitServices _services;

        /// <summary>
        /// 이 목록의 diff 기준(Unstaged·Staged·작업 트리 전체). 패치·외부 diff 가 이것을 쓴다.
        /// </summary>
        private readonly GitDiffSource _source;

        public FileActionMenu(GitServices services, GitDiffSource source)
        {
            _services = services;
            _source = source;
        }

        private bool IsStagedSide => _source == GitDiffSource.Staged;

        // ── 파일 ──────────────────────────────────────────

        /// <summary>
        /// 무시·LFS·Stash 묶음. Staged 목록에는 넣지 않는다(이미 인덱스에 올린 것은 Unstage 가 먼저다).
        /// </summary>
        public void AppendWorkingItems(DropdownMenu menu, GitStatusEntry entry)
        {
            if (IsStagedSide)
            {
                return;
            }
            if (entry.State == GitFileState.Conflicted)
            {
                AppendConflictItems(menu, entry);
            }
            string name = Path.GetFileName(entry.RepoPath);
            string extensionPattern = GitIgnorePatterns.ForExtension(entry.RepoPath);
            string folder = Path.GetDirectoryName(entry.RepoPath)?.Replace('\\', '/');

            string ignore = GitText.Get("menu.ignore");
            menu.AppendAction($"{ignore}/{GitText.Format("menu.ignoreFile", name)}", _ => AddToGitignore(GitIgnorePatterns.ForFile(entry.RepoPath)), Idle());
            if (extensionPattern != null)
            {
                menu.AppendAction($"{ignore}/{GitText.Format("menu.ignoreExtension", extensionPattern)}", _ => AddToGitignore(extensionPattern), Idle());
            }
            if (string.IsNullOrEmpty(folder) == false)
            {
                menu.AppendAction($"{ignore}/{GitText.Format("menu.ignoreFolder", folder + "/")}", _ => AddToGitignore(GitIgnorePatterns.ForFolder(folder)), Idle());
            }

            string lfs = GitText.Get("menu.lfs");
            menu.AppendAction($"{lfs}/{GitText.Format("menu.lfsFile", name)}", _ => LfsTrack(entry.RepoPath), Idle());
            if (extensionPattern != null)
            {
                menu.AppendAction($"{lfs}/{GitText.Format("menu.lfsExtension", extensionPattern)}", _ => LfsTrack(extensionPattern), Idle());
            }

            menu.AppendAction(GitText.Get("menu.stashFile"), _ => Stash(new[] { entry.RepoPath }, GitText.Format("menu.stashMessage", name)), Idle());
            menu.AppendSeparator();
        }

        /// <summary>
        /// 충돌 파일: 고친 그대로 해결됨으로 표시 · 한쪽(ours·theirs) 내용으로 해결. 바이너리 에셋(씬·프리팹·텍스처)은 손으로 고칠 수 없어 한쪽을 고르는 것이 창 안에서 푸는 길이다.
        /// 쪽 이름은 작업에 따라 다르다 — rebase 에서는 ours 가 바탕 브랜치, theirs 가 다시 쌓는 내 커밋이다(git 과 같다).
        /// </summary>
        private void AppendConflictItems(DropdownMenu menu, GitStatusEntry entry)
        {
            GitOperationKind kind = _services.Repository.InProgressOperation;
            menu.AppendAction(GitText.Get("inprogress.resolveMarked"), _ => Resolve(entry, GitConflictSide.AsIs), Idle());
            menu.AppendAction(GitText.Format("inprogress.resolveOurs", GitText.Get("inprogress.ours." + kind)), _ => Resolve(entry, GitConflictSide.Ours), Idle());
            menu.AppendAction(GitText.Format("inprogress.resolveTheirs", GitText.Get("inprogress.theirs." + kind)), _ => Resolve(entry, GitConflictSide.Theirs), Idle());
            menu.AppendSeparator();
        }

        private async void Resolve(GitStatusEntry entry, GitConflictSide side)
        {
            if (side == GitConflictSide.AsIs && GitInProgress.HasConflictMarkers(FullPath(entry.RepoPath))
                && await _services.Dialogs.ConfirmAsync(GitText.Get("inprogress.resolveMarked"), GitText.Format("inprogress.resolveMarkersConfirm", entry.RepoPath),
                    GitText.Get("inprogress.resolveMarked"), danger: true) == false)
            {
                return;
            }
            // 한쪽을 고르면 파일 내용이 바뀌므로 에셋을 다시 읽는다.
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opResolve", Path.GetFileName(entry.RepoPath)),
                () => _services.Commands.ResolveConflictAsync(entry, side), refreshAssets: side != GitConflictSide.AsIs, workingTreeOnly: true);
        }

        /// <summary>
        /// 히스토리·외부 프로그램·패치·편집·삭제 묶음. 지운 파일은 열 것이 없어 여는 항목을 끈다.
        /// 새 파일(untracked·added)은 히스토리가 없어 히스토리·blame 을 끈다.
        /// </summary>
        public void AppendCommonItems(DropdownMenu menu, GitStatusEntry entry)
        {
            bool exists = entry.State != GitFileState.Deleted && File.Exists(FullPath(entry.RepoPath));
            bool hasHistory = entry.State != GitFileState.Untracked && entry.State != GitFileState.Added;

            menu.AppendAction(GitText.Get("menu.fileHistory"), _ => _services.OpenFileHistory(entry.RepoPath), StatusOf(hasHistory));
            menu.AppendAction(GitText.Get("menu.fileBlame"), _ => GitBlameWindow.Open(entry.RepoPath), StatusOf(hasHistory && exists));
            menu.AppendSeparator();

            // 내장 뷰어를 쓰는 중이면 "외부"는 git difftool 이다.
            DiffEngine engine = GitVcsSettings.instance.DiffEngine == DiffEngine.BuiltIn ? DiffEngine.GitDifftool : GitVcsSettings.instance.DiffEngine;
            GitDiffTarget target = _source == GitDiffSource.WorkingTree ? GitDiffTarget.ForWorkingTree(entry) : GitDiffTarget.ForStaging(entry, IsStagedSide);
            menu.AppendAction(GitText.Get("menu.externalDiff"), _ => _services.Diff.OpenExternally(target, engine));
            menu.AppendAction(GitText.Get("menu.externalEditor"), _ => InternalEditorUtility.OpenFileAtLineExternal(FullPath(entry.RepoPath), 1), StatusOf(exists));
            menu.AppendAction(GitText.Get("menu.defaultProgram"), _ => EditorUtility.OpenWithDefaultApp(FullPath(entry.RepoPath)), StatusOf(exists));
            menu.AppendAction(GitFileManager.RevealLabel, _ => GitFileManager.Reveal(FullPath(entry.RepoPath)), StatusOf(exists));
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("menu.copyPath"), _ => _services.Assets.CopyToClipboard(entry.RepoPath));
            menu.AppendAction(GitText.Get("menu.createPatch"), _ => CreatePatch(new[] { entry }, Path.GetFileNameWithoutExtension(entry.RepoPath)));
            menu.AppendSeparator();

            menu.AppendAction(GitText.Get("menu.editFile"), _ => EditFile(entry), StatusOf(exists));
            menu.AppendAction(GitText.Get("menu.deleteFile"), _ => DeleteFile(entry), StatusOf(exists && _services.Runner.IsBusy == false));
        }

        // ── 폴더 ──────────────────────────────────────────

        /// <param name="repoFolder">저장소 기준 폴더 경로.</param>
        /// <param name="entries">폴더 아래 이 목록의 파일들.</param>
        public void AppendFolderItems(DropdownMenu menu, string repoFolder, IReadOnlyList<GitStatusEntry> entries)
        {
            string folderName = repoFolder.TrimEnd('/') + "/";
            if (IsStagedSide == false)
            {
                menu.AppendAction(GitText.Format("menu.ignoreFolderAll", folderName), _ => AddToGitignore(GitIgnorePatterns.ForFolder(repoFolder)), Idle());
                menu.AppendAction(GitText.Get("menu.stashFolder"), _ => Stash(new[] { repoFolder }, GitText.Format("menu.stashMessage", folderName)), Idle());
            }
            menu.AppendAction(GitText.Get("menu.createFolderPatch"), _ => CreatePatch(entries, Path.GetFileName(repoFolder.TrimEnd('/'))));
            menu.AppendSeparator();
            menu.AppendAction(GitFileManager.OpenLabel, _ => GitFileManager.OpenFolder(FullPath(repoFolder)), StatusOf(Directory.Exists(FullPath(repoFolder))));
            menu.AppendAction(GitText.Get("menu.createFile"), _ => CreateFileIn(repoFolder), StatusOf(Directory.Exists(FullPath(repoFolder))));
        }

        /// <summary>
        /// 트리의 폴더 줄은 화면 경로(프로젝트 기준)를 준다. 그 아래 파일 하나로 저장소 기준 경로를 거꾸로 맞춘다
        /// (프로젝트가 저장소 하위 폴더에 있으면 둘이 다르다). 예: 화면 "Assets/Art", 파일 저장소 경로 "Game/Assets/Art/a.png"·화면 "Assets/Art/a.png" → "Game/Assets/Art".
        /// </summary>
        public static string RepoFolderOf(string displayFolder, FileListItem anyFileUnder)
        {
            string display = anyFileUnder.DisplayPath;
            int tail = display.Length - displayFolder.TrimEnd('/').Length;
            return anyFileUnder.RepoPath.Substring(0, anyFileUnder.RepoPath.Length - tail);
        }

        // ── 동작 ──────────────────────────────────────────

        public static string FullPath(string repoPath)
        {
            return Path.GetFullPath(Path.Combine(GitRepository.RepoRoot, repoPath));
        }

        private DropdownMenuAction.Status Idle()
        {
            return StatusOf(_services.Runner.IsBusy == false);
        }

        private static DropdownMenuAction.Status StatusOf(bool enabled)
        {
            return enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
        }

        private void AddToGitignore(string pattern)
        {
            _ = _services.Runner.RunAsync(GitText.Format("menu.ignoreOp", pattern), () => System.Threading.Tasks.Task.FromResult(GitFileOperations.AddToGitignore(pattern)), refreshAssets: false);
        }

        private void LfsTrack(string pattern)
        {
            _ = _services.Runner.RunAsync(GitText.Format("menu.lfsOp", pattern), () => GitFileOperations.LfsTrackAsync(pattern), refreshAssets: false);
        }

        private void Stash(IReadOnlyCollection<string> repoPaths, string message)
        {
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Get("op.stash"), () => GitUndo.StashPathsAsync(repoPaths, message), refreshAssets: true);
        }

        private async void CreatePatch(IReadOnlyCollection<GitStatusEntry> entries, string suggestedName)
        {
            string path = EditorUtility.SaveFilePanel(GitText.Get("menu.createPatch"), GitRepository.RepoRoot, suggestedName + ".patch", "patch");
            if (string.IsNullOrEmpty(path))
            {
                return;
            }
            (string patch, string error) = await GitFileOperations.CreatePatchAsync(entries, _source);
            if (error != null)
            {
                await _services.Dialogs.AlertAsync(GitText.Get("menu.createPatch"), error);
                return;
            }
            File.WriteAllText(path, patch, new System.Text.UTF8Encoding(false));
            _services.Runner.SetStatus(GitText.Format("menu.patchSaved", path));
        }

        /// <summary>
        /// 에셋이면 Unity 가 그 종류에 맞게 연다(씬은 씬으로, 스크립트는 코드 편집기로). 에셋이 아니면 코드 편집기로.
        /// </summary>
        private static void EditFile(GitStatusEntry entry)
        {
            UnityEngine.Object asset = entry.ProjectPath == null ? null : AssetDatabase.LoadMainAssetAtPath(GitDisplay.AssetPathOf(entry.ProjectPath));
            if (asset != null)
            {
                AssetDatabase.OpenAsset(asset);
                return;
            }
            InternalEditorUtility.OpenFileAtLineExternal(FullPath(entry.RepoPath), 1);
        }

        /// <summary>
        /// 에셋은 휴지통으로 옮긴다(.meta 도 같이, 되살릴 수 있다). 에셋이 아닌 파일은 확인 뒤 지운다.
        /// </summary>
        private async void DeleteFile(GitStatusEntry entry)
        {
            string assetPath = entry.ProjectPath == null ? null : GitDisplay.AssetPathOf(entry.ProjectPath);
            bool isAsset = assetPath != null && AssetDatabase.LoadMainAssetAtPath(assetPath) != null;
            string message = GitText.Format(isAsset ? "menu.deleteAssetMessage" : "menu.deleteFileMessage", entry.RepoPath);
            if (await _services.Dialogs.ConfirmAsync(GitText.Get("menu.deleteFile"), message, GitText.Get("common.delete"), danger: isAsset == false) == false)
            {
                return;
            }
            if (isAsset)
            {
                AssetDatabase.MoveAssetToTrash(assetPath);
            }
            else
            {
                File.Delete(FullPath(entry.RepoPath));
            }
            _services.Repository.RequestRefresh(immediate: true);
        }

        private async void CreateFileIn(string repoFolder)
        {
            string folder = FullPath(repoFolder);
            // 잘못된 이름·이미 있는 파일은 쓰는 동안 막는다(입력이 닫히지 않고 남는다).
            string name = (await _services.Dialogs.PromptAsync(GitText.Get("menu.createFile"), GitText.Format("menu.createFileMessage", repoFolder.TrimEnd('/') + "/"), string.Empty,
                validate: value =>
                {
                    string trimmed = value.Trim();
                    if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                    {
                        return GitText.Format("menu.invalidFileName", trimmed);
                    }
                    return File.Exists(Path.Combine(folder, trimmed)) ? GitText.Format("menu.fileExists", trimmed) : null;
                }))?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                return;
            }
            string path = Path.Combine(folder, name);
            File.WriteAllText(path, string.Empty);
            AssetDatabase.Refresh();
            _services.Repository.RequestRefresh(immediate: true);
        }
    }
}
