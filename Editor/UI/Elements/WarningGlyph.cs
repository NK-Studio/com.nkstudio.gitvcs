using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 모서리가 둥근 경고 삼각형(가운데 느낌표는 뚫려 뒤 바탕이 보인다). 512 칸 SVG 경로를 옮겨 Painter2D 로 그린다 —
    /// 글꼴의 "⚠" 는 글꼴마다 모양·굵기가 달라 그래프 작업 중 줄 경고에 쓰지 않는다. 색은 <c>color</c> 스타일을 따른다.
    /// </summary>
    public sealed class WarningGlyph : VisualElement
    {
        private const float ViewBox = 512f;

        public WarningGlyph()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("warning-glyph");
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            float size = Mathf.Min(rect.width, rect.height);
            if (size <= 0f)
            {
                return;
            }
            float scale = size / ViewBox;
            Vector2 origin = new(rect.x + (rect.width - size) * 0.5f, rect.y + (rect.height - size) * 0.5f);
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;

            Painter2D painter = context.painter2D;
            painter.fillColor = resolvedStyle.color;
            painter.BeginPath();

            // 삼각형: 곧은 변 끝(SVG 의 모서리 호 시작·끝)을 잇고, 모서리는 뾰족한 꼭짓점을 조절점으로 둔 곡선으로 둥글린다.
            painter.MoveTo(P(507.494f, 426.066f));
            painter.LineTo(P(282.864f, 53.537f));
            painter.QuadraticCurveTo(P(256f, 9f), P(229.134f, 53.537f));
            painter.LineTo(P(4.506f, 426.066f));
            painter.QuadraticCurveTo(P(-24.2f, 473.6f), P(31.37f, 473.635f));
            painter.LineTo(P(480.629f, 473.635f));
            painter.QuadraticCurveTo(P(536.2f, 473.6f), P(507.494f, 426.066f));
            painter.ClosePath();

            // 느낌표 막대: 위는 둥글고 아래로 갈수록 조금 좁아진다.
            painter.MoveTo(P(232.35f, 187.4f));
            painter.BezierCurveTo(P(232.35f, 174.5f), P(242.9f, 167.2f), P(256.2f, 167.2f));
            painter.BezierCurveTo(P(269.1f, 167.2f), P(280f, 174.5f), P(280f, 187.4f));
            painter.LineTo(P(275.35f, 322.7f));
            painter.BezierCurveTo(P(275.35f, 333f), P(264.1f, 337.3f), P(256.2f, 337.3f));
            painter.BezierCurveTo(P(245.6f, 337.3f), P(236.65f, 333f), P(236.65f, 322.7f));
            painter.ClosePath();

            // 느낌표 점.
            painter.MoveTo(P(281.8f, 385.5f));
            painter.Arc(P(256.5f, 385.5f), 25.3f * scale, Angle.Degrees(0f), Angle.Degrees(360f));
            painter.ClosePath();

            // 짝수 번 겹친 곳(느낌표)은 비워 뒤 바탕(경고색)이 보이게 한다.
            painter.Fill(FillRule.OddEven);
        }
    }
}
