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
        private const string BadgeHiddenModifier = "file-row__badge--hidden";
        private const string FolderModifier = "file-row--folder";
        private const string CountModifier = "file-row__note--count";

        private readonly IFileIconProvider _icons;
        private Texture _folderIcon;

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
            row.Require<Image>("icon").image = _icons.GetIcon(item.ProjectPath, item.RepoPath);

            Label path = row.Require<Label>("path");
            path.text = nameOnly ? item.DisplayPath.Split('/').Last() : item.DisplayPath;
            path.tooltip = item.Tooltip;

            Label original = row.Require<Label>("original");
            original.text = item.OriginalRepoPath != null ? $"← {item.OriginalRepoPath}" : string.Empty;
            original.SetVisible(item.OriginalRepoPath != null);

            Label note = row.Require<Label>("note");
            note.RemoveFromClassList(CountModifier);
            note.text = item.Note ?? string.Empty;
            note.SetVisible(string.IsNullOrEmpty(item.Note) == false);
        }

        /// <param name="checkedFiles">아래 파일 중 체크된 수. 체크박스가 없으면 무시한다.</param>
        public void BindFolder(VisualElement row, FileTreeNode folder, bool showToggle, int checkedFiles)
        {
            row.AddToClassList(FolderModifier);
            bool? isChecked = showToggle ? checkedFiles == folder.FileCount : null;
            BindToggle(row, isChecked, mixed: showToggle && checkedFiles > 0 && checkedFiles < folder.FileCount);

            row.Require<Label>("badge").AddToClassList(BadgeHiddenModifier);
            _folderIcon ??= EditorGUIUtility.IconContent("Folder Icon").image;
            row.Require<Image>("icon").image = _folderIcon;

            Label path = row.Require<Label>("path");
            path.text = folder.Name;
            path.tooltip = folder.Path;

            row.Require<Label>("original").SetVisible(false);

            Label note = row.Require<Label>("note");
            note.AddToClassList(CountModifier);
            note.text = folder.FileCount.ToString();
            note.SetVisible(true);
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
            toggle.showMixedValue = mixed;
            if (isChecked.HasValue)
            {
                toggle.SetValueWithoutNotify(isChecked.Value);
            }
        }
    }
}
