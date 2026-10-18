using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 상태 배지(<c>.state-badge</c>). UVCS 패키지가 있으면 UVCS 상태 아이콘을 배경 그림으로 쓰고(<c>--icon</c>),
    /// 없으면 상태별 BEM 수식어(<c>--modified</c> 등)의 색과 글자로 보인다. 색·크기는 USS 가 정한다.
    /// </summary>
    public static class StateBadge
    {
        private const string Block = "state-badge";
        private const string IconModifier = Block + "--icon";
        private static readonly GitFileState[] AllStates = (GitFileState[])Enum.GetValues(typeof(GitFileState));

        public static void Apply(VisualElement badge, GitFileState state)
        {
            Texture2D icon = GitStatusIcons.Get(state);
            bool useIcon = icon != null;

            badge.EnableInClassList(IconModifier, useIcon);
            foreach (GitFileState each in AllStates)
            {
                badge.EnableInClassList(ModifierOf(each), useIcon == false && each == state);
            }
            badge.style.backgroundImage = useIcon ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.None);
            if (badge is TextElement text)
            {
                text.text = useIcon ? string.Empty : GitDisplay.StateLetter(state);
            }
            badge.tooltip = GitText.Get("state." + state);
        }

        private static string ModifierOf(GitFileState state)
        {
            return $"{Block}--{state.ToString().ToLowerInvariant()}";
        }
    }
}
