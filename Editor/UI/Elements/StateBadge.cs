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

        /// <summary>
        /// GitKraken 처럼 그림으로 보이는 상태: 수정 = 연필, 추가·untracked = +, 삭제 = −(<c>.state-badge--glyph-*</c>, 그림은 USS 가 넣는다).
        /// 나머지(이름 변경·충돌)는 UVCS 아이콘이나 글자 배지 그대로.
        /// </summary>
        private static string GlyphModifierOf(GitFileState state)
        {
            return state switch
            {
                GitFileState.Modified => Block + "--glyph-modified",
                GitFileState.Added => Block + "--glyph-added",
                GitFileState.Untracked => Block + "--glyph-added",
                GitFileState.Deleted => Block + "--glyph-deleted",
                _ => null,
            };
        }

        private static readonly string[] GlyphModifiers = { Block + "--glyph-modified", Block + "--glyph-added", Block + "--glyph-deleted" };

        public static void Apply(VisualElement badge, GitFileState state)
        {
            string glyph = GlyphModifierOf(state);
            Texture2D icon = glyph == null ? GitStatusIcons.Get(state) : null;
            bool useIcon = icon != null;
            bool useLetter = glyph == null && useIcon == false;

            badge.EnableInClassList(IconModifier, useIcon);
            foreach (string modifier in GlyphModifiers)
            {
                badge.EnableInClassList(modifier, modifier == glyph);
            }
            foreach (GitFileState each in AllStates)
            {
                badge.EnableInClassList(ModifierOf(each), useLetter && each == state);
            }
            // 그림 배지는 USS 의 background-image 를 쓰므로 인라인 값을 비워 둔다(인라인이 USS 를 덮는다).
            badge.style.backgroundImage = useIcon ? new StyleBackground(icon) : new StyleBackground(StyleKeyword.Null);
            if (badge is TextElement text)
            {
                text.text = useLetter ? GitDisplay.StateLetter(state) : string.Empty;
            }
            badge.tooltip = GitText.Get("state." + state);
        }

        private static string ModifierOf(GitFileState state)
        {
            return $"{Block}--{state.ToString().ToLowerInvariant()}";
        }
    }
}
