using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 분할(<see cref="TwoPaneSplitView"/>)의 고정 칸이 창보다 커지지 않게 줄인다.
    ///
    /// Unity 의 분할은 고정 칸에 크기와 <c>flex-shrink: 0</c> 을 인라인으로 넣고, 창이 작아져도 줄이지 않는다
    /// (넘치면 오히려 고정 칸을 분할 전체로 키운다). 그래서 창을 낮추면 그래프·커밋 목록이 사라지거나 커밋 상세가 창 밖으로 잘린다.
    /// 이 클래스는 분할 크기가 바뀔 때 고정 칸을 "사용자가 고른 크기"와 "늘어나는 칸에 최소 공간을 남긴 크기" 중 작은 값으로 맞춘다.
    /// 사용자가 끌어 바꾼 크기는 기억해 두었다가, 창이 다시 커지면 그 크기로 돌아간다.
    /// </summary>
    public sealed class SplitPaneClamp
    {
        /// <summary>
        /// 늘어나는 칸에 최소로 남길 크기. 목록 머리 줄 + 서너 줄, 또는 그래프의 오른쪽 아래 버튼 세 개가 들어가는 높이.
        /// </summary>
        public const float MinFlexedPane = 120f;

        /// <summary>
        /// 고정 칸의 최소 크기. 커밋 상세의 머리 줄 + 메시지 두세 줄이 보이는 높이.
        /// </summary>
        public const float MinFixedPane = 80f;

        private const string DragLineAnchorClass = "unity-two-pane-split-view__dragline-anchor";

        private readonly TwoPaneSplitView _split;
        private readonly VisualElement _dragLineAnchor;
        private float _preferred;

        private SplitPaneClamp(TwoPaneSplitView split)
        {
            _split = split;
            _preferred = split.fixedPaneInitialDimension;
            _dragLineAnchor = split.Q(className: DragLineAnchorClass);

            // Unity 의 분할도 같은 이벤트에서 고정 칸을 다시 잡으므로, 그 뒤에 맞추도록 한 박자 미룬다.
            // 칸이 접혔다 펴질 때(커밋 상세 열기)는 분할 크기가 그대로라 칸 쪽 이벤트도 본다.
            _split.RegisterCallback<GeometryChangedEvent>(_ => _split.schedule.Execute(Apply));
            foreach (VisualElement pane in _split.Children())
            {
                pane.RegisterCallback<GeometryChangedEvent>(_ => _split.schedule.Execute(Apply));
            }
            // 사용자가 경계선을 끌어 놓은 크기만 "고른 크기"로 기억한다. 우리가 줄인 크기를 고른 크기로 착각하지 않게.
            _dragLineAnchor?.RegisterCallback<PointerUpEvent>(_ => RememberDraggedSize(), TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 루트 아래의 모든 <c>.view__split</c> 분할에 붙인다.
        /// </summary>
        public static void AttachAll(VisualElement root)
        {
            root.Query<TwoPaneSplitView>(className: "view__split").ForEach(split => new SplitPaneClamp(split));
        }

        /// <summary>
        /// 고정 칸 크기를 정한다. 공간이 넉넉하면 <paramref name="preferred"/> 그대로, 모자라면 늘어나는 칸에 <paramref name="minFlexed"/> 를 남기고,
        /// 그래도 모자라면 <paramref name="minFixed"/> 아래로는 줄이지 않는다(그때는 늘어나는 칸이 양보한다).
        /// 예: (260, 600, 120, 80) → 260, (260, 300, 120, 80) → 180, (260, 150, 120, 80) → 80.
        /// </summary>
        public static float Fit(float preferred, float available, float minFlexed, float minFixed)
        {
            float room = available - minFlexed;
            return Mathf.Max(minFixed, Mathf.Min(preferred, room));
        }

        private bool IsVertical => _split.orientation == TwoPaneSplitViewOrientation.Vertical;

        private bool IsSplitShown =>
            _split.fixedPane != null
            && _split.fixedPane.resolvedStyle.display != DisplayStyle.None
            && _split.flexedPane != null
            && _split.flexedPane.resolvedStyle.display != DisplayStyle.None;

        private void RememberDraggedSize()
        {
            if (IsSplitShown == false)
            {
                return;
            }
            _preferred = IsVertical ? _split.fixedPane.resolvedStyle.height : _split.fixedPane.resolvedStyle.width;
        }

        private void Apply()
        {
            if (IsSplitShown == false)
            {
                return;
            }

            float available = IsVertical ? _split.layout.height : _split.layout.width;
            if (float.IsNaN(available) || available <= 0f)
            {
                return;
            }

            float target = Fit(_preferred, available, MinFlexedPane, MinFixedPane);
            // 경계선은 분할이 자기 크기가 바뀔 때만 옮기므로, 고정 칸을 바꾸면 경계선도 같이 옮긴다.
            float dragLinePosition = _split.fixedPaneIndex == 0 ? target : available - target;
            if (IsVertical)
            {
                _split.fixedPane.style.height = target;
                if (_dragLineAnchor != null)
                {
                    _dragLineAnchor.style.top = dragLinePosition;
                }
            }
            else
            {
                _split.fixedPane.style.width = target;
                if (_dragLineAnchor != null)
                {
                    _dragLineAnchor.style.left = dragLinePosition;
                }
            }
        }
    }
}
