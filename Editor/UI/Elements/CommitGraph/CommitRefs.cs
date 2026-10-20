using System;
using System.Collections.Generic;

namespace GitVersionControl
{
    public enum CommitRefKind
    {
        /// <summary>
        /// 지금 체크아웃한 브랜치.
        /// </summary>
        Current,
        Local,
        Remote,
        Tag,
    }

    public readonly struct CommitRef
    {
        public readonly string Name;
        public readonly CommitRefKind Kind;

        public CommitRef(string name, CommitRefKind kind)
        {
            Name = name;
            Kind = kind;
        }
    }

    /// <summary>
    /// 세로 그래프의 브랜치/태그 칸에 보일 참조. 지금 체크아웃한 브랜치가 맨 앞(GitKraken 과 같다 — 줄에는 체크 표시 이름표 하나와 "+n"),
    /// 그다음 이 커밋이 끝인 브랜치 줄 이름, 같은 커밋을 가리키는 다른 참조(<c>git log %D</c>: "origin/main", "tag: v1.0") 순서.
    /// 원격은 "원격이름/브랜치" 꼴이라 줄 정보가 없을 때는 알려진 원격 이름으로 가른다.
    /// </summary>
    public static class CommitRefs
    {
        private static readonly string[] RemotePrefixes = { "origin/", "upstream/" };

        public static IReadOnlyList<CommitRef> Of(GitGraphNode node)
        {
            List<CommitRef> refs = new();
            if (node.Column == node.Lane.LastColumn && node.Lane.IsAnonymous == false)
            {
                CommitRefKind kind = node.Lane.IsCurrent ? CommitRefKind.Current : node.Lane.IsRemote ? CommitRefKind.Remote : CommitRefKind.Local;
                refs.Add(new CommitRef(node.Lane.Name, kind));
            }
            foreach (string label in node.RefLabels)
            {
                // 줄 이름이 아닌 현재 브랜치("HEAD -> feature/x" 인데 줄은 main)도 현재 브랜치로 보인다.
                refs.Add(label == node.HeadBranch ? new CommitRef(label, CommitRefKind.Current) : Classify(label));
            }
            int current = refs.FindIndex(r => r.Kind == CommitRefKind.Current);
            if (current > 0)
            {
                CommitRef head = refs[current];
                refs.RemoveAt(current);
                refs.Insert(0, head);
            }
            return refs;
        }

        public static CommitRef Classify(string label)
        {
            if (label.StartsWith("tag: ", StringComparison.Ordinal))
            {
                return new CommitRef(label.Substring(5), CommitRefKind.Tag);
            }
            foreach (string prefix in RemotePrefixes)
            {
                if (label.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return new CommitRef(label, CommitRefKind.Remote);
                }
            }
            return new CommitRef(label, CommitRefKind.Local);
        }
    }
}
