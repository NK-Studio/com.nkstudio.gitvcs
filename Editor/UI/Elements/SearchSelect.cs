using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 서랍의 "Select…" 칸(GitKraken 의 검색 되는 고르기). 글자 칸 + ▾, 누르거나 쓰면 그 아래로 걸러진 목록을 펼친다.
    /// 누르기 · ↑↓ · Enter 로 고르고, 바깥으로 포커스가 나가면 접는다. 목록은 칸 아래를 밀어 내며 펼친다(겹쳐 띄우지 않는다).
    /// </summary>
    public sealed class SearchSelect<T> where T : class
    {
        private const string Block = "search-select";
        private const string HighlightModifier = Block + "__item--highlight";
        private const float RowHeight = 24f;
        private const int MaxVisibleRows = 8;

        private readonly Func<T, string> _label;
        private readonly Func<T, string> _badge;
        private readonly TextField _search;
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

            VisualElement box = new();
            box.AddToClassList(Block + "__box");
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
            _search.RegisterCallback<FocusInEvent>(_ => Refilter(open: true));
            _search.RegisterCallback<FocusOutEvent>(_ => _list.schedule.Execute(() => _list.SetVisible(false)).ExecuteLater(150));
            _search.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            box.Add(_search);
            ActionGlyph caret = new(ActionGlyphKind.Caret);
            caret.AddToClassList(Block + "__caret");
            box.Add(caret);
            box.RegisterCallback<PointerDownEvent>(_ => _search.Focus());
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
            _list.SetVisible(false);
            Root.Add(_list);
        }

        public void SetItems(IEnumerable<T> items)
        {
            _items = items.ToList();
            if (Selected != null && _items.Contains(Selected) == false)
            {
                Select(null);
            }
            Refilter(open: _search.focusController?.focusedElement == _search);
        }

        /// <summary>
        /// 코드가 고른다(<see cref="Changed"/> 없음). null 이면 비운다.
        /// </summary>
        public void Select(T item)
        {
            Selected = item;
            _search.SetValueWithoutNotify(item == null ? string.Empty : _label(item));
            _list.SetVisible(false);
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
            _list.style.height = Math.Min(_shown.Count, MaxVisibleRows) * RowHeight;
            _list.RefreshItems();
            _list.SetVisible(open && _shown.Count > 0);
            if (_list.resolvedStyle.display != DisplayStyle.None && _shown.Count > 0)
            {
                _list.ScrollToItem(_highlight);
            }
            Filtered?.Invoke();
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
            if (_list.resolvedStyle.display == DisplayStyle.None || _shown.Count == 0)
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
