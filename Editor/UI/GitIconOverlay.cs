using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Project 창 아이콘 왼쪽 아래에 git 상태 배지를 그린다.
    /// 매 Repaint 마다 보이는 항목 수만큼 불리므로 <see cref="GitRepository"/> 캐시 조회 외의 일은 하지 않는다.
    /// </summary>
    internal static class GitIconOverlay
    {
        private static readonly Color Outline = new(0f, 0f, 0f, 0.6f);
        private static GUIStyle s_letterStyle;

        public static Color ColorOf(GitFileState state)
        {
            switch (state)
            {
                case GitFileState.Modified: return new Color(1f, 0.62f, 0.1f);
                case GitFileState.Added: return new Color(0.3f, 0.82f, 0.3f);
                case GitFileState.Deleted: return new Color(0.92f, 0.3f, 0.3f);
                case GitFileState.Renamed: return new Color(0.35f, 0.65f, 1f);
                case GitFileState.Untracked: return new Color(0.45f, 0.79f, 0.57f);
                case GitFileState.Conflicted: return new Color(1f, 0.2f, 0.8f);
                default: return Color.white;
            }
        }

        public static string LetterOf(GitFileState state)
        {
            return GitDisplay.StateLetter(state);
        }

        public static void Draw(string assetPath, Rect iconRect)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(assetPath))
            {
                return;
            }

            if (GitRepository.TryGetState(assetPath, out GitFileState state))
            {
                Texture2D uvcsIcon = GitStatusIcons.Get(state);
                if (uvcsIcon != null)
                {
                    DrawUvcsIcon(iconRect, uvcsIcon);
                }
                else
                {
                    DrawBadge(iconRect, ColorOf(state), LetterOf(state), filled: true);
                }
            }
            else if (GitRepository.FolderHasChanges(assetPath))
            {
                // 폴더 자체는 그대로지만 안에 변경이 있다. 파일 배지와 구분되게 작은 점만 찍는다.
                DrawBadge(iconRect, ColorOf(GitFileState.Modified), null, filled: false);
            }
        }

        /// <summary>
        /// UVCS 아이콘을 에셋 아이콘 왼쪽 아래에 붙인다. PNG 둘레의 투명 여백을 빼고 실제 그림(가운데 10/16)이 아이콘 모서리에 맞도록 민다.
        /// 그림 크기는 16px 목록 아이콘에서 10px, 큰 격자 아이콘에서는 아이콘의 40% 까지 커진다.
        /// </summary>
        private static void DrawUvcsIcon(Rect iconRect, Texture2D icon)
        {
            float glyph = Mathf.Clamp(iconRect.height * 0.4f, 10f, 24f);
            float size = glyph / GitStatusIcons.GlyphRatio;
            float margin = (size - glyph) * 0.5f;
            Rect rect = new(iconRect.xMin - margin, iconRect.yMax - glyph - margin, size, size);
            GUI.DrawTexture(rect, icon, ScaleMode.ScaleToFit);
        }

        private static void DrawBadge(Rect iconRect, Color color, string letter, bool filled)
        {
            float size = Mathf.Clamp(iconRect.height * (filled ? 0.5f : 0.35f), 6f, 18f);
            Rect badge = new(iconRect.xMin, iconRect.yMax - size, size, size);

            GUI.DrawTexture(badge, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, Outline, 0f, size * 0.5f);
            Rect inner = new(badge.x + 1f, badge.y + 1f, badge.width - 2f, badge.height - 2f);
            GUI.DrawTexture(inner, Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f, color, 0f, inner.width * 0.5f);

            // 16px 목록 아이콘에서는 글자가 뭉개지니, 큰 격자 아이콘일 때만 글자를 넣는다.
            if (letter != null && size >= 12f)
            {
                s_letterStyle ??= new GUIStyle(EditorStyles.miniBoldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(0, 0, 0, 0),
                    normal = { textColor = Color.black },
                };
                s_letterStyle.fontSize = Mathf.RoundToInt(size * 0.65f);
                GUI.Label(badge, letter, s_letterStyle);
            }
        }
    }
}
