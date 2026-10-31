using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor.UIElements;
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

        /// <summary>
        /// 짐작을 얹어 보이는 마지막 시각. 작업이 어떤 이유로 끝을 알리지 못해도 짐작이 진짜 status 를 계속 가리지 않게 한다.
        /// </summary>
        private double _predictionDeadline;

        /// <summary>짐작이 진짜 status 를 덮을 수 있는 최대 시간(초).</summary>
        private const double PredictionLifetime = 3.0;

        /// <summary>바쁜 동안 누른 "모두 Stage/Unstage" — 앞 작업이 끝나면 실행한다(버린 것처럼 보이지 않게).</summary>
        private Action _queuedBulk;
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
                // 파일 찾기(돋보기·Ctrl+F)는 Fork 모드에만 있다.
                _filterButton.SetVisible(value);
                if (value == false)
                {
                    CloseFilter();
                }
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

        private const string FilterClosedModifier = "staging__filter-row--closed";
        private const string FilterHiddenModifier = "staging__filter-row--hidden";
        private readonly Button _filterButton;
        private readonly VisualElement _filterRow;
        private readonly ToolbarSearchField _filter;

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
                // Space 는 아무것도 하지 않는다. 정해 두지 않으면 목록이 Space 를 Enter 처럼 '고르기'로 받아 반대편으로 옮긴다.
                SpaceAction = IgnoreSpace,
                FileMenu = PopulateUnstagedMenu,
                MultiSelect = true,
            };
            _staged = new FileBrowser(Root.Require<VisualElement>("staged-files"), null, services)
            {
                DiffTargetOf = item => GitDiffTarget.ForStaging((GitStatusEntry)item.Source, staged: true),
                ChooseAction = item => Unstage(new[] { (GitStatusEntry)item.Source }),
                SpaceAction = IgnoreSpace,
                FileMenu = PopulateStagedMenu,
                MultiSelect = true,
            };
            switchHost.Q(className: "view-mode-switch")?.AddToClassList("view-mode-switch--large");
            ApplyRowActions();
            _unstaged.SelectionChanged += item => OnSelected(item, staged: false);
            _staged.SelectionChanged += item => OnSelected(item, staged: true);
            _unstagedMenu = new FileActionMenu(services, GitDiffSource.Unstaged);
            _stagedMenu = new FileActionMenu(services, GitDiffSource.Staged);
            (_filterButton, _filterRow, _filter) = CreateFilter(switchHost);
            _unstaged.FolderPathContextMenuRequested += PopulateUnstagedFolderMenu;
            _staged.FolderPathContextMenuRequested += PopulateStagedFolderMenu;

            // 누른 순간의 Shift 는 클릭 이벤트에서 읽는다(키 추적이 놓쳐도 맞게). null 이면 전부.
            // Button 의 Clickable 은 기본으로 수정 키 없는 왼쪽 클릭에만 반응하므로 Shift+클릭도 받게 한다.
            _stageButton.clickable.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = UnityEngine.EventModifiers.Shift });
            _unstageButton.clickable.activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse, modifiers = UnityEngine.EventModifiers.Shift });
            _stageButton.clickable.clickedWithEventInfo += evt => Stage(ButtonTargets(_selectedUnstaged, ShiftOf(evt), _unstagedEntries));
            _unstageButton.clickable.clickedWithEventInfo += evt => Unstage(ButtonTargets(_selectedStaged, ShiftOf(evt), _stagedEntries));

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
        /// <param name="side">그 버튼 쪽 목록 전체. 검색 중이면 "… All" 은 보이는 항목만 옮긴다(null = 전부 대신).</param>
        private List<GitStatusEntry> ButtonTargets(List<GitStatusEntry> selected, bool shift, List<GitStatusEntry> side)
        {
            List<GitStatusEntry> all = IsFiltering ? Filtered(side) : null;
            if (_shiftForAll)
            {
                return shift ? all : selected.ToList();
            }
            return selected.Count > 0 ? selected.ToList() : all;
        }

        private static void IgnoreSpace(IReadOnlyList<FileListItem> items)
        {
        }

        // ── 파일 찾기(Fork 모드) ───────────────────────────

        private bool IsFiltering => _filter != null && string.IsNullOrWhiteSpace(_filter.value) == false;

        private List<GitStatusEntry> Filtered(List<GitStatusEntry> entries)
        {
            if (IsFiltering == false)
            {
                return entries;
            }
            string needle = _filter.value.Trim();
            return entries.Where(e => (e.ProjectPath ?? e.RepoPath).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
        }

        /// <summary>
        /// Fork 처럼 위 도구 줄 왼쪽의 돋보기를 누르거나 Ctrl+F(macOS 는 Cmd+F)를 누르면 그 아래에 Filter 칸이 펼쳐진다.
        /// 열고 닫을 때 높이·불투명도가 움직이고(USS transition), 닫는 움직임이 끝나면 display 를 끈다. Esc 는 지우고 닫는다.
        /// </summary>
        private (Button, VisualElement, ToolbarSearchField) CreateFilter(VisualElement toolbar)
        {
            Button button = new(ToggleFilter) { tooltip = GitText.Get("staging.filterTooltip") };
            button.AddToClassList("staging__filter-button");
            button.Add(new ActionGlyph(ActionGlyphKind.Search));
            button.SetVisible(false);
            toolbar.Insert(0, button);

            VisualElement row = new();
            row.AddToClassList("staging__filter-row");
            row.AddToClassList(FilterClosedModifier);
            row.AddToClassList(FilterHiddenModifier);
            ToolbarSearchField field = new();
            field.AddToClassList("staging__filter");
            ActionGlyph.ReplaceSearchIcon(field);
            TextField input = field.Q<TextField>();
            if (input != null)
            {
                input.textEdition.placeholder = GitText.Get("refs.filterPlaceholder");
                input.textEdition.hidePlaceholderOnFocus = false;
            }
            field.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("git-search--focused"));
            field.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("git-search--focused"));
            field.RegisterValueChangedCallback(_ => ShowEntries(_prediction != null ? _prediction(_lastEntries) : _lastEntries));
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == UnityEngine.KeyCode.Escape)
                {
                    CloseFilter();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            row.Add(field);
            row.RegisterCallback<TransitionEndEvent>(_ =>
            {
                if (row.ClassListContains(FilterClosedModifier))
                {
                    row.AddToClassList(FilterHiddenModifier);
                }
            });
            toolbar.parent.Insert(toolbar.parent.IndexOf(toolbar) + 1, row);

            Root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (_shiftForAll && evt.actionKey && evt.keyCode == UnityEngine.KeyCode.F)
                {
                    OpenFilter();
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            return (button, row, field);
        }

        private void ToggleFilter()
        {
            if (_filterRow.ClassListContains(FilterClosedModifier))
            {
                OpenFilter();
            }
            else
            {
                CloseFilter();
            }
        }

        private void OpenFilter()
        {
            // display 를 먼저 켜고, 접힌 모양이 한 번 그려진 다음 틱에 펼쳐야 움직임이 보인다. 포커스도 그때 줘야 입력란이 키 입력을 받는다.
            _filterRow.RemoveFromClassList(FilterHiddenModifier);
            _filterButton.AddToClassList("staging__filter-button--on");
            TextField input = _filter.Q<TextField>();
            _filterRow.schedule.Execute(() =>
            {
                _filterRow.RemoveFromClassList(FilterClosedModifier);
                input?.Focus();
                input?.SelectAll();
            });
        }

        private void CloseFilter()
        {
            if (_filterRow == null || _filterRow.ClassListContains(FilterClosedModifier))
            {
                return;
            }
            _filter.value = string.Empty;
            _filter.Q<TextField>()?.Blur();
            _filterRow.AddToClassList(FilterClosedModifier);
            _filterButton.RemoveFromClassList("staging__filter-button--on");
        }

        /// <summary>
        /// 저장소에서 읽은 status 를 보인다. Stage·Unstage 를 하는 중이면 그 결과 짐작을 얹어 보인다 —
        /// 그 사이 다른 새로고침(에셋 저장 등)이 끼어들어도 옮긴 파일이 제자리로 튀었다 돌아오지 않게.
        /// </summary>
        public void SetEntries(IReadOnlyList<GitStatusEntry> entries)
        {
            _lastEntries = entries;
            if (_prediction != null && UnityEditor.EditorApplication.timeSinceStartup > _predictionDeadline)
            {
                _prediction = null;
            }
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
        /// <param name="saveFirst">git 을 부르기 전에 열린 씬·에셋을 저장한다. 짐작을 먼저 보인 뒤 저장해 누른 순간 목록이 움직이게 한다.</param>
        private async void RunPredicted(string title, Func<Task<GitResult>> operation, Func<IReadOnlyList<GitStatusEntry>, List<GitStatusEntry>> predict, bool saveFirst = false)
        {
            Func<IReadOnlyList<GitStatusEntry>, List<GitStatusEntry>> mine = predict;
            _prediction = mine;
            _predictionDeadline = UnityEditor.EditorApplication.timeSinceStartup + PredictionLifetime;
            ShowEntries(predict(_lastEntries));
            try
            {
                if (saveFirst)
                {
                    // 그림이 바뀐 틀을 먼저 내보낸 뒤 저장한다(저장이 메인 스레드를 잡는 동안에도 누른 결과가 보이게).
                    await Task.Yield();
                    if (_services.Dialogs.SaveOpenWork() == false)
                    {
                        return;
                    }
                }
                await _services.Runner.RunAsync(title, operation, refreshAssets: false, workingTreeOnly: true);
            }
            finally
            {
                // 그 사이 다른 작업이 새 짐작을 걸었으면 그것은 남긴다.
                if (_prediction == mine)
                {
                    _prediction = null;
                    ShowEntries(_lastEntries);
                }
            }
        }

        private void RunQueuedBulk()
        {
            if (_queuedBulk == null || _services.Runner.IsBusy)
            {
                return;
            }
            Action queued = _queuedBulk;
            _queuedBulk = null;
            queued();
        }

        private void ShowEntries(IReadOnlyList<GitStatusEntry> entries)
        {
            // 충돌 파일을 맨 위로(먼저 해결할 것이 먼저 보이게).
            _unstagedEntries = GitStaging.Unstaged(entries).OrderBy(e => e.State == GitFileState.Conflicted ? 0 : 1).ToList();
            _stagedEntries = GitStaging.Staged(entries);
            _isShowingEntries = true;
            try
            {
                _unstaged.SetItems(FileListItem.FromStatus(Filtered(_unstagedEntries)));
                _staged.SetItems(FileListItem.FromStatus(Filtered(_stagedEntries)));
            }
            finally
            {
                _isShowingEntries = false;
            }

            // 새로고침 뒤에도 같은 파일이 남아 있으면 선택을 이어 간다(목록 쪽이 경로로 되살린다). 사라졌으면 놓고 diff 도 비운다
            // (stage 해서 반대쪽으로 옮겨 간 파일의 diff 가 빈 채로 남지 않게).
            bool hadSelection = _selectedUnstaged.Count > 0 || _selectedStaged.Count > 0;
            _selectedUnstaged = StillPresent(_selectedUnstaged, _unstagedEntries);
            _selectedStaged = StillPresent(_selectedStaged, _stagedEntries);
            Render();
            if (hadSelection && _selectedUnstaged.Count == 0 && _selectedStaged.Count == 0)
            {
                _shownEntry = null;
                FileSelected?.Invoke(null);
            }
            else
            {
                RefreshShownEntry();
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
            RunQueuedBulk();
        }

        public void ClearSelection()
        {
            _unstaged.ClearSelection();
            _staged.ClearSelection();
        }

        /// <summary>
        /// 첫 충돌 파일을 골라 그 diff 를 연다("충돌 보기"). 충돌 파일이 없으면 false.
        /// </summary>
        public bool SelectFirstConflict()
        {
            GitStatusEntry conflict = _unstagedEntries.FirstOrDefault(e => e.State == GitFileState.Conflicted);
            return conflict != null && _unstaged.SelectFile(conflict.RepoPath);
        }

        /// <summary>
        /// 그 파일을 골라 diff 를 연다(Project 창 "Git 창에서 보기"). Unstaged 에 있으면 그쪽, 아니면 Staged. 에셋이 그대로이고 .meta 만 바뀌었으면 .meta 를 고른다.
        /// </summary>
        public bool SelectEntry(string repoPath)
        {
            foreach (string path in new[] { repoPath, repoPath + ".meta" })
            {
                if (_unstagedEntries.Any(e => e.RepoPath == path) && _unstaged.SelectFile(path))
                {
                    return true;
                }
                if (_stagedEntries.Any(e => e.RepoPath == path) && _staged.SelectFile(path))
                {
                    return true;
                }
            }
            return false;
        }

        public void Dispose()
        {
            GitVcsSettings.MetaGroupingChanged -= OnMetaGroupingChanged;
            _unstaged.Dispose();
            _staged.Dispose();
        }

        private void Render()
        {
            int conflicts = _unstagedEntries.Count(e => e.State == GitFileState.Conflicted);
            _unstagedTitle.text = GitText.Format("staging.unstagedTitle", _unstagedEntries.Count)
                                  + (conflicts > 0 ? GitText.Format("staging.conflictCount", conflicts) : string.Empty);
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

        /// <summary>
        /// 목록을 다시 채우는 중(<see cref="ShowEntries"/>). 다시 채우는 동안 목록이 보내는 선택 알림(선택 없음 → 같은 파일)은 사용자가 고른 것이 아니다 —
        /// 그대로 넘기면 diff 가 비었다가 다시 읽혀 깜빡이고 고른 글이 지워진다. 남은 선택은 ShowEntries 가 맞춘다.
        /// </summary>
        private bool _isShowingEntries;

        private void OnSelected(FileListItem item, bool staged)
        {
            if (_isShowingEntries)
            {
                return;
            }
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
                _shownEntry = entry;
                _shownStaged = staged;
                FileSelected?.Invoke(entry == null ? null : GitDiffTarget.ForStaging(entry, staged));
            }
        }

        /// <summary>diff 에 보이고 있는 파일 항목과 그 쪽(Staged 인지). <see cref="ShowEntries"/> 가 상태가 바뀌었는지 보는 데 쓴다.</summary>
        private GitStatusEntry _shownEntry;
        private bool _shownStaged;

        /// <summary>
        /// 목록을 다시 채운 뒤: diff 에 보이는 파일이 같은 쪽 목록에 남아 있는데 상태가 바뀌었으면(새 파일의 일부 줄을 Stage 해 untracked → 추가·수정 등)
        /// 새 항목으로 diff 를 다시 맞춘다. 다시 채우는 동안의 선택 알림은 막으므로(깜빡임) 여기서 따로 알린다 — 그러지 않으면 diff 가 옛 상태로
        /// 읽어(untracked 면 빈 파일과 비교) 방금 Stage 한 줄이 그대로 남아 보인다.
        /// </summary>
        private void RefreshShownEntry()
        {
            if (_shownEntry == null)
            {
                return;
            }
            GitStatusEntry current = (_shownStaged ? _stagedEntries : _unstagedEntries).FirstOrDefault(e => e.RepoPath == _shownEntry.RepoPath);
            if (current == null)
            {
                return;
            }
            bool changed = current.State != _shownEntry.State || current.IndexCode != _shownEntry.IndexCode || current.WorkTreeCode != _shownEntry.WorkTreeCode;
            _shownEntry = current;
            if (changed)
            {
                FileSelected?.Invoke(GitDiffTarget.ForStaging(current, _shownStaged));
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
            List<GitStatusEntry> entries = WithFolderMeta(items, displayFolder, _unstagedEntries);
            menu.AppendAction(GitText.Get("menu.stageFolder"), _ => Stage(entries), StatusOfIdle());
            menu.AppendAction(GitText.Get("menu.discardFolder"), _ => Discard(entries), StatusOfIdle());
            _unstagedMenu.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(displayFolder, items[0]), entries);
        }

        private void PopulateStagedFolderMenu(DropdownMenu menu, string displayFolder, IReadOnlyList<FileListItem> items)
        {
            List<GitStatusEntry> entries = WithFolderMeta(items, displayFolder, _stagedEntries);
            menu.AppendAction(GitText.Get("menu.unstageFolder"), _ => Unstage(entries), StatusOfIdle());
            _stagedMenu.AppendFolderItems(menu, FileActionMenu.RepoFolderOf(displayFolder, items[0]), entries);
        }

        /// <summary>
        /// 폴더 안 파일들에 폴더 자신의 .meta(<c>Folder.meta</c>)를 더한다. Unity 에서 폴더와 그 .meta 는 한 몸이라,
        /// 빠뜨리면 폴더 없이 .meta 만 남거나(다른 브랜치로 바꾸면 빈 폴더가 다시 생긴다) 그 반대가 된다.
        /// </summary>
        private static List<GitStatusEntry> WithFolderMeta(IReadOnlyList<FileListItem> items, string displayFolder, IReadOnlyList<GitStatusEntry> sameSide)
        {
            List<GitStatusEntry> entries = items.Select(i => (GitStatusEntry)i.Source).ToList();
            if (items.Count == 0)
            {
                return entries;
            }
            string folderMeta = FileActionMenu.RepoFolderOf(displayFolder, items[0]).TrimEnd('/') + ".meta";
            GitStatusEntry meta = sameSide.FirstOrDefault(e => e.RepoPath == folderMeta);
            if (meta != null && entries.All(e => e.RepoPath != folderMeta))
            {
                entries.Add(meta);
            }
            return entries;
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
        private async void Stage(IEnumerable<GitStatusEntry> entries, bool withPairs = true)
        {
            // 빈 목록은 무시한다 — 경로 없는 git add -A 는 전부를 올린다.
            if (entries != null && entries.Any() == false)
            {
                return;
            }
            if (_services.Runner.IsBusy)
            {
                // "모두 Stage" 는 자리가 움직이지 않는 머리 버튼이라 앞 작업이 끝나면 이어서 한다.
                // 파일 하나씩은 무시한다 — 목록이 밀려 커서 밑에 온 다른 파일을 두 번째 클릭으로 올리지 않게.
                if (entries == null)
                {
                    _queuedBulk = () => Stage(null, withPairs);
                }
                return;
            }
            // 별도 git 저장소 폴더가 끼어 있으면 묻는다. 기본은 빼고 나머지만 올린다(그대로 올리면 파일이 아니라 gitlink 가 올라간다).
            List<GitStatusEntry> candidates = entries == null ? _unstagedEntries.ToList() : withPairs ? WithPairs(entries, _unstagedEntries) : entries.ToList();
            List<GitStatusEntry> embedded = candidates.Where(e => GitStaging.IsEmbeddedRepository(e, GitRepository.RepoRoot)).ToList();
            bool skipEmbedded = false;
            if (embedded.Count > 0)
            {
                string list = string.Join("\n", embedded.Take(10).Select(e => e.RepoPath));
                int choice = await _services.Dialogs.ChooseAsync(GitText.Get("staging.embeddedTitle"), GitText.Format("staging.embeddedMessage", list),
                    GitText.Get("staging.embeddedSkip"), GitText.Get("staging.embeddedInclude"));
                if (choice == 1)
                {
                    return;
                }
                skipEmbedded = choice == 0;
            }
            // 충돌 표시(<<<<<<< ======= >>>>>>>)가 남은 파일을 올리면 git 은 그 파일을 해결된 것으로 본다. 어느 경로로 올리든 먼저 묻는다.
            List<string> marked = candidates.Where(e => GitInProgress.HasConflictMarkers(FileActionMenu.FullPath(e.RepoPath))).Select(e => e.RepoPath).ToList();
            if (marked.Count > 0)
            {
                string list = string.Join("\n", marked.Take(10));
                if (await _services.Dialogs.ConfirmAsync(GitText.Get("staging.markersTitle"), GitText.Format("staging.markersMessage", list), GitText.Get("staging.markersOk"),
                        danger: true) == false)
                {
                    return;
                }
            }
            if (_services.Runner.IsBusy)
            {
                return;
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
                RunPredicted(GitText.Get("op.stage"), _services.Commands.StageAllAsync, status => GitStaging.PredictStage(status, null), saveFirst: true);
                return;
            }
            List<GitStatusEntry> targets = candidates;
            HashSet<string> paths = new(targets.Select(e => e.RepoPath));
            // 열린 씬·프리팹의 저장 안 된 수정은 stage 되지 않는다. 저장은 목록을 먼저 옮겨 보인 뒤에 한다.
            RunPredicted(GitText.Get("op.stage"), () => _services.Commands.StageAsync(targets), status => GitStaging.PredictStage(status, paths), saveFirst: true);
        }

        /// <param name="entries">null 이면 전부.</param>
        /// <param name="withPairs">false 면 .meta 짝을 더하지 않는다.</param>
        private void Unstage(IEnumerable<GitStatusEntry> entries, bool withPairs = true)
        {
            // 빈 목록도 무시한다 — 경로 없는 restore·rm 이 엉뚱하게 돌지 않게.
            if (entries != null && entries.Any() == false)
            {
                return;
            }
            if (_services.Runner.IsBusy)
            {
                if (entries == null)
                {
                    _queuedBulk = () => Unstage(null, withPairs);
                }
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
            // Fork 방식은 Fork 처럼 파일 목록 없이 짧게 묻는 경고창. 되돌릴 수 없는 일이라 버리기 버튼은 위험 표시(빨강)로 둔다.
            Task<bool> confirm = _shiftForAll
                ? GitConfirmDialog.ShowAlertAsync(_services, ActionGlyphKind.Undo, GitText.Get("staging.discardAlertTitle"),
                    GitText.Get(staged ? "staging.discardAlertStagedMessage" : "staging.discardAlertMessage"), GitText.Get("staging.discardAlertOk"), okDanger: true)
                : GitConfirmDialog.ConfirmAsync(_services.PopupHost, ActionGlyphKind.Undo, title, message, GitText.Get("op.discard"), okDanger: true);
            if (await confirm == false || _services.Runner.IsBusy)
            {
                return;
            }
            await _services.Runner.RunAsync(GitText.Get("op.discard"),
                () => staged ? _services.Commands.DiscardAsync(targets) : _services.Commands.DiscardWorkTreeAsync(targets), refreshAssets: true);
        }
    }
}
