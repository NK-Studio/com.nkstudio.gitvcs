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
        public GitGraphLane Lane;
        public bool IsHead;

        /// <summary>이 커밋을 가리키는 참조 중 줄 이름이 아닌 것("origin/main", "tag: v0.1").</summary>
        public List<string> RefLabels = new();
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

        /// <summary>그래프에 없는(기간 밖) 첫 부모를 가진 노드. 왼쪽으로 이어지는 짧은 선을 그린다.</summary>
        public readonly HashSet<GitGraphNode> TruncatedNodes = new();

        public int RowCount { get; private set; }

        /// <param name="commitsNewestFirst"><c>git log --date-order</c> 출력 순서(자식이 항상 부모보다 앞).</param>
        public static GitGraph Build(List<GitCommitInfo> commitsNewestFirst, List<GitBranchRef> branches, string headHash)
        {
            GitGraph graph = new();

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

            graph.AssignNamedLanes(branches);
            graph.AssignAnonymousLanes();
            graph.CollectRefLabels();
            graph.BuildEdges();
            graph.PackRows();
            return graph;
        }

        private void AssignNamedLanes(List<GitBranchRef> branches)
        {
            IEnumerable<GitBranchRef> ordered = branches
                .Where(b => string.IsNullOrEmpty(b.Hash) == false)
                .OrderBy(BranchPriority)
                .ThenByDescending(b => b.Date);

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

        /// <summary>끝 커밋부터 첫 부모를 따라가며 줄이 없는 커밋을 <paramref name="lane"/> 에 넣는다. 하나라도 넣으면 줄을 등록한다.</summary>
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
            if (lane.LastColumn >= 0 && Lanes.Contains(lane) == false)
            {
                lane.ColorIndex = Lanes.Count;
                Lanes.Add(lane);
            }
        }

        private void AssignAnonymousLanes()
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

                string name = GitText.Get("graph.mergedBranch");
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
                    }
                    if (label == "HEAD" || label.EndsWith("/HEAD", StringComparison.Ordinal))
                    {
                        continue;
                    }
                    // 태그는 "tag: v0.1" 그대로 둔다. 그리는 쪽이 접두사로 구분해 색을 달리한다.
                    // 줄 이름과 같은 브랜치는 줄 머리표에 이미 나온다. 끝 커밋이 아닐 때만(줄이 더 이어질 때) 다시 붙인다.
                    if (node.Lane != null && label == node.Lane.Name && node.Column == node.Lane.LastColumn)
                    {
                        continue;
                    }
                    node.RefLabels.Add(label);
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
        /// 줄을 화면 줄에 채운다. 우선순위 순서대로, 기간(+여백)이 겹치지 않는 가장 위 줄에 놓는다.
        /// 브랜치가 많아도 그래프가 아래로만 끝없이 길어지지 않게 한다.
        /// </summary>
        private void PackRows()
        {
            List<List<GitGraphLane>> rows = new();
            foreach (GitGraphLane lane in Lanes)
            {
                // 갈라져 나온 지점(부모)과 같은 칸에 붙지 않도록 앞쪽에 한 칸 여유를 둔다.
                int start = lane.FirstColumn - 1;
                int end = lane.LastColumn + 1;
                int row = 0;
                while (row < rows.Count && rows[row].Any(other => other.FirstColumn - 1 <= end && start <= other.LastColumn + 1))
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
    }
}
