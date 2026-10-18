using System.Collections.Generic;
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
        private const string MonospaceFontPath = "Fonts/RobotoMono/RobotoMono-Regular.ttf";
        private const float RowHeight = 18f;

        [SerializeField] private string _repoPath;

        private ListView _list;
        private Label _summary;
        private List<GitBlameLine> _lines = new();
        private List<bool> _blockStarts = new();
        private List<bool> _oddBlocks = new();
        private Font _monospace;

        public static void Open(string repoPath)
        {
            GitBlameWindow window = CreateInstance<GitBlameWindow>();
            window._repoPath = repoPath;
            window.titleContent = TitleWithIcon(GitText.Format("blame.windowTitle", System.IO.Path.GetFileName(repoPath)));
            window.Show();
            window.Load();
        }

        private void CreateGUI()
        {
            CreateContent();
            _monospace = EditorGUIUtility.Load(MonospaceFontPath) as Font;

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
            ((Label)row[4]).text = (line.Text ?? string.Empty).Replace("\t", "    ");
            row.tooltip = line.IsUncommitted ? GitText.Get("blame.uncommitted") : $"{line.ShortHash}  {line.AuthorName}  {GitDisplay.FormatDate(line.Date)}\n{line.Summary}";
        }
    }
}
