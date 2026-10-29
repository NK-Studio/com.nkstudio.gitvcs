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

        /// <summary>로컬(현재) 브랜치인데 같은 이름의 원격 브랜치도 이 커밋을 가리킨다 — 이름표 하나에 노트북과 구름을 함께(GitKraken).</summary>
        public readonly bool HasRemote;

        public CommitRef(string name, CommitRefKind kind, bool hasRemote = false)
        {
            Name = name;
            Kind = kind;
            HasRemote = hasRemote;
        }

        /// <summary>
        /// 이름표에 보일 이름. 원격은 앞의 원격 이름을 뺀다("github/feature/x" → "feature/x") — 구름 아이콘이 원격임을 알려 준다(GitKraken).
        /// 전환 등 동작에는 <see cref="Name"/> 을 쓴다.
        /// </summary>
        public string DisplayName => Kind == CommitRefKind.Remote ? CommitRefs.WithoutRemote(Name) : Name;
    }

    /// <summary>
    /// 세로 그래프의 브랜치/태그 칸에 보일 참조. 지금 체크아웃한 브랜치가 맨 앞(GitKraken 과 같다 — 줄에는 체크 표시 이름표 하나와 "+n"),
    /// 그다음 이 커밋이 끝인 브랜치 줄 이름, 같은 커밋을 가리키는 다른 참조(<c>git log %D</c>: "origin/main", "tag: v1.0") 순서.
    /// 원격은 "원격이름/브랜치" 꼴이라 줄 정보가 없을 때는 알려진 원격 이름으로 가른다.
    /// </summary>
    public static class CommitRefs
    {
        private static readonly string[] RemotePrefixes = { "origin/", "upstream/" };

        /// <param name="mergeRemotes">
        /// 로컬(현재) 브랜치와 같은 커밋을 가리키는 같은 이름의 원격 브랜치("github/main" ← "main")를 따로 세지 않고 로컬 이름표에 합친다(<see cref="CommitRef.HasRemote"/>).
        /// GitKraken 처럼 이름표 하나에 노트북·구름, "+n" 은 남은 참조만. 커밋 상세처럼 모두 늘어놓을 곳은 false.
        /// </param>
        public static IReadOnlyList<CommitRef> Of(GitGraphNode node, bool mergeRemotes = true)
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
                refs.Add(label == node.HeadBranch ? new CommitRef(label, CommitRefKind.Current)
                    : node.RemoteRefLabels.Contains(label) ? new CommitRef(label, CommitRefKind.Remote)
                    : Classify(label));
            }
            if (mergeRemotes)
            {
                MergeRemotes(refs);
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

        /// <summary>로컬·현재 브랜치마다 "원격/같은이름" 원격 참조를 지우고 그 로컬 이름표에 HasRemote 를 켠다.</summary>
        private static void MergeRemotes(List<CommitRef> refs)
        {
            for (int i = 0; i < refs.Count; i++)
            {
                CommitRef local = refs[i];
                if (local.Kind is not (CommitRefKind.Local or CommitRefKind.Current))
                {
                    continue;
                }
                string suffix = "/" + local.Name;
                int removed = refs.RemoveAll(r => r.Kind == CommitRefKind.Remote && r.Name.EndsWith(suffix, StringComparison.Ordinal));
                if (removed > 0)
                {
                    // 지운 원격이 이 앞에 있었으면 자리가 당겨졌다.
                    i = refs.FindIndex(r => r.Name == local.Name && r.Kind == local.Kind);
                    refs[i] = new CommitRef(local.Name, local.Kind, hasRemote: true);
                }
            }
        }

        /// <summary>"원격/브랜치" 에서 원격 이름(첫 마디)을 뺀다. 마디가 하나면 그대로.</summary>
        public static string WithoutRemote(string remoteBranch)
        {
            int slash = remoteBranch.IndexOf('/');
            return slash > 0 && slash < remoteBranch.Length - 1 ? remoteBranch.Substring(slash + 1) : remoteBranch;
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
