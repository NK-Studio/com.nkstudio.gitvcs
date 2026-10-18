using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// diff 보기 본체: 도구 막대(보기 방식·외부 도구), 통계 줄, 줄 목록. git diff 옵션은 설정에서 고른다.
    /// git diff 출력(<see cref="GitDiff.Parse"/>)을 통합 보기 또는 나란히 보기로 그린다.
    /// 줄 번호, 더한·지운 줄 배경, 줄 안에서 실제로 바뀐 부분 강조, 변경 묶음(hunk) 사이 이동을 지원한다.
    /// 독립 창(<see cref="GitDiffWindow"/>)과 Fork 형태 레이아웃의 오른쪽 칸이 같은 패널을 쓴다.
    /// </summary>
    public sealed class GitDiffPanel
    {
        private const float RowHeight = 18f;

        private readonly IGitDiffService _service;
        private readonly Font _monospace;
        private GitDiffTarget _target;
        private GitDiffDocument _document;
        private List<GitDiffRow> _rows = new();
        private int _loadVersion;

        private Label _title;
        private Label _addedStat;
        private Label _removedStat;
        private Label _revisions;
        private readonly Label _notice;
        private readonly ListView _list;
        private ToolbarToggle _unifiedToggle;
        private ToolbarToggle _splitToggle;

        private ToolbarButton _closeButton;
        private ToolbarButton _reloadButton;

        /// <summary>
        /// 닫기(×)를 눌렀을 때. <see cref="ShowCloseButton"/> 로 버튼을 켠 곳(GitKraken 레이아웃의 가운데)만 쓴다.
        /// </summary>
        public event Action CloseRequested;

        public VisualElement Root { get; }

        public void ShowCloseButton(bool isVisible)
        {
            _closeButton.SetVisible(isVisible);
        }

        /// <summary>
        /// 다시 읽기 버튼. 따로 뜨는 창(diff·비교·파일 기록)에서만 쓰고, Git 창 안에서는 끈다 — 창 위쪽 새로고침이 diff 까지 다시 읽는다.
        /// </summary>
        public void ShowReloadButton(bool isVisible)
        {
            _reloadButton.SetVisible(isVisible);
        }
        public GitDiffTarget Target => _target;

        public GitDiffPanel(IGitDiffService service)
        {
            _service = service;
            _monospace = GitFonts.Monospace;

            Root = new VisualElement();
            Root.AddToClassList("git-diff");

            Root.Add(BuildToolbar());
            Root.Add(BuildInfoBar());

            _notice = new Label();
            _notice.AddToClassList("git-diff__notice");
            Root.Add(_notice);

            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                horizontalScrollingEnabled = false,
            };
            _list.AddToClassList("git-diff__list");
            Root.Add(_list);

            Root.RegisterCallback<AttachToPanelEvent>(_ => GitVcsSettings.DiffOptionsChanged += OnDiffOptionsChanged);
            Root.RegisterCallback<DetachFromPanelEvent>(_ => GitVcsSettings.DiffOptionsChanged -= OnDiffOptionsChanged);

            ApplyLayout();
            ShowEmpty();
        }

        /// <summary>
        /// 이 파일의 diff 를 읽어 보인다. null 이면 안내 문구만.
        /// </summary>
        public void SetTarget(GitDiffTarget target)
        {
            _target = target;
            Reload();
        }

        public void Reload()
        {
            if (_target == null)
            {
                ShowEmpty();
                return;
            }
            LoadAsync();
        }

        private void ShowEmpty()
        {
            ++_loadVersion;
            _document = null;
            _title.text = string.Empty;
            _addedStat.text = string.Empty;
            _removedStat.text = string.Empty;
            _revisions.text = string.Empty;
            _notice.text = GitText.Get("diff.noFile");
            _notice.SetVisible(true);
            ApplyLayout();
        }

        private VisualElement BuildToolbar()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            Toolbar toolbar = new();
            toolbar.AddToClassList("git-diff__toolbar");

            _title = new Label();
            _title.AddToClassList("git-diff__title");
            toolbar.Add(_title);
            toolbar.Add(Spacer());

            _unifiedToggle = new ToolbarToggle { text = GitText.Get("diff.unified") };
            _splitToggle = new ToolbarToggle { text = GitText.Get("diff.sideBySide") };
            _unifiedToggle.RegisterValueChangedCallback(evt => SetLayout(evt.newValue ? DiffLayout.Unified : DiffLayout.SideBySide));
            _splitToggle.RegisterValueChangedCallback(evt => SetLayout(evt.newValue ? DiffLayout.SideBySide : DiffLayout.Unified));
            toolbar.Add(_unifiedToggle);
            toolbar.Add(_splitToggle);

            // 알고리즘·공백·문맥 줄 수는 설정(Project Settings > Version Control)에서만 바꾼다. 바뀌면 OnDiffOptionsChanged 가 다시 읽는다.
            ToolbarButton external = new(() =>
            {
                if (_target != null)
                {
                    DiffEngine engine = settings.DiffEngine == DiffEngine.BuiltIn ? DiffEngine.GitDifftool : settings.DiffEngine;
                    _service.OpenExternally(_target, engine);
                }
            })
            {
                text = GitText.Get("diff.openExternal"),
                tooltip = GitText.Get("diff.openExternalTooltip"),
            };
            toolbar.Add(external);

            _reloadButton = new ToolbarButton(Reload) { tooltip = GitText.Get("common.reload") };
            ToolbarIcons.SetRefreshIcon(_reloadButton);
            toolbar.Add(_reloadButton);

            _closeButton = new ToolbarButton(() => CloseRequested?.Invoke()) { text = "×", tooltip = GitText.Get("common.close") };
            _closeButton.SetVisible(false);
            toolbar.Add(_closeButton);
            return toolbar;
        }

        private VisualElement BuildInfoBar()
        {
            VisualElement info = new();
            info.AddToClassList("git-diff__info");

            _addedStat = new Label();
            _addedStat.AddToClassList("git-diff__stat");
            _addedStat.AddToClassList("git-diff__stat--added");
            info.Add(_addedStat);

            _removedStat = new Label();
            _removedStat.AddToClassList("git-diff__stat");
            _removedStat.AddToClassList("git-diff__stat--removed");
            info.Add(_removedStat);

            _revisions = new Label();
            _revisions.AddToClassList("git-diff__revisions");
            info.Add(_revisions);
            info.Add(Spacer());

            Button previous = new(() => JumpToHunk(-1)) { text = "▲", tooltip = GitText.Get("diff.previousChange") };
            previous.AddToClassList("git-diff__nav");
            info.Add(previous);
            Button next = new(() => JumpToHunk(1)) { text = "▼", tooltip = GitText.Get("diff.nextChange") };
            next.AddToClassList("git-diff__nav");
            info.Add(next);
            return info;
        }

        private void OnDiffOptionsChanged()
        {
            if (_target != null)
            {
                Reload();
            }
        }

        private static VisualElement Spacer()
        {
            VisualElement spacer = new();
            spacer.AddToClassList("spacer");
            return spacer;
        }

        private void SetLayout(DiffLayout layout)
        {
            GitVcsSettings.instance.DiffLayout = layout;
            ApplyLayout();
        }

        /// <summary>
        /// 보기 방식에 맞는 줄 모양으로 목록을 다시 만든다(git 을 다시 부르지 않는다).
        /// </summary>
        private void ApplyLayout()
        {
            bool isSplit = GitVcsSettings.instance.DiffLayout == DiffLayout.SideBySide;
            _unifiedToggle.SetValueWithoutNotify(isSplit == false);
            _splitToggle.SetValueWithoutNotify(isSplit);

            // makeItem·bindItem 은 바꾸는 즉시 목록을 다시 채운다. 하나씩 바꾸면 그 사이에 옛 모양 줄을 새 bind 로 채우다 깨지므로
            // 먼저 목록을 비워 채울 줄을 없앤다.
            _list.itemsSource = new List<GitDiffRow>();
            _list.makeItem = isSplit ? MakeSplitRow : MakeUnifiedRow;
            _list.bindItem = isSplit ? BindSplitRow : BindUnifiedRow;
            _rows = _document == null ? new List<GitDiffRow>() : isSplit ? GitDiff.ToSideBySide(_document.Lines) : _document.Lines.Select(line => new GitDiffRow(line, line)).ToList();
            _list.itemsSource = _rows;
            _list.Rebuild();
        }

        private async void LoadAsync()
        {

            int version = ++_loadVersion;
            _title.text = GitDisplay.Nfc(_target.OriginalRepoPath != null ? $"{_target.OriginalRepoPath} → {_target.RepoPath}" : _target.RepoPath);
            _title.tooltip = _title.text;
            _revisions.text = DescribeRevisions(_target);
            _notice.text = GitText.Get("common.loading");
            _notice.SetVisible(true);

            (GitDiffDocument document, string error) = await _service.LoadAsync(_target, GitVcsSettings.instance.DiffOptions);
            if (version != _loadVersion)
            {
                // 기다리는 동안 다른 파일을 열었거나 옵션을 바꿨다.
                return;
            }

            _document = document;
            _addedStat.text = document != null ? $"+{document.Added}" : string.Empty;
            _removedStat.text = document != null ? $"−{document.Removed}" : string.Empty;
            if (error != null)
            {
                _notice.text = error;
            }
            else if (document.IsBinary)
            {
                _notice.text = GitText.Get("diff.binary");
            }
            else if (document.IsEmpty)
            {
                _notice.text = GitText.Get("diff.noDifference");
            }
            _notice.SetVisible(error != null || document.IsBinary || document.IsEmpty);
            ApplyLayout();
        }

        private static string DescribeRevisions(GitDiffTarget target)
        {
            switch (target.Source)
            {
                case GitDiffSource.WorkingTree:
                    return GitText.Get("diff.revisionsWorkingTree");
                case GitDiffSource.Staged:
                    return GitText.Get("diff.revisionsStaged");
                case GitDiffSource.Unstaged:
                    return GitText.Get("diff.revisionsUnstaged");
                case GitDiffSource.Commit:
                    return GitText.Format("diff.revisionsCommit", target.Commit.Substring(0, Math.Min(8, target.Commit.Length)));
                case GitDiffSource.MergeBaseRange:
                    return GitText.Format("diff.revisionsRange", target.From, target.To);
                case GitDiffSource.Range when target.To == null:
                    return GitText.Format("diff.revisionsCommitWorkingTree", GitDisplay.ShortRevision(target.From));
                case GitDiffSource.Range:
                    return GitText.Format("diff.revisionsTwoCommits", GitDisplay.ShortRevision(target.From), GitDisplay.ShortRevision(target.To));
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target.Source, null);
            }
        }

        /// <summary>
        /// 앞(-1)·뒤(+1)의 다음 변경 묶음 머리 줄로 스크롤한다.
        /// </summary>
        private void JumpToHunk(int direction)
        {
            if (_rows.Count == 0)
            {
                return;
            }
            ScrollView scroll = _list.Q<ScrollView>();
            int first = scroll == null ? 0 : Mathf.FloorToInt(scroll.scrollOffset.y / RowHeight);
            for (int index = first + direction; index >= 0 && index < _rows.Count; index += direction)
            {
                GitDiffLine line = _rows[index].Left ?? _rows[index].Right;
                if (line != null && line.Kind == GitDiffLineKind.Hunk)
                {
                    _list.ScrollToItem(index);
                    return;
                }
            }
        }

        // ── 통합 보기 ─────────────────────────────

        private VisualElement MakeUnifiedRow()
        {
            VisualElement row = new();
            row.AddToClassList("diff-row");
            row.Add(NumberLabel());
            row.Add(NumberLabel());
            Label sign = new();
            sign.AddToClassList("diff-row__sign");
            row.Add(sign);
            row.Add(CodeElement());
            return row;
        }

        private void BindUnifiedRow(VisualElement row, int index)
        {
            GitDiffLine line = _rows[index].Left;
            SetKindClasses(row, "diff-row", line);
            ((Label)row[0]).text = line.OldNumber > 0 ? line.OldNumber.ToString() : string.Empty;
            ((Label)row[1]).text = line.NewNumber > 0 ? line.NewNumber.ToString() : string.Empty;
            ((Label)row[2]).text = line.Kind switch
            {
                GitDiffLineKind.Added => "+",
                GitDiffLineKind.Removed => "−",
                _ => string.Empty,
            };
            BindCode(row[3], line);
        }

        // ── 나란히 보기 ───────────────────────────

        private VisualElement MakeSplitRow()
        {
            VisualElement row = new();
            row.AddToClassList("diff-row");
            row.Add(SideElement("diff-side--left"));
            row.Add(SideElement("diff-side--right"));
            return row;
        }

        private VisualElement SideElement(string modifier)
        {
            VisualElement side = new();
            side.AddToClassList("diff-side");
            side.AddToClassList(modifier);
            side.Add(NumberLabel());
            Label sign = new();
            sign.AddToClassList("diff-row__sign");
            side.Add(sign);
            side.Add(CodeElement());
            return side;
        }

        private void BindSplitRow(VisualElement row, int index)
        {
            GitDiffRow diffRow = _rows[index];
            BindSide(row[0], diffRow.Left, isLeft: true);
            BindSide(row[1], diffRow.Right, isLeft: false);
        }

        private void BindSide(VisualElement side, GitDiffLine line, bool isLeft)
        {
            side.EnableInClassList("diff-side--empty", line == null);
            SetKindClasses(side, "diff-side", line);
            Label number = (Label)side[0];
            Label sign = (Label)side[1];
            if (line == null)
            {
                number.text = string.Empty;
                sign.text = string.Empty;
                BindCode(side[2], null);
                return;
            }
            int lineNumber = isLeft ? line.OldNumber : line.NewNumber;
            number.text = lineNumber > 0 ? lineNumber.ToString() : string.Empty;
            sign.text = line.Kind switch
            {
                GitDiffLineKind.Added => "+",
                GitDiffLineKind.Removed => "−",
                _ => string.Empty,
            };
            BindCode(side[2], line);
        }

        // ── 공용 ─────────────────────────────────

        private static Label NumberLabel()
        {
            Label label = new();
            label.AddToClassList("diff-row__number");
            return label;
        }

        /// <summary>
        /// 코드 한 줄: 앞·바뀐 부분·뒤 세 조각. 바뀐 부분만 진한 배경(<c>.diff-row__change</c>)을 받는다.
        /// 리치 텍스트로 칠하지 않는 이유: 코드의 &lt;·&gt; 가 태그로 해석되지 않게 막을 깔끔한 방법이 없다.
        /// </summary>
        private VisualElement CodeElement()
        {
            VisualElement code = new();
            code.AddToClassList("diff-row__code");
            for (int i = 0; i < 3; i++)
            {
                Label part = new() { enableRichText = false };
                part.AddToClassList("diff-row__part");
                if (i == 1)
                {
                    part.AddToClassList("diff-row__change");
                }
                if (_monospace != null)
                {
                    part.style.unityFontDefinition = FontDefinition.FromFont(_monospace);
                }
                code.Add(part);
            }
            return code;
        }

        private static void BindCode(VisualElement code, GitDiffLine line)
        {
            Label before = (Label)code[0];
            Label change = (Label)code[1];
            Label after = (Label)code[2];
            if (line == null)
            {
                before.text = string.Empty;
                change.text = string.Empty;
                after.text = string.Empty;
                return;
            }
            string raw = line.Text ?? string.Empty;
            if (line.ChangeLength <= 0)
            {
                before.text = ExpandTabs(raw);
                change.text = string.Empty;
                after.text = string.Empty;
                return;
            }
            // 탭을 펼치기 전 위치로 자른 뒤 각 조각을 펼친다(펼친 뒤에는 위치가 달라진다).
            int start = Math.Min(line.ChangeStart, raw.Length);
            int length = Math.Min(line.ChangeLength, raw.Length - start);
            before.text = ExpandTabs(raw.Substring(0, start));
            change.text = ExpandTabs(raw.Substring(start, length));
            after.text = ExpandTabs(raw.Substring(start + length));
        }

        /// <summary>
        /// 탭 폭 4. 조각마다 따로 펼치므로 열을 정확히 맞추지는 않는다(줄 안 강조가 있는 줄만 해당).
        /// </summary>
        private static string ExpandTabs(string text)
        {
            return text.Replace("\t", "    ");
        }

        private static void SetKindClasses(VisualElement element, string block, GitDiffLine line)
        {
            GitDiffLineKind? kind = line?.Kind;
            element.EnableInClassList(block + "--added", kind == GitDiffLineKind.Added);
            element.EnableInClassList(block + "--removed", kind == GitDiffLineKind.Removed);
            element.EnableInClassList(block + "--hunk", kind == GitDiffLineKind.Hunk);
            element.EnableInClassList(block + "--meta", kind == GitDiffLineKind.NoNewline);
        }
    }
}
