using System;
using System.Globalization;
using System.Threading.Tasks;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace GitVersionControl
{
    /// <summary>
    /// 이미지 diff 보기: 나란히(2-up)·스와이프·어니언 스킨·픽셀 차이 네 방식. <see cref="GitDiffPanel"/> 이 이미지 파일일 때 텍스트 목록 대신 보인다.
    /// 모든 방식이 같은 확대·이동 상태를 쓴다 — 휠은 커서 기준 확대, 드래그는 이동, 더블클릭은 맞춤.
    /// 확대는 transform 이 아니라 요소의 위치·크기를 직접 바꿔 그린다(체커보드 칸 크기와 스와이프 경계선 굵기가 배율을 따라 변하지 않게).
    /// </summary>
    public sealed class GitImageDiffView
    {
        private const float MinZoom = 0.02f;
        private const float MaxZoom = 64f;
        private const float MaxFitZoom = 8f;
        private const float WheelStep = 1.15f;
        private const float SwipeHandleWidth = 12f;

        private static Texture2D _checker;

        private readonly ToolbarToggle[] _modeToggles = new ToolbarToggle[4];
        private readonly Slider _opacitySlider;
        private readonly SliderInt _toleranceSlider;
        private readonly Label _zoomLabel;
        private readonly Label _leftInfo;
        private readonly Label _rightInfo;
        private readonly Label _sizeChanged;
        private readonly Label _changedInfo;
        private readonly VisualElement _stage;
        private readonly VisualElement _modeControls;

        private GitImagePair _pair;
        private Color32[] _leftPixels;
        private Color32[] _rightPixels;
        private Texture2D _diffTexture;
        private int _diffVersion;

        private Viewport[] _viewports = Array.Empty<Viewport>();
        private float _zoom = 1f;
        private Vector2 _pan;
        private bool _isFit = true;
        private float _swipe = 0.5f;

        private int _dragPointer = -1;
        private Vector2 _dragStart;
        private Vector2 _panAtDragStart;

        public VisualElement Root { get; }

        /// <summary>
        /// 패널에서 떨어지면서 텍스처를 지웠을 때. 다시 붙으면 <see cref="GitDiffPanel"/> 이 다시 읽는다.
        /// </summary>
        public bool WasCleared { get; private set; }

        public GitImageDiffView()
        {
            Root = new VisualElement();
            Root.AddToClassList("git-image-diff");

            Toolbar toolbar = new();
            toolbar.AddToClassList("git-image-diff__toolbar");
            // 보기 방식·불투명도·허용 오차. 새로 만들거나 지운 이미지(한쪽만 있음)에는 비교할 게 없어 숨긴다.
            _modeControls = new VisualElement();
            _modeControls.AddToClassList("git-image-diff__mode-controls");
            toolbar.Add(_modeControls);
            string[] modeKeys = { "imageDiff.twoUp", "imageDiff.swipe", "imageDiff.onion", "imageDiff.difference" };
            for (int i = 0; i < _modeToggles.Length; i++)
            {
                ImageDiffMode mode = (ImageDiffMode)i;
                ToolbarToggle toggle = new() { text = GitText.Get(modeKeys[i]) };
                toggle.RegisterValueChangedCallback(evt =>
                {
                    if (evt.newValue)
                    {
                        SetMode(mode);
                    }
                    else
                    {
                        // 이미 고른 방식을 다시 눌러도 꺼지지 않게.
                        toggle.SetValueWithoutNotify(true);
                    }
                });
                _modeToggles[i] = toggle;
                _modeControls.Add(toggle);
            }

            _opacitySlider = new Slider(GitText.Get("imageDiff.opacity"), 0f, 1f) { value = 0.5f };
            _opacitySlider.AddToClassList("git-image-diff__slider");
            _opacitySlider.RegisterValueChangedCallback(_ => ApplyTransform());
            _modeControls.Add(_opacitySlider);

            _toleranceSlider = new SliderInt(GitText.Get("imageDiff.tolerance"), 0, 64) { value = 0, showInputField = true, tooltip = GitText.Get("imageDiff.toleranceTooltip") };
            _toleranceSlider.AddToClassList("git-image-diff__slider");
            _toleranceSlider.RegisterValueChangedCallback(_ => ComputeDiff());
            _modeControls.Add(_toleranceSlider);

            VisualElement spacer = new();
            spacer.AddToClassList("spacer");
            toolbar.Add(spacer);

            _zoomLabel = new Label { tooltip = GitText.Get("imageDiff.zoomTooltip") };
            _zoomLabel.AddToClassList("git-image-diff__zoom");
            toolbar.Add(_zoomLabel);
            toolbar.Add(new ToolbarButton(Fit) { text = GitText.Get("imageDiff.fit"), tooltip = GitText.Get("imageDiff.zoomTooltip") });
            toolbar.Add(new ToolbarButton(() => ZoomAt(1f, ViewportCenter())) { text = "100%" });
            Root.Add(toolbar);

            VisualElement info = new();
            info.AddToClassList("git-diff__info");
            _leftInfo = InfoLabel(info);
            _rightInfo = InfoLabel(info);
            _sizeChanged = InfoLabel(info);
            _sizeChanged.AddToClassList("git-image-diff__size-changed");
            _sizeChanged.text = GitText.Get("imageDiff.sizeChanged");
            _changedInfo = InfoLabel(info);
            Root.Add(info);

            _stage = new VisualElement();
            _stage.AddToClassList("git-image-diff__stage");
            Root.Add(_stage);

            Root.RegisterCallback<DetachFromPanelEvent>(_ =>
            {
                if (_pair != null)
                {
                    Clear();
                    WasCleared = true;
                }
            });
            SyncModeControls();
        }

        private static Label InfoLabel(VisualElement parent)
        {
            Label label = new();
            label.AddToClassList("git-image-diff__info");
            parent.Add(label);
            return label;
        }

        /// <summary>
        /// 새 이미지 한 쌍을 보인다. 텍스처는 이 뷰가 가지고, 다음 <see cref="SetImages"/>·<see cref="Clear"/> 에서 지운다.
        /// </summary>
        public void SetImages(GitImagePair pair)
        {
            Clear();
            WasCleared = false;
            _pair = pair;
            _leftPixels = pair.Left.Exists ? pair.Left.Texture.GetPixels32() : null;
            _rightPixels = pair.Right.Exists ? pair.Right.Texture.GetPixels32() : null;
            _isFit = true;

            _modeControls.SetVisible(IsSingle == false);
            if (IsSingle)
            {
                // 새로 만들거나 지운 이미지: 비교 없이 있는 쪽 이미지만 보인다.
                GitImageSide side = pair.Left.Exists ? pair.Left : pair.Right;
                _leftInfo.text = GitText.Format("imageDiff.singleInfo", side.Label, side.Width, side.Height, GitImageDiff.FormatBytes(side.ByteSize));
                _rightInfo.text = string.Empty;
                _sizeChanged.SetVisible(false);
                BuildStage();
                return;
            }
            _leftInfo.text = DescribeSide("imageDiff.before", pair.Left);
            _rightInfo.text = DescribeSide("imageDiff.after", pair.Right);
            _sizeChanged.SetVisible(pair.Left.Width != pair.Right.Width || pair.Left.Height != pair.Right.Height);

            BuildStage();
            ComputeDiff();
        }

        /// <summary>
        /// 한쪽에만 이미지가 있다(새로 만든·지운 파일). 둘 다 없으면 비교 화면 그대로 "없음" 을 보인다.
        /// </summary>
        private bool IsSingle => _pair != null && _pair.Left.Exists != _pair.Right.Exists;

        public void Clear()
        {
            ++_diffVersion;
            _pair?.Dispose();
            _pair = null;
            _leftPixels = null;
            _rightPixels = null;
            if (_diffTexture != null)
            {
                Object.DestroyImmediate(_diffTexture);
                _diffTexture = null;
            }
            _stage.Clear();
            _viewports = Array.Empty<Viewport>();
            _leftInfo.text = string.Empty;
            _rightInfo.text = string.Empty;
            _changedInfo.text = string.Empty;
            _sizeChanged.SetVisible(false);
        }

        private static string DescribeSide(string titleKey, GitImageSide side)
        {
            string title = GitText.Get(titleKey);
            if (side.Exists == false)
            {
                return GitText.Format("imageDiff.sideMissing", title, side.Label);
            }
            return GitText.Format("imageDiff.sideInfo", title, side.Label, side.Width, side.Height, GitImageDiff.FormatBytes(side.ByteSize));
        }

        // ── 보기 방식 ─────────────────────────────

        private static ImageDiffMode Mode => GitVcsSettings.instance.ImageDiffMode;

        private void SetMode(ImageDiffMode mode)
        {
            GitVcsSettings.instance.ImageDiffMode = mode;
            SyncModeControls();
            BuildStage();
        }

        private void SyncModeControls()
        {
            ImageDiffMode mode = Mode;
            for (int i = 0; i < _modeToggles.Length; i++)
            {
                _modeToggles[i].SetValueWithoutNotify((int)mode == i);
            }
            _opacitySlider.SetVisible(mode == ImageDiffMode.Onion);
            _toleranceSlider.SetVisible(mode == ImageDiffMode.Difference);
        }

        private int CanvasWidth => _pair == null ? 0 : Math.Max(_pair.Left.Width, _pair.Right.Width);
        private int CanvasHeight => _pair == null ? 0 : Math.Max(_pair.Left.Height, _pair.Right.Height);

        /// <summary>
        /// 지금 방식에 맞게 뷰포트와 그 안의 이미지 층을 새로 만든다.
        /// </summary>
        private void BuildStage()
        {
            _stage.Clear();
            if (_pair == null)
            {
                _viewports = Array.Empty<Viewport>();
                return;
            }

            if (IsSingle)
            {
                bool added = _pair.Right.Exists;
                GitImageSide side = added ? _pair.Right : _pair.Left;
                Viewport single = new(this, side.Label);
                single.AddImage(side);
                single.SetStatus(GitText.Get(added ? "imageDiff.added" : "imageDiff.deleted"), added ? "git-image-diff--added" : "git-image-diff--deleted");
                _viewports = new[] { single };
                _stage.Add(single.Root);
                if (_isFit)
                {
                    Fit();
                }
                else
                {
                    ApplyTransform();
                }
                return;
            }

            ImageDiffMode mode = Mode;
            string before = GitText.Get("imageDiff.before");
            string after = GitText.Get("imageDiff.after");
            switch (mode)
            {
                case ImageDiffMode.TwoUp:
                {
                    Viewport left = new(this, $"{before} · {_pair.Left.Label}");
                    left.AddImage(_pair.Left);
                    Viewport right = new(this, $"{after} · {_pair.Right.Label}");
                    right.AddImage(_pair.Right);
                    left.Root.AddToClassList("git-image-diff__viewport--left");
                    _viewports = new[] { left, right };
                    break;
                }
                case ImageDiffMode.Swipe:
                {
                    // 경계선 왼쪽은 이전만, 오른쪽은 이후만 보인다. 두 층을 각각 잘라 겹치지 않게 한다 —
                    // 한 층을 통째로 깔면 위층의 투명한 곳으로 아래층이 비친다.
                    Viewport viewport = new(this, $"{before} · {_pair.Left.Label}  |  {after} · {_pair.Right.Label}");
                    viewport.AddSwipeImages(_pair.Left, _pair.Right);
                    _viewports = new[] { viewport };
                    break;
                }
                case ImageDiffMode.Onion:
                {
                    Viewport viewport = new(this, $"{before} · {_pair.Left.Label}  →  {after} · {_pair.Right.Label}");
                    viewport.AddImage(_pair.Left);
                    viewport.AddOnionImage(_pair.Right);
                    _viewports = new[] { viewport };
                    break;
                }
                case ImageDiffMode.Difference:
                {
                    Viewport viewport = new(this, GitText.Get("imageDiff.difference"));
                    viewport.AddDifferenceImage();
                    _viewports = new[] { viewport };
                    break;
                }
            }
            foreach (Viewport viewport in _viewports)
            {
                _stage.Add(viewport.Root);
            }
            UpdateDifferenceImage();
            if (_isFit)
            {
                Fit();
            }
            else
            {
                ApplyTransform();
            }
        }

        // ── 픽셀 차이 ─────────────────────────────

        /// <summary>
        /// 차이는 무거울 수 있어(큰 텍스처) 백그라운드에서 계산한다. 그 사이 다른 파일을 열거나 허용 오차를 바꾸면 늦게 온 결과는 버린다.
        /// </summary>
        private async void ComputeDiff()
        {
            if (_pair == null)
            {
                return;
            }
            int version = ++_diffVersion;
            Color32[] left = _leftPixels;
            Color32[] right = _rightPixels;
            int leftWidth = _pair.Left.Width;
            int leftHeight = _pair.Left.Height;
            int rightWidth = _pair.Right.Width;
            int rightHeight = _pair.Right.Height;
            int tolerance = _toleranceSlider.value;
            _changedInfo.text = GitText.Get("imageDiff.computing");

            GitImageDiffResult result = await Task.Run(() => GitImageDiff.Compare(left, leftWidth, leftHeight, right, rightWidth, rightHeight, tolerance));
            if (version != _diffVersion)
            {
                return;
            }

            _changedInfo.text = result.ChangedPixels == 0
                ? GitText.Get("imageDiff.identical")
                : GitText.Format("imageDiff.changed", result.ChangedPixels.ToString("N0", CultureInfo.InvariantCulture), result.TotalPixels.ToString("N0", CultureInfo.InvariantCulture), (result.ChangedRatio * 100f).ToString("0.##", CultureInfo.InvariantCulture));
            _changedInfo.EnableInClassList("git-image-diff__info--changed", result.ChangedPixels > 0);

            if (result.Width > 0 && result.Height > 0)
            {
                if (_diffTexture == null || _diffTexture.width != result.Width || _diffTexture.height != result.Height)
                {
                    if (_diffTexture != null)
                    {
                        Object.DestroyImmediate(_diffTexture);
                    }
                    _diffTexture = new Texture2D(result.Width, result.Height, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Clamp,
                    };
                }
                _diffTexture.SetPixels32(result.Mask);
                _diffTexture.Apply(false, false);
            }
            UpdateDifferenceImage();
        }

        private void UpdateDifferenceImage()
        {
            foreach (Viewport viewport in _viewports)
            {
                if (viewport.DifferenceImage != null)
                {
                    viewport.DifferenceImage.image = _diffTexture;
                }
            }
        }

        // ── 확대·이동 ─────────────────────────────

        private Vector2 ViewportSize()
        {
            if (_viewports.Length == 0)
            {
                return Vector2.zero;
            }
            Rect rect = _viewports[0].Root.contentRect;
            return float.IsNaN(rect.width) || float.IsNaN(rect.height) ? Vector2.zero : rect.size;
        }

        private Vector2 ViewportCenter()
        {
            return ViewportSize() * 0.5f;
        }

        /// <summary>
        /// 뷰포트에 다 들어오게 맞추고 가운데에 둔다. 작은 이미지(아이콘 등)는 <see cref="MaxFitZoom"/> 배까지 키운다.
        /// </summary>
        private void Fit()
        {
            Vector2 size = ViewportSize();
            int width = CanvasWidth;
            int height = CanvasHeight;
            _isFit = true;
            if (size.x <= 0 || size.y <= 0 || width == 0 || height == 0)
            {
                ApplyTransform();
                return;
            }
            const float margin = 16f;
            float zoom = Mathf.Min((size.x - margin) / width, (size.y - margin) / height);
            _zoom = Mathf.Clamp(zoom, MinZoom, MaxFitZoom);
            _pan = (size - new Vector2(width, height) * _zoom) * 0.5f;
            ApplyTransform();
        }

        /// <summary>
        /// 뷰포트의 한 점(<paramref name="anchor"/>)이 가리키는 이미지 위치를 그대로 두고 배율을 바꾼다.
        /// </summary>
        private void ZoomAt(float zoom, Vector2 anchor)
        {
            zoom = Mathf.Clamp(zoom, MinZoom, MaxZoom);
            Vector2 content = (anchor - _pan) / _zoom;
            _zoom = zoom;
            _pan = anchor - content * _zoom;
            _isFit = false;
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            _zoomLabel.text = Mathf.RoundToInt(_zoom * 100f).ToString(CultureInfo.InvariantCulture) + "%";
            foreach (Viewport viewport in _viewports)
            {
                viewport.Layout();
            }
        }

        /// <summary>
        /// 뷰포트가 처음 배치되거나 창 크기가 바뀌었을 때. 맞춤 상태면 새 크기에 다시 맞춘다.
        /// </summary>
        private void OnViewportResized()
        {
            if (_isFit)
            {
                Fit();
            }
            else
            {
                ApplyTransform();
            }
        }

        private void OnWheel(Viewport viewport, WheelEvent evt)
        {
            Vector2 anchor = viewport.Root.WorldToLocal(evt.mousePosition);
            float factor = evt.delta.y > 0 ? 1f / WheelStep : WheelStep;
            ZoomAt(_zoom * factor, anchor);
            evt.StopPropagation();
        }

        private void OnPointerDown(Viewport viewport, PointerDownEvent evt)
        {
            if (evt.button == 0 && evt.clickCount == 2)
            {
                Fit();
                return;
            }
            if (evt.button != 0 && evt.button != 2)
            {
                return;
            }
            _dragPointer = evt.pointerId;
            _dragStart = viewport.Root.WorldToLocal(evt.position);
            _panAtDragStart = _pan;
            viewport.Root.CapturePointer(evt.pointerId);
            evt.StopPropagation();
        }

        private void OnPointerMove(Viewport viewport, PointerMoveEvent evt)
        {
            if (evt.pointerId != _dragPointer || viewport.Root.HasPointerCapture(evt.pointerId) == false)
            {
                return;
            }
            _pan = _panAtDragStart + ((Vector2)viewport.Root.WorldToLocal(evt.position) - _dragStart);
            _isFit = false;
            ApplyTransform();
        }

        private void OnPointerUp(Viewport viewport, PointerUpEvent evt)
        {
            if (evt.pointerId != _dragPointer)
            {
                return;
            }
            _dragPointer = -1;
            viewport.Root.ReleasePointer(evt.pointerId);
        }

        private void MoveSwipe(Viewport viewport, Vector2 panelPosition)
        {
            float canvas = CanvasWidth * _zoom;
            if (canvas <= 0)
            {
                return;
            }
            float x = viewport.Root.WorldToLocal(panelPosition).x;
            _swipe = Mathf.Clamp01((x - _pan.x) / canvas);
            ApplyTransform();
        }

        private static Texture2D Checker
        {
            get
            {
                if (_checker == null)
                {
                    Color32 light = new(204, 204, 204, 255);
                    Color32 dark = new(153, 153, 153, 255);
                    _checker = new Texture2D(2, 2, TextureFormat.RGBA32, false)
                    {
                        hideFlags = HideFlags.HideAndDontSave,
                        filterMode = FilterMode.Point,
                        wrapMode = TextureWrapMode.Repeat,
                    };
                    _checker.SetPixels32(new[] { light, dark, dark, light });
                    _checker.Apply(false, true);
                }
                return _checker;
            }
        }

        /// <summary>
        /// 이미지를 담는 칸 하나. 2-up 은 둘, 나머지 방식은 하나다. 층마다 이미지 원래 크기 × 배율로 위치·크기를 정한다.
        /// </summary>
        private sealed class Viewport
        {
            public readonly VisualElement Root;
            public Image DifferenceImage;

            private readonly GitImageDiffView _owner;
            private readonly VisualElement _backdrop;
            private readonly VisualElement _content;
            private readonly Label _placeholder;
            private Image _bottom;
            private GitImageSide _bottomSide;
            private VisualElement _swipeClip;
            private Image _swipeImage;
            private GitImageSide _swipeSide;
            private VisualElement _swipeRightClip;
            private Image _swipeRightImage;
            private GitImageSide _swipeRightSide;
            private VisualElement _swipeHandle;
            private Image _onionImage;
            private GitImageSide _onionSide;

            public Viewport(GitImageDiffView owner, string caption)
            {
                _owner = owner;
                Root = new VisualElement();
                Root.AddToClassList("git-image-diff__viewport");

                _backdrop = Layer();
                _backdrop.AddToClassList("git-image-diff__checker");
                _backdrop.style.backgroundImage = Checker;
                Root.Add(_backdrop);

                _content = Layer();
                Root.Add(_content);

                Label label = new(caption);
                label.AddToClassList("git-image-diff__caption");
                label.pickingMode = PickingMode.Ignore;
                Root.Add(label);

                _placeholder = new Label(GitText.Get("imageDiff.none"));
                _placeholder.AddToClassList("git-image-diff__placeholder");
                _placeholder.pickingMode = PickingMode.Ignore;
                _placeholder.SetVisible(false);
                Root.Add(_placeholder);

                Root.RegisterCallback<GeometryChangedEvent>(_ => _owner.OnViewportResized());
                Root.RegisterCallback<WheelEvent>(evt => _owner.OnWheel(this, evt));
                Root.RegisterCallback<PointerDownEvent>(evt => _owner.OnPointerDown(this, evt));
                Root.RegisterCallback<PointerMoveEvent>(evt => _owner.OnPointerMove(this, evt));
                Root.RegisterCallback<PointerUpEvent>(evt => _owner.OnPointerUp(this, evt));
            }

            private static VisualElement Layer()
            {
                VisualElement layer = new();
                layer.AddToClassList("git-image-diff__layer");
                layer.pickingMode = PickingMode.Ignore;
                return layer;
            }

            private static Image ImageOf(GitImageSide side)
            {
                Image image = new() { image = side?.Texture, scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
                image.AddToClassList("git-image-diff__layer");
                return image;
            }

            public void AddImage(GitImageSide side)
            {
                _bottomSide = side;
                _bottom = ImageOf(side);
                _content.Add(_bottom);
                _placeholder.SetVisible(side.Exists == false);
            }

            /// <summary>
            /// 새로 만든·지운 이미지: 위 가운데에 상태 글자(추가됨·삭제됨)를 띄우고 이미지 둘레에 같은 색 테두리를 두른다.
            /// </summary>
            public void SetStatus(string text, string modifier)
            {
                Root.AddToClassList(modifier);
                _backdrop.AddToClassList("git-image-diff__frame");
                Label status = new(text) { pickingMode = PickingMode.Ignore };
                status.AddToClassList("git-image-diff__status");
                Root.Add(status);
            }

            public void AddSwipeImages(GitImageSide left, GitImageSide right)
            {
                _swipeSide = left;
                _swipeClip = Layer();
                _swipeClip.AddToClassList("git-image-diff__clip");
                _swipeImage = ImageOf(left);
                _swipeClip.Add(_swipeImage);
                _content.Add(_swipeClip);

                _swipeRightSide = right;
                _swipeRightClip = Layer();
                _swipeRightClip.AddToClassList("git-image-diff__clip");
                _swipeRightImage = ImageOf(right);
                _swipeRightClip.Add(_swipeRightImage);
                _content.Add(_swipeRightClip);
                _placeholder.SetVisible(left.Exists == false || right.Exists == false);

                // 경계선은 배율과 상관없이 같은 굵기로 보이게 뷰포트 좌표에 둔다. 넓은 투명 칸이 잡기 쉬운 손잡이 역할을 한다.
                _swipeHandle = new VisualElement();
                _swipeHandle.AddToClassList("git-image-diff__swipe-handle");
                _swipeHandle.style.width = SwipeHandleWidth;
                VisualElement line = new() { pickingMode = PickingMode.Ignore };
                line.AddToClassList("git-image-diff__swipe-line");
                _swipeHandle.Add(line);
                _swipeHandle.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.button != 0)
                    {
                        return;
                    }
                    _swipeHandle.CapturePointer(evt.pointerId);
                    evt.StopPropagation();
                });
                _swipeHandle.RegisterCallback<PointerMoveEvent>(evt =>
                {
                    if (_swipeHandle.HasPointerCapture(evt.pointerId))
                    {
                        _owner.MoveSwipe(this, evt.position);
                        evt.StopPropagation();
                    }
                });
                _swipeHandle.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if (_swipeHandle.HasPointerCapture(evt.pointerId))
                    {
                        _swipeHandle.ReleasePointer(evt.pointerId);
                        evt.StopPropagation();
                    }
                });
                Root.Add(_swipeHandle);
            }

            public void AddOnionImage(GitImageSide side)
            {
                _onionSide = side;
                _onionImage = ImageOf(side);
                _content.Add(_onionImage);
            }

            public void AddDifferenceImage()
            {
                DifferenceImage = ImageOf(null);
                _content.Add(DifferenceImage);
            }

            public void Layout()
            {
                float zoom = _owner._zoom;
                Vector2 pan = _owner._pan;
                float width = _owner.CanvasWidth * zoom;
                float height = _owner.CanvasHeight * zoom;

                Place(_backdrop, pan.x, pan.y, width, height);
                Place(_content, pan.x, pan.y, width, height);
                Size(_bottom, _bottomSide, zoom);
                Size(_onionImage, _onionSide, zoom);
                if (_onionImage != null)
                {
                    // 이전은 반대로 옅어진다(교차 페이드) — 100% 면 이후만, 0% 면 이전만 보인다.
                    // 이전을 늘 그대로 두면 이후의 투명한 곳으로 이전이 비친다.
                    float opacity = _owner._opacitySlider.value;
                    _onionImage.style.opacity = opacity;
                    if (_bottom != null)
                    {
                        _bottom.style.opacity = 1f - opacity;
                    }
                }
                if (DifferenceImage != null)
                {
                    Place(DifferenceImage, 0, 0, width, height);
                }
                if (_swipeClip != null)
                {
                    float split = width * _owner._swipe;
                    Place(_swipeClip, 0, 0, split, height);
                    Size(_swipeImage, _swipeSide, zoom);
                    // 오른쪽 층은 경계선에서 시작하고, 안의 이미지를 그만큼 왼쪽으로 당겨 캔버스 위치를 맞춘다.
                    Place(_swipeRightClip, split, 0, Mathf.Max(0f, width - split), height);
                    Place(_swipeRightImage, -split, 0, (_swipeRightSide?.Width ?? 0) * zoom, (_swipeRightSide?.Height ?? 0) * zoom);
                    _swipeHandle.style.left = pan.x + split - SwipeHandleWidth * 0.5f;
                }
            }

            private static void Size(VisualElement element, GitImageSide side, float zoom)
            {
                if (element != null)
                {
                    Place(element, 0, 0, (side?.Width ?? 0) * zoom, (side?.Height ?? 0) * zoom);
                }
            }

            private static void Place(VisualElement element, float left, float top, float width, float height)
            {
                element.style.left = left;
                element.style.top = top;
                element.style.width = width;
                element.style.height = height;
            }
        }
    }
}
