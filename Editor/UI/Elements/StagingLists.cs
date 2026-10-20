using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Fork·GitKraken 레이아웃의 Unstaged(위)·Staged(아래) 목록(<c>StagingLists.uxml</c>).
    ///
    /// 한 번 누르면 그 파일의 diff 를 알리고(<see cref="FileSelected"/>), 더블클릭하면 반대쪽 목록으로 옮긴다(Stage·Unstage).
    /// Shift(범위)·Ctrl(하나씩)로 여러 파일을 고를 수 있다. 머리 줄 버튼·우클릭 메뉴는 고른 파일이 있으면 그 파일들만, 없으면 전부를 옮긴다.
    /// 기본은 에셋과 .meta 를 한 줄("A.png +meta")로 묶어 보이고 짝을 같이 옮긴다(<see cref="GitVcsSettings.GroupMetaPairs"/>). 한쪽만 옮기려면 우클릭의 "에셋만"·".meta 만".
    /// 묶어 보기를 끄면 <see cref="GitVcsSettings.StageMetaPairs"/> 를 켰을 때만 짝을 같이 옮긴다(<see cref="GitStaging.WithMetaPairs"/>).
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
        /// <summary>
        /// 고른 파일들. 한쪽 목록에서 고르면 다른 쪽 선택은 푼다(비어 있으면 고른 것 없음).
        /// </summary>
        private List<GitStatusEntry> _selectedUnstaged = new();
        private List<GitStatusEntry> _selectedStaged = new();

        /// <summary>
        /// 마지막으로 받은 status 원본(짐작을 얹기 전).
        /// </summary>
        private IReadOnlyList<GitStatusEntry> _lastEntries = Array.Empty<GitStatusEntry>();

        /// <summary>
        /// 진행 중인 Stage·Unstage 의 결과 짐작. 끝나면 null.
        /// </summary>
        private Func<IReadOnlyList<GitStatusEntry>, List<GitStatusEntry>> _prediction;
        private readonly FileActionMenu _unstagedMenu;
        private readonly FileActionMenu _stagedMenu;

        /// <summary>
        /// 파일을 골랐을 때 그 diff 대상. 선택이 풀리면 null.
        /// </summary>
        public event Action<GitDiffTarget> FileSelected;

        public VisualElement Root { get; }
        public int StagedCount => _stagedEntries.Count;
        public int UnstagedCount => _unstagedEntries.Count;

        /// <summary>
        /// Fork 방식 머리 줄 버튼: 기본은 "Stage"·"Unstage" 로 고른 파일만 옮기고(고른 게 없으면 꺼진다),
        /// Shift 를 누르고 있는 동안만 "Stage All"·"Unstage All" 로 바뀌어 전부를 옮긴다.
        /// 끄면(GitKraken 방식) 고른 게 없을 때 "Stage All"·"Unstage All" 이 된다.
        /// </summary>
        public bool ShiftForAll
        {
            get => _shiftForAll;
            set
            {
                _shiftForAll = value;
                // Fork 모드 표시(머리 줄 버튼을 초록·빨강 대신 무채색으로, USS .staging--fork).
                Root.EnableInClassList("staging--fork", value);
                ApplyRowActions();
                // Fork 모드에는 트리 위 "모두 펼치기 / 모두 접기" 버튼이 없다(폴더 우클릭 메뉴로 한다).
                _unstaged.ShowExpandToggle = value == false;
                _staged.ShowExpandToggle = value == false;
                Render();
            }
        }

        /// <summary>
        /// GitKraken 방식이면 줄에 마우스를 올리면 오른쪽에 그 파일 하나만 옮기는 버튼이 나온다(골라 둔 여러 파일과 상관없다).
        /// Fork 방식(<see cref="ShiftForAll"/>)에는 그런 버튼이 없다 — 머리 줄 버튼·더블클릭·메뉴로 옮긴다.
        /// </summary>
        private void ApplyRowActions()
        {
            if (_shiftForAll)
            {
                _unstaged.SetRowAction(null, null, null);
                _staged.SetRowAction(null, null, null);
                return;
            }
            _unstaged.SetRowAction(GitText.Get("staging.stageFile"), "file-row__action--stage", item => Stage(new[] { (GitStatusEntry)item.Source }));
            _staged.SetRowAction(GitText.Get("staging.unstageFile"), "file-row__action--unstage", item => Unstage(new[] { (GitStatusEntry)item.Source }));
        }

        private bool _shiftForAll;

        /// <summary>
        /// 지금 Shift 를 누르고 있는지(<see cref="ShiftForAll"/> 일 때 버튼 글자를 바꾸는 데 쓴다).
        /// </summary>
        private bool _shiftHeld;

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
                MultiSelect = true,
            };
            _staged = new FileBrowser(Root.Require<VisualElement>("staged-files"), null, services)
            {
                DiffTargetOf = item => GitDiffTarget.ForStaging((GitStatusEntry)item.Source, staged: true),
                ChooseAction = item => Unstage(new[] { (GitStatusEntry)item.Source }),
                FileMenu = PopulateStagedMenu,
                MultiSelect = true,
            };
            switchHost.Q(className: "view-mode-switch")?.AddToClassList("view-mode-switch--large");
            ApplyRowActions();
            _unstaged.SelectionChanged += item => OnSelected(item, staged: false);
            _staged.SelectionChanged += item => OnSelected(item, staged: true);
            _unstagedMenu = new FileActionMenu(services, GitDiffSource.Unstaged);
            _stagedMenu = new FileActionMenu(services, GitDiffSource.Staged);
            _unstaged.FolderPathContextMenuRequested += PopulateUnstagedFolderMenu;
            _staged.FolderPathContextMenuRequested += PopulateStagedFolderMenu;

            // 누른 순간의 Shift 는 클릭 이벤트에서 읽는다(키 추적이 놓쳐도 맞게). null 이면 전부.
            _stageButton.clickable.clickedWithEventInfo += evt => Stage(ButtonTargets(_selectedUnstaged, ShiftOf(evt)));
            _unstageButton.clickable.clickedWithEventInfo += evt => Unstage(ButtonTargets(_selectedStaged, ShiftOf(evt)));

            // Fork 방식이면 Shift 를 누르고 있는 동안 버튼 글자를 "… All" 로 바꾼다. 키는 창 어디에 포커스가 있든 받도록 패널 맨 위에서 듣고,
            // 다른 창에서 손을 뗀 경우를 위해 마우스가 움직일 때의 Shift 로도 맞춘다.
            Root.RegisterCallback<AttachToPanelEvent>(evt =>
            {
                evt.destinationPanel.visualTree.RegisterCallback<KeyDownEvent>(OnShiftKey, TrickleDown.TrickleDown);
                evt.destinationPanel.visualTree.RegisterCallback<KeyUpEvent>(OnShiftKey, TrickleDown.TrickleDown);
            });
            Root.RegisterCallback<DetachFromPanelEvent>(evt =>
            {
                evt.originPanel.visualTree.UnregisterCallback<KeyDownEvent>(OnShiftKey, TrickleDown.TrickleDown);
                evt.originPanel.visualTree.UnregisterCallback<KeyUpEvent>(OnShiftKey, TrickleDown.TrickleDown);
            });
            Root.RegisterCallback<PointerMoveEvent>(evt => SetShiftHeld(evt.shiftKey), TrickleDown.TrickleDown);
            GitVcsSettings.MetaGroupingChanged += OnMetaGroupingChanged;

            // 파일을 고르고 Delete(맥은 Backspace 도)를 누르면 그 파일들의 변경을 버릴지 Git 창 위 팝업으로 묻는다.
            Root.RegisterCallback<KeyDownEvent>(OnDeleteKey);
        }

        private void OnDeleteKey(KeyDownEvent evt)
        {
            if (evt.keyCode != UnityEngine.KeyCode.Delete && evt.keyCode != UnityEngine.KeyCode.Backspace)
            {
                return;
            }
            // 입력란 안의 지우기 키는 글자를 지우는 것이다.
            if (evt.target is VisualElement target && (target is TextField || target.GetFirstAncestorOfType<TextField>() != null))
            {
                return;
            }
            bool staged = _selectedStaged.Count > 0;
            List<GitStatusEntry> selected = staged ? _selectedStaged : _selectedUnstaged;
            if (selected.Count == 0)
            {
                return;
            }
            evt.StopPropagation();
            Discard(selected.ToList(), staged);
        }

        /// <summary>
        /// "에셋과 .meta 묶어 보기" 를 바꿨다. 같은 status 로 줄만 다시 만든다.
        /// </summary>
        private void OnMetaGroupingChanged()
        {
            SetEntries(_lastEntries);
        }

        private void OnShiftKey<T>(KeyboardEventBase<T> evt) where T : KeyboardEventBase<T>, new()
        {
            bool isShiftKey = evt.keyCode == UnityEngine.KeyCode.LeftShift || evt.keyCode == UnityEngine.KeyCode.RightShift;
            SetShiftHeld(isShiftKey ? evt is KeyDownEvent : evt.shiftKey);
        }

        private void SetShiftHeld(bool held)
        {
            if (_shiftHeld == held)
            {
                return;
            }
            _shiftHeld = held;
            if (_shiftForAll)
            {
                Render();
            }
        }

        private bool ShiftOf(EventBase evt)
        {
            return evt switch
            {
                IPointerEvent pointer => pointer.shiftKey,
                IMouseEvent mouse => mouse.shiftKey,
                IKeyboardEvent key => key.shiftKey,
                _ => _shiftHeld,
            };
        }

        /// <summary>
        /// 머리 줄 버튼이 옮길 파일. null 이면 전부.
        /// Fork 방식: Shift 면 전부, 아니면 고른 것(없으면 아무것도 — 버튼이 꺼져 있다). GitKraken 방식: 고른 게 없으면 전부.
        /// </summary>
        private List<GitStatusEntry> ButtonTargets(List<GitStatusEntry> selected, bool shift)
        {
            if (_shiftForAll)
            {
                return shift ? null : selected.ToList();
            }
            return selected.Count > 0 ? selected.ToList() : null;
        }

        /// <summary>
        /// 저장소에서 읽은 status 를 보인다. Stage·Unstage 를 하는 중이면 그 결과 짐작을 얹어 보인다 —
        /// 그 사이 다른 새로고침(에셋 저장 등)이 끼어들어도 옮긴 파일이 제자리로 튀었다 돌아오지 않게.
        /// </summary>
        public void SetEntries(IReadOnlyList<GitStatusEntry> entries)
        {
            _lastEntries = entries;
            ShowEntries(_prediction != null ? _prediction(entries) : entries);
        }

        /// <summary>
        /// 목록이 다시 채워졌을 때(짐작을 보일 때 포함). 커밋 상자의 stage 개수처럼 목록 옆 숫자를 맞추는 데 쓴다.
        /// </summary>
        public event Action EntriesShown;

        /// <summary>
        /// GitKraken 처럼 누르자마자 목록을 옮겨 보이고(<paramref name="predict"/>), git 이 끝난 뒤 오는 진짜 status 로 맞춘다.
        /// 실패하면 새로고침이 원래 상태를 그대로 보이므로 따로 되돌릴 것이 없다.
        /// </summary>
        private async void RunPredicted(string title, Func<Task<GitResult>> operation, Func<IReadOnlyList<GitStatusEntry>, List<GitStatusEntry>> predict)
        {
            _prediction = predict;
            ShowEntries(predict(_lastEntries));
            try
            {
                await _services.Runner.RunAsync(title, operation, refreshAssets: false, workingTreeOnly: true);
            }
            finally
            {
                _prediction = null;
            }
        }

        private void ShowEntries(IReadOnlyList<GitStatusEntry> entries)
        {
            _unstagedEntries = GitStaging.Unstaged(entries);
            _stagedEntries = GitStaging.Staged(entries);
            _unstaged.SetItems(FileListItem.FromStatus(_unstagedEntries));
            _staged.SetItems(FileListItem.FromStatus(_stagedEntries));

            // 새로고침 뒤에도 같은 파일이 남아 있으면 선택을 이어 간다(목록 쪽이 경로로 되살린다). 사라졌으면 놓고 diff 도 비운다
            // (stage 해서 반대쪽으로 옮겨 간 파일의 diff 가 빈 채로 남지 않게).
            bool hadSelection = _selectedUnstaged.Count > 0 || _selectedStaged.Count > 0;
            _selectedUnstaged = StillPresent(_selectedUnstaged, _unstagedEntries);
            _selectedStaged = StillPresent(_selectedStaged, _stagedEntries);
            Render();
            if (hadSelection && _selectedUnstaged.Count == 0 && _selectedStaged.Count == 0)
            {
                FileSelected?.Invoke(null);
            }
            EntriesShown?.Invoke();
        }

        /// <summary>
        /// 새로 읽은 목록에 아직 있는 고른 파일(경로로 맞춘 새 항목).
        /// </summary>
        private static List<GitStatusEntry> StillPresent(List<GitStatusEntry> selected, List<GitStatusEntry> entries)
        {
            if (selected.Count == 0)
            {
                return selected;
            }
            HashSet<string> paths = new(selected.Select(e => e.RepoPath));
            return entries.Where(e => paths.Contains(e.RepoPath)).ToList();
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
            GitVcsSettings.MetaGroupingChanged -= OnMetaGroupingChanged;
            _unstaged.Dispose();
            _staged.Dispose();
        }

        private void Render()
        {
            _unstagedTitle.text = GitText.Format("staging.unstagedTitle", _unstagedEntries.Count);
            _stagedTitle.text = GitText.Format("staging.stagedTitle", _stagedEntries.Count);
            bool idle = _services.Runner.IsBusy == false;
            RenderButton(_stageButton, _selectedUnstaged.Count, _unstagedEntries.Count, idle, "staging.stage", "staging.stageSelected", "staging.stageAll");
            RenderButton(_unstageButton, _selectedStaged.Count, _stagedEntries.Count, idle, "staging.unstage", "staging.unstageSelected", "staging.unstageAll");
            // 줄 hover 버튼(Stage File·Unstage File)도 작업 결과가 보일 때까지 끈다.
            _unstaged.SetRowActionEnabled(idle);
            _staged.SetRowActionEnabled(idle);
        }

        /// <summary>
        /// 머리 줄 버튼 글자·켜짐. Fork 방식이면 Shift 를 누른 동안만 "… All"(목록이 비지 않았으면 켜짐),
        /// 아니면 고른 파일 기준이고 고른 게 없으면 꺼진다. GitKraken 방식은 고른 게 없으면 "… All".
        /// </summary>
        private void RenderButton(Button button, int selected, int total, bool idle, string one, string many, string all)
        {
            if (_shiftForAll)
            {
                bool allMode = _shiftHeld;
                // Fork 처럼 고른 개수는 붙이지 않는다("Stage"·"Unstage").
                button.text = GitText.Get(allMode ? all : one);
                button.SetEnabled(idle && (allMode ? total > 0 : selected > 0));
                return;
            }
            button.text = ButtonText(selected, one, many, all);
            button.SetEnabled(idle && total > 0);
        }

        /// <summary>
        /// 고른 게 없으면 "모두 …", 하나면 "…", 여럿이면 "n개 …".
        /// </summary>
        private static string ButtonText(int selected, string one, string many, string all)
        {
            return selected switch
            {
                0 => GitText.Get(all),
                1 => GitText.Get(one),
                _ => GitText.Format(many, selected),
            };
        }

        private void OnSelected(FileListItem item, bool staged)
        {
            GitStatusEntry entry = item?.Source as GitStatusEntry;
            FileBrowser browser = staged ? _staged : _unstaged;
            List<GitStatusEntry> selected = browser.SelectedItems.Select(i => (GitStatusEntry)i.Source).ToList();
            if (staged)
            {
                _selectedStaged = selected;
                if (selected.Count > 0)
                {
                    _selectedUnstaged = new List<GitStatusEntry>();
                    _unstaged.ClearSelection();
                }
            }
            else
            {
                _selectedUnstaged = selected;
                if (selected.Count > 0)
                {
                    _selectedStaged = new List<GitStatusEntry>();
                    _staged.ClearSelection();
                }
            }
            Render();
            if (entry != null || (_selectedStaged.Count == 0 && _selectedUnstaged.Count == 0))
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
            List<GitStatusEntry> group = SelectionGroup(_selectedUnstaged, entry);
            if (group.Count > 1)
            {
                // 여럿을 골라 우클릭: 고른 것 전부를 옮기거나 버린다. 버리기는 새 파일을 빼고(하나일 때와 같다) 남은 게 있을 때만.
                menu.AppendAction(GitText.Format("staging.stageSelected", group.Count), _ => Stage(group), StatusOfIdle());
                List<GitStatusEntry> discardable = group.Where(e => e.State != GitFileState.Untracked).ToList();
                if (discardable.Count > 0)
                {
                    menu.AppendAction(GitText.Format("staging.discardSelected", discardable.Count), _ => Discard(discardable), StatusOfIdle());
                }
                menu.AppendSeparator();
                _unstagedMenu.AppendCommonItems(menu, entry);
                return;
            }
            menu.AppendAction(GitText.Get("staging.stage"), _ => Stage(new[] { entry }), StatusOfIdle());
            if (entry.State != GitFileState.Untracked)
            {
                menu.AppendAction(GitText.Get("staging.discard"), _ => Discard(new[] { entry }), StatusOfIdle());
            }
            if (item.MetaSource is GitStatusEntry meta)
            {
                // 묶인 줄에서 한쪽만: 에셋만 / .meta 만.
                menu.AppendAction(GitText.Get("staging.stageAssetOnly"), _ => Stage(new[] { entry }, withPairs: false), StatusOfIdle());
                menu.AppendAction(GitText.Get("staging.stageMetaOnly"), _ => Stage(new[] { meta }, withPairs: false), StatusOfIdle());
                // 변경 버리기는 위와 같이 새 파일에는 없다.
                if (entry.State != GitFileState.Untracked)
                {
                    menu.AppendAction(GitText.Get("staging.discardAssetOnly"), _ => Discard(new[] { entry }, withPairs: false), StatusOfIdle());
                }
                if (meta.State != GitFileState.Untracked)
                {
                    menu.AppendAction(GitText.Get("staging.discardMetaOnly"), _ => Discard(new[] { meta }, withPairs: false), StatusOfIdle());
                }
            }
            _unstagedMenu.AppendWorkingItems(menu, entry);
            _unstagedMenu.AppendCommonItems(menu, entry);
        }

        private void PopulateStagedMenu(DropdownMenu menu, FileListItem item)
        {
            GitStatusEntry entry = (GitStatusEntry)item.Source;
            List<GitStatusEntry> group = SelectionGroup(_selectedStaged, entry);
            menu.AppendAction(group.Count > 1 ? GitText.Format("staging.unstageSelected", group.Count) : GitText.Get("staging.unstage"),
                _ => Unstage(group), StatusOfIdle());
            if (group.Count == 1 && item.MetaSource is GitStatusEntry meta)
            {
                menu.AppendAction(GitText.Get("staging.unstageAssetOnly"), _ => Unstage(new[] { entry }, withPairs: false), StatusOfIdle());
                menu.AppendAction(GitText.Get("staging.unstageMetaOnly"), _ => Unstage(new[] { meta }, withPairs: false), StatusOfIdle());
            }
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

        /// <summary>
        /// 우클릭한 파일이 고른 여러 파일 중 하나면 고른 것 전부, 아니면 그 파일 하나.
        /// </summary>
        private static List<GitStatusEntry> SelectionGroup(List<GitStatusEntry> selected, GitStatusEntry clicked)
        {
            return selected.Count > 1 && selected.Any(e => e.RepoPath == clicked.RepoPath) ? selected.ToList() : new List<GitStatusEntry> { clicked };
        }

        /// <summary>
        /// 옮길 파일. 설정에서 ".meta 짝 함께 옮기기"를 켰으면 같은 목록의 짝을 더하고, 아니면 고른 그대로(Fork·GitKraken 과 같다).
        /// </summary>
        private static List<GitStatusEntry> WithPairs(IEnumerable<GitStatusEntry> entries, IReadOnlyList<GitStatusEntry> sameSide)
        {
            return GitVcsSettings.instance.MovesMetaPairs ? GitStaging.WithMetaPairs(entries, sameSide) : entries.ToList();
        }

        private DropdownMenuAction.Status StatusOfIdle()
        {
            return _services.Runner.IsBusy ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal;
        }

        /// <param name="entries">null 이면 전부.</param>
        /// <param name="withPairs">false 면 .meta 짝을 더하지 않고 고른 그대로(우클릭 "에셋만"·".meta 만").</param>
        private void Stage(IEnumerable<GitStatusEntry> entries, bool withPairs = true)
        {
            // 앞 작업의 결과가 보이기 전(버튼이 꺼진 동안) 더블클릭·hover 버튼·메뉴로 들어온 입력은 무시한다.
            // 빈 목록도 무시한다 — 경로 없는 git add -A 는 전부를 올린다.
            if (_services.Runner.IsBusy || (entries != null && entries.Any() == false))
            {
                return;
            }
            // 열린 씬·프리팹의 저장 안 된 수정은 stage 되지 않는다. 저장을 거부하면 멈춘다.
            if (_services.Dialogs.SaveOpenWork() == false)
            {
                return;
            }
            // 별도 git 저장소 폴더가 끼어 있으면 묻는다. 기본은 빼고 나머지만 올린다(그대로 올리면 파일이 아니라 gitlink 가 올라간다).
            List<GitStatusEntry> candidates = entries == null ? _unstagedEntries.ToList() : withPairs ? WithPairs(entries, _unstagedEntries) : entries.ToList();
            List<GitStatusEntry> embedded = candidates.Where(e => GitStaging.IsEmbeddedRepository(e, GitRepository.RepoRoot)).ToList();
            bool skipEmbedded = false;
            if (embedded.Count > 0)
            {
                string list = string.Join("\n", embedded.Take(10).Select(e => e.RepoPath));
                int choice = _services.Dialogs.Choose(GitText.Get("staging.embeddedTitle"), GitText.Format("staging.embeddedMessage", list),
                    GitText.Get("staging.embeddedSkip"), GitText.Get("staging.embeddedInclude"));
                if (choice == 1)
                {
                    return;
                }
                skipEmbedded = choice == 0;
            }
            if (skipEmbedded)
            {
                HashSet<string> embeddedPaths = new(embedded.Select(e => e.RepoPath));
                candidates = candidates.Where(e => embeddedPaths.Contains(e.RepoPath) == false).ToList();
                if (candidates.Count == 0)
                {
                    return;
                }
            }
            else if (entries == null)
            {
                RunPredicted(GitText.Get("op.stage"), _services.Commands.StageAllAsync, status => GitStaging.PredictStage(status, null));
                return;
            }
            List<GitStatusEntry> targets = candidates;
            HashSet<string> paths = new(targets.Select(e => e.RepoPath));
            RunPredicted(GitText.Get("op.stage"), () => _services.Commands.StageAsync(targets), status => GitStaging.PredictStage(status, paths));
        }

        /// <param name="entries">null 이면 전부.</param>
        /// <param name="withPairs">false 면 .meta 짝을 더하지 않는다.</param>
        private void Unstage(IEnumerable<GitStatusEntry> entries, bool withPairs = true)
        {
            // 빈 목록도 무시한다 — 경로 없는 restore·rm 이 엉뚱하게 돌지 않게.
            if (_services.Runner.IsBusy || (entries != null && entries.Any() == false))
            {
                return;
            }
            if (entries == null)
            {
                RunPredicted(GitText.Get("op.unstage"), _services.Commands.UnstageAllAsync, status => GitStaging.PredictUnstage(status, null));
                return;
            }
            List<GitStatusEntry> targets = withPairs ? WithPairs(entries, _stagedEntries) : entries.ToList();
            HashSet<string> paths = new(targets.Select(e => e.RepoPath));
            RunPredicted(GitText.Get("op.unstage"), () => _services.Commands.UnstageAsync(targets), status => GitStaging.PredictUnstage(status, paths));
        }

        /// <summary>
        /// 변경을 버린다. 되돌릴 수 없으므로 Git 창 위 팝업(<see cref="GitConfirmDialog"/>)으로 한 번 더 묻는다.
        /// Unstaged 면 작업 트리 쪽만(stage 한 내용은 남는다), Staged 면 stage 한 것까지 마지막 커밋 상태로 되돌린다. 새 파일은 지운다.
        /// </summary>
        private async void Discard(IEnumerable<GitStatusEntry> entries, bool staged = false, bool withPairs = true)
        {
            if (_services.Runner.IsBusy)
            {
                return;
            }
            List<GitStatusEntry> sameSide = staged ? _stagedEntries : _unstagedEntries;
            List<GitStatusEntry> targets = withPairs ? WithPairs(entries, sameSide) : entries.ToList();
            if (targets.Count == 0)
            {
                return;
            }
            string list = string.Join("\n", targets.Take(10).Select(e => e.ProjectPath ?? e.RepoPath))
                          + (targets.Count > 10 ? GitText.Format("pending.discardMore", targets.Count - 10) : string.Empty);
            string title = targets.Count > 1 ? GitText.Format("staging.discardSelected", targets.Count) : GitText.Get("staging.discard");
            string message = GitText.Format(staged ? "staging.discardStagedMessage" : "staging.discardMessage", list);
            // Fork 방식은 Fork 처럼 파일 목록 없이 짧게 묻는 경고창.
            Task<bool> confirm = _shiftForAll
                ? GitConfirmDialog.ShowAlertAsync(_services, ActionGlyphKind.Undo, GitText.Get("staging.discardAlertTitle"),
                    GitText.Get(staged ? "staging.discardAlertStagedMessage" : "staging.discardAlertMessage"), GitText.Get("staging.discardAlertOk"))
                : GitConfirmDialog.ShowAsync(_services, ActionGlyphKind.Undo, title, message, GitText.Get("op.discard"));
            if (await confirm == false || _services.Runner.IsBusy)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Get("op.discard"),
                () => staged ? _services.Commands.DiscardAsync(targets) : _services.Commands.DiscardWorkTreeAsync(targets), refreshAssets: true);
        }
    }
}
