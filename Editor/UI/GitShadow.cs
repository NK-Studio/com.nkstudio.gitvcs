using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    public enum GitShadowSize
    {
        /// <summary>
        /// 말풍선·떠 있는 버튼·그래프 이름표: 가까이 붙은 옅은 그림자.
        /// </summary>
        Small,

        /// <summary>
        /// 메뉴·팝업·대화 상자·서랍: 아래로 넓게 번지는 그림자.
        /// </summary>
        Large,
    }

    /// <summary>
    /// 다른 요소 위에 떠 있는 UI 아래에 그림자를 단다.
    ///
    /// USS drop-shadow(Unity 6.6+)는 요소를 따로 그려 합치면서 둥근 모서리 바깥에 흰 테두리가 한 겹 삐져나와 깨진 것처럼 보여서 쓰지 않는다.
    /// 대신 요소 자신이 테두리 바깥에 고리 모양 그림자를 크기를 조금씩 키우며 옅게 겹쳐 그린다(Painter2D, 모든 버전). 고리의 구멍은 요소 안쪽으로 1px
    /// 들여 두어 요소 가장자리와 그림자 사이에 밝은 틈이 생기지 않게 한다. 색은 테마 토큰(--git-shadow-large·small)을 GitThemes.uss 가 옮겨 적은
    /// --shadow-color 에서 읽어 테마를 바꾸면 따라간다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitShadow
    {
        private const string Block = "git-shadow";

        private static readonly CustomStyleProperty<Color> ColorProperty = new("--shadow-color");
        private static readonly ConditionalWeakTable<VisualElement, State> States = new();

        private sealed class State
        {
            public GitShadowSize Size;
            public Color Color = new(0f, 0f, 0f, 0.3f);
            public bool IsFixed;
        }

        public static void Add(VisualElement element, GitShadowSize size)
        {
            if (States.TryGetValue(element, out _))
            {
                return;
            }
            State state = new() { Size = size };
            States.Add(element, state);
            element.AddToClassList(Block);
            element.AddToClassList(size == GitShadowSize.Large ? Block + "--large" : Block + "--small");
            element.AddToClassList(Block + "--on");
            element.RegisterCallback<CustomStyleResolvedEvent>(evt =>
            {
                if (state.IsFixed == false && evt.customStyle.TryGetValue(ColorProperty, out Color color))
                {
                    state.Color = color;
                    element.MarkDirtyRepaint();
                }
            });
            element.generateVisualContent += context => Draw(element, state, context.painter2D);
        }

        /// <summary>
        /// 클래스를 매번 지우는 재사용 요소(그래프 이름표)처럼 USS 로 색을 받을 수 없을 때 색을 직접 정한다.
        /// </summary>
        public static void SetColor(VisualElement element, Color color)
        {
            if (States.TryGetValue(element, out State state) == false || (state.IsFixed && state.Color == color))
            {
                return;
            }
            state.IsFixed = true;
            state.Color = color;
            element.MarkDirtyRepaint();
        }

        private static void Draw(VisualElement element, State state, Painter2D painter)
        {
            Rect rect = new(Vector2.zero, element.layout.size);
            if (rect.width <= 0f || rect.height <= 0f || state.Color.a <= 0f)
            {
                return;
            }

            bool large = state.Size == GitShadowSize.Large;
            int layers = large ? 12 : 6;
            float drop = large ? 1f : 0.5f;
            float blur = large ? 7f : 2f;
            float radius = element.resolvedStyle.borderTopLeftRadius;
            Rect hole = new(rect.x + 1f, rect.y + 1f, rect.width - 2f, rect.height - 2f);
            float holeRadius = Mathf.Max(0f, radius - 1f);

            // 바깥으로 갈수록 덮는 겹이 줄어 옅어진다. 겹 간격을 제곱으로 벌려 가장자리 근처에 겹을 모으면, 가장자리에서 빠르게 옅어지고 바깥으로 긴 꼬리가
            // 남아 판처럼 끊겨 보이지 않는다. 가장 작은 겹도 아래로 내린 만큼(drop)은 커서 위쪽에서 구멍을 벗어나지 않는다.
            Color color = state.Color;
            color.a /= layers;
            painter.fillColor = color;
            for (int i = 0; i < layers; i++)
            {
                float t = 1f - (float)i / layers;
                float spread = drop + blur * t * t;
                Rect outer = new(rect.x - spread, rect.y + drop - spread, rect.width + spread * 2f, rect.height + spread * 2f);
                painter.BeginPath();
                RoundedRect(painter, outer, radius + spread);
                RoundedRect(painter, hole, holeRadius);
                painter.Fill(FillRule.OddEven);
            }
        }

        private static void RoundedRect(Painter2D painter, Rect rect, float radius)
        {
            float r = Mathf.Min(radius, Mathf.Min(rect.width, rect.height) * 0.5f);
            float x = rect.x;
            float y = rect.y;
            float right = rect.xMax;
            float bottom = rect.yMax;
            painter.MoveTo(new Vector2(x + r, y));
            painter.LineTo(new Vector2(right - r, y));
            painter.ArcTo(new Vector2(right, y), new Vector2(right, y + r), r);
            painter.LineTo(new Vector2(right, bottom - r));
            painter.ArcTo(new Vector2(right, bottom), new Vector2(right - r, bottom), r);
            painter.LineTo(new Vector2(x + r, bottom));
            painter.ArcTo(new Vector2(x, bottom), new Vector2(x, bottom - r), r);
            painter.LineTo(new Vector2(x, y + r));
            painter.ArcTo(new Vector2(x, y), new Vector2(x + r, y), r);
            painter.ClosePath();
        }
    }
}
