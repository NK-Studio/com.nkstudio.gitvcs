using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 충돌 파일 고치기(GitKraken 의 병합 편집기). 위 왼쪽 A(우리 쪽)·위 오른쪽 B(들어오는 쪽), 아래 출력.
    /// 충돌 덩어리마다 A·B 를 골라(체크) 출력에 넣는다 — 둘 다 고르면 고른 차례대로 잇고, 줄을 눌러 한 줄씩 빼거나 넣을 수 있다.
    /// 다 고르면 [저장] 이 출력을 파일에 쓰고 해결됨으로 표시한다(<c>git add</c>).
    /// 파일은 작업 사본에 git 이 남긴 충돌 표시를 읽는다(<see cref="GitConflictFile"/>). 표시가 없는 충돌(바이너리·삭제/수정)은 한쪽으로 해결하는 버튼만 둔다.
    /// <see cref="GitDiffPanel"/> 위에 덮어 띄운다.
    /// </summary>
    public sealed class GitMergeEditor
    {
        private const float RowHeight = 18f;

        // 가로 스크롤 폭: 줄 앞 칸(덩어리 체크·번호·줄 체크)과 고정폭 글자(12px Jetendard) 한 칸 폭.
        private const float RowGutterWidth = 100f;
        private const float CharWidth = 7.3f;

        private sealed class MergeRow
        {
            public string Text;
            public int Number;
            public int Conflict = -1;
            public int Line = -1;
            public bool HunkStart;
            public bool Placeholder;
            public GitMergeSide Side;
            public bool Chosen;
        }

        private readonly GitServices _services;
        private readonly Font _monospace;
        private readonly Label _path;
        private readonly Label _count;
        private readonly Button _save;
        private readonly Button _close;
        private readonly VisualElement _editor;
        private readonly VisualElement _binary;
        private readonly Label _binaryMessage;
        private readonly Button _binaryA;
        private readonly Button _binaryB;
        private readonly Button _binaryAsIs;
        private readonly Toggle _allA;
        private readonly Toggle _allB;
        private readonly Label _titleA;
        private readonly Label _titleB;
        private readonly Label _position;
        private readonly ListView _listA;
        private readonly ListView _listB;
        private readonly ListView _output;
        private readonly List<MergeRow> _rowsA = new();
        private readonly List<MergeRow> _rowsB = new();
        private readonly List<MergeRow> _rowsOutput = new();

        private string _repoPath;
        private string _loadedText;
        private string _savedText;
        private bool _hasBom;
        private GitConflictFile _file;
        private List<GitMergeSegment> _conflicts = new();
        private List<GitMergeChoice> _choices = new();
        private int _current;

        /// <summary>닫기(×). <see cref="GitDiffPanel"/> 의 닫기와 같다.</summary>
        public event Action CloseRequested;

        public VisualElement Root { get; }

        public GitMergeEditor(GitServices services)
        {
            _services = services;
            _monospace = GitFonts.Monospace;

            Root = new VisualElement();
            Root.AddToClassList("git-merge");

            VisualElement header = new();
            header.AddToClassList("git-merge__header");
            Label warning = new("⚠");
            warning.AddToClassList("git-merge__warning");
            header.Add(warning);
            _path = new Label();
            _path.AddToClassList("git-merge__path");
            header.Add(_path);
            _count = new Label();
            _count.AddToClassList("git-merge__count");
            header.Add(_count);
            VisualElement spacer = new();
            spacer.AddToClassList("git-merge__spacer");
            header.Add(spacer);
            _save = new Button(Save) { text = GitText.Get("merge.save") };
            _save.AddToClassList("git-merge__save");
            header.Add(_save);
            _close = new Button(() => CloseRequested?.Invoke()) { text = "×", tooltip = GitText.Get("common.close") };
            _close.AddToClassList("git-merge__close");
            _close.SetVisible(false);
            header.Add(_close);
            Root.Add(header);

            _editor = new VisualElement();
            _editor.AddToClassList("git-merge__editor");
            Root.Add(_editor);

            VisualElement top = new();
            top.AddToClassList("git-merge__top");
            _editor.Add(top);
            top.Add(Pane(GitMergeSide.A, out _allA, out _titleA, out _listA, _rowsA));
            top.Add(Pane(GitMergeSide.B, out _allB, out _titleB, out _listB, _rowsB));

            VisualElement outputHeader = new();
            outputHeader.AddToClassList("git-merge__pane-header");
            outputHeader.AddToClassList("git-merge__output-header");
            Label outputTitle = new(GitText.Get("merge.output"));
            outputTitle.AddToClassList("git-merge__output-title");
            outputHeader.Add(outputTitle);
            VisualElement outputSpacer = new();
            outputSpacer.AddToClassList("git-merge__spacer");
            outputHeader.Add(outputSpacer);
            _position = new Label();
            _position.AddToClassList("git-merge__position");
            outputHeader.Add(_position);
            Button previous = new(() => Move(-1)) { text = "▲", tooltip = GitText.Get("merge.previous") };
            previous.AddToClassList("git-merge__nav");
            outputHeader.Add(previous);
            Button next = new(() => Move(1)) { text = "▼", tooltip = GitText.Get("merge.next") };
            next.AddToClassList("git-merge__nav");
            outputHeader.Add(next);
            Button reset = new(Reset) { text = GitText.Get("merge.reset"), tooltip = GitText.Get("merge.resetTooltip") };
            reset.AddToClassList("git-merge__reset");
            outputHeader.Add(reset);
            _editor.Add(outputHeader);
            _output = List(_rowsOutput, output: true);
            _output.AddToClassList("git-merge__output");
            _editor.Add(_output);

            // 충돌 표시가 없는 충돌(바이너리 에셋·한쪽이 지운 파일): 한쪽으로 해결하거나 지금 파일로 해결됨 표시.
            _binary = new VisualElement();
            _binary.AddToClassList("git-merge__binary");
            _binaryMessage = new Label();
            _binaryMessage.AddToClassList("git-merge__binary-message");
            _binary.Add(_binaryMessage);
            VisualElement binaryButtons = new();
            binaryButtons.AddToClassList("git-merge__binary-buttons");
            _binaryA = new Button(() => ResolveWith(GitConflictSide.Ours));
            _binaryB = new Button(() => ResolveWith(GitConflictSide.Theirs));
            _binaryAsIs = new Button(() => ResolveWith(GitConflictSide.AsIs)) { text = GitText.Get("inprogress.resolveMarked") };
            foreach (Button button in new[] { _binaryA, _binaryB, _binaryAsIs })
            {
                button.AddToClassList("git-merge__binary-button");
                binaryButtons.Add(button);
            }
            _binary.Add(binaryButtons);
            _binary.SetVisible(false);
            Root.Add(_binary);

            Root.RegisterCallback<AttachToPanelEvent>(_ => _services.Runner.StateChanged += UpdateSave);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => _services.Runner.StateChanged -= UpdateSave);
        }

        public void ShowCloseButton(bool isVisible)
        {
            _close.SetVisible(isVisible);
        }

        /// <summary>
        /// 이 대상을 병합 편집기로 볼지: 작업 사본(또는 unstage 쪽) 파일이 지금 충돌 상태일 때.
        /// 대상의 상태는 고를 때의 것이라, 해결한 뒤에도 남아 있을 수 있어 저장소 상태로 다시 본다.
        /// </summary>
        public bool CanOpen(GitDiffTarget target)
        {
            if (target == null || (target.Source != GitDiffSource.WorkingTree && target.Source != GitDiffSource.Unstaged))
            {
                return false;
            }
            return _services.Repository.Entries.Any(e => e.RepoPath == target.RepoPath && e.State == GitFileState.Conflicted);
        }

        /// <summary>
        /// 파일을 읽어 보인다. 같은 파일을 다시 열 때(저장소 새로고침) 내용이 그대로면 고른 것을 그대로 둔다.
        /// </summary>
        public void Open(string repoPath)
        {
            string fullPath = FullPath(repoPath);
            string text = null;
            bool hasBom = false;
            try
            {
                if (File.Exists(fullPath))
                {
                    byte[] bytes = File.ReadAllBytes(fullPath);
                    // 바이너리(NUL 이 있는 파일)는 git 도 충돌 표시를 넣지 않는다.
                    if (Array.IndexOf(bytes, (byte)0) < 0)
                    {
                        hasBom = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF;
                        text = new UTF8Encoding(false).GetString(bytes, hasBom ? 3 : 0, bytes.Length - (hasBom ? 3 : 0));
                    }
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                text = null;
            }

            _path.text = GitDisplay.Nfc(repoPath);
            // 저장한 직후(해결 표시가 반영되기 전)의 새로고침이면 표시 없는 파일을 "줄 단위로 못 고침" 으로 잘못 보이지 않게 그대로 둔다.
            if (repoPath == _repoPath && _file != null && (text == _loadedText || (_savedText != null && text == _savedText)))
            {
                Render();
                return;
            }
            _repoPath = repoPath;
            _loadedText = text;
            _savedText = null;
            _hasBom = hasBom;
            _file = text != null ? GitConflictFile.Parse(text) : null;
            _current = 0;
            if (_file == null)
            {
                _conflicts = new List<GitMergeSegment>();
                _choices = new List<GitMergeChoice>();
                ShowBinary();
                return;
            }
            _conflicts = _file.Conflicts.ToList();
            _choices = _conflicts.Select(segment => new GitMergeChoice(segment)).ToList();
            _binary.SetVisible(false);
            _editor.SetVisible(true);
            GitOperationKind kind = _services.Repository.InProgressOperation;
            _titleA.text = SideTitle(GitText.Get("inprogress.ours." + kind), _file.ALabel);
            _titleB.text = SideTitle(GitText.Get("inprogress.theirs." + kind), _file.BLabel);
            Render();
            ScrollToCurrent();
        }

        private static string SideTitle(string name, string label)
        {
            return string.IsNullOrEmpty(label) ? name : $"{name} · {label}";
        }

        private void ShowBinary()
        {
            _editor.SetVisible(false);
            _binary.SetVisible(true);
            _count.text = string.Empty;
            GitOperationKind kind = _services.Repository.InProgressOperation;
            _binaryMessage.text = GitText.Get("merge.noMarkers");
            _binaryA.text = GitText.Format("inprogress.resolveOurs", GitText.Get("inprogress.ours." + kind));
            _binaryB.text = GitText.Format("inprogress.resolveTheirs", GitText.Get("inprogress.theirs." + kind));
            _save.SetVisible(false);
            UpdateSave();
        }

        // ── 칸 만들기 ──

        private VisualElement Pane(GitMergeSide side, out Toggle all, out Label title, out ListView list, List<MergeRow> rows)
        {
            VisualElement pane = new();
            pane.AddToClassList("git-merge__pane");
            pane.AddToClassList(side == GitMergeSide.A ? "git-merge__pane--a" : "git-merge__pane--b");

            VisualElement header = new();
            header.AddToClassList("git-merge__pane-header");
            all = new Toggle { tooltip = GitText.Get(side == GitMergeSide.A ? "merge.allATooltip" : "merge.allBTooltip") };
            all.AddToClassList("git-merge__all");
            all.RegisterValueChangedCallback(evt => ChooseAll(side, evt.newValue));
            header.Add(all);
            Label badge = new(side == GitMergeSide.A ? "A" : "B");
            badge.AddToClassList("git-merge__badge");
            header.Add(badge);
            title = new Label();
            title.AddToClassList("git-merge__pane-title");
            header.Add(title);
            pane.Add(header);

            list = List(rows, output: false);
            pane.Add(list);
            return pane;
        }

        private ListView List(List<MergeRow> rows, bool output)
        {
            ListView list = new()
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                horizontalScrollingEnabled = true,
                itemsSource = rows,
                makeItem = () => MakeRow(rows, output),
                bindItem = (element, index) =>
                {
                    BindRow(element, rows[index], output);
                    // 가장 긴 줄만큼 줄 폭을 잡아 두어 목록이 가로로 스크롤되게 한다(줄 요소는 다시 쓰이므로 모두 같은 폭).
                    element.style.minWidth = RowGutterWidth + LongestLine(rows) * CharWidth;
                },
            };
            list.AddToClassList("git-merge__list");
            return list;
        }

        private readonly Dictionary<List<MergeRow>, int> _longest = new();

        private int LongestLine(List<MergeRow> rows)
        {
            if (_longest.TryGetValue(rows, out int longest) == false)
            {
                longest = rows.Count == 0 ? 0 : rows.Max(row => row.Text.Replace("\t", "    ").Length);
                _longest[rows] = longest;
            }
            return longest;
        }

        private VisualElement MakeRow(List<MergeRow> rows, bool output)
        {
            VisualElement row = new();
            row.AddToClassList("merge-row");
            Toggle hunk = new();
            hunk.AddToClassList("merge-row__hunk");
            hunk.RegisterValueChangedCallback(evt =>
            {
                if (row.userData is MergeRow data && data.Conflict >= 0)
                {
                    ChooseSide(data.Conflict, data.Side, evt.newValue);
                }
            });
            row.Add(hunk);
            Label number = new();
            number.AddToClassList("merge-row__number");
            row.Add(number);
            Label check = new();
            check.AddToClassList("merge-row__check");
            row.Add(check);
            Label text = new();
            text.AddToClassList("merge-row__text");
            if (_monospace != null)
            {
                text.style.unityFontDefinition = FontDefinition.FromFont(_monospace);
            }
            row.Add(text);
            hunk.SetVisible(output == false);

            // 충돌 줄을 누르면 그 줄 하나를 넣거나 뺀다(체크 상자는 따로 받는다). 출력 칸에서는 아무것도 하지 않는다.
            if (output == false)
            {
                row.RegisterCallback<ClickEvent>(evt =>
                {
                    if (evt.target is VisualElement target && hunk.Contains(target))
                    {
                        return;
                    }
                    if (row.userData is MergeRow data && data.Conflict >= 0)
                    {
                        if (data.Placeholder)
                        {
                            ChooseSide(data.Conflict, data.Side, _choices[data.Conflict].IsChosen(data.Side) == false);
                        }
                        else
                        {
                            _choices[data.Conflict].ToggleLine(data.Side, data.Line);
                            _current = data.Conflict;
                            Render();
                        }
                    }
                });
            }
            return row;
        }

        private void BindRow(VisualElement row, MergeRow data, bool output)
        {
            row.userData = data;
            Toggle hunk = row.Q<Toggle>(className: "merge-row__hunk");
            Label number = row.Q<Label>(className: "merge-row__number");
            Label check = row.Q<Label>(className: "merge-row__check");
            Label text = row.Q<Label>(className: "merge-row__text");

            bool inConflict = data.Conflict >= 0;
            bool unresolved = output && data.Placeholder;
            row.EnableInClassList("merge-row--a", inConflict && unresolved == false && data.Side == GitMergeSide.A && (output || data.Placeholder == false || data.Chosen));
            row.EnableInClassList("merge-row--b", inConflict && unresolved == false && data.Side == GitMergeSide.B && (output || data.Placeholder == false || data.Chosen));
            row.EnableInClassList("merge-row--placeholder", data.Placeholder);
            row.EnableInClassList("merge-row--unresolved", unresolved);
            row.EnableInClassList("merge-row--current", inConflict && data.Conflict == _current);

            if (output == false)
            {
                hunk.SetVisible(true);
                hunk.style.visibility = inConflict && data.HunkStart ? Visibility.Visible : Visibility.Hidden;
                hunk.SetValueWithoutNotify(inConflict && _choices[data.Conflict].IsChosen(data.Side));
            }
            number.text = data.Number > 0 ? data.Number.ToString() : string.Empty;
            bool showCheck = inConflict && data.Placeholder == false && (output || data.Chosen);
            check.text = showCheck ? "✓" : string.Empty;
            check.EnableInClassList("merge-row__check--on", showCheck);
            check.EnableInClassList("merge-row__check--off", inConflict && data.Placeholder == false && output == false && data.Chosen == false);
            text.text = data.Text;
        }

        // ── 고르기 ──

        private void ChooseSide(int conflict, GitMergeSide side, bool chosen)
        {
            if (conflict < 0 || conflict >= _choices.Count)
            {
                return;
            }
            _choices[conflict].SetSide(side, chosen);
            _current = conflict;
            Render();
        }

        private void ChooseAll(GitMergeSide side, bool chosen)
        {
            foreach (GitMergeChoice choice in _choices)
            {
                if (choice.IsChosen(side) != chosen)
                {
                    choice.SetSide(side, chosen);
                }
            }
            Render();
        }

        private void Reset()
        {
            foreach (GitMergeChoice choice in _choices)
            {
                choice.Clear();
            }
            _current = 0;
            Render();
            ScrollToCurrent();
        }

        private void Move(int delta)
        {
            if (_conflicts.Count == 0)
            {
                return;
            }
            _current = (_current + delta + _conflicts.Count) % _conflicts.Count;
            Render();
            ScrollToCurrent();
        }

        private void ScrollToCurrent()
        {
            ScrollTo(_listA, _rowsA);
            ScrollTo(_listB, _rowsB);
            ScrollTo(_output, _rowsOutput);
        }

        private void ScrollTo(ListView list, List<MergeRow> rows)
        {
            int index = rows.FindIndex(row => row.Conflict == _current);
            if (index < 0)
            {
                return;
            }
            // 덩어리가 칸 가운데쯤 오게 몇 줄 위부터 보인다.
            int target = Math.Max(0, index - 4);
            list.schedule.Execute(() => list.ScrollToItem(target));
        }

        // ── 그리기 ──

        private void Render()
        {
            if (_file == null)
            {
                return;
            }
            BuildSideRows(GitMergeSide.A, _rowsA);
            BuildSideRows(GitMergeSide.B, _rowsB);
            BuildOutputRows();
            _longest.Clear();
            _listA.RefreshItems();
            _listB.RefreshItems();
            _output.RefreshItems();

            _allA.SetValueWithoutNotify(_choices.Count > 0 && _choices.All(c => c.IsChosen(GitMergeSide.A)));
            _allB.SetValueWithoutNotify(_choices.Count > 0 && _choices.All(c => c.IsChosen(GitMergeSide.B)));
            int left = _choices.Count(c => c.IsDecided == false);
            _count.text = GitText.Format("merge.count", _conflicts.Count);
            _position.text = GitText.Format("merge.position", Math.Min(_current + 1, _conflicts.Count), _conflicts.Count);
            _save.SetVisible(true);
            _save.tooltip = left > 0 ? GitText.Format("merge.saveBlocked", left) : GitText.Get("merge.saveTooltip");
            UpdateSave();
        }

        private void UpdateSave()
        {
            bool idle = _services.Runner.IsBusy == false;
            _save.SetEnabled(idle && _file != null && _choices.All(c => c.IsDecided));
            _binaryA.SetEnabled(idle);
            _binaryB.SetEnabled(idle);
            _binaryAsIs.SetEnabled(idle);
        }

        private void BuildSideRows(GitMergeSide side, List<MergeRow> rows)
        {
            rows.Clear();
            int number = 1;
            int conflict = 0;
            foreach (GitMergeSegment segment in _file.Segments)
            {
                if (segment.IsConflict == false)
                {
                    foreach (string line in segment.Common)
                    {
                        rows.Add(new MergeRow { Text = line, Number = number++ });
                    }
                    continue;
                }
                GitMergeChoice choice = _choices[conflict];
                List<string> lines = segment.Lines(side);
                bool[] on = choice.LinesOf(side);
                bool chosen = choice.IsChosen(side);
                if (lines.Count == 0)
                {
                    // 이 쪽은 그 자리를 비웠다(지웠다). 고르면 "아무것도 넣지 않음" 이다.
                    rows.Add(new MergeRow { Text = GitText.Get("merge.emptySide"), Conflict = conflict, Side = side, HunkStart = true, Placeholder = true, Chosen = chosen });
                }
                for (int i = 0; i < lines.Count; i++)
                {
                    rows.Add(new MergeRow { Text = lines[i], Number = number++, Conflict = conflict, Line = i, Side = side, HunkStart = i == 0, Chosen = chosen && on[i] });
                }
                conflict++;
            }
        }

        private void BuildOutputRows()
        {
            _rowsOutput.Clear();
            int number = 1;
            int conflict = 0;
            foreach (GitMergeSegment segment in _file.Segments)
            {
                if (segment.IsConflict == false)
                {
                    foreach (string line in segment.Common)
                    {
                        _rowsOutput.Add(new MergeRow { Text = line, Number = number++ });
                    }
                    continue;
                }
                GitMergeChoice choice = _choices[conflict];
                if (choice.IsDecided == false)
                {
                    _rowsOutput.Add(new MergeRow { Text = GitText.Format("merge.unresolved", conflict + 1), Conflict = conflict, Placeholder = true });
                }
                else
                {
                    bool any = false;
                    foreach ((GitMergeSide side, int index, string line) in GitConflictFile.ChosenLinesWithOrigin(segment, choice))
                    {
                        any = true;
                        _rowsOutput.Add(new MergeRow { Text = line, Number = number++, Conflict = conflict, Line = index, Side = side, Chosen = true });
                    }
                    if (any == false)
                    {
                        // 고른 쪽이 비어 있다(지운 쪽을 골랐다). 자리만 보인다 — 번호 없이 저장되지 않는 줄.
                        _rowsOutput.Add(new MergeRow { Text = GitText.Get("merge.emptyChosen"), Conflict = conflict, Side = choice.Order[0], Placeholder = true, Chosen = true });
                    }
                }
                conflict++;
            }
        }

        // ── 저장·해결 ──

        private void Save()
        {
            if (_file == null || _choices.Any(c => c.IsDecided == false) || _services.Runner.IsBusy)
            {
                return;
            }
            string fullPath = FullPath(_repoPath);
            try
            {
                _savedText = _file.Build(_choices);
                File.WriteAllText(fullPath, _savedText, new UTF8Encoding(_hasBom));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                _services.Dialogs.Alert(GitText.Get("merge.saveFailed"), e.Message);
                return;
            }
            ResolveWith(GitConflictSide.AsIs);
        }

        private void ResolveWith(GitConflictSide side)
        {
            GitStatusEntry entry = _services.Repository.Entries.FirstOrDefault(e => e.RepoPath == _repoPath);
            if (entry == null)
            {
                return;
            }
            _ = _services.Runner.RunAsync(GitText.Format("inprogress.opResolve", Path.GetFileName(_repoPath)),
                () => _services.Commands.ResolveConflictAsync(entry, side), refreshAssets: true, workingTreeOnly: true);
        }

        private static string FullPath(string repoPath)
        {
            return Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, repoPath);
        }
    }
}
