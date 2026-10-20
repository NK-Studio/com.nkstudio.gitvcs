using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 왼쪽 사이드바를 접었을 때 대신 보이는 아이콘 줄(GitKraken 에서 파일을 열면 왼쪽 패널이 줄어든 모양).
    /// 맨 위 펼치기 버튼, 그 아래 탭(뷰) 아이콘, 그 아래 참조 구역(로컬·리모트…) 아이콘과 개수.
    /// 누를 때 할 일은 만드는 쪽(<see cref="GitWindow"/>)이 정한다 — 지금은 모두 사이드바를 다시 펼친다.
    /// 아이콘은 아직 임시(<see cref="ActionGlyph"/>)다 — 바꿀 때는 아이콘을 넘기는 쪽(<see cref="GitWindow"/>)만 고치면 된다.
    /// </summary>
    public sealed class SidebarRail
    {
        private const string Block = "git-rail";
        private const string SelectedModifier = Block + "__item--selected";
        private const string BadgeHiddenModifier = Block + "__badge--hidden";

        private readonly VisualElement _views;
        private readonly VisualElement _sections;
        private readonly List<VisualElement> _viewItems = new();
        private readonly List<Label> _viewBadges = new();
        private readonly Dictionary<RefSection, Label> _sectionBadges = new();

        public VisualElement Root { get; }

        /// <summary>
        /// 펼치기 버튼을 눌렀을 때. 창이 가운데 diff 를 닫고 사이드바를 편다.
        /// </summary>
        public event Action ExpandRequested;

        public SidebarRail(VisualElement host)
        {
            Root = host;
            Root.AddToClassList(Block);

            VisualElement expand = MakeItem(ActionGlyphKind.Expand, GitText.Get("rail.expand"), () => ExpandRequested?.Invoke(), out _);
            expand.AddToClassList(Block + "__item--expand");
            Root.Add(expand);

            _views = new VisualElement();
            _views.AddToClassList(Block + "__group");
            Root.Add(_views);

            _sections = new VisualElement();
            _sections.AddToClassList(Block + "__group");
            Root.Add(_sections);
        }

        /// <summary>
        /// 탭 아이콘 하나를 더한다. 순서는 왼쪽 탭 목록과 같다.
        /// </summary>
        public void AddView(ActionGlyphKind icon, string title, Action clicked)
        {
            VisualElement item = MakeItem(icon, title, clicked, out Label badge);
            _views.Add(item);
            _viewItems.Add(item);
            _viewBadges.Add(badge);
        }

        public void SetViewBadge(int index, string badge)
        {
            SetBadge(_viewBadges[index], badge);
        }

        public void SetSelectedView(int index)
        {
            for (int i = 0; i < _viewItems.Count; i++)
            {
                _viewItems[i].EnableInClassList(SelectedModifier, i == index);
            }
        }

        /// <summary>
        /// 참조 구역 아이콘 하나를 더한다. 개수는 <see cref="SetSectionCount"/> 로 채운다.
        /// </summary>
        public void AddSection(RefSection section, ActionGlyphKind icon, Action clicked)
        {
            VisualElement item = MakeItem(icon, GitText.Get("refs.section" + section), clicked, out Label badge);
            _sections.Add(item);
            _sectionBadges[section] = badge;
        }

        public void SetSectionCount(RefSection section, int count)
        {
            if (_sectionBadges.TryGetValue(section, out Label badge))
            {
                SetBadge(badge, count.ToString());
            }
        }

        private static VisualElement MakeItem(ActionGlyphKind icon, string tooltip, Action clicked, out Label badge)
        {
            VisualElement item = new() { tooltip = tooltip };
            item.AddToClassList(Block + "__item");
            item.RegisterCallback<ClickEvent>(_ => clicked?.Invoke());

            VisualElement iconHost = new() { pickingMode = PickingMode.Ignore };
            iconHost.AddToClassList(Block + "__icon");
            iconHost.Add(new ActionGlyph(icon));
            item.Add(iconHost);

            badge = new Label { pickingMode = PickingMode.Ignore };
            badge.AddToClassList(Block + "__badge");
            badge.AddToClassList(BadgeHiddenModifier);
            item.Add(badge);
            return item;
        }

        private static void SetBadge(Label badge, string text)
        {
            badge.text = text ?? string.Empty;
            badge.EnableInClassList(BadgeHiddenModifier, string.IsNullOrEmpty(text));
        }
    }
}
