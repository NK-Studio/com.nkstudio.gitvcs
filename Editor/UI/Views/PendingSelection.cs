using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>
    /// Pending Changes 의 체크 상태 규칙(UI 와 분리된 모델).
    /// - 새 항목은 기본 체크, 해제한 경로만 <see cref="PendingChangesState.ExcludedPaths"/> 에 남긴다.
    /// - 에셋과 .meta 는 함께 체크·해제한다. 하나만 커밋되면 다른 사람의 프로젝트에서 GUID 가 깨진다.
    /// - 목록에서 사라진(커밋·되돌림) 경로의 해제 기록은 버린다.
    /// </summary>
    public sealed class PendingSelection
    {
        private readonly PendingChangesState _state;
        private readonly HashSet<string> _excluded;
        private List<GitStatusEntry> _rows = new();
        private Dictionary<string, GitStatusEntry> _rowByPath = new(StringComparer.OrdinalIgnoreCase);

        public PendingSelection(PendingChangesState state)
        {
            _state = state;
            _excluded = new HashSet<string>(state.ExcludedPaths);
        }

        public IReadOnlyList<GitStatusEntry> Rows => _rows;
        public int CheckedCount => _rows.Count(IsChecked);
        public bool AllChecked => _rows.Count > 0 && CheckedCount == _rows.Count;
        public bool NoneChecked => CheckedCount == 0;

        public void SetRows(IEnumerable<GitStatusEntry> entries)
        {
            _rows = entries.OrderBy(e => e.ProjectPath ?? e.RepoPath, StringComparer.OrdinalIgnoreCase).ToList();
            _rowByPath = _rows.GroupBy(r => r.RepoPath, StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
            _excluded.RemoveWhere(path => _rowByPath.ContainsKey(path) == false);
            Save();
        }

        public bool IsChecked(GitStatusEntry entry)
        {
            return _excluded.Contains(entry.RepoPath) == false;
        }

        public void SetChecked(GitStatusEntry entry, bool value)
        {
            Apply(entry.RepoPath, value);
            GitStatusEntry pair = FindMetaPair(entry);
            if (pair != null)
            {
                Apply(pair.RepoPath, value);
            }
            Save();
        }

        /// <summary>여러 항목을 한 번에(트리의 폴더 체크). .meta 짝도 함께 바뀐다.</summary>
        public void SetChecked(IEnumerable<GitStatusEntry> entries, bool value)
        {
            foreach (GitStatusEntry entry in entries)
            {
                Apply(entry.RepoPath, value);
                GitStatusEntry pair = FindMetaPair(entry);
                if (pair != null)
                {
                    Apply(pair.RepoPath, value);
                }
            }
            Save();
        }

        public void SetAll(bool value)
        {
            _excluded.Clear();
            if (value == false)
            {
                _excluded.UnionWith(_rows.Select(r => r.RepoPath));
            }
            Save();
        }

        public List<GitStatusEntry> CheckedEntries()
        {
            return _rows.Where(IsChecked).ToList();
        }

        public GitStatusEntry FindMetaPair(GitStatusEntry entry)
        {
            string pairPath = entry.RepoPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                ? entry.RepoPath.Substring(0, entry.RepoPath.Length - 5)
                : entry.RepoPath + ".meta";
            return _rowByPath.TryGetValue(pairPath, out GitStatusEntry pair) ? pair : null;
        }

        private void Apply(string path, bool value)
        {
            if (value)
            {
                _excluded.Remove(path);
            }
            else
            {
                _excluded.Add(path);
            }
        }

        private void Save()
        {
            _state.ExcludedPaths = _excluded.ToList();
        }
    }
}
