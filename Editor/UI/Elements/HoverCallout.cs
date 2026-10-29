using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 말풍선이 대상의 어느 쪽에 붙는지.
    /// </summary>
    public enum HoverCalloutPlacement
    {
        /// <summary>대상 오른쪽 가운데. 꼭지가 왼쪽에서 대상을 가리킨다(버튼 안내).</summary>
        Right,

        /// <summary>대상 아래 가운데. 꼭지가 위에서 대상을 가리킨다(사이드바 줄의 전체 이름).</summary>
        Below,
    }

    /// <summary>
    /// 마우스를 올리면 바로 뜨는 말풍선(GitKraken 의 "Add Remote" · "Create Pull Request" 안내, 사이드바 줄의 전체 이름).
    /// Unity 기본 tooltip 은 잠시 기다린 뒤 마우스 아래에 뜨고 창 배율·테마를 따르지 않아, 창 위 덮개(<see cref="GitServices.PopupHost"/>)에 직접 띄운다.
    /// 사이드바 밖으로 넘쳐도 잘리지 않고, 배율은 덮개가 함께 키운다.
    /// </summary>
    public static class HoverCallout
    {
        private const string Block = "hover-callout";

        /// <summary>덮개 가장자리와 띄울 거리.</summary>
        private const float EdgeMargin = 4f;

        /// <summary>
        /// 줄은 재사용되므로 글은 뜰 때마다 <paramref name="text"/> 로 다시 읽는다. 비어 있으면 띄우지 않는다.
        /// <see cref="HoverCalloutPlacement.Below"/> 의 글은 rich text(&lt;b&gt;)를 쓸 수 있다.
        /// </summary>
        public static void Attach(VisualElement target, Func<string> text, Func<VisualElement> host,
            HoverCalloutPlacement placement = HoverCalloutPlacement.Right)
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
                shown = Create(message, placement);
                layer.Add(shown);
                // 덮개 좌표로 옮겨 붙인다(배율이 걸려 있어도 WorldToLocal 이 맞춰 준다).
                Rect bounds = layer.WorldToLocal(target.worldBound);
                if (placement == HoverCalloutPlacement.Below)
                {
                    PlaceBelow(shown, layer, bounds);
                }
                else
                {
                    shown.style.left = bounds.xMax + 6f;
                    shown.style.top = bounds.center.y;
                }
            });
            target.RegisterCallback<PointerLeaveEvent>(_ => Hide());
            target.RegisterCallback<PointerDownEvent>(_ => Hide());
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide());
        }

        /// <summary>
        /// 대상 아래 가운데에 두되, 크기를 안 뒤 덮개 밖으로 넘치면 안쪽으로 밀고 꼭지는 대상 가운데에 남긴다.
        /// 크기를 알기 전에는 감춰 두어 제자리를 찾기 전 모습이 비치지 않게 한다.
        /// </summary>
        private static void PlaceBelow(VisualElement callout, VisualElement layer, Rect bounds)
        {
            callout.style.top = bounds.yMax + 4f;
            callout.style.left = bounds.center.x;
            callout.style.visibility = Visibility.Hidden;
            callout.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                float width = callout.layout.width;
                float maxLeft = Mathf.Max(EdgeMargin, layer.layout.width - width - EdgeMargin);
                float left = Mathf.Clamp(bounds.center.x - width * 0.5f, EdgeMargin, maxLeft);
                callout.style.left = left;
                callout[0].style.left = bounds.center.x - left - callout[0].layout.width * 0.5f;
                callout.style.visibility = Visibility.Visible;
            });
        }

        private static VisualElement Create(string message, HoverCalloutPlacement placement)
        {
            VisualElement callout = new();
            callout.AddToClassList(Block);
            callout.EnableInClassList(Block + "--below", placement == HoverCalloutPlacement.Below);
            callout.pickingMode = PickingMode.Ignore;
            VisualElement arrow = new();
            arrow.AddToClassList(Block + "__arrow");
            arrow.pickingMode = PickingMode.Ignore;
            callout.Add(arrow);
            Label label = new(message) { enableRichText = placement == HoverCalloutPlacement.Below };
            label.AddToClassList(Block + "__label");
            label.pickingMode = PickingMode.Ignore;
            callout.Add(label);
            return callout;
        }
    }
}
