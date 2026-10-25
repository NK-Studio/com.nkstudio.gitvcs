using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// diff 의 "이 파일 편집"(GitKraken 의 editable File View)이 쓰는 코드 편집기.
    /// 줄 번호 칸, 지금 줄 강조, 깜빡이는 커서, 들여쓰기 안내선, 간단한 문법 색칠을 그리고 글자 치기·고르기·복사·붙여 넣기·되돌리기를 받는다.
    /// 수천 줄 파일에서도 타자가 느려지지 않게 화면에 보이는 줄만 라벨로 그린다(나머지는 스크롤 높이만 차지한다).
    /// 글과 되돌리기는 <see cref="GitTextBuffer"/>, 색칠은 <see cref="GitSyntax"/> 가 맡는다.
    /// </summary>
    public sealed class GitCodeEditor : VisualElement
    {
        private const float LineHeight = 18f;

        /// <summary>
        /// 줄 번호 칸과 글 사이 여백(글 칸 안쪽 왼쪽).
        /// </summary>
        private const float TextPadding = 10f;

        /// <summary>
        /// 화면에서 탭 하나가 차지하는 칸 수(diff 의 탭 폭과 같다).
        /// </summary>
        private const int TabSize = 4;

        private const long BlinkMs = 530;

        /// <summary>
        /// IME 조합 글(한글 조합 중인 글자)을 읽는 간격.
        /// </summary>
        private const long CompositionPollMs = 30;

        /// <summary>
        /// 같은 단축키가 KeyDown 과 에디터 명령(Copy·Paste 등)으로 두 번 올 때 두 번째를 버리는 시간.
        /// </summary>
        private const double DuplicateCommandSeconds = 0.25;

        private readonly GitTextBuffer _buffer = new();
        private GitSyntaxLanguage _language;

        /// <summary>
        /// 줄 i 시작의 색칠 상태(블록 주석 안인지). 앞에서부터 <see cref="_validStates"/> 개만 맞다.
        /// </summary>
        private readonly List<int> _lineStates = new() { GitSyntax.StateNormal };
        private int _validStates = 1;
        private readonly List<GitSyntaxSpan> _spans = new();
        private readonly List<GitSyntaxSpan> _stateSpans = new();

        private GitTextPosition _anchor;
        private GitTextPosition _caret;

        /// <summary>
        /// ↑↓ 로 움직일 때 지킬 가로 자리(짧은 줄을 지나도 원래 자리로 돌아온다). 음수면 지금 커서 자리.
        /// </summary>
        private float _preferredX = -1f;

        private readonly VisualElement _gutter;
        private readonly ScrollView _scroll;
        private readonly VisualElement _content;
        private readonly VisualElement _decorations;
        private readonly VisualElement _linesLayer;
        private readonly VisualElement _caretElement;
        private readonly Label _composition;
        private readonly Label _measure;
        private readonly List<Label> _linePool = new();
        private readonly List<Label> _numberPool = new();

        /// <summary>
        /// 작업 사본에서 바뀐 줄(1부터, 마지막으로 읽은 diff 기준). 줄 번호 칸에 초록 띠로 보인다.
        /// </summary>
        private readonly HashSet<int> _changedLines = new();

        private readonly Dictionary<char, float> _charWidths = new();
        private float _asciiWidth;
        private float _measuredFontSize = -1f;
        private float _maxLineWidth;
        private int _indentSize = 4;
        private bool _useTabs;

        private IVisualElementScheduledItem _blink;
        private IVisualElementScheduledItem _compositionPoll;
        private string _compositionText = string.Empty;
        private bool _dragging;
        private bool _hasFocus;
        private bool _caretOn = true;

        /// <summary>
        /// KeyDown(키 코드)과 뒤따르는 글자 KeyDown 이 따로 오는 Enter·Tab 을 한 번만 처리하려고, 앞의 것을 처리했으면 뒤의 글자를 기다린다.
        /// </summary>
        private char _swallowCharacter;
        private string _lastShortcut;
        private double _lastShortcutTime;

        private Color _keywordColor = new(0.81f, 0.56f, 0.43f);
        private Color _stringColor = new(0.42f, 0.67f, 0.45f);
        private Color _numberColor = new(0.16f, 0.67f, 0.72f);
        private Color _commentColor = new(0.48f, 0.49f, 0.52f);
        private Color _keyColor = new(0.78f, 0.49f, 0.73f);
        private Color _currentLineColor = new(1f, 1f, 1f, 0.04f);
        private Color _indentGuideColor = new(1f, 1f, 1f, 0.08f);
        private Color _selectionColor = new(0.13f, 0.26f, 0.51f, 0.95f);
        private readonly string[] _kindColors = new string[6];

        private static readonly CustomStyleProperty<Color> KeywordProperty = new("--code-keyword");
        private static readonly CustomStyleProperty<Color> StringProperty = new("--code-string");
        private static readonly CustomStyleProperty<Color> NumberProperty = new("--code-number");
        private static readonly CustomStyleProperty<Color> CommentProperty = new("--code-comment");
        private static readonly CustomStyleProperty<Color> KeyProperty = new("--code-key");
        private static readonly CustomStyleProperty<Color> CurrentLineProperty = new("--editor-current-line");
        private static readonly CustomStyleProperty<Color> IndentGuideProperty = new("--editor-indent-guide");
        private static readonly CustomStyleProperty<Color> SelectionProperty = new("--editor-selection");

        /// <summary>
        /// Ctrl+S(맥은 Cmd+S).
        /// </summary>
        public event Action SaveRequested;

        /// <summary>
        /// Esc.
        /// </summary>
        public event Action CancelRequested;

        /// <summary>
        /// 글이 바뀌었을 때(되돌리기 포함). 저장 안 된 표시를 맞춘다.
        /// </summary>
        public event Action Changed;

        /// <summary>
        /// 오른쪽 버튼 메뉴 끝에 더할 항목(외부 편집기에서 열기 등).
        /// </summary>
        public Action<DropdownMenu> ExtraMenuItems { get; set; }

        public bool IsDirty => _buffer.IsDirty;

        public string Text => _buffer.GetText();

        public GitTextPosition Caret => _caret;

        public GitCodeEditor()
        {
            AddToClassList("git-code-editor");
            focusable = true;
            delegatesFocus = false;
            Font monospace = GitFonts.Monospace;
            if (monospace != null)
            {
                style.unityFontDefinition = FontDefinition.FromFont(monospace);
            }

            _gutter = new VisualElement { pickingMode = PickingMode.Ignore };
            _gutter.AddToClassList("git-code-editor__gutter");
            Add(_gutter);

            _scroll = new ScrollView(ScrollViewMode.VerticalAndHorizontal)
            {
                horizontalScrollerVisibility = ScrollerVisibility.Auto,
                verticalScrollerVisibility = ScrollerVisibility.Auto,
                touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped,
                mouseWheelScrollSize = LineHeight * 3,
            };
            _scroll.AddToClassList("git-code-editor__scroll");
            Add(_scroll);

            _content = new VisualElement();
            _content.AddToClassList("git-code-editor__content");
            _scroll.Add(_content);

            _decorations = new VisualElement { pickingMode = PickingMode.Ignore };
            _decorations.AddToClassList("git-code-editor__layer");
            _decorations.generateVisualContent += DrawDecorations;
            _content.Add(_decorations);

            _linesLayer = new VisualElement { pickingMode = PickingMode.Ignore };
            _linesLayer.AddToClassList("git-code-editor__layer");
            _content.Add(_linesLayer);

            _measure = NewLineLabel();
            _measure.style.visibility = Visibility.Hidden;
            _linesLayer.Add(_measure);

            _caretElement = new VisualElement { pickingMode = PickingMode.Ignore };
            _caretElement.AddToClassList("git-code-editor__caret");
            _content.Add(_caretElement);

            _composition = new Label { pickingMode = PickingMode.Ignore, enableRichText = false };
            _composition.AddToClassList("git-code-editor__composition");
            _composition.style.display = DisplayStyle.None;
            _content.Add(_composition);

            _scroll.verticalScroller.valueChanged += _ => Refresh();
            _scroll.contentViewport.RegisterCallback<GeometryChangedEvent>(_ =>
            {
                UpdateMetrics();
                Refresh();
            });
            _scroll.contentViewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            _scroll.contentViewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            _scroll.contentViewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            _scroll.contentViewport.RegisterCallback<PointerCaptureOutEvent>(_ => _dragging = false);

            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<NavigationMoveEvent>(OnNavigationMove, TrickleDown.TrickleDown);
            RegisterCallback<ValidateCommandEvent>(OnValidateCommand);
            RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand);
            RegisterCallback<FocusInEvent>(_ => OnFocusChanged(true));
            RegisterCallback<FocusOutEvent>(_ => OnFocusChanged(false));
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            RegisterCallback<DetachFromPanelEvent>(_ => OnFocusChanged(false));
            this.AddManipulator(new ContextualMenuManipulator(OnContextMenu));
            UpdateKindColors();
        }

        // ── 글 넣기·저장 표시 ─────────────────────

        /// <summary>
        /// 새 글을 보인다. 되돌리기 기록은 비우고 저장한 상태로 본다.
        /// </summary>
        /// <param name="caretLine">커서를 둘 줄(1부터). 그 줄이 화면 위쪽에 오게 굴린다.</param>
        public void Load(string text, GitSyntaxLanguage language, int caretLine)
        {
            _language = language;
            _buffer.SetText(text);
            InvalidateStates(0);
            DetectIndentation();
            _caret = _anchor = _buffer.Clamp(new GitTextPosition(Math.Max(0, caretLine - 1), 0));
            _preferredX = -1f;
            RecomputeMaxWidth();
            UpdateMetrics();
            // 줄 위치를 셀 수 있게 된 뒤(레이아웃 뒤) 커서 줄을 위에서 몇 줄 아래에 둔다.
            schedule.Execute(() =>
            {
                _scroll.scrollOffset = new Vector2(0, Mathf.Max(0, (_caret.Line - 3) * LineHeight));
                Refresh();
            });
            Refresh();
        }

        /// <summary>
        /// 지금 글을 저장한 판으로 삼는다(저장 안 된 표시가 꺼진다).
        /// </summary>
        public void MarkSaved()
        {
            _buffer.MarkSaved();
            Changed?.Invoke();
        }

        /// <summary>
        /// 작업 사본에서 바뀐 줄(1부터)을 줄 번호 칸에 표시한다.
        /// </summary>
        public void SetChangedLines(IEnumerable<int> lineNumbers)
        {
            _changedLines.Clear();
            if (lineNumbers != null)
            {
                _changedLines.UnionWith(lineNumbers);
            }
            Refresh();
        }

        public void FocusEditor()
        {
            Focus();
        }

        // ── 줄 폭·글자 자리 ────────────────────────

        private static Label NewLineLabel()
        {
            Label label = new() { pickingMode = PickingMode.Ignore, enableRichText = true };
            label.AddToClassList("git-code-editor__line");
            return label;
        }

        /// <summary>
        /// 글자 폭을 잰 글꼴 크기가 바뀌었으면(테마·UI 배율) 폭을 다시 잰다.
        /// </summary>
        private bool EnsureMeasured()
        {
            float fontSize = _measure.resolvedStyle.fontSize;
            if (float.IsNaN(fontSize) || fontSize <= 0 || _measure.panel == null)
            {
                return _asciiWidth > 0;
            }
            if (Mathf.Approximately(fontSize, _measuredFontSize) && _asciiWidth > 0)
            {
                return true;
            }
            _charWidths.Clear();
            float width = Measure('M');
            if (width <= 0)
            {
                return _asciiWidth > 0;
            }
            _asciiWidth = width;
            _measuredFontSize = fontSize;
            RecomputeMaxWidth();
            return true;
        }

        private float Measure(char c)
        {
            const int repeat = 10;
            Vector2 size = _measure.MeasureTextSize(new string(c, repeat), 0, MeasureMode.Undefined, 0, MeasureMode.Undefined);
            return size.x / repeat;
        }

        /// <summary>
        /// 글자 하나의 폭. 고정폭 글꼴이라 ASCII 는 모두 같고, 한글 등은 처음 볼 때 한 번 잰다.
        /// </summary>
        private float CharWidth(char c)
        {
            if (c >= 32 && c < 127)
            {
                return _asciiWidth;
            }
            if (char.IsSurrogate(c) || c < 32)
            {
                return char.IsLowSurrogate(c) ? 0f : _asciiWidth;
            }
            if (_charWidths.TryGetValue(c, out float width) == false)
            {
                width = _measure.panel != null ? Measure(c) : _asciiWidth;
                if (width <= 0)
                {
                    width = _asciiWidth;
                }
                _charWidths[c] = width;
            }
            return width;
        }

        private float TabWidth => TabSize * _asciiWidth;

        /// <summary>
        /// 자리 x 다음의 탭 칸 자리.
        /// </summary>
        private float NextTabStop(float x)
        {
            float tab = TabWidth;
            return tab <= 0 ? x : (Mathf.Floor(x / tab + 0.001f) + 1) * tab;
        }

        /// <summary>
        /// 줄 <paramref name="line"/> 의 글자 위치 <paramref name="column"/> 앞까지의 폭(글 칸 왼쪽 여백 빼고).
        /// </summary>
        private float XOf(int line, int column)
        {
            string text = _buffer[line];
            float x = 0;
            int end = Math.Min(column, text.Length);
            for (int i = 0; i < end; i++)
            {
                x = text[i] == '\t' ? NextTabStop(x) : x + CharWidth(text[i]);
            }
            return x;
        }

        /// <summary>
        /// 줄 <paramref name="line"/> 에서 가로 자리 <paramref name="x"/> 에 가장 가까운 글자 경계.
        /// </summary>
        private int ColumnAt(int line, float x)
        {
            string text = _buffer[line];
            float left = 0;
            for (int i = 0; i < text.Length; i++)
            {
                float right = text[i] == '\t' ? NextTabStop(left) : left + CharWidth(text[i]);
                if (x < (left + right) * 0.5f)
                {
                    return i;
                }
                left = right;
            }
            return text.Length;
        }

        private void RecomputeMaxWidth()
        {
            _maxLineWidth = 0;
            if (_asciiWidth <= 0)
            {
                return;
            }
            for (int line = 0; line < _buffer.LineCount; line++)
            {
                _maxLineWidth = Math.Max(_maxLineWidth, XOf(line, int.MaxValue));
            }
        }

        /// <summary>
        /// 들여쓰기 단위를 고른다: 탭으로 들여 쓴 줄이 많으면 탭, 아니면 공백 2칸 또는 4칸(2칸 단위로만 들여 쓴 줄이 있으면 2).
        /// </summary>
        private void DetectIndentation()
        {
            int tabLines = 0;
            int spaceLines = 0;
            bool twoSpaces = false;
            int limit = Math.Min(_buffer.LineCount, 2000);
            for (int line = 0; line < limit; line++)
            {
                string text = _buffer[line];
                if (text.Length == 0)
                {
                    continue;
                }
                if (text[0] == '\t')
                {
                    tabLines++;
                    continue;
                }
                int spaces = 0;
                while (spaces < text.Length && text[spaces] == ' ')
                {
                    spaces++;
                }
                if (spaces == 0 || spaces == text.Length)
                {
                    continue;
                }
                spaceLines++;
                twoSpaces |= spaces % 4 == 2;
            }
            _useTabs = tabLines > spaceLines;
            _indentSize = _useTabs ? TabSize : twoSpaces ? 2 : 4;
        }

        /// <summary>
        /// 줄 앞 공백의 폭. 공백뿐인 줄은 -1.
        /// </summary>
        private float IndentWidth(int line)
        {
            string text = _buffer[line];
            int first = 0;
            while (first < text.Length && (text[first] == ' ' || text[first] == '\t'))
            {
                first++;
            }
            return first == text.Length ? -1f : XOf(line, first);
        }

        /// <summary>
        /// 스크롤 내용 크기·줄 번호 칸 폭을 지금 글에 맞춘다.
        /// </summary>
        private void UpdateMetrics()
        {
            if (EnsureMeasured() == false)
            {
                return;
            }
            Rect view = _scroll.contentViewport.layout;
            float viewHeight = float.IsNaN(view.height) ? 0 : view.height;
            float viewWidth = float.IsNaN(view.width) ? 0 : view.width;
            // 편집기처럼 마지막 줄 아래로 반 화면 더 굴릴 수 있게 한다.
            _content.style.height = _buffer.LineCount * LineHeight + Mathf.Max(0, viewHeight * 0.5f);
            _content.style.width = Mathf.Max(viewWidth, _maxLineWidth + TextPadding * 2 + _asciiWidth * 2);
            int digits = Math.Max(3, _buffer.LineCount.ToString().Length);
            _gutter.style.width = digits * _asciiWidth + 22f;
        }

        // ── 색칠 ────────────────────────────────

        private void InvalidateStates(int fromLine)
        {
            _validStates = Math.Max(1, Math.Min(_validStates, fromLine + 1));
            if (_lineStates.Count > _validStates)
            {
                _lineStates.RemoveRange(_validStates, _lineStates.Count - _validStates);
            }
        }

        private int StateAt(int line)
        {
            if (_language == GitSyntaxLanguage.None)
            {
                return GitSyntax.StateNormal;
            }
            while (_validStates <= line)
            {
                // 그리는 줄의 조각(_spans)과 섞이지 않게 상태만 셀 때는 따로 쓴다.
                _stateSpans.Clear();
                int state = GitSyntax.Tokenize(_buffer[_validStates - 1], _language, _lineStates[_validStates - 1], _stateSpans);
                _lineStates.Add(state);
                _validStates++;
            }
            return _lineStates[line];
        }

        private void UpdateKindColors()
        {
            _kindColors[(int)GitSyntaxKind.Keyword] = Hex(_keywordColor);
            _kindColors[(int)GitSyntaxKind.String] = Hex(_stringColor);
            _kindColors[(int)GitSyntaxKind.Number] = Hex(_numberColor);
            _kindColors[(int)GitSyntaxKind.Comment] = Hex(_commentColor);
            _kindColors[(int)GitSyntaxKind.Key] = Hex(_keyColor);
        }

        private static string Hex(Color color)
        {
            return "#" + ColorUtility.ToHtmlStringRGBA(color);
        }

        /// <summary>
        /// 화면에 그릴 줄 글: 탭을 다음 탭 칸까지 공백으로 펼치고, 색칠 조각은 &lt;color&gt; 로 감싼다.
        /// &lt; 가 든 줄은 태그로 읽히지 않게 글 조각을 &lt;noparse&gt; 로 감싼다.
        /// </summary>
        private string RenderLine(int line)
        {
            string text = _buffer[line];
            if (text.Length == 0)
            {
                return string.Empty;
            }
            _spans.Clear();
            if (_language != GitSyntaxLanguage.None)
            {
                GitSyntax.Tokenize(text, _language, StateAt(line), _spans);
            }
            bool escape = text.IndexOf('<') >= 0;
            StringBuilder builder = new(text.Length + _spans.Count * 24 + 16);
            float x = 0;
            int position = 0;
            foreach (GitSyntaxSpan span in _spans)
            {
                AppendPlain(builder, text, position, span.Start, escape, ref x);
                builder.Append("<color=").Append(_kindColors[(int)span.Kind]).Append('>');
                AppendPlain(builder, text, span.Start, span.Start + span.Length, escape, ref x);
                builder.Append("</color>");
                position = span.Start + span.Length;
            }
            AppendPlain(builder, text, position, text.Length, escape, ref x);
            return builder.ToString();
        }

        private void AppendPlain(StringBuilder builder, string text, int from, int to, bool escape, ref float x)
        {
            if (to <= from)
            {
                return;
            }
            if (escape)
            {
                builder.Append("<noparse>");
            }
            int start = builder.Length;
            for (int i = from; i < to; i++)
            {
                char c = text[i];
                if (c == '\t')
                {
                    float stop = NextTabStop(x);
                    int spaces = Math.Max(1, Mathf.RoundToInt((stop - x) / Math.Max(0.01f, _asciiWidth)));
                    builder.Append(' ', spaces);
                    x = stop;
                    continue;
                }
                builder.Append(c);
                x += CharWidth(c);
            }
            if (escape)
            {
                // 코드에 </noparse> 가 그대로 있으면 감싼 구간이 거기서 끝난다. 사이에 폭 없는 공백을 넣어 끊는다.
                builder.Replace("</noparse>", "</no​parse>", start, builder.Length - start);
                builder.Append("</noparse>");
            }
        }

        // ── 그리기 ──────────────────────────────

        private (int first, int last) VisibleLines()
        {
            float top = _scroll.scrollOffset.y;
            float height = _scroll.contentViewport.layout.height;
            if (float.IsNaN(height) || height <= 0)
            {
                height = 600;
            }
            int first = Math.Max(0, Mathf.FloorToInt(top / LineHeight));
            int last = Math.Min(_buffer.LineCount - 1, Mathf.FloorToInt((top + height) / LineHeight));
            return (first, last);
        }

        /// <summary>
        /// 보이는 줄의 라벨·줄 번호·커서·장식을 다시 맞춘다. 보이는 줄만 다루므로 타자마다 불러도 가볍다.
        /// </summary>
        private void Refresh()
        {
            if (EnsureMeasured() == false)
            {
                return;
            }
            (int first, int last) = VisibleLines();
            int count = Math.Max(0, last - first + 1);
            while (_linePool.Count < count)
            {
                Label label = NewLineLabel();
                _linePool.Add(label);
                _linesLayer.Add(label);
                Label number = new() { pickingMode = PickingMode.Ignore };
                number.AddToClassList("git-code-editor__number");
                _numberPool.Add(number);
                _gutter.Add(number);
            }
            float scrollTop = _scroll.scrollOffset.y;
            for (int i = 0; i < _linePool.Count; i++)
            {
                Label label = _linePool[i];
                Label number = _numberPool[i];
                bool shown = i < count;
                label.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                number.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                if (shown == false)
                {
                    continue;
                }
                int line = first + i;
                label.style.top = line * LineHeight;
                label.text = RenderLine(line);
                number.style.top = line * LineHeight - scrollTop;
                number.text = (line + 1).ToString();
                number.EnableInClassList("git-code-editor__number--current", line == _caret.Line);
                number.EnableInClassList("git-code-editor__number--changed", _changedLines.Contains(line + 1));
            }
            UpdateCaret();
            _decorations.MarkDirtyRepaint();
        }

        private void UpdateCaret()
        {
            float x = TextPadding + XOf(_caret.Line, _caret.Column);
            float y = _caret.Line * LineHeight;
            _caretElement.style.left = x - 1;
            _caretElement.style.top = y + 1;
            bool composing = _compositionText.Length > 0;
            _composition.style.display = composing ? DisplayStyle.Flex : DisplayStyle.None;
            if (composing)
            {
                _composition.style.left = x;
                _composition.style.top = y;
                _composition.text = _compositionText;
            }
            _caretElement.style.visibility = _hasFocus && composing == false ? Visibility.Visible : Visibility.Hidden;
        }

        /// <summary>
        /// 지금 줄 강조, 고른 글 배경, 들여쓰기 안내선을 보이는 줄에만 그린다.
        /// </summary>
        private void DrawDecorations(MeshGenerationContext context)
        {
            if (_asciiWidth <= 0 || _buffer.LineCount == 0)
            {
                return;
            }
            Painter2D painter = context.painter2D;
            float width = _content.layout.width;
            if (float.IsNaN(width))
            {
                return;
            }
            (int first, int last) = VisibleLines();
            (GitTextPosition start, GitTextPosition end) = Ordered();
            bool hasSelection = start != end;
            if (hasSelection == false && _caret.Line >= first && _caret.Line <= last)
            {
                FillRect(painter, _currentLineColor, 0, _caret.Line * LineHeight, width, LineHeight);
            }
            DrawIndentGuides(painter, first, last);
            if (hasSelection)
            {
                for (int line = Math.Max(first, start.Line); line <= Math.Min(last, end.Line); line++)
                {
                    float x0 = line == start.Line ? XOf(line, start.Column) : 0;
                    // 다음 줄로 이어 고른 줄은 줄 끝 너머 한 칸까지 칠한다(빈 줄도 고른 것이 보이게).
                    float x1 = line == end.Line ? XOf(line, end.Column) : XOf(line, int.MaxValue) + _asciiWidth * 0.6f;
                    if (x1 > x0)
                    {
                        FillRect(painter, _selectionColor, TextPadding + x0, line * LineHeight, x1 - x0, LineHeight);
                    }
                }
            }
        }

        private void DrawIndentGuides(Painter2D painter, int first, int last)
        {
            float step = _indentSize * _asciiWidth;
            if (step <= 0)
            {
                return;
            }
            float previous = -1f;
            for (int line = first; line <= last; line++)
            {
                float indent = IndentWidth(line);
                if (indent < 0)
                {
                    // 빈 줄은 앞뒤 줄 중 덜 들여 쓴 쪽을 따라 안내선을 잇는다.
                    float before = previous >= 0 ? previous : NeighbourIndent(line, -1);
                    float after = NeighbourIndent(line, 1);
                    indent = Mathf.Min(before < 0 ? 0 : before, after < 0 ? 0 : after);
                }
                else
                {
                    previous = indent;
                }
                for (float x = 0; x < indent - 0.5f; x += step)
                {
                    FillRect(painter, _indentGuideColor, TextPadding + Mathf.Round(x), line * LineHeight, 1, LineHeight);
                }
            }
        }

        private float NeighbourIndent(int line, int direction)
        {
            for (int i = 1; i <= 200; i++)
            {
                int other = line + i * direction;
                if (other < 0 || other >= _buffer.LineCount)
                {
                    return -1f;
                }
                float indent = IndentWidth(other);
                if (indent >= 0)
                {
                    return indent;
                }
            }
            return -1f;
        }

        private static void FillRect(Painter2D painter, Color color, float x, float y, float width, float height)
        {
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(new Vector2(x, y));
            painter.LineTo(new Vector2(x + width, y));
            painter.LineTo(new Vector2(x + width, y + height));
            painter.LineTo(new Vector2(x, y + height));
            painter.ClosePath();
            painter.Fill();
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            ICustomStyle custom = evt.customStyle;
            custom.TryGetValue(KeywordProperty, out _keywordColor);
            custom.TryGetValue(StringProperty, out _stringColor);
            custom.TryGetValue(NumberProperty, out _numberColor);
            custom.TryGetValue(CommentProperty, out _commentColor);
            custom.TryGetValue(KeyProperty, out _keyColor);
            custom.TryGetValue(CurrentLineProperty, out _currentLineColor);
            custom.TryGetValue(IndentGuideProperty, out _indentGuideColor);
            custom.TryGetValue(SelectionProperty, out _selectionColor);
            UpdateKindColors();
            Refresh();
        }

        // ── 커서·고르기 ───────────────────────────

        private (GitTextPosition start, GitTextPosition end) Ordered()
        {
            return _anchor <= _caret ? (_anchor, _caret) : (_caret, _anchor);
        }

        private bool HasSelection => _anchor != _caret;

        /// <summary>
        /// 커서를 옮긴다. <paramref name="extend"/> 면 닻은 두고 고른 범위를 늘린다.
        /// </summary>
        private void MoveCaret(GitTextPosition position, bool extend, bool keepPreferredX = false)
        {
            _caret = _buffer.Clamp(position);
            if (extend == false)
            {
                _anchor = _caret;
            }
            if (keepPreferredX == false)
            {
                _preferredX = -1f;
            }
            _buffer.BreakMerge();
            AfterCaretMoved();
        }

        private void AfterCaretMoved()
        {
            RestartBlink();
            EnsureCaretVisible();
            Refresh();
        }

        private void EnsureCaretVisible()
        {
            Rect view = _scroll.contentViewport.layout;
            if (float.IsNaN(view.height) || view.height <= 0)
            {
                return;
            }
            Vector2 offset = _scroll.scrollOffset;
            float top = _caret.Line * LineHeight;
            if (top < offset.y)
            {
                offset.y = top;
            }
            else if (top + LineHeight > offset.y + view.height)
            {
                offset.y = top + LineHeight - view.height;
            }
            float x = TextPadding + XOf(_caret.Line, _caret.Column);
            float margin = _asciiWidth * 4;
            if (x - margin < offset.x)
            {
                offset.x = Mathf.Max(0, x - margin);
            }
            else if (x + margin > offset.x + view.width)
            {
                offset.x = x + margin - view.width;
            }
            if (offset != _scroll.scrollOffset)
            {
                UpdateMetrics();
                _scroll.scrollOffset = offset;
            }
        }

        private static int CharKind(char c)
        {
            if (char.IsLetterOrDigit(c) || c == '_')
            {
                return 0;
            }
            return c == ' ' || c == '\t' ? 1 : 2;
        }

        /// <summary>
        /// Ctrl+←→ 가 갈 자리: 공백을 건너 다음(앞) 낱말의 끝(시작). 줄 끝·처음이면 옆 줄로 넘어간다.
        /// </summary>
        private GitTextPosition WordStep(GitTextPosition from, int direction)
        {
            string text = _buffer[from.Line];
            int column = from.Column;
            if (direction < 0)
            {
                if (column == 0)
                {
                    return from.Line > 0 ? new GitTextPosition(from.Line - 1, _buffer[from.Line - 1].Length) : from;
                }
                while (column > 0 && CharKind(text[column - 1]) == 1)
                {
                    column--;
                }
                if (column > 0)
                {
                    int kind = CharKind(text[column - 1]);
                    while (column > 0 && CharKind(text[column - 1]) == kind)
                    {
                        column--;
                    }
                }
                return new GitTextPosition(from.Line, column);
            }
            if (column >= text.Length)
            {
                return from.Line + 1 < _buffer.LineCount ? new GitTextPosition(from.Line + 1, 0) : from;
            }
            while (column < text.Length && CharKind(text[column]) == 1)
            {
                column++;
            }
            if (column < text.Length)
            {
                int kind = CharKind(text[column]);
                while (column < text.Length && CharKind(text[column]) == kind)
                {
                    column++;
                }
            }
            return new GitTextPosition(from.Line, column);
        }

        private (int start, int end) WordAt(int line, int column)
        {
            string text = _buffer[line];
            if (text.Length == 0)
            {
                return (0, 0);
            }
            int at = Math.Min(column, text.Length - 1);
            int kind = CharKind(text[at]);
            if (kind == 2)
            {
                return (at, at + 1);
            }
            int start = at;
            int end = at + 1;
            while (start > 0 && CharKind(text[start - 1]) == kind)
            {
                start--;
            }
            while (end < text.Length && CharKind(text[end]) == kind)
            {
                end++;
            }
            return (start, end);
        }

        private static int FirstNonWhitespace(string text)
        {
            int i = 0;
            while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
            {
                i++;
            }
            return i;
        }

        private void MoveVertical(int lines, bool extend)
        {
            if (_preferredX < 0)
            {
                _preferredX = XOf(_caret.Line, _caret.Column);
            }
            int target = Math.Clamp(_caret.Line + lines, 0, _buffer.LineCount - 1);
            if (target == _caret.Line)
            {
                // 맨 위·아래 줄에서 더 가면 줄 처음·끝으로(편집기와 같다).
                MoveCaret(new GitTextPosition(target, lines < 0 ? 0 : _buffer[target].Length), extend);
                return;
            }
            MoveCaret(new GitTextPosition(target, ColumnAt(target, _preferredX)), extend, keepPreferredX: true);
        }

        private int PageLines()
        {
            float height = _scroll.contentViewport.layout.height;
            return float.IsNaN(height) ? 20 : Math.Max(1, Mathf.FloorToInt(height / LineHeight) - 1);
        }

        // ── 고치기 ──────────────────────────────

        /// <summary>
        /// 고른 범위(없으면 커서 자리)를 <paramref name="text"/> 로 바꾼다.
        /// </summary>
        private void ReplaceSelection(string text, GitTextEditKind kind)
        {
            (GitTextPosition start, GitTextPosition end) = Ordered();
            ReplaceRange(start, end, text, kind);
        }

        private void ReplaceRange(GitTextPosition start, GitTextPosition end, string text, GitTextEditKind kind)
        {
            GitTextPosition after = _buffer.Replace(start, end, text, kind, _anchor, _caret);
            _caret = _anchor = after;
            _preferredX = -1f;
            AfterEdit();
        }

        private void AfterEdit()
        {
            InvalidateStates(_buffer.FirstChangedLine);
            _buffer.FirstChangedLine = int.MaxValue;
            RecomputeMaxWidth();
            UpdateMetrics();
            AfterCaretMoved();
            Changed?.Invoke();
        }

        private void InsertNewline()
        {
            (GitTextPosition start, _) = Ordered();
            string line = _buffer[start.Line];
            string indent = line.Substring(0, Math.Min(FirstNonWhitespace(line), start.Column));
            // 여는 괄호 바로 뒤에서 줄을 바꾸면 한 단계 더 들여 쓴다.
            int before = start.Column - 1;
            while (before >= 0 && (line[before] == ' ' || line[before] == '\t'))
            {
                before--;
            }
            if (before >= 0 && (line[before] == '{' || line[before] == '[' || line[before] == '('))
            {
                indent += IndentUnit(0);
            }
            ReplaceSelection("\n" + indent, GitTextEditKind.Other);
        }

        /// <summary>
        /// 들여쓰기 한 단계. 공백이면 화면 칸 <paramref name="column"/> 에서 다음 단계 자리까지.
        /// </summary>
        private string IndentUnit(int visualColumn)
        {
            if (_useTabs)
            {
                return "\t";
            }
            return new string(' ', _indentSize - visualColumn % _indentSize);
        }

        private void IndentSelection(bool outdent)
        {
            (GitTextPosition start, GitTextPosition end) = Ordered();
            if (outdent == false && start == end)
            {
                int visualColumn = _asciiWidth > 0 ? Mathf.RoundToInt(XOf(start.Line, start.Column) / _asciiWidth) : start.Column;
                ReplaceSelection(IndentUnit(visualColumn), GitTextEditKind.Other);
                return;
            }
            // 고른 글이 있거나 Shift+Tab: 고른 줄마다 앞에 한 단계 넣거나 뺀다(고른 글은 지우지 않는다). 끝이 줄 처음이면 그 줄은 빼고.
            int lastLine = end.Line > start.Line && end.Column == 0 ? end.Line - 1 : end.Line;
            StringBuilder builder = new();
            int anchorShift = 0;
            int caretShift = 0;
            for (int line = start.Line; line <= lastLine; line++)
            {
                string text = _buffer[line];
                int delta;
                if (outdent)
                {
                    int remove = 0;
                    if (text.Length > 0 && text[0] == '\t')
                    {
                        remove = 1;
                    }
                    else
                    {
                        while (remove < _indentSize && remove < text.Length && text[remove] == ' ')
                        {
                            remove++;
                        }
                    }
                    builder.Append(text, remove, text.Length - remove);
                    delta = -remove;
                }
                else
                {
                    string unit = text.Length == 0 ? string.Empty : _useTabs ? "\t" : new string(' ', _indentSize);
                    builder.Append(unit).Append(text);
                    delta = unit.Length;
                }
                if (line == _anchor.Line)
                {
                    anchorShift = delta;
                }
                if (line == _caret.Line)
                {
                    caretShift = delta;
                }
                if (line < lastLine)
                {
                    builder.Append('\n');
                }
            }
            GitTextPosition oldAnchor = _anchor;
            GitTextPosition oldCaret = _caret;
            _buffer.Replace(new GitTextPosition(start.Line, 0), new GitTextPosition(lastLine, _buffer[lastLine].Length), builder.ToString(), GitTextEditKind.Other, _anchor, _caret);
            _anchor = _buffer.Clamp(new GitTextPosition(oldAnchor.Line, Math.Max(0, oldAnchor.Column + (oldAnchor.Line <= lastLine ? anchorShift : 0))));
            _caret = _buffer.Clamp(new GitTextPosition(oldCaret.Line, Math.Max(0, oldCaret.Column + (oldCaret.Line <= lastLine ? caretShift : 0))));
            AfterEdit();
        }

        private void Backspace(bool word)
        {
            if (HasSelection)
            {
                ReplaceSelection(string.Empty, GitTextEditKind.Deleting);
                return;
            }
            GitTextPosition from = word ? WordStep(_caret, -1) : _caret.Column > 0 ? new GitTextPosition(_caret.Line, _caret.Column - 1)
                : _caret.Line > 0 ? new GitTextPosition(_caret.Line - 1, _buffer[_caret.Line - 1].Length) : _caret;
            // 한 글자를 넘는 유니코드(서로게이트 쌍)는 함께 지운다.
            if (word == false && from.Line == _caret.Line && from.Column > 0 && char.IsLowSurrogate(_buffer[from.Line][from.Column]) && char.IsHighSurrogate(_buffer[from.Line][from.Column - 1]))
            {
                from = new GitTextPosition(from.Line, from.Column - 1);
            }
            if (from != _caret)
            {
                ReplaceRange(from, _caret, string.Empty, GitTextEditKind.Deleting);
            }
        }

        private void DeleteForward(bool word)
        {
            if (HasSelection)
            {
                ReplaceSelection(string.Empty, GitTextEditKind.Deleting);
                return;
            }
            string text = _buffer[_caret.Line];
            GitTextPosition to = word ? WordStep(_caret, 1) : _caret.Column < text.Length ? new GitTextPosition(_caret.Line, _caret.Column + 1)
                : _caret.Line + 1 < _buffer.LineCount ? new GitTextPosition(_caret.Line + 1, 0) : _caret;
            if (word == false && to.Line == _caret.Line && to.Column < text.Length && char.IsLowSurrogate(text[to.Column]))
            {
                to = new GitTextPosition(to.Line, to.Column + 1);
            }
            if (to != _caret)
            {
                ReplaceRange(_caret, to, string.Empty, GitTextEditKind.Deleting);
            }
        }

        private void Undo()
        {
            ApplyHistory(_buffer.Undo());
        }

        private void Redo()
        {
            ApplyHistory(_buffer.Redo());
        }

        private void ApplyHistory((GitTextPosition anchor, GitTextPosition caret)? selection)
        {
            if (selection == null)
            {
                return;
            }
            _anchor = _buffer.Clamp(selection.Value.anchor);
            _caret = _buffer.Clamp(selection.Value.caret);
            _preferredX = -1f;
            AfterEdit();
        }

        private void Copy()
        {
            if (HasSelection)
            {
                EditorGUIUtility.systemCopyBuffer = SelectedText();
            }
        }

        private void Cut()
        {
            if (HasSelection)
            {
                Copy();
                ReplaceSelection(string.Empty, GitTextEditKind.Other);
            }
        }

        private void Paste()
        {
            string text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrEmpty(text) == false)
            {
                ReplaceSelection(text, GitTextEditKind.Other);
            }
        }

        private void SelectAll()
        {
            _anchor = new GitTextPosition(0, 0);
            _caret = _buffer.End;
            _buffer.BreakMerge();
            AfterCaretMoved();
        }

        /// <summary>
        /// 고른 글. 줄 사이는 운영체제 줄 끝(다른 프로그램에 붙여 넣기 좋게). 이 편집기에 다시 붙여 넣으면 '\n' 으로 맞춘다.
        /// </summary>
        private string SelectedText()
        {
            (GitTextPosition start, GitTextPosition end) = Ordered();
            string text = _buffer.GetText(start, end);
            return Environment.NewLine == "\n" ? text : text.Replace("\n", Environment.NewLine);
        }

        // ── 키보드 ──────────────────────────────

        private void OnKeyDown(KeyDownEvent evt)
        {
            char character = evt.character;
            // 앞서 키 코드로 처리한 Enter·Tab 의 글자 KeyDown 은 버린다.
            if (_swallowCharacter != '\0' && evt.keyCode == KeyCode.None && character != '\0')
            {
                bool same = character == _swallowCharacter || (_swallowCharacter == '\n' && character == '\r');
                _swallowCharacter = '\0';
                if (same)
                {
                    Consume(evt);
                    return;
                }
            }
            else if (evt.keyCode != KeyCode.None)
            {
                _swallowCharacter = '\0';
            }

            bool shift = evt.shiftKey;
            bool action = evt.actionKey;
            bool handled = true;
            switch (evt.keyCode)
            {
                case KeyCode.LeftArrow:
                    if (HasSelection && shift == false && action == false)
                    {
                        MoveCaret(Ordered().start, false);
                    }
                    else
                    {
                        MoveCaret(action ? WordStep(_caret, -1) : _caret.Column > 0 ? new GitTextPosition(_caret.Line, _caret.Column - 1)
                            : _caret.Line > 0 ? new GitTextPosition(_caret.Line - 1, _buffer[_caret.Line - 1].Length) : _caret, shift);
                    }
                    break;
                case KeyCode.RightArrow:
                    if (HasSelection && shift == false && action == false)
                    {
                        MoveCaret(Ordered().end, false);
                    }
                    else
                    {
                        MoveCaret(action ? WordStep(_caret, 1) : _caret.Column < _buffer[_caret.Line].Length ? new GitTextPosition(_caret.Line, _caret.Column + 1)
                            : _caret.Line + 1 < _buffer.LineCount ? new GitTextPosition(_caret.Line + 1, 0) : _caret, shift);
                    }
                    break;
                case KeyCode.UpArrow:
                    MoveVertical(-1, shift);
                    break;
                case KeyCode.DownArrow:
                    MoveVertical(1, shift);
                    break;
                case KeyCode.PageUp:
                    MoveVertical(-PageLines(), shift);
                    break;
                case KeyCode.PageDown:
                    MoveVertical(PageLines(), shift);
                    break;
                case KeyCode.Home:
                    if (action)
                    {
                        MoveCaret(new GitTextPosition(0, 0), shift);
                    }
                    else
                    {
                        // 처음 누르면 글 시작(들여쓰기 뒤), 거기서 한 번 더 누르면 줄 처음.
                        int first = FirstNonWhitespace(_buffer[_caret.Line]);
                        MoveCaret(new GitTextPosition(_caret.Line, _caret.Column == first ? 0 : first), shift);
                    }
                    break;
                case KeyCode.End:
                    MoveCaret(action ? _buffer.End : new GitTextPosition(_caret.Line, _buffer[_caret.Line].Length), shift);
                    break;
                case KeyCode.Backspace:
                    Backspace(action);
                    break;
                case KeyCode.Delete:
                    DeleteForward(action);
                    break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    if (_compositionText.Length > 0)
                    {
                        // 한글 조합을 끝내는 Enter 는 IME 가 쓴다.
                        handled = false;
                        break;
                    }
                    InsertNewline();
                    if (character == '\0')
                    {
                        _swallowCharacter = '\n';
                    }
                    break;
                case KeyCode.Tab:
                    IndentSelection(shift);
                    if (character == '\0')
                    {
                        _swallowCharacter = '\t';
                    }
                    break;
                case KeyCode.Escape:
                    if (HasSelection)
                    {
                        MoveCaret(_caret, false);
                    }
                    else
                    {
                        CancelRequested?.Invoke();
                    }
                    break;
                default:
                    handled = HandleShortcutOrCharacter(evt);
                    break;
            }
            if (handled)
            {
                Consume(evt);
            }
        }

        private bool HandleShortcutOrCharacter(KeyDownEvent evt)
        {
            char character = evt.character;
            if (evt.actionKey && evt.altKey == false)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.A:
                        RunShortcut("SelectAll", SelectAll);
                        return true;
                    case KeyCode.C:
                        RunShortcut("Copy", Copy);
                        return true;
                    case KeyCode.X:
                        RunShortcut("Cut", Cut);
                        return true;
                    case KeyCode.V:
                        RunShortcut("Paste", Paste);
                        return true;
                    case KeyCode.Z:
                        if (evt.shiftKey)
                        {
                            RunShortcut("Redo", Redo);
                        }
                        else
                        {
                            RunShortcut("Undo", Undo);
                        }
                        return true;
                    case KeyCode.Y:
                        RunShortcut("Redo", Redo);
                        return true;
                    case KeyCode.S:
                        RunShortcut("Save", () => SaveRequested?.Invoke());
                        return true;
                }
                // 그 밖의 Ctrl 조합은 에디터 단축키 몫. 글자로 넣지 않는다.
                return false;
            }
            if (character == '\n' || character == '\r')
            {
                InsertNewline();
                return true;
            }
            if (character == '\t')
            {
                IndentSelection(evt.shiftKey);
                return true;
            }
            if (character == '\0' || char.IsControl(character))
            {
                // 글자 없는 키(F 키, Shift 만 등)는 그냥 둔다. 다만 Ctrl 없는 키라 에디터 단축키로 넘기지 않게 막는다.
                return evt.keyCode != KeyCode.None && IsFunctionKey(evt.keyCode) == false;
            }
            ReplaceSelection(character.ToString(), GitTextEditKind.Typing);
            return true;
        }

        private static bool IsFunctionKey(KeyCode keyCode)
        {
            return keyCode >= KeyCode.F1 && keyCode <= KeyCode.F15;
        }

        /// <summary>
        /// 단축키를 실행한다. 같은 동작이 에디터 명령(ExecuteCommand)으로 곧바로 한 번 더 오면 <see cref="OnExecuteCommand"/> 가 버린다.
        /// </summary>
        private void RunShortcut(string name, Action action)
        {
            if (_lastShortcut == name && EditorApplication.timeSinceStartup - _lastShortcutTime < DuplicateCommandSeconds)
            {
                return;
            }
            _lastShortcut = name;
            _lastShortcutTime = EditorApplication.timeSinceStartup;
            action();
        }

        private void Consume(EventBase evt)
        {
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        /// <summary>
        /// Tab·방향키가 다른 요소로 초점을 옮기지 않게 한다(글 칸 안에서 쓴다).
        /// </summary>
        private void OnNavigationMove(NavigationMoveEvent evt)
        {
            evt.StopPropagation();
            focusController?.IgnoreEvent(evt);
        }

        private static bool IsEditCommand(string name)
        {
            return name == "Copy" || name == "Cut" || name == "Paste" || name == "SelectAll" || name == "Undo" || name == "Redo" || name == "Delete" || name == "SoftDelete";
        }

        private void OnValidateCommand(ValidateCommandEvent evt)
        {
            if (IsEditCommand(evt.commandName))
            {
                evt.StopPropagation();
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            switch (evt.commandName)
            {
                case "Copy":
                    RunShortcut("Copy", Copy);
                    break;
                case "Cut":
                    RunShortcut("Cut", Cut);
                    break;
                case "Paste":
                    RunShortcut("Paste", Paste);
                    break;
                case "SelectAll":
                    RunShortcut("SelectAll", SelectAll);
                    break;
                case "Undo":
                    RunShortcut("Undo", Undo);
                    break;
                case "Redo":
                    RunShortcut("Redo", Redo);
                    break;
                case "Delete":
                case "SoftDelete":
                    // Delete 키는 KeyDown 이 이미 처리했다. 에디터의 "에셋 지우기" 로 넘어가지 않게 막기만 한다.
                    break;
                default:
                    return;
            }
            evt.StopPropagation();
        }

        // ── 마우스 ──────────────────────────────

        private GitTextPosition PositionAt(Vector2 worldPosition)
        {
            Vector2 local = _content.WorldToLocal(worldPosition);
            int line = Math.Clamp(Mathf.FloorToInt(local.y / LineHeight), 0, _buffer.LineCount - 1);
            if (local.y < 0)
            {
                return new GitTextPosition(0, 0);
            }
            return new GitTextPosition(line, ColumnAt(line, local.x - TextPadding));
        }

        private void OnPointerDown(PointerDownEvent evt)
        {
            if (evt.button != 0)
            {
                if (evt.button == 1)
                {
                    Focus();
                }
                return;
            }
            Focus();
            GitTextPosition position = PositionAt(evt.position);
            if (evt.clickCount == 2)
            {
                (int start, int end) = WordAt(position.Line, position.Column);
                _anchor = new GitTextPosition(position.Line, start);
                MoveCaret(new GitTextPosition(position.Line, end), true);
            }
            else if (evt.clickCount >= 3)
            {
                _anchor = new GitTextPosition(position.Line, 0);
                MoveCaret(position.Line + 1 < _buffer.LineCount ? new GitTextPosition(position.Line + 1, 0) : new GitTextPosition(position.Line, _buffer[position.Line].Length), true);
            }
            else
            {
                MoveCaret(position, evt.shiftKey);
            }
            _dragging = evt.clickCount == 1;
            if (_dragging)
            {
                _scroll.contentViewport.CapturePointer(evt.pointerId);
            }
            evt.StopPropagation();
        }

        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragging == false || _scroll.contentViewport.HasPointerCapture(evt.pointerId) == false)
            {
                return;
            }
            if ((evt.pressedButtons & 1) == 0)
            {
                _dragging = false;
                _scroll.contentViewport.ReleasePointer(evt.pointerId);
                return;
            }
            // 글 칸 밖으로 끌면 그만큼 굴린다.
            Rect view = _scroll.contentViewport.worldBound;
            Vector2 offset = _scroll.scrollOffset;
            if (evt.position.y < view.yMin)
            {
                offset.y -= view.yMin - evt.position.y;
            }
            else if (evt.position.y > view.yMax)
            {
                offset.y += evt.position.y - view.yMax;
            }
            if (evt.position.x < view.xMin)
            {
                offset.x -= view.xMin - evt.position.x;
            }
            else if (evt.position.x > view.xMax)
            {
                offset.x += evt.position.x - view.xMax;
            }
            _scroll.scrollOffset = new Vector2(Mathf.Max(0, offset.x), Mathf.Max(0, offset.y));
            GitTextPosition position = PositionAt(evt.position);
            if (position != _caret)
            {
                _caret = _buffer.Clamp(position);
                _preferredX = -1f;
                RestartBlink();
                Refresh();
            }
        }

        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragging == false)
            {
                return;
            }
            _dragging = false;
            if (_scroll.contentViewport.HasPointerCapture(evt.pointerId))
            {
                _scroll.contentViewport.ReleasePointer(evt.pointerId);
            }
        }

        private void OnContextMenu(ContextualMenuPopulateEvent evt)
        {
            DropdownMenuAction.Status selection = HasSelection ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled;
            evt.menu.AppendAction(GitText.Get("editor.undo"), _ => Undo(), _buffer.CanUndo ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendAction(GitText.Get("editor.redo"), _ => Redo(), _buffer.CanRedo ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
            evt.menu.AppendSeparator();
            evt.menu.AppendAction(GitText.Get("editor.cut"), _ => Cut(), selection);
            evt.menu.AppendAction(GitText.Get("diff.copy"), _ => Copy(), selection);
            evt.menu.AppendAction(GitText.Get("editor.paste"), _ => Paste(),
                string.IsNullOrEmpty(EditorGUIUtility.systemCopyBuffer) ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);
            evt.menu.AppendAction(GitText.Get("diff.selectAll"), _ => SelectAll());
            if (ExtraMenuItems != null)
            {
                evt.menu.AppendSeparator();
                ExtraMenuItems(evt.menu);
            }
            evt.StopPropagation();
        }

        // ── 초점·커서 깜빡임·IME ─────────────────

        private void OnFocusChanged(bool focused)
        {
            if (_hasFocus == focused)
            {
                return;
            }
            _hasFocus = focused;
            if (focused)
            {
                // 한글 입력기(IME)가 조합하도록 켠다. 조합 중인 글은 커서 자리에 따로 보이고, 조합이 끝난 글자는 KeyDown 으로 온다.
                Ime.SetEnabled(true);
                _compositionPoll ??= schedule.Execute(PollComposition).Every(CompositionPollMs);
                _compositionPoll.Resume();
                RestartBlink();
            }
            else
            {
                Ime.SetEnabled(false);
                _compositionPoll?.Pause();
                _blink?.Pause();
                _compositionText = string.Empty;
                _dragging = false;
            }
            UpdateCaret();
        }

        private void PollComposition()
        {
            string composition = Ime.CompositionString;
            if (composition == _compositionText)
            {
                return;
            }
            _compositionText = composition;
            RestartBlink();
            UpdateCaret();
        }

        private void RestartBlink()
        {
            _caretOn = true;
            _caretElement.style.opacity = 1f;
            if (_hasFocus == false)
            {
                return;
            }
            _blink ??= schedule.Execute(() =>
            {
                _caretOn = _caretOn == false;
                _caretElement.style.opacity = _caretOn ? 1f : 0f;
            }).Every(BlinkMs);
            _blink.ExecuteLater(BlinkMs);
        }

        /// <summary>
        /// 에디터의 IME 상태. IMGUI 글 칸이 쓰는 <c>GUIUtility.imeCompositionMode</c>·<c>compositionString</c>(내부)을 쓰고,
        /// 없으면 <see cref="Input"/> 으로 한다(Input System 만 켠 프로젝트에서는 Input 이 예외를 낼 수 있어 감싼다).
        /// </summary>
#if UNITY_6000_7_OR_NEWER
        [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
        private static class Ime
        {
            private const System.Reflection.BindingFlags StaticFlags = System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            private static readonly System.Reflection.PropertyInfo CompositionProperty = typeof(GUIUtility).GetProperty("compositionString", StaticFlags);
            private static readonly System.Reflection.PropertyInfo ModeProperty = typeof(GUIUtility).GetProperty("imeCompositionMode", StaticFlags);

            public static string CompositionString
            {
                get
                {
                    try
                    {
                        return (CompositionProperty != null ? CompositionProperty.GetValue(null) as string : Input.compositionString) ?? string.Empty;
                    }
                    catch (Exception)
                    {
                        return string.Empty;
                    }
                }
            }

            public static void SetEnabled(bool enabled)
            {
                IMECompositionMode mode = enabled ? IMECompositionMode.On : IMECompositionMode.Auto;
                try
                {
                    if (ModeProperty != null && ModeProperty.GetSetMethod(true) != null)
                    {
                        ModeProperty.SetValue(null, mode);
                    }
                    else
                    {
                        Input.imeCompositionMode = mode;
                    }
                }
                catch (Exception)
                {
                    // IME 를 켤 수 없는 환경이면 조합 없이 글자 KeyDown 만 받는다.
                }
            }
        }
    }
}
