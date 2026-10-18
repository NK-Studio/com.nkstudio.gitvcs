using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Fork·GitKraken 레이아웃의 Unstaged(위)·Staged(아래) 목록(<c>StagingLists.uxml</c>).
    ///
    /// 한 번 누르면 그 파일의 diff 를 알리고(<see cref="FileSelected"/>), 더블클릭하면 반대쪽 목록으로 옮긴다(Stage·Unstage).
    /// 머리 줄 버튼은 고른 파일이 있으면 그 파일만, 없으면 전부를 옮긴다. .meta 짝은 항상 같이 옮긴다(<see cref="GitStaging.WithMetaPairs"/>).
    /// </summary>
    public sealed class StagingLists : IDisposable
    {
        private readonly GitServices _services;
        private readonly FileBrowser _unstaged;
        private readonly FileBrowser _staged;
        private readonly Label _unstagedTitle;
        private readonly Label _stagedTitle;
        private readonly Button _stageButton;
        private readonly Button _unstageButton;

        private List<GitStatusEntry> _unstagedEntries = new();
        private List<GitStatusEntry> _stagedEntries = new();
        private GitStatusEntry _selectedUnstaged;
        private GitStatusEntry _selectedStaged;
        private readonly FileActionMenu _unstagedMenu;
        private readonly FileActionMenu _stagedMenu;

        /// <summary>
        /// 파일을 골랐을 때 그 diff 대상. 선택이 풀리면 null.
        /// </summary>
        public event Action<GitDiffTarget> FileSelected;

        public VisualElement Root { get; }
        public int StagedCount => _stagedEntries.Count;
        public int UnstagedCount => _unstagedEntries.Count;

        public StagingLists(VisualElement host, GitServices services)
        {
            _services = services;
            Root = GitUiAssets.CloneBlock(GitUiAssets.Load("StagingLists"));
            host.Add(Root);
            SplitPaneClamp.AttachAll(Root);

            _unstagedTitle = Root.Require<Label>("unstaged-title");
            _stagedTitle = Root.Require<Label>("staged-title");
            _stageButton = Root.Require<Button>("stage-button");
            _unstageButton = Root.Require<Button>("unstage-button");

            VisualElement switchHost = Root.Require<VisualElement>("view-mode-host");
            _unstaged = new FileBrowser(Root.Require<VisualElement>("unstaged-files"), switchHost, services)
            {
                DiffTargetOf = item => GitDiffTarget.ForStaging((GitStatusEntry)item.Source, staged: false),
                ChooseAction = item => Stage(new[] { (GitStatusEntry)item.Source }),
                FileMenu = PopulateUnstagedMenu,
            };
            _staged = new FileBrowser(Root.Require<VisualElement>("staged-files"), null, services)
            {
                DiffTargetOf = item => GitDiffTarget.ForStaging((GitStatusEntry)item.Source, staged: true),
                ChooseAction = item => Unstage(new[] { (GitStatusEntry)item.Source }),
                FileMenu = PopulateStagedMenu,
            };
            switchHost.Q(className: "view-mode-switch")?.AddToClassList("view-mode-switch--large");
            _unstaged.SelectionChanged += item => OnSelected(item, staged: false);
            _staged.SelectionChanged += item => OnSelected(item, staged: true);
            _unstagedMenu = new FileActionMenu(services, GitDiffSource.Unstaged);
            _stagedMenu = new FileActionMenu(services, GitDiffSource.Staged);
            _unstaged.FolderPathContextMenuRequested += PopulateUnstagedFolderMenu;
            _staged.FolderPathContextMenuRequested += PopulateStagedFolderMenu;

            _stageButton.clicked += () => Stage(_selectedUnstaged != null ? new[] { _selectedUnstaged } : null);
            _unstageButton.clicked += () => Unstage(_selectedStaged != null ? new[] { _selectedStaged } : null);
        }

        public void SetEntries(IReadOnlyList<GitStatusEntry> entries)
        {
            _unstagedEntries = GitStaging.Unstaged(entries);
            _stagedEntries = GitStaging.Staged(entries);
            _unstaged.SetItems(_unstagedEntries.Select(FileListItem.From));
            _staged.SetItems(_stagedEntries.Select(FileListItem.From));

            // 새로고침 뒤에도 같은 파일이 남아 있으면 선택을 이어 간다(목록 쪽이 경로로 되살린다). 사라졌으면 놓고 diff 도 비운다
            // (stage 해서 반대쪽으로 옮겨 간 파일의 diff 가 빈 채로 남지 않게).
            bool hadSelection = _selectedUnstaged != null || _selectedStaged != null;
            _selectedUnstaged = _selectedUnstaged == null ? null : _unstagedEntries.FirstOrDefault(e => e.RepoPath == _selectedUnstaged.RepoPath);
            _selectedStaged = _selectedStaged == null ? null : _stagedEntries.FirstOrDefault(e => e.RepoPath == _selectedStaged.RepoPath);
            Render();
            if (hadSelection && _selectedUnstaged == null && _selectedStaged == null)
            {
                FileSelected?.Invoke(null);
            }
        }

        public void OnBusyChanged()
        {
            Render();
        }

        public void ClearSelection()
        {
            _unstaged.ClearSelection();
            _staged.ClearSelection();
        }

        public void Dispose()
        {
            _unstaged.Dispose();
            _staged.Dispose();
        }

        private void Render()
        {
            _unstagedTitle.text = GitText.Format("staging.unstagedTitle", _unstagedEntries.Count);
            _stagedTitle.text = GitText.Format("staging.stagedTitle", _stagedEntries.Count);
            _stageButton.text = GitText.Get(_selectedUnstaged != null ? "staging.stage" : "staging.stageAll");
            _unstageButton.text = GitText.Get(_selectedStaged != null ? "staging.unstage" : "staging.unstageAll");
            bool idle = _services.Runner.IsBusy == false;
            _stageButton.SetEnabled(idle && _unstagedEntries.Count > 0);
            _unstageButton.SetEnabled(idle && _stagedEntries.Count > 0);
        }

        private void OnSelected(FileListItem item, bool staged)
        {
            GitStatusEntry entry = item?.Source as GitStatusEntry;
            if (staged)
            {
                _selectedStaged = entry;
                if (entry != null)
                {
                    _selectedUnstaged = null;
                    _unstaged.ClearSelection();
                }
            }
            else
            {
                _selectedUnstaged = entry;
                if (entry != null)
                {
                    _selectedStaged = null;
                    _staged.ClearSelection();
                }
            }
            Render();
            if (entry != null || (_selectedStaged == null && _selectedUnstaged == null))
            {
                FileSelected?.Invoke(entry == null ? null : GitDiffTarget.ForStaging(entry, staged));
            }
        }

        /// <summary>
        /// Unstaged 파일 메뉴(GitKraken 순서): Stage · 변경 버리기(새 파일은 없음) · 무시 › · LFS › · Stash, 그 아래 공통 묶음.
        /// </summary>
        private void PopulateUnstagedMenu(DropdownMenu menu, FileListItem item)
        {
            GitStatusEntry entry = (GitStatusEntry)item.Source;
            menu.AppendAction(GitText.Get("staging.stage"), _ => Stage(new[] { entry }), StatusOfIdle());
            if (entry.State != GitFileState.Untracked)
            {
                menu.AppendAction(GitText.Get("staging.discard"), _ => Discard(new[] { entry }), StatusOfIdle());
            }
            _unstagedMenu.AppendWorkingItems(menu, entry);
            _unstagedMenu.AppendCommonItems(menu, entry);
        }

        private void PopulateStagedMenu(DropdownMenu menu, FileListItem item)
        {
            GitStatusEntry entry = (GitStatusEntry)item.Source;
            menu.AppendAction(GitText.Get("staging.unstage"), _ => Unstage(new[] { entry }), StatusOfIdle());
            menu.AppendSeparator();
            _stagedMenu.AppendCommonItems(menu, entry);
        }

        private void PopulateUnstagedFolderMenu(DropdownMenu menu, string displayFolder, IReadOnlyList<FileListItem> items)
        {
            List<GitStatusEntry> entries = items.Select(i => (GitStatusEntry)i.Source).ToList();
            menu.AppendAction(GitText.Get("menu.stageFolder"), _ => Stage(entries), StatusOfIdle());
            menu.AppendAction(GitText.Get("menu.discardFolder"), _ => Discard(entries), StatusOfIdle());
            _unstagedMenu.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(displayFolder, items[0]), entries);
        }

        private void PopulateStagedFolderMenu(DropdownMenu menu, string displayFolder, IReadOnlyList<FileListItem> items)
        {
            List<GitStatusEntry> entries = items.Select(i => (GitStatusEntry)i.Source).ToList();
            menu.AppendAction(GitText.Get("menu.unstageFolder"), _ => Unstage(entries), StatusOfIdle());
            _stagedMenu.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(displayFolder, items[0]), entries);
        }

        private DropdownMenuAction.Status StatusOfIdle()
        {
            return _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
        }

        /// <param name="entries">null 이면 전부.</param>
        private void Stage(IEnumerable<GitStatusEntry> entries)
        {
            // 열린 씬·프리팹의 저장 안 된 수정은 stage 되지 않는다. 저장을 거부하면 멈춘다.
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            if (entries == null)
            {
                _ = _services.Runner.RunAsync(GitText.Get("op.stage"), _services.Commands.StageAllAsync, refreshAssets: false);
                return;
            }
            List<GitStatusEntry> targets = GitStaging.WithMetaPairs(entries, _unstagedEntries);
            _ = _services.Runner.RunAsync(GitText.Get("op.stage"), () => _services.Commands.StageAsync(targets), refreshAssets: false);
        }

        /// <param name="entries">null 이면 전부.</param>
        private void Unstage(IEnumerable<GitStatusEntry> entries)
        {
            if (entries == null)
            {
                _ = _services.Runner.RunAsync(GitText.Get("op.unstage"), _services.Commands.UnstageAllAsync, refreshAssets: false);
                return;
            }
            List<GitStatusEntry> targets = GitStaging.WithMetaPairs(entries, _stagedEntries);
            _ = _services.Runner.RunAsync(GitText.Get("op.unstage"), () => _services.Commands.UnstageAsync(targets), refreshAssets: false);
        }

        private async void Discard(IEnumerable<GitStatusEntry> entries)
        {
            List<GitStatusEntry> targets = GitStaging.WithMetaPairs(entries, _unstagedEntries);
            string list = string.Join("\n", targets.Take(10).Select(e => e.ProjectPath ?? e.RepoPath));
            if (_services.Dialogs.Confirm(GitText.Get("staging.discard"), GitText.Format("staging.discardMessage", list), GitText.Get("op.discard")) == false)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Get("op.discard"), () => _services.Commands.DiscardWorkTreeAsync(targets), refreshAssets: true);
        }
    }
}
