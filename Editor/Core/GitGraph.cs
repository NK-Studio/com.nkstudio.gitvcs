using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GitVersionControl
{
    public sealed class GitGraphNode
    {
        public GitCommitInfo Commit;

        /// <summary>왼쪽(오래됨)부터 0. 화면 x 위치.</summary>
        public int Column;

        /// <summary>
        /// 그리는 가지(열·색). 세로 그래프에서는 GitKraken 규칙으로 정한 "한 열에 곧게 이어진 첫 부모 줄"이라 <see cref="BranchLane"/> 과 다를 수 있다.
        /// </summary>
        public GitGraphLane Lane;

        /// <summary>
        /// 이 커밋이 든 브랜치 줄(이름·종류). 가지 끝 이름표와 이름표 정리에 쓴다. 가로 탐색기에서는 <see cref="Lane"/> 과 같다.
        /// </summary>
        public GitGraphLane BranchLane;
        public bool IsHead;

        /// <summary>이 커밋을 가리키는 참조 중 줄 이름이 아닌 것("origin/main", "tag: v0.1").</summary>
        public List<string> RefLabels = new();

        /// <summary>HEAD 가 이 커밋에서 가리키는 브랜치 이름(<c>%D</c> 의 "HEAD -> main"). 아니면 null.</summary>
        public string HeadBranch;

        /// <summary><see cref="RefLabels"/> 중 원격 브랜치인 것(브랜치 목록의 원격 이름으로 가른다 — "github/main" 처럼 origin 이 아닌 원격도).</summary>
        public HashSet<string> RemoteRefLabels = new();
    }

    public sealed class GitGraphLane
    {
        public string Name;

        /// <summary>화면 줄 번호. 기간이 겹치지 않는 줄끼리는 같은 줄을 나눠 쓴다.</summary>
        public int Row;
        public int ColorIndex;
        public int FirstColumn = int.MaxValue;
        public int LastColumn = -1;
        public bool IsRemote;
        public bool IsCurrent;

        /// <summary>이름 있는 브랜치가 아니라 병합된 뒤 지워진 브랜치에서 온 줄.</summary>
        public bool IsAnonymous;

        /// <summary>stash 하나만 든 줄(이름 "stash@{0}"). 브랜치 줄 배치를 바꾸지 않는다.</summary>
        public bool IsStash;
    }

    public sealed class GitGraphEdge
    {
        public GitGraphNode Parent;
        public GitGraphNode Child;

        /// <summary>병합 커밋의 두 번째 이후 부모(다른 브랜치가 들어오는 선).</summary>
        public bool IsMergeEdge;
    }

    /// <summary>
    /// Unity Version Control 의 Branch Explorer 처럼 커밋을 "브랜치마다 한 줄, 시간은 왼쪽→오른쪽" 으로 배치한다.
    ///
    /// git 커밋에는 소속 브랜치가 없으므로 우선순위 순서로 브랜치 끝에서 첫 부모를 따라가며 아직 줄이 없는 커밋을 그 브랜치 줄에 넣는다
    /// (main/master → 현재 브랜치 → 다른 로컬 → 원격). 이렇게 하면 main 에서 갈라진 feature 는 갈라진 지점 이후만 feature 줄에 놓인다.
    /// 어느 브랜치에서도 첫 부모로 닿지 않는 커밋(병합 뒤 지워진 브랜치)은 이름 없는 줄에 모으고, 병합 커밋 메시지에서 이름을 추측한다.
    /// </summary>
    public sealed class GitGraph
    {
        private static readonly Regex MergeBranchPattern = new(@"Merge (?:remote-tracking )?branch '([^']+)'", RegexOptions.Compiled);

        public readonly List<GitGraphNode> Nodes = new();
        public readonly List<GitGraphLane> Lanes = new();
        public readonly List<GitGraphEdge> Edges = new();
        public readonly Dictionary<string, GitGraphNode> NodeByHash = new();

        /// <summary>그래프를 만들 때 쓴 브랜치 목록. 커밋 우클릭 메뉴가 그 커밋을 가리키는 브랜치를 찾는다.</summary>
        public readonly List<GitBranchRef> Branches = new();

        /// <summary>그래프에 없는(기간 밖) 첫 부모를 가진 노드. 왼쪽으로 이어지는 짧은 선을 그린다.</summary>
        public readonly HashSet<GitGraphNode> TruncatedNodes = new();

        public int RowCount { get; private set; }

        /// <summary>
        /// 메인 스레드에서 부른다(이름 없는 줄 이름을 <see cref="GitText"/> 에서 읽는다). 다른 스레드에서는 이름을 넘기는 오버로드를 쓴다.
        /// </summary>
        /// <param name="commitsNewestFirst"><c>git log --date-order</c> 출력 순서(자식이 항상 부모보다 앞).</param>
        public static GitGraph Build(List<GitCommitInfo> commitsNewestFirst, List<GitBranchRef> branches, string headHash)
        {
            return Build(commitsNewestFirst, branches, headHash, GitText.Get("graph.mergedBranch"));
        }

        /// <summary>
        /// 어느 스레드에서나 부를 수 있다. 커밋·브랜치가 수천 개면 수십 ms 가 걸리므로 창은 작업 스레드에서 만든다.
        /// </summary>
        /// <param name="anonymousLaneName">병합 메시지에서 이름을 찾지 못한 줄의 이름.</param>
        /// <param name="topDown">
        /// 세로 그래프(GitKraken): GitKraken 과 같은 규칙으로 위(새 커밋)부터 한 줄씩 열을 주고 가지를 다시 짠다(<see cref="AssignTopDownColumns"/>).
        /// 브랜치 줄(이름)은 <see cref="GitGraphNode.BranchLane"/> 에 남는다. false 면 main 이 앞이다(Branch Explorer).
        /// </param>
        /// <param name="headFirst">
        /// <paramref name="topDown"/> 에서 지금 브랜치를 맨 위 커밋처럼 친다 — 작업 중 줄(WIP)이 그래프 맨 위에서 지금 브랜치에 이어질 때(GitKraken 과 같다).
        /// </param>
        public static GitGraph Build(List<GitCommitInfo> commitsNewestFirst, List<GitBranchRef> branches, string headHash, string anonymousLaneName,
            bool topDown = false, bool headFirst = false)
        {
            GitGraph graph = new();
            graph.Branches.AddRange(branches);

            for (int i = commitsNewestFirst.Count - 1; i >= 0; i--)
            {
                GitCommitInfo commit = commitsNewestFirst[i];
                GitGraphNode node = new()
                {
                    Commit = commit,
                    Column = graph.Nodes.Count,
                    IsHead = commit.Hash == headHash,
                };
                graph.Nodes.Add(node);
                graph.NodeByHash[commit.Hash] = node;
            }

            graph.AssignStashLanes();
            graph.AssignNamedLanes(branches, topDown, headFirst);
            graph.AssignAnonymousLanes(anonymousLaneName);
            foreach (GitGraphNode node in graph.Nodes)
            {
                node.BranchLane = node.Lane;
            }
            graph.CollectRefLabels();
            graph.BuildEdges();
            if (topDown)
            {
                graph.AssignTopDownColumns(branches, headFirst);
            }
            else
            {
                graph.PackRows();
            }
            return graph;
        }

        /// <summary>원격 브랜치 이름("github/main"). 참조 꼬리표가 원격인지 가른다.</summary>
        private readonly HashSet<string> _remoteBranchNames = new();

        /// <summary>stash 는 저마다 제 줄 하나만 갖는다(첫 부모로 이어 가지 않는다 — 만든 자리부터는 브랜치 줄이 갖는다).</summary>
        private void AssignStashLanes()
        {
            foreach (GitGraphNode node in Nodes.Where(node => node.Commit.IsStash))
            {
                GitGraphLane lane = new()
                {
                    Name = node.Commit.StashName,
                    IsStash = true,
                    FirstColumn = node.Column,
                    LastColumn = node.Column,
                    ColorIndex = StableColorIndex(node.Commit.StashName, Lanes.Count),
                };
                node.Lane = lane;
                Lanes.Add(lane);
            }
        }

        private void AssignNamedLanes(List<GitBranchRef> branches, bool topDown, bool headFirst)
        {
            foreach (GitBranchRef branch in branches.Where(b => b.IsRemote))
            {
                _remoteBranchNames.Add(branch.Name);
            }
            // 먼저 놓인 브랜치가 첫 부모를 따라가며 공통 조상(줄기)을 차지한다. 세로 그래프는 GitKraken 처럼 끝 커밋이 위에 있는 브랜치부터
            // (작업 중 줄이 있으면 지금 브랜치부터), Branch Explorer 는 main·지금 브랜치부터.
            IEnumerable<GitBranchRef> withTip = branches.Where(b => string.IsNullOrEmpty(b.Hash) == false);
            IEnumerable<GitBranchRef> ordered = topDown
                ? withTip.OrderBy(b => headFirst && b.IsCurrent && b.IsRemote == false ? 0 : 1)
                    .ThenByDescending(TipColumn)
                    .ThenBy(BranchPriority)
                : withTip.OrderBy(BranchPriority).ThenByDescending(b => b.Date);

            foreach (GitBranchRef branch in ordered)
            {
                GitGraphLane lane = new()
                {
                    Name = branch.Name,
                    IsRemote = branch.IsRemote,
                    IsCurrent = branch.IsCurrent,
                };
                WalkFirstParents(branch.Hash, lane);
            }
        }

        /// <summary>브랜치 끝 커밋의 칸(클수록 새 커밋 = 세로 그래프에서 위). 읽은 범위 밖이면 -1.</summary>
        private int TipColumn(GitBranchRef branch)
        {
            return NodeByHash.TryGetValue(branch.Hash, out GitGraphNode node) ? node.Column : -1;
        }

        private static int BranchPriority(GitBranchRef branch)
        {
            bool isMainName = branch.Name == "main" || branch.Name == "master";
            if (branch.IsRemote == false && isMainName)
            {
                return 0;
            }
            if (branch.IsCurrent)
            {
                return 1;
            }
            if (branch.IsRemote == false)
            {
                return 2;
            }
            return branch.Name.EndsWith("/main", StringComparison.Ordinal) || branch.Name.EndsWith("/master", StringComparison.Ordinal) ? 3 : 4;
        }

        /// <summary>
        /// 끝 커밋부터 첫 부모를 따라가며 줄이 없는 커밋을 <paramref name="lane"/>(매번 새 줄)에 넣는다. 하나라도 넣으면 줄을 등록한다.
        /// </summary>
        private void WalkFirstParents(string tipHash, GitGraphLane lane)
        {
            string hash = tipHash;
            while (hash != null && NodeByHash.TryGetValue(hash, out GitGraphNode node) && node.Lane == null)
            {
                node.Lane = lane;
                lane.FirstColumn = Math.Min(lane.FirstColumn, node.Column);
                lane.LastColumn = Math.Max(lane.LastColumn, node.Column);
                hash = node.Commit.Parents.Length > 0 ? node.Commit.Parents[0] : null;
            }
            if (lane.LastColumn >= 0)
            {
                lane.ColorIndex = StableColorIndex(lane.Name, Lanes.Count);
                Lanes.Add(lane);
            }
        }

        /// <summary>
        /// 줄 색 번호. 브랜치 이름에서 정해 새 커밋·새 브랜치가 생겨도 같은 브랜치는 같은 색을 지킨다(만든 순서로 매기면 매번 뒤섞인다).
        /// 원격 브랜치는 로컬과 같은 색("origin/main" → "main"). 이름이 없으면 순서를 쓴다.
        /// </summary>
        public static int StableColorIndex(string laneName, int fallback)
        {
            if (string.IsNullOrEmpty(laneName))
            {
                return fallback;
            }
            string name = laneName;
            int slash = name.IndexOf('/');
            if (name.StartsWith("origin/", StringComparison.Ordinal) && slash >= 0)
            {
                name = name.Substring(slash + 1);
            }
            unchecked
            {
                int hash = 17;
                foreach (char c in name)
                {
                    hash = hash * 31 + c;
                }
                return hash & 0x7fffffff;
            }
        }

        private void AssignAnonymousLanes(string anonymousLaneName)
        {
            // 병합 커밋의 두 번째 부모 → 그 병합 커밋. 이름 없는 줄의 이름을 병합 메시지에서 찾는다.
            Dictionary<string, GitGraphNode> mergedInto = new();
            foreach (GitGraphNode node in Nodes)
            {
                for (int p = 1; p < node.Commit.Parents.Length; p++)
                {
                    mergedInto[node.Commit.Parents[p]] = node;
                }
            }

            // 새 커밋부터 보아야 병합된 가지의 끝에서 시작해 가지 전체가 한 줄에 들어간다.
            for (int i = Nodes.Count - 1; i >= 0; i--)
            {
                GitGraphNode node = Nodes[i];
                if (node.Lane != null)
                {
                    continue;
                }

                string name = anonymousLaneName;
                if (mergedInto.TryGetValue(node.Commit.Hash, out GitGraphNode merge))
                {
                    Match match = MergeBranchPattern.Match(merge.Commit.Subject);
                    if (match.Success)
                    {
                        name = match.Groups[1].Value;
                    }
                }
                WalkFirstParents(node.Commit.Hash, new GitGraphLane { Name = name, IsAnonymous = true });
            }
        }

        private void CollectRefLabels()
        {
            foreach (GitGraphNode node in Nodes)
            {
                if (string.IsNullOrEmpty(node.Commit.Refs))
                {
                    continue;
                }
                foreach (string raw in node.Commit.Refs.Split(','))
                {
                    string label = raw.Trim();
                    if (label.StartsWith("HEAD -> ", StringComparison.Ordinal))
                    {
                        label = label.Substring(8);
                        node.HeadBranch = label;
                    }
                    if (label == "HEAD" || label.EndsWith("/HEAD", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // 태그는 "tag: v0.1" 그대로 둔다. 그리는 쪽이 접두사로 구분해 색을 달리한다.
                    // 줄 이름과 같은 브랜치는 줄 머리표에 이미 나온다. 끝 커밋이 아닐 때만(줄이 더 이어질 때) 다시 붙인다.
                    if (node.BranchLane != null && label == node.BranchLane.Name && node.Column == node.BranchLane.LastColumn)
                    {
                        continue;
                    }
                    node.RefLabels.Add(label);
                    if (_remoteBranchNames.Contains(label))
                    {
                        node.RemoteRefLabels.Add(label);
                    }
                }
            }
        }

        private void BuildEdges()
        {
            foreach (GitGraphNode child in Nodes)
            {
                string[] parents = child.Commit.Parents;
                for (int p = 0; p < parents.Length; p++)
                {
                    if (NodeByHash.TryGetValue(parents[p], out GitGraphNode parent))
                    {
                        Edges.Add(new GitGraphEdge { Parent = parent, Child = child, IsMergeEdge = p > 0 });
                    }
                    else if (p == 0)
                    {
                        TruncatedNodes.Add(child);
                    }
                }
            }
        }

        /// <summary>
        /// 가로 탐색기: 줄을 화면 줄에 채운다. 우선순위 순서대로, 기간(+여백)이 겹치지 않는 가장 위 줄에 놓는다.
        /// 브랜치가 많아도 그래프가 아래로만 끝없이 길어지지 않게 한다. 세로 그래프는 <see cref="AssignTopDownColumns"/>.
        /// </summary>
        private void PackRows()
        {
            List<List<GitGraphLane>> rows = new();
            // 줄이 차지하는 기간. 갈라져 나온 지점(부모)과 같은 칸에 붙지 않도록 앞뒤로 한 칸 여유를 둔다.
            // 가지 선은 갈라진 부모·들어가는 병합 커밋까지 그 줄 위로 길게 그어지므로 그 칸까지 친다 —
            // 그러지 않으면 짧은 가지가 같은 줄을 나눠 써서 선이 남의 커밋·머리표를 뚫고 지나가 보인다.
            Dictionary<GitGraphLane, (int start, int end)> spans = Lanes.ToDictionary(lane => lane, lane => (lane.FirstColumn - 1, lane.LastColumn + 1));
            foreach (GitGraphEdge edge in Edges)
            {
                if (edge.Parent.Lane == edge.Child.Lane)
                {
                    continue;
                }
                foreach (GitGraphLane lane in new[] { edge.Parent.Lane, edge.Child.Lane })
                {
                    (int start, int end) span = spans[lane];
                    spans[lane] = (Math.Min(span.start, edge.Parent.Column), Math.Max(span.end, edge.Child.Column));
                }
            }
            foreach (GitGraphLane lane in Lanes)
            {
                (int start, int end) = spans[lane];
                int row = 0;
                while (row < rows.Count && rows[row].Any(other => spans[other].start <= end && start <= spans[other].end))
                {
                    row++;
                }
                if (row == rows.Count)
                {
                    rows.Add(new List<GitGraphLane>());
                }
                rows[row].Add(lane);
                lane.Row = row;
            }
            RowCount = rows.Count;
        }

        /// <summary>
        /// 세로 그래프의 열 배정. GitKraken(<c>@gitkraken/gitkraken-components</c> 의 <c>getColumns</c>)과 같은 규칙이다.
        /// <list type="bullet">
        /// <item>위(새 커밋)에서부터 한 줄씩 본다. 자식이 잡아 둔 열이 있으면 그 열, 없으면(브랜치 끝) 가장 왼쪽 빈 열을 쓴다.</item>
        /// <item>첫 부모는 내 열에 잡아 둔다. 두 번째 이후 부모(병합)는 병합 줄에서 바로 가장 왼쪽 빈 열을 잡는다.</item>
        /// <item>첫 부모가 이미 다른 자식의 열에 잡혀 있으면 더 왼쪽 열이 부모를 갖고(부모를 병합하는 자식이 있으면 옮기지 않는다),
        /// 다른 열은 부모 줄에서 비운다 — 그 열의 선은 부모 줄에서 꺾여 들어간다. 빈 열은 바로 다음 줄부터 다시 쓴다.</item>
        /// <item>stash 는 브랜치를 다 놓은 뒤, 상자에서 만든 자리까지 빈 가장 왼쪽 열에 놓는다. 만든 자리가 그 열의 맨 위면 그 열을 이어 쓴다.</item>
        /// <item><paramref name="headFirst"/> 면 맨 위 작업 중 줄의 부모처럼 지금 브랜치 끝에 0 열을 위에서부터 잡아 둔다.</item>
        /// </list>
        /// 가지(<see cref="GitGraphLane"/>)는 한 열에 곧게 이어진 첫 부모 줄 하나로 다시 짠다. 이름·종류는 그 줄 맨 위 커밋의 <see cref="GitGraphNode.BranchLane"/> 을 따르고,
        /// 색은 열 번호(<see cref="GitGraphLane.Row"/>)다.
        /// </summary>
        private void AssignTopDownColumns(List<GitBranchRef> branches, bool headFirst)
        {
            // 줄 번호: 새 커밋이 0.
            List<GitGraphNode> rows = Nodes.OrderByDescending(node => node.Column).ToList();
            Dictionary<GitGraphNode, int> rowOf = new();
            for (int i = 0; i < rows.Count; i++)
            {
                rowOf[rows[i]] = i;
            }
            ColumnSpans columns = new();
            Dictionary<GitGraphNode, (int column, GitGraphLane segment)> reserved = new();
            Dictionary<GitGraphNode, List<int>> freeWhenFound = new();
            HashSet<GitGraphNode> mergeChild = new();
            List<GitGraphLane> segments = new();

            GitGraphLane NewSegment(GitGraphNode node, int column)
            {
                GitGraphLane owner = node.BranchLane;
                GitGraphLane lane = new()
                {
                    Name = owner?.Name,
                    Row = column,
                    ColorIndex = column,
                    IsRemote = owner?.IsRemote ?? false,
                    IsCurrent = owner?.IsCurrent ?? false,
                    IsAnonymous = owner?.IsAnonymous ?? false,
                    IsStash = owner?.IsStash ?? false,
                    FirstColumn = node.Column,
                    LastColumn = node.Column,
                };
                segments.Add(lane);
                return lane;
            }

            if (headFirst)
            {
                GitBranchRef current = branches.FirstOrDefault(b => b.IsCurrent && b.IsRemote == false && string.IsNullOrEmpty(b.Hash) == false);
                if (current != null && NodeByHash.TryGetValue(current.Hash, out GitGraphNode head))
                {
                    columns.Take(0, -1);
                    reserved[head] = (0, null);
                }
            }

            for (int row = 0; row < rows.Count; row++)
            {
                GitGraphNode node = rows[row];
                if (node.Commit.IsStash)
                {
                    continue;
                }
                if (freeWhenFound.Remove(node, out List<int> toFree))
                {
                    foreach (int free in toFree)
                    {
                        columns.Free(free, row);
                    }
                }
                int column;
                GitGraphLane segment;
                if (reserved.Remove(node, out (int column, GitGraphLane segment) r))
                {
                    (column, segment) = r;
                }
                else
                {
                    column = columns.TakeFree(row);
                    segment = null;
                }
                segment ??= NewSegment(node, column);
                segment.FirstColumn = node.Column;
                node.Lane = segment;

                bool merge = node.Commit.IsMerge;
                string[] parents = node.Commit.Parents;
                for (int i = 0; i < parents.Length; i++)
                {
                    // 읽은 범위 밖의 부모: 첫 부모면 이 열을 끝까지 쓴다(아래로 흐린 선). 병합 부모는 선이 없다.
                    if (NodeByHash.TryGetValue(parents[i], out GitGraphNode parent) == false)
                    {
                        continue;
                    }
                    if (merge)
                    {
                        mergeChild.Add(parent);
                    }
                    bool isReserved = reserved.TryGetValue(parent, out (int column, GitGraphLane segment) existing);
                    if (isReserved && i == 0 && existing.column != column)
                    {
                        if (freeWhenFound.TryGetValue(parent, out List<int> list) == false)
                        {
                            list = new List<int>();
                            freeWhenFound[parent] = list;
                        }
                        if (existing.column > column && mergeChild.Contains(parent) == false)
                        {
                            reserved[parent] = (column, segment);
                            list.Add(existing.column);
                        }
                        else
                        {
                            list.Add(column);
                        }
                    }
                    else if (isReserved == false)
                    {
                        reserved[parent] = i == 0 ? (column, segment) : (columns.TakeFree(row), null);
                    }
                }
            }
            columns.Finish();

            // stash: 새 것(위)부터, 상자 줄에서 만든 자리 줄까지 빈 열에.
            for (int row = 0; row < rows.Count; row++)
            {
                GitGraphNode node = rows[row];
                if (node.Commit.IsStash == false)
                {
                    continue;
                }
                GitGraphNode baseNode = node.Commit.Parents.Length > 0 && NodeByHash.TryGetValue(node.Commit.Parents[0], out GitGraphNode b) ? b : null;
                // 만든 자리가 읽은 범위 밖이면 상자 줄만 쓴다(아래로는 짧은 흐린 선뿐).
                int end = baseNode != null ? rowOf[baseNode] : row;
                int column = -1;
                if (baseNode != null)
                {
                    int baseColumn = baseNode.Lane.Row;
                    (int start, int end)? baseSpan = columns.SpanStartingAt(baseColumn, end);
                    if (baseSpan.HasValue && columns.IsFree(baseColumn, row, end, baseSpan))
                    {
                        column = baseColumn;
                    }
                }
                if (column < 0)
                {
                    column = 0;
                    while (columns.IsFree(column, row, end, null) == false)
                    {
                        column++;
                    }
                }
                columns.Take(column, row);
                columns.Free(column, end);
                node.Lane = NewSegment(node, column);
            }

            Lanes.Clear();
            Lanes.AddRange(segments);
            RowCount = Math.Max(1, columns.Count);
        }

        /// <summary>세로 그래프의 열마다 쓰이는 기간(줄 번호, 새 커밋이 0, 양 끝 포함).</summary>
        private sealed class ColumnSpans
        {
            private readonly List<int?> _open = new();
            private readonly List<List<(int start, int end)>> _spans = new();

            public int Count => _spans.Count;

            /// <summary>가장 왼쪽 빈 열을 이 줄부터 쓴다.</summary>
            public int TakeFree(int row)
            {
                int column = _open.FindIndex(start => start.HasValue == false);
                if (column < 0)
                {
                    column = _open.Count;
                }
                Take(column, row);
                return column;
            }

            public void Take(int column, int row)
            {
                while (_open.Count <= column)
                {
                    _open.Add(null);
                    _spans.Add(new List<(int, int)>());
                }
                _open[column] = row;
            }

            /// <summary>열을 이 줄까지 쓰고 비운다.</summary>
            public void Free(int column, int row)
            {
                if (column < _open.Count && _open[column] is int start)
                {
                    _spans[column].Add((start, row));
                    _open[column] = null;
                }
            }

            /// <summary>아직 쓰는 열은 끝까지 쓴 것으로 닫는다.</summary>
            public void Finish()
            {
                for (int column = 0; column < _open.Count; column++)
                {
                    Free(column, int.MaxValue);
                }
            }

            public (int start, int end)? SpanStartingAt(int column, int row)
            {
                if (column >= _spans.Count)
                {
                    return null;
                }
                foreach ((int start, int end) span in _spans[column])
                {
                    if (span.start == row)
                    {
                        return span;
                    }
                }
                return null;
            }

            /// <summary><paramref name="start"/>~<paramref name="end"/> 줄 동안 이 열이 비었는가. <paramref name="except"/> 기간은 빼고 본다.</summary>
            public bool IsFree(int column, int start, int end, (int start, int end)? except)
            {
                if (column >= _spans.Count)
                {
                    return true;
                }
                foreach ((int start, int end) span in _spans[column])
                {
                    if (except.HasValue && span == except.Value)
                    {
                        continue;
                    }
                    if (span.start <= end && start <= span.end)
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
