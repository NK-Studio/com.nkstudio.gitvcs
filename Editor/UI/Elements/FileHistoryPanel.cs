using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitKraken 레이아웃의 파일 히스토리: Git 창 가운데를 통째로 덮어 크게 보인다(GitKraken 의 File History 와 같다).
    /// 위에 "파일 히스토리: 경로" 와 닫기(×), 왼쪽에 그 파일을 바꾼 커밋(아바타·제목·"2시간 전 · 작성자"·짧은 해시), 오른쪽에 고른 커밋의 이 파일 diff.
    /// Fork·Unity VCS 레이아웃은 여기 대신 따로 뜨는 <see cref="GitFileHistoryWindow"/> 를 쓴다.
    /// </summary>
    public sealed class FileHistoryPanel
    {
        /// <summary>
        /// 한 번에 읽을 커밋 수. 따로 뜨는 창(<see cref="GitFileHistoryWindow"/>)과 같다.
        /// </summary>
        private const int MaxCommits = 500;

        private const float RowHeight = 44f;

        private readonly IAvatarProvider _avatars;
        private readonly Label _title;
        private readonly ListView _list;
        private readonly Label _footer;
        private readonly Label _notice;
        private readonly GitDiffPanel _diff;

        private List<GitCommitInfo> _commits = new();
        private Dictionary<string, string> _pathByCommit = new();
        private string _repoPath;
        private int _loadVersion;

        /// <summary>
        /// 닫기(× 또는 Esc)를 눌렀을 때. 호스트가 원래 화면(그래프)으로 돌아간다.
        /// </summary>
        public event Action CloseRequested;

        public VisualElement Root { get; }

        /// <summary>
        /// 지금 보이는 파일(저장소 루트 기준). 아무것도 안 열었으면 null.
        /// </summary>
        public string RepoPath => _repoPath;

        /// <summary>
        /// 오른쪽 diff 패널. 호스트가 diff 막대의 히스토리 버튼 같은 연결을 더 할 때 쓴다.
        /// </summary>
        public GitDiffPanel Diff => _diff;

        public FileHistoryPanel(GitServices services)
        {
            _avatars = services.Avatars;

            Root = new VisualElement { focusable = true };
            Root.AddToClassList("file-history");

            VisualElement header = new();
            header.AddToClassList("file-history__header");
            _title = new Label();
            _title.AddToClassList("file-history__title");
            header.Add(_title);
            Button close = new(() => CloseRequested?.Invoke()) { text = "×", tooltip = GitText.Get("history.closeTooltip") };
            close.AddToClassList("file-history__close");
            header.Add(close);
            Root.Add(header);

            TwoPaneSplitView split = new(0, 400, TwoPaneSplitViewOrientation.Horizontal);
            split.AddToClassList("view__split");
            split.AddToClassList("file-history__split");
            Root.Add(split);

            VisualElement left = new();
            left.AddToClassList("view__pane");
            left.AddToClassList("file-history__commits");
            _notice = new Label();
            _notice.AddToClassList("file-history__notice");
            left.Add(_notice);
            _list = new ListView
            {
                fixedItemHeight = RowHeight,
                virtualizationMethod = CollectionVirtualizationMethod.FixedHeight,
                selectionType = SelectionType.Single,
                makeItem = MakeRow,
                bindItem = BindRow,
            };
            _list.AddToClassList("file-history__list");
            _list.selectionChanged += _ => ShowSelected();
            left.Add(_list);
            _footer = new Label();
            _footer.AddToClassList("file-history__footer");
            left.Add(_footer);
            split.Add(left);

            VisualElement right = new();
            right.AddToClassList("view__pane");
            _diff = new GitDiffPanel(services.Diff);
            _diff.Root.style.flexGrow = 1;
            _diff.ShowReloadButton(false);
            right.Add(_diff.Root);
            split.Add(right);
            SplitPaneClamp.Attach(split);

            Root.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    CloseRequested?.Invoke();
                    evt.StopPropagation();
                }
            });
            if (_avatars != null)
            {
                Root.RegisterCallback<AttachToPanelEvent>(_ => _avatars.AvatarLoaded += RefreshAvatars);
                Root.RegisterCallback<DetachFromPanelEvent>(_ => _avatars.AvatarLoaded -= RefreshAvatars);
            }
        }

        /// <summary>
        /// 이 파일의 히스토리를 읽어 보인다. 맨 위(가장 최근) 커밋을 골라 그 diff 를 연다.
        /// </summary>
        public async void Show(string repoPath)
        {
            _repoPath = repoPath;
            int version = ++_loadVersion;
            _title.text = GitText.Format("history.inlineTitle", GitDisplay.Nfc(repoPath));
            _title.tooltip = _title.text;
            _commits = new List<GitCommitInfo>();
            _list.itemsSource = _commits;
            _list.RefreshItems();
            _footer.text = string.Empty;
            _notice.text = GitText.Get("common.loading");
            _notice.SetVisible(true);
            _diff.SetTarget(null);
            Root.Focus();

            (List<GitCommitInfo> commits, string error) = await GitFileOperations.FileLogAsync(repoPath, MaxCommits);
            Dictionary<string, string> paths = await GitFileOperations.FollowPathsAsync(repoPath, MaxCommits);
            if (version != _loadVersion)
            {
                // 기다리는 동안 다른 파일 히스토리를 열었다.
                return;
            }

            _commits = commits ?? new List<GitCommitInfo>();
            _pathByCommit = paths;
            _notice.text = error ?? GitText.Get("history.empty");
            _notice.SetVisible(error != null || _commits.Count == 0);
            _footer.text = _commits.Count >= MaxCommits ? GitText.Format("history.capped", MaxCommits) : GitText.Get("history.endOfHistory");
            _list.itemsSource = _commits;
            _list.Rebuild();
            if (_commits.Count > 0)
            {
                _list.SetSelection(0);
            }
        }

        private void ShowSelected()
        {
            if (_list.selectedIndex < 0 || _list.selectedIndex >= _commits.Count)
            {
                return;
            }
            GitCommitInfo commit = _commits[_list.selectedIndex];
            string path = _pathByCommit.TryGetValue(commit.Hash, out string followed) ? followed : _repoPath;
            // 가장 오래된 커밋(목록 끝)은 이 파일을 처음 만든 커밋이다. 히스토리 끝까지 읽었을 때만 그렇게 본다.
            bool isCreation = _list.selectedIndex == _commits.Count - 1 && _commits.Count < MaxCommits;
            _diff.SetTarget(GitDiffTarget.ForCommit(commit.Hash, new GitFileChange { Status = isCreation ? 'A' : 'M', RepoPath = path }));
        }

        // ── 커밋 줄: [아바타] 제목 / "2시간 전 · 작성자"  …  짧은 해시 ──

        private static VisualElement MakeRow()
        {
            VisualElement row = new();
            row.AddToClassList("file-history__row");

            VisualElement avatar = new();
            avatar.AddToClassList("file-history__avatar");
            Label initials = new();
            initials.AddToClassList("file-history__initials");
            avatar.Add(initials);
            row.Add(avatar);

            VisualElement text = new();
            text.AddToClassList("file-history__text");
            Label subject = new();
            subject.AddToClassList("file-history__subject");
            text.Add(subject);
            Label meta = new();
            meta.AddToClassList("file-history__meta");
            text.Add(meta);
            row.Add(text);

            Label hash = new();
            hash.AddToClassList("file-history__hash");
            row.Add(hash);
            return row;
        }

        private void BindRow(VisualElement row, int index)
        {
            GitCommitInfo commit = _commits[index];
            VisualElement avatar = row[0];
            BindAvatar(avatar, (Label)avatar[0], commit);
            VisualElement text = row[1];
            ((Label)text[0]).text = GitDisplay.Nfc(commit.Subject);
            ((Label)text[1]).text = GitText.Format("history.byAuthor", GitDisplay.RelativeTime(commit.Date, DateTime.Now), commit.AuthorName);
            Label hash = (Label)row[2];
            hash.text = commit.ShortHash;
            row.tooltip = $"{commit.Subject}\n{commit.AuthorName} <{commit.AuthorEmail}>\n{GitDisplay.FormatDate(commit.Date)}  {commit.ShortHash}";
        }

        /// <summary>
        /// 아바타 그림이 있으면 그림, 없으면(설정이 꺼졌거나 아직 받는 중) 작성자 색 동그라미에 머리글자. 커밋 상세(<see cref="GraphCommitDetail"/>)와 같다.
        /// </summary>
        private void BindAvatar(VisualElement avatar, Label initials, GitCommitInfo commit)
        {
            Texture2D texture = _avatars != null && _avatars.IsEnabled ? _avatars.TryGet(commit.AuthorEmail, commit.Hash) : null;
            avatar.style.backgroundImage = texture == null ? StyleKeyword.Null : new StyleBackground(texture);
            avatar.style.backgroundColor = texture == null ? GraphStyle.AuthorColor(commit.AuthorName) : Color.clear;
            initials.text = texture == null ? GitDisplay.Initials(commit.AuthorName) : string.Empty;
        }

        private void RefreshAvatars()
        {
            _list.RefreshItems();
        }
    }
}
