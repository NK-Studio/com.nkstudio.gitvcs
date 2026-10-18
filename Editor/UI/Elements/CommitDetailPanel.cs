using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 고른 커밋의 메시지와 바뀐 파일(<c>CommitDetail.uxml</c>). Changesets 와 Branch Explorer 가 함께 쓴다.
    /// 읽기는 비동기라, 기다리는 동안 다른 커밋을 고르면 늦게 온 결과는 버린다.
    /// </summary>
    public sealed class CommitDetailPanel
    {
        private const string HiddenModifier = "commit-detail--hidden";
        private const string CloseHiddenModifier = "commit-detail__close--hidden";

        private readonly IGitHistoryService _history;
        private readonly Label _title;
        private readonly Label _placeholder;
        private readonly VisualElement _body;
        private readonly TextField _message;
        private readonly FileBrowser _files;
        private string _shownHash;

        public VisualElement Root { get; }
        public string ShownHash => _shownHash;

        /// <summary>닫기(×) 를 눌렀을 때.</summary>
        public event Action Closed;

        public CommitDetailPanel(VisualElement host, GitServices services, bool closable)
        {
            _history = services.History;
            Root = GitUiAssets.CloneBlock(GitUiAssets.Load("CommitDetail"));
            host.Add(Root);

            _title = Root.Require<Label>("title");
            _placeholder = Root.Require<Label>("placeholder");
            _body = Root.Require<VisualElement>("body");
            _message = Root.Require<TextField>("message");
            _files = new FileBrowser(Root.Require<VisualElement>("files"), Root.Require<VisualElement>("view-mode-host"), services)
            {
                DiffTargetOf = item => GitDiffTarget.ForCommit(_shownHash, (GitFileChange)item.Source),
            };

            Button close = Root.Require<Button>("close-button");
            close.EnableInClassList(CloseHiddenModifier, closable == false);
            close.clicked += () =>
            {
                Hide();
                Closed?.Invoke();
            };
        }

        /// <summary>커밋을 고르기 전 안내 문구만 보인다.</summary>
        public void ShowPlaceholder()
        {
            _shownHash = null;
            Root.RemoveFromClassList(HiddenModifier);
            _title.text = string.Empty;
            _placeholder.SetVisible(true);
            _body.SetVisible(false);
        }

        public void Hide()
        {
            _shownHash = null;
            Root.AddToClassList(HiddenModifier);
        }

        public async void Show(GitCommitInfo commit, string laneName = null)
        {
            _shownHash = commit.Hash;
            Root.RemoveFromClassList(HiddenModifier);
            _placeholder.SetVisible(false);
            _body.SetVisible(true);

            string lane = string.IsNullOrEmpty(laneName) ? string.Empty : $"  ·  {GitDisplay.Nfc(laneName)}";
            _title.text = $"{commit.ShortHash}  ·  {commit.AuthorName} <{commit.AuthorEmail}>  ·  {GitDisplay.FormatDate(commit.Date)}{lane}";
            _message.SetValueWithoutNotify(commit.Subject);
            _files.SetItems(Array.Empty<FileListItem>());

            string hash = commit.Hash;
            string message = await _history.CommitMessageAsync(hash);
            List<GitFileChange> files = await _history.CommitFilesAsync(hash);
            if (_shownHash != hash)
            {
                return;
            }
            _message.SetValueWithoutNotify(message);
            _files.SetItems(files.Select(FileListItem.From));
        }
    }
}
