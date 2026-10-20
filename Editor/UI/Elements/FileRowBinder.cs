using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// <c>FileRow.uxml</c> 한 줄을 채우는 규칙. 목록(<see cref="FileList"/>)과 트리(<see cref="FileTree"/>)가 같은 줄 모양을 쓰도록 한곳에 둔다.
    /// </summary>
    public sealed class FileRowBinder
    {
        private const string ToggleHiddenModifier = "file-row__toggle--hidden";
        private const string ToggleColumnHiddenModifier = "file-row__toggle-column--hidden";
        private const string BadgeHiddenModifier = "file-row__badge--hidden";
        private const string FolderModifier = "file-row--folder";
        private const string CountModifier = "file-row__note--count";

        private readonly IFileIconProvider _icons;
        private Texture _folderIcon;

        /// <summary>
        /// 파일 줄에 마우스를 올리면 보일 버튼의 글자와 모양 클래스(<c>file-row__action--stage</c> 등). null 이면 버튼이 없다.
        /// 보이고 숨기는 것은 USS(<c>.file-row:hover</c>)가 한다 — 모양 클래스가 붙은 버튼만 나온다.
        /// </summary>
        public string ActionText { get; set; }

        public string ActionModifier
        {
            get => _actionModifier;
            set
            {
                // 바꾸거나 끄면(null) 이미 그린 줄에 남은 옛 모양 클래스를 다음 묶을 때 뗀다.
                if (_actionModifier != null && _actionModifier != value)
                {
                    _staleModifier = _actionModifier;
                }
                _actionModifier = value;
            }
        }

        private string _actionModifier;
        private string _staleModifier;

        /// <summary>
        /// 줄 버튼을 누를 수 있는지. git 작업 중(그 결과가 보이기 전)에는 끈다.
        /// </summary>
        public bool ActionEnabled { get; set; } = true;

        public FileRowBinder(IFileIconProvider icons)
        {
            _icons = icons;
        }

        /// <param name="isChecked">null 이면 체크박스를 숨긴다.</param>
        /// <param name="nameOnly">트리에서는 폴더가 경로를 보여 주므로 파일 이름만 쓴다.</param>
        public void BindFile(VisualElement row, FileListItem item, bool? isChecked, bool nameOnly)
        {
            row.RemoveFromClassList(FolderModifier);
            BindToggle(row, isChecked, mixed: false);

            Label badge = row.Require<Label>("badge");
            badge.RemoveFromClassList(BadgeHiddenModifier);
            StateBadge.Apply(badge, item.State);
            _icons.BindIcon(row.Require<Image>("icon"), item.ProjectPath, item.RepoPath);

            Label path = row.Require<Label>("path");
            path.text = GitDisplay.Nfc(nameOnly ? item.DisplayPath.Split('/').Last() : item.DisplayPath)
                        + (item.MetaSource != null ? GitText.Get("file.metaSuffix") : string.Empty);
            path.tooltip = item.Tooltip;

            Label original = row.Require<Label>("original");
            original.text = item.OriginalRepoPath != null ? $"← {GitDisplay.Nfc(item.OriginalRepoPath)}" : string.Empty;
            original.SetVisible(item.OriginalRepoPath != null);

            Label note = row.Require<Label>("note");
            note.RemoveFromClassList(CountModifier);
            note.text = item.Note ?? string.Empty;
            note.SetVisible(string.IsNullOrEmpty(item.Note) == false);
            BindAction(row, isFile: true);
        }

        /// <summary>
        /// 줄 버튼: 파일 줄이고 쓰는 쪽이 정했으면 글자·모양 클래스를 붙인다(폴더 줄에는 없다).
        /// </summary>
        private void BindAction(VisualElement row, bool isFile)
        {
            Button action = row.Q<Button>("action");
            if (action == null)
            {
                return;
            }
            if (_staleModifier != null)
            {
                action.RemoveFromClassList(_staleModifier);
            }
            if (ActionModifier == null)
            {
                return;
            }
            action.text = ActionText;
            action.EnableInClassList(ActionModifier, isFile);
            action.SetEnabled(ActionEnabled);
        }

        /// <param name="checkedFiles">아래 파일 중 체크된 수. 체크박스가 없으면 무시한다.</param>
        public void BindFolder(VisualElement row, FileTreeNode folder, bool showToggle, int checkedFiles)
        {
            row.AddToClassList(FolderModifier);
            bool? isChecked = showToggle ? checkedFiles == folder.FileCount : null;
            BindToggle(row, isChecked, mixed: showToggle && checkedFiles > 0 && checkedFiles < folder.FileCount);

            row.Require<Label>("badge").AddToClassList(BadgeHiddenModifier);
            _folderIcon ??= EditorGUIUtility.IconContent("Folder Icon").image;
            Image icon = row.Require<Image>("icon");
            icon.image = _folderIcon;
            // 이 줄이 전에 보이던 파일의 썸네일이 늦게 와도 폴더 아이콘을 덮지 않게(IFileIconProvider.BindIcon).
            icon.userData = null;

            Label path = row.Require<Label>("path");
            path.text = GitDisplay.Nfc(folder.Name);
            path.tooltip = folder.Path;

            row.Require<Label>("original").SetVisible(false);

            Label note = row.Require<Label>("note");
            note.AddToClassList(CountModifier);
            note.text = folder.FileCount.ToString();
            note.SetVisible(true);
            BindAction(row, isFile: false);
        }

        /// <summary>
        /// 줄 버튼을 눌렀을 때 그 줄만 처리하도록 누름을 목록에 넘기지 않는다(목록 선택·더블클릭이 바뀌지 않게).
        /// </summary>
        public static void HookAction(VisualElement row, System.Action onClicked)
        {
            Button action = row.Q<Button>("action");
            if (action == null)
            {
                return;
            }
            action.clicked += onClicked;
            action.RegisterCallback<PointerDownEvent>(evt => evt.StopPropagation());
            action.RegisterCallback<PointerUpEvent>(evt => evt.StopPropagation());
            action.RegisterCallback<ClickEvent>(evt => evt.StopPropagation());
        }

        public static void AppendFileMenu(DropdownMenu menu, FileListItem item, IAssetNavigator assets)
        {
            bool canPing = item.State != GitFileState.Deleted && assets.CanPing(item.ProjectPath);
            DropdownMenuAction.Status status = canPing ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            menu.AppendAction(GitText.Get("common.pingAsset"), _ => assets.Ping(item.ProjectPath), status);
            menu.AppendAction(GitText.Get("common.reveal"), _ => assets.Reveal(item.ProjectPath), status);
            menu.AppendAction(GitText.Get("common.copyPath"), _ => assets.CopyToClipboard(item.RepoPath));
        }

        private static void BindToggle(VisualElement row, bool? isChecked, bool mixed)
        {
            Toggle toggle = row.Require<Toggle>("toggle");
            toggle.EnableInClassList(ToggleHiddenModifier, isChecked.HasValue == false);
            // 체크박스가 없는 목록(Unstaged·Staged·커밋 파일)에서 빈 칸(22px)이 남아 아이콘이 멀리 밀리지 않게 칸째 접는다.
            row.Require<VisualElement>("toggle-column").EnableInClassList(ToggleColumnHiddenModifier, isChecked.HasValue == false);
            toggle.showMixedValue = mixed;
            if (isChecked.HasValue)
            {
                toggle.SetValueWithoutNotify(isChecked.Value);
            }
        }
    }
}
