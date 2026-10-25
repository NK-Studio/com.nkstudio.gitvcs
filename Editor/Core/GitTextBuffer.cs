using System;
using System.Collections.Generic;
using System.Text;

namespace GitVersionControl
{
    /// <summary>
    /// 글 안의 자리(줄, 줄 안 글자 위치). 둘 다 0부터.
    /// </summary>
    public readonly struct GitTextPosition : IEquatable<GitTextPosition>, IComparable<GitTextPosition>
    {
        public readonly int Line;
        public readonly int Column;

        public GitTextPosition(int line, int column)
        {
            Line = line;
            Column = column;
        }

        public int CompareTo(GitTextPosition other)
        {
            return Line != other.Line ? Line.CompareTo(other.Line) : Column.CompareTo(other.Column);
        }

        public bool Equals(GitTextPosition other)
        {
            return Line == other.Line && Column == other.Column;
        }

        public override bool Equals(object obj)
        {
            return obj is GitTextPosition other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Line * 397 ^ Column;
        }

        public static bool operator ==(GitTextPosition a, GitTextPosition b) => a.Equals(b);
        public static bool operator !=(GitTextPosition a, GitTextPosition b) => a.Equals(b) == false;
        public static bool operator <(GitTextPosition a, GitTextPosition b) => a.CompareTo(b) < 0;
        public static bool operator >(GitTextPosition a, GitTextPosition b) => a.CompareTo(b) > 0;
        public static bool operator <=(GitTextPosition a, GitTextPosition b) => a.CompareTo(b) <= 0;
        public static bool operator >=(GitTextPosition a, GitTextPosition b) => a.CompareTo(b) >= 0;

        public override string ToString()
        {
            return $"({Line}, {Column})";
        }
    }

    /// <summary>
    /// 편집 한 번이 어떤 갈래인지. 같은 갈래가 이어지면 되돌리기 한 번에 함께 되돌린다(편집기처럼 글자마다 되돌리지 않게).
    /// </summary>
    public enum GitTextEditKind
    {
        /// <summary>합치지 않는 편집(붙여 넣기·잘라 내기·들여쓰기·줄 바꿈 등).</summary>
        Other,
        /// <summary>글자 치기.</summary>
        Typing,
        /// <summary>Backspace·Delete.</summary>
        Deleting,
    }

    /// <summary>
    /// diff 의 편집기가 쓰는 줄 단위 글과 되돌리기·다시 하기. 줄 끝은 '\n' 하나로 본다(<see cref="GitTextFile"/> 가 저장할 때 원래 줄 끝을 되살린다).
    /// 화면과 상관없는 순수 코드라 테스트로 확인한다.
    /// </summary>
    public sealed class GitTextBuffer
    {
        /// <summary>
        /// 되돌리기 기록 하나: <see cref="Start"/> 에서 <see cref="Removed"/> 를 지우고 <see cref="Inserted"/> 를 넣었다. 커서는 앞뒤 고른 범위.
        /// </summary>
        private sealed class Edit
        {
            public GitTextPosition Start;
            public string Removed;
            public string Inserted;
            public GitTextEditKind Kind;
            public GitTextPosition AnchorBefore;
            public GitTextPosition CaretBefore;
            public GitTextPosition AnchorAfter;
            public GitTextPosition CaretAfter;
        }

        private readonly List<string> _lines = new() { string.Empty };
        private readonly List<Edit> _history = new();

        /// <summary>
        /// 다음 기록이 들어갈 자리. 이보다 뒤의 기록은 다시 하기 몫이다.
        /// </summary>
        private int _historyIndex;

        /// <summary>
        /// 저장한 때의 <see cref="_historyIndex"/>. 새 편집이 그 뒤 기록을 지워 다시 갈 수 없게 되면 -1.
        /// </summary>
        private int _savedIndex;

        /// <summary>
        /// 다음 편집을 앞 기록과 합치지 않는다(커서를 옮겼을 때).
        /// </summary>
        private bool _breakMerge = true;

        public int LineCount => _lines.Count;

        public string this[int line] => _lines[line];

        /// <summary>
        /// 글이 바뀔 때마다 1씩 는다(되돌리기 포함). 화면이 다시 그릴지 가른다.
        /// </summary>
        public int Version { get; private set; }

        /// <summary>
        /// 마지막 저장(또는 읽기) 뒤로 바뀌었는지. 되돌려 저장한 때로 돌아가면 다시 false.
        /// </summary>
        public bool IsDirty => _historyIndex != _savedIndex;

        public bool CanUndo => _historyIndex > 0;
        public bool CanRedo => _historyIndex < _history.Count;

