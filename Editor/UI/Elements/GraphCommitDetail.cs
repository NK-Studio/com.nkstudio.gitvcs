using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 형태 Graph 탭 오른쪽의 커밋 상세(<c>GraphCommitDetail.uxml</c>).
    /// 위에서부터 "commit: 해시"(누르면 복사) → 메시지 카드(제목을 크게, 본문은 아래) → 작성자 아바타·이름·날짜와 부모 링크 →
    /// 공동 작성자(Co-Authored-By) → 바뀐 파일 수(종류별 색) → 파일 목록(경로·트리).
    /// 파일을 고르면 <see cref="FileSelected"/> 로 알려 가운데에 diff 를 연다. 다른 탭은 <see cref="CommitDetailPanel"/> 을 쓴다.
    /// </summary>
    public sealed class GraphCommitDetail : IDisposable
    {
        private static readonly Regex CoAuthorPattern = new(@"^\s*Co-Authored-By:\s*(.+?)\s*<([^>]*)>\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        private readonly IGitHistoryService _history;
        private readonly IAvatarProvider _avatars;
        private readonly IAssetNavigator _assets;
        private readonly Label _hash;
        private readonly Label _placeholder;
        private readonly VisualElement _body;
        private readonly Label _subject;
        private readonly Label _message;
        private readonly VisualElement _avatar;
        private readonly Label _initials;
        private readonly Label _author;
        private readonly Label _authored;
        private readonly VisualElement _parents;
        private readonly VisualElement _coAuthorsRow;
        private readonly VisualElement _coAuthors;
        private readonly VisualElement _summary;
        private readonly FileBrowser _files;
        private readonly VisualElement _filesHost;
        // 바뀐 파일을 읽는 동안 파일 칸을 덮는 스켈레톤. 덮지 않으면 빈 목록의 "List is empty" 가 잠깐 보였다.
        private VisualElement _filesCover;
        // 바뀐 파일이 없는 커밋(빈 병합 등)에서 ListView 기본 "List is empty" 대신 보이는 안내.
        private readonly Label _noFiles;
        private GitCommitInfo _shown;

        public VisualElement Root { get; }
        public string ShownHash => _shown?.Hash;

        /// <summary>
        /// 바뀐 파일을 골랐을 때(선택 해제면 null). 가운데에 그 파일의 diff 를 연다.
        /// </summary>
        public event Action<GitDiffTarget> FileSelected;

        /// <summary>
        /// "parent: 해시" 를 눌렀을 때. 그래프에서 그 커밋을 고른다.
        /// </summary>
        public event Action<string> ParentRequested;

        public GraphCommitDetail(VisualElement host, GitServices services)
        {
            _history = services.History;
            _avatars = services.Avatars;
            _assets = services.Assets;
            Root = GitUiAssets.CloneBlock(GitUiAssets.Load("GraphCommitDetail"));
            host.Add(Root);

            _hash = Root.Require<Label>("hash");
            _placeholder = Root.Require<Label>("placeholder");
            _body = Root.Require<VisualElement>("body");
            _subject = Root.Require<Label>("subject");
            _message = Root.Require<Label>("message-body");
            _avatar = Root.Require<VisualElement>("avatar");
            _initials = Root.Require<Label>("initials");
            _author = Root.Require<Label>("author");
            _authored = Root.Require<Label>("authored");
            _parents = Root.Require<VisualElement>("parents");
            _coAuthorsRow = Root.Require<VisualElement>("coauthors-row");
            _coAuthors = Root.Require<VisualElement>("coauthors");
            _summary = Root.Require<VisualElement>("file-summary");

            // 메시지는 끌어서 골라 복사할 수 있게 한다.
            _subject.selection.isSelectable = true;
            _message.selection.isSelectable = true;
            _hash.RegisterCallback<ClickEvent>(_ =>
            {
                if (_shown != null)
                {
                    _assets.CopyToClipboard(_shown.Hash);
                }
            });

            _filesHost = Root.Require<VisualElement>("files");
            _noFiles = new Label(GitText.Get("graphDetail.noFiles")) { pickingMode = PickingMode.Ignore };
            _noFiles.AddToClassList("list__empty");
            _noFiles.AddToClassList("graph-detail__no-files");
            _noFiles.SetVisible(false);
            _files = new FileBrowser(_filesHost, Root.Require<VisualElement>("view-mode-host"), services)
            {
                DiffTargetOf = TargetOf,
            };
            _filesHost.Add(_noFiles);
            _files.SelectionChanged += item => FileSelected?.Invoke(item == null ? null : TargetOf(item));
            // 브랜치 탐색기(Fork 형태)의 Changes 칸과 같은 우클릭 메뉴(열기 · 외부 diff · 되돌리기 · Blame · 히스토리 · 이 버전 저장 · 경로 복사).
            CommitFileMenu fileMenu = new(services);
            _files.FileMenu = (menu, item) => fileMenu.Populate(menu, _shown, item);

            if (_avatars != null)
            {
                Root.RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += RefreshAvatars);
                Root.RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= RefreshAvatars);
            }
            ShowPlaceholder();
        }

        public void Dispose()
        {
            _files.Dispose();
        }

        public void ShowPlaceholder()
        {
            _shown = null;
            _hash.text = string.Empty;
            _filesCover?.RemoveFromHierarchy();
            _filesCover = null;
            _placeholder.SetVisible(true);
            _body.SetVisible(false);
        }

        public void ClearFileSelection()
        {
            _files.ClearSelection();
        }

        public async void Show(GitCommitInfo commit)
        {
            _shown = commit;
            _placeholder.SetVisible(false);
            _body.SetVisible(true);

            _hash.text = commit.ShortHash;
            SetMessage(commit.Subject);
            _author.text = GitDisplay.Nfc(commit.AuthorName);
            _author.tooltip = commit.AuthorEmail;
            _authored.text = GitText.Format("graphDetail.authored", GitDisplay.FormatDate(commit.Date));
            BindAvatar(_avatar, _initials, commit.AuthorName, commit.AuthorEmail, commit.Hash);
            BindParents(commit);
            BindCoAuthors(Array.Empty<(string, string)>());
            BindSummary(Array.Empty<GitFileChange>());
            _files.SetItems(Array.Empty<FileListItem>());
            _noFiles.SetVisible(false);
            // 앞 커밋을 읽던 스켈레톤이 남아 있으면 그대로 이어 쓴다(새로 덮으면 물결이 처음부터 다시 시작해 깜박였다).
            if (_filesCover?.parent == null)
            {
                _filesCover = GitSkeleton.Cover(_filesHost, SkeletonShape.Files);
            }

            string hash = commit.Hash;
            // 메시지와 바뀐 파일은 서로 기다릴 까닭이 없어 함께 읽는다(git 을 두 번 차례로 부르면 스켈레톤이 그만큼 오래 보였다).
            Task<string> messageTask = _history.CommitMessageAsync(hash);
            Task<List<GitFileChange>> filesTask = _history.CommitFilesAsync(hash);
            string message = await messageTask;
            List<GitFileChange> files = await filesTask;
            if (_shown?.Hash != hash)
            {
                return;
            }
            SetMessage(message);
            BindCoAuthors(CoAuthors(message));
            BindSummary(files);
            _files.SetItems(FileListItem.FromChanges(files));
            _noFiles.SetVisible(files.Count == 0);
            GitSkeleton.Uncover(_filesCover);
            _filesCover = null;
        }

        /// <summary>
        /// 메시지의 Co-Authored-By 줄에서 (이름, 이메일). 같은 이메일은 한 번만.
        /// </summary>
        public static List<(string name, string email)> CoAuthors(string message)
        {
            List<(string, string)> result = new();
            HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
            foreach (Match match in CoAuthorPattern.Matches(message ?? string.Empty))
            {
                string email = match.Groups[2].Value.Trim();
                if (seen.Add(email))
                {
                    result.Add((match.Groups[1].Value.Trim(), email));
                }
            }
            return result;
        }

        /// <summary>
        /// 첫 줄은 제목, 그 아래(빈 줄 다음)는 본문.
        /// </summary>
        public static (string subject, string body) SplitMessage(string message)
        {
            string text = (message ?? string.Empty).Replace("\r\n", "\n").Trim();
            int newline = text.IndexOf('\n');
            return newline < 0 ? (text, string.Empty) : (text.Substring(0, newline).Trim(), text.Substring(newline + 1).Trim('\n'));
        }

        private void SetMessage(string message)
        {
            (string subject, string body) = SplitMessage(GitDisplay.Nfc(message));
            _subject.text = subject;
            _message.text = body;
            _message.SetVisible(body.Length > 0);
        }

        private void BindParents(GitCommitInfo commit)
        {
            _parents.Clear();
            if (commit.Parents.Length == 0)
            {
                return;
            }
            Label caption = new(GitText.Get(commit.Parents.Length > 1 ? "graphDetail.parents" : "graphDetail.parent"));
            caption.AddToClassList("graph-detail__parent-label");
            _parents.Add(caption);
            foreach (string parent in commit.Parents)
            {
                Label link = new(GitDisplay.ShortRevision(parent)) { tooltip = GitText.Get("graphDetail.goToParent") };
                link.AddToClassList("graph-detail__parent-link");
                link.RegisterCallback<ClickEvent>(_ => ParentRequested?.Invoke(parent));
                _parents.Add(link);
            }
        }

        private void BindCoAuthors(IReadOnlyList<(string name, string email)> coAuthors)
        {
            _coAuthors.Clear();
            _coAuthorsRow.SetVisible(coAuthors.Count > 0);
            foreach ((string name, string email) in coAuthors)
            {
                VisualElement chip = new() { tooltip = $"{name} <{email}>" };
                chip.AddToClassList("graph-detail__coauthor");
                VisualElement avatar = new();
                avatar.AddToClassList("graph-detail__coauthor-avatar");
                Label initials = new();
                initials.AddToClassList("graph-detail__coauthor-initials");
                avatar.Add(initials);
                chip.Add(avatar);
                Label label = new(GitDisplay.Nfc(name));
                label.AddToClassList("graph-detail__coauthor-name");
                chip.Add(label);
                chip.userData = (name, email);
                // 커밋 해시는 넘기지 않는다 — GitHub 커밋 API 가 돌려주는 사진은 커밋 작성자의 것이라 공동 작성자 자리에 작성자 얼굴이 들어간다.
                BindAvatar(avatar, initials, name, email, null);
                _coAuthors.Add(chip);
            }
        }

        /// <summary>
        /// "✎ 2 modified  + 1 added  − 1 deleted" — 종류마다 상태 색.
        /// </summary>
        private void BindSummary(IReadOnlyCollection<GitFileChange> files)
        {
            _summary.Clear();
            AddCount(files.Count(f => f.State == GitFileState.Modified), "graphDetail.modified", "modified", "✎");
            AddCount(files.Count(f => f.State == GitFileState.Added), "graphDetail.added", "added", "+");
            AddCount(files.Count(f => f.State == GitFileState.Deleted), "graphDetail.deleted", "deleted", "−");
            AddCount(files.Count(f => f.State == GitFileState.Renamed), "graphDetail.renamed", "renamed", "→");
            int other = files.Count(f => f.State != GitFileState.Modified && f.State != GitFileState.Added && f.State != GitFileState.Deleted && f.State != GitFileState.Renamed);
            AddCount(other, "graphDetail.other", "other", "•");

            void AddCount(int count, string key, string kind, string sign)
            {
                if (count == 0)
                {
                    return;
                }
                Label label = new($"{sign} {GitText.Format(key, count)}");
                label.AddToClassList("graph-detail__count");
                label.AddToClassList("graph-detail__count--" + kind);
                _summary.Add(label);
            }
        }

        /// <summary>
        /// 아바타 그림이 있으면 그림, 없으면(설정이 꺼졌거나 아직 받는 중) 작성자 색 동그라미에 머리글자.
        /// </summary>
        private void BindAvatar(VisualElement avatar, Label initials, string name, string email, string commitHash)
        {
            Texture2D texture = _avatars != null && _avatars.IsEnabled ? _avatars.TryGet(email, commitHash) : null;
            avatar.style.backgroundImage = texture == null ? StyleKeyword.Null : new StyleBackground(texture);
            avatar.style.backgroundColor = texture == null ? GraphStyle.AuthorColor(name) : Color.clear;
            initials.text = texture == null ? GitDisplay.Initials(name) : string.Empty;
        }

        private void RefreshAvatars()
        {
            if (_shown == null)
            {
                return;
            }
            BindAvatar(_avatar, _initials, _shown.AuthorName, _shown.AuthorEmail, _shown.Hash);
            foreach (VisualElement chip in _coAuthors.Children())
            {
                if (chip.userData is ValueTuple<string, string> author)
                {
                    VisualElement avatar = chip[0];
                    BindAvatar(avatar, (Label)avatar[0], author.Item1, author.Item2, null);
                }
            }
        }

        private GitDiffTarget TargetOf(FileListItem item)
        {
            return GitDiffTarget.ForCommit(_shown?.Hash, (GitFileChange)item.Source);
        }
    }
}
