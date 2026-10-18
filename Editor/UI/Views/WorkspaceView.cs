using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 워크스페이스 탭: Unity Version Control Gluon 의 "워크스페이스 탐색". 저장소 폴더 전체를 표로 보이고(항목·상태·크기·수정한 날짜),
    /// 고른 항목의 정보와 히스토리를 오른쪽에 보인다. 변경 목록이 아니라 "지금 저장소에 무엇이 어떤 상태로 있나" 를 보는 곳이다.
    ///
    /// 폴더는 펼칠 때 그 폴더만 디스크에서 읽고(<see cref="Directory.EnumerateFileSystemEntries(string)"/>), 상태는
    /// <see cref="GitWorkspaceIndex"/> 가 git 두 번 부른 결과로 정한다 — 수만 개 파일이 있는 Unity 프로젝트에서도 가볍게.
    /// 펼친 폴더·토글·필터는 <see cref="WorkspaceState"/> 에 남는다.
    /// </summary>
    public sealed class WorkspaceView : GitView
    {
        private const float RowHeight = 20f;
        private const float IndentWidth = 14f;
        private const int HistoryCount = 100;

        private sealed class Row
        {
            public string RepoPath;
            public string Name;
            public bool IsFolder;
            public int Depth;
            public long Size;
            public DateTime Created;
            public DateTime Modified;
        }

        private readonly WorkspaceState _state;
        private readonly ToolbarSearchField _filter;
        private readonly ToolbarToggle _showIgnored;
        private readonly ToolbarToggle _changedOnly;
        private readonly Label _summary;
        private readonly VisualElement _errorNotice;
        private readonly Label _errorText;
        private readonly MultiColumnListView _table;
        private readonly Label _infoPlaceholder;
        private readonly VisualElement _info;
        private readonly Label _infoName;
        private readonly VisualElement _infoRows;
        private readonly Label _historyTitle;
        private readonly Label _historyEmpty;
        private readonly CommitList _history;
        private readonly ListView _historyList;
        private readonly FileActionMenu _fileMenu;

        /// <summary>
        /// 폴더 경로("" = 저장소 루트) → 디스크에서 읽은 아이. 다시 읽을 때 비운다.
        /// </summary>
        private readonly Dictionary<string, List<Row>> _children = new(StringComparer.Ordinal);
        private readonly List<Row> _rows = new();
        private GitWorkspaceIndex _index;
        private bool _isLoading;
        private bool _reloadQueued;
        private string _shownPath;

        public override string Title => GitText.Get("tab.workspace");

        private static string RepoRoot => GitRepository.RepoRoot ?? GitRepository.ProjectRoot;

        public WorkspaceView(GitServices services, WorkspaceState state) : base(services, "WorkspaceView")
        {
            _state = state;
            _filter = Root.Require<ToolbarSearchField>("filter");
            _showIgnored = Root.Require<ToolbarToggle>("show-ignored");
            _changedOnly = Root.Require<ToolbarToggle>("changed-only");
            _summary = Root.Require<Label>("summary");
            _errorNotice = Root.Require<VisualElement>("error-notice");
            _errorText = Root.Require<Label>("error-text");
            _infoPlaceholder = Root.Require<Label>("info-placeholder");
            _info = Root.Require<VisualElement>("info");
            _infoName = Root.Require<Label>("info-name");
            _infoRows = Root.Require<VisualElement>("info-rows");
            _historyTitle = Root.Require<Label>("history-title");
            _historyEmpty = Root.Require<Label>("history-empty");
            _historyList = Root.Require<ListView>("history");
            _history = new CommitList(_historyList, services.Assets);
            _history.ItemChosen += commit => GitCompareWindow.OpenCommit(commit.Hash);
            _fileMenu = new FileActionMenu(services, GitDiffSource.WorkingTree);
            _errorNotice.SetVisible(false);

            // 사이드바 필터와 같은 모양: 비어 있으면 "Filter" 자리표시, 포커스 표시는 바깥 상자에.
            TextField filterInput = _filter.Q<TextField>();
            if (filterInput != null)
            {
                filterInput.textEdition.placeholder = GitText.Get("refs.filterPlaceholder");
                filterInput.textEdition.hidePlaceholderOnFocus = false;
            }
            _filter.RegisterCallback<FocusInEvent>(_ => _filter.AddToClassList("git-search--focused"));
            _filter.RegisterCallback<FocusOutEvent>(_ => _filter.RemoveFromClassList("git-search--focused"));
            _filter.SetValueWithoutNotify(state.Filter ?? string.Empty);
            _filter.RegisterValueChangedCallback(evt =>
            {
                _state.Filter = evt.newValue;
                RebuildRows();
            });
            _showIgnored.SetValueWithoutNotify(state.ShowIgnored);
            _showIgnored.RegisterValueChangedCallback(evt =>
            {
                _state.ShowIgnored = evt.newValue;
                RebuildRows();
            });
            _changedOnly.SetValueWithoutNotify(state.ChangedOnly);
            _changedOnly.RegisterValueChangedCallback(evt =>
            {
                _state.ChangedOnly = evt.newValue;
                RebuildRows();
            });

            _table = CreateTable();
            Root.Require<VisualElement>("table-host").Add(_table);
            ShowInfo(null);
        }

        protected override void OnActivated()
        {
            Load();
        }

        public override void OnRepositoryChanged()
        {
            if (IsActive)
            {
                Load();
            }
        }

        public override void Reload()
        {
            Load();
        }

        // ── 읽기 ─────────────────────────────────────────

        private async void Load()
        {
            if (_isLoading)
            {
                _reloadQueued = true;
                return;
            }
            _isLoading = true;
            (GitWorkspaceIndex index, string error) = await GitWorkspaceIndex.LoadAsync(Services.Repository.Entries);
            _isLoading = false;
            _index = index;
            _errorNotice.SetVisible(error != null);
            _errorText.text = error ?? string.Empty;
            _summary.text = GitText.Format("workspace.summary", index.TrackedCount, index.ChangedCount);
            // 파일이 생기고 지워졌을 수 있으니 펼친 폴더도 다시 읽는다.
            _children.Clear();
            RebuildRows();
            if (_reloadQueued)
            {
                _reloadQueued = false;
                Load();
            }
        }

        private List<Row> ChildrenOf(string folder, int depth)
        {
            if (_children.TryGetValue(folder, out List<Row> cached))
            {
                return cached;
            }
            List<Row> rows = new();
            string full = Path.Combine(RepoRoot, folder);
            try
            {
                foreach (string entry in Directory.EnumerateFileSystemEntries(full))
                {
                    string name = Path.GetFileName(entry);
                    // .git 은 저장소 내부라 보이지 않는다.
                    if (folder.Length == 0 && name == ".git")
                    {
                        continue;
                    }
                    bool isFolder = Directory.Exists(entry);
                    Row row = new()
                    {
                        RepoPath = folder.Length == 0 ? name : folder + "/" + name,
                        Name = GitDisplay.Nfc(name),
                        IsFolder = isFolder,
                        Depth = depth,
                    };
                    try
                    {
                        FileSystemInfo info = isFolder ? new DirectoryInfo(entry) : new FileInfo(entry);
                        row.Created = info.CreationTime;
                        row.Modified = info.LastWriteTime;
                        row.Size = isFolder ? -1 : ((FileInfo)info).Length;
                    }
                    catch (IOException)
                    {
                        // 그 사이에 사라졌다.
                    }
                    rows.Add(row);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                // 읽을 수 없는 폴더는 빈 폴더로 보인다.
            }
            rows.Sort((a, b) => a.IsFolder != b.IsFolder ? (a.IsFolder ? -1 : 1) : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            _children[folder] = rows;
            return rows;
        }

        /// <summary>
        /// 펼친 폴더를 따라 보이는 줄을 다시 만든다. 무시 항목 숨기기·변경만·이름 필터를 여기서 건다.
        /// 이름 필터는 이미 읽은(펼친 적 있는) 폴더 안에서만 찾고, 맞는 항목의 조상 폴더는 남긴다.
        /// </summary>
        private void RebuildRows()
        {
            if (_index == null)
            {
                return;
            }
            string selected = _shownPath;
            _rows.Clear();
            HashSet<string> expanded = new(_state.Expanded, StringComparer.Ordinal);
            string needle = string.IsNullOrWhiteSpace(_state.Filter) ? null : _state.Filter.Trim();
            Append(string.Empty, 0, expanded, needle);
            _table.itemsSource = _rows;
            _table.RefreshItems();
            int index = selected == null ? -1 : _rows.FindIndex(r => r.RepoPath == selected);
            if (index >= 0)
            {
                _table.SetSelectionWithoutNotify(new[] { index });
            }
        }

        /// <returns>보이는 줄을 하나라도 더했으면 true.</returns>
        private bool Append(string folder, int depth, HashSet<string> expanded, string needle)
        {
            bool any = false;
            foreach (Row row in ChildrenOf(folder, depth))
            {
                GitWorkspaceState state = _index.Classify(row.RepoPath, row.IsFolder, out _);
                if (_state.ShowIgnored == false && state == GitWorkspaceState.Ignored)
                {
                    continue;
                }
                if (_state.ChangedOnly && _index.HasPendingChange(row.RepoPath, row.IsFolder) == false)
                {
                    continue;
                }
                bool nameMatches = needle == null || row.Name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
                if (row.IsFolder == false)
                {
                    if (nameMatches)
                    {
                        _rows.Add(row);
                        any = true;
                    }
                    continue;
                }

                int at = _rows.Count;
                bool isExpanded = expanded.Contains(row.RepoPath);
                bool childVisible = isExpanded && Append(row.RepoPath, depth + 1, expanded, needle);
                if (nameMatches || childVisible)
                {
                    _rows.Insert(at, row);
                    any = true;
                }
            }
            return any;
        }

        /// <summary>
        /// → : 접힌 폴더는 펼치고, 펼친 폴더는 첫 자식으로 간다. ← : 펼친 폴더는 접고, 그 밖(파일·접힌 폴더)은 부모 폴더로 간다.
        /// </summary>
        private void OnTableKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.RightArrow && evt.keyCode != KeyCode.LeftArrow)
            {
                return;
            }
            int index = _table.selectedIndex;
            if (index < 0 || index >= _rows.Count)
            {
                return;
            }
            Row row = _rows[index];
            bool expanded = row.IsFolder && _state.Expanded.Contains(row.RepoPath);
            if (evt.keyCode == KeyCode.RightArrow)
            {
                if (row.IsFolder && expanded == false)
                {
                    Toggle(row);
                }
                else if (expanded && index + 1 < _rows.Count && _rows[index + 1].Depth > row.Depth)
                {
                    SelectRow(index + 1);
                }
            }
            else if (expanded)
            {
                Toggle(row);
            }
            else
            {
                // 위로 올라가며 처음 만나는 한 단계 얕은 줄이 부모 폴더다.
                for (int i = index - 1; i >= 0; i--)
                {
                    if (_rows[i].Depth < row.Depth)
                    {
                        SelectRow(i);
                        break;
                    }
                }
            }
            evt.StopPropagation();
        }

        private void SelectRow(int index)
        {
            _table.SetSelection(index);
            _table.ScrollToItem(index);
        }

        private void Toggle(Row row)
        {
            if (row.IsFolder == false)
            {
                return;
            }
            if (_state.Expanded.Remove(row.RepoPath) == false)
            {
                _state.Expanded.Add(row.RepoPath);
            }
            RebuildRows();
        }

        // ── 표 ───────────────────────────────────────────

        private MultiColumnListView CreateTable()
        {
            MultiColumnListView table = new()
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                itemsSource = _rows,
            };
            table.AddToClassList("list");
            table.AddToClassList("workspace__list");
            table.columns.Add(new Column
            {
                name = "item",
                title = GitText.Get("workspace.columnItem"),
                stretchable = true,
                minWidth = 200,
                makeCell = MakeItemCell,
                bindCell = BindItemCell,
            });
            table.columns.Add(TextColumn("status", GitText.Get("workspace.columnStatus"), 130, (label, row) =>
            {
                GitWorkspaceState state = _index.Classify(row.RepoPath, row.IsFolder, out GitFileState? change);
                label.text = StateText(state, change);
                SetStateClass(label, state);
            }));
            table.columns.Add(TextColumn("size", GitText.Get("workspace.columnSize"), 80, (label, row) =>
            {
                label.text = row.IsFolder ? string.Empty : FormatSize(row.Size);
                label.style.unityTextAlign = TextAnchor.MiddleRight;
            }));
            table.columns.Add(TextColumn("modified", GitText.Get("workspace.columnModified"), 130, (label, row) => label.text = GitDisplay.FormatDate(row.Modified)));

            table.selectionChanged += selected => ShowInfo(selected.FirstOrDefault() as Row);
            // 탐색기·트리처럼 →·← 로 폴더를 펼치고 접는다(위·아래는 목록이 원래 처리한다).
            table.RegisterCallback<KeyDownEvent>(OnTableKeyDown, TrickleDown.TrickleDown);
            table.itemsChosen += chosen =>
            {
                if (chosen.FirstOrDefault() is Row row)
                {
                    Choose(row);
                }
            };
            return table;
        }

        private Column TextColumn(string name, string title, float width, Action<Label, Row> bind)
        {
            return new Column
            {
                name = name,
                title = title,
                width = width,
                minWidth = 50,
                makeCell = () =>
                {
                    Label label = new();
                    label.AddToClassList("workspace__cell");
                    label.AddToClassList("ellipsis");
                    AddMenu(label);
                    return label;
                },
                bindCell = (element, index) =>
                {
                    element.userData = index;
                    bind((Label)element, _rows[index]);
                },
            };
        }

        private VisualElement MakeItemCell()
        {
            VisualElement cell = new();
            cell.AddToClassList("workspace__item");
            VisualElement indent = new();
            cell.Add(indent);
            Label arrow = new();
            arrow.AddToClassList("workspace__arrow");
            arrow.RegisterCallback<ClickEvent>(evt =>
            {
                if (cell.userData is int index && index < _rows.Count)
                {
                    Toggle(_rows[index]);
                    evt.StopPropagation();
                }
            });
            cell.Add(arrow);
            Image icon = new();
            icon.AddToClassList("workspace__icon");
            cell.Add(icon);
            Label name = new();
            name.AddToClassList("workspace__name");
            name.AddToClassList("ellipsis");
            cell.Add(name);
            AddMenu(cell);
            return cell;
        }

        private void BindItemCell(VisualElement cell, int index)
        {
            cell.userData = index;
            Row row = _rows[index];
            cell[0].style.width = row.Depth * IndentWidth;
            ((Label)cell[1]).text = row.IsFolder ? (_state.Expanded.Contains(row.RepoPath) ? "▾" : "▸") : string.Empty;
            ((Image)cell[2]).image = IconOf(row);
            Label name = (Label)cell[3];
            name.text = row.Name;
            GitWorkspaceState state = _index.Classify(row.RepoPath, row.IsFolder, out _);
            SetStateClass(name, state);
            cell.tooltip = row.RepoPath;
        }

        private static Texture IconOf(Row row)
        {
            if (row.IsFolder)
            {
                return EditorGUIUtility.IconContent("Folder Icon").image;
            }
            return InternalEditorUtility.GetIconForFile(row.Name);
        }

        private static void SetStateClass(VisualElement element, GitWorkspaceState state)
        {
            foreach (GitWorkspaceState each in (GitWorkspaceState[])Enum.GetValues(typeof(GitWorkspaceState)))
            {
                element.EnableInClassList("workspace--" + each.ToString().ToLowerInvariant(), each == state);
            }
        }

        private static string StateText(GitWorkspaceState state, GitFileState? change)
        {
            string text = GitText.Get("workspace.state" + state);
            return change.HasValue && state == GitWorkspaceState.Changed ? $"{text} · {GitText.Get("state." + change.Value)}" : text;
        }

        private static string FormatSize(long bytes)
        {
            if (bytes < 0)
            {
                return string.Empty;
            }
            if (bytes < 1024)
            {
                return bytes + " bytes";
            }
            double kb = bytes / 1024.0;
            if (kb < 1024)
            {
                return kb.ToString("0.##") + " KB";
            }
            double mb = kb / 1024.0;
            return mb < 1024 ? mb.ToString("0.##") + " MB" : (mb / 1024.0).ToString("0.##") + " GB";
        }

        /// <summary>
        /// 더블클릭: 폴더는 펼치기·접기, Unity 에셋은 Project 창에서 고르기, 그 밖의 파일은 탐색기에서 보이기.
        /// </summary>
        private void Choose(Row row)
        {
            if (row.IsFolder)
            {
                Toggle(row);
                return;
            }
            // 파일은 실제로 연다: Unity 에셋은 Project 창 더블클릭과 같게, 그 밖의 파일은 OS 기본 프로그램으로.
            Services.Assets.Open(ProjectPathOf(row.RepoPath), FullPath(row.RepoPath));
        }

        // ── 우클릭 ───────────────────────────────────────

        private void AddMenu(VisualElement cell)
        {
            cell.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (cell.userData is int index && index < _rows.Count)
                {
                    _table.SetSelection(index);
                    PopulateMenu(evt.menu, _rows[index]);
                }
            }));
        }

        private void PopulateMenu(DropdownMenu menu, Row row)
        {
            GitWorkspaceState state = _index.Classify(row.RepoPath, row.IsFolder, out GitFileState? change);
            if (row.IsFolder)
            {
                menu.AppendAction(GitText.Get(_state.Expanded.Contains(row.RepoPath) ? "workspace.collapse" : "workspace.expand"), _ => Toggle(row));
                menu.AppendSeparator();
                List<GitStatusEntry> under = Services.Repository.Entries.Where(e => e.RepoPath.StartsWith(row.RepoPath + "/", StringComparison.Ordinal)).ToList();
                _fileMenu.AppendFolderItems(menu, row.RepoPath, under);
                return;
            }

            GitStatusEntry entry = new()
            {
                RepoPath = row.RepoPath,
                ProjectPath = ProjectPathOf(row.RepoPath),
                // 바뀌지 않은 추적 파일은 히스토리·blame 이 있는 파일로 다룬다.
                State = change ?? (state == GitWorkspaceState.Tracked ? GitFileState.Modified : GitFileState.Untracked),
            };
            if (Services.Assets.CanPing(entry.ProjectPath))
            {
                menu.AppendAction(GitText.Get("common.pingAsset"), _ => Services.Assets.Ping(entry.ProjectPath));
                menu.AppendSeparator();
            }
            if (state != GitWorkspaceState.Ignored)
            {
                _fileMenu.AppendWorkingItems(menu, entry);
            }
            _fileMenu.AppendCommonItems(menu, entry);
        }

        // ── 오른쪽 정보·히스토리 ─────────────────────────

        private async void ShowInfo(Row row)
        {
            _shownPath = row?.RepoPath;
            _infoPlaceholder.SetVisible(row == null);
            _info.SetVisible(row != null);
            _historyTitle.SetVisible(row != null && row.IsFolder == false);
            _history.SetItems(Array.Empty<GitCommitInfo>());
            _historyEmpty.SetVisible(false);
            _historyList.SetVisible(row != null && row.IsFolder == false);
            if (row == null)
            {
                return;
            }

            GitWorkspaceState state = _index.Classify(row.RepoPath, row.IsFolder, out GitFileState? change);
            _infoName.text = row.Name;
            _infoRows.Clear();
            AddInfo("workspace.infoPath", row.RepoPath);
            AddInfo("workspace.infoState", StateText(state, change));
            if (row.IsFolder == false)
            {
                AddInfo("workspace.infoSize", FormatSize(row.Size));
            }
            AddInfo("workspace.infoCreated", GitDisplay.FormatDate(row.Created));
            AddInfo("workspace.infoModified", GitDisplay.FormatDate(row.Modified));

            if (row.IsFolder)
            {
                return;
            }
            if (state == GitWorkspaceState.Untracked || state == GitWorkspaceState.Ignored)
            {
                _historyList.SetVisible(false);
                _historyEmpty.text = GitText.Get("workspace.noHistory");
                _historyEmpty.SetVisible(true);
                return;
            }
            string path = row.RepoPath;
            (List<GitCommitInfo> commits, string error) = await GitHistory.FileLogAsync(path, HistoryCount);
            if (_shownPath != path)
            {
                return;
            }
            _history.SetItems(commits, c => c.Hash);
            if (commits.Count == 0)
            {
                _historyEmpty.text = error ?? GitText.Get("workspace.noHistory");
                _historyEmpty.SetVisible(true);
            }
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

        private static string ProjectPathOf(string repoPath)
        {
            return GitStatusParser.ToProjectPath(repoPath, GitRepository.ProjectPrefix);
        }

        private static string FullPath(string repoPath)
        {
            return Path.Combine(RepoRoot, repoPath).Replace('\\', '/');
        }
    }
}
