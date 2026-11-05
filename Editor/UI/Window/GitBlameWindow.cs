using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 파일 blame: 줄마다 그 줄을 마지막으로 바꾼 커밋. 같은 커밋이 이어지는 줄 묶음은 첫 줄에만 해시·작성자·날짜를 적고,
    /// 묶음마다 배경을 번갈아 칠해 경계가 보이게 한다(GitKraken·IntelliJ 의 Annotate 와 같은 모양).
    /// </summary>
    public sealed class GitBlameWindow : GitToolWindowBase
    {
        private const float RowHeight = 18f;

        [SerializeField] private string _repoPath;

        private ListView _list;
        private Label _summary;
        private List<GitBlameLine> _lines = new();
        private List<bool> _blockStarts = new();
        private List<bool> _oddBlocks = new();
        private Font _monospace;

        /// <summary>
        /// 코드 칸의 문법 색(<see cref="GitSyntax"/>). diff 와 같은 테마 토큰을 GitStaging.uss 의 <c>.blame-list</c> 가 <c>--code-*</c> 로 넘긴다.
        /// 갈래 이름을 소문자로 붙인 이름(<c>--code-keyword</c> 등)이라 갈래가 늘어도 여기는 그대로 둔다. Plain 은 칠하지 않는다.
        /// </summary>
        private static readonly CustomStyleProperty<Color>[] SyntaxColorProperties = MakeSyntaxColorProperties();

        private readonly string[] _syntaxColors = new string[Enum.GetValues(typeof(GitSyntaxKind)).Length];
        private readonly List<GitSyntaxSpan> _syntaxSpans = new();

        /// <summary>파일의 말 갈래와, 줄마다 그 줄을 칠하기 시작할 상태(앞 줄의 블록 주석 안 등). <see cref="UpdateSyntaxStates"/> 가 채운다.</summary>
        private GitSyntaxLanguage _syntaxLanguage;
        private int[] _syntaxStates = Array.Empty<int>();

        /// <summary>이보다 줄이 많은 파일은 칠하지 않는다(열 때 모든 줄을 한 번 훑는다). diff 와 같은 한도.</summary>
        private const int MaxSyntaxLines = 50000;

        public static void Open(string repoPath)
        {
            GitBlameWindow window = CreateInstance<GitBlameWindow>();
            window._repoPath = repoPath;
            window.titleContent = TitleWithIcon(GitText.Format("blame.windowTitle", System.IO.Path.GetFileName(repoPath)));
            window.Show();
            window.position = GitWindowPlacement.CenteredOnMain(new UnityEngine.Vector2(1000, 800));
            window.Load();
        }

        private void CreateGUI()
        {
            CreateContent();
            _monospace = GitFonts.Monospace;

            _summary = new Label();
            _summary.AddToClassList("view__toolbar-label");
            _summary.style.height = 22;
            Content.Add(_summary);

            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.AddToClassList("git-diff__list");
            _list.AddToClassList("blame-list");
            _list.RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
            Content.Add(_list);
            Load();
        }

        private async void Load()
        {
            if (_list == null || string.IsNullOrEmpty(_repoPath))
            {
                return;
            }
            _summary.text = GitText.Get("common.loading");
            (List<GitBlameLine> lines, string error) = await GitFileOperations.BlameAsync(_repoPath);
            _lines = lines;
            MarkBlocks();
            UpdateSyntaxStates();
            _summary.text = error ?? GitText.Format("blame.summary", GitDisplay.Nfc(_repoPath), lines.Count);
            _list.itemsSource = _lines;
            _list.Rebuild();
        }

        /// <summary>
        /// 같은 커밋이 이어지는 줄을 한 묶음으로 본다. 묶음의 첫 줄과 묶음 순번의 홀짝을 미리 계산한다.
        /// </summary>
        private void MarkBlocks()
        {
            _blockStarts = new List<bool>(_lines.Count);
            _oddBlocks = new List<bool>(_lines.Count);
            bool odd = false;
            for (int i = 0; i < _lines.Count; i++)
            {
                bool isStart = i == 0 || _lines[i].Hash != _lines[i - 1].Hash;
                if (isStart && i > 0)
                {
                    odd = odd == false;
                }
                _blockStarts.Add(isStart);
                _oddBlocks.Add(odd);
            }
        }

        private VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList("blame-row");
            row.Add(Cell("blame-row__hash"));
            row.Add(Cell("blame-row__author"));
            row.Add(Cell("blame-row__date"));
            row.Add(Cell("blame-row__number"));
            Label code = Cell("blame-row__code");
            if (_monospace != null)
            {
                code.style.unityFontDefinition = FontDefinition.FromFont(_monospace);
            }
            row.Add(code);
            return row;
        }

        private static Label Cell(string className)
        {
            Label label = new() { enableRichText = false };
            label.AddToClassList(className);
            return label;
        }

        private void BindRow(VisualElement row, int index)
        {
            GitBlameLine line = _lines[index];
            bool isStart = _blockStarts[index];
            row.EnableInClassList("blame-row--odd", _oddBlocks[index]);
            row.EnableInClassList("blame-row--start", isStart);
            row.EnableInClassList("blame-row--uncommitted", line.IsUncommitted);

            ((Label)row[0]).text = isStart ? (line.IsUncommitted ? GitText.Get("blame.uncommitted") : line.ShortHash) : string.Empty;
            ((Label)row[1]).text = isStart && line.IsUncommitted == false ? line.AuthorName : string.Empty;
            ((Label)row[2]).text = isStart && line.IsUncommitted == false ? GitDisplay.FormatDate(line.Date) : string.Empty;
            ((Label)row[3]).text = line.LineNumber.ToString();
            BindCode((Label)row[4], index);
            row.tooltip = line.IsUncommitted ? GitText.Get("blame.uncommitted") : $"{line.ShortHash}  {line.AuthorName}  {GitDisplay.FormatDate(line.Date)}\n{line.Summary}";
        }

        private static CustomStyleProperty<Color>[] MakeSyntaxColorProperties()
        {
            Array kinds = Enum.GetValues(typeof(GitSyntaxKind));
            CustomStyleProperty<Color>[] properties = new CustomStyleProperty<Color>[kinds.Length];
            foreach (GitSyntaxKind kind in kinds)
            {
                properties[(int)kind] = new CustomStyleProperty<Color>("--code-" + kind.ToString().ToLowerInvariant());
            }
            return properties;
        }

        private void OnCustomStyleResolved(CustomStyleResolvedEvent evt)
        {
            bool changed = false;
            for (int kind = 1; kind < SyntaxColorProperties.Length; kind++)
            {
                string hex = evt.customStyle.TryGetValue(SyntaxColorProperties[kind], out Color color) ? "#" + ColorUtility.ToHtmlStringRGBA(color) : null;
                changed |= _syntaxColors[kind] != hex;
                _syntaxColors[kind] = hex;
            }
            if (changed && _syntaxLanguage != GitSyntaxLanguage.None)
            {
                _list.RefreshItems();
            }
        }

        /// <summary>
        /// 확장자로 말 갈래를 고르고, 첫 줄부터 이어서 훑어 줄마다 칠하기 시작할 상태를 구한다(blame 은 파일 전체가 차례대로라 diff 처럼 판을 나눌 일이 없다).
        /// </summary>
        private void UpdateSyntaxStates()
        {
            _syntaxLanguage = _lines.Count > MaxSyntaxLines ? GitSyntaxLanguage.None : GitSyntax.LanguageOf(_repoPath);
            _syntaxStates = new int[_lines.Count];
            if (_syntaxLanguage == GitSyntaxLanguage.None)
            {
                return;
            }
            int state = GitSyntax.StateNormal;
            for (int i = 0; i < _lines.Count; i++)
            {
                _syntaxStates[i] = state;
                _syntaxSpans.Clear();
                state = GitSyntax.Tokenize(_lines[i].Text ?? string.Empty, _syntaxLanguage, state, _syntaxSpans);
            }
        }

        /// <summary>
        /// 코드 칸. 칠할 조각이 있으면 리치 텍스트로 조각마다 &lt;color&gt; 를 두르고, 코드의 &lt; &gt; 는 &lt;noparse&gt; 로 감싼다. 탭은 4칸으로 펼친다.
        /// </summary>
        private void BindCode(Label code, int index)
        {
            string raw = _lines[index].Text ?? string.Empty;
            _syntaxSpans.Clear();
            if (_syntaxLanguage != GitSyntaxLanguage.None && raw.Length > 0 && index < _syntaxStates.Length)
            {
                GitSyntax.Tokenize(raw, _syntaxLanguage, _syntaxStates[index], _syntaxSpans);
            }
            bool colored = false;
            foreach (GitSyntaxSpan span in _syntaxSpans)
            {
                colored |= _syntaxColors[(int)span.Kind] != null && span.Length > 0;
            }
            code.enableRichText = colored;
            if (colored == false)
            {
                code.text = raw.Replace("\t", "    ");
                return;
            }
            StringBuilder builder = new(raw.Length * 2 + _syntaxSpans.Count * 32);
            int position = 0;
            foreach (GitSyntaxSpan span in _syntaxSpans)
            {
                int start = Math.Clamp(span.Start, position, raw.Length);
                int end = Math.Clamp(span.Start + span.Length, start, raw.Length);
                AppendPlain(builder, raw, position, start);
                string color = _syntaxColors[(int)span.Kind];
                if (color != null && end > start)
                {
                    builder.Append("<color=").Append(color).Append('>');
                    AppendPlain(builder, raw, start, end);
                    builder.Append("</color>");
                }
                else
                {
                    AppendPlain(builder, raw, start, end);
                }
                position = end;
            }
            AppendPlain(builder, raw, position, raw.Length);
            code.text = builder.ToString();
        }

        private static void AppendPlain(StringBuilder builder, string raw, int from, int to)
        {
            if (to <= from)
            {
                return;
            }
            // 코드에 </noparse> 가 그대로 있으면 감싼 구간이 거기서 끝난다. 사이에 폭 없는 공백을 넣어 끊는다(diff 와 같다).
            string text = raw.Substring(from, to - from).Replace("\t", "    ").Replace("</noparse>", "</no​parse>");
            builder.Append("<noparse>").Append(text).Append("</noparse>");
        }
    }
}
