using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 목록의 트리 보기(UI Toolkit TreeView). 폴더 줄은 아래 파일 수와 세 가지 상태 체크박스(전체·일부·없음)를 가진다.
    /// 목록이 새로 들어와도(파일 저장마다) 접어 둔 폴더는 접힌 채로 둔다 — 폴더 경로로 펼침 상태를 기억한다.
    /// </summary>
    public sealed class FileTree
    {
        private const float RowHeight = 20f;

        private readonly TreeView _tree;
        private readonly IAssetNavigator _assets;
        private readonly FileRowBinder _binder;
        private readonly VisualTreeAsset _template;
        private readonly Dictionary<string, int> _idByFolderPath = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 파일 줄 id 도 경로마다 고정한다. 목록이 새로 들어와도(저장·창 포커스마다 새로고침) 같은 파일은 같은 id 라 선택을 되살릴 수 있다.
        /// </summary>
        private readonly Dictionary<string, int> _idByFilePath = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _collapsedFolders = new(StringComparer.OrdinalIgnoreCase);
        private int _nextId;

        /// <summary>체크박스를 쓸 때 파일의 체크 상태를 묻는다. null 이면 체크박스를 숨긴다.</summary>
        public Func<FileListItem, bool> IsChecked;

        /// <summary>파일 하나(1개) 또는 폴더 아래 전부를 체크·해제했을 때.</summary>
        public event Action<IReadOnlyList<FileListItem>, bool> ToggleChanged;

        public event Action<DropdownMenu, FileListItem> ContextMenuRequested;
        public event Action<DropdownMenu, IReadOnlyList<FileListItem>> FolderContextMenuRequested;

        /// <summary>
        /// 파일 줄을 더블클릭·Enter 했을 때.
        /// </summary>
        public event Action<FileListItem> FileChosen;

        /// <summary>
        /// 고른 줄이 바뀌었을 때. 폴더 줄이나 선택 해제면 null.
        /// </summary>
        public event Action<FileListItem> SelectionChanged;

        /// <summary>
        /// 파일 줄 우클릭 메뉴의 맨 위 항목(diff 등)을 채운다. null 이면 없다.
        /// </summary>
        public Action<DropdownMenu, FileListItem> PopulateMenuHead;

        /// <summary>
        /// 정하면 파일 줄 메뉴 전체를 이것이 채운다(기본 항목을 넣지 않는다).
        /// </summary>
        public Action<DropdownMenu, FileListItem> ReplaceMenu;

        /// <summary>
        /// 폴더 줄 메뉴. 인자는 폴더 화면 경로와 그 아래 모든 파일.
        /// </summary>
        public event Action<DropdownMenu, string, IReadOnlyList<FileListItem>> FolderPathContextMenuRequested;

        public VisualElement Root => _tree;

        public FileTree(IAssetNavigator assets, FileRowBinder binder)
        {
            _assets = assets;
            _binder = binder;
            _template = GitUiAssets.Load("FileRow");

            _tree = new TreeView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                showAlternatingRowBackgrounds = AlternatingRowBackground.ContentOnly,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _tree.AddToClassList("list");
            _tree.selectionChanged += selected => SelectionChanged?.Invoke((selected.FirstOrDefault() as FileTreeNode)?.Item);
            _tree.itemsChosen += chosen =>
            {
                if (chosen.FirstOrDefault() is FileTreeNode node && node.Item != null)
                {
                    FileChosen?.Invoke(node.Item);
                }
            };
        }

        public void SetItems(IEnumerable<FileListItem> items)
        {
            RememberExpansion();
            List<int> selectedIds = _tree.selectedIds.ToList();
            List<FileTreeNode> roots = FileTreeBuilder.Build(items);
            _tree.SetRootItems(roots.Select(ToItemData).ToList());
            _tree.Rebuild();

            _tree.ExpandAll();
            foreach (string folder in _collapsedFolders)
            {
                if (_idByFolderPath.TryGetValue(folder, out int id))
                {
                    _tree.CollapseItem(id);
                }
            }

            // 새로고침 전에 고른 줄을 다시 고른다(사라진 파일은 빠진다). 알림은 보내지 않는다 — 사용자가 바꾼 선택이 아니다.
            List<int> stillPresent = selectedIds.Where(id => _tree.viewController.GetIndexForId(id) >= 0).ToList();
            _tree.SetSelectionByIdWithoutNotify(stillPresent);
        }

        public void Refresh()
        {
            _tree.RefreshItems();
        }

        public void ClearSelection()
        {
            _tree.ClearSelection();
        }

        /// <summary>지금 화면의 펼침 상태를 폴더 경로로 기록한다. 다음 SetItems 뒤에 되살린다.</summary>
        private void RememberExpansion()
        {
            foreach (KeyValuePair<string, int> pair in _idByFolderPath)
            {
                try
                {
                    if (_tree.IsExpanded(pair.Value))
                    {
                        _collapsedFolders.Remove(pair.Key);
                    }
                    else
                    {
                        _collapsedFolders.Add(pair.Key);
                    }
                }
                catch (ArgumentException)
                {
                    // 지난 목록에서 사라진 폴더.
                }
            }
        }

        private TreeViewItemData<FileTreeNode> ToItemData(FileTreeNode node)
        {
            int id;
            if (node.IsFolder)
            {
                if (_idByFolderPath.TryGetValue(node.Path, out id) == false)
                {
                    id = ++_nextId;
                    _idByFolderPath[node.Path] = id;
                }
            }
            else if (_idByFilePath.TryGetValue(node.Item.RepoPath, out id) == false)
            {
                id = ++_nextId;
                _idByFilePath[node.Item.RepoPath] = id;
            }
            List<TreeViewItemData<FileTreeNode>> children = node.IsFolder ? node.Children.Select(ToItemData).ToList() : null;
            return new TreeViewItemData<FileTreeNode>(id, node, children);
        }

        private VisualElement MakeRow()
        {
            VisualElement row = GitUiAssets.CloneBlock(_template);
            row.Require<Toggle>("toggle").RegisterValueChangedCallback(evt =>
            {
                FileTreeNode node = NodeOf(row);
                if (node == null)
                {
                    return;
                }
                IReadOnlyList<FileListItem> files = node.IsFolder ? node.Files() : new[] { node.Item };
                ToggleChanged?.Invoke(files, evt.newValue);
            });
            row.AddManipulator(new ContextualMenuManipulator(evt =>
            {
                FileTreeNode node = NodeOf(row);
                if (node == null)
                {
                    return;
                }
                _tree.SetSelection((int)row.userData);
                if (node.IsFolder)
                {
                    PopulateFolderMenu(evt.menu, node);
                }
                else if (ReplaceMenu != null)
                {
                    ReplaceMenu(evt.menu, node.Item);
                }
                else
                {
                    PopulateMenuHead?.Invoke(evt.menu, node.Item);
                    FileRowBinder.AppendFileMenu(evt.menu, node.Item, _assets);
                    evt.menu.AppendSeparator();
                    ContextMenuRequested?.Invoke(evt.menu, node.Item);
                }
            }));
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            row.userData = index;
            FileTreeNode node = _tree.GetItemDataForIndex<FileTreeNode>(index);
            if (node.IsFolder)
            {
                int checkedFiles = IsChecked == null ? 0 : node.Files().Count(IsChecked);
                _binder.BindFolder(row, node, IsChecked != null, checkedFiles);
            }
            else
            {
                _binder.BindFile(row, node.Item, IsChecked != null ? IsChecked(node.Item) : null, nameOnly: true);
            }
        }

        private void PopulateFolderMenu(DropdownMenu menu, FileTreeNode folder)
        {
            if (FolderPathContextMenuRequested != null)
            {
                // GitKraken 형태: git 동작이 맨 위, 보기 동작(펼치기·접기)이 아래.
                FolderPathContextMenuRequested(menu, folder.Path, folder.Files());
                menu.AppendSeparator();
            }
            menu.AppendAction(GitText.Get("fileView.expandAll"), _ => _tree.ExpandAll());
            menu.AppendAction(GitText.Get("fileView.collapseAll"), _ => _tree.CollapseAll());
            menu.AppendAction(GitText.Get("common.copyPath"), _ => _assets.CopyToClipboard(folder.Path));
            if (FolderContextMenuRequested != null)
            {
                menu.AppendSeparator();
                FolderContextMenuRequested(menu, folder.Files());
            }
        }

        private FileTreeNode NodeOf(VisualElement row)
        {
            return row.userData is int index ? _tree.GetItemDataForIndex<FileTreeNode>(index) : null;
        }
    }
}
