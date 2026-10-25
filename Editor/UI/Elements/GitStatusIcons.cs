using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 상태 아이콘을 Unity Version Control 패키지(com.unity.collab-proxy)에서 가져온다 — Project 창에서 UVCS 와 같은 모양으로 보이게.
    ///
    /// 파일을 복사해 오지 않고 설치된 패키지에서 그때그때 읽는다. 패키지가 없으면 null 을 돌려주고, 그리는 쪽은 글자 배지로 대신한다.
    /// 다크 테마는 d_ 접두사, 고해상도 화면은 @2x 파일을 쓴다(패키지의 Images.cs 와 같은 규칙).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitStatusIcons
    {
        private const string ImageFolder = "Packages/com.unity.collab-proxy/Editor/Assets/Images/";

        private static readonly Dictionary<string, Texture2D> Cache = new();

        /// <summary>
        /// UVCS 아이콘 PNG 는 16×16 중 가운데 약 10px 만 그림이고 둘레는 투명하다. 배치할 때 이 비율로 실제 그림 크기를 맞춘다.
        /// </summary>
        public const float GlyphRatio = 10f / 16f;

        public static bool IsAvailable => Get(GitFileState.Modified) != null;

        public static Texture2D Get(GitFileState state)
        {
            string name = state switch
            {
                GitFileState.Modified => "iconcheckedoutlocaloverlay",
                GitFileState.Added => "iconaddedoverlay",
                GitFileState.Deleted => "icondeletedlocaloverlay",
                GitFileState.Renamed => "iconmovedoverlay",
                GitFileState.Untracked => "iconprivateoverlay",
                GitFileState.Conflicted => "iconconflictedoverlay",
                _ => null,
            };
            if (name == null)
            {
                return null;
            }

            string prefix = EditorGUIUtility.isProSkin ? "d_" : string.Empty;
            bool hiDpi = EditorGUIUtility.pixelsPerPoint > 1f;
            string key = prefix + name + (hiDpi ? "@2x" : string.Empty);
            if (Cache.TryGetValue(key, out Texture2D cached) && cached != null)
            {
                return cached;
            }

            Texture2D texture = null;
            if (hiDpi)
            {
                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(ImageFolder + prefix + name + "@2x.png");
            }
            texture ??= AssetDatabase.LoadAssetAtPath<Texture2D>(ImageFolder + prefix + name + ".png");
            Cache[key] = texture;
            return texture;
        }
    }
}
