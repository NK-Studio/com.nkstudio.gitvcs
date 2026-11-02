using System.Linq;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub 가 아닌 서버(회사 NAS·사내 GitLab 등)가 계정을 요구해 원격 작업이 실패했을 때 아이디·비밀번호를 받는 팝업
    /// (<see cref="GitConfirmDialog"/> 와 같은 막·카드). 받으면 <see cref="GitRemoteLogin"/> 에 넘기고 실패한 작업을 다시 한다
    /// (<see cref="GitOperationRunner.AuthFailureHandler"/>).
    /// SSH 는 계정이 원격 주소로 정해져 비밀번호만 받는다. https 는 사용자 이름과 비밀번호(또는 액세스 토큰)를 받는다.
    /// Enter 는 로그인, Esc·× 는 취소.
    /// </summary>
    public sealed class GitLoginDialog
    {
        private readonly TaskCompletionSource<bool> _done = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly VisualElement _overlay;
        private readonly RemoteLoginTarget _target;
        private readonly TextField _user;
        private readonly TextField _password;
        private readonly Label _problem;
        private readonly Button _ok;
        private bool _busy;

        /// <summary>
        /// 실패 글에서 로그인할 서버를 찾아 팝업을 띄운다. 로그인했으면 true(실패한 작업을 다시 한다).
        /// 서버를 알아낼 수 없으면 로그인 대신 오류 알림을 보이고 false.
        /// </summary>
        public static async Task<bool> ShowAsync(GitServices services, string failure)
        {
            RemoteLoginTarget? parsed = GitRemoteLogin.ParseFailure(failure);
            RemoteLoginTarget? target = parsed;
            // 처음 접속하는 SSH 서버는 오류 글에 서버 이름이 없다. 계정 이름이 없을 때도 원격 주소에서 찾는다.
            if (parsed.HasValue && (parsed.Value.Host == null || parsed.Value.User == null))
            {
                GitResult url = await GitRepository.RunAsync("ls-remote", "--get-url");
                RemoteLoginTarget? fromUrl = url.Success ? GitRemoteLogin.ParseUrl(url.Output) : null;
                target = fromUrl.HasValue && fromUrl.Value.Kind == parsed.Value.Kind
                    ? new RemoteLoginTarget(parsed.Value.Kind, parsed.Value.Host ?? fromUrl.Value.Host, parsed.Value.User ?? fromUrl.Value.User)
                    : parsed.Value.Host != null ? parsed : null;
            }
            if (target.HasValue == false)
            {
                await GitErrorDialog.ShowAsync(services, GitText.Get("login.failedTitle"), failure);
                return false;
            }
            bool newHost = parsed.Value.Host == null;
            return await GitPopupHosts.ShowAsync(services.PopupHost, host => new GitLoginDialog(host, target.Value, newHost)._done.Task, false);
        }

        private GitLoginDialog(VisualElement host, RemoteLoginTarget target, bool newHost)
        {
            _target = target;
            bool ssh = target.Kind == RemoteLoginKind.Ssh;
            _overlay = new VisualElement();
            _overlay.AddToClassList("branch-popup");
            VisualElement card = new();
            card.AddToClassList("branch-dialog");
            card.AddToClassList("login-dialog");
            GitShadow.Add(card, GitShadowSize.Large);
            _overlay.Add(card);

            Button close = new(() => Finish(false)) { text = "×", tooltip = GitText.Get("common.cancel") };
            close.AddToClassList("branch-dialog__close");
            card.Add(close);

            VisualElement header = new();
            header.AddToClassList("branch-dialog__header");
            VisualElement badge = new();
            badge.AddToClassList("branch-dialog__badge");
            ActionGlyph glyph = new(ActionGlyphKind.Cloud);
            glyph.AddToClassList("branch-dialog__badge-glyph");
            badge.Add(glyph);
            header.Add(badge);
            VisualElement titles = new();
            titles.AddToClassList("branch-dialog__titles");
            titles.Add(Text(GitText.Format("login.title", target.Host), "branch-dialog__title"));
            string hint = GitText.Get(ssh ? "login.sshHint" : "login.httpsHint");
            if (ssh && newHost)
            {
                hint += " " + GitText.Get("login.newHost");
            }
            Label hintLabel = Text(hint, "branch-dialog__hint");
            hintLabel.AddToClassList("login-dialog__hint");
            titles.Add(hintLabel);
            header.Add(titles);
            card.Add(header);

            if (ssh)
            {
                // SSH 계정은 원격 주소(user@host)가 정한다. 바꾸려면 원격 주소를 바꿔야 해서 보이기만 한다.
                VisualElement account = Row(GitText.Get("login.account"));
                Label name = Text(string.IsNullOrEmpty(target.User) ? target.Host : target.User + "@" + target.Host, "branch-dialog__start-label");
                account.Add(name);
                card.Add(account);
            }
            else
            {
                _user = Input(card, GitText.Get("login.user"), password: false);
                _user.value = target.User ?? string.Empty;
            }
            _password = Input(card, GitText.Get(ssh ? "login.password" : "login.passwordOrToken"), password: true);

            // 기억한 비밀번호로 다시 거부됐으면 틀렸다고 알린다.
            _problem = Text(ssh && GitRemoteLogin.HasSshLogin(target.Host) ? GitText.Get("login.wrongPassword") : string.Empty, "branch-dialog__problem");
            _problem.AddToClassList("login-dialog__problem");
            card.Add(_problem);

            Label note = Text(GitText.Get(ssh ? "login.sshNote" : "login.httpsNote"), "login-dialog__note");
            card.Add(note);

            VisualElement buttons = new();
            buttons.AddToClassList("branch-dialog__buttons");
            _ok = new Button(() => _ = ConfirmAsync()) { text = GitText.Get("login.signInAndRetry") };
            _ok.AddToClassList("branch-dialog__create");
            Button cancel = new(() => Finish(false)) { text = GitText.Get("common.cancel") };
            cancel.AddToClassList("branch-dialog__cancel");
            buttons.Add(_ok);
            buttons.Add(cancel);
            card.Add(buttons);

            _overlay.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter)
                {
                    _ = ConfirmAsync();
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Finish(false);
                    evt.StopPropagation();
                }
            }, TrickleDown.TrickleDown);
            _overlay.RegisterCallback<DetachFromPanelEvent>(_ => _done.TrySetResult(false));
            host.Add(_overlay);
            TextField first = _user != null && string.IsNullOrEmpty(_user.value) ? _user : _password;
            first.schedule.Execute(() => first.Focus());
        }

        private async Task ConfirmAsync()
        {
            if (_busy)
            {
                return;
            }
            string user = _user?.value?.Trim() ?? string.Empty;
            string password = _password.value ?? string.Empty;
            if ((_user != null && user.Length == 0) || password.Length == 0)
            {
                _problem.text = GitText.Get("login.empty");
                return;
            }
            // 자격 증명 요청은 줄 단위라 줄바꿈이 들어가면 다른 항목으로 읽힌다.
            if (user.IndexOfAny(new[] { '\n', '\r' }) >= 0 || password.IndexOfAny(new[] { '\n', '\r' }) >= 0)
            {
                _problem.text = GitText.Get("login.newline");
                return;
            }
            if (_target.Kind == RemoteLoginKind.Ssh)
            {
                GitRemoteLogin.RememberSsh(_target.Host, password);
                Finish(true);
                return;
            }
            _busy = true;
            _ok.SetEnabled(false);
            string gitPath = GitVcsSettings.instance.GitExecutable;
            string root = GitRepository.RepoRoot ?? GitRepository.ProjectRoot;
            string host = _target.Host;
            GitResult result = await Task.Run(() => GitRemoteLogin.SaveHttps(gitPath, root, host, user, password));
            _busy = false;
            _ok.SetEnabled(true);
            if (result.Success == false)
            {
                _problem.text = GitText.Format("login.saveFailed", result.Message.Split('\n').FirstOrDefault()?.Trim());
                return;
            }
            Finish(true);
        }

        private static VisualElement Row(string label)
        {
            VisualElement row = new();
            row.AddToClassList("branch-dialog__row");
            Label name = Text(label, "branch-dialog__label");
            name.AddToClassList("login-dialog__label");
            row.Add(name);
            return row;
        }

        /// <summary>이름표 + 입력란 한 줄. 입력란은 바깥 상자 하나만 테두리(포커스면 강조색) — 다른 팝업과 같은 모양.</summary>
        private static TextField Input(VisualElement card, string label, bool password)
        {
            VisualElement row = Row(label);
            VisualElement field = new();
            field.AddToClassList("branch-dialog__field");
            TextField input = new() { isPasswordField = password, maskChar = '•' };
            input.AddToClassList("branch-dialog__input");
            input.RegisterCallback<FocusInEvent>(_ => field.AddToClassList("branch-dialog__field--focused"));
            input.RegisterCallback<FocusOutEvent>(_ => field.RemoveFromClassList("branch-dialog__field--focused"));
            field.RegisterCallback<PointerDownEvent>(_ => input.Focus());
            field.Add(input);
            row.Add(field);
            card.Add(row);
            return input;
        }

        private static Label Text(string text, string className)
        {
            Label label = new(text);
            label.AddToClassList(className);
            return label;
        }

        private void Finish(bool signedIn)
        {
            _done.TrySetResult(signedIn);
            GitPopupHosts.Close(_overlay, null);
        }
    }
}
