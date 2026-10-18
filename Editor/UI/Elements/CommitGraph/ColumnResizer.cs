using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 목록 머리 줄의 열 경계 손잡이. 왼쪽 열의 폭을 좌우로 끌어 바꾸고, 두 번 누르면 기본 폭으로 돌린다.
    /// 끌기는 <see cref="SplitPaneClamp"/> 와 같은 방식이다 — 포인터를 붙잡으면 UI 배율에서 바로 놓치므로,
    /// 패널 루트에서 이동·뗌을 받고 머리 줄의 로컬 좌표(배율이 반영된 좌표)로 바꿔 폭을 정한다.
    /// </summary>
    public sealed class ColumnResizer
    {
        private readonly VisualElement _handle;
        private readonly Func<float> _getWidth;
        private readonly Action<float> _setWidth;
        private readonly Action _commit;
        private readonly Action _reset;

        private bool _isDragging;
        private float _dragStartPointer;
        private float _dragStartWidth;
        private VisualElement _panelRoot;

        /// <param name="getWidth">끌기를 시작할 때의 왼쪽 열 폭.</param>
        /// <param name="setWidth">끄는 동안 새 폭(범위 맞추기는 받는 쪽이 한다).</param>
        /// <param name="commit">끌기를 마쳤을 때(폭 저장).</param>
        /// <param name="reset">두 번 눌렀을 때(기본 폭으로).</param>
        public ColumnResizer(Func<float> getWidth, Action<float> setWidth, Action commit, Action reset)
        {
            _getWidth = getWidth;
            _setWidth = setWidth;
            _commit = commit;
            _reset = reset;

            _handle = new VisualElement();
            _handle.AddToClassList("column-resizer");
            VisualElement line = new() { pickingMode = PickingMode.Ignore };
            line.AddToClassList("column-resizer__line");
            _handle.Add(line);
            _handle.RegisterCallback<PointerDownEvent>(BeginDrag);
        }

        public VisualElement Handle => _handle;

        /// <summary>
        /// 패널 좌표의 포인터 위치를 머리 줄 로컬 x 로.
        /// </summary>
        private float LocalX(Vector3 panelPosition)
        {
            VisualElement header = _handle.parent ?? _handle;
            return header.WorldToLocal(panelPosition).x;
        }

        private void BeginDrag(PointerDownEvent evt)
        {
            if (evt.button != 0 || _handle.panel == null)
            {
                return;
            }
            evt.StopImmediatePropagation();
            if (evt.clickCount == 2)
            {
                _reset();
                _commit();
                return;
            }
            _isDragging = true;
            _dragStartPointer = LocalX(evt.position);
            _dragStartWidth = _getWidth();
            _handle.AddToClassList("column-resizer--dragging");
            _panelRoot = _handle.panel.visualTree;
            _panelRoot.RegisterCallback<PointerMoveEvent>(OnDragMove, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<PointerUpEvent>(OnDragEnd, TrickleDown.TrickleDown);
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
            _setWidth(_dragStartWidth + LocalX(evt.position) - _dragStartPointer);
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
            _handle.RemoveFromClassList("column-resizer--dragging");
            _commit();
        }
    }
}
