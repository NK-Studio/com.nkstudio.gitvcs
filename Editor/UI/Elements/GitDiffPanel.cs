using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
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
        /// Compact: 가운데 맞춤을 풀고 "이 파일 편집" 을 아이콘만. Narrow: Blame·히스토리 숨김(파일 목록 메뉴에도 있다). Tiny: 공백·줄 바꿈 숨김. Micro: 보기 방식 셋 숨김.
        /// </summary>
        private const float CompactBarWidth = 820f;
        private const float NarrowBarWidth = 690f;
        private const float TinyBarWidth = 560f;
        private const float MicroBarWidth = 470f;

        /// <summary>
        /// 지금 읽어 둔 diff 의 문맥 줄 수. 보기 방식을 바꿨을 때 다시 읽어야 하는지(변경 묶음 ↔ 파일 전체) 가른다.
        /// </summary>
        private int _loadedContextLines = -1;

        /// <summary>
        /// 지금 목록이 긴 줄 접기(가변 높이) 모양인지. 바뀔 때만 줄 요소를 새로 만든다.
        /// </summary>
        private bool? _shownWrap;

        /// <summary>
        /// 지금 목록이 파일 보기인지. 바뀌면 줄 목록이 달라지므로 고른 글을 비운다.
        /// </summary>
        private bool? _shownFileView;
        private bool _wrap;

        /// <summary>
        /// 줄 높이가 줄마다 다른지(긴 줄 접기, 또는 변경 묶음 보기의 높은 머리 줄). 그러면 목록은 가변 높이이고 ↑↓ 이동은 줄 높이로 위치를 셈하지 않는다.
        /// </summary>
        private bool _variableHeights;
        private bool? _shownVariableHeights;
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

        // ── 코드 글 고르기·복사(편집기·GitKraken 처럼 끌어 고르고 Ctrl+C) ──

        /// <summary>
        /// 고른 글의 시작점(닻)과 끝점. 줄은 목록(<see cref="_rows"/>) 인덱스, 위치는 원래 줄 글(<see cref="GitDiffLine.Text"/>, 탭 그대로)의 글자 위치.
        /// 줄 요소는 스크롤하면 다른 줄에 다시 쓰이므로 고른 범위는 요소가 아니라 여기에 둔다. 닻 줄이 -1 이면 고른 글이 없다.
        /// </summary>
        private int _textAnchorRow = -1;
        private int _textAnchorOffset;
        private int _textFocusRow = -1;
        private int _textFocusOffset;

        /// <summary>
        /// 나란히 보기에서 글을 고르는 쪽(true = 오른쪽·새 판). 한 번에 한쪽만 고른다. 통합·파일 보기는 양쪽이 같은 줄이라 늘 false.
        /// </summary>
        private bool _textRight;
        private bool _textDragging;

        /// <summary>
        /// 글을 끄는 동안 끌기·떼기를 받는 패널 맨 위 요소. 포인터를 잡으면(CapturePointer) 줄의 :hover 가 풀린 뒤
        /// 떼어도 다른 요소로 옮겨 가기 전까지 돌아오지 않으므로, 잡지 않고 맨 위에서 먼저(TrickleDown) 받는다.
        /// </summary>
        private VisualElement _textDragRoot;

        /// <summary>
        /// 고른 글 배경. 테마 토큰(<c>--git-diff-text-selection</c>)을 GitDiff.uss 가 <c>--diff-text-selection</c> 으로 넘긴다. 리치 텍스트 &lt;mark&gt; 로 칠한다.
        /// </summary>
        private Color _textSelectionColor = new(0.22f, 0.47f, 0.82f, 0.45f);
        private static readonly CustomStyleProperty<Color> TextSelectionProperty = new("--diff-text-selection");

        /// <summary>
        /// 화면에서 탭 하나가 차지하는 글자 수(<see cref="ExpandTabs"/>·공백 보이기의 "→   " 와 같다).
        /// </summary>
        private const int TabDisplayWidth = 4;

        // ── 이 파일 편집(GitKraken 의 editable File View) ──

        /// <summary>
        /// 작업 사본 파일을 이 자리에서 고치는 편집기. 처음 편집할 때 만든다. 편집 중에는 줄 목록 대신 보인다.
        /// </summary>
        private GitCodeEditor _editor;
        private bool _isEditing;

        /// <summary>
        /// 편집 중인 파일의 전체 경로, 마지막으로 읽거나 쓴 바이트(저장할 때 다른 곳에서 바뀌었는지 비교), 인코딩·줄 끝.
        /// </summary>
        private string _editPath;
        private string _editRepoPath;
        private byte[] _editBytes;
        private GitTextFile _editFile;

        private VisualElement _editingGroup;
        private Label _editingLabel;
        private Button _saveButton;

        /// <summary>
        /// 닫기(×)를 눌렀을 때. <see cref="ShowCloseButton"/> 로 버튼을 켠 곳(GitKraken 레이아웃의 가운데)만 쓴다.
        /// </summary>
        public event Action CloseRequested;

        /// <summary>
        /// 지금 이 자리에서 파일을 고치는 중인지.
        /// </summary>
        public bool IsEditing => _isEditing;

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
        public void EnablePartialStaging(IGitOperationRunner runner, IGitCommandService commands, IDialogService dialogs)
        {
            _runner = runner;
            _commands = commands;
            _dialogs = dialogs;
            UpdateStageFileButton();
            _list.RefreshItems();
        }

        private IDialogService _dialogs;

        /// <summary>
        /// "Discard Hunk" 를 보일지: 작업 사본의 변경(Unstaged)이고 git 이 이미 아는 파일일 때만. 새 파일(untracked)은 버릴 원본이 없다.
        /// </summary>
        private bool CanDiscard => CanPartialStage && _target.Source == GitDiffSource.Unstaged && _target.State != GitFileState.Untracked && _dialogs != null;

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
            // 휠 한 칸에 세 줄(코드 편집기와 같다). 기본값은 한 줄 남짓이라 큰 파일에서 끝없이 굴려야 했다.
            _list.Q<ScrollView>().mouseWheelScrollSize = RowHeight * 3f;
            // 줄 끌어 고르기는 줄 요소의 PointerDown·PointerEnter 로 하고, 버튼을 떼거나 목록 밖으로 나가면 끝낸다.
            _list.RegisterCallback<PointerUpEvent>(_ => _dragSelecting = false, TrickleDown.TrickleDown);
            _list.RegisterCallback<PointerLeaveEvent>(_ => _dragSelecting = false);
            // 코드 글 고르기: 누르기는 코드 칸(CodeElement)이 받고, 끌기·떼기는 끄는 동안 패널 맨 위가 받는다(BeginTextDrag).
            _list.focusable = true;
            _list.RegisterCallback<DetachFromPanelEvent>(_ => EndTextDrag());
            _list.RegisterCallback<KeyDownEvent>(OnTextKeyDown, TrickleDown.TrickleDown);
            _list.RegisterCallback<ValidateCommandEvent>(OnValidateTextCommand);
            _list.RegisterCallback<ExecuteCommandEvent>(OnExecuteTextCommand);
            _list.AddManipulator(new ContextualMenuManipulator(OnTextContextMenu));
            Root.Add(_list);
            Root.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);

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
            // 계정 문제로 못 받았을 때만 보인다. 이 창은 Git 창과 따로 뜰 수 있어 Git 창을 열고 그 위에 계정 고르기를 띄운다.
            _lfsAccountButton = new Button(GitWindow.ChooseAccount) { text = GitText.Get("lfs.chooseAccount") };
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
            if (_isEditing)
            {
                // 같은 작업 사본 파일을 다시 고른 것(새로고침 뒤 목록이 다시 고른 것 등)이면 편집을 그대로 둔다.
                if (target != null && IsEditableSource(target) && target.RepoPath == _editRepoPath)
                {
                    _target = target;
                    Reload();
                    return;
                }
                // 다른 파일로 가려면 저장 안 된 편집을 먼저 묻는다(Git 창 위 팝업). 저장·버리기면 그 뒤에 이 파일로 가고, 취소하면 지금 파일에 머문다.
                LeaveEdit(() => SetTarget(target));
                return;
            }
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
            if (_isEditing)
            {
                ApplyEditVisibility();
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
            Image icon = new() { image = GitTheme.SkinIcon(UnityEditorInternal.InternalEditorUtility.GetIconForFile(repoPath ?? string.Empty)), scaleMode = ScaleMode.ScaleToFit };
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
            ApplyEditVisibility();
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

            // 알고리즘·공백·문맥 줄 수는 설정(Preferences > Git)에서만 바꾼다. 바뀌면 OnDiffOptionsChanged 가 다시 읽는다.
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
            _reloadButton.AddToClassList("git-diff__tool-button--icon");
            ToolbarIcons.SetRefreshIcon(_reloadButton);
            toolbar.Add(_reloadButton);

            // GitKraken 처럼 × 바로 옆에 이 파일 전체 Stage·Unstage. 부분 스테이징을 켠 패널의 Unstaged·Staged diff 에서만 보인다.
            _stageFileButton = new ToolbarButton(StageWholeFileAsync);
            _stageFileButton.AddToClassList("git-diff__stage-file");
            _stageFileButton.SetVisible(false);
            toolbar.Add(_stageFileButton);

            _closeButton = new ToolbarButton(() =>
            {
                // 저장 안 된 편집이 있으면 먼저 묻는다(Git 창 위 팝업). 취소하면 닫지 않는다.
                LeaveEdit(() => CloseRequested?.Invoke());
            }) { text = "×", tooltip = GitText.Get("common.close") };
            _closeButton.AddToClassList("git-diff__close");
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
            _editButton = BarButton(BeginEdit, GitText.Get("diff.editFileTooltip"), "edit", GitText.Get("diff.editFile"));
            _editButton.AddToClassList("git-diff__bar-button--outlined");
            // 오른쪽 클릭하면 예전처럼 외부 편집기에서 연다.
            _editButton.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                evt.menu.AppendAction(GitText.Get("diff.openInExternalEditor"), _ => OpenInEditor(),
                    _target != null && System.IO.File.Exists(FullPathOf(_target)) ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
                evt.StopPropagation();
            }));
            left.Add(_editButton);

            // 편집 중에는 "이 파일 편집" 자리에 GitKraken 처럼 "편집 중" 표시와 [저장][취소][외부 편집기].
            _editingGroup = new VisualElement();
            _editingGroup.AddToClassList("git-diff__editing");
            VisualElement pencil = new() { pickingMode = PickingMode.Ignore };
            pencil.AddToClassList("git-diff__bar-icon");
            pencil.AddToClassList("git-diff__bar-icon--edit");
            _editingGroup.Add(pencil);
            _editingLabel = new Label(GitText.Get("diff.editing")) { pickingMode = PickingMode.Ignore };
            _editingLabel.AddToClassList("git-diff__editing-label");
            _editingGroup.Add(_editingLabel);
            _saveButton = BarButton(() => _ = SaveEditAsync(), GitText.Get("diff.editSaveTooltip"), null, GitText.Get("diff.editSave"));
            _saveButton.AddToClassList("git-diff__bar-button--outlined");
            _saveButton.AddToClassList("git-diff__edit-save");
            _editingGroup.Add(_saveButton);
            Button cancel = BarButton(CancelEdit, GitText.Get("diff.editCancelTooltip"), null, GitText.Get("common.cancel"));
            cancel.AddToClassList("git-diff__bar-button--outlined");
            cancel.AddToClassList("git-diff__edit-action");
            _editingGroup.Add(cancel);
            Button external = BarButton(OpenInEditor, GitText.Get("diff.openInExternalEditorTooltip"), null, GitText.Get("diff.openInExternalEditorShort"));
            external.AddToClassList("git-diff__bar-button--outlined");
            external.AddToClassList("git-diff__edit-action");
            external.AddToClassList("git-diff__edit-external");
            _editingGroup.Add(external);
            _editingGroup.SetVisible(false);
            left.Add(_editingGroup);
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
            layouts.AddToClassList("git-diff__segment--layouts");
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
                _bar.EnableInClassList("git-diff__bar--micro", width < MicroBarWidth);
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
            UpdateStageFileButton();
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
                // 편집 중에는 diff 에만 뜻이 있는 묶음(변경 이동·보기 방식·공백·줄 바꿈)을 숨기고 파일/Diff 보기만 둔다.
                control.SetVisible(isText && (_isEditing == false || control == _fileViewButton.parent));
            }
            _editButton.SetVisible(_isEditing == false);
            _editingGroup.SetVisible(_isEditing);
            if (_isEditing)
            {
                // GitKraken 처럼 편집은 파일 보기의 한 모습이다(설정의 보기는 바꾸지 않는다).
                SetChecked(_fileViewButton, true);
                SetChecked(_diffViewButton, false);
                UpdateEditingState();
                UpdateSideChip();
                return;
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
            // 작업 사본의 글 diff 면 이 자리에서 고치고, 커밋·Staged 등이면 예전처럼 외부 편집기에서 연다.
            _editButton.tooltip = GitText.Get(exists == false ? "diff.editFileMissing" : CanEditInPlace ? "diff.editFileTooltip" : "diff.editFileNotWorkingCopy");

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
            int line = _isEditing ? _editor.Caret.Line + 1 : LineToOpen();
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

        // ── 이 파일 편집(이 자리에서 고치기) ─────────────

        /// <summary>
        /// 편집 확인 팝업(<see cref="GitConfirmDialog"/>)을 덮어 띄울 곳(Git 창). null 이면 <see cref="GitPopupHosts"/> 가 열린 창을 고른다(따로 뜬 diff 창 등).
        /// </summary>
        public Func<VisualElement> PopupHostProvider { get; set; }

        private VisualElement PopupHost => PopupHostProvider?.Invoke();

        /// <summary>
        /// 편집을 끝낼지 묻는 팝업이 떠 있는 동안 true. 같은 질문이 겹쳐 뜨지 않게 한다.
        /// </summary>
        private bool _isPrompting;

        /// <summary>
        /// 저장 중(디스크가 바뀌었다는 팝업을 기다리는 중 포함)이면 true. Ctrl+S 를 거듭 눌러도 한 번만 쓴다.
        /// </summary>
        private bool _isSaving;

        /// <summary>
        /// 작업 사본의 변경을 보는 diff 인지(Unstaged·작업 트리). 이때만 보이는 내용이 디스크의 파일과 같아 이 자리에서 고칠 수 있다.
        /// </summary>
        private static bool IsEditableSource(GitDiffTarget target)
        {
            return target.Source == GitDiffSource.Unstaged || target.Source == GitDiffSource.WorkingTree;
        }

        /// <summary>
        /// "이 파일 편집" 이 이 자리에서 고치는지(아니면 외부 편집기에서 연다). 작업 사본의 글 파일이어야 한다.
        /// </summary>
        private bool CanEditInPlace => _target != null && IsEditableSource(_target) && _isImageMode == false
            && (_document == null || _document.IsBinary == false) && System.IO.File.Exists(FullPathOf(_target));

        /// <summary>
        /// "이 파일 편집": GitKraken 처럼 이 자리를 편집할 수 있는 파일 보기로 바꾼다. 작업 사본의 글 파일이 아니면 외부 편집기에서 연다.
        /// UTF-8·UTF-16 으로 풀리지 않는 파일은 고치면 깨지므로 외부 편집기를 권한다.
        /// </summary>
        private async void BeginEdit()
        {
            if (_isEditing)
            {
                _editor?.FocusEditor();
                return;
            }
            if (CanEditInPlace == false)
            {
                OpenInEditor();
                return;
            }
            GitDiffTarget target = _target;
            string path = FullPathOf(target);
            byte[] bytes;
            try
            {
                bytes = System.IO.File.ReadAllBytes(path);
            }
            catch (Exception exception)
            {
                await GitConfirmDialog.AlertAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editUnsupportedTitle"), exception.Message);
                return;
            }
            GitTextFile file = GitTextFile.TryDecode(bytes);
            if (file == null)
            {
                if (await GitConfirmDialog.ConfirmAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editUnsupportedTitle"),
                        GitText.Format("diff.editUnsupportedMessage", GitDisplay.Nfc(target.RepoPath)), GitText.Get("diff.openInExternalEditor")))
                {
                    OpenInEditor();
                }
                return;
            }
            if (_editor == null)
            {
                _editor = new GitCodeEditor();
                _editor.SaveRequested += () => _ = SaveEditAsync();
                _editor.CancelRequested += CancelEdit;
                _editor.Changed += UpdateEditingState;
                _editor.ExtraMenuItems = menu => menu.AppendAction(GitText.Get("diff.openInExternalEditor"), _ => OpenInEditor());
                _editor.SetVisible(false);
                // 줄 목록 바로 뒤(이미지·바이너리 보기 앞)에 넣어 같은 자리를 차지한다.
                Root.Insert(Root.IndexOf(_list) + 1, _editor);
            }
            int line = LineToOpen();
            _editor.Load(file.Text, GitSyntax.LanguageOf(_target.RepoPath), line);
            _editPath = path;
            _editRepoPath = _target.RepoPath;
            _editBytes = bytes;
            _editFile = file;
            _isEditing = true;
            ClearSelection();
            ClearTextSelection();
            _editor.SetChangedLines(ChangedNewLines());
            ApplyEditVisibility();
            UpdateBar();
            _editor.schedule.Execute(_editor.FocusEditor);
        }

        /// <summary>
        /// 편집을 끝내고 <paramref name="proceed"/> 를 한다. 저장 안 된 편집이 없으면 바로 끝내고 바로 부른다.
        /// 있으면 Git 창 위 팝업으로 [저장][버리기][취소] 를 묻고, 저장(성공)·버리기면 편집을 끝낸 뒤 부른다. 취소하면 부르지 않고 편집을 이어 간다.
        /// </summary>
        /// <returns>편집 중이 아니었거나 묻지 않고 끝냈으면 true(<paramref name="proceed"/> 를 이미 불렀다). 물으러 갔으면 false.</returns>
        public bool LeaveEdit(Action proceed)
        {
            if (_isEditing == false || _editor.IsDirty == false)
            {
                if (_isEditing)
                {
                    EndEdit();
                }
                proceed?.Invoke();
                return true;
            }
            AskToLeaveEdit(proceed);
            return false;
        }

        private async void AskToLeaveEdit(Action proceed)
        {
            if (_isPrompting)
            {
                return;
            }
            _isPrompting = true;
            int choice;
            try
            {
                choice = await GitConfirmDialog.ChooseAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editUnsavedTitle"),
                    GitText.Format("diff.editUnsavedMessage", GitDisplay.Nfc(_editRepoPath)), GitText.Get("diff.editSave"), GitText.Get("diff.editDiscard"), altDanger: true);
                // 0 = 저장, 2 = 버리기, 1 = 취소. 저장하지 못했으면(취소·실패) 편집을 이어 간다.
                if (choice == 0 && await SaveEditAsync() == false)
                {
                    choice = 1;
                }
            }
            finally
            {
                _isPrompting = false;
            }
            if (_isEditing == false)
            {
                // 기다리는 동안 다른 길로 편집이 끝났다(다시 읽기 등). 할 일만 한다.
                proceed?.Invoke();
                return;
            }
            if (choice == 1)
            {
                _editor.FocusEditor();
                return;
            }
            EndEdit();
            proceed?.Invoke();
        }

        /// <summary>
        /// Esc·취소: 저장 안 된 편집이 있으면 Git 창 위 팝업으로 버릴지 묻고 diff 로 돌아간다.
        /// </summary>
        private async void CancelEdit()
        {
            if (_isEditing == false || _isPrompting)
            {
                return;
            }
            if (_editor.IsDirty)
            {
                _isPrompting = true;
                bool discard;
                try
                {
                    discard = await GitConfirmDialog.ConfirmAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editUnsavedTitle"),
                        GitText.Format("diff.editDiscardMessage", GitDisplay.Nfc(_editRepoPath)), GitText.Get("diff.editDiscard"), okDanger: true);
                }
                finally
                {
                    _isPrompting = false;
                }
                if (_isEditing == false)
                {
                    return;
                }
                if (discard == false)
                {
                    _editor.FocusEditor();
                    return;
                }
            }
            EndEdit();
        }

        private void EndEdit()
        {
            _isEditing = false;
            _editPath = null;
            _editRepoPath = null;
            _editBytes = null;
            _editFile = null;
            ApplyEditVisibility();
            // 줄 목록을 되돌린다(안내 문구·바이너리 카드는 이어서 다시 읽으며 맞춘다).
            _list.SetVisible(_isImageMode == false && (_document == null || _document.IsBinary == false));
            UpdateBar();
            // 편집하는 동안 바뀐 파일(저장)을 diff 에 보인다.
            Reload();
            _list.Focus();
        }

        /// <summary>
        /// 편집기를 파일에 쓴다. 편집을 시작한(마지막으로 저장한) 뒤 다른 곳에서 파일이 바뀌었으면 덮어쓰기 전에 묻는다.
        /// 원래 인코딩·BOM·줄 끝으로 쓰고(<see cref="GitTextFile.Encode"/>), Unity 에셋이면 다시 가져오고, git 상태와 diff 를 새로 읽는다.
        /// </summary>
        /// <returns>썼으면 true.</returns>
        private async Task<bool> SaveEditAsync()
        {
            if (_isEditing == false || _isSaving)
            {
                return false;
            }
            _isSaving = true;
            try
            {
                return await WriteEditAsync();
            }
            finally
            {
                _isSaving = false;
            }
        }

        private async Task<bool> WriteEditAsync()
        {
            byte[] onDisk = null;
            try
            {
                if (System.IO.File.Exists(_editPath))
                {
                    onDisk = System.IO.File.ReadAllBytes(_editPath);
                }
            }
            catch (Exception)
            {
                // 읽지 못하면 바뀐 것으로 보고 묻는다.
            }
            if (onDisk == null || onDisk.AsSpan().SequenceEqual(_editBytes) == false)
            {
                int choice = await GitConfirmDialog.ChooseAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editChangedOnDiskTitle"),
                    GitText.Format("diff.editChangedOnDiskMessage", GitDisplay.Nfc(_editRepoPath)), GitText.Get("diff.editOverwrite"), GitText.Get("diff.editReloadFromDisk"), okDanger: true);
                if (_isEditing == false)
                {
                    return false;
                }
                if (choice == 1)
                {
                    _editor.FocusEditor();
                    return false;
                }
                if (choice == 2)
                {
                    ReloadEditFromDisk(onDisk);
                    return false;
                }
            }
            byte[] bytes = _editFile.Encode(_editor.Text);
            try
            {
                System.IO.File.WriteAllBytes(_editPath, bytes);
            }
            catch (Exception exception)
            {
                await GitConfirmDialog.AlertAsync(PopupHost, ActionGlyphKind.Pencil, GitText.Get("diff.editSaveFailedTitle"),
                    GitText.Format("diff.editSaveFailedMessage", GitDisplay.Nfc(_editRepoPath), exception.Message));
                return false;
            }
            // 다음 저장은 방금 쓴 판과 비교하고, 줄 끝도 방금 쓴 판을 기준으로 맞춘다.
            _editBytes = bytes;
            _editFile = GitTextFile.TryDecode(bytes) ?? _editFile;
            _editor.MarkSaved();
            ImportIfAsset(_editRepoPath);
            GitRepository.RequestRefresh(immediate: true, workingTreeOnly: true);
            Reload();
            return true;
        }

        /// <summary>
        /// 디스크의 지금 파일로 편집기를 다시 채운다(편집은 버린다). 읽을 수 없거나 글 파일이 아니게 되었으면 편집을 끝낸다.
        /// </summary>
        private void ReloadEditFromDisk(byte[] bytes)
        {
            GitTextFile file = bytes == null ? null : GitTextFile.TryDecode(bytes);
            if (file == null)
            {
                EndEdit();
                return;
            }
            _editBytes = bytes;
            _editFile = file;
            _editor.Load(file.Text, GitSyntax.LanguageOf(_editRepoPath), _editor.Caret.Line + 1);
            UpdateEditingState();
            Reload();
        }

        /// <summary>
        /// 저장한 파일이 Unity 프로젝트 안의 에셋(또는 .meta)이면 다시 가져와 Unity 가 바뀐 내용을 보게 한다.
        /// </summary>
        private static void ImportIfAsset(string repoPath)
        {
            string projectPath = GitStatusParser.ToProjectPath(repoPath, GitRepository.ProjectPrefix, GitRepository.RepoInProject);
            string assetPath = GitDisplay.AssetPathOf(projectPath);
            if (string.IsNullOrEmpty(assetPath))
            {
                return;
            }
            bool isAsset = assetPath.StartsWith("Assets/", StringComparison.Ordinal) || assetPath.StartsWith("Packages/", StringComparison.Ordinal);
            if (isAsset && string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(assetPath)) == false)
            {
                AssetDatabase.ImportAsset(assetPath);
            }
        }

        /// <summary>
        /// 편집 중이면 줄 목록·안내·바이너리 카드 대신 편집기를 보이고, 아니면 편집기를 숨긴다(줄 목록은 다른 곳이 정한다).
        /// </summary>
        private void ApplyEditVisibility()
        {
            Root.EnableInClassList("git-diff--editing", _isEditing);
            if (_editor == null)
            {
                return;
            }
            _editor.SetVisible(_isEditing);
            if (_isEditing)
            {
                _list.SetVisible(false);
                _notice.SetVisible(false);
                _binaryView.SetVisible(false);
                _loading.SetVisible(false);
            }
        }

        /// <summary>
        /// 막대의 "편집 중" 표시와 저장 버튼을 저장 안 된 편집이 있는지에 맞춘다.
        /// </summary>
        private void UpdateEditingState()
        {
            if (_editor == null || _editingLabel == null)
            {
                return;
            }
            bool dirty = _isEditing && _editor.IsDirty;
            _editingLabel.text = GitText.Get(dirty ? "diff.editingDirty" : "diff.editing");
            _editingLabel.EnableInClassList("git-diff__editing-label--dirty", dirty);
            _saveButton.SetEnabled(dirty);
        }

        /// <summary>
        /// 지금 diff 에서 작업 사본에 더해진 줄 번호(새 판, 1부터). 편집기의 줄 번호 칸에 표시한다.
        /// </summary>
        private IEnumerable<int> ChangedNewLines()
        {
            if (_document == null)
            {
                return Array.Empty<int>();
            }
            return _document.Lines.Where(line => line.Kind == GitDiffLineKind.Added && line.NewNumber > 0).Select(line => line.NewNumber).ToList();
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

        /// <summary>
        /// diff 막대의 "히스토리" 를 창 안에 보일 곳(GitKraken 레이아웃). true 를 돌려주면 따로 창을 띄우지 않는다. null 이면 늘 창으로.
        /// </summary>
        public Func<string, bool> FileHistoryPresenter { get; set; }

        private void OpenHistory()
        {
            if (_target == null)
            {
                return;
            }
            if (FileHistoryPresenter != null && FileHistoryPresenter(_target.RepoPath))
            {
                return;
            }
            GitFileHistoryWindow.Open(_target.RepoPath, _service);
        }

        private void SetFileView(bool fileView)
        {
            if (_isEditing)
            {
                // 편집이 곧 파일 보기다. Diff 보기로 가면 편집을 끝낸다(저장 안 된 편집은 먼저 묻고, 저장·버리기 뒤에 Diff 보기로 간다).
                if (fileView == false)
                {
                    LeaveEdit(() => SetFileView(false));
                }
                return;
            }
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
            // 변경 묶음(Hunk) 보기의 머리 줄은 [Discard Hunk][Stage Hunk] 가 다른 버튼과 같은 크기로 들어가게 높다(GitKraken 과 같다).
            bool tallHunks = fileView == false && settings.DiffLayout == DiffLayout.Unified;
            Root.EnableInClassList("git-diff--tall-hunks", tallHunks);
            _variableHeights = _wrap || tallHunks;
            UpdateBar();

            // 보기(통합·나란히·파일)가 바뀌면 줄 목록이 달라져 고른 글의 줄 인덱스가 뜻을 잃는다.
            if (_shownSplit != isSplit || _shownFileView != fileView)
            {
                ResetTextSelection();
            }
            _shownFileView = fileView;

            // 줄 모양(통합·나란히, 고정·가변 높이)이 바뀔 때만 줄 요소를 새로 만든다. 같은 모양이면 보이는 줄만 다시 채워(RefreshItems)
            // 다른 파일을 고를 때마다 목록 전체가 지워졌다 다시 그려지며 깜빡이지 않게 한다.
            bool rebuild = _shownSplit != isSplit || _shownWrap != _wrap || _shownVariableHeights != _variableHeights;
            bool virtualizationChanged = _shownVariableHeights != _variableHeights;
            if (rebuild)
            {
                // makeItem·bindItem 은 바꾸는 즉시 목록을 다시 채운다. 하나씩 바꾸면 그 사이에 옛 모양 줄을 새 bind 로 채우다 깨지므로
                // 먼저 목록을 비워 채울 줄을 없앤다.
                _list.itemsSource = new List<GitDiffRow>();
                // 접은 줄·높은 머리 줄은 높이가 줄마다 달라 가변 높이로 둔다(고정 높이가 훨씬 빨라 파일 전체를 읽는 보기는 고정).
                _list.virtualizationMethod = _variableHeights ? CollectionVirtualizationMethod.DynamicHeight : CollectionVirtualizationMethod.FixedHeight;
                _list.makeItem = isSplit ? MakeSplitRow : MakeUnifiedRow;
                _list.bindItem = isSplit ? BindSplitRow : BindUnifiedRow;
                _shownSplit = isSplit;
                _shownWrap = _wrap;
                _shownVariableHeights = _variableHeights;
            }
            UpdateGutter(isSplit, fileView);
            // 파일 전체를 읽은 보기(통합·나란히)는 GitKraken 처럼 "@@ -1,161 +1,161 @@" 머리 줄 없이 1번 줄부터 보인다(파일 전체가 묶음 하나라 뜻이 없다).
            IReadOnlyList<GitDiffLine> lines = _document == null ? null
                : _loadedContextLines >= GitDiffOptions.MaxContextLines ? _document.Lines.Where(line => line.Kind != GitDiffLineKind.Hunk).ToList()
                : _document.Lines;
            _rows = lines == null ? new List<GitDiffRow>()
                : fileView ? FileViewRows(lines)
                : isSplit ? GitDiff.ToSideBySide(lines)
                : lines.Select(line => new GitDiffRow(line, line)).ToList();
            _list.itemsSource = _rows;
            if (rebuild)
            {
                _list.Rebuild();
                if (virtualizationChanged)
                {
                    // 가변 → 고정 높이로 바꾼 틀에는 고정 높이 컨트롤러가 내용 높이를 0 으로 둔 채 남아 줄이 모두 잘려 보인다
                    // (Hunk → Inline 이 빈 화면). 배치가 끝난 다음 틀에 한 번 더 채우면 높이가 잡힌다.
                    _list.schedule.Execute(_list.RefreshItems);
                }
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
            if (_isEditing)
            {
                // 저장 뒤 새로 읽은 diff 로 편집기의 "바뀐 줄" 표시를 맞춘다.
                ApplyEditVisibility();
                _editor.SetChangedLines(ChangedNewLines());
            }
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
                // 편집 중이면 편집기가 그 자리에 그대로 있으니 "불러오는 중" 을 겹쳐 띄우지 않는다.
                _loading.SetVisible(_isEditing == false);
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
            ResetTextSelection();
        }

        // ── 부분 스테이징 ───────────────────────────

        /// <summary>
        /// 줄 요소에 끌기·Shift+클릭 고르기를 단다. 요소의 userData 는 그 줄의 목록 인덱스(없으면 -1)로 bind 때 넣는다.
        /// GitKraken 처럼 줄 고르기는 거터·줄 번호 쪽에서 시작한다 — 코드 칸을 누르면 코드 칸이 이벤트를 막고 글 고르기를 한다(<see cref="OnCodePointerDown"/>).
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
            if (evt.button != 0 || evt.target is Button)
            {
                return;
            }
            // 줄 번호 쪽을 누르면 고른 글은 푼다(두 가지가 한꺼번에 칠해지면 무엇을 올리는지 헷갈린다).
            ClearTextSelection();
            if (CanPartialStage == false)
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
                // 고른 줄 중 하나를 누르면 고른 줄을 모두, 아니면 그 줄 하나만. 나란히 보기면 같은 행의 짝(삭제↔추가)도 함께.
                ApplyLinesAsync(WithSplitPartners(_selectedLines.Contains(index) ? new List<int>(_selectedLines) : new List<int> { index }));
            };
            return button;
        }

        /// <summary>
        /// 나란히 보기의 한 행은 바뀐 줄 한 쌍(왼쪽 '-', 오른쪽 '+')이다. 한쪽만 올리면 원래 줄과 새 줄이 둘 다 인덱스에 남으므로
        /// 고른 줄과 같은 행에 있는 반대쪽 바뀐 줄을 더한다. 통합 보기에서는 그대로 둔다(줄마다 따로 고를 수 있다).
        /// </summary>
        private List<int> WithSplitPartners(List<int> lines)
        {
            if (_shownSplit != true)
            {
                return lines;
            }
            HashSet<int> result = new(lines);
            foreach (GitDiffRow row in _rows)
            {
                int left = IndexOfChange(row.Left);
                int right = IndexOfChange(row.Right);
                if (left < 0 || right < 0)
                {
                    continue;
                }
                if (result.Contains(left) || result.Contains(right))
                {
                    result.Add(left);
                    result.Add(right);
                }
            }
            return result.OrderBy(i => i).ToList();
        }

        private int IndexOfChange(GitDiffLine line)
        {
            return line != null && GitPartialPatch.IsChange(line) && _lineIndexes.TryGetValue(line, out int index) ? index : -1;
        }

        /// <summary>
        /// 머리 줄 오른쪽의 GitKraken 식 버튼 둘: [Discard Hunk](빨강, Unstaged 만) [Stage Hunk](초록, Staged 면 Unstage Hunk).
        /// 묶음(<c>.diff-row__hunk-actions</c>)의 userData 가 묶음 순번이다.
        /// </summary>
        private VisualElement HunkActions()
        {
            VisualElement actions = new();
            actions.AddToClassList("diff-row__hunk-actions");
            Button discard = new();
            discard.AddToClassList("diff-row__hunk-button");
            discard.AddToClassList("diff-row__hunk-button--discard");
            discard.clicked += () =>
            {
                int hunkIndex = LineIndexOf(actions);
                if (hunkIndex >= 0 && _document != null)
                {
                    DiscardLinesAsync(GitPartialPatch.ChangedLinesOfHunk(_document.Lines, hunkIndex));
                }
            };
            actions.Add(discard);
            Button stage = new();
            stage.AddToClassList("diff-row__hunk-button");
            stage.AddToClassList("diff-row__hunk-button--stage");
            stage.clicked += () =>
            {
                int hunkIndex = LineIndexOf(actions);
                if (hunkIndex >= 0 && _document != null)
                {
                    ApplyLinesAsync(GitPartialPatch.ChangedLinesOfHunk(_document.Lines, hunkIndex));
                }
            };
            actions.Add(stage);
            return actions;
        }

        /// <summary>
        /// 줄 요소의 거터(버튼 칸)·머리 줄 버튼을 이 줄에 맞춘다. 부분 스테이징이 안 되면 거터 칸을 숨겨 자리를 내주지 않고,
        /// 바뀐 줄이 아니면 버튼만 숨기며, 머리 줄이 아니면 hunk 버튼을 숨긴다.
        /// </summary>
        /// <returns>이 줄이 고른 줄인지.</returns>
        private bool BindStageControls(VisualElement element, VisualElement gutter, VisualElement hunk, GitDiffLine line)
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
                // GitKraken 처럼 변경 묶음(Hunk) 보기에서는 머리 줄마다 늘 보인다. 파일 전체를 읽은 보기(통합·나란히)는 머리 줄이 파일 전체 하나라
                // "hunk 스테이지" 가 곧 파일 전체 Stage 라서 숨긴다(GitKraken 도 그 보기에서는 머리 줄 버튼이 없다).
                bool isHunk = enabled && line.Kind == GitDiffLineKind.Hunk && _loadedContextLines < GitDiffOptions.MaxContextLines;
                hunk.style.display = isHunk ? DisplayStyle.Flex : DisplayStyle.None;
                hunk.userData = isHunk ? line.HunkIndex : -1;
                Button discardHunk = (Button)hunk[0];
                Button stageHunk = (Button)hunk[1];
                // 버리기는 작업 사본의 변경(Unstaged)에만 있다. git 이 아직 모르는 새 파일은 버릴 원본이 없다(파일 지우기는 파일 메뉴에서).
                discardHunk.style.display = isHunk && CanDiscard ? DisplayStyle.Flex : DisplayStyle.None;
                discardHunk.text = GitText.Get("diff.discardHunk");
                discardHunk.tooltip = GitText.Get("diff.discardHunkTooltip");
                stageHunk.EnableInClassList("diff-row__hunk-button--unstage", IsUnstage);
                stageHunk.text = GitText.Get(IsUnstage ? "diff.unstageHunk" : "diff.stageHunk");
                stageHunk.tooltip = GitText.Get(IsUnstage ? "diff.unstageHunkTooltip" : "diff.stageHunkTooltip");
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
            if (reverse == false && (await ConfirmConflictMarkersAsync(target) == false || _runner.IsBusy || _target != target))
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

        /// <summary>
        /// "Discard Hunk": 고른 줄의 변경을 작업 사본에서 되돌린다(<c>git apply --reverse</c>, 인덱스는 그대로).
        /// Stage 와 달리 되돌릴 수 없어(작업 사본의 글이 사라진다) 기존 "변경 버리기" 처럼 한 번 묻는다.
        /// </summary>
        private async void DiscardLinesAsync(ICollection<int> lines)
        {
            if (CanDiscard == false || _runner.IsBusy)
            {
                return;
            }
            GitDiffTarget target = _target;
            // 작업 사본(새 판)에 거꾸로 적용할 패치라 Unstage 와 같은 규칙(reverse)으로 만든다: 고르지 않은 '+' 는 작업 사본에 있으니 문맥, '-' 는 뺀다.
            string patch = GitPartialPatch.Build(_document.Lines, lines, target.RepoPath, reverse: true);
            if (patch == null)
            {
                return;
            }
            if (await GitConfirmDialog.ConfirmAsync(PopupHost, ActionGlyphKind.Undo, GitText.Get("diff.discardHunkTitle"),
                    GitText.Format("diff.discardHunkMessage", GitDisplay.Nfc(target.RepoPath)), GitText.Get("staging.discardAlertOk"), okDanger: true) == false
                || _runner.IsBusy)
            {
                return;
            }
            bool zeroContext = _loadedContextLines == 0;
            ClearSelection();
            await _runner.RunAsync(GitText.Get("op.discardHunk"),
                () => _commands.DiscardPatchFromWorkTreeAsync(patch, zeroContext, target.RepoPath), refreshAssets: true, workingTreeOnly: true);
            if (_target == target)
            {
                Reload();
            }
        }

        // ── 머리 줄의 "Stage this change"(파일 전체 Stage·Unstage, GitKraken 과 같다) ──

        private ToolbarButton _stageFileButton;

        private void UpdateStageFileButton()
        {
            if (_stageFileButton == null)
            {
                return;
            }
            bool shown = _runner != null && _target != null && (_target.Source == GitDiffSource.Unstaged || _target.Source == GitDiffSource.Staged);
            _stageFileButton.SetVisible(shown);
            if (shown == false)
            {
                return;
            }
            _stageFileButton.EnableInClassList("git-diff__stage-file--unstage", IsUnstage);
            _stageFileButton.text = GitText.Get(IsUnstage ? "diff.unstageFile" : "diff.stageFile");
            _stageFileButton.tooltip = GitText.Get(IsUnstage ? "diff.unstageFileTooltip" : "diff.stageFileTooltip");
        }

        /// <summary>
        /// 충돌 표시(<<<<<<< ======= >>>>>>>)가 남은 파일을 올리려 하면 먼저 묻는다(목록의 Stage 와 같은 규칙). 올려도 되면 true.
        /// </summary>
        private async Task<bool> ConfirmConflictMarkersAsync(GitDiffTarget target)
        {
            string fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(GitRepository.RepoRoot, target.RepoPath));
            if (GitInProgress.HasConflictMarkers(fullPath) == false)
            {
                return true;
            }
            return await GitConfirmDialog.ConfirmAsync(PopupHost, ActionGlyphKind.Branch, GitText.Get("staging.markersTitle"),
                GitText.Format("staging.markersMessage", target.RepoPath), GitText.Get("staging.markersOk"), okDanger: true);
        }

        /// <summary>
        /// 이 파일 전체를 올리거나(Unstaged) 내린다(Staged). 목록의 Stage·Unstage 와 같은 명령이다.
        /// </summary>
        private async void StageWholeFileAsync()
        {
            if (_runner == null || _runner.IsBusy || _target == null)
            {
                return;
            }
            GitDiffTarget target = _target;
            GitStatusEntry entry = GitRepository.Entries.FirstOrDefault(e => e.RepoPath == target.RepoPath);
            if (entry == null)
            {
                return;
            }
            bool unstage = target.Source == GitDiffSource.Staged;
            if (unstage == false && await ConfirmConflictMarkersAsync(target) == false)
            {
                return;
            }
            GitStatusEntry[] entries = { entry };
            await _runner.RunAsync(GitText.Get(unstage ? "op.unstage" : "op.stage"),
                () => unstage ? _commands.UnstageAsync(entries) : _commands.StageAsync(entries), refreshAssets: false, workingTreeOnly: true);
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
                    // git 이 아직 모르는 새 파일은 비교할 스테이지 쪽이 없다.
                    return GitText.Get(target.State == GitFileState.Untracked ? "diff.revisionsUntracked" : "diff.revisionsUnstaged");
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
            if (_variableHeights || scroll == null)
            {
                // 줄 높이가 달라(접은 줄·높은 머리 줄) 위치를 셀 수 없다. 목록이 알아서 보이게 한다.
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
            if (_variableHeights || scroll == null)
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

        // ── 코드 글 고르기·복사 ────────────────────

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(TextSelectionProperty, out Color color))
            {
                _textSelectionColor = color;
                RefreshTextSelection();
            }
        }

        private bool HasTextSelection => _textAnchorRow >= 0 && (_textAnchorRow != _textFocusRow || _textAnchorOffset != _textFocusOffset);

        /// <summary>
        /// 고른 글을 다시 그리지 않고 비운다(곧 목록을 다시 채울 때).
        /// </summary>
        private void ResetTextSelection()
        {
            _textAnchorRow = -1;
            _textFocusRow = -1;
            _textAnchorOffset = 0;
            _textFocusOffset = 0;
            EndTextDrag();
        }

        private void ClearTextSelection()
        {
            if (_textAnchorRow < 0)
            {
                return;
            }
            bool hadSelection = HasTextSelection;
            ResetTextSelection();
            if (hadSelection)
            {
                RefreshTextSelection();
            }
        }

        /// <summary>
        /// 닻·끝점을 앞뒤 순서로 맞춘 고른 범위.
        /// </summary>
        private (int startRow, int startOffset, int endRow, int endOffset) TextRange()
        {
            bool anchorFirst = _textAnchorRow < _textFocusRow || (_textAnchorRow == _textFocusRow && _textAnchorOffset <= _textFocusOffset);
            return anchorFirst
                ? (_textAnchorRow, _textAnchorOffset, _textFocusRow, _textFocusOffset)
                : (_textFocusRow, _textFocusOffset, _textAnchorRow, _textAnchorOffset);
        }

        /// <summary>
        /// 목록 줄 <paramref name="row"/> 의 한쪽에서 고른 글 범위. 고른 글이 없으면 시작이 -1.
        /// </summary>
        /// <returns>시작·끝(원래 줄 글의 위치)과, 고른 범위가 다음 줄로 이어지는지.</returns>
        private (int start, int end, bool selectsLineEnd) TextSelectionIn(int row, bool isRight, int length)
        {
            if (HasTextSelection == false || isRight != _textRight)
            {
                return (-1, -1, false);
            }
            (int startRow, int startOffset, int endRow, int endOffset) = TextRange();
            if (row < startRow || row > endRow)
            {
                return (-1, -1, false);
            }
            int start = row == startRow ? Math.Min(startOffset, length) : 0;
            int end = row == endRow ? Math.Min(endOffset, length) : length;
            return (start, end, row < endRow);
        }

        /// <summary>
        /// 나란히 보기의 오른쪽(새 판) 코드 칸인지.
        /// </summary>
        private static bool IsRightCode(VisualElement code)
        {
            return code.parent != null && code.parent.ClassListContains("diff-side--right");
        }

        private static int RowOf(VisualElement code)
        {
            return code.userData is int row ? row : -1;
        }

        /// <summary>
        /// 글을 고르는 쪽(<see cref="_textRight"/>)의 줄. 나란히 보기에서 그쪽이 빈 줄이면 null.
        /// </summary>
        private GitDiffLine TextLineAt(int row)
        {
            if (row < 0 || row >= _rows.Count)
            {
                return null;
            }
            return _textRight ? _rows[row].Right : _rows[row].Left;
        }

        /// <summary>
        /// 코드 칸을 눌렀을 때: 글 고르기를 시작한다(두 번 누르면 낱말, 세 번이면 줄, Shift 면 고른 범위를 늘린다).
        /// 줄 요소의 줄 고르기(<see cref="OnRowPointerDown"/>)로 넘어가지 않게 막는다 — 줄 고르기는 거터·줄 번호 쪽에서 한다.
        /// </summary>
        private void OnCodePointerDown(VisualElement code, PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                return;
            }
            int row = RowOf(code);
            if (row < 0 || row >= _rows.Count)
            {
                return;
            }
            evt.StopPropagation();
            // 줄 고르기(부분 스테이징)는 푼다. 글 고르기와 함께 칠해지면 헷갈린다.
            ClearSelection();
            bool isRight = IsRightCode(code);
            bool extend = evt.shiftKey && _textAnchorRow >= 0 && isRight == _textRight;
            _textRight = isRight;
            GitDiffLine line = TextLineAt(row);
            int offset = OffsetAt(code, line, evt.position);
            if (extend)
            {
                _textFocusRow = row;
                _textFocusOffset = offset;
            }
            else if (evt.clickCount == 2 && line != null)
            {
                (int start, int end) = WordAt(line.Text ?? string.Empty, offset);
                SetTextSelection(row, start, row, end);
            }
            else if (evt.clickCount >= 3)
            {
                // 줄 전체와 줄바꿈까지(편집기와 같다). 마지막 줄이면 줄 끝까지.
                bool hasNext = row + 1 < _rows.Count;
                SetTextSelection(row, 0, hasNext ? row + 1 : row, hasNext ? 0 : (line?.Text ?? string.Empty).Length);
            }
            else
            {
                SetTextSelection(row, offset, row, offset);
            }
            if (evt.clickCount == 1)
            {
                BeginTextDrag();
            }
            _list.Focus();
            RefreshTextSelection();
        }

        private void SetTextSelection(int anchorRow, int anchorOffset, int focusRow, int focusOffset)
        {
            _textAnchorRow = anchorRow;
            _textAnchorOffset = anchorOffset;
            _textFocusRow = focusRow;
            _textFocusOffset = focusOffset;
        }

        private void BeginTextDrag()
        {
            EndTextDrag();
            _textDragRoot = _list.panel?.visualTree;
            if (_textDragRoot == null)
            {
                return;
            }
            _textDragging = true;
            _textDragRoot.RegisterCallback<PointerMoveEvent>(OnTextPointerMove, TrickleDown.TrickleDown);
            _textDragRoot.RegisterCallback<PointerUpEvent>(OnTextPointerUp, TrickleDown.TrickleDown);
        }

        private void EndTextDrag()
        {
            _textDragging = false;
            if (_textDragRoot == null)
            {
                return;
            }
            _textDragRoot.UnregisterCallback<PointerMoveEvent>(OnTextPointerMove, TrickleDown.TrickleDown);
            _textDragRoot.UnregisterCallback<PointerUpEvent>(OnTextPointerUp, TrickleDown.TrickleDown);
            _textDragRoot = null;
        }

        private void OnTextPointerMove(PointerMoveEvent evt)
        {
            if (_textDragging == false)
            {
                return;
            }
            // 창이 포커스를 잃는 사이 버튼 떼기를 놓쳤으면(왼쪽 버튼이 눌려 있지 않으면) 끌기를 끝낸다.
            if ((evt.pressedButtons & 1) == 0)
            {
                EndTextDrag();
                return;
            }
            VisualElement code = CodeAt(evt.position);
            if (code == null)
            {
                return;
            }
            int row = RowOf(code);
            if (row < 0 || row >= _rows.Count)
            {
                return;
            }
            int offset = OffsetAt(code, TextLineAt(row), evt.position);
            if (row == _textFocusRow && offset == _textFocusOffset)
            {
                return;
            }
            _textFocusRow = row;
            _textFocusOffset = offset;
            RefreshTextSelection();
        }

        private void OnTextPointerUp(PointerUpEvent evt)
        {
            if (_textDragging == false)
            {
                return;
            }
            EndTextDrag();
        }

        /// <summary>
        /// 화면 위치의 줄에서 글을 고르는 쪽의 코드 칸. 목록 밖(위·아래)으로 끌면 목록을 그만큼 굴리고 맨 위·아래 줄을 쓴다.
        /// 나란히 보기에서 다른 쪽으로 넘어가도 처음 고른 쪽의 코드 칸을 돌려준다(위치는 그 칸 끝으로 붙는다).
        /// </summary>
        private VisualElement CodeAt(Vector2 position)
        {
            ScrollView scroll = _list.Q<ScrollView>();
            if (scroll == null || _list.panel == null)
            {
                return null;
            }
            Rect view = scroll.contentViewport.worldBound;
            if (position.y < view.yMin)
            {
                scroll.scrollOffset -= new Vector2(0, view.yMin - position.y);
            }
            else if (position.y > view.yMax)
            {
                scroll.scrollOffset += new Vector2(0, position.y - view.yMax);
            }
            Vector2 inside = new(Mathf.Clamp(position.x, view.xMin + 1, view.xMax - 1), Mathf.Clamp(position.y, view.yMin + 1, view.yMax - 1));
            VisualElement row = _list.panel.Pick(inside);
            while (row != null && row != _list && row.ClassListContains("diff-row") == false)
            {
                row = row.parent;
            }
            if (row == null || row == _list)
            {
                return null;
            }
            // 통합 보기: 거터·옛 번호·새 번호·부호·코드. 나란히 보기: 왼쪽·오른쪽(각각 거터·번호·부호·코드).
            return _shownSplit == true ? row[_textRight ? 1 : 0][3] : row[4];
        }

        /// <summary>
        /// 화면 위치에 가장 가까운 글자 경계(원래 줄 글의 위치). 코드 칸 왼쪽이면 0, 글 끝 너머면 줄 길이.
        /// 글자 자리는 TextCore 가 그린 그대로 묻는다(<see cref="CursorPosition"/>) — 한글처럼 폭이 다른 글자·접은 줄도 맞는다.
        /// </summary>
        private int OffsetAt(VisualElement code, GitDiffLine line, Vector2 position)
        {
            if (line == null)
            {
                return 0;
            }
            string raw = line.Text ?? string.Empty;
            // 위치가 든 조각: 오른쪽 끝이 위치보다 오른쪽인 첫 조각(없으면 마지막 조각).
            int part = -1;
            for (int i = 0; i < 3; i++)
            {
                (int from, int to) = PartRange(line, i);
                if (to <= from)
                {
                    continue;
                }
                part = i;
                if (position.x < code[i].worldBound.xMax)
                {
                    break;
                }
            }
            if (part < 0)
            {
                return 0;
            }
            (int partStart, int partEnd) = PartRange(line, part);
            Label label = (Label)code[part];
            int display = DisplayIndexAt(label, label.WorldToLocal(position), DisplayLength(raw, partStart, partEnd));
            return RawOffset(raw, partStart, partEnd, display);
        }

        /// <summary>
        /// 라벨 안 위치에 가장 가까운 글자 경계(화면 글자 순번, 태그는 세지 않는다). 경계의 자리는 글자 순서대로 줄(y)·x 가 커지므로 둘로 나눠 찾는다.
        /// 커서 자리의 y 는 그 줄의 아래 끝이다.
        /// </summary>
        private static int DisplayIndexAt(Label label, Vector2 local, int count)
        {
            // 위치가 든 줄: 아래 끝이 위치보다 아래인 첫 경계의 줄(없으면 마지막 줄).
            int probe = LowerBound(0, count, i => CursorPosition(label, i).y >= local.y);
            float lineY = CursorPosition(label, Math.Min(probe, count)).y;
            int lineStart = LowerBound(0, count, i => CursorPosition(label, i).y >= lineY - 0.5f);
            int lineEnd = LowerBound(lineStart, count, i => CursorPosition(label, i).y > lineY + 0.5f) - 1;
            lineEnd = Math.Max(lineStart, Math.Min(lineEnd, count));
            // 그 줄에서 x 가 위치 이상인 첫 경계와 바로 앞 경계 중 가까운 쪽.
            int index = LowerBound(lineStart, lineEnd, i =>CursorPosition(label, i).x >= local.x);
            if (index > lineEnd)
            {
                return lineEnd;
            }
            if (index > lineStart)
            {
                float right = CursorPosition(label, index).x - local.x;
                float left = local.x - CursorPosition(label, index - 1).x;
                if (left < right)
                {
                    return index - 1;
                }
            }
            return index;
        }

        /// <summary>
        /// 라벨 글의 <paramref name="index"/> 번째 화면 글자 경계(커서 자리). 줄의 아래 끝 y.
        /// <c>ITextSelection.GetCursorPositionFromStringIndex</c> 가 있으면(Unity 6.x 뒤 버전) 그것을 쓰고 — TextCore 가 그린 그대로라 접은 줄도 맞는다 —
        /// 없으면(Unity 6.0) 앞쪽 글자를 같은 스타일로 재서(<see cref="TextElement.MeasureTextSize"/>) 한 줄로 셈한다. 버전을 추측하지 않으려고 실행 중에 찾는다.
        /// </summary>
        private static Vector2 CursorPosition(Label label, int index)
        {
            if (s_cursorPosition != null)
            {
                return (Vector2)s_cursorPosition.Invoke(label.selection, new object[] { index });
            }
            string plain = RichTagPattern.Replace(label.text ?? string.Empty, string.Empty);
            string prefix = plain.Substring(0, Math.Clamp(index, 0, plain.Length));
            if (label.enableRichText)
            {
                prefix = "<noparse>" + prefix + "</noparse>";
            }
            Vector2 size = label.MeasureTextSize(prefix, 0, VisualElement.MeasureMode.Undefined, 0, VisualElement.MeasureMode.Undefined);
            Rect content = label.contentRect;
            return new Vector2(content.x + size.x, content.yMax);
        }

        private static readonly System.Reflection.MethodInfo s_cursorPosition =
            typeof(ITextSelection).GetMethod("GetCursorPositionFromStringIndex", new[] { typeof(int) });

        private static readonly System.Text.RegularExpressions.Regex RichTagPattern = new("<[^>]+>");

        /// <summary>
        /// [from, to] 에서 <paramref name="predicate"/> 가 참인 첫 값. 모두 거짓이면 to + 1. 술어는 앞쪽이 거짓, 뒤쪽이 참이어야 한다.
        /// </summary>
        private static int LowerBound(int from, int to, Func<int, bool> predicate)
        {
            int low = from;
            int high = to + 1;
            while (low < high)
            {
                int middle = low + (high - low) / 2;
                if (predicate(middle))
                {
                    high = middle;
                }
                else
                {
                    low = middle + 1;
                }
            }
            return low;
        }

        /// <summary>
        /// 원래 줄 글 [from, to) 가 화면에서 차지하는 글자 수. 탭만 <see cref="TabDisplayWidth"/> 칸이다(공백 보이기의 · 도 한 칸).
        /// </summary>
        private static int DisplayLength(string raw, int from, int to)
        {
            int length = 0;
            for (int i = from; i < to; i++)
            {
                length += raw[i] == '\t' ? TabDisplayWidth : 1;
            }
            return length;
        }

        /// <summary>
        /// 화면 글자 순번을 원래 줄 글의 위치로 바꾼다. 탭 가운데를 누르면 가까운 쪽 끝으로 붙는다.
        /// </summary>
        private static int RawOffset(string raw, int from, int to, int display)
        {
            int column = 0;
            for (int i = from; i < to; i++)
            {
                int width = raw[i] == '\t' ? TabDisplayWidth : 1;
                if (display < column + (width + 1) / 2)
                {
                    return i;
                }
                column += width;
            }
            return to;
        }

        /// <summary>
        /// 두 번 눌러 고를 낱말: 위치의 글자와 같은 갈래(낱말 글자·공백·그 밖의 기호 하나)로 이어진 범위.
        /// </summary>
        private static (int start, int end) WordAt(string text, int offset)
        {
            if (text.Length == 0)
            {
                return (0, 0);
            }
            int at = Math.Min(offset, text.Length - 1);
            int kind = CharKind(text[at]);
            if (kind == 2)
            {
                return (at, at + 1);
            }
            int start = at;
            int end = at + 1;
            while (start > 0 && CharKind(text[start - 1]) == kind)
            {
                start--;
            }
            while (end < text.Length && CharKind(text[end]) == kind)
            {
                end++;
            }
            return (start, end);
        }

        /// <summary>
        /// 0 낱말 글자(글자·숫자·_), 1 공백·탭, 2 그 밖.
        /// </summary>
        private static int CharKind(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                return 0;
            }
            return c == ' ' || c == '\t' ? 1 : 2;
        }

        /// <summary>
        /// 지금 보이는 줄 요소의 코드 칸만 다시 칠한다(목록 전체를 다시 bind 하지 않는다 — 끄는 동안 자주 부른다).
        /// </summary>
        private void RefreshTextSelection()
        {
            _list.Query<VisualElement>(className: "diff-row__code").ForEach(code =>
            {
                int row = RowOf(code);
                if (row < 0 || row >= _rows.Count)
                {
                    return;
                }
                BindCode(code, IsRightCode(code) ? _rows[row].Right : _rows[row].Left, row);
            });
        }

        /// <summary>
        /// 고른 글. 원래 줄 글 그대로(탭은 탭, 공백 표시·+/− 부호·줄 번호 없이)이고 줄 사이는 그 줄의 줄바꿈(CRLF 면 CRLF)이다.
        /// 나란히 보기에서 고른 쪽이 빈 줄은 건너뛴다.
        /// </summary>
        private string SelectedText()
        {
            (int startRow, int startOffset, int endRow, int endOffset) = TextRange();
            StringBuilder builder = new();
            GitDiffLine previous = null;
            for (int row = startRow; row <= endRow && row < _rows.Count; row++)
            {
                GitDiffLine line = TextLineAt(row);
                if (line == null)
                {
                    continue;
                }
                if (previous != null)
                {
                    builder.Append(previous.HasCarriageReturn ? "\r\n" : "\n");
                }
                string raw = line.Text ?? string.Empty;
                int start = row == startRow ? Math.Min(startOffset, raw.Length) : 0;
                int end = row == endRow ? Math.Min(endOffset, raw.Length) : raw.Length;
                builder.Append(raw, start, Math.Max(0, end - start));
                previous = line;
            }
            return builder.ToString();
        }

        private void CopySelection()
        {
            if (HasTextSelection)
            {
                EditorGUIUtility.systemCopyBuffer = SelectedText();
            }
        }

        /// <summary>
        /// 글을 고르는 쪽의 줄 전체를 고른다.
        /// </summary>
        private void SelectAllText()
        {
            if (_rows.Count == 0)
            {
                return;
            }
            ClearSelection();
            int last = _rows.Count - 1;
            SetTextSelection(0, 0, last, (TextLineAt(last)?.Text ?? string.Empty).Length);
            RefreshTextSelection();
        }

        /// <summary>
        /// Ctrl+C(맥은 Cmd+C) 복사, Ctrl+A 모두 고르기, Esc 풀기. 에디터는 같은 단축키를 Copy·SelectAll 명령으로도 보내므로 둘 다 받는다(두 번 해도 같다).
        /// </summary>
        private void OnTextKeyDown(KeyDownEvent evt)
        {
            // Cmd/Ctrl+Home·End, Home·End: 처음·끝으로. PageUp·PageDown: 한 화면씩.
            ScrollView scroll = _list.Q<ScrollView>();
            if (scroll != null && _rows.Count > 0)
            {
                float page = Mathf.Max(RowHeight, scroll.contentViewport.layout.height - RowHeight);
                float? target = evt.keyCode switch
                {
                    KeyCode.Home => 0f,
                    KeyCode.End => float.MaxValue,
                    KeyCode.UpArrow when evt.actionKey => 0f,
                    KeyCode.DownArrow when evt.actionKey => float.MaxValue,
                    KeyCode.PageUp => scroll.scrollOffset.y - page,
                    KeyCode.PageDown => scroll.scrollOffset.y + page,
                    _ => null,
                };
                if (target.HasValue)
                {
                    float max = Mathf.Max(0, _rows.Count * RowHeight - scroll.contentViewport.layout.height);
                    scroll.scrollOffset = new Vector2(scroll.scrollOffset.x, Mathf.Clamp(target.Value, 0, max));
                    evt.StopPropagation();
                    return;
                }
            }
            if (evt.actionKey && evt.keyCode == KeyCode.C && HasTextSelection)
            {
                CopySelection();
                evt.StopPropagation();
            }
            else if (evt.actionKey && evt.keyCode == KeyCode.A && _rows.Count > 0)
            {
                SelectAllText();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape && HasTextSelection)
            {
                ClearTextSelection();
                evt.StopPropagation();
            }
        }

        private void OnValidateTextCommand(ValidateCommandEvent evt)
        {
            if ((evt.commandName == "Copy" && HasTextSelection) || (evt.commandName == "SelectAll" && _rows.Count > 0))
            {
                evt.StopPropagation();
            }
        }

        private void OnExecuteTextCommand(ExecuteCommandEvent evt)
        {
            if (evt.commandName == "Copy" && HasTextSelection)
            {
                CopySelection();
                evt.StopPropagation();
            }
            else if (evt.commandName == "SelectAll" && _rows.Count > 0)
            {
                SelectAllText();
                evt.StopPropagation();
            }
        }

        /// <summary>
        /// 오른쪽 버튼 메뉴: 복사·모두 선택. 나란히 보기에서 고른 글이 없으면 누른 쪽을 고를 쪽으로 삼는다(모두 선택이 그쪽을 고른다).
        /// </summary>
        private void OnTextContextMenu(ContextualMenuPopulateEvent evt)
        {
            if (_target == null || _isImageMode || _rows.Count == 0)
            {
                return;
            }
            if (HasTextSelection == false && evt.target is VisualElement target)
            {
                VisualElement side = target;
                while (side != null && side != _list && side.ClassListContains("diff-side") == false)
                {
                    side = side.parent;
                }
                _textRight = side != null && side.ClassListContains("diff-side--right");
            }
            _list.Focus();
            evt.menu.AppendAction(GitText.Get("diff.copy"), _ => CopySelection(), HasTextSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction(GitText.Get("diff.selectAll"), _ => SelectAllText());
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
            row.Add(HunkActions());
            RegisterSelection(row);
            return row;
        }

        private void BindUnifiedRow(VisualElement row, int index)
        {
            GitDiffLine line = _rows[index].Left;
            SetKindClasses(row, "diff-row", line);
            row.EnableInClassList("diff-row--selected", BindStageControls(row, row[0], row[5], line));
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
            BindCode(row[4], line, index);
        }

        // ── 나란히 보기 ───────────────────────────

        private VisualElement MakeSplitRow()
        {
            VisualElement row = new();
            row.AddToClassList("diff-row");
            row.Add(SideElement("diff-side--left"));
            row.Add(SideElement("diff-side--right"));
            row.Add(HunkActions());
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
            bool leftSelected = BindSide(row[0], diffRow.Left, index, isLeft: true, hunk: row[2]);
            bool rightSelected = BindSide(row[1], diffRow.Right, index, isLeft: false, hunk: null);
            row.EnableInClassList("diff-row--selected", leftSelected || rightSelected);
        }

        private bool BindSide(VisualElement side, GitDiffLine line, int rowIndex, bool isLeft, VisualElement hunk)
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
                BindCode(side[3], null, rowIndex);
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
            BindCode(side[3], line, rowIndex);
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
        /// 보통은 리치 텍스트를 끈다. 공백 보이기·고른 글이 있을 때만 켜고 코드는 &lt;noparse&gt; 로 감싼다.
        /// userData 는 그 줄의 목록 인덱스(<see cref="BindCode"/> 가 넣는다). 누르면 글 고르기를 시작한다.
        /// </summary>
        private VisualElement CodeElement()
        {
            VisualElement code = new();
            code.AddToClassList("diff-row__code");
            code.RegisterCallback<PointerDownEvent>(evt => OnCodePointerDown(code, evt));
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

        private void BindCode(VisualElement code, GitDiffLine line, int rowIndex)
        {
            code.userData = rowIndex;
            if (line == null)
            {
                for (int i = 0; i < 3; i++)
                {
                    Label part = (Label)code[i];
                    part.enableRichText = false;
                    part.text = string.Empty;
                }
                return;
            }
            string raw = line.Text ?? string.Empty;
            (int selectStart, int selectEnd, bool selectsLineEnd) = TextSelectionIn(rowIndex, IsRightCode(code), raw.Length);
            int lastPart = IsSplitIntoParts(line) ? 2 : 0;
            // 탭을 펼치기 전 위치로 자른 뒤 각 조각을 펼친다(펼친 뒤에는 위치가 달라진다).
            for (int i = 0; i < 3; i++)
            {
                (int from, int to) = PartRange(line, i);
                // 줄 끝 너머까지 고른 표시(빈칸 하나)는 마지막 조각에 붙인다.
                bool isLast = i == lastPart;
                int localStart = selectStart < 0 ? -1 : Math.Clamp(selectStart - from, 0, to - from);
                int localEnd = selectStart < 0 ? -1 : Math.Clamp(selectEnd - from, 0, to - from);
                bool marked = selectStart >= 0 && (localEnd > localStart || (selectsLineEnd && isLast));
                Label part = (Label)code[i];
                // 공백 보이기는 흐린 · → 를, 고른 글은 <mark> 를 리치 텍스트로 칠한다. 코드의 < > 는 <noparse> 로 감싸 태그로 읽히지 않게 한다.
                part.enableRichText = _showWhitespace || marked;
                string text = raw.Substring(from, to - from);
                part.text = marked ? RenderSelected(text, localStart, localEnd, selectsLineEnd && isLast) : Render(text);
            }
        }

        /// <summary>
        /// 이 줄을 세 조각(앞·바뀐 부분·뒤)으로 나눠 그리는지. 아니면 첫 조각 하나에 다 그린다.
        /// 접어 보기는 한 조각이어야 줄 끝에서 접힌다(세 조각이면 조각마다 따로 접혀 어긋난다). 그래서 줄 안 강조는 접지 않을 때만 조각을 나눈다.
        /// </summary>
        private bool IsSplitIntoParts(GitDiffLine line)
        {
            return line.ChangeLength > 0 && _wrap == false;
        }

        /// <summary>
        /// 코드 칸의 조각 <paramref name="part"/>(0 앞·1 바뀐 부분·2 뒤)가 그리는 원래 줄 글의 범위 [from, to).
        /// </summary>
        private (int from, int to) PartRange(GitDiffLine line, int part)
        {
            int length = (line.Text ?? string.Empty).Length;
            if (IsSplitIntoParts(line) == false)
            {
                return part == 0 ? (0, length) : (length, length);
            }
            int start = Math.Min(line.ChangeStart, length);
            int end = start + Math.Min(line.ChangeLength, length - start);
            return part switch
            {
                0 => (0, start),
                1 => (start, end),
                _ => (end, length),
            };
        }

        /// <summary>
        /// 고른 부분 [<paramref name="selectStart"/>, <paramref name="selectEnd"/>)을 &lt;mark&gt; 로 칠한 코드 글. 나머지는 <see cref="Render"/> 와 같게 그린다.
        /// </summary>
        /// <param name="selectsLineEnd">고른 범위가 다음 줄로 이어진다. 줄바꿈 자리를 빈칸 하나로 칠한다(빈 줄도 고른 것이 보이게).</param>
        private string RenderSelected(string text, int selectStart, int selectEnd, bool selectsLineEnd)
        {
            StringBuilder builder = new(text.Length * 2 + 32);
            builder.Append(RenderRich(text.Substring(0, selectStart)));
            builder.Append("<mark=#").Append(ColorUtility.ToHtmlStringRGBA(_textSelectionColor)).Append('>');
            builder.Append(RenderRich(text.Substring(selectStart, selectEnd - selectStart)));
            if (selectsLineEnd)
            {
                builder.Append(' ');
            }
            builder.Append("</mark>");
            builder.Append(RenderRich(text.Substring(selectEnd)));
            return builder.ToString();
        }

        /// <summary>
        /// 리치 텍스트를 켠 조각에 넣을 코드 글. 공백 보이기면 <see cref="Render"/> 가 이미 &lt;noparse&gt; 로 감싼다.
        /// </summary>
        private string RenderRich(string text)
        {
            if (text.Length == 0)
            {
                return string.Empty;
            }
            if (_showWhitespace)
            {
                return Render(text);
            }
            // 코드에 </noparse> 가 그대로 있으면 감싼 구간이 거기서 끝난다. 사이에 폭 없는 공백을 넣어 끊는다.
            return "<noparse>" + ExpandTabs(text).Replace("</noparse>", "</no​parse>") + "</noparse>";
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
