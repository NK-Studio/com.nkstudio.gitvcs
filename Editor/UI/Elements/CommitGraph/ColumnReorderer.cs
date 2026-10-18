using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 목록 머리 줄의 칸을 좌우로 끌어 순서를 바꾼다(GitKraken 의 BRANCH / TAG · GRAPH · COMMIT MESSAGE 머리처럼).
    /// 끄는 동안 칸 이름 유령을 포인터에 붙이고, 놓일 자리에 세로선을 그린다. 놓으면 <c>move(원래 자리, 새 자리)</c> 를 부른다.
    /// 끌기는 <see cref="ColumnResizer"/> 와 같은 방식이다 — 패널 루트에서 이동·뗌을 받고 로컬 좌표(배율이 반영된 좌표)로 바꾼다.
    /// </summary>
    public sealed class ColumnReorderer
    {
        /// <summary>
        /// 이만큼 움직여야 끌기로 본다(그냥 누른 것과 구분).
        /// </summary>
        private const float DragThreshold = 4f;

        private const float MinGhostWidth = 60f;
        private const float MaxGhostWidth = 260f;

        private readonly VisualElement _header;
        private readonly VisualElement _layer;
        private readonly Action<int, int> _move;
        private readonly HashSet<VisualElement> _cells = new();
        private readonly Label _ghost;
        private readonly VisualElement _indicator;

        private VisualElement _pressedCell;
        private Vector2 _pressPosition;
        private Vector2 _grabOffset;
        private bool _isDragging;
        private int _targetIndex;
        private VisualElement _panelRoot;

        /// <param name="header">칸이 들어 있는 머리 줄. 칸 사이의 다른 요소(경계 손잡이)는 순서에 세지 않는다.</param>
        /// <param name="layer">유령·세로선을 띄울 요소(머리 줄과 목록을 함께 덮는 부모).</param>
        /// <param name="move">놓았을 때. 원래 자리와, 끈 칸을 뺀 나머지 사이에 끼울 자리(곧 새 자리).</param>
        public ColumnReorderer(VisualElement header, VisualElement layer, Action<int, int> move)
        {
            _header = header;
            _layer = layer;
            _move = move;

            _ghost = new Label { pickingMode = PickingMode.Ignore };
            _ghost.AddToClassList("list-header__cell");
            _ghost.AddToClassList("column-reorder__ghost");
            _indicator = new VisualElement { pickingMode = PickingMode.Ignore };
            _indicator.AddToClassList("column-reorder__indicator");
        }

        /// <summary>
        /// 끌 수 있는 칸으로 등록한다. 칸은 머리 줄의 직접 자식이어야 한다.
        /// </summary>
        public void Attach(VisualElement cell)
        {
            if (_cells.Add(cell))
            {
                cell.AddToClassList("column-reorder__cell");
                cell.RegisterCallback<PointerDownEvent>(OnPointerDown);
            }
        }

        /// <summary>
        /// 지금 머리 줄에 놓인 순서대로의 칸.
        /// </summary>
        private List<VisualElement> CellsInOrder()
        {
            return _header.Children().Where(_cells.Contains).ToList();
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0 || evt.clickCount != 1 || evt.currentTarget is not VisualElement cell || cell.panel == null)
            {
                return;
            }
            EndDrag(false);
            _pressedCell = cell;
            _pressPosition = _layer.WorldToLocal(evt.position);
            _grabOffset = cell.WorldToLocal(evt.position);
            _panelRoot = cell.panel.visualTree;
            _panelRoot.RegisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
            _panelRoot.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_pressedCell == null)
            {
                return;
            }
            // 버튼을 창 밖에서 놓아 뗌 이벤트를 놓친 경우: 옮기지 않고 끝낸다.
            if ((evt.pressedButtons & 1) == 0)
            {
                EndDrag(false);
                return;
            }
            Vector2 position = _layer.WorldToLocal(evt.position);
            if (_isDragging == false)
            {
                if (Mathf.Abs(position.x - _pressPosition.x) < DragThreshold)
                {
                    return;
                }
                BeginDrag();
            }
            UpdateDrag(position);
            evt.StopPropagation();
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_isDragging)
            {
                evt.StopPropagation();
            }
            EndDrag(_isDragging);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Escape && _isDragging)
            {
                evt.StopPropagation();
                EndDrag(false);
            }
        }

        private void BeginDrag()
        {
            _isDragging = true;
            _ghost.text = _pressedCell is TextElement text ? text.text : string.Empty;
            _ghost.style.width = Mathf.Clamp(_pressedCell.layout.width, MinGhostWidth, MaxGhostWidth);
            _ghost.style.height = _header.layout.height;
            _indicator.style.top = _header.ChangeCoordinatesTo(_layer, Vector2.zero).y;
            _layer.Add(_indicator);
            _layer.Add(_ghost);
            _pressedCell.AddToClassList("column-reorder__cell--source");
            // 끄는 동안 어디 위에 있든 손 모양 커서를 유지한다.
            _layer.AddToClassList("column-reorder--dragging");
        }

        private void UpdateDrag(Vector2 position)
        {
            _ghost.style.left = position.x - _grabOffset.x;
            _ghost.style.top = position.y - _grabOffset.y;

            // 끈 칸을 뺀 나머지 칸 가운데 포인터보다 왼쪽에 가운데가 있는 수 = 끼울 자리.
            List<VisualElement> others = CellsInOrder();
            others.Remove(_pressedCell);
            float pointerX = _layer.ChangeCoordinatesTo(_header, position).x;
            _targetIndex = others.Count(cell => cell.layout.center.x < pointerX);

            float lineX = others.Count == 0 ? _pressedCell.layout.xMin
                : _targetIndex == 0 ? others[0].layout.xMin
                : others[_targetIndex - 1].layout.xMax;
            _indicator.style.left = _header.ChangeCoordinatesTo(_layer, new Vector2(lineX, 0f)).x - 1f;
        }

        /// <param name="apply">true 면 놓은 자리로 옮긴다.</param>
        private void EndDrag(bool apply)
        {
            if (_panelRoot != null)
            {
                _panelRoot.UnregisterCallback<PointerMoveEvent>(OnPointerMove, TrickleDown.TrickleDown);
                _panelRoot.UnregisterCallback<PointerUpEvent>(OnPointerUp, TrickleDown.TrickleDown);
                _panelRoot.UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
                _panelRoot = null;
            }
            VisualElement cell = _pressedCell;
            bool wasDragging = _isDragging;
            _pressedCell = null;
            _isDragging = false;
            if (wasDragging == false || cell == null)
            {
                return;
            }
            _ghost.RemoveFromHierarchy();
            _indicator.RemoveFromHierarchy();
            cell.RemoveFromClassList("column-reorder__cell--source");
            _layer.RemoveFromClassList("column-reorder--dragging");
            int from = CellsInOrder().IndexOf(cell);
            if (apply && from >= 0 && from != _targetIndex)
            {
                _move(from, _targetIndex);
            }
        }
    }
}