        /// <summary>
        /// 글이 바뀐 첫 줄. 문법 색칠이 그 줄부터 다시 센다. 바뀔 때마다 부르는 쪽이 읽고 <see cref="int.MaxValue"/> 로 되돌린다.
        /// </summary>
        public int FirstChangedLine { get; set; } = int.MaxValue;

        public GitTextBuffer()
        {
        }

        public GitTextBuffer(string text)
        {
            SetText(text);
        }

        /// <summary>
        /// 글 전체를 바꾸고 되돌리기 기록을 비운다(새로 읽은 파일).
        /// </summary>
        public void SetText(string text)
        {
            _lines.Clear();
            _lines.AddRange((text ?? string.Empty).Split('\n'));
            _history.Clear();
            _historyIndex = 0;
            _savedIndex = 0;
            _breakMerge = true;
            FirstChangedLine = 0;
            Version++;
        }

        /// <summary>
        /// 줄 끝 '\n' 으로 이은 글 전체.
        /// </summary>
        public string GetText()
        {
            return string.Join("\n", _lines);
        }

        /// <summary>
        /// 지금을 저장한 때로 삼는다.
        /// </summary>
        public void MarkSaved()
        {
            _savedIndex = _historyIndex;
            _breakMerge = true;
        }

        /// <summary>
        /// 다음 편집이 앞 편집과 합쳐지지 않게 한다(커서를 옮기거나 고른 범위를 바꿨을 때).
        /// </summary>
        public void BreakMerge()
        {
            _breakMerge = true;
        }

        public GitTextPosition Clamp(GitTextPosition position)
        {
            int line = Math.Clamp(position.Line, 0, _lines.Count - 1);
            return new GitTextPosition(line, Math.Clamp(position.Column, 0, _lines[line].Length));
        }

        public GitTextPosition End => new(_lines.Count - 1, _lines[_lines.Count - 1].Length);

        /// <summary>
        /// [a, b) 범위의 글(순서는 상관없다). 줄 사이는 '\n'.
        /// </summary>
        public string GetText(GitTextPosition a, GitTextPosition b)
        {
            (GitTextPosition start, GitTextPosition end) = Order(Clamp(a), Clamp(b));
            if (start.Line == end.Line)
            {
                return _lines[start.Line].Substring(start.Column, end.Column - start.Column);
            }
            StringBuilder builder = new();
            builder.Append(_lines[start.Line], start.Column, _lines[start.Line].Length - start.Column);
            for (int line = start.Line + 1; line < end.Line; line++)
            {
                builder.Append('\n').Append(_lines[line]);
            }
            builder.Append('\n').Append(_lines[end.Line], 0, end.Column);
            return builder.ToString();
        }

        /// <summary>
        /// [a, b) 를 <paramref name="text"/> 로 바꾸고 되돌리기 기록에 남긴다.
        /// </summary>
        /// <param name="anchor">바꾸기 전 고른 범위의 닻(되돌리면 이리로 돌아간다).</param>
        /// <param name="caret">바꾸기 전 커서.</param>
        /// <returns>넣은 글의 끝(새 커서 자리).</returns>
        public GitTextPosition Replace(GitTextPosition a, GitTextPosition b, string text, GitTextEditKind kind, GitTextPosition anchor, GitTextPosition caret)
        {
            (GitTextPosition start, GitTextPosition end) = Order(Clamp(a), Clamp(b));
            text = Normalize(text);
            string removed = GetText(start, end);
            if (removed.Length == 0 && text.Length == 0)
            {
                return start;
            }
            GitTextPosition after = ApplyReplace(start, end, text);
            Record(new Edit
            {
                Start = start,
                Removed = removed,
                Inserted = text,
                Kind = kind,
                AnchorBefore = anchor,
                CaretBefore = caret,
                AnchorAfter = after,
                CaretAfter = after,
            });
            return after;
        }

        /// <summary>
        /// 마지막 편집을 되돌린다.
        /// </summary>
        /// <returns>되돌린 뒤 고를 범위(닻, 커서). 되돌릴 것이 없으면 null.</returns>
        public (GitTextPosition anchor, GitTextPosition caret)? Undo()
        {
            if (CanUndo == false)
            {
                return null;
            }
            Edit edit = _history[--_historyIndex];
            ApplyReplace(edit.Start, EndOf(edit.Start, edit.Inserted), edit.Removed);
            _breakMerge = true;
            return (edit.AnchorBefore, edit.CaretBefore);
        }

        /// <summary>
        /// 되돌린 편집을 다시 한다.
        /// </summary>
        public (GitTextPosition anchor, GitTextPosition caret)? Redo()
        {
            if (CanRedo == false)
            {
                return null;
            }
            Edit edit = _history[_historyIndex++];
            ApplyReplace(edit.Start, EndOf(edit.Start, edit.Removed), edit.Inserted);
            _breakMerge = true;
            return (edit.AnchorAfter, edit.CaretAfter);
        }

