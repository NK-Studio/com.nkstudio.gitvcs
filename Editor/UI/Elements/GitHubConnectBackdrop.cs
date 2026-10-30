#if UNITY_6000_6_OR_NEWER
using System.Collections.Generic;
#endif
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 연결 화면의 배경: 흐리게 번진 색 원 셋(오로라)이 천천히 떠다니고, 그 위로 Git 그래프처럼 갈라지고 합쳐지는 옅은 가지 선이 아래로 흐른다.
    /// 원의 색·흐림과 선 색은 USS(테마 토큰 --git-connect-*), 움직임은 여기서 준다. 승인을 기다리는 동안(<see cref="Waiting"/>)은 오로라 색이 천천히 돈다(hue-rotate).
    /// Unity 가 앞에 있지 않으면 멈춘다(다시 그리기 비용).
    /// </summary>
    public sealed class GitHubConnectBackdrop : VisualElement
    {
        private const string Block = "github-connect__backdrop";

        /// <summary>원 지름: 화면 긴 변의 이 비율(창 크기와 상관없이 같은 느낌으로 번진다).</summary>
        private const float BlobScale = 0.34f;

        /// <summary>움직임 갱신 간격(ms). 30fps 정도면 느린 흐름이 매끄럽다.</summary>
        private const long FrameMs = 33;

        /// <summary>가지 선 사이 간격·커밋 점 간격·아래로 흐르는 속도(px/s).</summary>
        private const float LaneGap = 120f;
        private const float CommitGap = 72f;
        private const float FlowSpeed = 10f;

        /// <summary>기다리는 동안 오로라 색이 한 바퀴 도는 시간(초).</summary>
        private const float HueCycleSeconds = 12f;

        private readonly VisualElement _aurora;
        private readonly VisualElement[] _blobs = new VisualElement[3];
        private readonly VisualElement _lanes;

        /// <summary>
        /// 원마다 떠다니는 중심·흔들림 폭(둘 다 화면 비율)·주기(초)·위상. 중심을 가운데 카드 둘레에 모아 원이 카드 뒤를 지나가게 한다 —
        /// 유리 카드(backdrop-filter)에 색이 흐리게 비쳐야 유리처럼 보인다.
        /// </summary>
        private static readonly (Vector2 Center, Vector2 Amplitude, float Period, float Phase)[] Drift =
        {
            (new Vector2(0.36f, 0.38f), new Vector2(0.10f, 0.08f), 26f, 0f),
            (new Vector2(0.64f, 0.44f), new Vector2(0.09f, 0.10f), 31f, 2.1f),
            (new Vector2(0.50f, 0.66f), new Vector2(0.12f, 0.06f), 37f, 4.2f),
        };

        private float _time;
        private float _hue;
        private double _lastTick;

        /// <summary>승인을 기다리는 중이면 오로라 색이 천천히 돈다.</summary>
        public bool Waiting { get; set; }

        public GitHubConnectBackdrop()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList(Block);

            _aurora = new VisualElement { pickingMode = PickingMode.Ignore };
            _aurora.AddToClassList(Block + "-aurora");
            Add(_aurora);
            for (int i = 0; i < _blobs.Length; i++)
            {
                VisualElement blob = new() { pickingMode = PickingMode.Ignore };
                blob.AddToClassList(Block + "-blob");
                blob.AddToClassList(Block + "-blob--" + (char)('a' + i));
                _aurora.Add(blob);
                _blobs[i] = blob;
            }

#if !UNITY_6000_6_OR_NEWER
            // 흐림(filter)이 없는 버전에서는 원이 또렷한 동그라미로 보이므로 오로라는 끄고 가지 선만 남긴다.
            _aurora.style.display = DisplayStyle.None;
#endif

            _lanes = new VisualElement { pickingMode = PickingMode.Ignore };
            _lanes.AddToClassList(Block + "-lanes");
            _lanes.generateVisualContent += DrawLanes;
            Add(_lanes);

            RegisterCallback<GeometryChangedEvent>(_ => Place());
            schedule.Execute(Tick).Every(FrameMs);
        }

        private void Tick()
        {
            double now = Time.realtimeSinceStartupAsDouble;
            float dt = _lastTick > 0 ? Mathf.Min((float)(now - _lastTick), 0.1f) : 0f;
            _lastTick = now;
            if (InternalEditorUtility.isApplicationActive == false)
            {
                return;
            }
            _time += dt;
            if (Waiting)
            {
                _hue = (_hue + dt * 360f / HueCycleSeconds) % 360f;
            }
            else if (_hue > 0f)
            {
                // 기다림이 끝나면 가까운 쪽으로 원래 색에 돌아온다.
                _hue = _hue > 180f ? Mathf.Min(_hue + dt * 120f, 360f) % 360f : Mathf.Max(_hue - dt * 120f, 0f);
            }
            Place();
            _lanes.MarkDirtyRepaint();
        }

        /// <summary>원을 지금 시간의 떠다니는 자리에 놓고, 오로라 색 회전을 넣는다.</summary>
        private void Place()
        {
            Rect area = contentRect;
            if (float.IsNaN(area.width) || area.width <= 0f)
            {
                return;
            }
            float size = Mathf.Max(area.width, area.height) * BlobScale;
            for (int i = 0; i < _blobs.Length; i++)
            {
                (Vector2 center, Vector2 amplitude, float period, float phase) = Drift[i];
                float w = Mathf.PI * 2f / period;
                float x = area.width * (center.x + amplitude.x * Mathf.Sin(_time * w + phase));
                float y = area.height * (center.y + amplitude.y * Mathf.Cos(_time * w * 0.8f + phase));
                VisualElement blob = _blobs[i];
                if (Mathf.Abs(blob.resolvedStyle.width - size) > 1f)
                {
                    blob.style.width = size;
                    blob.style.height = size;
                    blob.style.borderTopLeftRadius = blob.style.borderTopRightRadius = blob.style.borderBottomLeftRadius = blob.style.borderBottomRightRadius = size * 0.5f;
                }
                blob.style.translate = new Translate(x - size * 0.5f, y - size * 0.5f);
            }
#if UNITY_6000_6_OR_NEWER
            // filter 는 Unity 6.6 부터(그 전 버전은 오로라를 끈다 — 생성자).
            if (_hue > 0.01f)
            {
                FilterFunction hue = new(FilterFunctionType.HueRotate);
                hue.AddParameter(new FilterParameter(_hue));
                _aurora.style.filter = new List<FilterFunction> { hue };
            }
            else
            {
                _aurora.style.filter = StyleKeyword.Null;
            }
#endif
        }

        /// <summary>
        /// Git 그래프 모양의 가지 선: 세로 줄 몇 개가 아래로 흐르고, 일정 간격마다 옆 줄로 갈라지거나 합쳐지는 곡선과 커밋 점을 찍는다.
        /// 색은 이 요소의 글자색(USS --git-connect-lanes, 아주 옅게)을 쓴다.
        /// </summary>
        private void DrawLanes(MeshGenerationContext context)
        {
            Rect area = _lanes.contentRect;
            if (area.width <= 0f || area.height <= 0f)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            Color color = _lanes.resolvedStyle.color;
            painter.strokeColor = color;
            painter.fillColor = color;
            painter.lineWidth = 2f;

            int laneCount = Mathf.Max(2, Mathf.CeilToInt(area.width / LaneGap) + 1);
            float left = (area.width - (laneCount - 1) * LaneGap) * 0.5f;
            // 한 칸(CommitGap)만큼 흐르면 0 으로 돌아오고, 그만큼 행 번호가 하나 밀린다(Hash) — 모양이 끊기지 않고 이어서 내려간다.
            float offset = _time * FlowSpeed % CommitGap;
            int rows = Mathf.CeilToInt(area.height / CommitGap) + 3;
            for (int lane = 0; lane < laneCount; lane++)
            {
                float x = left + lane * LaneGap;
                painter.BeginPath();
                painter.MoveTo(new Vector2(x, 0f));
                painter.LineTo(new Vector2(x, area.height));
                painter.Stroke();
                for (int row = -2; row < rows; row++)
                {
                    float y = row * CommitGap + offset;
                    // 줄마다 커밋 점 자리를 조금씩 엇갈리게 해 규칙적인 격자로 보이지 않게 한다.
                    int seed = Hash(lane, row);
                    if (seed % 3 != 0)
                    {
                        painter.BeginPath();
                        painter.Arc(new Vector2(x, y + (seed % 5) * 6f), 4f, 0f, 360f);
                        painter.Fill();
                    }
                    // 몇 칸마다 오른쪽 줄로 갈라지는(또는 합쳐지는) 곡선.
                    if (lane < laneCount - 1 && seed % 7 == 0)
                    {
                        float x2 = x + LaneGap;
                        bool merge = seed % 2 == 0;
                        Vector2 from = new(merge ? x2 : x, y);
                        Vector2 to = new(merge ? x : x2, y + CommitGap);
                        painter.BeginPath();
                        painter.MoveTo(from);
                        painter.BezierCurveTo(new Vector2(from.x, y + CommitGap * 0.6f), new Vector2(to.x, y + CommitGap * 0.4f), to);
                        painter.Stroke();
                    }
                }
            }
        }

        /// <summary>
        /// 흐르는 동안 같은 커밋 자리는 같은 모양이도록 (줄, 행)으로 정하는 의사 난수. 행은 흐름과 함께 움직이는 번호라 모양이 따라 내려간다.
        /// </summary>
        private int Hash(int lane, int row)
        {
            int flowRow = row - Mathf.FloorToInt(_time * FlowSpeed / CommitGap);
            unchecked
            {
                int h = lane * 73856093 ^ flowRow * 19349663;
                h ^= h >> 13;
                h *= 1274126177;
                return Mathf.Abs(h ^ (h >> 16));
            }
        }
    }
}
