using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// UXML 줄 템플릿으로 그리는 가상화 목록의 공통 동작: 줄 생성, 선택, 더블클릭, 우클릭 메뉴.
    /// 하위 클래스는 "줄 하나를 어떻게 채울지"(<see cref="Bind"/>)만 정한다 — 목록 종류를 늘려도 이 클래스는 고치지 않는다(OCP).
    /// </summary>
    public abstract class TemplateList<T> where T : class
    {
        private const float RowHeight = 20f;

        private readonly VisualTreeAsset _template;
        private List<T> _items = new();

        protected ListView ListView { get; }

        public IReadOnlyList<T> Items => _items;
        public T SelectedItem => ListView.selectedIndex >= 0 && ListView.selectedIndex < _items.Count ? _items[ListView.selectedIndex] : null;

        /// <summary>
        /// 고른 항목 전부(목록 순서). 여러 개 고르기(<see cref="SelectionType"/>)를 켜면 Shift(범위)·Ctrl(하나씩)로 늘어난다.
        /// </summary>
        public IReadOnlyList<T> SelectedItems => ListView.selectedIndices.Where(i => i >= 0 && i < _items.Count).OrderBy(i => i).Select(i => _items[i]).ToList();

        public SelectionType SelectionType
        {
            get => ListView.selectionType;
            set => ListView.selectionType = value;
        }

        /// <summary>선택이 바뀔 때. 선택이 풀리면 null.</summary>
        public event Action<T> SelectionChanged;

        /// <summary>더블클릭·Enter.</summary>
        public event Action<T> ItemChosen;

        /// <summary>우클릭 메뉴를 채울 때. 하위 클래스의 기본 항목 뒤에 붙는다.</summary>
        public event Action<DropdownMenu, T> ContextMenuRequested;

        protected TemplateList(ListView listView, string templateName, SelectionType selectionType = SelectionType.Single)
        {
            ListView = listView;
            _template = GitUiAssets.Load(templateName);

            ListView.fixedItemHeight = RowHeight;
            ListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
            ListView.selectionType = selectionType;
            ListView.showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly;
            ListView.makeItem = MakeRow;
            ListView.bindItem = BindRow;
            ListView.itemsSource = _items;
            ListView.selectionChanged += _ => SelectionChanged?.Invoke(SelectedItem);
            ListView.itemsChosen += chosen =>
            {
                if (chosen.FirstOrDefault() is T item)
                {
                    ItemChosen?.Invoke(item);
                }
            };
        }

        /// <summary>
        /// 항목을 바꾼다. <paramref name="keyOf"/> 를 주면 같은 키의 항목이 다시 선택된다(새로고침해도 선택이 풀리지 않게).
        /// </summary>
        public void SetItems(IEnumerable<T> items, Func<T, string> keyOf = null)
        {
            // 여러 개 골랐으면 모두 키로 기억해 두었다가 되살린다.
            HashSet<string> selectedKeys = keyOf != null ? new HashSet<string>(SelectedItems.Select(keyOf)) : null;
            _items = items?.ToList() ?? new List<T>();
            ListView.itemsSource = _items;
            ListView.RefreshItems();

            List<int> indices = new();
            if (selectedKeys != null && selectedKeys.Count > 0)
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (selectedKeys.Contains(keyOf(_items[i])))
                    {
                        indices.Add(i);
                    }
                }
            }
            if (indices.Count > 0)
            {
                ListView.SetSelectionWithoutNotify(indices);
            }
            else
            {
                ListView.ClearSelection();
            }
        }

        /// <summary>항목 내용만 바뀌었을 때(체크 상태 등) 보이는 줄을 다시 채운다.</summary>
        public void Refresh()
        {
            ListView.RefreshItems();
        }

        /// <summary>불러오는 중에는 ListView 기본 "List is empty" 를 숨긴다.</summary>
        public void SetLoading(bool isLoading)
        {
            ListView.EnableInClassList("list--loading", isLoading);
        }

        public void ClearSelection()
        {
            ListView.ClearSelection();
        }

        public void Select(T item)
        {
            int index = _items.IndexOf(item);
            if (index >= 0)
            {
                ListView.SetSelection(index);
                ListView.ScrollToItem(index);
            }
        }

        protected abstract void Bind(VisualElement row, T item);

        /// <summary>줄이 처음 만들어질 때 한 번. 줄 안 컨트롤의 콜백을 여기서 건다.</summary>
        protected virtual void OnRowCreated(VisualElement row)
        {
        }

        protected virtual void PopulateContextMenu(DropdownMenu menu, T item)
        {
            ContextMenuRequested?.Invoke(menu, item);
        }

        /// <summary>줄에 저장해 둔 인덱스로 지금 그 줄이 보여 주는 항목을 찾는다.</summary>
        protected T ItemOf(VisualElement row)
        {
            return row.userData is int index && index >= 0 && index < _items.Count ? _items[index] : null;
        }

        private VisualElement MakeRow()
        {
            VisualElement row = GitUiAssets.CloneBlock(_template);
            OnRowCreated(row);
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                T item = ItemOf(row);
                if (item == null)
                {
                    return;
                }
                // 이미 고른 여러 줄 중 하나를 우클릭하면 선택을 그대로 둔다(메뉴가 고른 것 전부에 쓰이게).
                int index = (int)row.userData;
                if (ListView.selectedIndices.Contains(index) == false)
                {
                    ListView.SetSelection(index);
                }
                PopulateContextMenu(evt.menu, item);
            }));
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            row.userData = index;
            Bind(row, _items[index]);
        }

        protected static DropdownMenuAction.Status StatusOf(bool enabled)
        {
            return enabled ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
        }
    }
}
