using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 세로 커밋 그래프의 칸. 번호는 <see cref="GitVcsSettings.GraphColumnOrder"/> 에 저장되므로 바꾸지 않는다.
    /// </summary>
    public enum CommitGraphColumn
    {
        Refs = 0,
        Graph = 1,
        Message = 2,
    }

    /// <summary>
    /// GitKraken 형태의 세로 커밋 그래프: 맨 위 작업 중(WIP) 줄, 그 아래 최신부터 커밋 한 줄씩.
    /// 한 줄은 [브랜치/태그 이름표][그래프][메시지] 이고, 머리 줄 칸을 끌어 순서를, 칸 경계를 끌어 폭을 바꾼다.
    /// 맨 오른쪽 칸은 남은 폭을 갖는다. 이름표가 그래프 바로 옆이면 이름표에서 커밋 원까지 가지 색 선을 잇고,
    /// 그래프 칸에는 그 커밋 가지 색을 옅게 깔아 어느 가지인지 한눈에 보이게 한다.
    /// 줄은 가상화 ListView 가 그리고, 선·원은 그래프 칸(옆의 이름표 칸까지) 위에 겹친 요소가 Painter2D 로 그린다
    /// (보이는 줄만 그리고, 스크롤할 때마다 다시 그린다). 배치는 <see cref="VerticalGraphLayout"/>.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public sealed class CommitGraphList
    {
        /// <summary>
        /// 목록 맨 위 작업 중 줄의 자리표.
        /// </summary>
        private sealed class WipItem
        {
        }

        /// <summary>
        /// 커밋 한 줄. 칸 순서를 바꿔도 칸을 찾을 수 있게 칸 요소를 들고 있다. 줄 번호는 <see cref="VisualElement.userData"/>.
        /// </summary>
        private sealed class GraphRow : VisualElement
        {
            public VisualElement Refs;
            public VisualElement Graph;
            public VisualElement Message;

            /// <summary>
            /// Fork 형태에서 메시지 뒤에 붙는 [작성자][SHA][날짜] 묶음(<see cref="CommitGraphList(VisualElement, IAvatarProvider, bool)"/> 의 detailColumns). 없으면 null.
            /// </summary>
            public VisualElement Details;

            public Label Author;
            public Label Hash;
            public Label Date;

            /// <summary>
            /// 이름표에 보이는 첫 참조의 종류와 참조 개수. 칸 폭이 바뀔 때 다시 묶지 않고 이름표 모양만 고르려고 들고 있다.
            /// </summary>
            public CommitRefKind RefKind;

            /// <summary>첫 이름표에 노트북 뒤 구름도 붙는지(로컬과 원격이 같은 커밋).</summary>
            public bool RefHasRemote;

            public int RefCount;

            /// <summary>
            /// 그래프 칸에 까는 가지 색 띠. 커밋 원 자리에서 시작한다(GitKraken 과 같다).
            /// </summary>
            public VisualElement Band;

            /// <summary>
            /// 고른 줄·마우스가 올라간 줄의 메시지 칸 바탕(GitKraken 과 같다).
            /// </summary>
            public VisualElement Highlight;

            /// <summary>
            /// 고른 줄에서 가지 색 띠를 진하게 덮는 막.
            /// </summary>
            public VisualElement BandGlow;

            /// <summary>
            /// 이 줄 커밋의 가지 열. WIP 줄이면 -1(띠가 없다).
            /// </summary>
            public int LaneRow = -1;

            /// <summary>
            /// 작업 중 줄의 [제목 입력란][연필]. 이 줄이 작업 중 줄에 처음 묶일 때 만든다(<see cref="EnsureWipEditor"/>).
            /// </summary>
            public VisualElement WipEditor;

            public TextField WipInput;

            /// <summary>
            /// 작업 중 줄 입력란 뒤의 변경 개수(고침·추가·지움 순서의 숫자 글자). 묶음은 <see cref="WipCountsRoot"/>.
            /// </summary>
            public Label[] WipCounts;

            public VisualElement WipCountsRoot;

            public VisualElement Cell(CommitGraphColumn column)
            {
                return column switch
                {
                    CommitGraphColumn.Refs => Refs,
                    CommitGraphColumn.Graph => Graph,
                    _ => Message,
                };
            }
        }

        private static readonly WipItem Wip = new();

        private static readonly CommitGraphColumn[] DefaultOrder = { CommitGraphColumn.Refs, CommitGraphColumn.Graph, CommitGraphColumn.Message };

        /// <summary>
        /// 메시지 칸이 맨 오른쪽이 아닐 때의 기본 폭.
        /// </summary>
        private const float DefaultMessageColumnWidth = 420f;

        /// <summary>
        /// 메시지 칸을 끌어 줄일 수 있는 최소 폭. 머리는 아이콘(말풍선), 메시지는 말줄임으로 첫 몇 글자만 남는다(GitKraken 과 같다, 약 66px).
        /// 이 폭에서도 작업 중 줄 입력란에 글이 몇 자 보이도록 여백(칸 10 + 입력란 4·4 + 테두리)을 빼고 40px 쯤 남긴다.
        /// </summary>
        private const float MinMessageColumnWidth = 64f;
        private const float MaxMessageColumnWidth = 2000f;

        /// <summary>
        /// 남은 폭을 갖는 맨 오른쪽 칸의 최소 폭.
        /// </summary>
        private const float MinLastColumnWidth = 40f;

        /// <summary>
        /// 왼쪽 브랜치/태그 칸의 기본 폭. 이름표 하나와 "+n" 이 들어가는 크기. 머리 줄 경계를 끌어 바꾼다.
        /// </summary>
        private const float DefaultRefsColumnWidth = 170f;

        /// <summary>
        /// 칸 왼쪽 여백(8)과 이름표 앞쪽 아이콘이 보일 만큼. 이보다 좁게는 끌리지 않는다.
        /// </summary>
        private const float MinRefsColumnWidth = 32f;

        /// <summary>
        /// 브랜치/태그 칸이 맨 왼쪽일 때의 최소 폭. 머리 아이콘(들여쓰기 20 + 14)·아이콘 하나짜리 이름표(12 + 30)가
        /// 왼쪽 자리에 그대로 있는 채로 오른쪽에도 같은 여백이 남는 폭이라, 끝까지 줄이면 가운데 맞춘 것처럼 보인다(GitKraken 과 같다).
        /// 20 + 14 + 20 = 12 + 30 + 12 = 54. USS 의 들여쓰기·이름표 폭을 바꾸면 같이 맞춘다.
        /// </summary>
        private const float MinRefsFirstWidth = 54f;

        private float MinRefsWidth => _order[0] == CommitGraphColumn.Refs ? MinRefsFirstWidth : MinRefsColumnWidth;

        /// <summary>
        /// 브랜치/태그 칸 폭. 끌어 정한 폭도 지금 순서의 최소 폭보다 좁게는 두지 않는다(맨 왼쪽으로 옮기면 넓어진다).
        /// </summary>
        private float RefsWidth => Mathf.Max(_refsWidth, MinRefsWidth);

        private const float MaxRefsColumnWidth = 480f;

        // ── 칸이 좁아질 때(GitKraken 과 같다) ──
        // 이름이 말줄임으로 줄다가 "f…" 정도가 되면 이름을 빼고 아이콘만 남긴다(--no-name). 그보다 좁으면 이름표 모양은 그대로 두고
        // 칸 경계가 오른쪽부터 가린다(.commit-graph-row__refs 의 overflow: hidden). 그래서 한 번에 바뀌지 않고 서서히 가려진다.
        // 아래 폭은 GitStaging.uss 의 이름표 여백·테두리·아이콘 크기를 더한 값이라 USS 를 바꾸면 같이 맞춘다.

        /// <summary>
        /// 칸 좌우 여백(.commit-graph-row__refs 의 왼쪽 8 + 오른쪽 6). 맨 왼쪽 칸이면 왼쪽이 12(--refs-first).
        /// </summary>
        private float RefsCellPadding => _order[0] == CommitGraphColumn.Refs ? 18f : 14f;

        private const string FirstHeaderModifier = "commit-graph__header-cell--first";

        /// <summary>
        /// 맨 왼쪽 칸 종류를 루트에 붙인다(--refs-first·--graph-first·--message-first). 맨 왼쪽 칸의 내용 들여쓰기에 쓴다.
        /// </summary>
        private static readonly string[] FirstColumnModifiers = { "commit-graph--refs-first", "commit-graph--graph-first", "commit-graph--message-first" };

        /// <summary>
        /// 이름을 빼기 전에 남겨 둘 이름 폭(첫 글자와 "…"). 이보다 좁으면 "…" 만 남아 읽을 수 없다.
        /// </summary>
        private const float MinRefNameWidth = 18f;

        /// <summary>
        /// "+n" 앞 간격(.commit-graph-ref__more 의 margin-left). 이 틈에 커밋까지 잇는 선이 지나간다(GitKraken 과 같다).
        /// </summary>
        private const float MoreGap = 6f;

        /// <summary>
        /// "+n" 상자의 좌우 여백 합(.commit-graph-ref__more 의 padding 5 + 5).
        /// </summary>
        private const float MorePadding = 10f;

        /// <summary>
        /// 이름표 바탕의 가지 색 진하기: 현재 브랜치는 진하게, 로컬 브랜치와 "+n" 은 옅게(GitKraken 과 같다).
        /// </summary>
        private const float CurrentRefAlpha = 0.7f;
        private const float LocalRefAlpha = 0.35f;

        /// <summary>원격에만 있는 브랜치 이름표: 로컬보다 한 단계 어둡게 채운다(GitKraken). 테두리 없이 채움만.</summary>
        private const float RemoteRefAlpha = 0.2f;

        /// <summary>
        /// 칸 폭이 최소 폭에서 이만큼 안이면 끝까지 줄인 것으로 본다(끌어 놓은 폭의 소수점 오차).
        /// </summary>
        private const float AtMinimumTolerance = 1f;

        private const string IconHeaderModifier = "commit-graph__header-cell--icon";

        /// <summary>
        /// 브랜치/태그 칸이 맨 왼쪽이고 머리가 아이콘으로 바뀔 만큼 좁을 때: 머리 아이콘과 이름표(상태 아이콘 하나)를 칸 가운데에 둔다(GitKraken 과 같다).
        /// </summary>
        private const string CenteredRefsModifier = "commit-graph--refs-centered";

        /// <summary>
        /// 새 브랜치 이름 입력란과 브랜치/태그 칸 양쪽 경계 사이 간격. 오른쪽 간격에서 커밋까지 잇는 선이 나간다.
        /// </summary>
        private const float BranchInputMargin = 6f;

        /// <summary>
        /// 새 브랜치 이름 입력란의 최소 폭. 칸을 끝까지 줄여도 깜박이는 커서와 글자 한두 개는 보이게.
        /// </summary>
        private const float MinBranchInputWidth = 20f;

        private const float MaxGraphColumnWidth = 800f;

        // Fork 형태의 오른쪽 고정 칸(작성자 · SHA · 날짜). 순서·폭을 바꾸는 칸이 아니라 늘 맨 오른쪽에 붙는다.
        private const float AuthorColumnWidth = 150f;
        private const float HashColumnWidth = 84f;
        private const float DateColumnWidth = 150f;
        private const string DetailsModifier = "commit-graph--details";

        /// <summary>
        /// 그래프 칸 오른쪽 끝 가지 색 막대 폭(USS .commit-graph-row__band 의 border-right-width). 가지는 이 막대 앞까지에 맞춘다.
        /// </summary>
        private const float LaneBarWidth = 3f;

        /// <summary>
        /// 지금 칸 순서에서의 막대 폭. 그래프가 맨 오른쪽이면 막대를 그리지 않으므로 0.
        /// </summary>
        private float BarWidth => IsLast(CommitGraphColumn.Graph) ? 0f : LaneBarWidth;

        private const string GraphLastModifier = "commit-graph--graph-last";

        /// <summary>
        /// 가지가 하나뿐일 때 커밋 원(HEAD 고리)과 오른쪽 막대 사이에 남길 틈.
        /// </summary>
        private const float NodeBarGap = 7f;

        /// <summary>
        /// 그래프 칸에 까는 가지 색의 진하기. 글자를 가리지 않을 만큼만.
        /// </summary>
        private const float LaneTintAlpha = 0.10f;

        /// <summary>
        /// 고른 줄에서 띠를 덮는 가지 색의 진하기(GitKraken 처럼 고른 줄의 띠가 밝아진다).
        /// </summary>
        private const float SelectedLaneTintAlpha = 0.45f;

        // 테마 색. 테마 변수(--git-*)는 물려받기만 해서 요소의 사용자 정의 스타일로는 읽히지 않으므로,
        // GitStaging.uss 가 선·원 층(.commit-graph__overlay)에 이 이름으로 옮겨 적은 값을 읽는다.
        private static readonly CustomStyleProperty<Color> HeadProperty = new("--graph-head");
        private static readonly CustomStyleProperty<Color> OutlineProperty = new("--graph-outline");
        private static readonly CustomStyleProperty<Color> MutedProperty = new("--graph-muted");

        private readonly ListView _list;
        private readonly VisualElement _overlay;
        private readonly GraphLabelLayer _avatarLayer;
        private readonly VisualElement _avatarHost;
        private readonly IAvatarProvider _avatars;
        private readonly VisualElement _header;
        private readonly VisualElement _body;
        private readonly Label[] _headerCells = new Label[3];
        private readonly ColumnResizer[] _resizers = new ColumnResizer[3];
        private readonly TextField _branchInput;

        /// <summary>
        /// Fork 형태: 메시지 뒤에 작성자 · SHA · 날짜 칸을 둔다. 머리 줄 쪽 묶음은 <see cref="_headerDetails"/>.
        /// </summary>
        private readonly bool _detailColumns;

        private readonly VisualElement _headerDetails;

        /// <summary>
        /// 오른쪽 고정 칸들의 폭 합. 맨 오른쪽 순서 칸(남은 폭)은 이만큼 덜 갖는다.
        /// </summary>
        private float DetailsWidth => _detailColumns ? AuthorColumnWidth + HashColumnWidth + DateColumnWidth : 0f;

        /// <summary>
        /// 이름표 칸에 마우스를 올렸을 때 그 줄 참조를 모두 세로로 펼친 목록(<see cref="ShowRefsPopup"/>).
        /// </summary>
        private readonly VisualElement _refsPopup;

        /// <summary>
        /// 펼친 목록의 줄 번호와 그 줄 이름표 칸. 닫혀 있으면 -1·null.
        /// </summary>
        private int _refsPopupIndex = -1;
        private VisualElement _refsPopupCell;
        private CommitGraphColumn[] _order;
        private float _refsWidth;
        private float _messageWidth;

        /// <summary>
        /// 머리 칸 글자(<see cref="GitText"/> 키). 좁아져 아이콘으로 바꿨다가 되돌릴 때 쓴다.
        /// </summary>
        private static readonly string[] HeaderTextKeys = { "graph.columnRefs", "graph.columnGraph", "column.message" };

        /// <summary>
        /// 보이는 줄의 이름표 모양을 마지막으로 고른 칸 폭. 맨 오른쪽 칸이면 창 크기로 바뀌므로 달라졌을 때만 다시 고른다.
        /// </summary>
        private float _fittedRefsWidth = -1f;

        /// <summary>
        /// 보이는 줄의 가지 색 띠를 마지막으로 놓은 그래프 칸 폭.
        /// </summary>
        private float _placedGraphWidth = -1f;

        /// <summary>
        /// 선·원 층 왼쪽 끝에서 그래프 칸 왼쪽 끝까지(층이 그래프 왼쪽의 이름표 칸까지 덮으면 그 폭).
        /// </summary>
        private float _graphOffset;

        /// <summary>
        /// 이름 입력란을 띄운 커밋. null 이면 닫혀 있다.
        /// </summary>
        private string _branchInputHash;

        private Action<string> _branchInputSubmit;

        /// <summary>
        /// 사용자가 끌어 정한 그래프 칸 폭. 0 이면 가지 수에 맞춘 폭(<see cref="VerticalGraphLayout.Width"/>).
        /// </summary>
        private float _graphWidthOverride;
        private VerticalGraphLayout _layout = VerticalGraphLayout.Build(null, false);
        private List<object> _items = new();
        private WipCounts _wip;

        /// <summary>
        /// 작업 중 줄 입력란의 글(커밋 제목). 비어 있으면 "// 작업 중" 이 자리표시로 보인다.
        /// </summary>
        private string _wipSubject = string.Empty;

        /// <summary>
        /// 지금 작업 중 줄에 묶인 줄 요소. 줄 요소는 재사용되므로 다른 줄에 묶이면 비운다.
        /// </summary>
        private GraphRow _wipRow;

        /// <summary>
        /// 작업 중 줄 입력란에 포커스가 있는가. 쓰는 동안에는 바깥에서 들어온 값으로 덮지 않는다.
        /// </summary>
        private bool _wipEditing;

        private Color _headColor = GraphStyle.Head;
        private Color _outlineColor = new(0.12f, 0.12f, 0.13f);
        private Color _mutedColor = Color.gray;

        /// <summary>
        /// 줄을 골랐을 때. 작업 중 줄이면 null.
        /// </summary>
        public event Action<GitGraphNode> CommitSelected;

        public event Action WipSelected;

        /// <summary>
        /// 작업 중 줄 입력란에 제목을 쓸 때(GitKraken 처럼 커밋 입력란의 제목과 같은 글이다).
        /// </summary>
        public event Action<string> WipSubjectEdited;

        /// <summary>
        /// 커밋 줄 우클릭 메뉴.
        /// </summary>
        public event Action<DropdownMenu, GitGraphNode> ContextMenuRequested;

        /// <summary>
        /// 이름표(줄 이름표나 펼친 목록의 참조)를 두 번 눌렀을 때. 그 참조로 전환한다(GitKraken 과 같다).
        /// </summary>
        public event Action<GitGraphNode, CommitRef> RefActivated;

        public VisualElement Root { get; }

        /// <param name="avatars">작성자 아바타. null 이거나 설정에서 꺼져 있으면 작은 점으로 그린다.</param>
        /// <param name="detailColumns">Fork 형태: 메시지 뒤에 작성자 · SHA · 날짜 칸을 둔다(브랜치 탐색기의 커밋 목록 모양).</param>
        public CommitGraphList(VisualElement host, IAvatarProvider avatars = null, bool detailColumns = false)
        {
            _avatars = avatars;
            _detailColumns = detailColumns;
            Root = new VisualElement();
            Root.AddToClassList("commit-graph");
            host.Add(Root);

            GitVcsSettings settings = GitVcsSettings.instance;
            // 칸 순서를 읽기 전이라 여기서는 공통 최소만 막고, 순서에 따른 최소는 RefsWidth 가 지킨다.
            _refsWidth = settings.GraphRefsColumnWidth > 0f ? Mathf.Clamp(settings.GraphRefsColumnWidth, MinRefsColumnWidth, MaxRefsColumnWidth) : DefaultRefsColumnWidth;
            // 최소 폭은 그래프(가지 수·아바타)를 읽어야 정해지므로 여기서는 최대만 막고, 최소는 GraphWidth 가 지킨다.
            _graphWidthOverride = settings.GraphColumnWidth > 0f ? Mathf.Min(settings.GraphColumnWidth, MaxGraphColumnWidth) : 0f;
            _messageWidth = settings.GraphMessageColumnWidth > 0f ? ClampMessageWidth(settings.GraphMessageColumnWidth) : DefaultMessageColumnWidth;
            _order = ParseOrder(settings.GraphColumnOrder);

            // 칸은 오른쪽 경계를 끌어 폭을 바꾸고(두 번 누르면 기본 폭), 머리를 끌어 순서를 바꾼다. 맨 오른쪽 칸이 남은 폭을 갖는다.
            // GitKraken 처럼 작성자·날짜 칸은 두지 않는다 — 오른쪽 커밋 상세와 줄 툴팁에서 본다.
            _header = new VisualElement();
            _header.AddToClassList("list-header");
            _header.AddToClassList("commit-graph__header");
            foreach (CommitGraphColumn column in DefaultOrder)
            {
                _headerCells[(int)column] = HeaderCell(GitText.Get(HeaderTextKeys[(int)column]));
            }
            // 칸이 좁으면 머리 글자 대신 보이는 아이콘(브랜치/태그 = 브랜치, 그래프 = 계층도, 메시지 = 말풍선). 보이고 숨기는 것은 USS(--icon).
            AddHeaderGlyph(CommitGraphColumn.Refs, ActionGlyphKind.Branch);
            AddHeaderGlyph(CommitGraphColumn.Graph, ActionGlyphKind.Hierarchy);
            AddHeaderGlyph(CommitGraphColumn.Message, ActionGlyphKind.Comment);
            _headerCells[(int)CommitGraphColumn.Refs].AddToClassList("commit-graph__header-cell--refs");
            _resizers[(int)CommitGraphColumn.Refs] = new ColumnResizer(
                () => RefsWidth,
                width => SetColumnWidths(ClampRefsWidth(width), _graphWidthOverride, _messageWidth),
                () => settings.GraphRefsColumnWidth = _refsWidth,
                () => SetColumnWidths(DefaultRefsColumnWidth, _graphWidthOverride, _messageWidth));
            _resizers[(int)CommitGraphColumn.Graph] = new ColumnResizer(
                () => GraphWidth,
                width => SetColumnWidths(_refsWidth, ClampGraphWidth(width), _messageWidth),
                () => settings.GraphColumnWidth = _graphWidthOverride,
                () => SetColumnWidths(_refsWidth, 0f, _messageWidth));
            _resizers[(int)CommitGraphColumn.Message] = new ColumnResizer(
                () => _messageWidth,
                width => SetColumnWidths(_refsWidth, _graphWidthOverride, ClampMessageWidth(width)),
                () => settings.GraphMessageColumnWidth = _messageWidth,
                () => SetColumnWidths(_refsWidth, _graphWidthOverride, DefaultMessageColumnWidth));
            if (_detailColumns)
            {
                Root.AddToClassList(DetailsModifier);
                _headerDetails = new VisualElement();
                _headerDetails.AddToClassList("commit-graph__details");
                _headerDetails.Add(DetailHeaderCell("column.author", AuthorColumnWidth));
                _headerDetails.Add(DetailHeaderCell("column.hash", HashColumnWidth));
                _headerDetails.Add(DetailHeaderCell("column.date", DateColumnWidth));
            }
            Root.Add(_header);

            ColumnReorderer reorderer = new(_header, Root, MoveColumn);
            foreach (Label cell in _headerCells)
            {
                reorderer.Attach(cell);
            }

            _body = new VisualElement();
            _body.AddToClassList("commit-graph__body");
            Root.Add(_body);
            VisualElement body = _body;

            _list = new ListView
            {
                fixedItemHeight = VerticalGraphLayout.RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _items,
            };
            _list.AddToClassList("list");
            _list.AddToClassList("commit-graph__list");
            _list.selectionChanged += _ => OnSelectionChanged();
            body.Add(_list);

            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.AddToClassList("commit-graph__overlay");
            _overlay.generateVisualContent += Draw;
            _overlay.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            body.Add(_overlay);

            // 아바타·머리글자는 선 위에 얹는다(선이 그림을 가로지르지 않게). 보이는 줄 수만큼만 요소를 재사용한다.
            _avatarHost = new VisualElement { pickingMode = PickingMode.Ignore };
            _avatarHost.AddToClassList("commit-graph__overlay");
            body.Add(_avatarHost);
            _avatarLayer = new GraphLabelLayer(_avatarHost);

            // GitKraken 처럼 새 브랜치 이름은 그 커밋 줄의 브랜치/태그 칸에서 받는다. 줄 요소는 재사용되므로 입력란 하나를 그 줄 자리에 띄운다.
            // 입력란은 칸 안에만 두고, 커밋까지는 이름표처럼 가지 색 선으로 잇는다(DrawRefConnectors).
            _branchInput = new TextField();
            _branchInput.AddToClassList("commit-graph__branch-input");
            _branchInput.textEdition.placeholder = GitText.Get("graph.branchNamePlaceholder");
            _branchInput.SetVisible(false);
            _branchInput.RegisterCallback<KeyDownEvent>(OnBranchInputKeyDown, TrickleDown.TrickleDown);
            _branchInput.RegisterValueChangedCallback(_ => ValidateBranchInput());
            // 다른 곳을 누르면(줄 선택·다른 창) 만들지 않고 닫는다.
            _branchInput.RegisterCallback<FocusOutEvent>(_ => CloseBranchInput());
            body.Add(_branchInput);

            // 펼친 참조 목록. 누르면 그 커밋 줄을 고르고, 우클릭은 그 줄 메뉴. 마우스가 그 줄 이름표 칸이 아닌 곳으로 벗어나면 닫는다.
            _refsPopup = new VisualElement();
            _refsPopup.AddToClassList("commit-graph-refs-popup");
            GitShadow.Add(_refsPopup, GitShadowSize.Large);
            _refsPopup.SetVisible(false);
            _refsPopup.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (_refsPopupCell == null || _refsPopupCell.worldBound.Contains(evt.position) == false)
                {
                    HideRefsPopup();
                }
            });
            _refsPopup.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && _refsPopupIndex >= 0)
                {
                    _list.SetSelection(_refsPopupIndex);
                }
            });
            _refsPopup.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (_refsPopupIndex >= 0 && _refsPopupIndex < _items.Count && _items[_refsPopupIndex] is GitGraphNode node)
                {
                    _list.SetSelection(_refsPopupIndex);
                    ContextMenuRequested?.Invoke(evt.menu, node);
                }
            }));
            body.Add(_refsPopup);

            // 목록을 스크롤하거나 크기가 바뀌면 선을 다시 그린다. ListView 는 생성자에서 안쪽 ScrollView 를 만든다.
            _list.RegisterCallback<GeometryChangedEvent>(_ => Redraw());
            // 펼친 참조 목록은 줄을 따라가지 않으므로 스크롤하면 닫는다.
            _list.Q<ScrollView>().verticalScroller.valueChanged += _ =>
            {
                HideRefsPopup();
                Redraw();
            };
            if (_avatars != null)
            {
                // 아바타는 받는 대로 나타난다. 패널에서 떨어지면 구독을 끊는다.
                Root.RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += Redraw);
                Root.RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= Redraw);
            }

            RebuildHeader();
            ApplyColumnWidths();
        }

        private bool AvatarsEnabled => _avatars != null && _avatars.IsEnabled;

        private void Redraw()
        {
            // 설정에서 아바타를 켜고 끄면 열 간격이 달라지므로 배치부터 다시 한다.
            if (_layout.HasAvatars != AvatarsEnabled && Graph != null)
            {
                SetGraph(Graph, _wip);
                return;
            }
            // 브랜치/태그 칸이 맨 오른쪽이면 창 크기에 따라 폭이 바뀐다.
            UpdateCompactRefs();
            _overlay.MarkDirtyRepaint();
            PlaceAvatars();
            PlaceBranchInput();
        }

        public GitGraph Graph { get; private set; }

        /// <param name="wip">작업 중인 변경 개수. 하나도 없으면 WIP 줄을 숨긴다.</param>
        public void SetGraph(GitGraph graph, WipCounts wip)
        {
            string selectedHash = SelectedNode?.Commit.Hash;
            bool wipSelected = _list.selectedItem is WipItem;

            HideRefsPopup();
            Graph = graph;
            _wip = wip;
            _layout = VerticalGraphLayout.Build(graph, wip.Total > 0, AvatarsEnabled);
            _items = new List<object>();
            if (_layout.HasWip)
            {
                _items.Add(Wip);
            }
            _items.AddRange(_layout.NodesNewestFirst);
            _list.itemsSource = _items;
            _list.RefreshItems();

            ApplyColumnWidths();

            // 새로 읽어도 고른 줄을 잃지 않는다(알림은 보내지 않는다 — 사용자가 바꾼 선택이 아니다).
            int index = wipSelected && _layout.HasWip ? 0 : _items.FindIndex(item => item is GitGraphNode node && node.Commit.Hash == selectedHash);
            if (index >= 0)
            {
                _list.SetSelectionWithoutNotify(new[] { index });
            }
            else
            {
                _list.ClearSelection();
            }
            _overlay.MarkDirtyRepaint();
            PlaceAvatars();
            PlaceBranchInput();
        }

        /// <summary>
        /// 그래프 칸 폭: 사용자가 끌어 정한 폭, 없으면 가지 수에 맞춘 폭.
        /// 끌어 정한 폭도 <see cref="MinGraphWidth"/> 보다 좁게는 두지 않는다(아바타를 켜거나 가지 수가 바뀌면 달라진다).
        /// </summary>
        private float GraphWidth => _graphWidthOverride > 0f ? Mathf.Max(_graphWidthOverride, MinGraphWidth) : _layout.Width;

        /// <summary>
        /// 그래프 칸을 줄일 수 있는 가장 좁은 폭.
        /// 가지가 하나면 원을 제자리(<see cref="VerticalGraphLayout.MaxFirstLaneX"/>)에 둔 채 원 오른쪽 + 틈 + 막대까지(GitKraken 과 같다),
        /// 여럿이면 첫 열 원이 여백을 줄인 자리에서 다 보이는 폭까지(그 아래로는 한 줄로 접힌다).
        /// </summary>
        private float MinGraphWidth => _layout.LaneCount == 1
            ? _layout.MaxFirstLaneX + _layout.NodeOuterRadius + NodeBarGap + BarWidth
            : _layout.OneLaneWidth;

        private float ClampRefsWidth(float width)
        {
            return Mathf.Clamp(width, MinRefsWidth, MaxRefsColumnWidth);
        }

        /// <summary>
        /// 그래프 칸은 커밋 원(HEAD 고리까지)이 다 보이는 폭(<see cref="MinGraphWidth"/>)까지만 줄인다.
        /// </summary>
        private float ClampGraphWidth(float width)
        {
            return Mathf.Clamp(width, MinGraphWidth, MaxGraphColumnWidth);
        }

        private static float ClampMessageWidth(float width)
        {
            return Mathf.Clamp(width, MinMessageColumnWidth, MaxMessageColumnWidth);
        }

        /// <summary>
        /// 저장된 칸 순서. 세 칸이 한 번씩 들어 있지 않으면(처음·깨진 값) 기본 순서.
        /// </summary>
        private static CommitGraphColumn[] ParseOrder(string saved)
        {
            string[] parts = saved.Split(',');
            if (parts.Length != DefaultOrder.Length)
            {
                return (CommitGraphColumn[])DefaultOrder.Clone();
            }
            CommitGraphColumn[] order = new CommitGraphColumn[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (int.TryParse(parts[i], out int value) == false || Enum.IsDefined(typeof(CommitGraphColumn), value) == false)
                {
                    return (CommitGraphColumn[])DefaultOrder.Clone();
                }
                order[i] = (CommitGraphColumn)value;
            }
            return order.Distinct().Count() == order.Length ? order : (CommitGraphColumn[])DefaultOrder.Clone();
        }

        /// <summary>
        /// 머리 칸을 끌어 놓았을 때: <paramref name="from"/> 자리의 칸을 <paramref name="to"/> 자리로 옮기고 저장한다.
        /// </summary>
        private void MoveColumn(int from, int to)
        {
            List<CommitGraphColumn> order = _order.ToList();
            CommitGraphColumn column = order[from];
            order.RemoveAt(from);
            order.Insert(Mathf.Clamp(to, 0, order.Count), column);
            _order = order.ToArray();
            HideRefsPopup();
            GitVcsSettings.instance.GraphColumnOrder = string.Join(",", _order.Select(c => ((int)c).ToString()));

            RebuildHeader();
            _list.Query<GraphRow>().ForEach(ArrangeCells);
            // 맨 왼쪽이 되면 이름표 칸 여백이 바뀌므로 폭이 같아도 이름표 모양을 다시 고른다.
            _fittedRefsWidth = -1f;
            ApplyColumnWidths();
            _overlay.MarkDirtyRepaint();
            PlaceAvatars();
            PlaceBranchInput();
        }

        /// <summary>
        /// 머리 줄을 지금 순서로 다시 놓는다. 경계 손잡이는 맨 오른쪽이 아닌 칸 뒤에만 둔다(맨 오른쪽 칸은 남은 폭).
        /// </summary>
        private void RebuildHeader()
        {
            _header.Clear();
            for (int i = 0; i < _order.Length; i++)
            {
                // 맨 왼쪽 칸은 머리 글자·내용을 더 들여 쓴다(GitKraken 과 같다 — 목록 왼쪽 끝에 붙지 않게).
                _headerCells[(int)_order[i]].EnableInClassList(FirstHeaderModifier, i == 0);
                Root.EnableInClassList(FirstColumnModifiers[(int)_order[i]], i == 0);
                _header.Add(_headerCells[(int)_order[i]]);
                if (i < _order.Length - 1)
                {
                    _header.Add(_resizers[(int)_order[i]].Handle);
                }
            }
            if (_headerDetails != null)
            {
                _header.Add(_headerDetails);
            }
        }

        private static Label DetailHeaderCell(string textKey, float width)
        {
            Label cell = new(GitText.Get(textKey));
            cell.AddToClassList("list-header__cell");
            cell.AddToClassList("commit-graph__detail-cell");
            cell.style.width = width;
            return cell;
        }

        private static Label DetailRowCell(string className, float width)
        {
            Label cell = new();
            cell.AddToClassList("commit-graph-row__detail");
            cell.AddToClassList(className);
            cell.AddToClassList("ellipsis");
            cell.style.width = width;
            return cell;
        }

        /// <summary>
        /// 줄의 칸 요소를 지금 순서로 늘어놓는다.
        /// </summary>
        private void ArrangeCells(GraphRow row)
        {
            foreach (CommitGraphColumn column in _order)
            {
                row.Cell(column).BringToFront();
            }
            row.Details?.BringToFront();
        }

        private bool IsLast(CommitGraphColumn column)
        {
            return _order[^1] == column;
        }

        /// <summary>
        /// 맨 오른쪽이 아닐 때의 칸 폭.
        /// </summary>
        private float FixedWidth(CommitGraphColumn column)
        {
            return column switch
            {
                CommitGraphColumn.Refs => RefsWidth,
                CommitGraphColumn.Graph => GraphWidth,
                _ => _messageWidth,
            };
        }

        /// <summary>
        /// 줄 왼쪽 끝에서 그 칸 왼쪽 끝까지. 앞 칸은 모두 맨 오른쪽이 아니므로 정해진 폭을 더하면 된다.
        /// </summary>
        private float ColumnStart(CommitGraphColumn column)
        {
            float x = 0f;
            foreach (CommitGraphColumn other in _order)
            {
                if (other == column)
                {
                    break;
                }
                x += FixedWidth(other);
            }
            return x;
        }

        /// <summary>
        /// 실제 칸 폭. 맨 오른쪽 칸은 줄의 남은 폭(아직 배치 전이면 정해진 폭).
        /// </summary>
        private float ColumnWidth(CommitGraphColumn column)
        {
            if (IsLast(column) == false)
            {
                return FixedWidth(column);
            }
            float bodyWidth = _body.contentRect.width;
            return float.IsNaN(bodyWidth) || bodyWidth <= 0f ? FixedWidth(column) : Mathf.Max(bodyWidth - ColumnStart(column) - DetailsWidth, MinLastColumnWidth);
        }

        /// <summary>
        /// 이름표 칸이 그래프 칸 바로 왼쪽인지. 이때만 이름표에서 커밋까지 선을 잇는다(GitKraken 과 같다 —
        /// 그래프 오른쪽이나 떨어진 자리면 가지 색 막대가 줄을 이어 주므로 선을 긋지 않는다).
        /// </summary>
        private bool RefsLeftOfGraph => Array.IndexOf(_order, CommitGraphColumn.Refs) == Array.IndexOf(_order, CommitGraphColumn.Graph) - 1;

        /// <param name="graphWidthOverride">0 이면 그래프 칸을 가지 수에 맞춘다.</param>
        private void SetColumnWidths(float refsWidth, float graphWidthOverride, float messageWidth)
        {
            _refsWidth = refsWidth;
            _graphWidthOverride = graphWidthOverride;
            _messageWidth = messageWidth;
            HideRefsPopup();
            ApplyColumnWidths();
            _overlay.MarkDirtyRepaint();
            PlaceAvatars();
            PlaceBranchInput();
        }

        /// <summary>
        /// 머리 줄·선 층·보이는 줄에 칸 폭을 넣는다. 새로 묶이는 줄은 <see cref="BindRow"/> 가 넣는다.
        /// </summary>
        private void ApplyColumnWidths()
        {
            foreach (CommitGraphColumn column in _order)
            {
                ApplyCellWidth(_headerCells[(int)column], column);
            }
            _list.Query<GraphRow>().ForEach(ApplyCellWidths);
            UpdateCompactRefs();

            // 선·원 층은 그래프 칸과, 바로 왼쪽 이름표 칸까지 덮는다(이름표에서 커밋까지 잇는 선을 그리려고).
            // 맨 오른쪽 칸까지 덮으면 오른쪽 끝에 붙인다. 칸보다 넓은 가지는 여기서 잘린다.
            float graphStart = ColumnStart(CommitGraphColumn.Graph);
            float spanStart = RefsLeftOfGraph ? ColumnStart(CommitGraphColumn.Refs) : graphStart;
            const CommitGraphColumn spanEnd = CommitGraphColumn.Graph;
            _graphOffset = graphStart - spanStart;
            foreach (VisualElement layer in new[] { _overlay, _avatarHost })
            {
                layer.style.left = spanStart;
                if (IsLast(spanEnd))
                {
                    layer.style.width = StyleKeyword.Null;
                    layer.style.right = DetailsWidth;
                }
                else
                {
                    layer.style.width = ColumnStart(spanEnd) + FixedWidth(spanEnd) - spanStart;
                    layer.style.right = StyleKeyword.Null;
                }
            }
        }

        /// <summary>
        /// 좁은 머리 칸은 글자 대신 아이콘으로 바꾸고, 브랜치/태그 칸 폭이 바뀌었으면 보이는 줄마다 이름표 모양을 다시 고른다(<see cref="FitRefs"/>).
        /// </summary>
        private void UpdateCompactRefs()
        {
            // 커밋 원 자리는 그래프 칸 폭에 따라 움직이므로(가운데 맞춤) 띠 시작점도 따라 옮긴다.
            // 막대 폭(맨 오른쪽이면 0)까지 뺀 폭으로 재서 칸 순서가 바뀌어도 다시 놓는다.
            float graphWidth = ColumnWidth(CommitGraphColumn.Graph) - BarWidth;
            if (Mathf.Approximately(graphWidth, _placedGraphWidth) == false)
            {
                _placedGraphWidth = graphWidth;
                _list.Query<GraphRow>().ForEach(PlaceBand);
            }

            UpdateIconHeader(CommitGraphColumn.Refs);
            UpdateIconHeader(CommitGraphColumn.Graph);
            UpdateIconHeader(CommitGraphColumn.Message);
            // 맨 왼쪽 브랜치/태그 칸만: 머리가 아이콘이 될 만큼 좁으면 머리 아이콘·이름표를 칸 가운데에 둔다(모양은 USS).
            // 다른 자리에서는 왼쪽 여백에 그대로 둔다(글자 자리에서 아이콘으로 바뀌어 보이게).
            bool centered = _order[0] == CommitGraphColumn.Refs && _headerCells[(int)CommitGraphColumn.Refs].ClassListContains(IconHeaderModifier);
            Root.EnableInClassList(CenteredRefsModifier, centered);
            // 그래프가 맨 오른쪽이면 막대가 가를 다음 칸이 없으므로 띠만 칸 끝까지 깐다(GitKraken 과 같다).
            Root.EnableInClassList(GraphLastModifier, IsLast(CommitGraphColumn.Graph));

            float width = ColumnWidth(CommitGraphColumn.Refs);
            if (Mathf.Approximately(width, _fittedRefsWidth))
            {
                return;
            }
            _fittedRefsWidth = width;
            _list.Query<GraphRow>().ForEach(FitRefs);
        }

        /// <summary>
        /// 지금 칸 순서·가지 수에서 그 칸을 줄일 수 있는 가장 좁은 폭. 맨 오른쪽 칸은 남은 폭을 갖고 <see cref="MinLastColumnWidth"/> 까지 준다.
        /// </summary>
        private float MinColumnWidth(CommitGraphColumn column)
        {
            if (IsLast(column))
            {
                return MinLastColumnWidth;
            }
            return column switch
            {
                CommitGraphColumn.Refs => MinRefsWidth,
                CommitGraphColumn.Graph => MinGraphWidth,
                _ => MinMessageColumnWidth,
            };
        }

        private void UpdateIconHeader(CommitGraphColumn column)
        {
            Label header = _headerCells[(int)column];
            // GitKraken 처럼 칸을 끝까지 줄였을 때(최소 폭에 닿았을 때)만 아이콘으로 바꾼다. 그 전까지는 글자가 말줄임으로 줄어든다
            // (그래프는 아바타가 막대에 붙을 때, 메시지·브랜치/태그는 각자 최소 폭에서).
            bool icon = ColumnWidth(column) <= MinColumnWidth(column) + AtMinimumTolerance;
            if (icon == header.ClassListContains(IconHeaderModifier))
            {
                return;
            }
            header.EnableInClassList(IconHeaderModifier, icon);
            string title = GitText.Get(HeaderTextKeys[(int)column]);
            header.text = icon ? string.Empty : title;
            header.tooltip = icon ? title : null;
        }

        /// <summary>
        /// 가지 색 띠를 이 줄 커밋 원의 가운데에서 시작하게 놓는다(그래프 칸 왼쪽의 빈 자리와 왼쪽 가지 위에는 깔지 않는다).
        /// 작업 중 줄은 HEAD 열의 빈 원(<see cref="DrawWip"/>)에서 시작한다.
        /// </summary>
        private void PlaceBand(GraphRow row)
        {
            int laneRow = row.LaneRow >= 0 ? row.LaneRow : HeadNode?.Lane.Row ?? 0;
            row.Band.style.left = X(laneRow) - _graphOffset;
        }

        private GitGraphNode HeadNode => _layout.NodesNewestFirst.FirstOrDefault(node => node.IsHead);

        /// <summary>
        /// 띠 색: 칸에 옅게 깐 가지 색, 끝 막대, 고른 줄에서 덮는 진한 가지 색.
        /// </summary>
        private static void PaintBand(GraphRow row, Color laneColor)
        {
            Color tint = laneColor;
            tint.a = LaneTintAlpha;
            Color glow = laneColor;
            glow.a = SelectedLaneTintAlpha;
            row.Band.style.backgroundColor = tint;
            row.Band.style.borderRightColor = laneColor;
            row.BandGlow.style.backgroundColor = glow;
        }

        /// <summary>
        /// 이름이 첫 글자와 "…" 보다 좁아질 만큼 칸이 좁으면 이름을 빼고 아이콘만 남긴다(--no-name).
        /// 그보다 좁아지면 이름표는 그대로 두고 칸 경계가 가린다. 숨긴 이름·참조는 마우스를 올리면 펼치는 목록으로 본다.
        /// </summary>
        private void FitRefs(GraphRow row)
        {
            if (row.RefCount == 0)
            {
                return;
            }
            VisualElement pill = row.Refs[0];
            Label more = (Label)row.Refs[1];
            float inner = ColumnWidth(CommitGraphColumn.Refs) - RefsCellPadding;
            float moreWidth = row.RefCount > 1 ? MoreGap + MorePadding + MoreTextWidth(more) : 0f;
            bool showName = inner - moreWidth >= PillChrome(row.RefKind, row.RefHasRemote) + MinRefNameWidth;
            pill.EnableInClassList("commit-graph-ref--no-name", showName == false);
        }

        /// <summary>
        /// 이름을 뺀 이름표 폭: 테두리 2 + 왼쪽 여백 + (현재 브랜치) 체크 18 + 간격 5 + 종류 아이콘 16 + (원격도 있으면) 간격 3 + 구름 16 + 오른쪽 여백 8.
        /// </summary>
        private static float PillChrome(CommitRefKind kind, bool hasRemote)
        {
            float remote = hasRemote ? 3f + 16f : 0f;
            return (kind == CommitRefKind.Current ? 2f + 3f + 18f + 5f + 16f + 8f : 2f + 8f + 5f + 16f + 8f) + remote;
        }

        /// <summary>
        /// "+n" 글자 폭. 아직 패널에 붙지 않아 잴 수 없으면 글자 수로 어림한다(11px 글꼴의 숫자 폭).
        /// </summary>
        private static float MoreTextWidth(Label more)
        {
            Vector2 size = more.MeasureTextSize(more.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            return float.IsNaN(size.x) || size.x <= 0f ? more.text.Length * 7f : size.x;
        }

        private void ApplyCellWidths(GraphRow row)
        {
            ApplyCellWidth(row.Refs, CommitGraphColumn.Refs);
            ApplyCellWidth(row.Graph, CommitGraphColumn.Graph);
            ApplyCellWidth(row.Message, CommitGraphColumn.Message);
        }

        /// <summary>
        /// 맨 오른쪽 칸은 남은 폭을 채우고, 나머지는 정해진 폭으로 고정한다.
        /// </summary>
        private void ApplyCellWidth(VisualElement cell, CommitGraphColumn column)
        {
            if (IsLast(column))
            {
                cell.style.width = StyleKeyword.Null;
                cell.style.minWidth = MinLastColumnWidth;
                cell.style.flexGrow = 1f;
                cell.style.flexShrink = 1f;
                cell.style.flexBasis = 0f;
                return;
            }
            float width = FixedWidth(column);
            cell.style.width = width;
            cell.style.minWidth = width;
            cell.style.flexGrow = 0f;
            cell.style.flexShrink = 0f;
            cell.style.flexBasis = StyleKeyword.Auto;
        }

        /// <summary>
        /// 보이는 커밋마다 아바타(없으면 작성자 색 위 머리글자)를 원 자리에 놓는다. 병합 커밋은 작은 빈 원 그대로 둔다(GitKraken 과 같다).
        /// </summary>
        private void PlaceAvatars()
        {
            _avatarLayer.Begin();
            if (_layout.HasAvatars && Graph != null)
            {
                float scrollY = ScrollY;
                float height = _overlay.contentRect.height;
                const float radius = VerticalGraphLayout.AvatarRadius;
                foreach (GitGraphNode node in _layout.NodesNewestFirst)
                {
                    float y = VerticalGraphLayout.RowCenterY(_layout.RowOf[node], scrollY);
                    if (node.Commit.IsMerge || y < -VerticalGraphLayout.RowHeight || y > height + VerticalGraphLayout.RowHeight)
                    {
                        continue;
                    }
                    Vector2 center = new(X(node.Lane.Row), y);
                    Texture2D avatar = _avatars.TryGet(node.Commit.AuthorEmail, node.Commit.Hash);
                    if (avatar != null)
                    {
                        _avatarLayer.PlaceCircleImage("commit-graph__avatar", avatar, center, radius);
                        continue;
                    }
                    _avatarLayer.Place("commit-graph__initials", GitDisplay.Initials(node.Commit.AuthorName),
                        center - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f), 10f);
                }
            }
            _avatarLayer.End();
        }

        public GitGraphNode SelectedNode => _list.selectedItem as GitGraphNode;

        public void SelectWip()
        {
            if (_layout.HasWip)
            {
                _list.SetSelection(0);
            }
        }

        /// <summary>
        /// 커밋 줄을 골라 보이는 곳으로 옮긴다(커밋 상세의 "parent" 링크, 메뉴의 "부모 커밋으로 이동"). 그래프에 없으면 false.
        /// </summary>
        public bool SelectCommit(string hash)
        {
            int index = IndexOf(hash);
            if (index < 0)
            {
                return false;
            }
            _list.SetSelection(index);
            _list.ScrollToItem(index);
            return true;
        }

        /// <summary>
        /// 커밋 줄 번호(작업 중 줄이 있으면 1부터). 그래프에 없으면 -1.
        /// </summary>
        private int IndexOf(string hash)
        {
            return _items.FindIndex(item => item is GitGraphNode node && node.Commit.Hash == hash);
        }

        /// <summary>
        /// 그 커밋 줄의 브랜치/태그 칸에 새 브랜치 이름 입력란을 띄운다(GitKraken 의 브랜치 만들기).
        /// Enter 면 다듬은 이름을 <paramref name="submit"/> 에 넘기고, Esc 나 다른 곳을 누르면 그냥 닫는다.
        /// </summary>
        /// <returns>그 커밋이 그래프에 없으면 false.</returns>
        public bool BeginBranchInput(string hash, Action<string> submit)
        {
            int index = IndexOf(hash);
            if (index < 0)
            {
                return false;
            }
            HideRefsPopup();
            _branchInputHash = hash;
            _branchInputSubmit = submit;
            _branchInput.SetValueWithoutNotify(string.Empty);
            ValidateBranchInput();
            _branchInput.SetVisible(true);
            _list.ScrollToItem(index);
            PlaceBranchInput();
            // 이 줄의 이름표 연결선은 입력란이 대신한다.
            _overlay.MarkDirtyRepaint();
            // 보이게 한 다음 프레임에 포커스해야 글자 입력이 바로 들어간다.
            _branchInput.schedule.Execute(() =>
            {
                if (_branchInputHash != null)
                {
                    _branchInput.Focus();
                }
            });
            return true;
        }

        /// <summary>
        /// 입력란을 그 커밋 줄의 브랜치/태그 칸 안에 맞춘다. 스크롤·칸 폭·다시 읽기(작업 중 줄이 생기고 없어지면 줄이 밀린다)마다 부른다.
        /// GitKraken 처럼 칸 밖으로 넘치지 않고 칸 폭을 따라 줄어든다(좁으면 글자가 옆으로 밀려 보인다).
        /// </summary>
        private void PlaceBranchInput()
        {
            if (_branchInputHash == null)
            {
                return;
            }
            int index = IndexOf(_branchInputHash);
            if (index < 0)
            {
                CloseBranchInput();
                return;
            }
            const float inset = 4f;
            _branchInput.style.top = VerticalGraphLayout.RowCenterY(index, ScrollY) - VerticalGraphLayout.RowHeight * 0.5f + inset;
            _branchInput.style.height = VerticalGraphLayout.RowHeight - inset * 2f;
            _branchInput.style.left = ColumnStart(CommitGraphColumn.Refs) + BranchInputMargin;
            _branchInput.style.width = Mathf.Max(ColumnWidth(CommitGraphColumn.Refs) - BranchInputMargin * 2f, MinBranchInputWidth);
        }

        /// <summary>
        /// 쓰는 동안 이름을 검사한다(<see cref="BranchNameValidator"/>). 문제가 있으면 칸을 빨갛게 하고 툴팁에 까닭을 단다.
        /// </summary>
        public Func<string, string> BranchNameValidator { get; set; }

        private string BranchInputProblem()
        {
            string value = _branchInput.value?.Trim();
            return string.IsNullOrEmpty(value) ? null : BranchNameValidator?.Invoke(value);
        }

        private void ValidateBranchInput()
        {
            string problem = BranchInputProblem();
            _branchInput.EnableInClassList("commit-graph__branch-input--invalid", problem != null);
            _branchInput.tooltip = problem;
        }

        private void OnBranchInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
            {
                evt.StopPropagation();
                // 잘못된 이름이면 닫지 않고 쓴 글을 그대로 둔다(빨간 테두리·툴팁으로 까닭을 보인다).
                if (BranchInputProblem() != null)
                {
                    ValidateBranchInput();
                    return;
                }
                string name = _branchInput.value?.Trim();
                Action<string> submit = _branchInputSubmit;
                CloseBranchInput();
                if (string.IsNullOrEmpty(name) == false)
                {
                    submit?.Invoke(name);
                }
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                CloseBranchInput();
            }
        }

        private void CloseBranchInput()
        {
            if (_branchInputHash == null)
            {
                return;
            }
            // 숨기면 포커스를 잃어 FocusOut 이 다시 부르므로 상태부터 비운다.
            _branchInputHash = null;
            _branchInputSubmit = null;
            _branchInput.SetVisible(false);
            _overlay.MarkDirtyRepaint();
        }

        private static Label HeaderCell(string text)
        {
            Label label = new(text);
            label.AddToClassList("list-header__cell");
            label.AddToClassList("commit-graph__header-cell");
            return label;
        }

        private void AddHeaderGlyph(CommitGraphColumn column, ActionGlyphKind kind)
        {
            ActionGlyph glyph = new(kind);
            glyph.AddToClassList("commit-graph__header-glyph");
            _headerCells[(int)column].Add(glyph);
        }

        private void OnSelectionChanged()
        {
            object item = _list.selectedItem;
            if (item is WipItem)
            {
                WipSelected?.Invoke();
            }
            else if (item is GitGraphNode node)
            {
                CommitSelected?.Invoke(node);
            }
        }

        private VisualElement MakeRow()
        {
            GraphRow row = new();
            row.AddToClassList("commit-graph-row");

            VisualElement refs = new();
            refs.AddToClassList("commit-graph-row__refs");
            VisualElement pill = CreateRefPill();
            refs.Add(pill);
            Label more = new();
            more.AddToClassList("commit-graph-ref__more");
            refs.Add(more);
            row.Add(refs);
            // 이름표 폭이 바뀌면(다른 커밋에 다시 묶이면) 이름표에서 시작하는 선도 다시 그린다.
            pill.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());
            more.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());
            // 이름표를 두 번 누르면 그 참조(줄에 보이는 첫 참조)로 전환한다.
            pill.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount == 2 && row.userData is int index && index < _items.Count && _items[index] is GitGraphNode node)
                {
                    IReadOnlyList<CommitRef> refs = CommitRefs.Of(node);
                    if (refs.Count > 0)
                    {
                        RefActivated?.Invoke(node, refs[0]);
                    }
                }
            });
            // 이름표 칸에 마우스를 올리면 이 커밋의 참조를 모두 펼쳐 보인다(GitKraken 과 같다). 펼친 목록으로 옮겨 가면 닫지 않는다.
            refs.RegisterCallback<PointerEnterEvent>(_ => ShowRefsPopup(row));
            refs.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (_refsPopupCell == refs && _refsPopup.worldBound.Contains(evt.position) == false)
                {
                    HideRefsPopup();
                }
            });

            VisualElement graph = new();
            graph.AddToClassList("commit-graph-row__graph");
            // ListView 가 항목 전체에 칠하는 고른 줄·마우스 바탕은 끈다(인라인이라 테마 USS 보다 앞선다). GitKraken 처럼 고른 줄은
            // 그래프 칸의 가지 색 띠를 진하게(BandGlow), 메시지 칸에만 바탕(Highlight)을 깐다 — 브랜치/태그 칸은 칠하지 않는다.
            // 항목 클래스는 줄 자체나 감싸는 요소에 붙는다(ListView 설정에 따라 다르다).
            row.RegisterCallback<AttachToPanelEvent>(_ =>
            {
                VisualElement item = row.ClassListContains(BaseVerticalCollectionView.itemUssClassName) ? row : row.parent;
                item.style.backgroundColor = Color.clear;
            });
            VisualElement band = new() { pickingMode = PickingMode.Ignore };
            band.AddToClassList("commit-graph-row__band");
            // 고른 줄의 띠를 진하게 덮는 막. 색(가지 색)은 BindRow 가, 보이고 숨기는 것은 USS(--selected).
            VisualElement bandGlow = new() { pickingMode = PickingMode.Ignore };
            bandGlow.AddToClassList("commit-graph-row__band-glow");
            band.Add(bandGlow);
            graph.Add(band);
            row.Band = band;
            row.BandGlow = bandGlow;
            row.Add(graph);

            VisualElement message = new();
            message.AddToClassList("commit-graph-row__message");
            // 고른 줄·마우스가 올라간 줄의 메시지 칸 바탕. 보이고 숨기는 것과 색은 USS(--selected·:hover).
            VisualElement highlight = new() { pickingMode = PickingMode.Ignore };
            highlight.AddToClassList("commit-graph-row__highlight");
            message.Add(highlight);
            row.Highlight = highlight;
            Label subject = new();
            subject.AddToClassList("commit-row__subject");
            subject.AddToClassList("ellipsis");
            message.Add(subject);
            row.Add(message);

            row.Refs = refs;
            row.Graph = graph;
            row.Message = message;
            if (_detailColumns)
            {
                VisualElement details = new();
                details.AddToClassList("commit-graph-row__details");
                row.Author = DetailRowCell("commit-graph-row__author", AuthorColumnWidth);
                row.Hash = DetailRowCell("commit-graph-row__hash", HashColumnWidth);
                row.Date = DetailRowCell("commit-graph-row__date", DateColumnWidth);
                details.Add(row.Author);
                details.Add(row.Hash);
                details.Add(row.Date);
                row.Add(details);
                row.Details = details;
            }
            ArrangeCells(row);
            ApplyCellWidths(row);

            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                if (row.userData is int index && index < _items.Count && _items[index] is GitGraphNode node)
                {
                    _list.SetSelection(index);
                    ContextMenuRequested?.Invoke(evt.menu, node);
                }
            }));
            return row;
        }

        private void BindRow(VisualElement element, int index)
        {
            GraphRow row = (GraphRow)element;
            row.userData = index;
            ApplyCellWidths(row);
            VisualElement refsCell = row.Refs;
            VisualElement pill = refsCell[0];
            Label more = (Label)refsCell[1];
            VisualElement message = row.Message;
            Label subject = message.Q<Label>(className: "commit-row__subject");

            bool isWip = _items[index] is WipItem;
            row.EnableInClassList("commit-graph-row--wip", isWip);
            row.WipEditor?.SetVisible(isWip);
            row.WipCountsRoot?.SetVisible(isWip);
            // 작업 중 줄은 제목 글자 대신 입력란·변경 개수를 보인다.
            subject.SetVisible(isWip == false);
            if (isWip)
            {
                _wipRow = row;
                EnsureWipEditor(row);
                if (_wipEditing == false)
                {
                    row.WipInput.SetValueWithoutNotify(_wipSubject);
                }
                row.RefCount = 0;
                row.LaneRow = -1;
                BindRefs(pill, more, Array.Empty<CommitRef>(), Color.clear);
                // GitKraken 처럼 작업 중 줄도 HEAD 가지 색 띠를 깐다.
                GitGraphNode head = HeadNode;
                if (head != null)
                {
                    PaintBand(row, GraphStyle.LaneColor(head.Lane));
                }
                else
                {
                    row.Band.style.backgroundColor = StyleKeyword.Null;
                    row.Band.style.borderRightColor = StyleKeyword.Null;
                    row.BandGlow.style.backgroundColor = StyleKeyword.Null;
                }
                PlaceBand(row);
                BindWipCounts(row);
                subject.text = string.Empty;
                subject.tooltip = null;
                BindDetails(row, null);
                return;
            }
            if (_wipRow == row)
            {
                _wipRow = null;
            }
            // 줄 요소가 다른 커밋에 다시 묶이면 쓰는 중 표시를 남기지 않는다.
            row.RemoveFromClassList("commit-graph-row--wip-editing");

            GitGraphNode node = (GitGraphNode)_items[index];
            GitCommitInfo commit = node.Commit;
            Color laneColor = GraphStyle.LaneColor(node.Lane);
            IReadOnlyList<CommitRef> refs = CommitRefs.Of(node);
            BindRefs(pill, more, refs, laneColor);
            row.RefCount = refs.Count;
            row.RefKind = refs.Count > 0 ? refs[0].Kind : CommitRefKind.Local;
            row.RefHasRemote = refs.Count > 0 && refs[0].HasRemote;
            FitRefs(row);
            // 가지 색 띠: 커밋 원 자리부터 그래프 칸 오른쪽 끝까지 옅게 깔고 끝에 막대를 둬서 줄마다 어느 가지인지 보이게 한다.
            // 막대는 칸 순서와 상관없이 그래프 칸에 붙는다(GitKraken 과 같다 — 기본 순서에서는 메시지 바로 앞).
            row.LaneRow = node.Lane.Row;
            PaintBand(row, laneColor);
            PlaceBand(row);
            subject.text = commit.Subject;
            subject.tooltip = $"{commit.ShortHash}  {commit.Subject}\n{commit.AuthorName}  ·  {GitDisplay.FormatDate(commit.Date)}";
            BindDetails(row, commit);
        }

        /// <summary>
        /// Fork 형태의 작성자 · SHA · 날짜 칸. 작업 중 줄(null)은 비운다.
        /// </summary>
        private static void BindDetails(GraphRow row, GitCommitInfo commit)
        {
            if (row.Details == null)
            {
                return;
            }
            row.Author.text = commit == null ? string.Empty : GitDisplay.Nfc(commit.AuthorName);
            row.Author.tooltip = commit?.AuthorEmail;
            row.Hash.text = commit?.ShortHash ?? string.Empty;
            row.Date.text = commit == null ? string.Empty : GitDisplay.FormatDate(commit.Date);
        }

        /// <summary>
        /// 입력란 뒤의 변경 개수: [연필 아이콘]3 [+ 아이콘]8 [− 아이콘]170(GitKraken 과 같다). 개수가 0 인 종류는 뺀다.
        /// </summary>
        private void BindWipCounts(GraphRow row)
        {
            int[] counts = { _wip.Modified, _wip.Added, _wip.Deleted };
            for (int i = 0; i < counts.Length; i++)
            {
                row.WipCounts[i].parent.SetVisible(counts[i] > 0);
                row.WipCounts[i].text = counts[i].ToString();
            }
        }

        /// <summary>
        /// 변경 개수 묶음 = 종류마다 [아이콘][숫자]. 아이콘 그림·색은 USS(--modified 는 원래 색, --added·--deleted 는 테마 색으로 물들인다).
        /// </summary>
        private static VisualElement MakeWipCounts(out Label[] labels)
        {
            VisualElement counts = new() { pickingMode = PickingMode.Ignore };
            counts.AddToClassList("commit-graph-wip__counts");
            string[] kinds = { "modified", "added", "deleted" };
            labels = new Label[kinds.Length];
            for (int i = 0; i < kinds.Length; i++)
            {
                VisualElement group = new() { pickingMode = PickingMode.Ignore };
                group.AddToClassList("commit-graph-wip__count");
                VisualElement icon = new() { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("commit-graph-wip__count-icon");
                icon.AddToClassList("commit-graph-wip__count-icon--" + kinds[i]);
                group.Add(icon);
                Label number = new() { pickingMode = PickingMode.Ignore };
                number.AddToClassList("commit-graph-wip__count-number");
                group.Add(number);
                counts.Add(group);
                labels[i] = number;
            }
            return counts;
        }

        /// <summary>
        /// 작업 중 줄 입력란의 글을 바꾼다(커밋 입력란에서 제목을 고쳤을 때). 입력란에서 쓰는 중이면 포커스를 잃을 때 맞춘다.
        /// </summary>
        public void SetWipSubject(string subject)
        {
            _wipSubject = subject ?? string.Empty;
            if (_wipEditing == false && _wipRow != null && _wipRow.WipInput.value != _wipSubject)
            {
                _wipRow.WipInput.SetValueWithoutNotify(_wipSubject);
            }
        }

        /// <summary>
        /// 작업 중 줄 메시지 칸 앞에 제목 입력란을 붙인다(GitKraken 과 같다 — 비어 있으면 "// 작업 중" 이 자리표시로 보인다).
        /// 쓴 글은 <see cref="WipSubjectEdited"/> 로 커밋 입력란에 넘긴다.
        /// </summary>
        private void EnsureWipEditor(GraphRow row)
        {
            if (row.WipEditor != null)
            {
                return;
            }
            VisualElement editor = new();
            editor.AddToClassList("commit-graph-wip");
            TextField input = new();
            input.AddToClassList("commit-graph-wip__input");
            input.textEdition.placeholder = GitText.Get("graph.wip");
            input.textEdition.hidePlaceholderOnFocus = false;
            editor.Add(input);
            ActionGlyph pencil = new(ActionGlyphKind.Pencil);
            pencil.AddToClassList("commit-graph-wip__pencil");
            editor.Add(pencil);
            row.Message.Insert(0, editor);
            VisualElement counts = MakeWipCounts(out row.WipCounts);
            row.Message.Insert(row.Message.IndexOf(editor) + 1, counts);
            // 고른 줄 바탕은 입력란보다 뒤에 그린다.
            row.Highlight.SendToBack();
            row.WipEditor = editor;
            row.WipCountsRoot = counts;
            row.WipInput = input;

            input.RegisterValueChangedCallback(evt =>
            {
                _wipSubject = evt.newValue ?? string.Empty;
                WipSubjectEdited?.Invoke(_wipSubject);
            });
            input.RegisterCallback<FocusInEvent>(_ =>
            {
                _wipEditing = true;
                editor.AddToClassList("commit-graph-wip--editing");
                // 쓰는 동안은 입력란을 칸 안에 맞춰 줄인다(뒤의 개수를 먼저 가린다). 그래야 입력란 안에서 글이 스크롤돼
                // 방금 쓴 글자가 칸 오른쪽 끝에 보인다(GitKraken 과 같다). 다 쓰면 다시 칸 경계가 가리는 모양으로 돌아간다.
                row.AddToClassList("commit-graph-row--wip-editing");
                // 입력란을 누르면 작업 중 줄을 고른 것으로 본다(오른쪽에 stage·커밋 패널).
                if (_list.selectedItem is not WipItem && _items.Count > 0 && _items[0] is WipItem)
                {
                    _list.SetSelection(0);
                }
            });
            input.RegisterCallback<FocusOutEvent>(_ =>
            {
                _wipEditing = false;
                editor.RemoveFromClassList("commit-graph-wip--editing");
                row.RemoveFromClassList("commit-graph-row--wip-editing");
                // 쓰는 동안 커밋 입력란 쪽에서 바뀐 값(템플릿을 다시 채우는 등)을 맞춘다.
                if (input.value != _wipSubject)
                {
                    input.SetValueWithoutNotify(_wipSubject);
                }
            });
            // 글을 쓰는 키가 목록까지 올라가 줄 선택을 옮기지 않게 한다(Home·End·화살표 등). Enter·Esc 는 입력을 마친다.
            input.RegisterCallback<KeyDownEvent>(evt =>
            {
                evt.StopPropagation();
                if (evt.keyCode is KeyCode.Return or KeyCode.KeypadEnter or KeyCode.Escape)
                {
                    _list.Focus();
                }
            });
            input.RegisterCallback<NavigationMoveEvent>(evt => evt.StopPropagation());
            input.RegisterCallback<NavigationSubmitEvent>(evt => evt.StopPropagation());
            input.RegisterCallback<NavigationCancelEvent>(evt => evt.StopPropagation());
        }

        /// <summary>
        /// 이름표 칸:첫 참조 하나를 이름표로, 나머지는 "+n" 으로. 현재 브랜치는 강조, 원격은 테두리만, 태그는 태그 색.
        /// </summary>
        private static void BindRefs(VisualElement pill, Label more, IReadOnlyList<CommitRef> refs, Color laneColor)
        {
            pill.SetVisible(refs.Count > 0);
            // "+n" 은 칸이 좁으면 FitRefs 가 클래스로 숨기므로 여기서도 인라인 display 대신 클래스로 숨긴다.
            more.EnableInClassList("commit-graph-ref__more--hidden", refs.Count <= 1);
            if (refs.Count == 0)
            {
                return;
            }
            BindRefPill(pill, refs[0], laneColor);
            // 숨은 이름·참조는 마우스를 올리면 펼치는 목록(ShowRefsPopup)으로 보므로 운영체제 툴팁은 두지 않는다.
            more.text = refs.Count > 1 ? $"+{refs.Count - 1}" : string.Empty;
            // "+n" 은 로컬 이름표와 같은 옅은 가지 색 상자(GitKraken 과 같다).
            Color moreFill = laneColor;
            moreFill.a = LocalRefAlpha;
            more.style.backgroundColor = moreFill;
        }

        /// <summary>
        /// 이름표 = [체크 표시][이름][종류 아이콘]. Label 에 자식을 넣으면 글자 폭을 재지 않아 잘리므로 감싸는 요소에 나란히 둔다.
        /// </summary>
        private static VisualElement CreateRefPill()
        {
            VisualElement pill = new();
            pill.AddToClassList("commit-graph-ref");
            // 현재 브랜치 체크 표시. 글자(✓)는 글꼴마다 모양이 달라 Painter2D 로 그린다. 보이고 숨기는 것은 USS(--current).
            ActionGlyph check = new(ActionGlyphKind.Check);
            check.AddToClassList("commit-graph-ref__check");
            pill.Add(check);
            Label name = new();
            name.AddToClassList("commit-graph-ref__name");
            name.AddToClassList("ellipsis");
            pill.Add(name);
            // 로컬에 있는 브랜치(현재·로컬) 이름 뒤의 노트북 표시(GitKraken 과 같다). 그림과 보이고 숨기는 것은 USS(--current·--local).
            VisualElement local = new() { pickingMode = PickingMode.Ignore };
            local.AddToClassList("commit-graph-ref__local");
            pill.Add(local);
            // 원격(구름)·태그 이름 뒤의 종류 아이콘. 노트북과 같은 자리라 이름을 뺀 좁은 칸에서도 무엇인지 알 수 있다.
            ActionGlyph remote = new(ActionGlyphKind.Cloud);
            remote.AddToClassList("commit-graph-ref__remote-icon");
            pill.Add(remote);
            ActionGlyph tag = new(ActionGlyphKind.Tag);
            tag.AddToClassList("commit-graph-ref__tag-icon");
            pill.Add(tag);
            // 마우스를 올리면 밝게(USS :hover). 이름표 바탕이 가지 색 인라인 스타일이라 USS 로 바꿀 수 없어 위에 얇은 막을 깐다.
            VisualElement veil = new() { pickingMode = PickingMode.Ignore };
            veil.AddToClassList("commit-graph-ref__veil");
            pill.Add(veil);
            // 커서(USS cursor)는 포인터 바로 밑 요소 것을 쓰므로 안쪽 요소는 포인터를 받지 않게 해 이름표 어디서나 손가락 커서가 되게 한다.
            check.pickingMode = PickingMode.Ignore;
            name.pickingMode = PickingMode.Ignore;
            remote.pickingMode = PickingMode.Ignore;
            tag.pickingMode = PickingMode.Ignore;
            return pill;
        }

        /// <summary>
        /// 이름표 하나에 참조 이름과 종류를 넣는다. 현재 브랜치는 강조, 원격은 테두리만, 태그는 태그 색.
        /// </summary>
        private static void BindRefPill(VisualElement pill, CommitRef commitRef, Color laneColor)
        {
            pill.Q<Label>(className: "commit-graph-ref__name").text = GitDisplay.Nfc(commitRef.DisplayName);
            pill.EnableInClassList("commit-graph-ref--current", commitRef.Kind == CommitRefKind.Current);
            pill.EnableInClassList("commit-graph-ref--local", commitRef.Kind == CommitRefKind.Local);
            pill.EnableInClassList("commit-graph-ref--remote", commitRef.Kind == CommitRefKind.Remote);
            pill.EnableInClassList("commit-graph-ref--tag", commitRef.Kind == CommitRefKind.Tag);
            pill.EnableInClassList("commit-graph-ref--synced", commitRef.HasRemote);
            // 브랜치 이름표는 GitKraken 처럼 가지 색을 입혀 선과 같은 색으로 잇는다: 현재 브랜치는 진하게, 로컬은 옅게, 원격은 더 옅게.
            // 모두 테두리와 바탕이 한 색인 평평한 채움(테두리는 투명). 태그는 USS 의 고정 색.
            bool usesLaneColor = commitRef.Kind != CommitRefKind.Tag;
            Color fill = laneColor;
            fill.a = commitRef.Kind switch
            {
                CommitRefKind.Current => CurrentRefAlpha,
                CommitRefKind.Local => LocalRefAlpha,
                CommitRefKind.Remote => RemoteRefAlpha,
                _ => 0f,
            };
            // 테두리 폭(1px)은 남겨 두어 종류마다 이름표 크기가 같다. 테두리는 투명하게 해 바탕과 한 색으로 보인다.
            Color border = Color.clear;
            pill.style.backgroundColor = usesLaneColor ? fill : StyleKeyword.Null;
            pill.style.borderLeftColor = usesLaneColor ? border : StyleKeyword.Null;
            pill.style.borderRightColor = usesLaneColor ? border : StyleKeyword.Null;
            pill.style.borderTopColor = usesLaneColor ? border : StyleKeyword.Null;
            pill.style.borderBottomColor = usesLaneColor ? border : StyleKeyword.Null;
        }

        /// <summary>
        /// 이름표 칸에 마우스가 올라오면 그 줄 이름표 자리에 이 커밋의 참조를 모두 세로로 펼친다(GitKraken 과 같다).
        /// 참조가 하나이고 이름이 다 보이면 펼칠 것이 없으므로 띄우지 않는다. 마우스가 펼친 목록을 벗어나면 닫는다.
        /// </summary>
        private void ShowRefsPopup(GraphRow row)
        {
            if (_branchInputHash != null || row.RefCount == 0 || row.userData is not int index || index >= _items.Count || _items[index] is not GitGraphNode node)
            {
                return;
            }
            VisualElement pill = row.Refs[0];
            Label more = (Label)row.Refs[1];
            if (row.RefCount == 1 && IsRefNameCut(pill) == false)
            {
                return;
            }
            IReadOnlyList<CommitRef> refs = CommitRefs.Of(node);
            Color laneColor = GraphStyle.LaneColor(node.Lane);
            while (_refsPopup.childCount < refs.Count)
            {
                VisualElement item = CreateRefPill();
                // 두 번 누르면 그 참조로 전환한다.
                int refIndex = _refsPopup.childCount;
                item.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0 || evt.clickCount != 2 || _refsPopupIndex < 0 || _refsPopupIndex >= _items.Count || _items[_refsPopupIndex] is not GitGraphNode target)
                    {
                        return;
                    }
                    IReadOnlyList<CommitRef> targetRefs = CommitRefs.Of(target);
                    if (refIndex < targetRefs.Count)
                    {
                        RefActivated?.Invoke(target, targetRefs[refIndex]);
                    }
                });
                _refsPopup.Add(item);
            }
            for (int i = 0; i < _refsPopup.childCount; i++)
            {
                VisualElement item = _refsPopup[i];
                item.SetVisible(i < refs.Count);
                if (i < refs.Count)
                {
                    BindRefPill(item, refs[i], laneColor);
                    // 위아래로 붙은 이름표는 맞닿는 테두리를 한 줄만 남기고, 모서리는 목록의 바깥 네 귀만 둥글게 한다(USS).
                    item.EnableInClassList("commit-graph-ref--stack-first", i == 0);
                    item.EnableInClassList("commit-graph-ref--stack-last", i == refs.Count - 1);
                }
            }

            // 줄의 이름표 바로 위에 겹쳐 첫 참조가 제자리에서 펼쳐져 보이게 한다. 폭은 가장 긴 이름에 맞추고(GitKraken), 줄 이름표보다 좁아지지만 않게 한다.
            // 전에는 "+n" 상자까지 덮는 폭부터 시작해 이름이 짧으면 오른쪽이 비었다 — 대신 열려 있는 동안 줄의 "+n" 을 숨긴다.
            Rect pillBound = pill.worldBound;
            Vector2 topLeft = _body.WorldToLocal(pillBound.position);
            _refsPopup.style.left = topLeft.x;
            _refsPopup.style.top = topLeft.y;
            // worldBound 는 창 배율이 곱해진 값이라 목록이 붙는 _body 좌표로 바꿔 잰다(그대로 쓰면 배율만큼 넓어졌다).
            _refsPopup.style.minWidth = _body.WorldToLocal(new Vector2(pillBound.xMax, pillBound.yMin)).x - topLeft.x;
            _refsPopupMore?.RemoveFromClassList(MoreCoveredModifier);
            _refsPopupMore = more;
            more.AddToClassList(MoreCoveredModifier);
            _refsPopupIndex = index;
            _refsPopupCell = row.Refs;
            _refsPopup.SetVisible(true);
            _refsPopup.BringToFront();
        }

        /// <summary>
        /// 이름표 이름이 말줄임으로 잘렸거나 칸이 좁아 아예 빠졌는지.
        /// </summary>
        private static bool IsRefNameCut(VisualElement pill)
        {
            Label name = pill.Q<Label>(className: "commit-graph-ref__name");
            if (name.resolvedStyle.display == DisplayStyle.None)
            {
                return true;
            }
            Vector2 size = name.MeasureTextSize(name.text, 0f, VisualElement.MeasureMode.Undefined, 0f, VisualElement.MeasureMode.Undefined);
            return float.IsNaN(size.x) == false && size.x > name.contentRect.width + 0.5f;
        }

        private void HideRefsPopup()
        {
            if (_refsPopupIndex < 0)
            {
                return;
            }
            _refsPopupIndex = -1;
            _refsPopupCell = null;
            _refsPopupMore?.RemoveFromClassList(MoreCoveredModifier);
            _refsPopupMore = null;
            _refsPopup.SetVisible(false);
        }

        /// <summary>펼친 목록 밑에 깔린 줄의 "+n"(목록이 열려 있는 동안 숨긴다).</summary>
        private Label _refsPopupMore;

        private const string MoreCoveredModifier = "commit-graph-ref__more--covered";

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            _headColor = style.TryGetValue(HeadProperty, out Color head) ? head : GraphStyle.Head;
            _outlineColor = style.TryGetValue(OutlineProperty, out Color outline) ? outline : _outlineColor;
            _mutedColor = style.TryGetValue(MutedProperty, out Color muted) ? muted : _mutedColor;
            _overlay.MarkDirtyRepaint();
            // 테마가 바뀌면 줄에 넣은 색(가지 색 이름표·띠, 변경 개수 글자)도 새 테마로 다시 칠한다.
            _list.RefreshItems();
            PlaceAvatars();
        }

        private float ScrollY
        {
            get
            {
                ScrollView scroll = _list.Q<ScrollView>();
                return scroll == null ? 0f : scroll.scrollOffset.y;
            }
        }

        private void Draw(MeshGenerationContext context)
        {
            if (Graph == null)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            float scrollY = ScrollY;
            float height = _overlay.contentRect.height;
            const float rowHeight = VerticalGraphLayout.RowHeight;

            DrawEdges(painter, scrollY, height, rowHeight);
            DrawRefConnectors(painter, scrollY, height, rowHeight);
            DrawWip(painter, scrollY);
            DrawNodes(painter, scrollY, height, rowHeight);
        }

        private void DrawEdges(Painter2D painter, float scrollY, float height, float rowHeight)
        {
            // 한 줄로 접으면 서로 다른 가지가 같은 자리에 겹쳐 선이 뜻을 잃는다.
            if (CompactGraph)
            {
                return;
            }
            painter.lineWidth = 2f;
            foreach (GitGraphEdge edge in Graph.Edges)
            {
                if (_layout.RowOf.TryGetValue(edge.Child, out int childRow) == false || _layout.RowOf.TryGetValue(edge.Parent, out int parentRow) == false)
                {
                    continue;
                }
                float yc = VerticalGraphLayout.RowCenterY(childRow, scrollY);
                float yp = VerticalGraphLayout.RowCenterY(parentRow, scrollY);
                if (yp < -rowHeight || yc > height + rowHeight)
                {
                    continue;
                }
                float xc = X(edge.Child.Lane.Row);
                float xp = X(edge.Parent.Lane.Row);

                // 병합으로 들어오는 선은 들어오는 가지(부모) 색, 나머지는 새 커밋 쪽 줄 색(가로 탐색기와 같은 규칙).
                painter.strokeColor = GraphStyle.LaneColor(edge.IsMergeEdge ? edge.Parent.Lane : edge.Child.Lane);
                painter.BeginPath();
                painter.MoveTo(new Vector2(xc, yc));
                if (Mathf.Approximately(xc, xp))
                {
                    painter.LineTo(new Vector2(xp, yp));
                }
                else if (edge.IsMergeEdge)
                {
                    // 병합: 병합 커밋 바로 아래에서 옆 가지로 꺾여 내려간다.
                    float bendEnd = Mathf.Min(yc + rowHeight, yp);
                    painter.BezierCurveTo(new Vector2(xc, bendEnd), new Vector2(xp, yc), new Vector2(xp, bendEnd));
                    painter.LineTo(new Vector2(xp, yp));
                }
                else
                {
                    // 갈라짐: 가지 줄을 따라 내려오다 부모 바로 위에서 부모 줄로 꺾인다.
                    float bendStart = Mathf.Max(yp - rowHeight, yc);
                    painter.LineTo(new Vector2(xc, bendStart));
                    painter.BezierCurveTo(new Vector2(xc, yp), new Vector2(xp, bendStart), new Vector2(xp, yp));
                }
                painter.Stroke();
            }

            // 불러온 기간 밖으로 이어지는 히스토리: 아래로 짧게 흐린 선.
            painter.strokeColor = GraphStyle.Truncated;
            foreach (GitGraphNode node in Graph.TruncatedNodes)
            {
                if (_layout.RowOf.TryGetValue(node, out int row) == false)
                {
                    continue;
                }
                float x = X(node.Lane.Row);
                float y = VerticalGraphLayout.RowCenterY(row, scrollY);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x, y + rowHeight * 0.8f));
                painter.Stroke();
            }
        }

        /// <summary>
        /// 이름표가 있는 커밋: 이름표 오른쪽 끝에서 커밋 원까지 가지 색 선(GitKraken 처럼 이름표가 어느 커밋인지 따라가게).
        /// </summary>
        private void DrawRefConnectors(Painter2D painter, float scrollY, float height, float rowHeight)
        {
            if (RefsLeftOfGraph == false)
            {
                return;
            }
            Dictionary<int, RefSpan> spans = RefSpansByRow();
            // 아직 배치 전인 줄은 이름표 칸의 그래프 쪽 끝에서 시작한다.
            float fallbackX = _graphOffset - 6f;
            painter.lineWidth = 1.5f;
            foreach (GitGraphNode node in _layout.NodesNewestFirst)
            {
                int row = _layout.RowOf[node];
                float y = VerticalGraphLayout.RowCenterY(row, scrollY);
                // 이름 입력란을 띄운 줄은 이름표가 없어도 입력란 오른쪽 끝(칸 끝 - 간격)에서 커밋까지 잇는다(GitKraken 과 같다).
                bool branchInput = node.Commit.Hash == _branchInputHash;
                if (y < -rowHeight || y > height + rowHeight || (CommitRefs.Of(node).Count == 0 && branchInput == false))
                {
                    continue;
                }
                painter.strokeColor = GraphStyle.LaneColor(node.Lane);
                // 이름표는 글자 폭만큼만 차지하므로 칸 끝이 아니라 이름표의 그래프 쪽 끝에서 잇는다.
                // "+n" 상자가 있으면 이름표와 상자 사이 틈에도 선을 그어 이름표 → "+n" → 커밋이 한 줄로 이어 보이게 한다(선 층이 줄 위라 상자는 건너뛴다).
                float startX = fallbackX;
                if (branchInput)
                {
                    startX = _graphOffset - BranchInputMargin;
                }
                else if (spans.TryGetValue(row, out RefSpan span))
                {
                    startX = span.End;
                    if (span.HasMore)
                    {
                        Line(painter, span.PillEnd, span.MoreStart, y);
                    }
                }
                Line(painter, startX, X(node.Lane.Row), y);
            }
        }

        private static void Line(Painter2D painter, float fromX, float toX, float y)
        {
            if (toX <= fromX)
            {
                return;
            }
            painter.BeginPath();
            painter.MoveTo(new Vector2(fromX, y));
            painter.LineTo(new Vector2(toX, y));
            painter.Stroke();
        }

        /// <summary>
        /// 그래프 왼쪽 이름표 칸에서 보이는 이름표·"+n" 의 가로 자리(선·원 층 좌표).
        /// </summary>
        private struct RefSpan
        {
            public float PillEnd;
            public float MoreStart;

            /// <summary>
            /// 마지막으로 보이는 요소("+n" 이 있으면 그것)의 오른쪽 끝. 여기서 커밋까지 잇는다.
            /// </summary>
            public float End;

            public bool HasMore;
        }

        /// <summary>
        /// 화면에 있는 줄마다 이름표·"+n" 자리. 줄 번호 → 자리. 칸이 좁으면 이름표가 칸 경계에 가려지므로(overflow: hidden) 보이는 끝인 칸 경계까지만 본다.
        /// </summary>
        private Dictionary<int, RefSpan> RefSpansByRow()
        {
            Dictionary<int, RefSpan> spans = new();
            _list.Query<GraphRow>().ForEach(row =>
            {
                if (row.userData is not int index || row.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }
                VisualElement refsCell = row.Refs;
                VisualElement pill = refsCell[0];
                VisualElement more = refsCell[1];
                if (pill.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }
                float cellEnd = refsCell.worldBound.xMax;
                float pillEnd = Mathf.Min(pill.worldBound.xMax, cellEnd);
                if (float.IsNaN(pillEnd))
                {
                    return;
                }
                bool hasMore = more.resolvedStyle.display != DisplayStyle.None && more.worldBound.xMin < cellEnd;
                float end = hasMore ? Mathf.Min(more.worldBound.xMax, cellEnd) : pillEnd;
                spans[index] = new RefSpan
                {
                    PillEnd = ToOverlayX(pillEnd),
                    MoreStart = hasMore ? ToOverlayX(more.worldBound.xMin) : 0f,
                    End = ToOverlayX(end),
                    HasMore = hasMore,
                };
            });
            return spans;
        }

        private float ToOverlayX(float worldX)
        {
            return _overlay.WorldToLocal(new Vector2(worldX, 0f)).x;
        }

        /// <summary>
        /// 그래프 칸이 두 번째 열도 못 보일 만큼 좁은지. 그러면 GitKraken 처럼 커밋을 모두 첫 열에 세우고 가지 선을 긋지 않는다
        /// (줄마다의 가지 색은 원 테두리·띠·막대로 남는다). 첫 열 자리는 그대로라 접히는 순간 원이 옆으로 튀지 않는다.
        /// 가지가 하나뿐이면 접을 것이 없으니 선을 그대로 긋는다.
        /// </summary>
        private bool CompactGraph => _layout.LaneCount > 1 && ColumnWidth(CommitGraphColumn.Graph) < _layout.TwoLaneWidth;

        /// <summary>
        /// 가지 열의 x. 칸 폭에 맞춰 가지 묶음을 가운데에 두고(<see cref="VerticalGraphLayout.FirstLaneX"/>),
        /// 선·원 층이 그래프 왼쪽 이름표 칸부터 시작하면 그만큼 민다.
        /// 한 줄로 접어도 첫 열 자리는 모든 열이 있을 때와 같게 둬서 접히는 순간 원이 튀지 않는다.
        /// </summary>
        private float X(int laneRow)
        {
            float contentWidth = ColumnWidth(CommitGraphColumn.Graph) - BarWidth;
            float first = _layout.FirstLaneX(contentWidth, _layout.LaneCount);
            return _graphOffset + _layout.LaneX(CompactGraph ? 0 : laneRow, first);
        }

        /// <summary>
        /// 작업 중 줄(GitKraken 과 같다): HEAD 열에 커밋 원과 같은 크기의 점선 원, HEAD 까지 점선. 색은 HEAD 가지 색.
        /// 원 안은 목록 바탕으로 채워, 원 가운데에서 시작하는 가지 색 띠가 원 오른쪽 가장자리부터 보이게 한다(커밋 줄은 아바타가 덮는다).
        /// </summary>
        private void DrawWip(Painter2D painter, float scrollY)
        {
            if (_layout.HasWip == false)
            {
                return;
            }
            GitGraphNode head = HeadNode;
            float x = X(head?.Lane.Row ?? 0);
            float y = VerticalGraphLayout.RowCenterY(0, scrollY);
            // 커밋 원의 가지 색 테두리 바깥과 같은 크기(아바타면 아바타 + 2, 아니면 작은 원 바깥).
            float radius = _layout.HasAvatars ? VerticalGraphLayout.AvatarRadius + 2f : 6.5f;
            Color color = head != null ? GraphStyle.LaneColor(head.Lane) : _mutedColor;

            painter.strokeColor = color;
            painter.lineWidth = 1.5f;
            // 한 줄로 접으면 HEAD 까지의 점선이 사이 커밋들을 지나가므로 긋지 않는다.
            if (head != null && CompactGraph == false && _layout.RowOf.TryGetValue(head, out int headRow))
            {
                float headY = VerticalGraphLayout.RowCenterY(headRow, scrollY);
                float headTop = headY - NodeTopRadius(head);
                for (float dashY = y + radius + 2f; dashY < headTop; dashY += 6f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, dashY));
                    painter.LineTo(new Vector2(x, Mathf.Min(dashY + 3f, headTop)));
                    painter.Stroke();
                }
            }

            FillCircle(painter, new Vector2(x, y), radius, _outlineColor);
            // 점선 원: 같은 길이의 호를 고르게 나눠 긋는다.
            const int dashes = 12;
            const float step = 360f / dashes;
            painter.lineWidth = 2f;
            for (int i = 0; i < dashes; i++)
            {
                float start = i * step;
                painter.BeginPath();
                painter.Arc(new Vector2(x, y), radius - 1f, Angle.Degrees(start), Angle.Degrees(start + step * 0.55f));
                painter.Stroke();
            }
        }

        /// <summary>
        /// 커밋 원 위쪽 가장자리까지의 거리(HEAD 고리까지). <see cref="DrawNode"/> 와 맞춘다.
        /// </summary>
        private float NodeTopRadius(GitGraphNode node)
        {
            if (_layout.HasAvatars && node.Commit.IsMerge == false)
            {
                return VerticalGraphLayout.AvatarRadius + (node.IsHead ? 4f : 2f);
            }
            return node.IsHead ? 8f : 6.5f;
        }

        private void DrawNodes(Painter2D painter, float scrollY, float height, float rowHeight)
        {
            foreach (GitGraphNode node in _layout.NodesNewestFirst)
            {
                float y = VerticalGraphLayout.RowCenterY(_layout.RowOf[node], scrollY);
                if (y < -rowHeight || y > height + rowHeight)
                {
                    continue;
                }
                DrawNode(painter, node, new Vector2(X(node.Lane.Row), y));
            }
        }

        private void DrawNode(Painter2D painter, GitGraphNode node, Vector2 center)
        {
            Color laneColor = GraphStyle.LaneColor(node.Lane);
            if (_layout.HasAvatars && node.Commit.IsMerge == false)
            {
                // 아바타 원: 줄 색 테두리 + 작성자 색 바탕(그림을 받기 전·머리글자 뒤에 보인다). 그림은 위 층이 얹는다.
                const float avatarRadius = VerticalGraphLayout.AvatarRadius;
                if (node.IsHead)
                {
                    FillCircle(painter, center, avatarRadius + 4f, _headColor);
                    FillCircle(painter, center, avatarRadius + 3f, _outlineColor);
                }
                FillCircle(painter, center, avatarRadius + 2f, laneColor);
                FillCircle(painter, center, avatarRadius, GraphStyle.AuthorColor(node.Commit.AuthorName));
                return;
            }
            if (node.IsHead)
            {
                FillCircle(painter, center, 8f, _headColor);
            }
            FillCircle(painter, center, 6.5f, _outlineColor);
            FillCircle(painter, center, 5f, node.Commit.IsMerge ? _outlineColor : laneColor);
            if (node.Commit.IsMerge)
            {
                // 병합 커밋은 속이 빈 원.
                painter.strokeColor = laneColor;
                painter.lineWidth = 2f;
                painter.BeginPath();
                painter.Arc(center, 4.5f, Angle.Degrees(0f), Angle.Degrees(360f));
                painter.Stroke();
            }
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill();
        }
    }
}
