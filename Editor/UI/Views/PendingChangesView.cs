using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Pending Changes 탭: 작업 트리 변경을 보고, 고른 파일만 커밋하거나 되돌린다.
    /// 위에서부터 동작 줄·커밋 메시지·파일 목록·고른 파일의 diff, 오른쪽에 "세부사항 표시" 로 펼치는 파일 정보·히스토리(Unity VCS 와 같은 배치).
    /// 체크 규칙은 <see cref="PendingSelection"/>, 커밋·되돌리기는 <see cref="IGitCommandService"/> 가 맡는다.
    /// </summary>
    public sealed class PendingChangesView : GitView
    {
        /// <summary>세부사항 히스토리에 읽을 커밋 수(워크스페이스 탭과 같다).</summary>
        private const int HistoryCount = 100;

        private readonly FileActionMenu _fileActions;
        private readonly PendingChangesState _state;
        private readonly PendingSelection _selection;
        private readonly FileBrowser _files;
        private readonly Toggle _selectAll;
        private readonly Label _summary;
        private readonly TextField _message;
        private readonly VisualElement _authorNotice;
        private readonly Button _commitButton;
        private readonly Button _discardButton;
        private readonly Button _detailsButton;
        private readonly TwoPaneSplitView _detailsSplit;
        private readonly GitDiffPanel _diff;
        private readonly Label _infoPlaceholder;
        private readonly VisualElement _info;
        private readonly Label _infoName;
        private readonly VisualElement _infoRows;
        private readonly Label _historyTitle;
        private readonly Label _historyEmpty;
        private readonly ListView _historyList;
        private readonly CommitList _history;
        private readonly Label _historyMessage;
        private GitStatusEntry _selected;
        private string _detailsPath;
        private string _template;
        private GitOperationKind _seenOperation;

        /// <summary>마지막으로 채운 준비 메시지. 작업이 배너(이어가기·취소)로 끝났는데 그대로 남아 있으면 템플릿으로 되돌린다.</summary>
        private string _preparedMessage;

        public override string Title => GitText.Get("tab.pending");

        public override System.Collections.Generic.IEnumerable<(VisualElement Host, SkeletonShape Shape)> SkeletonHosts
        {
            get { yield return (Root.Require<VisualElement>("file-list"), SkeletonShape.Files); }
        }
        public override string Badge => _selection.Rows.Count > 0 ? _selection.Rows.Count.ToString() : null;

        public PendingChangesView(GitServices services, PendingChangesState state) : base(services, "PendingChangesView")
        {
            _state = state;
            _selection = new PendingSelection(state);

            _selectAll = Root.Require<Toggle>("select-all");
            _summary = Root.Require<Label>("summary");
            _message = Root.Require<TextField>("commit-message");
            _authorNotice = Root.Require<VisualElement>("author-notice");
            _commitButton = Root.Require<Button>("commit-button");
            _discardButton = Root.Require<Button>("discard-button");
            _detailsButton = Root.Require<Button>("details-button");
            _detailsSplit = Root.Require<TwoPaneSplitView>("details-split");
            _message.textEdition.placeholder = GitText.Get("pending.commitTitle");

            // 파일 목록 아래: 고른 파일의 diff(Fork 배치와 같은 패널). 커밋은 체크박스로 고르므로 줄 단위 Stage 는 켜지 않는다.
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowReloadButton(false);
            _diff.PopupHostProvider = () => services.PopupHost;
            Root.Require<VisualElement>("diff-host").Add(_diff.Root);

            // 오른쪽 세부사항: 워크스페이스 탭의 정보·히스토리와 같은 모양. 히스토리에서 커밋을 고르면 그 메시지를 아래에 적는다.
            _infoPlaceholder = Root.Require<Label>("info-placeholder");
            _info = Root.Require<VisualElement>("info");
            _infoName = Root.Require<Label>("info-name");
            _infoRows = Root.Require<VisualElement>("info-rows");
            _historyTitle = Root.Require<Label>("history-title");
            _historyEmpty = Root.Require<Label>("history-empty");
            _historyList = Root.Require<ListView>("history");
            _historyMessage = Root.Require<Label>("history-message");
            _historyMessage.selection.isSelectable = true;
            _history = new CommitList(_historyList, services.Assets);
            _history.ItemChosen += commit => GitCompareWindow.OpenCommit(commit.Hash);
            _history.SelectionChanged += commit => _historyMessage.text = commit?.Subject ?? string.Empty;

            _files = new FileBrowser(Root.Require<VisualElement>("file-list"), Root.Require<VisualElement>("view-mode-host"), services)
            {
                // 묶인 줄("A.png +meta")은 둘 중 하나라도 체크돼 있으면 체크로 보이고, 한쪽만이면 오른쪽에 "에셋만"·".meta 만" 을 적는다.
                IsChecked = item => _selection.IsChecked((GitStatusEntry)item.Source)
                                    || (item.MetaSource is GitStatusEntry meta && _selection.IsChecked(meta)),
                DiffTargetOf = item => GitDiffTarget.ForWorkingTree((GitStatusEntry)item.Source),
            };
            _files.ToggleChanged += OnToggleChanged;
            _files.SelectionChanged += item => ShowSelected((GitStatusEntry)item?.Source);
            GitVcsSettings.MetaGroupingChanged += OnMetaGroupingChanged;
            _files.FolderContextMenuRequested += PopulateFolderMenu;
            _fileActions = new FileActionMenu(services, GitDiffSource.WorkingTree);
            _files.FileMenu = PopulateFileMenu;
            _files.FolderPathContextMenuRequested += (menu, folder, items) =>
                _fileActions.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(folder, items[0]), items.Select(i => (GitStatusEntry)i.Source).ToList());

            _selectAll.RegisterValueChangedCallback(evt =>
            {
                _selection.SetAll(evt.newValue);
                _files.SetItems(BuildItems());
                UpdateCommitArea();
            });

            _message.SetValueWithoutNotify(state.CommitMessage);
            _message.RegisterValueChangedCallback(evt =>
            {
                _state.CommitMessage = evt.newValue;
                UpdateCommitArea();
            });
            // 칸을 다 지우고 다른 곳을 누르면 템플릿을 다시 채운다(Fork 와 같다).
            _message.RegisterCallback<FocusOutEvent>(_ =>
            {
                if (_template != null && string.IsNullOrWhiteSpace(_message.value))
                {
                    _message.value = TemplateText();
                }
            });
            _commitButton.clicked += Commit;
            _discardButton.clicked += () => Discard(_selection.CheckedEntries());
            _detailsButton.clicked += () =>
            {
                _state.ShowDetails = _state.ShowDetails == false;
                ApplyDetailsVisibility();
            };
            ApplyDetailsVisibility();
            ShowDetails(null);
            LoadTemplate();
        }

        /// <summary>
        /// 커밋 템플릿(<see cref="GitCommitTemplate"/>)을 읽어 두고, 메시지 칸이 비어 있으면 채운다.
        /// </summary>
        private async void LoadTemplate()
        {
            _template = await GitCommitTemplate.ReadAsync();
            if (_template != null && string.IsNullOrWhiteSpace(_message.value))
            {
                _message.value = TemplateText();
            }
            UpdateCommitArea();
        }

        private string TemplateText()
        {
            return _template != null ? _template.Replace("\r\n", "\n").TrimEnd() : string.Empty;
        }

        /// <summary>
        /// 실제로 커밋될 메시지. 템플릿을 쓰면 '#' 안내 줄을 뺀다(git 편집기와 같다).
        /// </summary>
        private string MessageToCommit()
        {
            return _template != null ? GitCommitTemplate.StripComments(_message.value) : _message.value.Trim();
        }

        public override void OnRepositoryChanged()
        {
            _selection.SetRows(Services.Repository.Entries);
            // 보이지 않으면 파일 목록(수백 줄 트리)을 다시 만들지 않는다 — 탭을 열 때 만든다. 탭 옆 개수는 바로 바꾼다.
            if (IsActive)
            {
                _files.SetItems(BuildItems());
                UpdateCommitArea();
                RefreshSelected();
            }
            NotifyBadgeChanged();
        }

        protected override void OnActivated()
        {
            _files.SetItems(BuildItems());
            UpdateCommitArea();
            RefreshSelected();
        }

        public override void Reload()
        {
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
        }

        public override void OnBusyChanged(bool isBusy)
        {
            UpdateCommitArea();
        }

        public override bool ShowsChanges => true;

        /// <summary>충돌 안내의 "충돌 보기": 첫 충돌 파일을 골라 diff 를 연다.</summary>
        public override void ShowConflicts()
        {
            GitStatusEntry conflict = Services.Repository.Entries.FirstOrDefault(e => e.State == GitFileState.Conflicted);
            if (conflict != null)
            {
                _files.SelectFile(conflict.RepoPath);
            }
        }

        public override bool RevealChange(string repoPath)
        {
            return _files.SelectFile(repoPath) || _files.SelectFile(repoPath + ".meta");
        }

        public override void Dispose()
        {
            GitVcsSettings.MetaGroupingChanged -= OnMetaGroupingChanged;
            _files.Dispose();
        }

        private void OnToggleChanged(IReadOnlyList<FileListItem> items, bool value)
        {
            _selection.SetChecked(items.Select(item => (GitStatusEntry)item.Source), value);
            // .meta 짝도 바뀌었을 수 있으니 줄(체크·"에셋만" 표시)을 다시 만든다.
            _files.SetItems(BuildItems());
            UpdateCommitArea();
        }

        /// <summary>
        /// 줄을 만든다. 설정대로 에셋과 .meta 를 한 줄로 묶고(<see cref="FileListItem.FromStatus"/>), 한쪽만 체크된 줄에는 표시를 단다.
        /// </summary>
        private List<FileListItem> BuildItems()
        {
            List<FileListItem> items = FileListItem.FromStatus(_selection.Rows).ToList();
            foreach (FileListItem item in items)
            {
                if (item.MetaSource is GitStatusEntry meta)
                {
                    bool asset = _selection.IsChecked((GitStatusEntry)item.Source);
                    bool metaChecked = _selection.IsChecked(meta);
                    item.Note = asset == metaChecked ? null : GitText.Get(asset ? "pending.assetOnly" : "pending.metaOnly");
                }
            }
            return items;
        }

        private void OnMetaGroupingChanged()
        {
            if (IsActive)
            {
                _files.SetItems(BuildItems());
            }
        }

        /// <summary>
        /// 묶인 줄의 한쪽만 커밋에 넣는다(다른 쪽은 체크를 푼다).
        /// </summary>
        private void CheckOnly(GitStatusEntry keep, GitStatusEntry drop)
        {
            _selection.SetCheckedOnly(keep, true);
            _selection.SetCheckedOnly(drop, false);
            _files.SetItems(BuildItems());
            UpdateCommitArea();
        }

        private void UpdateCommitArea()
        {
            FillPreparedMessage();
            int total = _selection.Rows.Count;
            int selected = _selection.CheckedCount;
            _summary.text = GitText.Format("pending.summary", total, selected);
            _selectAll.showMixedValue = selected > 0 && selected < total;
            _selectAll.SetValueWithoutNotify(_selection.AllChecked);

            IGitRepositoryState repository = Services.Repository;
            bool hasAuthor = string.IsNullOrEmpty(repository.UserName) == false && string.IsNullOrEmpty(repository.UserEmail) == false;
            _authorNotice.SetVisible(hasAuthor == false);

            _commitButton.text = GitText.Format("pending.commitButton", selected);
            _commitButton.SetEnabled(Services.Runner.IsBusy == false && hasAuthor && selected > 0 && MessageToCommit().Length > 0);
            _discardButton.SetEnabled(Services.Runner.IsBusy == false && selected > 0);
        }

        // ── 아래 diff · 오른쪽 세부사항 ─────────────────────

        /// <summary>
        /// 목록을 다시 만든 뒤: 고른 파일이 남아 있으면 diff 와 세부사항을 다시 읽고(저장으로 내용이 바뀌었을 수 있다), 사라졌으면 비운다.
        /// </summary>
        private void RefreshSelected()
        {
            GitStatusEntry entry = (GitStatusEntry)_files.SelectedItems.FirstOrDefault()?.Source;
            if (entry == null || entry.RepoPath != _selected?.RepoPath)
            {
                ShowSelected(entry);
                return;
            }
            _selected = entry;
            if (_diff.Target != null)
            {
                _diff.Reload();
            }
            ShowDetails(entry);
        }

        private void ShowSelected(GitStatusEntry entry)
        {
            _selected = entry;
            _diff.SetTarget(entry == null ? null : GitDiffTarget.ForWorkingTree(entry));
            ShowDetails(entry);
        }

        private void ApplyDetailsVisibility()
        {
            if (_state.ShowDetails)
            {
                _detailsSplit.UnCollapse();
            }
            else
            {
                _detailsSplit.CollapseChild(1);
            }
            _detailsButton.EnableInClassList("pending-changes__details-button--on", _state.ShowDetails);
        }

        /// <summary>
        /// 오른쪽 세부사항: 이름·경로·상태·크기·만든 날짜·수정한 날짜, 그 아래 이 파일을 바꾼 커밋들.
        /// 아직 커밋된 적 없는 파일(새 파일·추가)은 히스토리 대신 안내를 적는다.
        /// </summary>
        private async void ShowDetails(GitStatusEntry entry)
        {
            string path = entry?.RepoPath;
            bool sameFile = path != null && path == _detailsPath;
            _detailsPath = path;
            _infoPlaceholder.SetVisible(entry == null);
            _info.SetVisible(entry != null);
            _historyTitle.SetVisible(entry != null);
            _historyEmpty.SetVisible(false);
            _historyList.SetVisible(entry != null);
            _historyMessage.SetVisible(entry != null);
            if (sameFile == false)
            {
                _history.SetItems(Array.Empty<GitCommitInfo>());
                _historyMessage.text = string.Empty;
            }
            if (entry == null)
            {
                return;
            }

            _infoName.text = Path.GetFileName(path);
            _infoRows.Clear();
            AddInfo("workspace.infoPath", path);
            AddInfo("workspace.infoState", GitText.Get("state." + entry.State));
            string fullPath = Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, path);
            FileSystemInfo file = File.Exists(fullPath) ? new FileInfo(fullPath) : Directory.Exists(fullPath) ? new DirectoryInfo(fullPath) : null;
            if (file is FileInfo info)
            {
                AddInfo("workspace.infoSize", WorkspaceView.FormatSize(info.Length));
            }
            if (file != null)
            {
                AddInfo("workspace.infoCreated", GitDisplay.FormatDate(file.CreationTime));
                AddInfo("workspace.infoModified", GitDisplay.FormatDate(file.LastWriteTime));
            }

            if (entry.State == GitFileState.Untracked || entry.State == GitFileState.Added)
            {
                _historyList.SetVisible(false);
                _historyMessage.SetVisible(false);
                _historyEmpty.text = GitText.Get("workspace.noHistory");
                _historyEmpty.SetVisible(true);
                return;
            }
            if (sameFile)
            {
                return;
            }
            (List<GitCommitInfo> commits, string error) = await GitHistory.FileLogAsync(path, HistoryCount);
            if (_detailsPath != path)
            {
                return;
            }
            _history.SetItems(commits, c => c.Hash);
            if (commits.Count == 0)
            {
                _historyList.SetVisible(false);
                _historyMessage.SetVisible(false);
                _historyEmpty.text = error ?? GitText.Get("workspace.noHistory");
                _historyEmpty.SetVisible(true);
                return;
            }
            _history.Select(commits[0]);
        }

        private void AddInfo(string key, string value)
        {
            VisualElement line = new();
            line.AddToClassList("workspace__info-row");
            Label caption = new(GitText.Get(key));
            caption.AddToClassList("workspace__info-caption");
            line.Add(caption);
            Label text = new(value);
            text.AddToClassList("workspace__info-value");
            text.selection.isSelectable = true;
            line.Add(text);
            _infoRows.Add(line);
        }

        /// <summary>
        /// 멈춘 병합·cherry-pick·revert 는 이 커밋으로 끝난다. 그 작업이 보이기 시작할 때 비어 있는(템플릿 그대로인) 메시지 칸에
        /// git 이 준비한 메시지(MERGE_MSG)를 채운다. 사용자가 쓴 글은 덮지 않는다. 작업이 커밋 없이 끝나면(배너의 이어가기·취소) 손대지 않은 그 메시지를 거둔다.
        /// </summary>
        private void FillPreparedMessage()
        {
            GitOperationKind kind = Services.Repository.InProgressOperation;
            if (kind == _seenOperation)
            {
                return;
            }
            _seenOperation = kind;
            if (_preparedMessage != null && _message.value == _preparedMessage)
            {
                _message.value = TemplateText();
            }
            _preparedMessage = null;
            bool untouched = string.IsNullOrWhiteSpace(_message.value) || _message.value == TemplateText();
            string message = GitInProgress.RequiresFullCommit(kind) && untouched ? Services.Repository.PreparedCommitMessage : null;
            if (message != null)
            {
                _message.value = message;
                _preparedMessage = message;
            }
        }

        private async void Commit()
        {
            // 열린 씬·프리팹의 수정이 디스크에 없으면 커밋되지 않는다. 저장을 거부하면 커밋도 멈춘다.
            if (Services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }

            List<GitStatusEntry> entries = _selection.CheckedEntries();
            string message = MessageToCommit();
            GitResult result = await Services.Runner.RunAsync(GitText.Get("op.commit"), () => Services.Commands.CommitAsync(entries, message), refreshAssets: false);
            if (result.Success)
            {
                _message.value = TemplateText();
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
            if (item.MetaSource is GitStatusEntry meta)
            {
                // 묶인 줄에서 한쪽만: 커밋에 넣기 / 되돌리기.
                menu.AppendAction(GitText.Get("pending.checkAssetOnly"), _ => CheckOnly(entry, meta));
                menu.AppendAction(GitText.Get("pending.checkMetaOnly"), _ => CheckOnly(meta, entry));
                menu.AppendAction(GitText.Get("pending.discardAssetOnly"), _ => Discard(new List<GitStatusEntry> { entry }),
                    enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                menu.AppendAction(GitText.Get("pending.discardMetaOnly"), _ => Discard(new List<GitStatusEntry> { meta }),
                    enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            }

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
            if (await Services.Dialogs.ConfirmAsync(GitText.Get("pending.discard"), GitText.Format("pending.discardMessage", list), GitText.Get("op.discard"),
                    danger: true, icon: ActionGlyphKind.Undo) == false)
            {
                return;
            }
            await Services.Runner.RunAsync(GitText.Get("op.discard"), () => Services.Commands.DiscardAsync(entries), refreshAssets: true);
        }
    }
}