        /// <summary>
        /// <paramref name="start"/> 에 <paramref name="text"/> 를 넣으면 끝나는 자리.
        /// </summary>
        public static GitTextPosition EndOf(GitTextPosition start, string text)
        {
            int lastBreak = text.LastIndexOf('\n');
            if (lastBreak < 0)
            {
                return new GitTextPosition(start.Line, start.Column + text.Length);
            }
            int breaks = 0;
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    breaks++;
                }
            }
            return new GitTextPosition(start.Line + breaks, text.Length - lastBreak - 1);
        }

        private static (GitTextPosition, GitTextPosition) Order(GitTextPosition a, GitTextPosition b)
        {
            return a <= b ? (a, b) : (b, a);
        }

        /// <summary>
        /// 붙여 넣은 글의 줄 끝("\r\n"·"\r")을 '\n' 으로 맞춘다.
        /// </summary>
        private static string Normalize(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }
            return text.IndexOf('\r') < 0 ? text : text.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private GitTextPosition ApplyReplace(GitTextPosition start, GitTextPosition end, string text)
        {
            string head = _lines[start.Line].Substring(0, start.Column);
            string tail = _lines[end.Line].Substring(end.Column);
            string[] pieces = text.Split('\n');
            _lines.RemoveRange(start.Line, end.Line - start.Line + 1);
            if (pieces.Length == 1)
            {
                _lines.Insert(start.Line, head + pieces[0] + tail);
            }
            else
            {
                string[] inserted = new string[pieces.Length];
                inserted[0] = head + pieces[0];
                for (int i = 1; i < pieces.Length - 1; i++)
                {
                    inserted[i] = pieces[i];
                }
                inserted[pieces.Length - 1] = pieces[pieces.Length - 1] + tail;
                _lines.InsertRange(start.Line, inserted);
            }
            FirstChangedLine = Math.Min(FirstChangedLine, start.Line);
            Version++;
            return new GitTextPosition(start.Line + pieces.Length - 1, (pieces.Length == 1 ? head.Length : 0) + pieces[pieces.Length - 1].Length);
        }

        /// <summary>
        /// 기록을 남긴다. 다시 하기 몫은 버린다. 바로 앞 기록과 같은 갈래로 이어 치거나 지우면 하나로 합친다.
        /// </summary>
        private void Record(Edit edit)
        {
            if (_historyIndex < _history.Count)
            {
                _history.RemoveRange(_historyIndex, _history.Count - _historyIndex);
                if (_savedIndex > _historyIndex)
                {
                    _savedIndex = -1;
                }
            }
            Edit previous = _historyIndex > 0 ? _history[_historyIndex - 1] : null;
            bool canMerge = _breakMerge == false && previous != null && _savedIndex != _historyIndex && edit.Kind != GitTextEditKind.Other && previous.Kind == edit.Kind;
            _breakMerge = false;
            if (canMerge && edit.Kind == GitTextEditKind.Typing && edit.Removed.Length == 0 && previous.Removed.Length == 0
                && EndOf(previous.Start, previous.Inserted) == edit.Start && StartsWord(previous.Inserted, edit.Inserted) == false)
            {
                previous.Inserted += edit.Inserted;
                previous.AnchorAfter = edit.AnchorAfter;
                previous.CaretAfter = edit.CaretAfter;
                return;
            }
            if (canMerge && edit.Kind == GitTextEditKind.Deleting && edit.Inserted.Length == 0 && previous.Inserted.Length == 0)
            {
                // Backspace: 앞으로 이어 지운다. Delete: 같은 자리에서 뒤를 이어 지운다.
                if (EndOf(edit.Start, edit.Removed) == previous.Start)
                {
                    previous.Start = edit.Start;
                    previous.Removed = edit.Removed + previous.Removed;
                    previous.AnchorAfter = edit.AnchorAfter;
                    previous.CaretAfter = edit.CaretAfter;
                    return;
                }
                if (edit.Start == previous.Start)
                {
                    previous.Removed += edit.Removed;
                    previous.AnchorAfter = edit.AnchorAfter;
                    previous.CaretAfter = edit.CaretAfter;
                    return;
                }
            }
            _history.Add(edit);
            _historyIndex = _history.Count;
        }

        /// <summary>
        /// 공백 뒤에 새 낱말을 치기 시작하면 따로 되돌린다(편집기처럼 낱말 단위로 되돌린다).
        /// </summary>
        private static bool StartsWord(string before, string typed)
        {
            if (before.Length == 0 || typed.Length == 0)
            {
                return false;
            }
            char last = before[before.Length - 1];
            return (last == ' ' || last == '\t') && typed[0] != ' ' && typed[0] != '\t';
        }
    }
}
