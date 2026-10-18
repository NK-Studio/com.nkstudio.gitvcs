using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 전체 UI 크기 조절. Ctrl(macOS 는 Cmd)+휠, Ctrl+= / Ctrl+- / Ctrl+0.
    ///
    /// 글자 크기만 바꾸는 대신 내용 전체를 scale 로 키우고, 그만큼 작은 논리 크기(창 크기 ÷ 배율)로 배치한다.
    /// 그래야 목록 줄 높이·여백·아이콘·그래프까지 같은 비율로 커지고, 창 밖으로 넘치거나 남는 공간이 생기지 않는다.
    /// 휠만 굴리면 목록 스크롤·그래프 확대 그대로이고, Ctrl 을 누른 휠만 가로챈다(TrickleDown 으로 자식보다 먼저 받는다).
    ///
    /// 이벤트는 창 루트가 아니라 창을 채우는 host 에 건다 — 창이 UI 를 다시 만들 때(언어 전환) host 와 함께 버려져 중복 등록되지 않는다.
    /// </summary>
    public sealed class UiScaleController
    {
        public const float MinScale = 0.6f;
        public const float MaxScale = 2f;

        /// <summary>
        /// 휠 한 칸·단축키 한 번에 바뀌는 양. 10% 단위라 배율이 100%·110% 처럼 읽기 쉬운 값으로 떨어진다.
        /// </summary>
        public const float ScaleStep = 0.1f;

        /// <summary>
        /// 배율을 바꾼 뒤 "120%" 표시를 남겨 두는 시간.
        /// </summary>
        private const long IndicatorMs = 900;

        private readonly VisualElement _root;
        private readonly VisualElement _content;
        private readonly Func<float> _getScale;
        private readonly Action<float> _setScale;
        private readonly Label _indicator;
        private IVisualElementScheduledItem _hideIndicator;

        /// <param name="host">창을 채우는 빈 요소. 휠·키를 여기서 받는다.</param>
        /// <param name="content">키우고 줄일 내용. host 의 자식이어야 한다.</param>
        public UiScaleController(VisualElement host, VisualElement content, Func<float> getScale, Action<float> setScale)
        {
            _root = host;
            _root.style.flexGrow = 1;
            _root.style.overflow = Overflow.Hidden;
            _root.focusable = true;
            _content = content;
            _getScale = getScale;
            _setScale = setScale;

            _content.style.position = Position.Absolute;
            _content.style.left = 0;
            _content.style.top = 0;
            _content.style.transformOrigin = new TransformOrigin(0, 0);

            _indicator = new Label();
            _indicator.pickingMode = PickingMode.Ignore;
            // 창의 USS 는 내용(템플릿) 쪽에 붙어 있어 host 의 자식에는 닿지 않으므로 여기서 직접 꾸민다.
            _indicator.style.display = DisplayStyle.None;
            _indicator.style.position = Position.Absolute;
            _indicator.style.right = 12;
            _indicator.style.bottom = 12;
            _indicator.style.paddingLeft = 10;
            _indicator.style.paddingRight = 10;
            _indicator.style.paddingTop = 4;
            _indicator.style.paddingBottom = 4;
            _indicator.style.fontSize = 14;
            _indicator.style.unityFontStyleAndWeight = FontStyle.Bold;
            _indicator.style.color = Color.white;
            _indicator.style.backgroundColor = new Color(0f, 0f, 0f, 0.75f);
            SetRadius(_indicator.style, 4f);
            _root.Add(_indicator);

            _root.RegisterCallback<GeometryChangedEvent>(_ => Apply());
            _root.RegisterCallback<WheelEvent>(OnWheel, TrickleDown.TrickleDown);
            _root.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            Apply();
        }

        /// <summary>
        /// 휠 방향대로 한 단계 바꾼 배율. 위로 굴리면(delta.y &lt; 0) 커진다.
        /// </summary>
        public static float NextScale(float current, float wheelDeltaY)
        {
            if (Mathf.Approximately(wheelDeltaY, 0f))
            {
                return Clamp(current);
            }
            return Clamp(current + (wheelDeltaY < 0f ? ScaleStep : -ScaleStep));
        }

        /// <summary>
        /// 범위 안으로 넣고 10% 단위로 맞춘다(소수 오차가 쌓여 109.99% 가 되지 않게).
        /// </summary>
        public static float Clamp(float scale)
        {
            float rounded = Mathf.Round(scale / ScaleStep) * ScaleStep;
            return Mathf.Clamp(rounded, MinScale, MaxScale);
        }

        public void ZoomIn() => SetScale(_getScale() + ScaleStep);

        public void ZoomOut() => SetScale(_getScale() - ScaleStep);

        public void Reset() => SetScale(1f);

        private void OnWheel(WheelEvent evt)
        {
            if (evt.actionKey == false)
            {
                return;
            }
            SetScale(NextScale(_getScale(), evt.delta.y));
            evt.StopPropagation();
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.actionKey == false)
            {
                return;
            }
            switch (evt.keyCode)
            {
                case KeyCode.Equals:
                case KeyCode.Plus:
                case KeyCode.KeypadPlus:
                    ZoomIn();
                    break;
                case KeyCode.Minus:
                case KeyCode.KeypadMinus:
                    ZoomOut();
                    break;
                case KeyCode.Alpha0:
                case KeyCode.Keypad0:
                    Reset();
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        private void SetScale(float scale)
        {
            float clamped = Clamp(scale);
            _setScale(clamped);
            Apply();
            ShowIndicator(clamped);
        }

        private void Apply()
        {
            float scale = Clamp(_getScale());
            Rect area = _root.contentRect;
            if (float.IsNaN(area.width) || area.width <= 0f)
            {
                return;
            }
            _content.style.width = area.width / scale;
            _content.style.height = area.height / scale;
            _content.style.scale = new Scale(new Vector3(scale, scale, 1f));
        }

        private static void SetRadius(IStyle style, float radius)
        {
            style.borderTopLeftRadius = radius;
            style.borderTopRightRadius = radius;
            style.borderBottomLeftRadius = radius;
            style.borderBottomRightRadius = radius;
        }

        private void ShowIndicator(float scale)
        {
            _indicator.text = $"{Mathf.RoundToInt(scale * 100f)}%";
            _indicator.style.display = DisplayStyle.Flex;
            _indicator.BringToFront();
            _hideIndicator?.Pause();
            _hideIndicator = _indicator.schedule.Execute(() => _indicator.style.display = DisplayStyle.None).StartingIn(IndicatorMs);
        }
    }
}
