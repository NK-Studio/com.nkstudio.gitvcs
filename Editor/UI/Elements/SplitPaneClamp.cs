using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 분할(<see cref="TwoPaneSplitView"/>)의 경계선 끌기와 창 크기 맞추기를 맡는다.
    ///
    /// 1) 창 크기 맞추기: Unity 의 분할은 고정 칸에 크기와 <c>flex-shrink: 0</c> 을 인라인으로 넣고, 창이 작아져도 줄이지 않는다.
    ///    그래서 창을 낮추면 그래프·커밋 목록이 사라지거나 커밋 상세가 창 밖으로 잘린다. 분할 크기가 바뀔 때 고정 칸을
    ///    "사용자가 고른 크기"와 "늘어나는 칸에 최소 공간을 남긴 크기" 중 작은 값으로 맞추고, 창이 다시 커지면 고른 크기로 돌린다.
    /// 2) 경계선 끌기: Unity 의 끌기 처리기는 포인터를 붙잡아(capture) 끄는데, 창 내용을 UI 배율(scale)로 키우면 실제 마우스에서
    ///    붙잡자마자 놓쳐서 경계선이 움직이지 않는다(2026-10-18 실측: 120% 에서 누른 직후 capture 가 풀림, 100% 에서는 정상).
    ///    그래서 끌기를 직접 한다 — 경계선을 누르면 Unity 처리기에 넘기지 않고, 패널 루트에서 이동·뗌을 받아(붙잡기 불필요)
    ///    분할의 로컬 좌표(배율이 반영된 좌표)로 바꿔 고정 칸 크기를 정한다.
    /// </summary>
    public sealed class SplitPaneClamp
    {
        /// <summary>
        /// 늘어나는 칸에 최소로 남길 크기. 목록 머리 줄 + 서너 줄, 또는 그래프의 오른쪽 아래 버튼 세 개가 들어가는 크기.
        /// </summary>
        public const float MinFlexedPane = 120f;

        /// <summary>
        /// 고정 칸의 최소 크기. 커밋 상세의 머리 줄 + 메시지 두세 줄, 또는 왼쪽 탭 이름이 보이는 크기.
        /// </summary>
        public const float MinFixedPane = 80f;

        private const string DragLineAnchorClass = "unity-two-pane-split-view__dragline-anchor";

        private readonly TwoPaneSplitView _split;
        private readonly VisualElement _dragLineAnchor;
        private float _preferred;

        /// <summary>
        /// 경계선을 끄는 중. 이동 중에는 크기 맞추기를 멈춘다(끄는 대로 기억해 둔 크기로 되돌리지 않게).
        /// </summary>
        private bool _isDragging;
        private float _dragStartPointer;
        private float _dragStartSize;
        private VisualElement _panelRoot;

        /// <summary>
        /// 경계선을 끌기 전까지 두 칸을 반씩 나눈다(<c>.view__split--half</c>). Fork·GitKraken 의 Unstaged·Staged 처럼
        /// 창 크기와 상관없이 반반으로 열리고, 창 크기가 바뀌어도 반을 지킨다. 한 번 끌면 그 크기를 따른다.
        /// </summary>
        private const string HalfModifier = "view__split--half";

        private bool _halfUntilDragged;

        private SplitPaneClamp(TwoPaneSplitView split)
        {
            _split = split;
            _preferred = split.fixedPaneInitialDimension;
            _halfUntilDragged = split.ClassListContains(HalfModifier);
            // 안쪽 칸에 든 다른 분할의 경계선을 집지 않도록 이 분할의 직접 자식에서 찾는다.
            _dragLineAnchor = split.hierarchy.Children().FirstOrDefault(child => child.ClassListContains(DragLineAnchorClass));

            // Unity 의 분할도 같은 이벤트에서 고정 칸을 다시 잡으므로, 그 뒤에 맞추도록 한 박자 미룬다.
            // 칸이 접혔다 펴질 때(커밋 상세 열기)는 분할 크기가 그대로라 칸 쪽 이벤트도 본다.
            _split.RegisterCallback<GeometryChangedEvent>(_ => _split.schedule.Execute(Apply));
            foreach (VisualElement pane in _split.Children())
            {
                pane.RegisterCallback<GeometryChangedEvent>(_ => _split.schedule.Execute(Apply));
            }
            _dragLineAnchor?.RegisterCallback<PointerDownEvent>(BeginDrag, TrickleDown.TrickleDown);
        }

        /// <summary>
        /// 루트 아래의 모든 <c>.view__split</c> 분할에 붙인다.
        /// </summary>
        public static void AttachAll(VisualElement root)
        {
            root.Query<TwoPaneSplitView>(className: "view__split").ForEach(Attach);
        }

        /// <summary>
        /// 분할 하나에 붙인다(<c>.view__split</c> 이 아닌 창 본체 분할 등).
        /// </summary>
        public static void Attach(TwoPaneSplitView split)
        {
            _ = new SplitPaneClamp(split);
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

        /// <summary>
        /// 경계선을 <paramref name="pointerDelta"/>(분할 로컬 좌표) 만큼 끌었을 때의 고정 칸 크기.
        /// 고정 칸이 앞(0)이면 경계선을 뒤로 끌수록 커지고, 뒤(1)면 앞으로 끌수록 커진다.
        /// 예: (380, +50, 0, 1000) → 430, (380, +50, 1, 1000) → 330.
        /// </summary>
        public static float DraggedSize(float startSize, float pointerDelta, int fixedPaneIndex, float available)
        {
            float size = fixedPaneIndex == 0 ? startSize + pointerDelta : startSize - pointerDelta;
            return Fit(size, available, MinFlexedPane, MinFixedPane);
        }

        private bool IsVertical => _split.orientation == TwoPaneSplitViewOrientation.Vertical;

        private bool IsSplitShown =>
            _split.fixedPane != null
            && _split.fixedPane.resolvedStyle.display != DisplayStyle.None
            && _split.flexedPane != null
            && _split.flexedPane.resolvedStyle.display != DisplayStyle.None;

        private float CurrentFixedSize => IsVertical ? _split.fixedPane.resolvedStyle.height : _split.fixedPane.resolvedStyle.width;

        private float Available => IsVertical ? _split.layout.height : _split.layout.width;

        /// <summary>
        /// 패널 좌표의 포인터 위치를 분할 로컬 좌표(UI 배율이 반영된 크기 단위)의 끄는 축 값으로.
        /// </summary>
        private float LocalAxis(Vector3 panelPosition)
        {
            Vector2 local = _split.WorldToLocal(panelPosition);
            return IsVertical ? local.y : local.x;
        }

        private void BeginDrag(PointerDownEvent evt)
        {
            if (evt.button != 0 || IsSplitShown == false || _split.panel == null)
            {
                return;
            }
            _isDragging = true;
            _dragStartPointer = LocalAxis(evt.position);
            _dragStartSize = CurrentFixedSize;
            _panelRoot = _split.panel.visualTree;
            _panelRoot.RegisterCallback<PointerMoveEvent>(OnDragMove, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<PointerUpEvent>(OnDragEnd, TrickleDown.TrickleDown);
            // Unity 의 끌기 처리기는 돌지 않게 한다(둘이 같이 돌면 크기를 서로 덮어쓴다).
            evt.StopImmediatePropagation();
        }

        private void OnDragMove(PointerMoveEvent evt)
        {
            if (_isDragging == false)
            {
                return;
            }
            // 버튼을 창 밖에서 놓아 뗌 이벤트를 놓친 경우: 다음 이동에서 끝낸다.
            if ((evt.pressedButtons & 1) == 0)
            {
                EndDrag();
                return;
            }
            float delta = LocalAxis(evt.position) - _dragStartPointer;
            SetFixedSize(DraggedSize(_dragStartSize, delta, _split.fixedPaneIndex, Available));
            evt.StopPropagation();
        }

        private void OnDragEnd(PointerUpEvent evt)
        {
            if (_isDragging)
            {
                evt.StopPropagation();
            }
            EndDrag();
        }

        /// <summary>
        /// 끌기를 마치면 놓은 크기를 고른 크기로 삼는다.
        /// </summary>
        private void EndDrag()
        {
            if (_panelRoot != null)
            {
                _panelRoot.UnregisterCallback<PointerMoveEvent>(OnDragMove, TrickleDown.TrickleDown);
                _panelRoot.UnregisterCallback<PointerUpEvent>(OnDragEnd, TrickleDown.TrickleDown);
                _panelRoot = null;
            }
            if (_isDragging == false)
            {
                return;
            }
            _isDragging = false;
            if (IsSplitShown && float.IsNaN(CurrentFixedSize) == false)
            {
                _preferred = CurrentFixedSize;
                _halfUntilDragged = false;
            }
            _split.schedule.Execute(Apply);
        }

        private void Apply()
        {
            if (_isDragging || IsSplitShown == false)
            {
                return;
            }
            float available = Available;
            if (float.IsNaN(available) || available <= 0f)
            {
                return;
            }
            if (_halfUntilDragged)
            {
                _preferred = available * 0.5f;
            }
            float target = Fit(_preferred, available, MinFlexedPane, MinFixedPane);
            if (Mathf.Abs(CurrentFixedSize - target) < 0.5f)
            {
                return;
            }
            SetFixedSize(target);
        }

        /// <summary>
        /// 고정 칸 크기와 경계선 위치를 함께 바꾼다(경계선은 분할이 자기 크기가 바뀔 때만 스스로 옮긴다).
        /// </summary>
        private void SetFixedSize(float size)
        {
            float dragLinePosition = _split.fixedPaneIndex == 0 ? size : Available - size;
            if (IsVertical)
            {
                _split.fixedPane.style.height = size;
                if (_dragLineAnchor != null)
                {
                    _dragLineAnchor.style.top = dragLinePosition;
                }
            }
            else
            {
                _split.fixedPane.style.width = size;
                if (_dragLineAnchor != null)
                {
                    _dragLineAnchor.style.left = dragLinePosition;
                }
            }
        }
    }
}
