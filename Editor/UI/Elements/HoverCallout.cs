using System;
using System.Runtime.CompilerServices;
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
        /// 지금 떠 있는 말풍선과 그 주인. 한 번에 하나만 띄운다 — 줄 안의 버튼(⋮)에 올리면 줄의 말풍선 대신 버튼 것만 보인다.
        /// </summary>
        private static VisualElement s_shown;

        private static VisualElement s_owner;

        /// <summary>
        /// 대상마다 말풍선을 띄우는 법. 안쪽 버튼에서 나와 아직 줄 위에 있으면 줄의 말풍선을 다시 띄울 때 쓴다.
        /// </summary>
        private static readonly ConditionalWeakTable<VisualElement, Action> s_showFor = new();

        /// <summary>
        /// 줄은 재사용되므로 글은 뜰 때마다 <paramref name="text"/> 로 다시 읽는다. 비어 있으면 띄우지 않는다.
        /// <see cref="HoverCalloutPlacement.Below"/> 의 글은 rich text(&lt;b&gt;)를 쓸 수 있다.
        /// 다른 말풍선 대상 안에 든 대상(줄 안의 버튼)이면 그 위에서는 이 말풍선만, 나오면 바깥 대상의 말풍선을 다시 띄운다.
        /// </summary>
        public static void Attach(VisualElement target, Func<string> text, Func<VisualElement> host,
            HoverCalloutPlacement placement = HoverCalloutPlacement.Right)
        {
            void Show()
            {
                Hide(null);
                string message = text();
                VisualElement layer = host();
                if (string.IsNullOrEmpty(message) || layer == null || target.panel == null)
                {
                    return;
                }
                VisualElement callout = Create(message, placement);
                s_shown = callout;
                s_owner = target;
                layer.Add(callout);
                // 덮개 좌표로 옮겨 붙인다(배율이 걸려 있어도 WorldToLocal 이 맞춰 준다).
                Rect bounds = layer.WorldToLocal(target.worldBound);
                if (placement == HoverCalloutPlacement.Below)
                {
                    PlaceBelow(callout, layer, bounds);
                }
                else
                {
                    callout.style.left = bounds.xMax + 6f;
                    callout.style.top = bounds.center.y;
                }
            }

            s_showFor.AddOrUpdate(target, Show);
            target.RegisterCallback<PointerEnterEvent>(_ => Show());
            target.RegisterCallback<PointerLeaveEvent>(evt =>
            {
                if (Hide(target) == false)
                {
                    return;
                }
                // 줄 안의 버튼에서 나왔지만 아직 줄 위라면 줄의 말풍선으로 돌아간다.
                for (VisualElement parent = target.parent; parent != null; parent = parent.parent)
                {
                    if (s_showFor.TryGetValue(parent, out Action showParent))
                    {
                        if (parent.worldBound.Contains(evt.position))
                        {
                            showParent();
                        }
                        break;
                    }
                }
            });
            target.RegisterCallback<PointerDownEvent>(_ => Hide(target));
            target.RegisterCallback<DetachFromPanelEvent>(_ => Hide(target));
        }

        /// <summary>
        /// 떠 있는 말풍선을 닫는다. <paramref name="owner"/> 를 주면 그 대상의 것일 때만 닫고, 닫았는지 돌려준다.
        /// </summary>
        private static bool Hide(VisualElement owner)
        {
            if (s_shown == null || (owner != null && s_owner != owner))
            {
                return false;
            }
            s_shown.RemoveFromHierarchy();
            s_shown = null;
            s_owner = null;
            return true;
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
