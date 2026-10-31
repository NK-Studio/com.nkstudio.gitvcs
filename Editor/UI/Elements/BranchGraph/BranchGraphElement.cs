using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Branch Explorer 캔버스(<c>.branch-graph</c>). 브랜치마다 한 줄, 시간은 왼쪽→오른쪽.
    ///
    /// 역할을 나눠 둔다: 배치는 <see cref="GitGraph"/>, 확대·이동 계산은 <see cref="GraphViewport"/>,
    /// 글자 재사용은 <see cref="GraphLabelLayer"/>. 이 클래스는 선·원 그리기(Painter2D)와 입력(끌기·휠·클릭)만 맡는다.
    /// </summary>
    public sealed class BranchGraphElement : VisualElement
    {
        private const string Block = "branch-graph";
        private const string HiddenTextModifier = Block + "--hidden-text";

        private readonly GraphLabelLayer _labels;
        private readonly GraphLabelLayer _headerLabels;
        private readonly Label _tooltip;
        private readonly Label _empty;
        private readonly IAvatarProvider _avatars;

        private GitGraph _graph;
        // 줄 머리표·커밋 이름표가 이미 차지한 자리(캔버스 좌표). 뒤에 놓는 이름표가 겹치면 접는다.
        private readonly List<Rect> _occupied = new();
        // 줄마다 같은 행에서 바로 앞에 끝난 줄의 마지막 칸(없으면 없음). 줄 머리표가 그 커밋 위로 뻗지 않게 한다.
        private readonly Dictionary<GitGraphLane, int> _previousLaneEnd = new();
        private GitGraph _previousLaneEndGraph;
        private string _selectedHash;
        private string _search = string.Empty;
        private bool _centerPending = true;
        private bool _isPanning;
        private GitGraphNode _hovered;

        // 올려 둔 커밋의 장식(빛 번짐·굵어지는 테두리·퍼지는 고리). 0→1 로 차오르고, 떠난 커밋은 1→0 으로 사라진다.
        private const float HoverFadeSeconds = 0.15f;
        private const float HoverPulseSeconds = 1.4f;
        private float _hoverIn;
        private GitGraphNode _hoverFading;
        private float _hoverOut;
        private double _hoverStartTime;
        private double _lastHoverTick;
        private IVisualElementScheduledItem _hoverTicker;

        // Painter2D 로 그리는 색 중 배경에 따라 달라야 하는 것은 테마 토큰(--git-graph-*)에서 읽는다. 기본값은 어두운 테마 값.
        // 테마 변수는 물려받기만 해서 사용자 정의 스타일로는 읽히지 않으므로, BranchGraph.uss 가 이 요소에 옮겨 적은 이름(--graph-*)을 읽는다.
        private static readonly CustomStyleProperty<Color> SelectionProperty = new("--graph-selection");
        private static readonly CustomStyleProperty<Color> SearchProperty = new("--graph-search");
        private static readonly CustomStyleProperty<Color> HeadProperty = new("--graph-head");
        private static readonly CustomStyleProperty<Color> DateBandProperty = new("--graph-date-band");
        private static readonly CustomStyleProperty<Color> TruncatedProperty = new("--graph-truncated");
        private static readonly CustomStyleProperty<Color> ShadowProperty = new("--graph-shadow");
        private Color _selectionColor = GraphStyle.Selection;
        private Color _searchColor = GraphStyle.SearchMatch;
        private Color _headColor = GraphStyle.Head;
        private Color _dateBandColor = GraphStyle.DateBand;
        private Color _truncatedColor = GraphStyle.Truncated;
        private Color _shadowColor = new(0f, 0f, 0f, 0.5f);
        private Vector2 _pointer;

        public GraphViewport Viewport { get; }
        public GitGraph Graph => _graph;

        public event Action<GitGraphNode> NodeSelected;
        public event Action<DropdownMenu, GitGraphNode> NodeContextMenu;

        /// <summary>끌기·휠로 확대·이동이 바뀐 뒤(상태 저장용).</summary>
        public event Action ViewportChanged;

        /// <param name="avatars">작성자 아바타. null 이거나 꺼져 있으면 머리글자만 그린다.</param>
        public BranchGraphElement(float zoom, Vector2 pan, IAvatarProvider avatars)
        {
            Viewport = new GraphViewport(zoom, pan);
            _avatars = avatars;
            AddToClassList(Block);

            VisualElement labelLayer = new() { pickingMode = PickingMode.Ignore };
            labelLayer.StretchToParentSize();
            Add(labelLayer);
            _labels = new GraphLabelLayer(labelLayer);

            VisualElement header = new() { pickingMode = PickingMode.Ignore };
            header.AddToClassList(Block + "__header");
            Add(header);
            _headerLabels = new GraphLabelLayer(header);

            _empty = new Label { pickingMode = PickingMode.Ignore };
            _empty.AddToClassList(Block + "__empty");
            Add(_empty);

            _tooltip = new Label { pickingMode = PickingMode.Ignore };
            _tooltip.AddToClassList(Block + "__label");
            _tooltip.AddToClassList(Block + "__tooltip");
            GitShadow.Add(_tooltip, GitShadowSize.Small);
            _tooltip.SetVisible(false);
            Add(_tooltip);

            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            if (_avatars != null)
            {
                // 아바타는 받는 대로 나타난다. 패널에서 떨어지면 구독을 끊는다.
                RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += Refresh);
                RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= Refresh);
            }
            RegisterCallback<GeometryChangedEvent>(_ => OnGeometryChanged());
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerLeaveEvent>(_ => SetHovered(null));
            RegisterCallback<WheelEvent>(OnWheel);
            this.AddManipulator(new ContextualMenuManipulator(OnContextMenu));
        }

        // ── 외부 API ───────────────────────────────────────

        /// <param name="emptyMessage">커밋이 없을 때 가운데 보일 문구.</param>
        public void SetGraph(GitGraph graph, string emptyMessage)
        {
            bool firstGraph = _graph == null;
            _graph = graph;
            _centerPending |= firstGraph;
            _empty.text = graph == null || graph.Nodes.Count == 0 ? emptyMessage : string.Empty;
            _empty.SetVisible(string.IsNullOrEmpty(_empty.text) == false);
            // 이미 크기가 잡힌 채로 그래프가 오면 GeometryChangedEvent 가 다시 오지 않으니 여기서 바로 HEAD 로 옮긴다.
            // 아직 배치 전이면(크기 0) 배치가 끝난 뒤 OnGeometryChanged 가 한다.
            TryCenterOnHead();
            Refresh();
        }

        public void SetSelected(string hash)
        {
            _selectedHash = hash;
            Refresh();
        }

        public void SetSearch(string query)
        {
            _search = query?.Trim() ?? string.Empty;
            Refresh();
        }

        public bool Matches(GitGraphNode node)
        {
            if (_search.Length == 0)
            {
                return true;
            }
            GitCommitInfo c = node.Commit;
            return c.Subject.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || c.AuthorName.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || c.Hash.StartsWith(_search, StringComparison.OrdinalIgnoreCase)
                   || c.Refs.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0
                   || node.Lane.Name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void FocusNode(GitGraphNode node)
        {
            if (node == null)
            {
                return;
            }
            Viewport.Focus(GraphStyle.WorldPosition(node), layout.size);
            Refresh();
            ViewportChanged?.Invoke();
        }

        public void ZoomBy(float factor)
        {
            Viewport.ZoomAround(layout.size * 0.5f, Viewport.Zoom * factor);
            Refresh();
            ViewportChanged?.Invoke();
        }

        public GitGraphNode FindHead()
        {
            if (_graph == null)
            {
                return null;
            }
            foreach (GitGraphNode node in _graph.Nodes)
            {
                if (node.IsHead)
                {
                    return node;
                }
            }
            return null;
        }

        // ── 그리기 ────────────────────────────────────────

        /// <summary>그래프 데이터는 그대로이고 보이는 것만 다시 그릴 때(아바타 켜기·끄기 등).</summary>
        public void Redraw()
        {
            Refresh();
        }

        private void Refresh()
        {
            MarkDirtyRepaint();
            UpdateLabels();
        }

        private void OnGeometryChanged()
        {
            TryCenterOnHead();
            Refresh();
        }

        /// <summary>
        /// 처음 받은 그래프는 맨 처음 커밋이 아니라 HEAD(없으면 가장 최근 커밋)를 가운데에 두고 시작한다. 크기가 잡히기 전이면 미룬다.
        /// </summary>
        private void TryCenterOnHead()
        {
            if (_centerPending == false || _graph == null || _graph.Nodes.Count == 0 || float.IsNaN(layout.width) || layout.width <= 1f)
            {
                return;
            }
            _centerPending = false;
            GitGraphNode head = FindHead() ?? _graph.Nodes[_graph.Nodes.Count - 1];
            Viewport.Focus(GraphStyle.WorldPosition(head), layout.size);
            ViewportChanged?.Invoke();
        }

        private bool IsVisibleX(float x, float margin)
        {
            return x >= -margin && x <= layout.width + margin;
        }

        private void Draw(MeshGenerationContext context)
        {
            if (_graph == null || _graph.Nodes.Count == 0)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            DrawDateBands(painter);
            DrawEdges(painter);
            DrawNodes(painter);
        }

        /// <summary>날짜가 같은 연속 칸을 묶어 띠로 번갈아 칠한다.</summary>
        private void DrawDateBands(Painter2D painter)
        {
            painter.fillColor = _dateBandColor;
            int bandIndex = 0;
            foreach ((int start, int end) in DateRuns())
            {
                if (bandIndex++ % 2 == 0)
                {
                    continue;
                }
                float x0 = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(start - 0.5f), 0)).x;
                float x1 = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(end + 0.5f), 0)).x;
                if (x1 < 0 || x0 > layout.width)
                {
                    continue;
                }
                painter.BeginPath();
                painter.MoveTo(new Vector2(x0, 0));
                painter.LineTo(new Vector2(x1, 0));
                painter.LineTo(new Vector2(x1, layout.height));
                painter.LineTo(new Vector2(x0, layout.height));
                painter.ClosePath();
                painter.Fill();
            }
        }

        private void DrawEdges(Painter2D painter)
        {
            painter.lineWidth = Mathf.Max(1.5f, 3f * Viewport.Zoom);
            foreach (GitGraphEdge edge in _graph.Edges)
            {
                Vector2 from = Viewport.ToCanvas(GraphStyle.WorldPosition(edge.Parent));
                Vector2 to = Viewport.ToCanvas(GraphStyle.WorldPosition(edge.Child));
                if (Mathf.Max(from.x, to.x) < -50f || Mathf.Min(from.x, to.x) > layout.width + 50f)
                {
                    continue;
                }

                // 선 색은 새 커밋 쪽 줄 색. 병합으로 들어오는 선은 들어오는 가지(부모) 색.
                Color color = GraphStyle.LaneColor(edge.IsMergeEdge ? edge.Parent.Lane : edge.Child.Lane);
                color.a = Matches(edge.Parent) || Matches(edge.Child) ? 0.9f : 0.25f;
                painter.strokeColor = color;

                painter.BeginPath();
                painter.MoveTo(from);
                if (Mathf.Approximately(from.y, to.y))
                {
                    painter.LineTo(to);
                }
                else
                {
                    float bend = Mathf.Max(20f, (to.x - from.x) * 0.5f);
                    painter.BezierCurveTo(from + new Vector2(bend, 0), to - new Vector2(bend, 0), to);
                }
                painter.Stroke();
            }

            // 기간 밖으로 이어지는 히스토리: 왼쪽으로 짧은 흐린 선.
            painter.strokeColor = _truncatedColor;
            foreach (GitGraphNode node in _graph.TruncatedNodes)
            {
                Vector2 p = Viewport.ToCanvas(GraphStyle.WorldPosition(node));
                painter.BeginPath();
                painter.MoveTo(p);
                painter.LineTo(p - new Vector2(GraphStyle.ColumnWidth * 0.6f * Viewport.Zoom, 0));
                painter.Stroke();
            }
        }

        private void DrawNodes(Painter2D painter)
        {
            float radius = GraphStyle.NodeRadius * Viewport.Zoom;
            bool searching = _search.Length > 0;
            foreach (GitGraphNode node in _graph.Nodes)
            {
                Vector2 p = Viewport.ToCanvas(GraphStyle.WorldPosition(node));
                if (IsVisibleX(p.x, GraphStyle.ColumnWidth) == false)
                {
                    continue;
                }

                bool matches = Matches(node);
                float alpha = matches ? 1f : 0.3f;
                if (node.Commit.Hash == _selectedHash)
                {
                    FillCircle(painter, p, radius + 7f, _selectionColor);
                }
                else if (searching && matches)
                {
                    FillCircle(painter, p, radius + 7f, _searchColor);
                }

                bool isLaneTip = node.Column == node.Lane.LastColumn;
                Color ring = node.IsHead ? _headColor : GraphStyle.LaneColor(node.Lane);
                ring.a = alpha;
                float hover = HoverAmount(node);
                if (hover > 0f)
                {
                    DrawHoverGlow(painter, p, radius, ring, hover, node == _hovered);
                }
                float ringRadius = radius + (isLaneTip ? 4f : 2.5f) + 2f * hover;
                DrawShadow(painter, p, ringRadius, alpha);
                FillCircle(painter, p, ringRadius, ring);

                Color fill = GraphStyle.AuthorColor(node.Commit.AuthorName);
                fill = Color.Lerp(fill, Color.white, 0.15f * hover);
                fill.a = alpha;
                FillCircle(painter, p, radius, fill);
            }
        }

        /// <summary>
        /// 0(장식 없음)~1(다 드러남). 차오름·사라짐에 ease-out 을 걸어 끝이 부드럽게 멈추게 한다.
        /// </summary>
        private float HoverAmount(GitGraphNode node)
        {
            float linear = node == _hovered ? _hoverIn : node == _hoverFading ? _hoverOut : 0f;
            return 1f - (1f - linear) * (1f - linear);
        }

        /// <summary>
        /// 테두리 아래에 깔리는 빛 번짐과, 올려 두는 동안 바깥으로 퍼지며 옅어지는 고리.
        /// </summary>
        private void DrawHoverGlow(Painter2D painter, Vector2 center, float radius, Color color, float amount, bool isPulsing)
        {
            // 많이 축소해도 장식이 커밋보다 커 보이지 않게, 확대만큼만 키운다(최소 0.6배).
            float scale = Mathf.Max(Viewport.Zoom, 0.6f);
            float[] offsets = { 16f, 11f, 7f };
            float[] strengths = { 0.06f, 0.1f, 0.16f };
            for (int i = 0; i < offsets.Length; i++)
            {
                Color glow = color;
                glow.a = color.a * strengths[i] * amount;
                FillCircle(painter, center, radius + offsets[i] * scale, glow);
            }

            if (isPulsing == false)
            {
                return;
            }
            float phase = (float)((Time.realtimeSinceStartupAsDouble - _hoverStartTime) % HoverPulseSeconds / HoverPulseSeconds);
            Color pulse = color;
            pulse.a = color.a * 0.7f * (1f - phase) * amount;
            painter.strokeColor = pulse;
            painter.lineWidth = 2f;
            painter.BeginPath();
            painter.Arc(center, radius + (5f + phase * 16f) * scale, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Stroke();
        }

        /// <summary>
        /// 장식이 움직이는 동안만 매 프레임 다시 그린다. 다 사라지면 멈춘다(가만히 있는 그래프는 다시 그리지 않는다).
        /// </summary>
        private void TickHover()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float step = (float)(now - _lastHoverTick) / HoverFadeSeconds;
            _lastHoverTick = now;
            _hoverIn = _hovered == null ? 0f : Mathf.Min(1f, _hoverIn + step);
            _hoverOut = Mathf.Max(0f, _hoverOut - step);
            if (_hoverOut <= 0f)
            {
                _hoverFading = null;
            }
            MarkDirtyRepaint();
            if (_hovered == null && _hoverFading == null)
            {
                _hoverTicker.Pause();
            }
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle style = evt.customStyle;
            _selectionColor = style.TryGetValue(SelectionProperty, out Color selection) ? selection : GraphStyle.Selection;
            _searchColor = style.TryGetValue(SearchProperty, out Color search) ? search : GraphStyle.SearchMatch;
            _headColor = style.TryGetValue(HeadProperty, out Color head) ? head : GraphStyle.Head;
            _dateBandColor = style.TryGetValue(DateBandProperty, out Color band) ? band : GraphStyle.DateBand;
            _truncatedColor = style.TryGetValue(TruncatedProperty, out Color truncated) ? truncated : GraphStyle.Truncated;
            _shadowColor = style.TryGetValue(ShadowProperty, out Color shadow) ? shadow : new Color(0f, 0f, 0f, 0.5f);
            // 이름표 그림자 색도 테마를 따르므로 다시 단다.
            UpdateLabels();
            MarkDirtyRepaint();
        }

        /// <summary>
        /// 원 아래 그림자. Painter2D 는 흐림이 없으므로 아래로 조금 내린 원을 바깥에서 안쪽으로 조금씩 줄이며 옅게 겹쳐 번지게 한다(USS drop-shadow 와 비슷한 결).
        /// 겹이 적으면 경계가 계단처럼 보이므로, 한 겹 간격을 0.5px 남짓으로 두고 진하기는 겹마다 조금씩만 더한다.
        /// </summary>
        private void DrawShadow(Painter2D painter, Vector2 center, float radius, float alpha)
        {
            const int Layers = 12;
            float scale = Mathf.Max(Viewport.Zoom, 0.6f);
            Vector2 drop = new(0f, 2f * scale);
            float spread = 6f * scale;
            Color color = _shadowColor;
            color.a *= 0.06f * alpha;
            for (int i = 0; i < Layers; i++)
            {
                FillCircle(painter, center + drop, radius + spread * (1f - (float)i / Layers), color);
            }
        }

        /// <summary>
        /// 이름표(줄 이름·HEAD·브랜치·태그) 아래 그림자(<see cref="GitShadow"/>). 이름표는 재사용하며 클래스를 매번 지우므로 색은 직접 넣는다.
        /// </summary>
        private void ApplyShadow(VisualElement element)
        {
            GitShadow.Add(element, GitShadowSize.Small);
            GitShadow.SetColor(element, _shadowColor);
        }

        private static void FillCircle(Painter2D painter, Vector2 center, float radius, Color color)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.Arc(center, radius, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();
            painter.Fill();
        }

        /// <summary>날짜가 같은 연속 칸의 (시작, 끝) 칸 번호.</summary>
        private IEnumerable<(int start, int end)> DateRuns()
        {
            List<GitGraphNode> nodes = _graph.Nodes;
            int start = 0;
            for (int i = 1; i <= nodes.Count; i++)
            {
                if (i == nodes.Count || nodes[i].Commit.Date.Date != nodes[start].Commit.Date.Date)
                {
                    yield return (start, i - 1);
                    start = i;
                }
            }
        }

        // ── 글자 ──────────────────────────────────────────

        private void UpdateLabels()
        {
            _labels.Begin();
            _headerLabels.Begin();
            _occupied.Clear();
            if (_graph != null && _graph.Nodes.Count > 0 && layout.width > 1f)
            {
                EnableInClassList(HiddenTextModifier, Viewport.Zoom < GraphStyle.TextZoomThreshold);
                PlaceLaneLabels();
                PlaceNodeLabels();
                PlaceDateLabels();
            }
            _labels.End();
            _headerLabels.End();
            UpdateTooltip();
        }

        private void PlaceLaneLabels()
        {
            float radius = GraphStyle.NodeRadius * Viewport.Zoom;
            foreach (GitGraphLane lane in _graph.Lanes)
            {
                Vector2 first = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(lane.FirstColumn), GraphStyle.OriginY + lane.Row * GraphStyle.RowHeight));
                if (IsVisibleX(first.x, 300f) == false)
                {
                    continue;
                }
                // 이름 없는(병합된) 줄은 병합 메시지("Merge branch 'master' of …")에서 딴 이름을 "master 에서 병합" 처럼 출처로 보인다 —
                // 그냥 "master" 라고 쓰면 진짜 master 줄과 헷갈렸다. 이름을 못 찾은 줄은 "(병합된 브랜치)" 그대로.
                string text = lane.IsAnonymous == false ? "/" + lane.Name
                    : lane.Name == GitText.Get("graph.mergedBranch") ? lane.Name
                    : GitText.Format("graph.mergedFrom", lane.Name);
                // 머리표는 줄 첫 커밋 왼쪽으로 뻗는다 — 같은 행에서 앞서 끝난 줄이 있으면 그 마지막 커밋까지만 쓰고 넘치면 말줄임.
                // 가지가 많은 곳에서 머리표가 남의 커밋·이름표를 덮어 읽기 어려웠다.
                float right = first.x - radius - 8f;
                float maxWidth = float.PositiveInfinity;
                if (PreviousLaneEnd(lane, out int previousColumn))
                {
                    float previousX = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(previousColumn), 0f)).x;
                    maxWidth = right - (previousX + radius + 6f);
                    if (maxWidth < MinLaneLabelWidth)
                    {
                        continue;
                    }
                }
                string[] modifiers = lane.IsAnonymous ? new[] { Block + "__lane--anonymous" } : Array.Empty<string>();
                Label label = _labels.Place(Block + "__lane", text, new Vector2(right, first.y - 11f), Vector2.zero, 0f, 1f, modifiers);
                label.style.maxWidth = float.IsPositiveInfinity(maxWidth) ? new StyleLength(StyleKeyword.None) : new StyleLength(maxWidth);
                label.style.borderBottomColor = GraphStyle.LaneColor(lane);
                ApplyShadow(label);
                Vector2 size = MeasureText(label);
                float width = Mathf.Min(size.x + LaneLabelPadding.x, maxWidth);
                _occupied.Add(new Rect(right - width, first.y - 11f, width, size.y + LaneLabelPadding.y));
            }
        }

        private const float MinLaneLabelWidth = 28f;
        // .branch-graph__lane 의 좌우 여백(8 + 8), 위아래 여백(3 + 4)·밑줄(2).
        private static readonly Vector2 LaneLabelPadding = new(16f, 9f);
        // .branch-graph__pill 의 좌우 여백(5 + 5).
        private const float PillPadding = 10f;
        // 이름표끼리 이만큼은 떨어져 있어야 한다 — 맞닿아도 그림자 때문에 한 덩어리로 보였다.
        private const float LabelGap = 3f;

        /// <summary>
        /// 그 줄과 같은 행에서 그 줄보다 앞에 끝난 줄 가운데 가장 늦게 끝난 줄의 마지막 칸. 그래프가 바뀔 때만 다시 센다.
        /// </summary>
        private bool PreviousLaneEnd(GitGraphLane lane, out int column)
        {
            if (_previousLaneEndGraph != _graph)
            {
                _previousLaneEndGraph = _graph;
                _previousLaneEnd.Clear();
                Dictionary<int, List<GitGraphLane>> byRow = new();
                foreach (GitGraphLane each in _graph.Lanes)
                {
                    if (byRow.TryGetValue(each.Row, out List<GitGraphLane> row) == false)
                    {
                        row = new List<GitGraphLane>();
                        byRow[each.Row] = row;
                    }
                    row.Add(each);
                }
                foreach (List<GitGraphLane> row in byRow.Values)
                {
                    row.Sort((a, b) => a.FirstColumn.CompareTo(b.FirstColumn));
                    int lastEnd = -1;
                    foreach (GitGraphLane each in row)
                    {
                        if (lastEnd >= 0 && lastEnd < each.FirstColumn)
                        {
                            _previousLaneEnd[each] = lastEnd;
                        }
                        lastEnd = Mathf.Max(lastEnd, each.LastColumn);
                    }
                }
            }
            return _previousLaneEnd.TryGetValue(lane, out column);
        }

        private static Vector2 MeasureText(Label label)
        {
            return label.MeasureTextSize(label.text, 0f, MeasureMode.Undefined, 0f, MeasureMode.Undefined);
        }

        private bool IsOccupied(Rect rect)
        {
            Rect padded = new(rect.x - LabelGap, rect.y - LabelGap, rect.width + LabelGap * 2f, rect.height + LabelGap * 2f);
            foreach (Rect each in _occupied)
            {
                if (each.Overlaps(padded))
                {
                    return true;
                }
            }
            return false;
        }

        private void PlaceNodeLabels()
        {
            float zoom = Viewport.Zoom;
            float radius = GraphStyle.NodeRadius * zoom;
            float initialsSize = Mathf.Clamp(11f * zoom, 6f, 20f);
            float subjectSize = Mathf.Clamp(10f * zoom, 6f, 18f);
            float subjectWidth = GraphStyle.ColumnWidth * zoom - 10f;

            foreach (GitGraphNode node in _graph.Nodes)
            {
                Vector2 p = Viewport.ToCanvas(GraphStyle.WorldPosition(node));
                if (IsVisibleX(p.x, GraphStyle.ColumnWidth) == false)
                {
                    continue;
                }

                float opacity = Matches(node) ? 1f : 0.3f;
                Texture2D avatar = _avatars?.TryGet(node.Commit.AuthorEmail, node.Commit.Hash);
                if (avatar != null)
                {
                    _labels.PlaceCircleImage(Block + "__avatar", avatar, p, radius).style.opacity = opacity;
                }
                else
                {
                    Label initials = _labels.Place(Block + "__initials", GitDisplay.Initials(node.Commit.AuthorName),
                        new Vector2(p.x - radius, p.y - radius), new Vector2(radius * 2f, radius * 2f), initialsSize);
                    initials.style.opacity = opacity;
                }
                // 폭은 글자에 맞추고(바탕 상자가 글자 둘레만 선을 가리게) 칸 폭을 넘으면 말줄임.
                Label subject = _labels.Place(Block + "__subject", node.Commit.Subject,
                    new Vector2(p.x, p.y + radius + 6f), Vector2.zero, subjectSize, 0.5f);
                subject.style.maxWidth = subjectWidth;
                subject.style.opacity = opacity;

                PlacePills(node, p, radius);
            }
        }

        /// <summary>
        /// 커밋 위에 HEAD·브랜치·태그 이름표를 쌓는다. 셋 넘으면 "+n" 으로 줄인다.
        /// HEAD 커밋은 "HEAD" 대신 지금 브랜치 이름을 HEAD 색으로(detached 면 "HEAD"), 줄 끝 커밋은 그 줄의 브랜치 이름도 붙인다 —
        /// 줄 이름과 같은 브랜치는 <see cref="GitGraphNode.RefLabels"/> 에서 빠지고(줄 머리표가 맡는다) 그 머리표는 줄이 시작하는 왼쪽 끝에 있어,
        /// HEAD 근처를 보면 지금 브랜치(master) 이름이 어디에도 보이지 않았다.
        /// </summary>
        private void PlacePills(GitGraphNode node, Vector2 p, float radius)
        {
            List<(string text, string modifier)> pills = new();
            string headName = null;
            if (node.IsHead)
            {
                headName = string.IsNullOrEmpty(node.HeadBranch) ? "HEAD" : node.HeadBranch;
                pills.Add((headName, Block + "__pill--head"));
            }
            if (node.Column == node.Lane.LastColumn && node.Lane.IsAnonymous == false && node.Lane.Name != headName)
            {
                pills.Add((node.Lane.Name, null));
            }
            foreach (string label in node.RefLabels)
            {
                if (label == headName)
                {
                    continue;
                }
                bool isTag = label.StartsWith("tag: ", StringComparison.Ordinal);
                pills.Add((isTag ? label.Substring(5) : label, isTag ? Block + "__pill--tag" : null));
            }

            // 셋까지 쌓고 넘치면 셋째 자리를 "+n" 으로. 위로 쌓다가 줄 머리표·다른 커밋 이름표와 겹치는 자리가 나오면 거기서 멈추고
            // 바로 앞 자리를 "+n" 으로 바꾼다(첫 자리만 들어가면 첫 이름표만). 지금 브랜치(HEAD) 이름표는 겹쳐도 보인다.
            const float pillHeight = 16f;
            int slots = Mathf.Min(pills.Count, 3);
            List<(Label label, Rect rect)> placed = new();
            for (int i = 0; i < slots; i++)
            {
                bool isOverflow = i == 2 && pills.Count > 3;
                Label pill = PlacePill(isOverflow ? $"+{pills.Count - 2}" : pills[i].text, isOverflow ? null : pills[i].modifier, p, radius, i, pillHeight, out Rect rect);
                if (IsOccupied(rect) && (i > 0 || headName == null))
                {
                    pill.style.display = DisplayStyle.None;
                    if (placed.Count >= 2)
                    {
                        int last = placed.Count - 1;
                        placed[last].label.style.display = DisplayStyle.None;
                        PlacePill($"+{pills.Count - last}", null, p, radius, last, pillHeight, out Rect overflowRect);
                        placed[last] = (null, overflowRect);
                    }
                    break;
                }
                placed.Add((pill, rect));
            }
            foreach ((Label _, Rect rect) in placed)
            {
                _occupied.Add(rect);
            }
        }

        private Label PlacePill(string text, string modifier, Vector2 p, float radius, int slot, float pillHeight, out Rect rect)
        {
            string[] modifiers = modifier == null ? Array.Empty<string>() : new[] { modifier };
            Vector2 position = new(p.x, p.y - radius - 6f - (slot + 1) * (pillHeight + 2f));
            Label pill = _labels.Place(Block + "__pill", text, position, new Vector2(0f, pillHeight), 0f, 0.5f, modifiers);
            ApplyShadow(pill);
            float width = MeasureText(pill).x + PillPadding;
            rect = new Rect(p.x - width / 2f, position.y, width, pillHeight);
            return pill;
        }

        private void PlaceDateLabels()
        {
            foreach ((int start, int end) in DateRuns())
            {
                float x0 = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(start - 0.5f), 0)).x;
                float x1 = Viewport.ToCanvas(new Vector2(GraphStyle.ColumnCenterX(end + 0.5f), 0)).x;
                if (x1 < 0 || x0 > layout.width)
                {
                    continue;
                }
                // 띠가 화면 밖으로 일부 나가도 날짜는 보이는 부분 가운데에 둔다.
                float left = Mathf.Max(x0, 0f);
                float right = Mathf.Min(x1, layout.width);
                _headerLabels.Place(Block + "__date", _graph.Nodes[start].Commit.Date.ToString("yyyy-MM-dd"),
                    new Vector2(left, 0f), new Vector2(right - left, GraphStyle.HeaderHeight), 0f);
            }
        }

        private void UpdateTooltip()
        {
            if (_hovered == null || _isPanning)
            {
                _tooltip.SetVisible(false);
                return;
            }
            GitCommitInfo commit = _hovered.Commit;
            _tooltip.text = GitText.Format("explorer.tooltip", commit.Subject, commit.ShortHash, commit.AuthorName, GitDisplay.FormatDate(commit.Date), GitDisplay.Nfc(_hovered.Lane.Name));
            _tooltip.style.left = _pointer.x + 14f;
            _tooltip.style.top = _pointer.y + 14f;
            _tooltip.SetVisible(true);
            _tooltip.BringToFront();
        }

        // ── 입력 ──────────────────────────────────────────

        private GitGraphNode HitTest(Vector2 canvasPosition)
        {
            if (_graph == null)
            {
                return null;
            }
            float radius = GraphStyle.NodeRadius * Viewport.Zoom + 3f;
            foreach (GitGraphNode node in _graph.Nodes)
            {
                if (Vector2.Distance(canvasPosition, Viewport.ToCanvas(GraphStyle.WorldPosition(node))) <= radius)
                {
                    return node;
                }
            }
            return null;
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            Vector2 position = evt.localPosition;
            if (evt.button == 0)
            {
                GitGraphNode hit = HitTest(position);
                if (hit != null)
                {
                    NodeSelected?.Invoke(hit);
                    evt.StopPropagation();
                    return;
                }
            }
            if (evt.button == 0 || evt.button == 2)
            {
                _isPanning = true;
                this.CapturePointer(evt.pointerId);
                SetHovered(null);
                evt.StopPropagation();
            }
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            _pointer = evt.localPosition;
            if (_isPanning && this.HasPointerCapture(evt.pointerId))
            {
                Viewport.PanBy(evt.deltaPosition);
                Refresh();
                return;
            }
            SetHovered(HitTest(_pointer));
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_isPanning == false)
            {
                return;
            }
            _isPanning = false;
            this.ReleasePointer(evt.pointerId);
            ViewportChanged?.Invoke();
        }

        private void OnWheel(WheelEvent evt)
        {
            // 가로 휠(트랙패드 좌우 스와이프·Shift+휠)은 좌우로 옮기고, 세로 휠은 예전처럼 확대·축소한다.
            float horizontal = GraphViewport.HorizontalWheel(evt.delta, evt.shiftKey);
            if (horizontal != 0f)
            {
                Viewport.PanBy(new Vector2(-horizontal * GraphViewport.WheelPanStep, 0f));
                // 그래프가 마우스 밑에서 움직였으니 올려 둔 커밋을 다시 고른다.
                _pointer = evt.localMousePosition;
                SetHovered(HitTest(_pointer));
            }
            else
            {
                Viewport.ZoomAround(evt.localMousePosition, Viewport.Zoom * (1f - evt.delta.y * 0.05f));
            }
            Refresh();
            ViewportChanged?.Invoke();
            evt.StopPropagation();
        }

        private void OnContextMenu(ContextualMenuPopulateEvent evt)
        {
            GitGraphNode hit = HitTest(evt.localMousePosition);
            if (hit == null)
            {
                return;
            }
            NodeSelected?.Invoke(hit);
            NodeContextMenu?.Invoke(evt.menu, hit);
        }

        private void SetHovered(GitGraphNode node)
        {
            if (_hovered == node && node == null)
            {
                return;
            }
            if (_hovered != node)
            {
                // 떠나는 커밋은 지금 드러난 만큼에서 사라지기 시작한다(빠르게 옮겨 다녀도 뚝 끊기지 않게).
                if (_hovered != null)
                {
                    _hoverFading = _hovered;
                    _hoverOut = _hoverIn;
                }
                _hoverIn = 0f;
                _hoverStartTime = Time.realtimeSinceStartupAsDouble;
                StartHoverTicker();
            }
            _hovered = node;
            // 커밋 위에서는 손가락 커서(누르면 고른다). USS 의 cursor 는 요소 단위라 수식어로 켜고 끈다.
            EnableInClassList(Block + "--node-hover", node != null);
            UpdateTooltip();
        }

        private void StartHoverTicker()
        {
            _lastHoverTick = Time.realtimeSinceStartupAsDouble;
            if (_hoverTicker == null)
            {
                _hoverTicker = schedule.Execute(TickHover).Every(16);
                return;
            }
            _hoverTicker.Resume();
        }
    }
}
