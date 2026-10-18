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

        /// <summary>
        /// "불러오는 중" 표시. 안내 문구(<see cref="_notice"/>)와 달리 내용 위에 겹쳐 떠서 자리를 차지하지 않는다 —
        /// 자리를 차지하면 다시 읽을 때마다 내용이 밀렸다 올라와 깜빡여 보인다. 늦을 때(<see cref="LoadingDelayMs"/>)만 보인다.
        /// </summary>
        private readonly Label _loading;
        private IVisualElementScheduledItem _loadingDelay;
        private const long LoadingDelayMs = 250;

        /// <summary>
        /// 지금 보이는 diff 의 파일(출처·경로). 같은 파일을 다시 읽을 때(저장·Stage 뒤 새로고침)는 내용을 그대로 두고 로딩 표시도 하지 않는다.
        /// </summary>
        private string _shownKey;

        /// <summary>
        /// 지금 목록의 줄 모양(true = 나란히). 바뀔 때만 줄 요소를 새로 만든다.
        /// </summary>
        private bool? _shownSplit;

        /// <summary>
        /// 다른 파일로 바뀌었으니 새 내용이 들어오면 맨 위로 올린다.
        /// </summary>
        private bool _scrollToTop;

        // 줄 번호 칸: 자릿수 × 글자 폭 + 오른쪽 여백(.diff-row__number 의 padding-right 6 과 왼쪽 숨 4).
        private const int MinNumberDigits = 2;
        private const float NumberDigitWidth = 7f;
        private const float NumberPadding = 10f;
        private float _numberWidth = 3 * NumberDigitWidth + NumberPadding;
        private bool _showOldNumbers = true;
        private bool _showNewNumbers = true;

        private readonly ListView _list;
        private ToolbarToggle _unifiedToggle;
        private ToolbarToggle _splitToggle;
        private Button _previousButton;
        private Button _nextButton;
        private readonly GitImageDiffView _imageView;
        private readonly VisualElement _binaryView;
        private bool _isImageMode;

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

            // 이미지 파일이면 줄 목록 대신 이 뷰가 보인다.
            _imageView = new GitImageDiffView();
            Root.Add(_imageView.Root);

            // 바이너리 파일(이미지 밖)이면 줄 목록 대신 파일 카드가 보인다(Fork 와 같다).
            _binaryView = new VisualElement();
            _binaryView.AddToClassList("git-diff-binary");
            _binaryView.SetVisible(false);
            Root.Add(_binaryView);

            // 맨 마지막에 넣어 목록·이미지 위에 겹쳐 그린다.
            _loading = new Label(GitText.Get("common.loading")) { pickingMode = PickingMode.Ignore };
            _loading.AddToClassList("git-diff__loading");
            _loading.SetVisible(false);
            Root.Add(_loading);

            Root.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                GitVcsSettings.DiffOptionsChanged += OnDiffOptionsChanged;
                // 패널에서 떨어질 때 텍스처를 지웠으므로(GitKraken 레이아웃이 가운데 칸을 바꿔 끼울 때 등) 다시 읽는다.
                if (_isImageMode && _imageView.WasCleared && _target != null)
                {
                    Reload();
                }
            });
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
            HideLoading();
            _shownKey = null;
            _document = null;
            _title.text = string.Empty;
            _addedStat.text = string.Empty;
            _removedStat.text = string.Empty;
            _revisions.text = string.Empty;
            _notice.text = GitText.Get("diff.noFile");
            _notice.SetVisible(true);
            _imageView.Clear();
            SetImageMode(false);
            ShowBinary(false);
            ApplyLayout();
        }

        /// <summary>
        /// 이미지 diff 와 텍스트 diff 를 오간다. 이미지일 때는 줄 목록·통합/나란히·변경 묶음 이동·+/- 통계가 뜻이 없어 숨긴다.
        /// </summary>
        /// <summary>
        /// 바이너리 파일 카드를 보이고(줄 목록은 숨긴다) 끄면 목록을 되돌린다.
        /// </summary>
        private void ShowBinary(bool shown)
        {
            _binaryView.SetVisible(shown);
            if (_isImageMode == false)
            {
                _list.SetVisible(shown == false);
            }
            if (shown == false)
            {
                _binaryView.Clear();
            }
        }

        /// <summary>
        /// 양쪽 판 크기를 읽어 카드를 채운다: 추가 = 새 파일 하나("created"), 삭제 = 옛 파일 하나("deleted"),
        /// 수정 = 옛 → 새 두 장("modified"). 크기는 "242 KB (248,320 B)".
        /// </summary>
        private async void LoadBinaryAsync(int version, GitDiffTarget target)
        {
            (long? left, long? right, string error) = await _service.LoadSizesAsync(target);
            if (version != _loadVersion)
            {
                return;
            }
            _binaryView.Clear();
            if (error != null)
            {
                _binaryView.Add(BinaryLabel(error, "git-diff-binary__error"));
                return;
            }
            (string statusKey, string statusModifier) = left == null ? ("diff.binaryCreated", "git-diff-binary__status--created")
                : right == null ? ("diff.binaryDeleted", "git-diff-binary__status--deleted")
                : ("diff.binaryModified", "git-diff-binary__status--modified");
            Label status = BinaryLabel(GitText.Get(statusKey), "git-diff-binary__status");
            status.AddToClassList(statusModifier);
            _binaryView.Add(status);

            VisualElement cards = new();
            cards.AddToClassList("git-diff-binary__cards");
            if (left != null)
            {
                cards.Add(BinaryCard(target.LeftPath, left.Value));
            }
            if (left != null && right != null)
            {
                cards.Add(BinaryLabel("→", "git-diff-binary__arrow"));
            }
            if (right != null)
            {
                cards.Add(BinaryCard(target.RepoPath, right.Value));
            }
            _binaryView.Add(cards);
        }

        private static VisualElement BinaryCard(string repoPath, long bytes)
        {
            VisualElement column = new();
            column.AddToClassList("git-diff-binary__column");
            VisualElement card = new();
            card.AddToClassList("git-diff-binary__card");
            Image icon = new() { image = UnityEditorInternal.InternalEditorUtility.GetIconForFile(repoPath ?? string.Empty), scaleMode = ScaleMode.ScaleToFit };
            icon.AddToClassList("git-diff-binary__icon");
            card.Add(icon);
            string extension = System.IO.Path.GetExtension(repoPath ?? string.Empty);
            card.Add(BinaryLabel(string.IsNullOrEmpty(extension) ? GitDisplay.Nfc(System.IO.Path.GetFileName(repoPath ?? string.Empty)) : extension, "git-diff-binary__extension"));
            column.Add(card);
            string size = $"{GitImageDiff.FormatBytes(bytes)} ({bytes.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)} B)";
            column.Add(BinaryLabel(size, "git-diff-binary__size"));
            return column;
        }

        private static Label BinaryLabel(string text, string className)
        {
            Label label = new(text);
            label.AddToClassList(className);
            return label;
        }

        private void SetImageMode(bool isImage)
        {
            _isImageMode = isImage;
            _list.SetVisible(isImage == false);
            _unifiedToggle.SetVisible(isImage == false);
            _splitToggle.SetVisible(isImage == false);
            _previousButton.SetVisible(isImage == false);
            _nextButton.SetVisible(isImage == false);
            _addedStat.SetVisible(isImage == false);
            _removedStat.SetVisible(isImage == false);
            _imageView.Root.SetVisible(false);
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

            _previousButton = new Button(() => JumpToHunk(-1)) { text = "▲", tooltip = GitText.Get("diff.previousChange") };
            _previousButton.AddToClassList("git-diff__nav");
            info.Add(_previousButton);
            _nextButton = new Button(() => JumpToHunk(1)) { text = "▼", tooltip = GitText.Get("diff.nextChange") };
            _nextButton.AddToClassList("git-diff__nav");
            info.Add(_nextButton);
            return info;
        }

        private void OnDiffOptionsChanged()
        {
            // 알고리즘·공백 옵션은 이미지 비교와 상관없다.
            if (_target != null && _isImageMode == false)
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

            // 줄 모양(통합·나란히)이 바뀔 때만 줄 요소를 새로 만든다. 같은 모양이면 보이는 줄만 다시 채워(RefreshItems)
            // 다른 파일을 고를 때마다 목록 전체가 지워졌다 다시 그려지며 깜빡이지 않게 한다.
            bool rebuild = _shownSplit != isSplit;
            if (rebuild)
            {
                // makeItem·bindItem 은 바꾸는 즉시 목록을 다시 채운다. 하나씩 바꾸면 그 사이에 옛 모양 줄을 새 bind 로 채우다 깨지므로
                // 먼저 목록을 비워 채울 줄을 없앤다.
                _list.itemsSource = new List<GitDiffRow>();
                _list.makeItem = isSplit ? MakeSplitRow : MakeUnifiedRow;
                _list.bindItem = isSplit ? BindSplitRow : BindUnifiedRow;
                _shownSplit = isSplit;
            }
            UpdateGutter(isSplit);
            _rows = _document == null ? new List<GitDiffRow>() : isSplit ? GitDiff.ToSideBySide(_document.Lines) : _document.Lines.Select(line => new GitDiffRow(line, line)).ToList();
            _list.itemsSource = _rows;
            if (rebuild)
            {
                _list.Rebuild();
            }
            else
            {
                _list.RefreshItems();
            }
        }

        /// <summary>
        /// 줄 번호 칸 폭을 이 파일의 가장 큰 번호 자릿수에 맞춘다(고정 폭이면 짧은 파일에 왼쪽 여백이 크게 남는다).
        /// 통합 보기에서 한쪽 번호가 하나도 없으면(새 파일·지운 파일) 그 칸은 숨긴다 — Fork 처럼 번호 칸이 하나만 남는다.
        /// </summary>
        private void UpdateGutter(bool isSplit)
        {
            if (_document == null)
            {
                return;
            }
            int maxNumber = 0;
            bool anyOld = false;
            bool anyNew = false;
            foreach (GitDiffLine line in _document.Lines)
            {
                maxNumber = Math.Max(maxNumber, Math.Max(line.OldNumber, line.NewNumber));
                anyOld |= line.OldNumber > 0;
                anyNew |= line.NewNumber > 0;
            }
            int digits = Math.Max(MinNumberDigits, maxNumber.ToString().Length);
            _numberWidth = digits * NumberDigitWidth + NumberPadding;
            _showOldNumbers = isSplit || anyOld || anyNew == false;
            _showNewNumbers = isSplit || anyNew || anyOld == false;
        }

        private void ApplyNumberColumn(VisualElement number, bool shown)
        {
            number.style.width = _numberWidth;
            number.style.minWidth = _numberWidth;
            number.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private async void LoadAsync()
        {

            int version = ++_loadVersion;
            _title.text = GitDisplay.Nfc(_target.OriginalRepoPath != null ? $"{_target.OriginalRepoPath} → {_target.RepoPath}" : _target.RepoPath);
            _title.tooltip = _title.text;
            _revisions.text = DescribeRevisions(_target);

            // 같은 파일을 다시 읽으면 지금 내용을 둔 채 조용히 바꾼다. 다른 파일이어도 새 내용이 올 때까지 지금 내용을 둔다 —
            // 미리 비우면 빈 화면이 끼어 깜빡인다. 늦을 때(LoadingDelayMs)만 비우고 "불러오는 중" 을 겹쳐 띄운다.
            string key = $"{_target.Source}|{_target.Commit}|{_target.From}|{_target.To}|{_target.RepoPath}";
            if (key != _shownKey)
            {
                _shownKey = key;
                _scrollToTop = true;
                ShowLoadingLater(version);
            }

            // 이미지 비교는 양쪽 판이 다 있을 때만(수정). 새로 만든·지운 이미지는 비교할 반대쪽이 없으니
            // 다른 바이너리처럼 파일 카드("created"·"deleted"·크기)로 보인다.
            bool isImage = GitImageDiff.IsImage(_target.RepoPath)
                           && _target.State != GitFileState.Added && _target.State != GitFileState.Untracked && _target.State != GitFileState.Deleted;
            SetImageMode(isImage);
            if (isImage)
            {
                ShowBinary(false);
                _document = null;
                ApplyLayout();
                LoadImagesAsync(version);
                return;
            }

            (GitDiffDocument document, string error) = await _service.LoadAsync(_target, GitVcsSettings.instance.DiffOptions);
            if (version != _loadVersion)
            {
                // 기다리는 동안 다른 파일을 열었거나 옵션을 바꿨다.
                return;
            }

            HideLoading();
            _document = document;
            _addedStat.text = document != null ? $"+{document.Added}" : string.Empty;
            _removedStat.text = document != null ? $"−{document.Removed}" : string.Empty;
            if (error != null)
            {
                _notice.text = error;
            }
            else if (document.IsEmpty && document.IsBinary == false)
            {
                _notice.text = GitText.Get("diff.noDifference");
            }
            // 바이너리는 안내 문구 대신 Fork 처럼 파일 카드(상태·아이콘·확장자·크기)를 보인다.
            bool isBinary = error == null && document.IsBinary;
            _notice.SetVisible(error != null || (document.IsEmpty && isBinary == false));
            ShowBinary(isBinary);
            if (isBinary)
            {
                LoadBinaryAsync(version, _target);
            }
            ApplyLayout();
            if (_scrollToTop)
            {
                _scrollToTop = false;
                if (_rows.Count > 0)
                {
                    _list.ScrollToItem(0);
                }
            }
        }

        private async void LoadImagesAsync(int version)
        {
            (GitImagePair pair, string error) = await _service.LoadImagesAsync(_target);
            if (version != _loadVersion)
            {
                pair?.Dispose();
                return;
            }
            HideLoading();
            if (error != null)
            {
                _imageView.Clear();
                _notice.text = GitText.Format("imageDiff.loadFailed", error);
                _notice.SetVisible(true);
                return;
            }
            _notice.SetVisible(false);
            _imageView.SetImages(pair);
            _imageView.Root.SetVisible(true);
        }

        /// <summary>
        /// 이 읽기(<paramref name="version"/>)가 <see cref="LoadingDelayMs"/> 안에 끝나지 않으면 "불러오는 중" 을 띄운다(빨리 끝나면 아예 안 보인다).
        /// </summary>
        private void ShowLoadingLater(int version)
        {
            _loadingDelay?.Pause();
            _loadingDelay = Root.schedule.Execute(() =>
            {
                if (version != _loadVersion)
                {
                    return;
                }
                // 늦어지면 옛 파일 내용을 비우고(다른 파일 내용이 남아 헷갈리지 않게) "불러오는 중" 을 겹쳐 띄운다.
                _notice.SetVisible(false);
                ShowBinary(false);
                _document = null;
                ApplyLayout();
                _loading.SetVisible(true);
            }).StartingIn(LoadingDelayMs);
        }

        private void HideLoading()
        {
            _loadingDelay?.Pause();
            _loadingDelay = null;
            _loading.SetVisible(false);
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
            ApplyNumberColumn(row[0], _showOldNumbers);
            ApplyNumberColumn(row[1], _showNewNumbers);
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
            ApplyNumberColumn(number, true);
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
