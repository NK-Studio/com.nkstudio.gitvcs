using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 트리 펼침 화살표의 회전 연출을 사용자가 직접 펼치거나 접은 줄에만 보인다.
    /// TreeView 는 펼치고 접을 때마다 줄 칸을 다시 써서, 다른 줄에서 쓰던 칸이 새 줄에 묶이며 화살표 상태가 바뀐다.
    /// 회전 연출이 늘 켜져 있으면 손대지 않은 줄의 화살표까지 함께 돈다. 그래서 연출은 누른 줄(또는 키보드로 고른 줄)에만 잠깐 켠다.
    /// </summary>
    internal static class GitTreeToggleAnimation
    {
        /// <summary>이 클래스가 붙은 줄만 화살표 회전 연출을 한다(GitThemes.uss).</summary>
        public const string ToggleClass = "git-tree-item--toggling";

        private const string ItemClass = "unity-tree-view__item";
        private const string SelectedClass = "unity-collection-view__item--selected";

        /// <summary>회전(0.12s)이 끝날 때까지 연출 클래스를 둔다.</summary>
        private const long HoldMs = 200;

        /// <summary>
        /// root 아래 트리의 마우스·키보드 펼침에 연출을 붙인다. 같은 콜백은 한 번만 붙으므로 여러 번 불러도 된다.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<PointerDownEvent>(OnPointerDown, TrickleDown.TrickleDown);
            root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        private static void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.target is VisualElement target)
            {
                Mark(target.ItemOrSelf());
            }
        }

        /// <summary>왼쪽·오른쪽 화살표 키로 고른 줄을 펼치고 접는다.</summary>
        private static void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != UnityEngine.KeyCode.LeftArrow && evt.keyCode != UnityEngine.KeyCode.RightArrow)
            {
                return;
            }
            if (evt.target is VisualElement target && (target as TreeView ?? target.GetFirstAncestorOfType<TreeView>()) is TreeView tree)
            {
                tree.Query(className: SelectedClass).ForEach(Mark);
            }
        }

        private static VisualElement ItemOrSelf(this VisualElement element)
        {
            for (VisualElement current = element; current != null; current = current.parent)
            {
                if (current.ClassListContains(ItemClass))
                {
                    return current;
                }
            }
            return null;
        }

        private static void Mark(VisualElement item)
        {
            if (item == null)
            {
                return;
            }
            item.AddToClassList(ToggleClass);
            item.schedule.Execute(() => item.RemoveFromClassList(ToggleClass)).StartingIn(HoldMs);
        }
    }
}
