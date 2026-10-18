using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>파일 목록 한 줄이 보여 줄 값. 작업 트리 변경(<see cref="GitStatusEntry"/>)과 커밋 변경(<see cref="GitFileChange"/>)을 같은 모양으로 그린다.</summary>
    public sealed class FileListItem
    {
        public GitFileState State;
        public string RepoPath;
        public string OriginalRepoPath;
        public string ProjectPath;
        public string Tooltip;

        /// <summary>오른쪽 끝의 강조 글자("⚠ 로컬 변경 있음"). 없으면 null.</summary>
        public string Note;

        /// <summary>원래 데이터. 체크·되돌리기처럼 원본이 필요한 곳에서 꺼내 쓴다.</summary>
        public object Source;

        public string DisplayPath => ProjectPath ?? RepoPath;

        public static FileListItem From(GitStatusEntry entry)
        {
            return new FileListItem
            {
                State = entry.State,
                RepoPath = entry.RepoPath,
                OriginalRepoPath = entry.OriginalRepoPath,
                ProjectPath = entry.ProjectPath,
                Tooltip = $"{entry.State}  [{entry.IndexCode}{entry.WorkTreeCode}]  {entry.RepoPath}",
                Source = entry,
            };
        }

        public static FileListItem From(GitFileChange change)
        {
            return new FileListItem
            {
                State = change.State,
                RepoPath = change.RepoPath,
                OriginalRepoPath = change.OriginalRepoPath,
                ProjectPath = change.ProjectPath,
                Tooltip = change.RepoPath,
                Source = change,
            };
        }
    }

    /// <summary>
    /// 파일 목록의 평면 보기(<c>FileRow.uxml</c>, 전체 경로). 줄 채우기는 <see cref="FileRowBinder"/> 가 맡는다.
    /// 목록/트리 전환은 <see cref="FileBrowser"/> 가 하므로, 보통은 이 클래스를 직접 쓰지 않는다.
    /// </summary>
    public sealed class FileList : TemplateList<FileListItem>
    {
        private readonly IAssetNavigator _assets;
        private readonly FileRowBinder _binder;

        /// <summary>체크박스를 쓸 때 체크 상태를 묻는다. null 이면 체크박스를 숨긴다.</summary>
        public Func<FileListItem, bool> IsChecked;

        public event Action<FileListItem, bool> ToggleChanged;

        /// <summary>
        /// 줄에 마우스를 올리면 나오는 버튼(<see cref="FileRowBinder.ActionText"/>)을 눌렀을 때. 그 줄 항목 하나.
        /// </summary>
        public event Action<FileListItem> ActionClicked;

        /// <summary>
        /// 우클릭 메뉴의 맨 위 항목(diff 등)을 채운다. null 이면 없다.
        /// </summary>
        public Action<DropdownMenu, FileListItem> PopulateMenuHead;

        /// <summary>
        /// 정하면 파일 줄 메뉴 전체를 이것이 채운다(기본 항목을 넣지 않는다).
        /// </summary>
        public Action<DropdownMenu, FileListItem> ReplaceMenu;

        public FileList(ListView listView, IAssetNavigator assets, FileRowBinder binder) : base(listView, "FileRow")
        {
            _assets = assets;
            _binder = binder;
        }

        protected override void OnRowCreated(VisualElement row)
        {
            row.Require<Toggle>("toggle").RegisterValueChangedCallback(evt =>
            {
                FileListItem item = ItemOf(row);
                if (item != null)
                {
                    ToggleChanged?.Invoke(item, evt.newValue);
                }
            });
            FileRowBinder.HookAction(row, () =>
            {
                FileListItem item = ItemOf(row);
                if (item != null)
                {
                    ActionClicked?.Invoke(item);
                }
            });
        }

        protected override void Bind(VisualElement row, FileListItem item)
        {
            _binder.BindFile(row, item, IsChecked != null ? IsChecked(item) : null, nameOnly: false);
        }

        protected override void PopulateContextMenu(DropdownMenu menu, FileListItem item)
        {
            if (ReplaceMenu != null)
            {
                ReplaceMenu(menu, item);
                return;
            }
            PopulateMenuHead?.Invoke(menu, item);
            FileRowBinder.AppendFileMenu(menu, item, _assets);
            menu.AppendSeparator();
            base.PopulateContextMenu(menu, item);
        }
    }
}
