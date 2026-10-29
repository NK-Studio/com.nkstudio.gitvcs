using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 이 저장소의 GitHub 계정 고르기 팝업(<see cref="GitConfirmDialog"/> 와 같은 막·카드). 두 가지로 연다.
    /// <list type="bullet">
    /// <item>원격 작업이 인증 문제로 실패했을 때(<see cref="GitOperationRunner.AuthFailureHandler"/>): 어느 계정으로 접속했다가 거부됐는지 알리고, 고르면 그 작업을 다시 한다.</item>
    /// <item>Git 창 ⋮ 메뉴의 "이 저장소의 GitHub 계정…": 실패 안내 없이 계정만 고른다.</item>
    /// </list>
    /// 후보는 이 PC 의 gh·Git Credential Manager·패키지 로그인 계정이고, 각 계정이 이 저장소를 읽을 수 있는지 바로 확인해 표시한다(<see cref="GitAccounts"/>).
    /// </summary>
    public sealed class GitAccountDialog
    {
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly Label _message;
        private readonly VisualElement _list;
        private readonly Label _status;
        private readonly string _failure;
        private (string owner, string repository)? _remote;
        private bool _busy;

        /// <summary>
        /// 팝업을 띄우고 계정을 고를 때까지 기다린다. 계정을 골라 저장했으면 true(실패한 작업을 다시 할 때).
        /// Git 창이 없으면(<see cref="GitServices.PopupHost"/>) 운영체제 대화상자로 원인과 고치는 법만 알린다.
        /// </summary>
        /// <param name="failure">실패한 작업의 git 오류 글. null 이면 계정 바꾸기로 연다.</param>
        public static Task<bool> ShowAsync(GitServices services, string failure)
        {
            return GitPopupHosts.ShowAsync(services.PopupHost, host => new GitAccountDialog(host, failure)._done.Task, false);
        }

        private GitAccountDialog(VisualElement host, string failure)
        {
            _failure = failure;
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            GitShadow.Add(card, GitShadowSize.Large);
            card.AddToClassList("confirm-dialog");
            card.AddToClassList("account-dialog");
            _overlay.Add(card);

            Button close = new(() => Finish(false)) { text = "×", tooltip = GitText.Get("common.cancel") };
            close.AddToClassList("branch-dialog__close");
            card.Add(close);

            VisualElement header = new();
            header.AddToClassList("branch-dialog__header");
            VisualElement badge = new();
            badge.AddToClassList("branch-dialog__badge");
            badge.EnableInClassList("account-dialog__badge--failed", failure != null);
            ActionGlyph glyph = new(ActionGlyphKind.Cloud);
            glyph.AddToClassList("branch-dialog__badge-glyph");
            badge.Add(glyph);
            header.Add(badge);
            Label title = new(GitText.Get(failure != null ? "account.failedTitle" : "account.chooseTitle"));
            title.AddToClassList("branch-dialog__title");
            title.AddToClassList("confirm-dialog__title");
            header.Add(title);
            card.Add(header);

            _message = new Label(GitText.Get("account.looking"));
            _message.AddToClassList("confirm-dialog__message");
            card.Add(_message);

            _list = new VisualElement();
            _list.AddToClassList("account-dialog__list");
            card.Add(_list);

            _status = new Label();
            _status.AddToClassList("account-dialog__status");
            _status.SetVisible(false);
            card.Add(_status);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            if (GitHubAccount.IsSignedIn == false)
            {
                Button signIn = new(() =>
                {
                    SettingsService.OpenUserPreferences(GitMainToolbarMenu.PreferencesPath);
                    Finish(false);
                }) { text = GitText.Get("account.signIn"), tooltip = GitText.Get("account.signInTooltip") };
                signIn.AddToClassList("branch-dialog__cancel");
                buttons.Add(signIn);
            }
            Button reset = new(() => _ = ResetAsync()) { text = GitText.Get("account.reset"), tooltip = GitText.Get("account.resetTooltip") };
            reset.AddToClassList("branch-dialog__cancel");
            buttons.Add(reset);
            Button cancel = new(() => Finish(false)) { text = GitText.Get(failure != null ? "common.close" : "common.cancel") };
            cancel.AddToClassList("branch-dialog__cancel");
            buttons.Add(cancel);
            card.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape)
                {
                    Finish(false);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(false));
            host.Add(_overlay);
            _ = LoadAsync();
        }

        private async Task LoadAsync()
        {
            _remote = await GitAccounts.GitHubRemoteAsync();
            if (_remote == null)
            {
                // GitHub 가 아닌 원격(GitLab·사내 서버 등): 계정 목록은 못 보이고 원인과 git 오류만 알린다.
                _message.text = GitText.Format("account.notGitHub", FirstLines(_failure ?? string.Empty));
                return;
            }
            (string owner, string repository) = _remote.Value;
            string current = await GitAccounts.CurrentUsernameAsync(owner, repository);
            string repoName = $"{owner}/{repository}";
            _message.text = _failure != null
                ? string.IsNullOrEmpty(current)
                    ? GitText.Format("account.failedNoAccount", repoName)
                    : GitText.Format("account.failedWithAccount", current, repoName)
                : GitText.Format("account.chooseMessage", repoName);

            Label loading = new(GitText.Get("account.checking"));
            loading.AddToClassList("account-dialog__empty");
            _list.Add(loading);
            List<GitAccountCandidate> candidates = await GitAccounts.ListAsync(owner, repository);
            _list.Clear();
            if (candidates.Count == 0)
            {
                Label empty = new(GitText.Get("account.none"));
                empty.AddToClassList("account-dialog__empty");
                _list.Add(empty);
                return;
            }
            // 접근할 수 있는 계정을 위로.
            foreach (GitAccountCandidate candidate in candidates.OrderBy(c => c.CanAccess == true ? 0 : c.CanAccess == null ? 1 : 2))
            {
                _list.Add(Row(candidate));
            }
            // UI Toolkit 은 :last-child 를 모른다. 마지막 줄의 밑줄은 클래스로 지운다.
            _list[_list.childCount - 1].AddToClassList("account-dialog__row--last");
        }

        private VisualElement Row(GitAccountCandidate candidate)
        {
            VisualElement row = new();
            row.AddToClassList("account-dialog__row");

            VisualElement names = new();
            names.AddToClassList("account-dialog__names");
            Label login = new(candidate.Login);
            login.AddToClassList("account-dialog__login");
            names.Add(login);
            Label source = new(GitText.Get("account.source" + candidate.Source) + (candidate.IsCurrent ? GitText.Get("account.currentSuffix") : string.Empty));
            source.AddToClassList("account-dialog__source");
            names.Add(source);
            row.Add(names);

            Label access = new(GitText.Get(candidate.CanAccess == true ? "account.canAccess" : candidate.CanAccess == false ? "account.noAccess" : "account.unknownAccess"));
            access.AddToClassList("account-dialog__access");
            access.AddToClassList(candidate.CanAccess == true ? "account-dialog__access--yes" : candidate.CanAccess == false ? "account-dialog__access--no" : "account-dialog__access--unknown");
            row.Add(access);

            Button use = new(() => _ = ChooseAsync(candidate)) { text = GitText.Get(_failure != null ? "account.useAndRetry" : "account.use") };
            use.AddToClassList(candidate.CanAccess == true ? "branch-dialog__create" : "branch-dialog__cancel");
            use.AddToClassList("account-dialog__use");
            // 지금 쓰는 계정은 다시 고를 일이 없다(실패했다면 더욱).
            use.SetEnabled(candidate.IsCurrent == false);
            row.Add(use);
            return row;
        }

        private async Task ChooseAsync(GitAccountCandidate candidate)
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            GitResult result = await GitAccounts.ChooseAsync(candidate);
            _busy = false;
            if (result.Success == false)
            {
                ShowStatus(result.Message);
                return;
            }
            Finish(true);
        }

        private async Task ResetAsync()
        {
            if (_busy)
            {
                return;
            }
            _busy = true;
            await GitAccounts.ResetAsync();
            _busy = false;
            Finish(_failure != null);
        }

        private void ShowStatus(string text)
        {
            _status.text = text;
            _status.SetVisible(true);
        }

        /// <summary>
        /// git 오류의 앞쪽 몇 줄(remote: … / fatal: …). 긴 안내 글은 자른다.
        /// </summary>
        private static string FirstLines(string message)
        {
            return string.Join("\n", (message ?? string.Empty).Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).Take(3));
        }

        private void Finish(bool chosen)
        {
            _done.TrySetResult(chosen);
            GitPopupHosts.Close(_overlay, null);
        }
    }
}
