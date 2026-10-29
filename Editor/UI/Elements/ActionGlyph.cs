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

        /// <summary>
        /// 태그(모서리가 잘린 꼬리표와 구멍). 사이드바 참조 트리.
        /// </summary>
        Tag,

        /// <summary>
        /// 폴더(왼쪽 위 손잡이가 있는 상자).
        /// </summary>
        Folder,

        /// <summary>
        /// 원격(구름).
        /// </summary>
        Cloud,

        /// <summary>
        /// GitHub 마크(채운 원 안의 고양이). github.com 원격. Octicons "mark-github" 를 옮겼다.
        /// </summary>
        GitHub,

        /// <summary>
        /// 지구본(원 안의 세로 타원과 위도선 셋). 원격 추가 서랍의 URL 탭.
        /// </summary>
        Globe,

        /// <summary>
        /// Pull Request(왼쪽 줄기 위아래 두 점과, 오른쪽 아래 점에서 올라가 왼쪽 위로 꺾이는 화살표).
        /// </summary>
        PullRequest,

        /// <summary>
        /// 로컬(노트북: 화면과 받침).
        /// </summary>
        Laptop,

        /// <summary>
        /// 서브모듈(상자 안의 상자).
        /// </summary>
        Submodule,

        /// <summary>
        /// 워크트리(나무: 세모 잎과 줄기).
        /// </summary>
        Worktree,

        /// <summary>
        /// 체크 표시(채운 도형). 커밋 그래프의 현재 브랜치 이름표. Material Symbols "check_small".
        /// </summary>
        Check,

        /// <summary>
        /// 계층도(위 칸 하나에서 갈라져 아래 칸 셋). 좁아진 커밋 그래프 머리 칸.
        /// </summary>
        Hierarchy,

        /// <summary>
        /// 말풍선(왼쪽 아래 꼬리가 달린 네모). 좁아진 커밋 메시지 머리 칸. Material Symbols "chat_bubble" 외곽선.
        /// </summary>
        Comment,

        /// <summary>
        /// 연필(비스듬한 몸통과 뾰족한 끝). 커밋 그래프 작업 중 줄의 제목 입력란.
        /// </summary>
        Pencil,

        /// <summary>
        /// 검색(손잡이 달린 돋보기). 작업 막대 브랜치 고르기의 검색창.
        /// </summary>
        Search,

        /// <summary>
        /// 아래를 향한 작은 삼각형(채움). 눌러서 펼치는 버튼 뒤에 붙인다.
        /// </summary>
        Caret,

        /// <summary>
        /// 원 안의 오른쪽 꺾쇠. 접힌 사이드바(아이콘 줄)를 다시 펼치는 버튼. 임시 아이콘.
        /// </summary>
        Expand,

        /// <summary>
        /// 더 보기(세로로 늘어선 점 셋). 사이드바 브랜치 줄의 메뉴 버튼. Material Symbols "more_vert".
        /// </summary>
        MoreVert,
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
                case ActionGlyphKind.Tag:
                    Polyline(painter, P(3.5f, 3.5f), P(10f, 3.5f), P(16.5f, 10f), P(10f, 16.5f), P(3.5f, 10f), P(3.5f, 3.5f));
                    DrawRing(painter, P(7f, 7f), 1.2f * scale);
                    break;
                case ActionGlyphKind.Folder:
                    Polyline(painter, P(3f, 5f), P(8f, 5f), P(9.5f, 7f), P(17f, 7f), P(17f, 15.5f), P(3f, 15.5f), P(3f, 5f));
                    break;
                case ActionGlyphKind.Cloud:
                    // Material Symbols "cloud"(FILL0 wght400 opsz48) SVG 경로(viewBox 0 -960 960 960)를 그대로 20 칸으로 옮겼다. 바깥·안쪽 두 윤곽을 홀짝 규칙으로 채워 테두리만 남긴다.
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(P(5.229f, 16.667f));
                    painter.QuadraticCurveTo(P(3.396f, 16.667f), P(2.115f, 15.385f));
                    painter.QuadraticCurveTo(P(0.833f, 14.104f), P(0.833f, 12.271f));
                    painter.QuadraticCurveTo(P(0.833f, 10.646f), P(1.875f, 9.417f));
                    painter.QuadraticCurveTo(P(2.917f, 8.188f), P(4.521f, 7.938f));
                    painter.QuadraticCurveTo(P(4.938f, 5.917f), P(6.479f, 4.635f));
                    painter.QuadraticCurveTo(P(8.021f, 3.354f), P(10.042f, 3.354f));
                    painter.QuadraticCurveTo(P(12.375f, 3.354f), P(13.979f, 5.052f));
                    painter.QuadraticCurveTo(P(15.583f, 6.75f), P(15.583f, 9.125f));
                    painter.LineTo(P(15.583f, 9.625f));
                    painter.QuadraticCurveTo(P(17.083f, 9.583f), P(18.125f, 10.594f));
                    painter.QuadraticCurveTo(P(19.167f, 11.604f), P(19.167f, 13.146f));
                    painter.QuadraticCurveTo(P(19.167f, 14.583f), P(18.125f, 15.625f));
                    painter.QuadraticCurveTo(P(17.083f, 16.667f), P(15.646f, 16.667f));
                    painter.LineTo(P(5.229f, 16.667f));
                    painter.ClosePath();
                    painter.MoveTo(P(5.229f, 15.417f));
                    painter.LineTo(P(15.646f, 15.417f));
                    painter.QuadraticCurveTo(P(16.583f, 15.417f), P(17.25f, 14.75f));
                    painter.QuadraticCurveTo(P(17.917f, 14.083f), P(17.917f, 13.146f));
                    painter.QuadraticCurveTo(P(17.917f, 12.208f), P(17.25f, 11.542f));
                    painter.QuadraticCurveTo(P(16.583f, 10.875f), P(15.646f, 10.875f));
                    painter.LineTo(P(14.333f, 10.875f));
                    painter.LineTo(P(14.333f, 9.125f));
                    painter.QuadraticCurveTo(P(14.333f, 7.229f), P(13.062f, 5.917f));
                    painter.QuadraticCurveTo(P(11.792f, 4.604f), P(9.958f, 4.604f));
                    painter.QuadraticCurveTo(P(8.125f, 4.604f), P(6.844f, 5.917f));
                    painter.QuadraticCurveTo(P(5.562f, 7.229f), P(5.562f, 9.125f));
                    painter.LineTo(P(5.167f, 9.125f));
                    painter.QuadraticCurveTo(P(3.875f, 9.125f), P(2.979f, 10.031f));
                    painter.QuadraticCurveTo(P(2.083f, 10.938f), P(2.083f, 12.271f));
                    painter.QuadraticCurveTo(P(2.083f, 13.583f), P(3f, 14.5f));
                    painter.QuadraticCurveTo(P(3.917f, 15.417f), P(5.229f, 15.417f));
                    painter.ClosePath();
                    painter.Fill(FillRule.OddEven);
                    break;
                case ActionGlyphKind.GitHub:
                    // 원본 SVG(16 칸)를 20 칸 가운데로 옮긴 외곽선 하나. 고양이는 원 아래에서 파고든 빈자리다.
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(P(10f, 2f));
                    painter.BezierCurveTo(P(14.42f, 2f), P(18f, 5.58f), P(18f, 10f));
                    painter.Arc(P(10f, 10f), 8f * scale, 0f, 71.429f);
                    painter.BezierCurveTo(P(12.15f, 17.67f), P(12f, 17.42f), P(12f, 17.21f));
                    painter.BezierCurveTo(P(12f, 16.94f), P(12.01f, 16.08f), P(12.01f, 15.01f));
                    painter.BezierCurveTo(P(12.01f, 14.26f), P(11.76f, 13.78f), P(11.47f, 13.53f));
                    painter.BezierCurveTo(P(13.25f, 13.33f), P(15.12f, 12.65f), P(15.12f, 9.58f));
                    painter.BezierCurveTo(P(15.12f, 8.7f), P(14.81f, 7.99f), P(14.3f, 7.43f));
                    painter.BezierCurveTo(P(14.38f, 7.23f), P(14.66f, 6.41f), P(14.22f, 5.31f));
                    painter.BezierCurveTo(P(14.22f, 5.31f), P(13.55f, 5.09f), P(12.02f, 6.13f));
                    painter.BezierCurveTo(P(11.38f, 5.95f), P(10.7f, 5.86f), P(10.02f, 5.86f));
                    painter.BezierCurveTo(P(9.34f, 5.86f), P(8.66f, 5.95f), P(8.02f, 6.13f));
                    painter.BezierCurveTo(P(6.49f, 5.1f), P(5.82f, 5.31f), P(5.82f, 5.31f));
                    painter.BezierCurveTo(P(5.38f, 6.41f), P(5.66f, 7.23f), P(5.74f, 7.43f));
                    painter.BezierCurveTo(P(5.23f, 7.99f), P(4.92f, 8.71f), P(4.92f, 9.58f));
                    painter.BezierCurveTo(P(4.92f, 12.64f), P(6.78f, 13.33f), P(8.56f, 13.53f));
                    painter.BezierCurveTo(P(8.33f, 13.73f), P(8.12f, 14.08f), P(8.05f, 14.6f));
                    painter.BezierCurveTo(P(7.59f, 14.81f), P(6.44f, 15.15f), P(5.72f, 13.94f));
                    painter.BezierCurveTo(P(5.57f, 13.7f), P(5.12f, 13.11f), P(4.49f, 13.12f));
                    painter.BezierCurveTo(P(3.82f, 13.13f), P(4.22f, 13.5f), P(4.5f, 13.65f));
                    painter.BezierCurveTo(P(4.84f, 13.84f), P(5.23f, 14.55f), P(5.32f, 14.78f));
                    painter.BezierCurveTo(P(5.48f, 15.23f), P(6f, 16.09f), P(8.01f, 15.72f));
                    painter.BezierCurveTo(P(8.01f, 16.39f), P(8.02f, 17.02f), P(8.02f, 17.21f));
                    painter.BezierCurveTo(P(8.02f, 17.42f), P(7.87f, 17.66f), P(7.47f, 17.59f));
                    painter.Arc(P(10f, 10f), 8f * scale, 108.435f, 180f);
                    painter.BezierCurveTo(P(2f, 5.58f), P(5.58f, 2f), P(10f, 2f));
                    painter.ClosePath();
                    painter.Fill();
                    break;
                case ActionGlyphKind.Globe:
                    DrawRing(painter, P(10f, 10f), 7.5f * scale);
                    painter.BeginPath();
                    painter.MoveTo(P(10f, 2.5f));
                    painter.BezierCurveTo(P(14f, 6f), P(14f, 14f), P(10f, 17.5f));
                    painter.BezierCurveTo(P(6f, 14f), P(6f, 6f), P(10f, 2.5f));
                    painter.Stroke();
                    Polyline(painter, P(2.5f, 10f), P(17.5f, 10f));
                    Polyline(painter, P(4f, 6.2f), P(16f, 6.2f));
                    Polyline(painter, P(4f, 13.8f), P(16f, 13.8f));
                    break;
                case ActionGlyphKind.PullRequest:
                    DrawRing(painter, P(6f, 4.5f), 1.6f * scale);
                    DrawRing(painter, P(6f, 15.5f), 1.6f * scale);
                    DrawRing(painter, P(14f, 15.5f), 1.6f * scale);
                    Polyline(painter, P(6f, 6.1f), P(6f, 13.9f));
                    Polyline(painter, P(14f, 13.9f), P(14f, 7.5f), P(12f, 5.5f), P(9f, 5.5f));
                    Polyline(painter, P(10.8f, 3.7f), P(9f, 5.5f), P(10.8f, 7.3f));
                    break;
                case ActionGlyphKind.Laptop:
                    Polyline(painter, P(5f, 13f), P(5f, 5f), P(15f, 5f), P(15f, 13f), P(5f, 13f));
                    Polyline(painter, P(3f, 15.5f), P(17f, 15.5f));
                    break;
                case ActionGlyphKind.Submodule:
                    Polyline(painter, P(3.5f, 3.5f), P(16.5f, 3.5f), P(16.5f, 16.5f), P(3.5f, 16.5f), P(3.5f, 3.5f));
                    DrawBox(painter, P, 7.5f, 7.5f);
                    break;
                case ActionGlyphKind.Worktree:
                    // Material Symbols "park"(FILL0 wght400 opsz48) SVG 경로(viewBox 0 -960 960 960)를 20 칸으로 옮겼다. 줄기 달린 나무 바깥 윤곽과 안쪽 윤곽을 홀짝 규칙으로 채워 테두리만 남긴다.
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(P(11.208f, 18.333f));
                    painter.LineTo(P(8.813f, 18.333f));
                    painter.LineTo(P(8.813f, 15.229f));
                    painter.LineTo(P(2.5f, 15.229f));
                    painter.LineTo(P(6.438f, 9.521f));
                    painter.LineTo(P(4.458f, 9.521f));
                    painter.LineTo(P(10f, 1.667f));
                    painter.LineTo(P(15.542f, 9.521f));
                    painter.LineTo(P(13.583f, 9.521f));
                    painter.LineTo(P(17.5f, 15.229f));
                    painter.LineTo(P(11.208f, 15.229f));
                    painter.ClosePath();
                    painter.MoveTo(P(4.917f, 13.979f));
                    painter.LineTo(P(15.104f, 13.979f));
                    painter.LineTo(P(11.167f, 8.271f));
                    painter.LineTo(P(13.021f, 8.271f));
                    painter.LineTo(P(10f, 3.979f));
                    painter.LineTo(P(6.979f, 8.271f));
                    painter.LineTo(P(8.854f, 8.271f));
                    painter.ClosePath();
                    painter.Fill(FillRule.OddEven);
                    break;
                case ActionGlyphKind.Check:
                    // 원본 SVG(viewBox 0 -960 960 960)를 20 칸으로 옮긴 외곽선을 선 대신 글자색으로 채운다.
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(P(8.333f, 13.25f));
                    painter.LineTo(P(5.146f, 10.063f));
                    painter.LineTo(P(6.021f, 9.188f));
                    painter.LineTo(P(8.333f, 11.625f));
                    painter.LineTo(P(13.979f, 5.979f));
                    painter.LineTo(P(14.854f, 6.854f));
                    painter.ClosePath();
                    painter.Fill();
                    break;
                case ActionGlyphKind.Hierarchy:
                    // 위 칸 → 줄기 → 가로대 → 아래 칸 셋으로 내려가는 가지.
                    Polyline(painter, P(8f, 2.5f), P(12f, 2.5f), P(12f, 6.5f), P(8f, 6.5f), P(8f, 2.5f));
                    Polyline(painter, P(10f, 6.5f), P(10f, 13.5f));
                    Polyline(painter, P(4f, 13.5f), P(4f, 10f), P(16f, 10f), P(16f, 13.5f));
                    Polyline(painter, P(2.5f, 13.5f), P(5.5f, 13.5f), P(5.5f, 16.5f), P(2.5f, 16.5f), P(2.5f, 13.5f));
                    Polyline(painter, P(8.5f, 13.5f), P(11.5f, 13.5f), P(11.5f, 16.5f), P(8.5f, 16.5f), P(8.5f, 13.5f));
                    Polyline(painter, P(14.5f, 13.5f), P(17.5f, 13.5f), P(17.5f, 16.5f), P(14.5f, 16.5f), P(14.5f, 13.5f));
                    break;
                case ActionGlyphKind.Comment:
                    // 말풍선: 위 네모에서 왼쪽 아래로 꼬리가 내려왔다 돌아간다.
                    Polyline(painter, P(3.5f, 3.5f), P(16.5f, 3.5f), P(16.5f, 13f), P(8f, 13f), P(4.5f, 16.5f), P(4.5f, 13f),
                        P(3.5f, 13f), P(3.5f, 3.5f));
                    break;
                case ActionGlyphKind.Pencil:
                    // 오른쪽 위 끝 → 몸통 두 변 → 왼쪽 아래 촉. 끝 가까이 가로지르는 선은 지우개 경계.
                    Polyline(painter, P(13.5f, 3.5f), P(16.5f, 6.5f), P(7f, 16f), P(3.5f, 16.5f), P(4f, 13f), P(13.5f, 3.5f));
                    Polyline(painter, P(11.5f, 5.5f), P(14.5f, 8.5f));
                    break;
                case ActionGlyphKind.Search:
                    // Material Symbols "search"(FILL0 wght400 opsz48) 의 렌즈(가운데 7.85,7.81 · 반지름 4.08~5.33)와 45° 손잡이를 20 칸 그대로 옮겨,
                    // 윤곽을 채우는 대신 선으로 긋는다. 원본 굵기(1.25 칸)는 작은 크기에서 가늘어 1.75 칸으로 둔다.
                    // (채운 윤곽을 한 번 더 긋는 식은 두 윤곽 사이에 이음선이 생겨 렌즈 왼쪽 위로 삐죽 선이 튀었다.)
                    painter.lineWidth = 1.75f * scale;
                    painter.BeginPath();
                    painter.Arc(P(7.85f, 7.81f), 4.7f * scale, 0f, 180f);
                    painter.Arc(P(7.85f, 7.81f), 4.7f * scale, 180f, 360f);
                    painter.ClosePath();
                    painter.Stroke();
                    painter.lineCap = LineCap.Butt;
                    Polyline(painter, P(11.2f, 11.15f), P(17.05f, 17.0f));
                    break;
                case ActionGlyphKind.Caret:
                    painter.fillColor = resolvedStyle.color;
                    painter.BeginPath();
                    painter.MoveTo(P(5f, 7.5f));
                    painter.LineTo(P(15f, 7.5f));
                    painter.LineTo(P(10f, 13f));
                    painter.ClosePath();
                    painter.Fill();
                    break;
                case ActionGlyphKind.Expand:
                    painter.BeginPath();
                    painter.Arc(P(10f, 10f), 7.5f * scale, 0f, 360f);
                    painter.Stroke();
                    Polyline(painter, P(8.5f, 6.5f), P(12f, 10f), P(8.5f, 13.5f));
                    break;
                case ActionGlyphKind.MoreVert:
                    // Material Symbols "more_vert"(FILL0 wght400 opsz48) SVG(viewBox 0 -960 960 960)의 점 셋(지름 96)을 20 칸으로 옮겨 글자색으로 채운다.
                    painter.fillColor = resolvedStyle.color;
                    foreach (float y in new[] { 4.333f, 10f, 15.667f })
                    {
                        painter.BeginPath();
                        painter.Arc(P(10f, y), 1f * scale, 0f, 180f);
                        painter.Arc(P(10f, y), 1f * scale, 180f, 360f);
                        painter.ClosePath();
                        painter.Fill();
                    }
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
