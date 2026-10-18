using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>목록/트리 전환 버튼 한 쌍(<c>.view-mode-switch</c>). 고른 쪽에 <c>--selected</c> 수식어를 단다.</summary>
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

            _listButton = CreateButton("fileView.list", "fileView.listTooltip", FileViewMode.List);
            _treeButton = CreateButton("fileView.tree", "fileView.treeTooltip", FileViewMode.Tree);

            preference.Changed += Render;
            Root.RegisterCallback<DetachFromPanelEvent>(_ => preference.Changed -= Render);
            Render();
        }

        private Button CreateButton(string textKey, string tooltipKey, FileViewMode mode)
        {
            Button button = new(() => _preference.Mode = mode)
            {
                text = GitText.Get(textKey),
                tooltip = GitText.Get(tooltipKey),
            };
            button.AddToClassList(Block + "__button");
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
