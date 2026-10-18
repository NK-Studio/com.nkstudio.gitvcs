using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>커밋 목록(<c>CommitRow.uxml</c>): 해시, 브랜치·태그 이름표, 메시지, 작성자, 날짜.</summary>
    public sealed class CommitList : TemplateList<GitCommitInfo>
    {
        private const string RefsHiddenModifier = "commit-row__refs--hidden";

        private readonly IAssetNavigator _assets;

        public CommitList(ListView listView, IAssetNavigator assets) : base(listView, "CommitRow")
        {
            _assets = assets;
        }

        protected override void Bind(VisualElement row, GitCommitInfo commit)
        {
            row.Require<Label>("hash").text = commit.ShortHash;

            Label refs = row.Require<Label>("refs");
            refs.text = string.IsNullOrEmpty(commit.Refs) ? string.Empty : $"[{GitDisplay.Nfc(commit.Refs)}]";
            refs.EnableInClassList(RefsHiddenModifier, string.IsNullOrEmpty(commit.Refs));

            Label subject = row.Require<Label>("subject");
            subject.text = commit.IsMerge ? GitText.Format("commit.merge", commit.Subject) : commit.Subject;
            subject.tooltip = commit.Subject;

            Label author = row.Require<Label>("author");
            author.text = commit.AuthorName;
            author.tooltip = commit.AuthorEmail;

            row.Require<Label>("date").text = GitDisplay.FormatDate(commit.Date);
        }

        /// <summary>
        /// 메뉴 맨 위에 해시·메시지 복사를 넣을지. 전체 커밋 메뉴(<see cref="CommitMenu"/>)를 붙이는 곳은 끈다(그 메뉴의 "고급 옵션" 에 있다).
        /// </summary>
        public bool ShowCopyItems { get; set; } = true;

        protected override void PopulateContextMenu(DropdownMenu menu, GitCommitInfo commit)
        {
            if (ShowCopyItems)
            {
                menu.AppendAction(GitText.Get("common.copyHash"), _ => _assets.CopyToClipboard(commit.Hash));
                menu.AppendAction(GitText.Get("common.copySubject"), _ => _assets.CopyToClipboard(commit.Subject));
            }
            base.PopulateContextMenu(menu, commit);
        }
    }
}
