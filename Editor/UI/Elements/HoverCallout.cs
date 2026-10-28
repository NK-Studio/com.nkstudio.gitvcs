using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 버튼에 마우스를 올리면 오른쪽에 바로 뜨는 말풍선(GitKraken 의 "Add Remote" · "Create Pull Request" 안내).
    /// Unity 기본 tooltip 은 잠시 기다린 뒤 마우스 아래에 뜨고 창 배율·테마를 따르지 않아, 창 위 덮개(<see cref="GitServices.PopupHost"/>)에 직접 띄운다.
    /// 사이드바 밖으로 넘쳐도 잘리지 않고, 배율은 덮개가 함께 키운다.
    /// </summary>
    public static class HoverCallout
    {
        private const string Block = "hover-callout";

        /// <summary>
        /// 줄은 재사용되므로 글은 뜰 때마다 <paramref name="text"/> 로 다시 읽는다. 비어 있으면 띄우지 않는다.
        /// </summary>
        public static void Attach(VisualElement target, Func<string> text, Func<VisualElement> host)
        {
            VisualElement shown = null;

            void Hide()
            {
                shown?.RemoveFromHierarchy();
                shown = null;
            }

            target.RegisterCallback<PointerEnterEvent>(_ =>
            {
                Hide();
                string message = text();
                VisualElement layer = host();
                if (string.IsNullOrEmpty(message) || layer == null || target.panel == null)
                {
                    return;
                }
                shown = Create(message);
                layer.Add(shown);
                // 덮개 좌표로 옮겨 버튼 오른쪽 가운데에 붙인다(배율이 걸려 있어도 WorldToLocal 이 맞춰 준다).
                UnityEngine.Rect bounds = layer.WorldToLocal(target.worldBound);
                shown.style.left = bounds.xMax + 8f;
                shown.style.top = bounds.center.y;
            });
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            target.RegisterCallback<PointerDownEvent>(_ => Hide());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        private static VisualElement Create(string message)
        {
            VisualElement callout = new();
            callout.AddToClassList(Block);
            callout.pickingMode = PickingMode.Ignore;
            VisualElement arrow = new();
            arrow.AddToClassList(Block + "__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            callout.Add(arrow);
            Label label = new(message);
            label.AddToClassList(Block + "__label");
            label.pickingMode = PickingMode.Ignore;
            callout.Add(label);
            return callout;
        }
    }
}
