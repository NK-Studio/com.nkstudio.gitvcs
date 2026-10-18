using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 목록을 목록(<see cref="FileList"/>)이나 트리(<see cref="FileTree"/>)로 보여 준다. 쓰는 쪽은 보기 방식을 몰라도 된다 —
    /// 항목·체크·메뉴는 이 클래스 하나로 다루고, 전환 버튼(<see cref="FileViewModeSwitch"/>)이 보기 방식을 바꾼다.
    /// </summary>
    public sealed class FileBrowser : IDisposable
    {
        private const string Block = "file-browser";

        private readonly FileViewPreference _preference;
        private readonly IAssetNavigator _assets;
        private readonly IGitDiffService _diff;
        private readonly FileList _list;
        private readonly FileTree _tree;
        private readonly VisualElement _listRoot;
        private List<FileListItem> _items = new();
        private Func<FileListItem, bool> _isChecked;
        private FileViewMode _shownMode;

        /// <summary>파일 하나 또는 폴더 아래 전부를 체크·해제했을 때.</summary>
        public event Action<IReadOnlyList<FileListItem>, bool> ToggleChanged;

        /// <summary>파일 줄의 우클릭 메뉴(기본 항목 뒤에 붙는다).</summary>
        public event Action<DropdownMenu, FileListItem> ContextMenuRequested;

        /// <summary>트리의 폴더 줄 우클릭 메뉴. 인자는 폴더 아래 모든 파일.</summary>
        public event Action<DropdownMenu, IReadOnlyList<FileListItem>> FolderContextMenuRequested;

        /// <summary>체크박스를 쓸 때 체크 상태를 묻는다. null 이면 체크박스를 숨긴다.</summary>
        public Func<FileListItem, bool> IsChecked
        {
            get => _isChecked;
            set
            {
                _isChecked = value;
                _list.IsChecked = value;
                _tree.IsChecked = value;
            }
        }

        public IReadOnlyList<FileListItem> Items => _items;

        /// <summary>
        /// 파일 줄의 diff 대상을 만든다. 정하면 더블클릭이 diff 를 열고 우클릭 메뉴 맨 위에 "변경 내용 보기"가 생긴다.
        /// null 이면 더블클릭은 Project 창에서 선택한다.
        /// </summary>
        public Func<FileListItem, GitDiffTarget> DiffTargetOf { get; set; }

        /// <summary>
        /// 고른 파일이 바뀌었을 때(폴더 줄·선택 해제면 null). Fork 레이아웃은 이것으로 오른쪽 diff 를 바꾼다.
        /// </summary>
        public event Action<FileListItem> SelectionChanged;

        /// <summary>
        /// 더블클릭·Enter 동작을 바꾼다(Stage·Unstage 목록은 반대편으로 옮긴다). null 이면 diff 를 열거나 Project 창에서 고른다.
        /// </summary>
        public Action<FileListItem> ChooseAction { get; set; }

        /// <summary>
        /// 우클릭 메뉴 맨 위에 더할 항목(Stage·Unstage 등). "변경 내용 보기" 보다 위에 온다.
        /// </summary>
        public Action<DropdownMenu, FileListItem> MenuHead { get; set; }

        /// <summary>
        /// 파일 줄 메뉴 전체를 직접 채운다(GitKraken 형태 메뉴). 정하면 기본 항목·MenuHead·ContextMenuRequested 는 쓰지 않는다.
        /// </summary>
        public Action<DropdownMenu, FileListItem> FileMenu
        {
            set
            {
                _list.ReplaceMenu = value;
                _tree.ReplaceMenu = value;
            }
        }

        /// <summary>
        /// 트리의 폴더 줄 메뉴(폴더 화면 경로, 아래 파일들).
        /// </summary>
        public event Action<DropdownMenu, string, IReadOnlyList<FileListItem>> FolderPathContextMenuRequested;

        /// <param name="host">UXML 의 자리(<c>.file-browser</c>). 목록·트리가 이 안에 들어간다.</param>
        /// <param name="switchHost">목록/트리 전환 버튼을 둘 자리(목록 머리 줄 오른쪽 등). null 이면 버튼 없이 설정만 따른다.</param>
        public FileBrowser(VisualElement host, VisualElement switchHost, GitServices services)
        {
            _preference = services.FileView;
            _assets = services.Assets;
            _diff = services.Diff;
            host.AddToClassList(Block);

            FileRowBinder binder = new(services.Icons);
            ListView listView = new();
            listView.AddToClassList("list");
            host.Add(listView);
            _listRoot = listView;
            _list = new FileList(listView, services.Assets, binder);
            _list.ToggleChanged += (item, value) => ToggleChanged?.Invoke(new[] { item }, value);
            _list.ContextMenuRequested += (menu, item) => ContextMenuRequested?.Invoke(menu, item);
            _list.ItemChosen += OnFileChosen;
            _list.SelectionChanged += item => SelectionChanged?.Invoke(item);
            _list.PopulateMenuHead = PopulateMenuHead;

            _tree = new FileTree(services.Assets, binder);
            host.Add(_tree.Root);
            _tree.ToggleChanged += (files, value) => ToggleChanged?.Invoke(files, value);
            _tree.ContextMenuRequested += (menu, item) => ContextMenuRequested?.Invoke(menu, item);
            _tree.FolderContextMenuRequested += (menu, files) => FolderContextMenuRequested?.Invoke(menu, files);
            _tree.FileChosen += OnFileChosen;
            _tree.FolderPathContextMenuRequested += (menu, path, files) => FolderPathContextMenuRequested?.Invoke(menu, path, files);
            _tree.SelectionChanged += item => SelectionChanged?.Invoke(item);
            _tree.PopulateMenuHead = PopulateMenuHead;

            if (switchHost != null)
            {
                switchHost.Add(new FileViewModeSwitch(_preference).Root);
            }

            _shownMode = _preference.Mode;
            _preference.Changed += OnModeChanged;
            ApplyVisibility();
        }

        public void SetItems(IEnumerable<FileListItem> items)
        {
            _items = items.ToList();
            Populate();
        }

        /// <summary>체크 상태처럼 항목은 그대로이고 보이는 값만 바뀌었을 때.</summary>
        public void Refresh()
        {
            if (_shownMode == FileViewMode.Tree)
            {
                _tree.Refresh();
            }
            else
            {
                _list.Refresh();
            }
        }

        public void Dispose()
        {
            _preference.Changed -= OnModeChanged;
        }

        public void ClearSelection()
        {
            _list.ClearSelection();
            _tree.ClearSelection();
        }

        private void OnFileChosen(FileListItem item)
        {
            if (ChooseAction != null)
            {
                ChooseAction(item);
                return;
            }
            if (DiffTargetOf == null)
            {
                _assets.Ping(item.ProjectPath);
                return;
            }
            _diff.Open(DiffTargetOf(item));
        }

        private void PopulateMenuHead(DropdownMenu menu, FileListItem item)
        {
            MenuHead?.Invoke(menu, item);
            if (DiffTargetOf == null)
            {
                return;
            }
            menu.AppendAction(GitText.Get("diff.show"), _ => _diff.Open(DiffTargetOf(item)));
            menu.AppendSeparator();
        }

        private void OnModeChanged()
        {
            _shownMode = _preference.Mode;
            ApplyVisibility();
            Populate();
        }

        /// <summary>보이는 쪽만 채운다. 숨은 쪽은 전환할 때 채운다(큰 목록을 두 번 만들지 않게).</summary>
        private void Populate()
        {
            if (_shownMode == FileViewMode.Tree)
            {
                _tree.SetItems(_items);
            }
            else
            {
                _list.SetItems(_items, item => item.RepoPath);
            }
        }

        private void ApplyVisibility()
        {
            _listRoot.SetVisible(_shownMode == FileViewMode.List);
            _tree.Root.SetVisible(_shownMode == FileViewMode.Tree);
        }
    }
}
