using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    public enum ActionGlyphKind
    {
        Undo,
        Redo,
        Fetch,
        Pull,
        Push,
        Stash,
        Pop,
        Terminal,

        /// <summary>
        /// 새로고침(위가 트인 원과 끝의 화살촉).
        /// </summary>
        Refresh,

        /// <summary>
        /// 경로 목록 보기(가로줄 세 개).
        /// </summary>
        PathList,

        /// <summary>
        /// 트리 보기(세로 줄기에서 갈라지는 두 가지와 끝 칸).
        /// </summary>
        FileTree,

        /// <summary>
        /// HEAD 로 가기(문이 있는 집). Material Symbols "home" 외곽선을 옮겼다.
        /// </summary>
        Home,

        /// <summary>
        /// 확대(손잡이 달린 돋보기 안의 +). Material Symbols "zoom_in".
        /// </summary>
        ZoomIn,

        /// <summary>
        /// 축소(돋보기 안의 −). Material Symbols "zoom_out".
        /// </summary>
        ZoomOut,

        /// <summary>
        /// 새 브랜치(세로 줄기 위아래 두 점과, 오른쪽 점에서 휘어 들어오는 가지).
        /// </summary>
        Branch,
    }

    /// <summary>
    /// 작업 막대 버튼의 선 아이콘. 그림 파일 대신 Painter2D 로 그려 어느 배율·테마에서도 선명하고, 색은 글자색(<c>color</c>)을 따른다.
    /// 좌표는 20×20 칸 기준으로 적고 실제 크기에 맞춰 늘린다.
    /// </summary>
    public sealed class ActionGlyph : VisualElement
    {
        private const string Block = "action-glyph";

        /// <summary>
        /// 그림을 그리는 기준 칸 크기. 아래 좌표가 모두 이 칸 안의 값이다.
        /// </summary>
        private const float Design = 20f;

        private readonly ActionGlyphKind _kind;

        public ActionGlyph(ActionGlyphKind kind)
        {
            _kind = kind;
            AddToClassList(Block);
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            // 마우스를 올리거나 꺼질 때 글자색이 바뀌면 다시 그린다.
            RegisterCallback<CustomStyleResolvedEvent>(_ => MarkDirtyRepaint());
        }

        private void Draw(MeshGenerationContext context)
        {
            Rect rect = contentRect;
            float scale = Mathf.Min(rect.width, rect.height) / Design;
            if (scale <= 0f)
            {
                return;
            }
            Vector2 origin = new(rect.x + (rect.width - Design * scale) * 0.5f, rect.y + (rect.height - Design * scale) * 0.5f);
            Vector2 P(float x, float y) => origin + new Vector2(x, y) * scale;

            Painter2D painter = context.painter2D;
            painter.strokeColor = resolvedStyle.color;
            painter.lineWidth = 1.6f * scale;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;

            switch (_kind)
            {
                case ActionGlyphKind.Undo:
                    DrawCurvedArrow(painter, P, mirrored: false);
                    break;
                case ActionGlyphKind.Redo:
                    DrawCurvedArrow(painter, P, mirrored: true);
                    break;
                case ActionGlyphKind.Fetch:
                    // 구름 아래로 내려오는 화살표.
                    Polyline(painter, P(5f, 11f), P(4f, 8.5f), P(6.5f, 5.5f), P(10f, 4.5f), P(13.5f, 5.5f), P(16f, 8.5f), P(15f, 11f));
                    Polyline(painter, P(10f, 8f), P(10f, 17f));
                    Polyline(painter, P(7f, 14f), P(10f, 17f), P(13f, 14f));
                    break;
                case ActionGlyphKind.Pull:
                    Polyline(painter, P(10f, 3f), P(10f, 13f));
                    Polyline(painter, P(6f, 9f), P(10f, 13f), P(14f, 9f));
                    Polyline(painter, P(4f, 17f), P(16f, 17f));
                    break;
                case ActionGlyphKind.Push:
                    Polyline(painter, P(10f, 15f), P(10f, 5f));
                    Polyline(painter, P(6f, 9f), P(10f, 5f), P(14f, 9f));
                    Polyline(painter, P(4f, 17f), P(16f, 17f));
                    break;
                case ActionGlyphKind.Stash:
                    DrawTray(painter, P);
                    Polyline(painter, P(10f, 2.5f), P(10f, 11f));
                    Polyline(painter, P(7f, 8f), P(10f, 11f), P(13f, 8f));
                    break;
                case ActionGlyphKind.Pop:
                    DrawTray(painter, P);
                    Polyline(painter, P(10f, 11f), P(10f, 2.5f));
                    Polyline(painter, P(7f, 5.5f), P(10f, 2.5f), P(13f, 5.5f));
                    break;
                case ActionGlyphKind.Terminal:
                    Polyline(painter, P(4f, 6f), P(9f, 10f), P(4f, 14f));
                    Polyline(painter, P(10.5f, 15f), P(16f, 15f));
                    break;
                case ActionGlyphKind.Refresh:
                    // 오른쪽 위(-60°)에서 시계 방향으로 돌아 왼쪽 위(240°)에서 끝나는 원. 화살촉은 그 끝에서 트인 쪽을 향한다.
                    painter.BeginPath();
                    painter.Arc(P(10f, 10.5f), 6f * scale, -60f, 240f);
                    painter.Stroke();
                    Polyline(painter, P(3.6f, 4.4f), P(7f, 5.3f), P(6.1f, 8.7f));
                    break;
                case ActionGlyphKind.PathList:
                    Polyline(painter, P(3f, 5f), P(17f, 5f));
                    Polyline(painter, P(3f, 10f), P(17f, 10f));
                    Polyline(painter, P(3f, 15f), P(17f, 15f));
                    break;
                case ActionGlyphKind.FileTree:
                    DrawBox(painter, P, 2.5f, 2.5f);
                    Polyline(painter, P(5f, 7.5f), P(5f, 15f), P(10f, 15f));
                    Polyline(painter, P(5f, 10f), P(10f, 10f));
                    DrawBox(painter, P, 10.5f, 7.5f);
                    DrawBox(painter, P, 10.5f, 12.5f);
                    break;
                case ActionGlyphKind.Home:
                    // 문 왼쪽 아래에서 시작해 문 → 오른쪽 벽 → 지붕 → 왼쪽 벽으로 한 바퀴.
                    Polyline(painter, P(8.3f, 16.9f), P(8.3f, 11.7f), P(11.7f, 11.7f), P(11.7f, 16.9f), P(16f, 16.9f),
                        P(16f, 7.8f), P(10f, 3.3f), P(4f, 7.8f), P(4f, 16.9f), P(8.3f, 16.9f));
                    break;
                case ActionGlyphKind.ZoomIn:
                    DrawMagnifier(painter, P, scale);
                    Polyline(painter, P(6.2f, 7.8f), P(9.4f, 7.8f));
                    Polyline(painter, P(7.8f, 6.2f), P(7.8f, 9.4f));
                    break;
                case ActionGlyphKind.ZoomOut:
                    DrawMagnifier(painter, P, scale);
                    Polyline(painter, P(6.2f, 7.8f), P(9.4f, 7.8f));
                    break;
                case ActionGlyphKind.Branch:
                    // 점이 굵은 선보다 무거워 보여서 다른 아이콘보다 안쪽(약 5~15)에 작게 그린다.
                    DrawRing(painter, P(7f, 5.6f), 1.6f * scale);
                    DrawRing(painter, P(7f, 14.4f), 1.6f * scale);
                    DrawRing(painter, P(13.2f, 6.8f), 1.6f * scale);
                    Polyline(painter, P(7f, 7.2f), P(7f, 12.8f));
                    painter.BeginPath();
                    painter.MoveTo(P(13.2f, 8.4f));
                    painter.BezierCurveTo(P(13.2f, 11f), P(7f, 10f), P(7f, 12.8f));
                    painter.Stroke();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_kind), _kind, null);
            }
        }

        /// <summary>
        /// 실행 취소(왼쪽으로 휘어 돌아가는 화살표). <paramref name="mirrored"/> 면 좌우를 뒤집어 다시 실행.
        /// </summary>
        private static void DrawCurvedArrow(Painter2D painter, Func<float, float, Vector2> p, bool mirrored)
        {
            float X(float x) => mirrored ? Design - x : x;
            painter.BeginPath();
            painter.MoveTo(p(X(5f), 8f));
            painter.LineTo(p(X(12f), 8f));
            painter.BezierCurveTo(p(X(18f), 8f), p(X(18f), 16f), p(X(12f), 16f));
            painter.LineTo(p(X(8f), 16f));
            painter.Stroke();
            Polyline(painter, p(X(8.5f), 4.5f), p(X(5f), 8f), p(X(8.5f), 11.5f));
        }

        /// <summary>
        /// 트리 아이콘의 작은 칸(5×5). 왼쪽 위 모서리 좌표를 받는다.
        /// </summary>
        private static void DrawBox(Painter2D painter, Func<float, float, Vector2> p, float left, float top)
        {
            Polyline(painter, p(left, top), p(left + 5f, top), p(left + 5f, top + 5f), p(left, top + 5f), p(left, top));
        }

        /// <summary>
        /// 확대·축소의 돋보기(왼쪽 위 원과 오른쪽 아래로 뻗는 손잡이). 안의 +/− 는 부르는 쪽이 그린다.
        /// </summary>
        private static void DrawMagnifier(Painter2D painter, Func<float, float, Vector2> p, float scale)
        {
            DrawRing(painter, p(7.8f, 7.8f), 4.7f * scale);
            Polyline(painter, p(11.2f, 11.2f), p(16.5f, 16.5f));
        }

        /// <summary>
        /// 속이 빈 원. 0°→360° 한 번은 시작·끝이 같아 비어 버릴 수 있어 반원 두 개로 잇는다.
        /// </summary>
        private static void DrawRing(Painter2D painter, Vector2 center, float radius)
        {
            painter.BeginPath();
            painter.Arc(center, radius, 0f, 180f);
            painter.Arc(center, radius, 180f, 360f);
            painter.ClosePath();
            painter.Stroke();
        }

        /// <summary>
        /// Stash/Pop 의 받침(위가 열린 상자).
        /// </summary>
        private static void DrawTray(Painter2D painter, Func<float, float, Vector2> p)
        {
            Polyline(painter, p(4f, 11f), p(4f, 17f), p(16f, 17f), p(16f, 11f));
        }

        private static void Polyline(Painter2D painter, params Vector2[] points)
        {
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int i = 1; i < points.Length; i++)
            {
                painter.LineTo(points[i]);
            }
            painter.Stroke();
        }
    }
}
