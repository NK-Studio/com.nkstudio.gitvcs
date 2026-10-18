using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 경로/트리 전환(<c>.view-mode-switch</c>). GitKraken 처럼 테두리로 묶은 두 칸에 아이콘과 글자를 두고, 고른 칸을 칠한다.
    /// </summary>
    public sealed class FileViewModeSwitch
    {
        private const string Block = "view-mode-switch";
        private const string SelectedModifier = Block + "__button--selected";

        private readonly FileViewPreference _preference;
        private readonly Button _listButton;
        private readonly Button _treeButton;

        public VisualElement Root { get; }

        public FileViewModeSwitch(FileViewPreference preference)
        {
            _preference = preference;
            Root = new VisualElement();
            Root.AddToClassList(Block);

            _listButton = CreateButton("fileView.list", "fileView.listTooltip", FileViewMode.List, ActionGlyphKind.PathList);
            _treeButton = CreateButton("fileView.tree", "fileView.treeTooltip", FileViewMode.Tree, ActionGlyphKind.FileTree);
            _treeButton.AddToClassList(Block + "__button--last");

            preference.Changed += Render;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => preference.Changed -= Render);
            Render();
        }

        private Button CreateButton(string textKey, string tooltipKey, FileViewMode mode, ActionGlyphKind icon)
        {
            Button button = new(() => _preference.Mode = mode) { tooltip = GitText.Get(tooltipKey) };
            button.AddToClassList(Block + "__button");

            ActionGlyph glyph = new(icon);
            glyph.AddToClassList(Block + "__icon");
            button.Add(glyph);

            Label label = new(GitText.Get(textKey));
            label.AddToClassList(Block + "__label");
            button.Add(label);

            Root.Add(button);
            return button;
        }

        private void Render()
        {
            _listButton.EnableInClassList(SelectedModifier, _preference.Mode == FileViewMode.List);
            _treeButton.EnableInClassList(SelectedModifier, _preference.Mode == FileViewMode.Tree);
        }
    }
}
