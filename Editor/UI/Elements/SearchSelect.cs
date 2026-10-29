using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary><see cref="SearchSelect{T}"/> 의 클래스 이름(제네릭이라 다른 곳에서 상수를 바로 볼 수 없어 따로 둔다).</summary>
    public static class SearchSelectClasses
    {
        public const string Open = "search-select--open";
    }

    /// <summary>
    /// 서랍의 "Select…" 칸(GitKraken 의 검색 되는 고르기). 테두리 상자 안에 글자 칸 · 구분선 · ⌄ 를 두고(react-select 와 같은 모양),
    /// 누르거나 쓰거나 ↓ 를 누르면 칸 바로 아래에 걸러진 목록을 펼친다. 목록은 아래 칸들을 밀어 내지 않고 그 위를 덮도록 창의 테마 루트(git-themed)에
    /// 따로 띄워 칸 위치에 맞춘다(테마 루트 안이라 색 토큰은 그대로 받는다)(포커스만 받았을 때는 펼치지 않는다 — 서랍이 열리며 첫 칸에 포커스를 줄 때
    /// 목록이 저절로 펼쳐지지 않게). ⌄ 는 누를 때마다 펼치고 접는다. 펼치면 ⌄ 가 위를 본다.
    /// 누르기 · ↑↓ · Enter 로 고르고, 바깥으로 포커스가 나가면 접는다.
    /// </summary>
    public sealed class SearchSelect<T> where T : class
    {
        private const string Block = "search-select";
        private const string HighlightModifier = Block + "__item--highlight";
        private const string OpenModifier = SearchSelectClasses.Open;
        private const string FocusedModifier = Block + "__box--focused";
        private const float RowHeight = 24f;
        private const int MaxVisibleRows = 8;
        private const float ListChrome = 10f;

        private readonly Func<T, string> _label;
        private readonly Func<T, string> _badge;
        private readonly TextField _search;
        private readonly VisualElement _box;
        private readonly ListView _list;
        private readonly List<T> _shown = new();
        private List<T> _items = new();
        private int _highlight;

        public VisualElement Root { get; }

        public T Selected { get; private set; }

        /// <summary>
        /// 지금 검색어에 맞는 항목이 하나라도 있는지(서랍이 "맞는 것이 없습니다" 를 보일 때 쓴다).
        /// </summary>
        public bool HasMatches => _shown.Count > 0;

        public int ItemCount => _items.Count;

        /// <summary>
        /// 사용자가 고르거나 고른 것을 풀었을 때(코드가 <see cref="Select"/> 로 정할 때는 오지 않는다).
        /// </summary>
        public event Action<T> Changed;

        /// <summary>
        /// 검색어가 바뀌어 목록을 다시 걸렀을 때.
        /// </summary>
        public event Action Filtered;

        /// <param name="badge">오른쪽 작은 글자(비공개 · 원격 · 태그…). null 이면 없음.</param>
        public SearchSelect(string placeholder, Func<T, string> label, Func<T, string> badge = null)
        {
            _label = label;
            _badge = badge;

            Root = new VisualElement();
            Root.AddToClassList(Block);

            // ⌄ 나 상자 테두리처럼 포커스를 받지 않는 곳을 누르면 Unity 는 포커스 받을 조상을 찾다가 없으면 포커스를 빼 버려 목록이 곧바로 접힌다.
            // 상자가 포커스를 받아 안쪽 글자 칸에 넘기게 한다(Tab 순서에는 끼지 않는다).
            VisualElement box = new() { focusable = true, delegatesFocus = true, tabIndex = -1 };
            box.AddToClassList(Block + "__box");
            _box = box;
            _search = new TextField();
            _search.AddToClassList(Block + "__search");
            _search.textEdition.placeholder = placeholder;
            _search.textEdition.hidePlaceholderOnFocus = false;
            _search.RegisterValueChangedCallback(evt =>
            {
                // 고른 이름을 다시 고치기 시작하면 고른 것을 푼다.
                if (Selected != null && evt.newValue != _label(Selected))
                {
                    Selected = null;
                    Changed?.Invoke(null);
                }
                Refilter(open: true);
            });
            _search.RegisterCallback<FocusInEvent>(_ => box.AddToClassList(FocusedModifier));
            _search.RegisterCallback<FocusOutEvent>(_ =>
            {
                box.RemoveFromClassList(FocusedModifier);
                // 목록 줄을 누르는 동안 포커스가 잠깐 빠져도 고를 수 있게 조금 기다렸다 접는다. 그 사이 칸이 포커스를 되찾았으면 접지 않는다.
                _list.schedule.Execute(() =>
                {
                    if (HasFocus() == false)
                    {
                        SetOpen(false);
                    }
                }).ExecuteLater(150);
            });
            _search.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            box.Add(_search);
            VisualElement separator = new() { pickingMode = PickingMode.Ignore };
            separator.AddToClassList(Block + "__separator");
            box.Add(separator);
            VisualElement indicator = new();
            indicator.AddToClassList(Block + "__indicator");
            VisualElement chevron = new() { pickingMode = PickingMode.Ignore };
            chevron.AddToClassList(Block + "__chevron");
            indicator.Add(chevron);
            box.Add(indicator);
            // 글자 칸이 누르기를 먼저 받아 멈출 수 있으므로 내려가는 길(TrickleDown)에서 받는다.
            box.RegisterCallback<PointerDownEvent>(evt =>
            {
                bool onIndicator = evt.target is VisualElement target && indicator.Contains(target);
                bool open = onIndicator ? Root.ClassListContains(OpenModifier) == false : true;
                _search.Focus();
                if (open)
                {
                    Refilter(open: true);
                }
                else
                {
                    SetOpen(false);
                }
            }, TrickleDown.TrickleDown);
            Root.Add(box);

            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.None,
                makeItem = MakeRow,
                bindItem = BindRow,
                itemsSource = _shown,
                focusable = false,
            };
            _list.AddToClassList(Block + "__list");
            GitShadow.Add(_list, GitShadowSize.Small);
            _list.SetVisible(false);
            // 펼친 채로 칸이 움직이면(창 크기·서랍 폭) 목록도 따라간다. 칸이 숨으면(서랍 탭을 바꿔 패널이 사라지는 등) 접히기를 기다리지 않고
            // 바로 접는다 — 기다리면 그 사이 목록이 0 크기가 된 칸 자리로 튄다. 칸이 화면에서 빠지면 목록도 뗀다.
            box.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                if (IsBoxShown() == false)
                {
                    SetOpen(false);
                }
                else if (Root.ClassListContains(OpenModifier))
                {
                    PlaceList();
                }
            });
            Root.RegisterCallback<DetachFromPanelEvent>(_ => _list.RemoveFromHierarchy());
        }

        public void SetItems(IEnumerable<T> items)
        {
            _items = items.ToList();
            if (Selected != null && _items.Contains(Selected) == false)
            {
                Select(null);
            }
            // 목록이 늦게 와도 저절로 펼치지 않는다. 이미 펼쳐 둔 상태만 이어 간다.
            Refilter(open: Root.ClassListContains(OpenModifier));
        }

        /// <summary>
        /// 코드가 고른다(<see cref="Changed"/> 없음). null 이면 비운다.
        /// </summary>
        public void Select(T item)
        {
            Selected = item;
            _search.SetValueWithoutNotify(item == null ? string.Empty : _label(item));
            SetOpen(false);
        }

        public void Focus()
        {
            _search.Focus();
        }

        public void SetEnabled(bool enabled)
        {
            Root.SetEnabled(enabled);
        }

        private void Refilter(bool open)
        {
            _shown.Clear();
            // 고른 이름이 그대로 칸에 있으면 걸러내지 않고 모두 보인다(다시 고르기 쉽게).
            string query = Selected != null && _search.value == _label(Selected) ? null : _search.value?.Trim();
            _shown.AddRange(string.IsNullOrEmpty(query)
                ? _items
                : _items.Where(item => _label(item).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0));
            _highlight = Selected != null ? Math.Max(0, _shown.IndexOf(Selected)) : 0;
            // 줄 높이 합 + 목록 위아래 안쪽 여백(4px 씩)과 테두리(1px 씩). 빼먹으면 마지막 줄이 잘려 스크롤 막대가 생긴다.
            _list.style.height = Math.Min(_shown.Count, MaxVisibleRows) * RowHeight + ListChrome;
            _list.RefreshItems();
            SetOpen(open && _shown.Count > 0);
            if (_list.resolvedStyle.display != DisplayStyle.None && _shown.Count > 0)
            {
                _list.ScrollToItem(_highlight);
            }
            Filtered?.Invoke();
        }

        private bool IsBoxShown()
        {
            Rect bound = _box.worldBound;
            return _box.panel != null && float.IsNaN(bound.width) == false && bound.width > 0f && bound.height > 0f;
        }

        private bool HasFocus()
        {
            return _search.focusController?.focusedElement is VisualElement focused && (focused == _search || _search.Contains(focused));
        }

        private void SetOpen(bool open)
        {
            Root.EnableInClassList(OpenModifier, open);
            if (open && IsBoxShown() == false)
            {
                open = false;
                Root.RemoveFromClassList(OpenModifier);
            }
            if (open)
            {
                PlaceList();
                _list.BringToFront();
            }
            _list.SetVisible(open);
        }

        /// <summary>
        /// 목록을 테마 루트에 붙이고 칸 바로 아래(4px), 칸과 같은 폭에 둔다.
        /// </summary>
        private void PlaceList()
        {
            VisualElement host = OverlayHost();
            if (host == null)
            {
                return;
            }
            if (_list.parent != host)
            {
                host.Add(_list);
            }
            Rect box = host.WorldToLocal(_box.worldBound);
            _list.style.left = box.x;
            _list.style.top = box.yMax + 4f;
            _list.style.width = box.width;
        }

        private VisualElement OverlayHost()
        {
            for (VisualElement element = Root.parent; element != null; element = element.parent)
            {
                if (element.ClassListContains("git-themed"))
                {
                    return element;
                }
            }
            return Root.panel?.visualTree;
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList(Block + "__item");
            Label name = new();
            name.AddToClassList(Block + "__item-label");
            name.AddToClassList("ellipsis");
            row.Add(name);
            Label badge = new();
            badge.AddToClassList(Block + "__item-badge");
            row.Add(badge);
            // 검색창에서 포커스가 빠져 목록이 접히기 전에 고르도록 누르는 순간 처리한다.
            row.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (row.userData is int index)
                {
                    Choose(index);
                    evt.StopPropagation();
                }
            });
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            row.userData = index;
            T item = _shown[index];
            ((Label)row[0]).text = GitDisplay.Nfc(_label(item));
            string badge = _badge?.Invoke(item);
            ((Label)row[1]).text = badge ?? string.Empty;
            row[1].SetVisible(string.IsNullOrEmpty(badge) == false);
            row.EnableInClassList(HighlightModifier, index == _highlight);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            // 목록이 열려 있으면 Esc 는 목록만 닫는다(서랍까지 닫지 않는다 — SideDrawer 가 이 경우를 건너뛴다).
            if (evt.keyCode == UnityEngine.KeyCode.Escape && Root.ClassListContains(OpenModifier))
            {
                SetOpen(false);
                evt.StopPropagation();
                return;
            }
            if (Root.ClassListContains(OpenModifier) == false)
            {
                if (evt.keyCode == UnityEngine.KeyCode.DownArrow)
                {
                    Refilter(open: true);
                    evt.StopPropagation();
                }
                return;
            }
            if (_shown.Count == 0)
            {
                return;
            }
            switch (evt.keyCode)
            {
                case UnityEngine.KeyCode.DownArrow:
                    MoveHighlight(1);
                    evt.StopPropagation();
                    break;
                case UnityEngine.KeyCode.UpArrow:
                    MoveHighlight(-1);
                    evt.StopPropagation();
                    break;
                case UnityEngine.KeyCode.Return:
                case UnityEngine.KeyCode.KeypadEnter:
                    Choose(_highlight);
                    evt.StopPropagation();
                    break;
            }
        }

        private void MoveHighlight(int delta)
        {
            _highlight = Math.Clamp(_highlight + delta, 0, _shown.Count - 1);
            _list.RefreshItems();
            _list.ScrollToItem(_highlight);
        }

        private void Choose(int index)
        {
            if (index < 0 || index >= _shown.Count)
            {
                return;
            }
            Select(_shown[index]);
            Changed?.Invoke(Selected);
        }
    }
}
