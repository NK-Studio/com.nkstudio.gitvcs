using System.Collections.Generic;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>브랜치 목록 한 줄. <see cref="Branch"/> 가 null 이면 "로컬"·"원격" 구역 제목 줄.</summary>
    public sealed class BranchListItem
    {
        public string Section;
        public GitBranchRef Branch;

        public string Key => Branch?.RefName ?? "section:" + Section;

        /// <summary>현재 브랜치를 맨 위로, 로컬 다음 원격 순서의 줄 목록을 만든다.</summary>
        public static List<BranchListItem> Build(IEnumerable<GitBranchRef> branches)
        {
            List<BranchListItem> locals = new();
            List<BranchListItem> remotes = new();
            foreach (GitBranchRef branch in branches)
            {
                BranchListItem item = new() { Branch = branch };
                if (branch.IsRemote)
                {
                    remotes.Add(item);
                }
                else if (branch.IsCurrent)
                {
                    locals.Insert(0, item);
                }
                else
                {
                    locals.Add(item);
                }
            }

            List<BranchListItem> rows = new() { new BranchListItem { Section = GitText.Get("branches.local") } };
            rows.AddRange(locals);
            if (remotes.Count > 0)
            {
                rows.Add(new BranchListItem { Section = GitText.Get("branches.remote") });
                rows.AddRange(remotes);
            }
            return rows;
        }
    }

    /// <summary>브랜치 목록(<c>BranchRow.uxml</c>). 구역 제목·현재 브랜치는 BEM 수식어로 구분한다.</summary>
    public sealed class BranchList : TemplateList<BranchListItem>
    {
        private const string SectionModifier = "branch-row--section";
        private const string CurrentModifier = "branch-row--current";

        public BranchList(ListView listView) : base(listView, "BranchRow")
        {
        }

        protected override void Bind(VisualElement row, BranchListItem item)
        {
            GitBranchRef branch = item.Branch;
            row.EnableInClassList(SectionModifier, branch == null);
            row.EnableInClassList(CurrentModifier, branch != null && branch.IsCurrent);

            Label name = row.Require<Label>("name");
            if (branch == null)
            {
                name.text = item.Section;
                name.tooltip = string.Empty;
                return;
            }

            name.text = branch.IsCurrent ? $"● {branch.Name}" : branch.Name;
            name.tooltip = branch.IsCurrent ? GitText.Get("branches.current") : branch.RefName;

            Label track = row.Require<Label>("track");
            track.text = GitDisplay.FormatTrack(branch);
            track.tooltip = branch.Upstream;

            Label commit = row.Require<Label>("commit");
            commit.text = $"{branch.ShortHash}  {branch.Subject}";
            commit.tooltip = $"{branch.AuthorName}: {branch.Subject}";

            row.Require<Label>("date").text = GitDisplay.FormatDate(branch.Date);
        }
    }
}
