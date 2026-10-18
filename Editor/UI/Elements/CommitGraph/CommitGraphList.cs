using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태의 세로 커밋 그래프: 맨 위 작업 중(WIP) 줄, 그 아래 최신부터 커밋 한 줄씩.
    /// 한 줄은 [브랜치/태그 이름표][그래프][메시지][작성자][날짜]. 이름표에서 커밋 원까지 가지 색 선을 잇고,
    /// 그래프 칸에는 그 커밋 가지 색을 옅게 깔아 어느 가지인지 한눈에 보이게 한다.
    /// 줄은 가상화 ListView 가 그리고, 선·원은 이름표·그래프 칸 위에 겹친 요소가 Painter2D 로 그린다
    /// (보이는 줄만 그리고, 스크롤할 때마다 다시 그린다). 배치는 <see cref="VerticalGraphLayout"/>.
    /// </summary>
    public sealed class CommitGraphList
    {
        /// <summary>
        /// 목록 맨 위 작업 중 줄의 자리표.
        /// </summary>
        private sealed class WipItem
        {
        }

        private static readonly WipItem Wip = new();

        /// <summary>
        /// 왼쪽 브랜치/태그 칸의 폭. 이름표 하나와 "+n" 이 들어가는 크기.
        /// </summary>
        private const float RefsColumnWidth = 170f;

        /// <summary>
        /// 그래프 칸에 까는 가지 색의 진하기. 글자를 가리지 않을 만큼만.
        /// </summary>
        private const float LaneTintAlpha = 0.10f;

        private static readonly CustomStyleProperty<Color> HeadProperty = new("--git-graph-head");
        private static readonly CustomStyleProperty<Color> OutlineProperty = new("--git-list-bg");
        private static readonly CustomStyleProperty<Color> MutedProperty = new("--git-text-muted");
        private static readonly CustomStyleProperty<Color> ModifiedProperty = new("--git-state-modified");
        private static readonly CustomStyleProperty<Color> AddedProperty = new("--git-state-added");
        private static readonly CustomStyleProperty<Color> DeletedProperty = new("--git-state-untracked");

        private readonly ListView _list;
        private readonly VisualElement _overlay;
        private readonly GraphLabelLayer _avatarLayer;
        private readonly VisualElement _avatarHost;
        private readonly IAvatarProvider _avatars;
        private readonly Label _graphHeader;
        private VerticalGraphLayout _layout = VerticalGraphLayout.Build(null, false);
        private List<object> _items = new();
        private WipCounts _wip;
        private Color _headColor = GraphStyle.Head;
        private Color _outlineColor = new(0.12f, 0.12f, 0.13f);
        private Color _mutedColor = Color.gray;
        private Color _modifiedColor = new(0.44f, 0.68f, 1f);
        private Color _addedColor = new(0.45f, 0.74f, 0.47f);
        private Color _deletedColor = new(0.91f, 0.56f, 0.54f);

        /// <summary>
        /// 줄을 골랐을 때. 작업 중 줄이면 null.
        /// </summary>
        public event Action<GitGraphNode> CommitSelected;

        public event Action WipSelected;

        /// <summary>
        /// 커밋 줄 우클릭 메뉴.
        /// </summary>
        public event Action<DropdownMenu, GitGraphNode> ContextMenuRequested;

        public VisualElement Root { get; }

        /// <param name="avatars">작성자 아바타. null 이거나 설정에서 꺼져 있으면 작은 점으로 그린다.</param>
        public CommitGraphList(VisualElement host, IAvatarProvider avatars = null)
        {
            _avatars = avatars;
            Root = new VisualElement();
            Root.AddToClassList("commit-graph");
            host.Add(Root);

            VisualElement header = new();
            header.AddToClassList("list-header");
            header.Add(HeaderCell(GitText.Get("graph.columnRefs"), "commit-graph__header-refs"));
            _graphHeader = HeaderCell(GitText.Get("graph.columnGraph"), "commit-graph__header-graph");
            header.Add(_graphHeader);
            header.Add(HeaderCell(GitText.Get("column.message"), "column--grow-3"));
            header.Add(HeaderCell(GitText.Get("column.author"), "column--author"));
            header.Add(HeaderCell(GitText.Get("column.date"), "column--date"));
            Root.Add(header);

            VisualElement body = new();
            body.AddToClassList("commit-graph__body");
            Root.Add(body);

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

            // 목록을 스크롤하거나 크기가 바뀌면 선을 다시 그린다. ListView 는 생성자에서 안쪽 ScrollView 를 만든다.
            _list.RegisterCallback<GeometryChangedEvent>(_ => Redraw());
            _list.Q<ScrollView>().verticalScroller.valueChanged += _ => Redraw();
            if (_avatars != null)
            {
                // 아바타는 받는 대로 나타난다. 패널에서 떨어지면 구독을 끊는다.
                Root.RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += Redraw);
                Root.RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= Redraw);
            }
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
            _overlay.MarkDirtyRepaint();
            PlaceAvatars();
        }

        public GitGraph Graph { get; private set; }

        /// <param name="wip">작업 중인 변경 개수. 하나도 없으면 WIP 줄을 숨긴다.</param>
        public void SetGraph(GitGraph graph, WipCounts wip)
        {
            string selectedHash = SelectedNode?.Commit.Hash;
            bool wipSelected = _list.selectedItem is WipItem;

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

            // 선·원 층은 이름표 칸까지 덮는다(이름표에서 커밋까지 잇는 선을 그리려고).
            _overlay.style.width = RefsColumnWidth + _layout.Width;
            _avatarHost.style.width = RefsColumnWidth + _layout.Width;
            _graphHeader.style.width = _layout.Width;
            _graphHeader.style.minWidth = _layout.Width;

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

        private static Label HeaderCell(string text, string columnClass)
        {
            Label label = new(text);
            label.AddToClassList("list-header__cell");
            label.AddToClassList(columnClass);
            return label;
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
            VisualElement row = new();
            row.AddToClassList("commit-graph-row");

            VisualElement refs = new();
            refs.AddToClassList("commit-graph-row__refs");
            Label pill = new();
            pill.AddToClassList("commit-graph-ref");
            pill.AddToClassList("ellipsis");
            refs.Add(pill);
            Label more = new();
            more.AddToClassList("commit-graph-ref__more");
            refs.Add(more);
            row.Add(refs);
            // 이름표 폭이 바뀌면(다른 커밋에 다시 묶이면) 이름표에서 시작하는 선도 다시 그린다.
            pill.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());
            more.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());

            VisualElement graph = new();
            graph.AddToClassList("commit-graph-row__graph");
            row.Add(graph);

            VisualElement message = new();
            message.AddToClassList("commit-graph-row__message");
            message.AddToClassList("column--grow-3");
            Label subject = new();
            subject.AddToClassList("commit-row__subject");
            subject.AddToClassList("ellipsis");
            message.Add(subject);
            row.Add(message);

            Label author = new();
            author.AddToClassList("commit-row__author");
            author.AddToClassList("column--author");
            author.AddToClassList("ellipsis");
            row.Add(author);

            Label date = new();
            date.AddToClassList("commit-row__date");
            date.AddToClassList("column--date");
            row.Add(date);

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

        private void BindRow(VisualElement row, int index)
        {
            row.userData = index;
            VisualElement refsCell = row[0];
            Label pill = (Label)refsCell[0];
            Label more = (Label)refsCell[1];
            VisualElement graph = row[1];
            graph.style.width = _layout.Width;
            graph.style.minWidth = _layout.Width;
            VisualElement message = row[2];
            Label subject = message.Q<Label>(className: "commit-row__subject");
            Label author = (Label)row[3];
            Label date = (Label)row[4];

            bool isWip = _items[index] is WipItem;
            row.EnableInClassList("commit-graph-row--wip", isWip);
            if (isWip)
            {
                BindRefs(pill, more, Array.Empty<CommitRef>(), Color.clear);
                graph.style.backgroundColor = StyleKeyword.Null;
                message.style.borderLeftColor = StyleKeyword.Null;
                subject.text = WipText();
                subject.tooltip = null;
                author.text = string.Empty;
                date.text = string.Empty;
                return;
            }

            GitGraphNode node = (GitGraphNode)_items[index];
            GitCommitInfo commit = node.Commit;
            Color laneColor = GraphStyle.LaneColor(node.Lane);
            BindRefs(pill, more, CommitRefs.Of(node), laneColor);
            // 가지 색 띠: 그래프 칸에 옅게 깔고 메시지 앞에 막대를 둬서 줄마다 어느 가지인지 보이게 한다.
            Color tint = laneColor;
            tint.a = LaneTintAlpha;
            graph.style.backgroundColor = tint;
            message.style.borderLeftColor = laneColor;
            subject.text = commit.Subject;
            subject.tooltip = $"{commit.ShortHash}  {commit.Subject}";
            author.text = commit.AuthorName;
            date.text = GitDisplay.FormatDate(commit.Date);
        }

        /// <summary>
        /// "// 작업 중  ~3 +8 −170". 개수가 0 인 종류는 뺀다. 색은 파일 상태 표시와 같은 테마 색.
        /// </summary>
        private string WipText()
        {
            string text = GitText.Get("graph.wip");
            text += Count("~", _wip.Modified, _modifiedColor);
            text += Count("+", _wip.Added, _addedColor);
            text += Count("\u2212", _wip.Deleted, _deletedColor);
            return text;

            static string Count(string sign, int count, Color color)
            {
                return count == 0 ? string.Empty : $"   <color=#{ColorUtility.ToHtmlStringRGB(color)}>{sign}{count}</color>";
            }
        }

        /// <summary>
        /// 이름표 칸: 첫 참조 하나를 이름표로, 나머지는 "+n" 으로. 현재 브랜치는 강조, 원격은 테두리만, 태그는 태그 색.
        /// </summary>
        private static void BindRefs(Label pill, Label more, IReadOnlyList<CommitRef> refs, Color laneColor)
        {
            pill.SetVisible(refs.Count > 0);
            more.SetVisible(refs.Count > 1);
            if (refs.Count == 0)
            {
                return;
            }
            CommitRef first = refs[0];
            pill.text = GitDisplay.Nfc(first.Kind == CommitRefKind.Current ? "\u2713 " + first.Name : first.Name);
            pill.tooltip = GitDisplay.Nfc(string.Join("\n", refs.Select(r => r.Name)));
            pill.EnableInClassList("commit-graph-ref--current", first.Kind == CommitRefKind.Current);
            pill.EnableInClassList("commit-graph-ref--remote", first.Kind == CommitRefKind.Remote);
            pill.EnableInClassList("commit-graph-ref--tag", first.Kind == CommitRefKind.Tag);
            // 로컬·원격 이름표는 가지 색을 입혀 선과 같은 색으로 잇는다(현재 브랜치·태그는 USS 의 고정 색).
            bool usesLaneColor = first.Kind == CommitRefKind.Local || first.Kind == CommitRefKind.Remote;
            Color fill = laneColor;
            fill.a = first.Kind == CommitRefKind.Local ? 0.35f : 0f;
            pill.style.backgroundColor = usesLaneColor ? fill : StyleKeyword.Null;
            pill.style.borderLeftColor = usesLaneColor ? laneColor : StyleKeyword.Null;
            pill.style.borderRightColor = usesLaneColor ? laneColor : StyleKeyword.Null;
            pill.style.borderTopColor = usesLaneColor ? laneColor : StyleKeyword.Null;
            pill.style.borderBottomColor = usesLaneColor ? laneColor : StyleKeyword.Null;
            more.text = refs.Count > 1 ? $"+{refs.Count - 1}" : string.Empty;
            more.tooltip = pill.tooltip;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            _headColor = style.TryGetValue(HeadProperty, out Color head) ? head : GraphStyle.Head;
            _outlineColor = style.TryGetValue(OutlineProperty, out Color outline) ? outline : _outlineColor;
            _mutedColor = style.TryGetValue(MutedProperty, out Color muted) ? muted : _mutedColor;
            _modifiedColor = style.TryGetValue(ModifiedProperty, out Color modified) ? modified : _modifiedColor;
            _addedColor = style.TryGetValue(AddedProperty, out Color added) ? added : _addedColor;
            _deletedColor = style.TryGetValue(DeletedProperty, out Color deleted) ? deleted : _deletedColor;
            _overlay.MarkDirtyRepaint();
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
            Dictionary<int, float> refEnds = RefEndsByRow();
            painter.lineWidth = 1.5f;
            foreach (GitGraphNode node in _layout.NodesNewestFirst)
            {
                int row = _layout.RowOf[node];
                float y = VerticalGraphLayout.RowCenterY(row, scrollY);
                if (y < -rowHeight || y > height + rowHeight || CommitRefs.Of(node).Count == 0)
                {
                    continue;
                }
                // 이름표는 글자 폭만큼만 차지하므로 칸 끝이 아니라 이름표("+n" 이 있으면 그것) 끝에서 잇는다.
                // 아직 배치 전인 줄은 칸 끝에서 시작한다.
                float startX = refEnds.TryGetValue(row, out float end) ? end : RefsColumnWidth - 6f;
                painter.strokeColor = GraphStyle.LaneColor(node.Lane);
                painter.BeginPath();
                painter.MoveTo(new Vector2(startX, y));
                painter.LineTo(new Vector2(X(node.Lane.Row), y));
                painter.Stroke();
            }
        }

        /// <summary>
        /// 화면에 있는 줄마다 이름표 칸에서 마지막으로 보이는 요소의 오른쪽 끝(선·원 층 좌표). 줄 번호 → x.
        /// </summary>
        private Dictionary<int, float> RefEndsByRow()
        {
            Dictionary<int, float> ends = new();
            _list.Query<VisualElement>(className: "commit-graph-row").ForEach(row =>
            {
                if (row.userData is not int index || row.resolvedStyle.display == DisplayStyle.None)
                {
                    return;
                }
                VisualElement refsCell = row[0];
                VisualElement last = refsCell[1].resolvedStyle.display != DisplayStyle.None ? refsCell[1] : refsCell[0];
                if (last.resolvedStyle.display == DisplayStyle.None || float.IsNaN(last.worldBound.xMax))
                {
                    return;
                }
                ends[index] = _overlay.WorldToLocal(new Vector2(last.worldBound.xMax, 0f)).x;
            });
            return ends;
        }

        /// <summary>
        /// 가지 열의 x. 선·원 층은 이름표 칸부터 시작하므로 그만큼 민다.
        /// </summary>
        private float X(int laneRow)
        {
            return RefsColumnWidth + _layout.LaneX(laneRow);
        }

        /// <summary>
        /// 작업 중 줄: HEAD 열에 빈 원, HEAD 까지 점선.
        /// </summary>
        private void DrawWip(Painter2D painter, float scrollY)
        {
            if (_layout.HasWip == false)
            {
                return;
            }
            GitGraphNode head = _layout.NodesNewestFirst.FirstOrDefault(node => node.IsHead);
            float x = X(head?.Lane.Row ?? 0);
            float y = VerticalGraphLayout.RowCenterY(0, scrollY);

            painter.strokeColor = _mutedColor;
            painter.lineWidth = 1.5f;
            if (head != null && _layout.RowOf.TryGetValue(head, out int headRow))
            {
                float headY = VerticalGraphLayout.RowCenterY(headRow, scrollY);
                for (float dashY = y + 7f; dashY < headY - 6f; dashY += 6f)
                {
                    painter.BeginPath();
                    painter.MoveTo(new Vector2(x, dashY));
                    painter.LineTo(new Vector2(x, Mathf.Min(dashY + 3f, headY - 6f)));
                    painter.Stroke();
                }
            }
            painter.BeginPath();
            painter.Arc(new Vector2(x, y), 6f, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.Stroke();
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
                Vector2 center = new(X(node.Lane.Row), y);
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
                    continue;
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
