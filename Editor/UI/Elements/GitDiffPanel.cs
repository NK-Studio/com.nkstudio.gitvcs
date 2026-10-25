using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
        private Button _previousButton;
        private Button _nextButton;

        // ── GitKraken 식 diff 막대(이 파일 편집 | Unstaged · 파일/Diff 보기 | Blame·히스토리 · ↑↓ · 보기 방식 · 공백·줄 바꿈) ──
        private VisualElement _bar;
        private Button _editButton;
        private Button _sideChip;
        private Button _fileViewButton;
        private Button _diffViewButton;
        private Button _blameButton;
        private Button _historyButton;
        private Button _hunkLayoutButton;
        private Button _inlineLayoutButton;
        private Button _splitLayoutButton;
        private Button _whitespaceButton;
        private Button _wrapButton;

        /// <summary>
        /// 텍스트 diff 에만 뜻이 있는 막대 묶음(파일/Diff 보기·변경 이동·보기 방식·공백·줄 바꿈). 이미지·바이너리면 숨긴다.
        /// </summary>
        private readonly List<VisualElement> _textOnlyControls = new();

        /// <summary>
        /// 막대가 좁을 때 줄이는 단계. 이 패널은 Git 창의 좁은 가운데 칸에도 들어간다.
        /// Compact: 가운데 맞춤을 풀고 "이 파일 편집" 을 아이콘만. Narrow: Blame·히스토리 숨김(파일 목록 메뉴에도 있다). Tiny: 공백·줄 바꿈 숨김.
        /// </summary>
        private const float CompactBarWidth = 820f;
        private const float NarrowBarWidth = 690f;
        private const float TinyBarWidth = 560f;

        /// <summary>
        /// 지금 읽어 둔 diff 의 문맥 줄 수. 보기 방식을 바꿨을 때 다시 읽어야 하는지(변경 묶음 ↔ 파일 전체) 가른다.
        /// </summary>
        private int _loadedContextLines = -1;

        /// <summary>
        /// 지금 목록이 긴 줄 접기(가변 높이) 모양인지. 바뀔 때만 줄 요소를 새로 만든다.
        /// </summary>
        private bool? _shownWrap;
        private bool _wrap;
        private bool _showWhitespace;

        /// <summary>
        /// ↑↓ 로 마지막에 간 줄(목록 인덱스). 끝 근처에서 더 내려가지 못해 화면 맨 위가 그 줄이 아닐 때도 다음 변경으로 이어 가게 한다.
        /// </summary>
        private int _lastJumpRow = -1;

        /// <summary>
        /// ↑↓ 로 간 변경 위에 남겨 둘 줄 수(바로 위 문맥이 보이게).
        /// </summary>
        private const int JumpMargin = 3;
        private readonly GitImageDiffView _imageView;
        private readonly VisualElement _binaryView;
        private readonly VisualElement _lfsView;
        private readonly Label _lfsMessage;
        private readonly Button _lfsFetchButton;
        private readonly Button _lfsAccountButton;
        private bool _isImageMode;

        private ToolbarButton _closeButton;
        private ToolbarButton _reloadButton;

        // ── 부분 스테이징(GitKraken 식 거터 + 버튼) ──

        /// <summary>
        /// 줄·hunk 단위 Stage·Unstage 를 돌릴 곳. <see cref="EnablePartialStaging"/> 로 넣은 패널(Fork·GitKraken 레이아웃)에서만 버튼이 보인다.
        /// </summary>
        private IGitOperationRunner _runner;
        private IGitCommandService _commands;

        /// <summary>
        /// 고른 줄(<see cref="_document"/>.Lines 의 인덱스). 끌기·Shift+클릭으로 모은 뒤 거터 버튼 한 번으로 같이 올린다. 문서가 바뀌면 비운다.
        /// </summary>
        private readonly HashSet<int> _selectedLines = new();
        private int _selectionAnchor = -1;
        private bool _dragSelecting;

        /// <summary>
        /// 줄 → 줄 목록 인덱스. 나란히 보기의 줄 요소는 줄만 알고 목록 인덱스는 모른다.
        /// </summary>
        private readonly Dictionary<GitDiffLine, int> _lineIndexes = new();

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

        /// <summary>
        /// 거터의 "이 줄 스테이지" 버튼과 머리 줄의 "hunk 스테이지" 버튼을 켠다. Unstaged·Staged 출처의 diff 에서만 보인다.
        /// </summary>
        public void EnablePartialStaging(IGitOperationRunner runner, IGitCommandService commands)
        {
            _runner = runner;
            _commands = commands;
            _list.RefreshItems();
        }

        /// <summary>
        /// 지금 보이는 diff 에서 줄·hunk 단위 Stage·Unstage 가 되는지. 공백을 무시한 diff 는 줄 내용이 인덱스와 달라 패치가 붙지 않으므로 뺀다.
        /// </summary>
        private bool CanPartialStage => _runner != null && _document != null && _target != null && _isImageMode == false && _document.IsBinary == false
            && (_target.Source == GitDiffSource.Unstaged || _target.Source == GitDiffSource.Staged)
            && GitVcsSettings.instance.DiffOptions.Whitespace == DiffWhitespace.Show
            // 파일 보기는 지운 줄·머리 줄을 숨겨 고를 수 있는 줄이 반쪽이다. GitKraken 처럼 Diff 보기에서만 줄을 올린다.
            && GitVcsSettings.instance.DiffFileView == false;

        /// <summary>
        /// 이번에 git 에 넘길 diff 옵션. 변경 묶음(Hunk) 보기만 설정의 문맥 줄 수를 쓰고, 통합·나란히·파일 보기는 GitKraken 처럼 파일 전체를 읽는다.
        /// </summary>
        private static GitDiffOptions EffectiveOptions()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            GitDiffOptions options = settings.DiffOptions;
            bool wholeFile = settings.DiffFileView || settings.DiffLayout != DiffLayout.Unified;
            return wholeFile ? new GitDiffOptions(options.Algorithm, options.Whitespace, GitDiffOptions.MaxContextLines) : options;
        }

        /// <summary>
        /// Staged 목록의 diff 면 버튼은 내리기(Unstage)다.
        /// </summary>
        private bool IsUnstage => _target != null && _target.Source == GitDiffSource.Staged;

        public GitDiffPanel(IGitDiffService service)
        {
            _service = service;
            _monospace = GitFonts.Monospace;

            Root = new VisualElement();
            Root.AddToClassList("git-diff");

            Root.Add(BuildToolbar());
            Root.Add(BuildBar());

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
            // 줄 끌어 고르기는 줄 요소의 PointerDown·PointerEnter 로 하고, 버튼을 떼거나 목록 밖으로 나가면 끝낸다.
            _list.RegisterCallback<PointerUpEvent>(_ => _dragSelecting = false, TrickleDown.TrickleDown);
            _list.RegisterCallback<PointerLeaveEvent>(_ => _dragSelecting = false);
            Root.Add(_list);

            // 이미지 파일이면 줄 목록 대신 이 뷰가 보인다.
            _imageView = new GitImageDiffView();
            Root.Add(_imageView.Root);

            // LFS 원본을 받지 않은 이미지(저장소가 lfs.fetchexclude 로 막아 둔 아트 원본 등)는 안내와 "이 파일 원본 받기" 버튼.
            _lfsView = new VisualElement();
            _lfsView.AddToClassList("git-diff-lfs");
            _lfsMessage = new Label();
            _lfsMessage.AddToClassList("git-diff-lfs__message");
            _lfsView.Add(_lfsMessage);
            _lfsFetchButton = new Button(FetchLfs);
            _lfsFetchButton.AddToClassList("git-diff-lfs__button");
            _lfsView.Add(_lfsFetchButton);
            // 계정 문제로 못 받았을 때만 보인다. 이 창은 Git 창과 따로 떠서 계정 팝업 대신 Project Settings > Git 을 연다.
            _lfsAccountButton = new Button(() => SettingsService.OpenProjectSettings(GitIntegration.ProjectSettingsPath)) { text = GitText.Get("lfs.chooseAccount") };
            _lfsAccountButton.AddToClassList("git-diff-lfs__button");
            _lfsView.Add(_lfsAccountButton);
            _lfsView.SetVisible(false);
            Root.Add(_lfsView);

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
            SetDocument(null);
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
            _addedStat.SetVisible(isImage == false);
            _removedStat.SetVisible(isImage == false);
            _imageView.Root.SetVisible(false);
            _lfsView.SetVisible(false);
            UpdateBar();
        }

        /// <summary>
        /// LFS 원본이 없다는 안내와 받기 버튼을 보인다.
        /// </summary>
        /// <param name="failure">방금 받기에 실패했으면 그 이유(안내 아래에 덧붙인다).</param>
        private void ShowLfsMissing(long missingBytes, string failure)
        {
            _imageView.Clear();
            _imageView.Root.SetVisible(false);
            _notice.SetVisible(false);
            string message = GitText.Get("lfs.missing");
            bool isAuthFailure = failure != null && GitAuth.IsAuthFailure(failure);
            if (isAuthFailure)
            {
                // GitHub 는 권한 없는 계정에게 비공개 저장소를 "Not Found" 로 답한다. git-lfs 의 긴 오류 대신 고치는 법을 보인다.
                message += "\n\n" + GitText.Get("lfs.authFailed");
            }
            else if (failure != null)
            {
                message += "\n\n" + GitText.Format("lfs.fetchFailed", FirstLine(failure));
            }
            _lfsMessage.text = message;
            _lfsAccountButton.SetVisible(isAuthFailure);
            _lfsFetchButton.text = missingBytes > 0
                ? GitText.Format("lfs.fetchWithSize", GitImageDiff.FormatBytes(missingBytes))
                : GitText.Get("lfs.fetch");
            _lfsFetchButton.SetEnabled(true);
            _lfsView.SetVisible(true);
        }

        /// <summary>
        /// git-lfs 오류에서 사람이 읽을 첫 줄("Downloading …" 진행 줄은 건너뛴다).
        /// </summary>
        private static string FirstLine(string message)
        {
            foreach (string line in message.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0 && trimmed.StartsWith("Downloading ", StringComparison.Ordinal) == false)
                {
                    return trimmed;
                }
            }
            return message.Trim();
        }

        /// <summary>
        /// 이 파일의 두 판만 LFS 원본을 받아 다시 보인다. 받은 원본은 .git/lfs 에 남아 다음부터는 바로 보인다.
        /// </summary>
        private void FetchLfs()
        {
            if (_target == null)
            {
                return;
            }
            _lfsFetchButton.SetEnabled(false);
            _lfsFetchButton.text = GitText.Get("lfs.fetching");
            LoadImagesAsync(++_loadVersion, fetchLfs: true);
        }

        private VisualElement BuildToolbar()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            Toolbar toolbar = new();
            toolbar.AddToClassList("git-diff__toolbar");

            _title = new Label();
            _title.AddToClassList("git-diff__title");
            toolbar.Add(_title);

            // 더한·지운 줄 수와 비교하는 두 판. GitKraken 처럼 파일 이름 줄에 같이 둔다.
            _addedStat = new Label();
            _addedStat.AddToClassList("git-diff__stat");
            _addedStat.AddToClassList("git-diff__stat--added");
            toolbar.Add(_addedStat);
            _removedStat = new Label();
            _removedStat.AddToClassList("git-diff__stat");
            _removedStat.AddToClassList("git-diff__stat--removed");
            toolbar.Add(_removedStat);
            _revisions = new Label();
            _revisions.AddToClassList("git-diff__revisions");
            toolbar.Add(_revisions);
            toolbar.Add(Spacer());

            // 알고리즘·공백·문맥 줄 수는 설정(Project Settings > Git)에서만 바꾼다. 바뀌면 OnDiffOptionsChanged 가 다시 읽는다.
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

        /// <summary>
        /// GitKraken 의 diff 막대. 왼쪽 "이 파일 편집", 가운데 지금 보는 쪽(Unstaged·Staged)과 파일/Diff 보기, 오른쪽 Blame·히스토리, 변경 이동, 보기 방식 셋, 공백·줄 바꿈.
        /// 가운데 묶음이 늘 가운데 오도록 왼쪽·오른쪽 묶음이 남는 폭을 반씩 나눠 가진다.
        /// </summary>
        private VisualElement BuildBar()
        {
            _bar = new VisualElement();
            _bar.AddToClassList("git-diff__bar");

            VisualElement left = BarGroup("git-diff__bar-side");
            _editButton = BarButton(OpenInEditor, GitText.Get("diff.editFileTooltip"), "edit", GitText.Get("diff.editFile"));
            _editButton.AddToClassList("git-diff__bar-button--outlined");
            left.Add(_editButton);
            _bar.Add(left);

            VisualElement center = BarGroup("git-diff__bar-center");
            _sideChip = new Button(SwitchSide);
            _sideChip.AddToClassList("git-diff__chip");
            center.Add(_sideChip);
            _fileViewButton = BarButton(() => SetFileView(true), GitText.Get("diff.fileViewTooltip"), null, GitText.Get("diff.fileView"));
            _diffViewButton = BarButton(() => SetFileView(false), GitText.Get("diff.diffViewTooltip"), null, GitText.Get("diff.diffView"));
            VisualElement viewSegment = Segment(_fileViewButton, _diffViewButton);
            center.Add(viewSegment);
            _textOnlyControls.Add(viewSegment);
            _bar.Add(center);

            VisualElement right = BarGroup("git-diff__bar-side");
            right.AddToClassList("git-diff__bar-side--right");
            _blameButton = BarButton(OpenBlame, GitText.Get("diff.blameTooltip"), null, GitText.Get("diff.blame"));
            _historyButton = BarButton(OpenHistory, GitText.Get("diff.historyTooltip"), null, GitText.Get("diff.history"));
            VisualElement fileTools = Segment(_blameButton, _historyButton);
            fileTools.AddToClassList("git-diff__segment--file-tools");
            right.Add(fileTools);

            _previousButton = BarButton(() => JumpToChange(-1), GitText.Get("diff.previousChange"), "up", null);
            _nextButton = BarButton(() => JumpToChange(1), GitText.Get("diff.nextChange"), "down", null);
            VisualElement navigation = Segment(_previousButton, _nextButton);
            right.Add(navigation);
            _textOnlyControls.Add(navigation);

            _hunkLayoutButton = BarButton(() => SetLayout(DiffLayout.Unified), GitText.Get("diff.viewHunk"), "hunk", null);
            _inlineLayoutButton = BarButton(() => SetLayout(DiffLayout.Inline), GitText.Get("diff.viewInline"), "inline", null);
            _splitLayoutButton = BarButton(() => SetLayout(DiffLayout.SideBySide), GitText.Get("diff.viewSplit"), "split", null);
            VisualElement layouts = Segment(_hunkLayoutButton, _inlineLayoutButton, _splitLayoutButton);
            right.Add(layouts);
            _textOnlyControls.Add(layouts);

            _whitespaceButton = BarButton(ToggleWhitespace, GitText.Get("diff.showWhitespace"), "whitespace", null);
            _wrapButton = BarButton(ToggleWrap, GitText.Get("diff.wordWrap"), "wrap", null);
            VisualElement display = Segment(_whitespaceButton, _wrapButton);
            display.AddToClassList("git-diff__segment--display");
            right.Add(display);
            _textOnlyControls.Add(display);
            _bar.Add(right);

            // 좁은 칸(Git 창 가운데)에서는 단계적으로 줄인다(CompactBarWidth 참고).
            _bar.RegisterCallback<GeometryChangedEvent>(evt =>
            {
                float width = evt.newRect.width;
                _bar.EnableInClassList("git-diff__bar--compact", width < CompactBarWidth);
                _bar.EnableInClassList("git-diff__bar--narrow", width < NarrowBarWidth);
                _bar.EnableInClassList("git-diff__bar--tiny", width < TinyBarWidth);
            });
            return _bar;
        }

        private static VisualElement BarGroup(string className)
        {
            VisualElement group = new();
            group.AddToClassList("git-diff__bar-group");
            group.AddToClassList(className);
            return group;
        }

        /// <summary>
        /// 막대 버튼. 아이콘(<c>Icons/Diff*.png</c>, USS 가 테마 색으로 물들인다)과 글자 중 하나 또는 둘 다.
        /// </summary>
        /// <param name="icon">아이콘 이름(<c>.git-diff__bar-icon--{icon}</c>). 없으면 null.</param>
        /// <param name="text">글자. 없으면 null. 좁은 막대에서는 아이콘이 있는 버튼만 글자를 숨긴다.</param>
        private static Button BarButton(Action onClick, string tooltip, string icon, string text)
        {
            Button button = new(onClick) { tooltip = tooltip };
            button.AddToClassList("git-diff__bar-button");
            if (icon != null)
            {
                VisualElement image = new() { pickingMode = PickingMode.Ignore };
                image.AddToClassList("git-diff__bar-icon");
                image.AddToClassList("git-diff__bar-icon--" + icon);
                button.Add(image);
            }
            if (text != null)
            {
                Label label = new(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList("git-diff__bar-text");
                label.EnableInClassList("git-diff__bar-text--with-icon", icon != null);
                button.Add(label);
            }
            button.EnableInClassList("git-diff__bar-button--icon-only", text == null);
            return button;
        }

        /// <summary>
        /// 붙어 있는 버튼 묶음(GitKraken 의 [File View|Diff View]·[↑|↓] 처럼 한 테두리 안에 칸으로 나뉜다).
        /// USS 의 :first-child 를 쓸 수 없어 맨 앞 칸에 수식어를 단다.
        /// </summary>
        private static VisualElement Segment(params Button[] buttons)
        {
            VisualElement segment = new();
            segment.AddToClassList("git-diff__segment");
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].AddToClassList("git-diff__segment-button");
                buttons[i].EnableInClassList("git-diff__segment-button--first", i == 0);
                segment.Add(buttons[i]);
            }
            return segment;
        }

        private static void SetChecked(Button button, bool isChecked)
        {
            button.EnableInClassList("git-diff__bar-button--checked", isChecked);
        }

        /// <summary>
        /// 막대 버튼들을 지금 파일·설정에 맞춘다(눌린 칸, 쓸 수 있는지, 보일지).
        /// </summary>
        private void UpdateBar()
        {
            if (_bar == null)
            {
                return;
            }
            GitVcsSettings settings = GitVcsSettings.instance;
            bool hasTarget = _target != null;
            _bar.SetVisible(hasTarget);
            if (hasTarget == false)
            {
                return;
            }
            bool isText = _isImageMode == false;
            foreach (VisualElement control in _textOnlyControls)
            {
                control.SetVisible(isText);
            }

            bool fileView = settings.DiffFileView;
            SetChecked(_fileViewButton, fileView);
            SetChecked(_diffViewButton, fileView == false);
            DiffLayout layout = settings.DiffLayout;
            SetChecked(_hunkLayoutButton, fileView == false && layout == DiffLayout.Unified);
            SetChecked(_inlineLayoutButton, fileView == false && layout == DiffLayout.Inline);
            SetChecked(_splitLayoutButton, fileView == false && layout == DiffLayout.SideBySide);
            // 파일 보기는 diff 가 아니라 보기 방식이 뜻이 없다(GitKraken 도 끈다).
            _hunkLayoutButton.SetEnabled(fileView == false);
            _inlineLayoutButton.SetEnabled(fileView == false);
            _splitLayoutButton.SetEnabled(fileView == false);
            SetChecked(_whitespaceButton, settings.DiffShowWhitespace);
            SetChecked(_wrapButton, settings.DiffWordWrap);

            bool exists = System.IO.File.Exists(FullPathOf(_target));
            _editButton.SetEnabled(exists);
            _editButton.tooltip = GitText.Get(exists ? "diff.editFileTooltip" : "diff.editFileMissing");

            bool hasHistory = HasCommittedVersion(_target);
            _blameButton.SetEnabled(hasHistory && isText);
            _historyButton.SetEnabled(hasHistory);
            _blameButton.tooltip = GitText.Get(hasHistory ? "diff.blameTooltip" : "diff.noCommittedVersion");
            _historyButton.tooltip = GitText.Get(hasHistory ? "diff.historyTooltip" : "diff.noCommittedVersion");

            UpdateSideChip();
        }

        /// <summary>
        /// 가운데 칩: 지금 보는 쪽(Unstaged·Staged·작업 사본). 같은 파일이 양쪽에 다 바뀌어 있으면(일부만 stage 한 파일) 눌러서 다른 쪽으로 바꾼다.
        /// 커밋·범위 비교에서는 뜻이 없어 숨긴다.
        /// </summary>
        private void UpdateSideChip()
        {
            string current = _target.Source switch
            {
                GitDiffSource.Unstaged => GitText.Get("diff.sideUnstaged"),
                GitDiffSource.Staged => GitText.Get("diff.sideStaged"),
                GitDiffSource.WorkingTree => GitText.Get("diff.sideWorkingTree"),
                _ => null,
            };
            _sideChip.SetVisible(current != null);
            if (current == null)
            {
                return;
            }
            _sideChip.text = current;
            _sideChip.EnableInClassList("git-diff__chip--staged", _target.Source == GitDiffSource.Staged);
            GitDiffTarget other = OtherSideOf(_target);
            _sideChip.SetEnabled(other != null);
            _sideChip.EnableInClassList("git-diff__chip--switchable", other != null);
            _sideChip.tooltip = other != null
                ? GitText.Format("diff.sideSwitchTooltip", current, GitText.Get(other.Source == GitDiffSource.Staged ? "diff.sideStaged" : "diff.sideUnstaged"))
                : GitText.Format("diff.sideTooltip", current);
        }

        /// <summary>
        /// 같은 파일의 반대쪽(Unstaged ↔ Staged) diff 대상. 반대쪽에 변경이 없으면 null.
        /// </summary>
        private static GitDiffTarget OtherSideOf(GitDiffTarget target)
        {
            if (target.Source != GitDiffSource.Unstaged && target.Source != GitDiffSource.Staged)
            {
                return null;
            }
            GitStatusEntry entry = GitRepository.Entries.FirstOrDefault(e => e.RepoPath == target.RepoPath);
            if (entry == null)
            {
                return null;
            }
            bool toStaged = target.Source == GitDiffSource.Unstaged;
            List<GitStatusEntry> side = toStaged ? GitStaging.Staged(new[] { entry }) : GitStaging.Unstaged(new[] { entry });
            return side.Count > 0 ? GitDiffTarget.ForStaging(side[0], toStaged) : null;
        }

        private void SwitchSide()
        {
            GitDiffTarget other = _target == null ? null : OtherSideOf(_target);
            if (other != null)
            {
                SetTarget(other);
            }
        }

        private static string FullPathOf(GitDiffTarget target)
        {
            string root = GitRepository.RepoRoot ?? GitRepository.ProjectRoot;
            return System.IO.Path.GetFullPath(System.IO.Path.Combine(root, target.RepoPath));
        }

        /// <summary>
        /// 커밋된 판이 있는지(Blame·히스토리가 뜻이 있는지). 작업 중인 새 파일(untracked·새로 추가)은 없다.
        /// </summary>
        private static bool HasCommittedVersion(GitDiffTarget target)
        {
            bool isWorkInProgress = target.Source == GitDiffSource.WorkingTree || target.Source == GitDiffSource.Unstaged || target.Source == GitDiffSource.Staged;
            return isWorkInProgress == false || (target.State != GitFileState.Untracked && target.State != GitFileState.Added);
        }

        /// <summary>
        /// "이 파일 편집": 작업 사본 파일을 코드 편집기로 연다. ↑↓ 로 간 변경(없으면 첫 변경)의 새 판 줄로 간다.
        /// Unity 에셋이면 Project 창에서 연 것과 같게(스크립트는 설정의 외부 편집기), 아니면 외부 편집기·OS 기본 프로그램으로.
        /// </summary>
        private void OpenInEditor()
        {
            if (_target == null)
            {
                return;
            }
            string fullPath = FullPathOf(_target);
            if (System.IO.File.Exists(fullPath) == false)
            {
                return;
            }
            int line = LineToOpen();
            string projectPath = GitStatusParser.ToProjectPath(_target.RepoPath, GitRepository.ProjectPrefix, GitRepository.RepoInProject);
            UnityEngine.Object asset = string.IsNullOrEmpty(projectPath) ? null : AssetDatabase.LoadMainAssetAtPath(GitDisplay.AssetPathOf(projectPath));
            if (asset != null && AssetDatabase.OpenAsset(asset, line))
            {
                return;
            }
            if (UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(fullPath, line) == false)
            {
                EditorUtility.OpenWithDefaultApp(fullPath);
            }
        }

        /// <summary>
        /// 편집기에서 열 줄(새 판 기준, 1부터). 지운 줄이면 그 아래 첫 남은 줄. 없으면 1.
        /// </summary>
        private int LineToOpen()
        {
            int start = _lastJumpRow >= 0 ? _lastJumpRow : ChangeStartRows().DefaultIfEmpty(0).First();
            for (int index = Math.Max(0, start); index < _rows.Count; index++)
            {
                GitDiffLine line = _rows[index].Right ?? _rows[index].Left;
                if (line != null && line.NewNumber > 0)
                {
                    return line.NewNumber;
                }
            }
            return 1;
        }

        private void OpenBlame()
        {
            if (_target != null)
            {
                GitBlameWindow.Open(_target.RepoPath);
            }
        }

        private void OpenHistory()
        {
            if (_target != null)
            {
                GitFileHistoryWindow.Open(_target.RepoPath, _service);
            }
        }

        private void SetFileView(bool fileView)
        {
            GitVcsSettings.instance.DiffFileView = fileView;
            ReloadOrRelayout();
        }

        private void ToggleWhitespace()
        {
            GitVcsSettings.instance.DiffShowWhitespace = GitVcsSettings.instance.DiffShowWhitespace == false;
            ApplyLayout();
        }

        private void ToggleWrap()
        {
            GitVcsSettings.instance.DiffWordWrap = GitVcsSettings.instance.DiffWordWrap == false;
            ApplyLayout();
        }

        /// <summary>
        /// 보기를 바꾼 뒤: 읽어 둔 diff 의 문맥 줄 수로 충분하면(같은 범위) 다시 그리기만, 아니면(변경 묶음 ↔ 파일 전체) git 을 다시 부른다.
        /// </summary>
        private void ReloadOrRelayout()
        {
            if (_target != null && _isImageMode == false && _document != null && EffectiveOptions().ContextLines != _loadedContextLines)
            {
                Reload();
                return;
            }
            ApplyLayout();
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
            ReloadOrRelayout();
        }

        /// <summary>
        /// 보기 방식에 맞는 줄 모양으로 목록을 다시 만든다(git 을 다시 부르지 않는다).
        /// </summary>
        private void ApplyLayout()
        {
            GitVcsSettings settings = GitVcsSettings.instance;
            bool fileView = settings.DiffFileView;
            bool isSplit = fileView == false && settings.DiffLayout == DiffLayout.SideBySide;
            _showWhitespace = settings.DiffShowWhitespace;
            _wrap = settings.DiffWordWrap;
            Root.EnableInClassList("git-diff--wrap", _wrap);
            Root.EnableInClassList("git-diff--file-view", fileView);
            UpdateBar();

            // 줄 모양(통합·나란히, 고정·가변 높이)이 바뀔 때만 줄 요소를 새로 만든다. 같은 모양이면 보이는 줄만 다시 채워(RefreshItems)
            // 다른 파일을 고를 때마다 목록 전체가 지워졌다 다시 그려지며 깜빡이지 않게 한다.
            bool rebuild = _shownSplit != isSplit || _shownWrap != _wrap;
            if (rebuild)
            {
                // makeItem·bindItem 은 바꾸는 즉시 목록을 다시 채운다. 하나씩 바꾸면 그 사이에 옛 모양 줄을 새 bind 로 채우다 깨지므로
                // 먼저 목록을 비워 채울 줄을 없앤다.
                _list.itemsSource = new List<GitDiffRow>();
                // 접은 줄은 높이가 줄마다 달라 가변 높이로 둔다(고정 높이가 훨씬 빨라 접지 않을 때는 고정).
                _list.virtualizationMethod = _wrap ? CollectionVirtualizationMethod.DynamicHeight : CollectionVirtualizationMethod.FixedHeight;
                _list.makeItem = isSplit ? MakeSplitRow : MakeUnifiedRow;
                _list.bindItem = isSplit ? BindSplitRow : BindUnifiedRow;
                _shownSplit = isSplit;
                _shownWrap = _wrap;
            }
            UpdateGutter(isSplit, fileView);
            _rows = _document == null ? new List<GitDiffRow>()
                : fileView ? FileViewRows(_document.Lines)
                : isSplit ? GitDiff.ToSideBySide(_document.Lines)
                : _document.Lines.Select(line => new GitDiffRow(line, line)).ToList();
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
        private void UpdateGutter(bool isSplit, bool fileView)
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
            if (fileView)
            {
                // 파일 보기는 한 판(새 판, 지운 파일이면 옛 판)만 보이므로 번호 칸도 하나.
                _showOldNumbers = anyNew == false;
                _showNewNumbers = anyNew;
                return;
            }
            _showOldNumbers = isSplit || anyOld || anyNew == false;
            _showNewNumbers = isSplit || anyNew || anyOld == false;
        }

        /// <summary>
        /// 파일 보기(GitKraken 의 File View)의 줄: 바뀐 뒤 파일 전체(문맥·더한 줄). 지운 파일이면 바뀌기 전 파일 전체(문맥·지운 줄).
        /// 머리 줄·"파일 끝 개행 없음" 표시는 뺀다. 더한 줄은 줄 배경으로 그대로 표시된다.
        /// </summary>
        private static List<GitDiffRow> FileViewRows(IReadOnlyList<GitDiffLine> lines)
        {
            bool hasNewSide = lines.Any(line => line.NewNumber > 0);
            GitDiffLineKind changed = hasNewSide ? GitDiffLineKind.Added : GitDiffLineKind.Removed;
            List<GitDiffRow> rows = new();
            foreach (GitDiffLine line in lines)
            {
                if (line.Kind == GitDiffLineKind.Context || line.Kind == changed)
                {
                    rows.Add(new GitDiffRow(line, line));
                }
            }
            return rows;
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

            // 이미지는 이미지 보기로. 새로 만든·지운 이미지는 비교 없이 있는 쪽 이미지만 "추가됨"·"삭제됨" 과 함께 보인다(GitImageDiffView).
            bool isImage = GitImageDiff.IsImage(_target.RepoPath);
            SetImageMode(isImage);
            if (isImage)
            {
                ShowBinary(false);
                SetDocument(null);
                ApplyLayout();
                LoadImagesAsync(version);
                return;
            }

            GitDiffOptions options = EffectiveOptions();
            (GitDiffDocument document, string error) = await _service.LoadAsync(_target, options);
            if (version != _loadVersion)
            {
                // 기다리는 동안 다른 파일을 열었거나 옵션을 바꿨다.
                return;
            }
            _loadedContextLines = options.ContextLines;

            HideLoading();
            SetDocument(document);
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

        private async void LoadImagesAsync(int version, bool fetchLfs = false)
        {
            (GitImagePair pair, string error) = await _service.LoadImagesAsync(_target, fetchLfs);
            if (version != _loadVersion)
            {
                pair?.Dispose();
                return;
            }
            HideLoading();
            if (fetchLfs && error != null)
            {
                // 받기에 실패했다(권한·네트워크 등). 안내를 남겨 다시 누를 수 있게 한다.
                ShowLfsMissing(0, error);
                return;
            }
            if (pair != null && pair.IsMissingLfs)
            {
                long missingBytes = pair.MissingLfsBytes;
                pair.Dispose();
                ShowLfsMissing(missingBytes, fetchLfs ? GitText.Get("lfs.stillMissing") : null);
                return;
            }
            _lfsView.SetVisible(false);
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
                SetDocument(null);
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

        /// <summary>
        /// 보일 문서를 바꾼다. 줄 인덱스 표를 다시 만들고 고른 줄은 비운다(줄이 바뀌었으니 옛 인덱스는 뜻이 없다). 부르는 쪽이 ApplyLayout 으로 다시 그린다.
        /// </summary>
        private void SetDocument(GitDiffDocument document)
        {
            _document = document;
            _lineIndexes.Clear();
            if (document != null)
            {
                for (int i = 0; i < document.Lines.Count; i++)
                {
                    _lineIndexes[document.Lines[i]] = i;
                }
            }
            _selectedLines.Clear();
            _selectionAnchor = -1;
            _dragSelecting = false;
            _lastJumpRow = -1;
        }

        // ── 부분 스테이징 ───────────────────────────

        /// <summary>
        /// 줄 요소에 끌기·Shift+클릭 고르기를 단다. 요소의 userData 는 그 줄의 목록 인덱스(없으면 -1)로 bind 때 넣는다.
        /// </summary>
        private void RegisterSelection(VisualElement element)
        {
            element.RegisterCallback<PointerDownEvent>(evt => OnRowPointerDown(element, evt));
            element.RegisterCallback<PointerEnterEvent>(_ => OnRowPointerEnter(element));
        }

        private static int LineIndexOf(VisualElement element)
        {
            return element.userData is int index ? index : -1;
        }

        private void OnRowPointerDown(VisualElement element, PointerDownEvent evt)
        {
            // 버튼 자체를 누른 것은 버튼이 처리한다. 오른쪽 버튼은 메뉴 몫.
            if (evt.button != 0 || CanPartialStage == false || evt.target is Button)
            {
                return;
            }
            int index = LineIndexOf(element);
            if (index < 0 || GitPartialPatch.IsChange(_document.Lines[index]) == false)
            {
                ClearSelection();
                return;
            }
            if (evt.shiftKey && _selectionAnchor >= 0)
            {
                SelectRange(_selectionAnchor, index);
            }
            else
            {
                _selectedLines.Clear();
                _selectedLines.Add(index);
                _selectionAnchor = index;
            }
            _dragSelecting = true;
            _list.RefreshItems();
        }

        private void OnRowPointerEnter(VisualElement element)
        {
            if (_dragSelecting == false || CanPartialStage == false || _selectionAnchor < 0)
            {
                return;
            }
            int index = LineIndexOf(element);
            if (index >= 0)
            {
                SelectRange(_selectionAnchor, index);
                _list.RefreshItems();
            }
        }

        /// <summary>
        /// 두 인덱스 사이의 바뀐 줄('+'·'-')을 모두 고른다. 묶음 여러 개에 걸쳐도 된다 — 패치가 묶음마다 나눠 만든다.
        /// </summary>
        private void SelectRange(int from, int to)
        {
            _selectedLines.Clear();
            int first = Math.Min(from, to);
            int last = Math.Max(from, to);
            for (int i = first; i <= last && i < _document.Lines.Count; i++)
            {
                if (GitPartialPatch.IsChange(_document.Lines[i]))
                {
                    _selectedLines.Add(i);
                }
            }
        }

        private void ClearSelection()
        {
            if (_selectedLines.Count == 0 && _selectionAnchor < 0)
            {
                return;
            }
            _selectedLines.Clear();
            _selectionAnchor = -1;
            _list.RefreshItems();
        }

        /// <summary>
        /// 줄 번호 왼쪽의 버튼 칸(GitKraken 의 거터). 안에 + 버튼 하나. 부분 스테이징이 안 되는 diff 에서는 bind 가 칸째 숨긴다.
        /// </summary>
        private VisualElement GutterElement()
        {
            VisualElement gutter = new();
            gutter.AddToClassList("diff-row__gutter");
            gutter.Add(StageButton());
            return gutter;
        }

        /// <summary>
        /// 거터의 + 버튼(GitKraken 의 "Stage this line"). 마우스를 올린 바뀐 줄과 고른 줄에서만 보인다(USS).
        /// </summary>
        private Button StageButton()
        {
            Button button = new();
            button.AddToClassList("diff-row__stage");
            button.clicked += () =>
            {
                int index = LineIndexOf(button);
                if (index < 0)
                {
                    return;
                }
                // 고른 줄 중 하나를 누르면 고른 줄을 모두, 아니면 그 줄 하나만.
                ApplyLinesAsync(_selectedLines.Contains(index) ? new List<int>(_selectedLines) : new List<int> { index });
            };
            return button;
        }

        /// <summary>
        /// 머리 줄 오른쪽의 "hunk 스테이지" 버튼. userData 는 묶음 순번.
        /// </summary>
        private Button HunkButton()
        {
            Button button = new();
            button.AddToClassList("diff-row__hunk-button");
            button.clicked += () =>
            {
                int hunkIndex = LineIndexOf(button);
                if (hunkIndex >= 0 && _document != null)
                {
                    ApplyLinesAsync(GitPartialPatch.ChangedLinesOfHunk(_document.Lines, hunkIndex));
                }
            };
            return button;
        }

        /// <summary>
        /// 줄 요소의 거터(버튼 칸)·머리 줄 버튼을 이 줄에 맞춘다. 부분 스테이징이 안 되면 거터 칸을 숨겨 자리를 내주지 않고,
        /// 바뀐 줄이 아니면 버튼만 숨기며, 머리 줄이 아니면 hunk 버튼을 숨긴다.
        /// </summary>
        /// <returns>이 줄이 고른 줄인지.</returns>
        private bool BindStageControls(VisualElement element, VisualElement gutter, Button hunk, GitDiffLine line)
        {
            int index = -1;
            bool canStage = CanPartialStage;
            bool enabled = canStage && line != null && _lineIndexes.TryGetValue(line, out index);
            if (enabled == false)
            {
                index = -1;
            }
            element.userData = index;
            bool isChange = enabled && GitPartialPatch.IsChange(line);
            bool selected = isChange && _selectedLines.Contains(index);
            gutter.style.display = canStage ? DisplayStyle.Flex : DisplayStyle.None;
            Button stage = (Button)gutter[0];
            stage.userData = index;
            // 인라인 visibility 가 USS 의 :hover 규칙보다 세다. 바뀐 줄이면 인라인 값을 지워 USS 가 정하게 둔다.
            stage.style.visibility = isChange ? StyleKeyword.Null : Visibility.Hidden;
            stage.EnableInClassList("diff-row__stage--unstage", IsUnstage);
            stage.text = IsUnstage ? "−" : "+";
            stage.tooltip = selected && _selectedLines.Count > 1
                ? GitText.Format(IsUnstage ? "diff.unstageSelectedLines" : "diff.stageSelectedLines", _selectedLines.Count)
                : GitText.Get(IsUnstage ? "diff.unstageLine" : "diff.stageLine");
            if (hunk != null)
            {
                bool isHunk = enabled && line.Kind == GitDiffLineKind.Hunk;
                hunk.style.display = isHunk ? DisplayStyle.Flex : DisplayStyle.None;
                hunk.userData = isHunk ? line.HunkIndex : -1;
                hunk.text = GitText.Get(IsUnstage ? "diff.unstageHunk" : "diff.stageHunk");
                hunk.tooltip = GitText.Get(IsUnstage ? "diff.unstageHunkTooltip" : "diff.stageHunkTooltip");
            }
            return selected;
        }

        /// <summary>
        /// 고른 줄만 든 패치를 만들어 인덱스에 적용한다(Unstaged 면 올리고, Staged 면 내린다). 실패하면 runner 가 오류 팝업을 띄운다.
        /// 되돌릴 수 있는 일(다시 Stage·Unstage 하면 된다)이라 묻지 않는다. 끝나면 diff 를 다시 읽어 남은 변경만 보인다.
        /// </summary>
        private async void ApplyLinesAsync(ICollection<int> lines)
        {
            if (CanPartialStage == false || _runner.IsBusy)
            {
                return;
            }
            GitDiffTarget target = _target;
            bool reverse = IsUnstage;
            string patch = GitPartialPatch.Build(_document.Lines, lines, target.RepoPath, reverse);
            if (patch == null)
            {
                return;
            }
            // 문맥 줄 없이(-U0) 읽은 diff 에서 만든 패치는 git apply 가 따로 허락해야 받는다.
            bool zeroContext = _loadedContextLines == 0;
            // git 이 모르는 파일은 인덱스에 항목이 없어 패치가 붙을 곳이 없다. add -N 으로 빈 항목을 먼저 만든다.
            string intentToAdd = reverse == false && target.State == GitFileState.Untracked ? target.RepoPath : null;
            ClearSelection();
            await _runner.RunAsync(GitText.Get(reverse ? "op.unstageLines" : "op.stageLines"),
                () => _commands.ApplyPatchToIndexAsync(patch, reverse, zeroContext, intentToAdd), refreshAssets: false, workingTreeOnly: true);
            // 새로고침으로 목록이 바뀌며 뷰가 이미 다시 읽었을 수 있다. 같은 파일을 아직 보고 있으면 한 번 더 맞춘다.
            if (_target == target)
            {
                Reload();
            }
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
        /// 앞(-1)·뒤(+1)의 다음 변경(이어진 더한·지운 줄 덩어리의 첫 줄)으로 간다. 파일 전체를 보는 통합·나란히 보기에는 머리 줄이 하나뿐이라
        /// 머리 줄이 아니라 바뀐 덩어리 단위로 움직인다(GitKraken 의 ↑↓ 와 같다). 간 줄은 화면 위쪽(<see cref="JumpMargin"/> 줄 아래)에 둔다.
        /// </summary>
        private void JumpToChange(int direction)
        {
            List<int> starts = ChangeStartRows();
            if (starts.Count == 0)
            {
                return;
            }
            int focus = FocusRow();
            int target = -1;
            foreach (int row in starts)
            {
                if (direction > 0 && row > focus)
                {
                    target = row;
                    break;
                }
                if (direction < 0 && row < focus)
                {
                    target = row;
                }
            }
            if (target < 0)
            {
                return;
            }
            _lastJumpRow = target;
            ScrollView scroll = _list.Q<ScrollView>();
            if (_wrap || scroll == null)
            {
                // 접은 줄은 높이가 달라 위치를 셀 수 없다. 목록이 알아서 보이게 한다.
                _list.ScrollToItem(target);
                return;
            }
            scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Max(0, (target - JumpMargin) * RowHeight));
        }

        /// <summary>
        /// 이동의 기준 줄: 화면 맨 위에서 <see cref="JumpMargin"/> 줄 아래. 방금 ↑↓ 로 간 줄이 아직 화면 안이면 그 줄(파일 끝 근처에서는 더 내려가지 못해
        /// 화면 위치로 셈하면 같은 변경에 머문다). 맨 위에 있고 아직 움직인 적 없으면 -1(첫 변경부터).
        /// </summary>
        private int FocusRow()
        {
            ScrollView scroll = _list.Q<ScrollView>();
            if (_wrap || scroll == null)
            {
                return _lastJumpRow;
            }
            float offset = scroll.scrollOffset.y;
            int firstVisible = Mathf.FloorToInt(offset / RowHeight);
            int visibleRows = Mathf.Max(1, Mathf.FloorToInt(scroll.contentViewport.layout.height / RowHeight));
            if (_lastJumpRow >= firstVisible && _lastJumpRow < firstVisible + visibleRows)
            {
                return _lastJumpRow;
            }
            return offset < RowHeight * 0.5f ? -1 : firstVisible + JumpMargin;
        }

        /// <summary>
        /// 바뀐 덩어리마다 첫 줄의 목록 인덱스.
        /// </summary>
        private List<int> ChangeStartRows()
        {
            List<int> starts = new();
            bool previous = false;
            for (int index = 0; index < _rows.Count; index++)
            {
                bool isChange = GitPartialPatch.IsChange(_rows[index].Left) || GitPartialPatch.IsChange(_rows[index].Right);
                if (isChange && previous == false)
                {
                    starts.Add(index);
                }
                previous = isChange;
            }
            return starts;
        }

        // ── 통합 보기 ─────────────────────────────

        private VisualElement MakeUnifiedRow()
        {
            VisualElement row = new();
            row.AddToClassList("diff-row");
            row.Add(GutterElement());
            row.Add(NumberLabel());
            row.Add(NumberLabel());
            Label sign = new();
            sign.AddToClassList("diff-row__sign");
            row.Add(sign);
            row.Add(CodeElement());
            row.Add(HunkButton());
            RegisterSelection(row);
            return row;
        }

        private void BindUnifiedRow(VisualElement row, int index)
        {
            GitDiffLine line = _rows[index].Left;
            SetKindClasses(row, "diff-row", line);
            row.EnableInClassList("diff-row--selected", BindStageControls(row, row[0], (Button)row[5], line));
            ApplyNumberColumn(row[1], _showOldNumbers);
            ApplyNumberColumn(row[2], _showNewNumbers);
            ((Label)row[1]).text = line.OldNumber > 0 ? line.OldNumber.ToString() : string.Empty;
            ((Label)row[2]).text = line.NewNumber > 0 ? line.NewNumber.ToString() : string.Empty;
            ((Label)row[3]).text = line.Kind switch
            {
                GitDiffLineKind.Added => "+",
                GitDiffLineKind.Removed => "−",
                _ => string.Empty,
            };
            BindCode(row[4], line);
        }

        // ── 나란히 보기 ───────────────────────────

        private VisualElement MakeSplitRow()
        {
            VisualElement row = new();
            row.AddToClassList("diff-row");
            row.Add(SideElement("diff-side--left"));
            row.Add(SideElement("diff-side--right"));
            row.Add(HunkButton());
            return row;
        }

        private VisualElement SideElement(string modifier)
        {
            VisualElement side = new();
            side.AddToClassList("diff-side");
            side.AddToClassList(modifier);
            side.Add(GutterElement());
            side.Add(NumberLabel());
            Label sign = new();
            sign.AddToClassList("diff-row__sign");
            side.Add(sign);
            side.Add(CodeElement());
            RegisterSelection(side);
            return side;
        }

        private void BindSplitRow(VisualElement row, int index)
        {
            GitDiffRow diffRow = _rows[index];
            // 머리 줄은 양쪽에 같은 줄이 들어가므로 hunk 버튼은 줄 하나에 하나만(오른쪽 끝) 둔다. 고른 표시는 줄 전체에 한다 — 한쪽을 골라도 짝이 같이 올라간다.
            bool leftSelected = BindSide(row[0], diffRow.Left, isLeft: true, hunk: (Button)row[2]);
            bool rightSelected = BindSide(row[1], diffRow.Right, isLeft: false, hunk: null);
            row.EnableInClassList("diff-row--selected", leftSelected || rightSelected);
        }

        private bool BindSide(VisualElement side, GitDiffLine line, bool isLeft, Button hunk)
        {
            side.EnableInClassList("diff-side--empty", line == null);
            SetKindClasses(side, "diff-side", line);
            bool selected = BindStageControls(side, side[0], hunk, line);
            Label number = (Label)side[1];
            Label sign = (Label)side[2];
            ApplyNumberColumn(number, true);
            if (line == null)
            {
                number.text = string.Empty;
                sign.text = string.Empty;
                BindCode(side[3], null);
                return false;
            }
            int lineNumber = isLeft ? line.OldNumber : line.NewNumber;
            number.text = lineNumber > 0 ? lineNumber.ToString() : string.Empty;
            sign.text = line.Kind switch
            {
                GitDiffLineKind.Added => "+",
                GitDiffLineKind.Removed => "−",
                _ => string.Empty,
            };
            BindCode(side[3], line);
            return selected;
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

        private void BindCode(VisualElement code, GitDiffLine line)
        {
            Label before = (Label)code[0];
            Label change = (Label)code[1];
            Label after = (Label)code[2];
            // 공백 보이기는 흐린 · → 를 리치 텍스트로 칠한다. 코드의 < > 는 Render 가 <noparse> 로 감싸 태그로 읽히지 않게 한다.
            before.enableRichText = _showWhitespace;
            change.enableRichText = _showWhitespace;
            after.enableRichText = _showWhitespace;
            if (line == null)
            {
                before.text = string.Empty;
                change.text = string.Empty;
                after.text = string.Empty;
                return;
            }
            string raw = line.Text ?? string.Empty;
            // 접어 보기는 한 조각이어야 줄 끝에서 접힌다(세 조각이면 조각마다 따로 접혀 어긋난다). 그래서 줄 안 강조는 접지 않을 때만.
            if (line.ChangeLength <= 0 || _wrap)
            {
                before.text = Render(raw);
                change.text = string.Empty;
                after.text = string.Empty;
                return;
            }
            // 탭을 펼치기 전 위치로 자른 뒤 각 조각을 펼친다(펼친 뒤에는 위치가 달라진다).
            int start = Math.Min(line.ChangeStart, raw.Length);
            int length = Math.Min(line.ChangeLength, raw.Length - start);
            before.text = Render(raw.Substring(0, start));
            change.text = Render(raw.Substring(start, length));
            after.text = Render(raw.Substring(start + length));
        }

        /// <summary>
        /// 화면에 쓸 코드 글. 탭은 4칸으로 펼친다. 공백 보이기면 공백은 흐린 ·, 탭은 흐린 → 와 남은 칸으로 바꾸고 나머지는 <c>&lt;noparse&gt;</c> 로 감싼다.
        /// </summary>
        private string Render(string text)
        {
            if (_showWhitespace == false)
            {
                return ExpandTabs(text);
            }
            StringBuilder builder = new(text.Length * 2);
            int runStart = 0;
            for (int i = 0; i <= text.Length; i++)
            {
                bool isWhitespace = i < text.Length && (text[i] == ' ' || text[i] == '\t');
                if (i < text.Length && isWhitespace == false)
                {
                    continue;
                }
                if (i > runStart)
                {
                    // 코드에 </noparse> 가 그대로 있으면 감싼 구간이 거기서 끝난다. 사이에 폭 없는 공백을 넣어 끊는다.
                    builder.Append("<noparse>").Append(text, runStart, i - runStart).Replace("</noparse>", "</no​parse>", builder.Length - (i - runStart), i - runStart).Append("</noparse>");
                }
                if (i < text.Length)
                {
                    builder.Append(WhitespaceOpen).Append(text[i] == ' ' ? "·" : "→   ").Append(WhitespaceClose);
                }
                runStart = i + 1;
            }
            return builder.ToString();
        }

        /// <summary>
        /// 공백 표시의 흐림. 글자색은 테마를 따르고 투명도만 낮춘다.
        /// </summary>
        private const string WhitespaceOpen = "<alpha=#50>";
        private const string WhitespaceClose = "<alpha=#FF>";

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
