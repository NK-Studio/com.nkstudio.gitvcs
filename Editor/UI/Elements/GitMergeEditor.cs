using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 충돌 파일 고치기(GitKraken 의 병합 편집기). 위 왼쪽 A(우리 쪽)·위 오른쪽 B(들어오는 쪽), 아래 출력.
    /// 충돌 덩어리마다 A·B 를 골라(체크) 출력에 넣는다 — 둘 다 고르면 고른 차례대로 잇고, 줄을 눌러 한 줄씩 빼거나 넣을 수 있다.
    /// 다 고르면 [저장] 이 출력을 파일에 쓰고 해결됨으로 표시한다(<c>git add</c>).
    /// 파일은 작업 사본에 git 이 남긴 충돌 표시를 읽는다(<see cref="GitConflictFile"/>). 표시가 없는 충돌(바이너리·삭제/수정)은 한쪽으로 해결하는 버튼만 둔다.
    /// 이미지 충돌은 GitKraken 처럼 A·B 이미지를 나란히 보이고, 체크로 한쪽을 고르면 아래 출력에 그 이미지가 보인다. [저장] 이 그쪽으로 해결한다.
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
        private readonly VisualElement _imageView;
        private readonly ImagePane _imageA;
        private readonly ImagePane _imageB;
        private readonly VisualElement _imageOutputStage;
        private readonly VisualElement _imageOutputFrame;
        private readonly Image _imageOutput;
        private readonly Label _imageOutputHint;
        private readonly Button _imageAsIs;
        private GitImagePair _images;
        private string _imagesPath;
        private GitConflictSide? _imageChoice;
        private int _imageVersion;
        private bool _imageMode;
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

            // 이미지 충돌: 위 A·B 이미지, 아래 고른 이미지(출력).
            _imageView = new VisualElement();
            _imageView.AddToClassList("git-merge__editor");
            _imageView.AddToClassList("git-merge__images");
            VisualElement imageTop = new();
            imageTop.AddToClassList("git-merge__top");
            _imageA = new ImagePane(GitMergeSide.A, value => ChooseImage(GitConflictSide.Ours, value));
            _imageB = new ImagePane(GitMergeSide.B, value => ChooseImage(GitConflictSide.Theirs, value));
            imageTop.Add(_imageA.Root);
            imageTop.Add(_imageB.Root);
            _imageView.Add(imageTop);
            VisualElement imageOutputHeader = new();
            imageOutputHeader.AddToClassList("git-merge__pane-header");
            imageOutputHeader.AddToClassList("git-merge__output-header");
            Label imageOutputTitle = new(GitText.Get("merge.output"));
            imageOutputTitle.AddToClassList("git-merge__output-title");
            imageOutputHeader.Add(imageOutputTitle);
            VisualElement imageOutputSpacer = new();
            imageOutputSpacer.AddToClassList("git-merge__spacer");
            imageOutputHeader.Add(imageOutputSpacer);
            // 다른 프로그램(포토샵 등)에서 직접 고쳐 둔 파일을 그대로 쓸 때.
            _imageAsIs = new Button(() => ResolveWith(GitConflictSide.AsIs)) { text = GitText.Get("inprogress.resolveMarked"), tooltip = GitText.Get("merge.imageAsIsTooltip") };
            _imageAsIs.AddToClassList("git-merge__reset");
            imageOutputHeader.Add(_imageAsIs);
            _imageView.Add(imageOutputHeader);
            _imageOutputStage = ImageStage(out _imageOutputFrame, out _imageOutput, out _imageOutputHint);
            _imageOutputStage.AddToClassList("git-merge__image-stage--output");
            _imageView.Add(_imageOutputStage);
            _imageView.SetVisible(false);
            Root.Add(_imageView);

            Root.RegisterCallback<AttachToPanelEvent>(_ => _services.Runner.StateChanged += UpdateSave);
            Root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                _services.Runner.StateChanged -= UpdateSave;
                ReleaseImages();
            });
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
            SetImageMode(false);
            ReleaseImages();
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
            if (GitImageDiff.IsImage(_repoPath))
            {
                ShowImages();
                return;
            }
            ReleaseImages();
            SetImageMode(false);
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
            if (_imageMode)
            {
                _save.SetEnabled(idle && _imageChoice != null);
                _imageAsIs.SetEnabled(idle);
                return;
            }
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
            if (_imageMode)
            {
                if (_imageChoice != null && _services.Runner.IsBusy == false)
                {
                    ResolveWith(_imageChoice.Value);
                }
                return;
            }
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
                _ = _services.Dialogs.AlertAsync(GitText.Get("merge.saveFailed"), e.Message);
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

        // ── 이미지 충돌 ──

        /// <summary>
        /// 이미지 충돌을 A·B 나란히 보인다. 같은 파일을 다시 열 때(저장소 새로고침)는 읽어 둔 이미지와 고른 쪽을 그대로 둔다.
        /// </summary>
        private void ShowImages()
        {
            _binary.SetVisible(false);
            SetImageMode(true);
            _count.text = string.Empty;
            _save.SetVisible(true);
            if (_imagesPath == _repoPath && _images != null)
            {
                RenderImages();
                return;
            }
            ReleaseImages();
            _imagesPath = _repoPath;
            _imageChoice = null;
            GitOperationKind kind = _services.Repository.InProgressOperation;
            _imageA.SetTitle(GitText.Get("inprogress.ours." + kind));
            _imageB.SetTitle(GitText.Get("inprogress.theirs." + kind));
            _imageA.ShowLoading();
            _imageB.ShowLoading();
            RenderImages();
            LoadImagesAsync(++_imageVersion, _repoPath, kind);
        }

        private async void LoadImagesAsync(int version, string repoPath, GitOperationKind kind)
        {
            (GitImagePair pair, string error) = await _services.Diff.LoadConflictImagesAsync(repoPath);
            (string ours, string theirs) = await SideNamesAsync(kind);
            if (version != _imageVersion || repoPath != _repoPath || _imageMode == false)
            {
                pair?.Dispose();
                return;
            }
            _images = pair;
            _imageA.SetTitle(SideTitle(GitText.Get("inprogress.ours." + kind), ours));
            _imageB.SetTitle(SideTitle(GitText.Get("inprogress.theirs." + kind), theirs));
            _imageA.Show(pair?.Left, error);
            _imageB.Show(pair?.Right, error);
            RenderImages();
        }

        /// <summary>
        /// A·B 머리에 붙일 이름: 우리 쪽은 지금 브랜치(또는 짧은 해시), 들어오는 쪽은 MERGE_HEAD 등이 가리키는 브랜치 이름(없으면 짧은 해시).
        /// </summary>
        private static async Task<(string ours, string theirs)> SideNamesAsync(GitOperationKind kind)
        {
            string branch = GitRepository.Branch?.Detached == false ? GitRepository.Branch.Name : null;
            string ours = string.IsNullOrEmpty(branch) ? await ShortHashAsync("HEAD") : branch;
            string incoming = kind switch
            {
                GitOperationKind.Merge => "MERGE_HEAD",
                GitOperationKind.CherryPick => "CHERRY_PICK_HEAD",
                GitOperationKind.Revert => "REVERT_HEAD",
                GitOperationKind.Rebase => "REBASE_HEAD",
                _ => null,
            };
            if (incoming == null)
            {
                return (ours, null);
            }
            GitResult name = await GitRepository.RunAsync("name-rev", "--name-only", "--no-undefined", "--exclude=refs/tags/*", incoming);
            string theirs = name.Success ? name.Output.Trim() : null;
            return (ours, string.IsNullOrEmpty(theirs) ? await ShortHashAsync(incoming) : theirs);
        }

        private static async Task<string> ShortHashAsync(string revision)
        {
            GitResult result = await GitRepository.RunAsync("rev-parse", "--short", "--verify", "--quiet", revision);
            return result.Success ? result.Output.Trim() : null;
        }

        /// <summary>한쪽만 고른다(이미지는 섞을 수 없다). 고른 쪽을 다시 누르면 고르지 않은 상태로.</summary>
        private void ChooseImage(GitConflictSide side, bool chosen)
        {
            _imageChoice = chosen ? side : _imageChoice == side ? null : _imageChoice;
            RenderImages();
        }

        private void RenderImages()
        {
            _imageA.SetChosen(_imageChoice == GitConflictSide.Ours);
            _imageB.SetChosen(_imageChoice == GitConflictSide.Theirs);
            GitImageSide chosen = _imageChoice == GitConflictSide.Ours ? _images?.Left
                : _imageChoice == GitConflictSide.Theirs ? _images?.Right
                : null;
            Texture2D texture = chosen?.Texture;
            _imageOutput.image = texture;
            _imageOutputFrame.SetVisible(texture != null);
            _imageOutputHint.text = _imageChoice == null ? GitText.Get("merge.imagePick")
                : texture == null ? GitText.Get("merge.imageDeleteChosen")
                : string.Empty;
            _imageOutputHint.SetVisible(texture == null);
            FitImage(_imageOutputStage, _imageOutputFrame, texture);
            _save.tooltip = _imageChoice == null ? GitText.Get("merge.imagePick") : GitText.Get("merge.imageSaveTooltip");
            UpdateSave();
        }

        private void SetImageMode(bool on)
        {
            _imageMode = on;
            _imageView.SetVisible(on);
        }

        private void ReleaseImages()
        {
            _imageVersion++;
            _images?.Dispose();
            _images = null;
            _imagesPath = null;
            _imageA.Show(null, null);
            _imageB.Show(null, null);
            _imageOutput.image = null;
        }

        /// <summary>이미지를 담는 칸: 체커 무늬를 깐 틀(이미지 비율대로 칸 안에 맞춘다) 안에 이미지, 비었을 때의 안내.</summary>
        private static VisualElement ImageStage(out VisualElement frame, out Image image, out Label hint)
        {
            VisualElement stage = new();
            stage.AddToClassList("git-merge__image-stage");
            VisualElement fitFrame = new();
            fitFrame.AddToClassList("git-merge__image-frame");
            fitFrame.AddToClassList("git-image-diff__checker");
            fitFrame.style.backgroundImage = GitImageDiffView.Checker;
            Image fitImage = new() { scaleMode = ScaleMode.StretchToFill };
            fitImage.AddToClassList("git-merge__image");
            fitFrame.Add(fitImage);
            stage.Add(fitFrame);
            hint = new Label();
            hint.AddToClassList("git-merge__image-hint");
            stage.Add(hint);
            stage.RegisterCallback<GeometryChangedEvent>(_ => FitImage(stage, fitFrame, fitImage.image as Texture2D));
            frame = fitFrame;
            image = fitImage;
            return stage;
        }

        /// <summary>
        /// 틀 크기를 이미지 비율대로 칸 안에 맞춘다. 작은 이미지(아이콘·픽셀 아트)도 잘 보이게 칸에 맞춰 키운다 — 점 필터라 흐려지지 않는다.
        /// </summary>
        private static void FitImage(VisualElement stage, VisualElement frame, Texture2D texture)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0)
            {
                return;
            }
            float width = stage.contentRect.width;
            float height = stage.contentRect.height;
            if (float.IsNaN(width) || float.IsNaN(height) || width <= 0f || height <= 0f)
            {
                return;
            }
            float scale = Mathf.Min(width / texture.width, height / texture.height);
            frame.style.width = Mathf.Floor(texture.width * scale);
            frame.style.height = Mathf.Floor(texture.height * scale);
        }

        /// <summary>A·B 이미지 칸 하나: 머리(고르기 체크·배지·이름), 이미지, 아래 크기 정보.</summary>
        private sealed class ImagePane
        {
            public readonly VisualElement Root;
            private readonly Toggle _choose;
            private readonly Label _title;
            private readonly VisualElement _stage;
            private readonly VisualElement _frame;
            private readonly Image _image;
            private readonly Label _hint;
            private readonly Label _info;

            public ImagePane(GitMergeSide side, Action<bool> chosen)
            {
                Root = new VisualElement();
                Root.AddToClassList("git-merge__pane");
                Root.AddToClassList(side == GitMergeSide.A ? "git-merge__pane--a" : "git-merge__pane--b");
                VisualElement header = new();
                header.AddToClassList("git-merge__pane-header");
                _choose = new Toggle { tooltip = GitText.Get(side == GitMergeSide.A ? "merge.imageChooseA" : "merge.imageChooseB") };
                _choose.AddToClassList("git-merge__all");
                _choose.RegisterValueChangedCallback(evt => chosen(evt.newValue));
                header.Add(_choose);
                Label badge = new(side == GitMergeSide.A ? "A" : "B");
                badge.AddToClassList("git-merge__badge");
                header.Add(badge);
                _title = new Label();
                _title.AddToClassList("git-merge__pane-title");
                header.Add(_title);
                Root.Add(header);
                _stage = ImageStage(out _frame, out _image, out _hint);
                // 이미지를 눌러도 고른다(작은 체크 칸만 누르지 않아도 되게).
                _stage.RegisterCallback<ClickEvent>(_ => _choose.value = _choose.value == false);
                Root.Add(_stage);
                _info = new Label();
                _info.AddToClassList("git-merge__image-info");
                Root.Add(_info);
            }

            public void SetTitle(string title)
            {
                _title.text = title ?? string.Empty;
            }

            public void SetChosen(bool chosen)
            {
                _choose.SetValueWithoutNotify(chosen);
                Root.EnableInClassList("git-merge__pane--chosen", chosen);
            }

            public void ShowLoading()
            {
                _image.image = null;
                _frame.SetVisible(false);
                _hint.text = GitText.Get("merge.imageLoading");
                _hint.SetVisible(true);
                _info.text = string.Empty;
            }

            public void Show(GitImageSide side, string error)
            {
                Texture2D texture = side?.Texture;
                if (texture != null)
                {
                    texture.filterMode = FilterMode.Point;
                }
                _image.image = texture;
                _frame.SetVisible(texture != null);
                _hint.text = error ?? (side == null ? string.Empty : side.IsMissingLfs ? GitText.Get("merge.imageMissingLfs") : GitText.Get("merge.imageDeleted"));
                _hint.SetVisible(texture == null);
                _info.text = texture != null ? GitText.Format("merge.imageInfo", texture.width, texture.height, GitImageDiff.FormatBytes(side.ByteSize)) : string.Empty;
                FitImage(_stage, _frame, texture);
            }
        }

        private static string FullPath(string repoPath)
        {
            return Path.Combine(GitRepository.RepoRoot ?? GitRepository.ProjectRoot, repoPath);
        }
    }
}
