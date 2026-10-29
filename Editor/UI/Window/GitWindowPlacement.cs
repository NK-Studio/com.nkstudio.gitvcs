using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 새로 뜨는 보조 창(diff·히스토리·blame·비교)의 첫 자리. Unity 기본은 최소 크기에 가까운 작은 창이라 이미지 diff·코드가 손톱만 하게 보인다.
    /// 원하는 크기를 에디터 메인 창의 85% 안으로 줄여 메인 창 가운데에 둔다. 사용자가 옮기거나 키운 뒤에는 건드리지 않는다(새로 뜰 때만).
    /// </summary>
    internal static class GitWindowPlacement
    {
        private const float MaxShareOfMain = 0.85f;

        public static Rect CenteredOnMain(Vector2 preferred)
        {
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            float width = Mathf.Min(preferred.x, main.width * MaxShareOfMain);
            float height = Mathf.Min(preferred.y, main.height * MaxShareOfMain);
            return new Rect(main.x + (main.width - width) * 0.5f, main.y + (main.height - height) * 0.5f, width, height);
        }
    }
}
