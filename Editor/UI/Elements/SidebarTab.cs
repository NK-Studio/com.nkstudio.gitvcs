using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>왼쪽 탭 버튼 하나(<c>.git-sidebar__tab</c>). 제목·개수 배지·선택 표시만 맡는다.</summary>
    public sealed class SidebarTab
    {
        private const string SelectedModifier = "git-sidebar__tab--selected";
        private const string BadgeHiddenModifier = "git-sidebar__tab-badge--hidden";

        private readonly Label _title;
        private readonly Label _badge;

        public VisualElement Root { get; }
        public event Action Clicked;

        public SidebarTab(VisualTreeAsset template, string title)
        {
            Root = GitUiAssets.CloneBlock(template);
            _title = Root.Require<Label>("title");
            _badge = Root.Require<Label>("badge");
            _title.text = title;
            Root.RegisterCallback<ClickEvent>(_ => Clicked?.Invoke());
        }

        public void SetBadge(string badge)
        {
            _badge.text = badge ?? string.Empty;
            _badge.EnableInClassList(BadgeHiddenModifier, string.IsNullOrEmpty(badge));
        }

        public void SetSelected(bool selected)
        {
            Root.EnableInClassList(SelectedModifier, selected);
        }
    }
}
