using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Project 창 아이콘 왼쪽 아래에 git 상태 아이콘(<see cref="GitStatusIcons"/>)을 그린다.
    /// 매 Repaint 마다 보이는 항목 수만큼 불리므로 <see cref="GitRepository"/> 캐시 조회 외의 일은 하지 않는다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    internal static class GitIconOverlay
    {
        public static void Draw(string assetPath, Rect iconRect)
        {
            if (Event.current.type != EventType.Repaint || string.IsNullOrEmpty(assetPath))
            {
                return;
            }
            // 그 에셋(또는 .meta 를 합친 폴더) 자체가 바뀌었을 때만 상태 아이콘을 붙인다.
            // 안에 변경이 있는 폴더에 색 점을 찍거나, 아이콘이 없을 때 색 동그라미로 대신하지는 않는다(색만 있는 동그라미는 뜻을 알기 어렵다).
            if (GitRepository.TryGetState(assetPath, out GitFileState state) && GitStatusIcons.Get(state) is Texture2D icon)
            {
                DrawUvcsIcon(iconRect, icon);
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
    }
}
