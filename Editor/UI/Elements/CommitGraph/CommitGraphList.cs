using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태의 세로 커밋 그래프: 맨 위 작업 중(WIP) 줄, 그 아래 최신부터 커밋 한 줄씩.
    /// 줄은 가상화 ListView 가 그리고, 브랜치 선·원은 그 위 왼쪽 칸에 겹친 요소가 Painter2D 로 그린다
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

        private static readonly CustomStyleProperty<Color> HeadProperty = new("--git-graph-head");
        private static readonly CustomStyleProperty<Color> OutlineProperty = new("--git-list-bg");
        private static readonly CustomStyleProperty<Color> MutedProperty = new("--git-text-muted");

        private readonly ListView _list;
        private readonly VisualElement _overlay;
        private readonly Label _graphHeader;
        private VerticalGraphLayout _layout = VerticalGraphLayout.Build(null, false);
        private List<object> _items = new();
        private int _wipCount;
        private Color _headColor = GraphStyle.Head;
        private Color _outlineColor = new(0.12f, 0.12f, 0.13f);
        private Color _mutedColor = Color.gray;

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

        public CommitGraphList(VisualElement host)
        {
            Root = new VisualElement();
            Root.AddToClassList("commit-graph");
            host.Add(Root);

            VisualElement header = new();
            header.AddToClassList("list-header");
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

            // 목록을 스크롤하거나 크기가 바뀌면 선을 다시 그린다. ListView 는 생성자에서 안쪽 ScrollView 를 만든다.
            _list.RegisterCallback<GeometryChangedEvent>(_ => _overlay.MarkDirtyRepaint());
            _list.Q<ScrollView>().verticalScroller.valueChanged += _ => _overlay.MarkDirtyRepaint();
        }

        public GitGraph Graph { get; private set; }

        /// <param name="wipCount">작업 중인 변경 수. 0 이면 WIP 줄을 숨긴다.</param>
        public void SetGraph(GitGraph graph, int wipCount)
        {
            string selectedHash = SelectedNode?.Commit.Hash;
            bool wipSelected = _list.selectedItem is WipItem;

            Graph = graph;
            _wipCount = wipCount;
            _layout = VerticalGraphLayout.Build(graph, wipCount > 0);
            _items = new List<object>();
            if (_layout.HasWip)
            {
                _items.Add(Wip);
            }
            _items.AddRange(_layout.NodesNewestFirst);
            _list.itemsSource = _items;
            _list.RefreshItems();

            _overlay.style.width = _layout.Width;
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

            VisualElement graph = new();
            graph.AddToClassList("commit-graph-row__graph");
            row.Add(graph);

            VisualElement message = new();
            message.AddToClassList("commit-graph-row__message");
            message.AddToClassList("column--grow-3");
            Label refs = new();
            refs.AddToClassList("commit-row__refs");
            refs.AddToClassList("ellipsis");
            message.Add(refs);
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
            VisualElement graph = row[0];
            graph.style.width = _layout.Width;
            graph.style.minWidth = _layout.Width;
            Label refs = row[1].Q<Label>(className: "commit-row__refs");
            Label subject = row[1].Q<Label>(className: "commit-row__subject");
            Label author = (Label)row[2];
            Label date = (Label)row[3];

            bool isWip = _items[index] is WipItem;
            row.EnableInClassList("commit-graph-row--wip", isWip);
            if (isWip)
            {
                refs.text = string.Empty;
                refs.AddToClassList("commit-row__refs--hidden");
                subject.text = GitText.Format("graph.wip", _wipCount);
                author.text = string.Empty;
                date.text = string.Empty;
                return;
            }

            GitGraphNode node = (GitGraphNode)_items[index];
            GitCommitInfo commit = node.Commit;
            string labels = string.Join("  ", RefNames(node));
            refs.text = labels;
            refs.EnableInClassList("commit-row__refs--hidden", labels.Length == 0);
            subject.text = commit.Subject;
            subject.tooltip = $"{commit.ShortHash}  {commit.Subject}";
            author.text = commit.AuthorName;
            date.text = GitDisplay.FormatDate(commit.Date);
        }

        /// <summary>
        /// 줄 앞에 붙일 참조 이름: 이 커밋이 끝인 브랜치 줄 이름, 그 밖의 참조(원격·태그).
        /// </summary>
        private static IEnumerable<string> RefNames(GitGraphNode node)
        {
            if (node.Column == node.Lane.LastColumn && node.Lane.IsAnonymous == false)
            {
                yield return node.Lane.IsCurrent ? "✓ " + node.Lane.Name : node.Lane.Name;
            }
            foreach (string label in node.RefLabels)
            {
                yield return label;
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            _headColor = style.TryGetValue(HeadProperty, out Color head) ? head : GraphStyle.Head;
            _outlineColor = style.TryGetValue(OutlineProperty, out Color outline) ? outline : _outlineColor;
            _mutedColor = style.TryGetValue(MutedProperty, out Color muted) ? muted : _mutedColor;
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
                float xc = VerticalGraphLayout.LaneX(edge.Child.Lane.Row);
                float xp = VerticalGraphLayout.LaneX(edge.Parent.Lane.Row);

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
                float x = VerticalGraphLayout.LaneX(node.Lane.Row);
                float y = VerticalGraphLayout.RowCenterY(row, scrollY);
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, y));
                painter.LineTo(new Vector2(x, y + rowHeight * 0.8f));
                painter.Stroke();
            }
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
            float x = VerticalGraphLayout.LaneX(head?.Lane.Row ?? 0);
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
                Vector2 center = new(VerticalGraphLayout.LaneX(node.Lane.Row), y);
                Color laneColor = GraphStyle.LaneColor(node.Lane);
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
