using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Pending Changes 탭: 작업 트리 변경을 보고, 고른 파일만 커밋하거나 되돌린다.
    /// 체크 규칙은 <see cref="PendingSelection"/>, 커밋·되돌리기는 <see cref="IGitCommandService"/> 가 맡는다.
    /// </summary>
    public sealed class PendingChangesView : GitView
    {
        private readonly FileActionMenu _fileActions;
        private readonly PendingChangesState _state;
        private readonly PendingSelection _selection;
        private readonly FileBrowser _files;
        private readonly Toggle _selectAll;
        private readonly Label _summary;
        private readonly TextField _message;
        private readonly VisualElement _authorNotice;
        private readonly VisualElement _behindNotice;
        private readonly Label _behindText;
        private readonly Button _commitButton;

        public override string Title => GitText.Get("tab.pending");
        public override string Badge => _selection.Rows.Count > 0 ? _selection.Rows.Count.ToString() : null;

        public PendingChangesView(GitServices services, PendingChangesState state) : base(services, "PendingChangesView")
        {
            _state = state;
            _selection = new PendingSelection(state);

            _selectAll = Root.Require<Toggle>("select-all");
            _summary = Root.Require<Label>("summary");
            _message = Root.Require<TextField>("commit-message");
            _authorNotice = Root.Require<VisualElement>("author-notice");
            _behindNotice = Root.Require<VisualElement>("behind-notice");
            _behindText = Root.Require<Label>("behind-text");
            _commitButton = Root.Require<Button>("commit-button");

            _files = new FileBrowser(Root.Require<VisualElement>("file-list"), Root.Require<VisualElement>("view-mode-host"), services)
            {
                IsChecked = item => _selection.IsChecked((GitStatusEntry)item.Source),
                DiffTargetOf = item => GitDiffTarget.ForWorkingTree((GitStatusEntry)item.Source),
            };
            _files.ToggleChanged += OnToggleChanged;
            _files.FolderContextMenuRequested += PopulateFolderMenu;
            _fileActions = new FileActionMenu(services, GitDiffSource.WorkingTree);
            _files.FileMenu = PopulateFileMenu;
            _files.FolderPathContextMenuRequested += (menu, folder, items) =>
                _fileActions.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(folder, items[0]), items.Select(i => (GitStatusEntry)i.Source).ToList());

            _selectAll.RegisterValueChangedCallback(evt =>
            {
                _selection.SetAll(evt.newValue);
                _files.Refresh();
                UpdateCommitArea();
            });

            _message.SetValueWithoutNotify(state.CommitMessage);
            _message.RegisterValueChangedCallback(evt =>
            {
                _state.CommitMessage = evt.newValue;
                UpdateCommitArea();
            });
            _commitButton.clicked += Commit;
        }

        public override void OnRepositoryChanged()
        {
            _selection.SetRows(Services.Repository.Entries);
            _files.SetItems(_selection.Rows.Select(FileListItem.From));
            UpdateCommitArea();
            NotifyBadgeChanged();
        }

        public override void OnBusyChanged(bool isBusy)
        {
            UpdateCommitArea();
        }

        public override void Dispose()
        {
            _files.Dispose();
        }

        private void OnToggleChanged(IReadOnlyList<FileListItem> items, bool value)
        {
            _selection.SetChecked(items.Select(item => (GitStatusEntry)item.Source), value);
            // .meta 짝도 바뀌었을 수 있으니 보이는 줄을 다시 채운다.
            _files.Refresh();
            UpdateCommitArea();
        }

        private void UpdateCommitArea()
        {
            int total = _selection.Rows.Count;
            int selected = _selection.CheckedCount;
            _summary.text = GitText.Format("pending.summary", total, selected);
            _selectAll.showMixedValue = selected > 0 && selected < total;
            _selectAll.SetValueWithoutNotify(_selection.AllChecked);

            IGitRepositoryState repository = Services.Repository;
            bool hasAuthor = string.IsNullOrEmpty(repository.UserName) == false && string.IsNullOrEmpty(repository.UserEmail) == false;
            _authorNotice.SetVisible(hasAuthor == false);

            int behind = repository.Branch?.Behind ?? 0;
            _behindNotice.SetVisible(behind > 0);
            _behindText.text = GitText.Format("pending.behind", behind);

            _commitButton.text = GitText.Format("pending.commitButton", selected);
            _commitButton.SetEnabled(Services.Runner.IsBusy == false && hasAuthor && selected > 0 && string.IsNullOrWhiteSpace(_message.value) == false);
        }

        private async void Commit()
        {
            // 열린 씬·프리팹의 수정이 디스크에 없으면 커밋되지 않는다. 저장을 거부하면 커밋도 멈춘다.
            if (Services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }

            List<GitStatusEntry> entries = _selection.CheckedEntries();
            string message = _message.value.Trim();
            GitResult result = await Services.Runner.RunAsync(GitText.Get("op.commit"), () => Services.Commands.CommitAsync(entries, message), refreshAssets: false);
            if (result.Success)
            {
                _message.value = string.Empty;
                Services.Runner.SetStatus(GitDisplay.FirstLine(result.Output));
            }
        }

        /// <summary>
        /// 파일 줄 메뉴: 변경 내용 보기 · 되돌리기(기존) · 무시 › · LFS › · Stash, 그 아래 공통 묶음(히스토리·외부 프로그램·패치·편집·삭제).
        /// </summary>
        private void PopulateFileMenu(DropdownMenu menu, FileListItem item)
        {
            GitStatusEntry entry = (GitStatusEntry)item.Source;
            menu.AppendAction(GitText.Get("diff.show"), _ => Services.Diff.Open(GitDiffTarget.ForWorkingTree(entry)));
            PopulateContextMenu(menu, item);
            menu.AppendSeparator();
            _fileActions.AppendWorkingItems(menu, entry);
            _fileActions.AppendCommonItems(menu, entry);
        }

        private void PopulateContextMenu(DropdownMenu menu, FileListItem item)
        {
            GitStatusEntry entry = (GitStatusEntry)item.Source;
            List<GitStatusEntry> targets = new() { entry };
            GitStatusEntry pair = _selection.FindMetaPair(entry);
            if (pair != null)
            {
                targets.Add(pair);
            }

            bool enabled = Services.Runner.IsBusy == false;
            menu.AppendAction(GitText.Get(pair != null ? "pending.discardWithMeta" : "pending.discard"), _ => Discard(targets),
                enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            List<GitStatusEntry> checkedEntries = _selection.CheckedEntries();
            if (checkedEntries.Count > 1)
            {
                menu.AppendAction(GitText.Format("pending.discardChecked", checkedEntries.Count), _ => Discard(checkedEntries),
                    enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }
        }

        /// <summary>트리의 폴더 줄: 폴더 아래 변경을 한꺼번에 되돌린다.</summary>
        private void PopulateFolderMenu(DropdownMenu menu, IReadOnlyList<FileListItem> items)
        {
            List<GitStatusEntry> entries = items.Select(item => (GitStatusEntry)item.Source).ToList();
            menu.AppendAction(GitText.Format("pending.discardFolder", entries.Count), _ => Discard(entries),
                Services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
        }

        private async void Discard(List<GitStatusEntry> entries)
        {
            string list = string.Join("\n", entries.Take(10).Select(e => e.ProjectPath ?? e.RepoPath))
                          + (entries.Count > 10 ? GitText.Format("pending.discardMore", entries.Count - 10) : string.Empty);
            if (Services.Dialogs.Confirm(GitText.Get("pending.discard"), GitText.Format("pending.discardMessage", list), GitText.Get("op.discard")) == false)
            {
                return;
            }
            await Services.Runner.RunAsync(GitText.Get("op.discard"), () => Services.Commands.DiscardAsync(entries), refreshAssets: true);
        }
    }
}
