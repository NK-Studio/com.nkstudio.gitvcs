using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 창 아래쪽 상태 줄(<c>.git-footer</c>). GitKraken 처럼
    /// 왼쪽: 열린 풀 리퀘스트 수(누르면 사이드바 풀 리퀘스트 구역) | 오른쪽: UI 배율(누르면 크게·작게·원래대로) · GitHub 계정 · 패키지 버전.
    /// </summary>
    public sealed class GitFooter
    {
        private readonly Button _pullRequests;
        private readonly Label _pullRequestsText;
        private readonly Button _scale;
        private readonly Label _scaleText;
        private readonly Button _account;
        private readonly Label _accountText;

        /// <summary>풀 리퀘스트 칸을 눌렀을 때. 창이 사이드바의 풀 리퀘스트 구역을 보인다.</summary>
        public event Action PullRequestsRequested;

        /// <summary>UI 배율 메뉴에서 고른 동작(크게·작게·원래대로).</summary>
        public Action ZoomIn { get; set; }
        public Action ZoomOut { get; set; }
        public Action ZoomReset { get; set; }

        /// <summary>계정 칸을 눌렀을 때. 창이 이 저장소의 GitHub 계정 고르기를 띄운다.</summary>
        public event Action AccountRequested;

        public GitFooter(VisualElement layout)
        {
            _pullRequests = layout.Require<Button>("footer-pull-requests");
            _pullRequestsText = AddContent(_pullRequests, ActionGlyphKind.PullRequest);
            _pullRequests.clicked += () => PullRequestsRequested?.Invoke();

            _scale = layout.Require<Button>("footer-scale");
            _scaleText = AddContent(_scale, ActionGlyphKind.ZoomIn);
            _scale.tooltip = GitText.Get("window.uiScale");
            _scale.clicked += ShowScaleMenu;

            _account = layout.Require<Button>("footer-account");
            _accountText = AddContent(_account, ActionGlyphKind.GitHub);
            _account.tooltip = GitText.Get("account.menu");
            _account.clicked += () => AccountRequested?.Invoke();
            _accountText.text = GitHubAccount.IsSignedIn ? GitHubAccount.Login : "GitHub";

            Label version = layout.Require<Label>("footer-version");
            UnityEditor.PackageManager.PackageInfo package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(GitFooter).Assembly);
            version.text = package?.version ?? string.Empty;
            version.tooltip = package?.displayName;
            version.SetVisible(package != null);

            SetPullRequests(null);
            SetScale(GitVcsSettings.instance.GitWindowScale);
        }

        /// <summary>사이드바가 풀 리퀘스트를 다 읽을 때마다 부른다. 개수는 사이드바 구역 머리와 같은 규칙(<see cref="RefTreeBuilder.PullRequestCount"/>).</summary>
        public void SetPullRequests(GitPullRequestSet set)
        {
            int count = RefTreeBuilder.PullRequestCount(set);
            _pullRequestsText.text = set == null || set.IsLoading ? GitText.Get("common.loading")
                : count > 0 ? GitText.Format("footer.pullRequests", count)
                : GitText.Get("footer.noPullRequests");
            _pullRequests.tooltip = set == null ? null
                : set.NeedsSignIn ? GitText.Get("prList.signIn")
                : set.NoGitHubRemote ? GitText.Get("prList.noRemote")
                : set.Error != null ? GitText.Get("prList.error") + " " + set.Error
                : set.Repository;
        }

        public void SetScale(float scale)
        {
            _scaleText.text = Mathf.RoundToInt(scale * 100f) + "%";
        }

        private void ShowScaleMenu()
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent(GitText.Get("window.uiScaleIn")), false, () => ZoomIn?.Invoke());
            menu.AddItem(new GUIContent(GitText.Get("window.uiScaleOut")), false, () => ZoomOut?.Invoke());
            menu.AddItem(new GUIContent(GitText.Format("window.uiScaleReset", Mathf.RoundToInt(GitVcsSettings.instance.GitWindowScale * 100f))), false, () => ZoomReset?.Invoke());
            menu.DropDown(_scale.worldBound);
        }

        /// <summary>버튼 안에 아이콘과 글자를 넣고 글자 칸을 돌려준다.</summary>
        private static Label AddContent(Button button, ActionGlyphKind icon)
        {
            ActionGlyph glyph = new(icon);
            glyph.AddToClassList("git-footer__icon");
            button.Add(glyph);
            Label text = new();
            text.AddToClassList("git-footer__text");
            text.pickingMode = PickingMode.Ignore;
            button.Add(text);
            return text;
        }
    }
}
