using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 그래프 캔버스의 확대·이동 계산만 맡는다. "월드"(확대 1 기준 픽셀)와 캔버스 좌표를 <c>캔버스 = 월드 × 확대 − 이동</c> 으로 오간다.
    /// UI 와 무관한 순수 계산이라 따로 시험할 수 있다.
    /// </summary>
    public sealed class GraphViewport
    {
        public const float MinZoom = 0.25f;
        public const float MaxZoom = 2f;

        public float Zoom { get; private set; } = 1f;
        public Vector2 Pan { get; private set; }

        public GraphViewport(float zoom, Vector2 pan)
        {
            Zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            Pan = pan;
        }

        public Vector2 ToCanvas(Vector2 world)
        {
            return world * Zoom - Pan;
        }

        public Vector2 ToWorld(Vector2 canvas)
        {
            return (canvas + Pan) / Zoom;
        }

        public void PanBy(Vector2 canvasDelta)
        {
            Pan -= canvasDelta;
        }

        /// <summary>휠 한 칸(delta 1)에 옮기는 픽셀. ScrollView 의 기본 휠 이동 크기와 같다.</summary>
        public const float WheelPanStep = 18f;

        /// <summary>
        /// 휠 입력 중 가로 이동으로 쓸 양(오른쪽이 +). 가로로 굴린 게 아니면 0 이고, 그때 휠은 그대로 확대·축소를 한다.
        /// 트랙패드 좌우 스와이프는 delta.x 가 더 크게 온다. Shift+휠은 OS 에 따라 delta.x 로 바뀌어 오기도, delta.y 그대로 오기도 해서 둘 다 받는다.
        /// </summary>
        public static float HorizontalWheel(Vector2 delta, bool shift)
        {
            if (shift)
            {
                return delta.x != 0f ? delta.x : delta.y;
            }
            return Mathf.Abs(delta.x) > Mathf.Abs(delta.y) ? delta.x : 0f;
        }

        /// <summary>캔버스의 한 점(마우스 위치 등) 아래 월드 지점이 그대로 있도록 확대·축소한다.</summary>
        public void ZoomAround(Vector2 canvasPoint, float newZoom)
        {
            Vector2 world = ToWorld(canvasPoint);
            Zoom = Mathf.Clamp(newZoom, MinZoom, MaxZoom);
            Pan = world * Zoom - canvasPoint;
        }

        /// <summary>
        /// 월드 지점을 화면의 정해진 비율 위치에 둔다. 기본은 정중앙.
        /// 예전에는 가로 3/4·세로 35% 에 두고 그래프가 화면 위로 내려오지 않게 막았는데, 커밋·줄이 적으면 HEAD 가 오른쪽 위 구석에 붙어 보였다.
        /// </summary>
        public void Focus(Vector2 world, Vector2 canvasSize, float horizontalRatio = 0.5f, float verticalRatio = 0.5f)
        {
            Pan = world * Zoom - new Vector2(canvasSize.x * horizontalRatio, canvasSize.y * verticalRatio);
        }
    }
}
