using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace GitVersionControl
{
    /// <summary>
    /// "GitHub 로 로그인" 한 번의 흐름(GitHub OAuth Device Flow). 코드를 받아 브라우저를 열고, 사용자가 승인할 때까지 정해진 간격으로 묻는다.
    /// 규칙(응답 읽기·다음에 할 일)은 <see cref="GitHubDeviceFlow"/>, 받은 토큰 저장은 <see cref="GitHubAccount"/>.
    /// </summary>
    public sealed class GitHubDeviceLogin
    {
        public enum Step
        {
            Idle,
            RequestingCode,

            /// <summary>
            /// 브라우저에서 <see cref="UserCode"/> 입력·승인을 기다리는 중.
            /// </summary>
            WaitingForApproval,
            Done,
            Failed,
        }

        private const string UserAgent = "com.nkstudio.gitvcs";

        private int _attempt;

        public Step Current { get; private set; } = Step.Idle;
        public string UserCode { get; private set; }
        public string VerificationUri { get; private set; }
        public string Error { get; private set; }

        public bool IsRunning => Current == Step.RequestingCode || Current == Step.WaitingForApproval;

        /// <summary>
        /// 화면을 다시 그려야 할 때(단계가 바뀔 때).
        /// </summary>
        public event System.Action StateChanged;

        public async void Start()
        {
            int attempt = ++_attempt;
            SetStep(Step.RequestingCode, null);

            WWWForm codeForm = new();
            codeForm.AddField("client_id", GitHubAccount.ClientId);
            codeForm.AddField("scope", GitHubAccount.Scope);
            (string codeJson, string codeError) = await PostAsync(GitHubDeviceFlow.DeviceCodeUrl, codeForm);
            if (attempt != _attempt)
            {
                return;
            }
            GitHubDeviceFlow.DeviceCode code = codeError == null ? GitHubDeviceFlow.ParseDeviceCode(codeJson) : null;
            if (code == null)
            {
                SetStep(Step.Failed, codeError ?? GitText.Get("github.badResponse"));
                return;
            }

            UserCode = code.user_code;
            VerificationUri = code.verification_uri;
            GUIUtility.systemCopyBuffer = UserCode;
            Application.OpenURL(VerificationUri);
            SetStep(Step.WaitingForApproval, null);

            int intervalSeconds = GitHubDeviceFlow.IntervalSeconds(code.interval);
            float deadline = Time.realtimeSinceStartup + code.expires_in;
            while (Time.realtimeSinceStartup < deadline)
            {
                await Task.Delay(intervalSeconds * 1000);
                if (attempt != _attempt)
                {
                    return;
                }

                WWWForm tokenForm = new();
                tokenForm.AddField("client_id", GitHubAccount.ClientId);
                tokenForm.AddField("device_code", code.device_code);
                tokenForm.AddField("grant_type", GitHubDeviceFlow.GrantType);
                (string tokenJson, string tokenError) = await PostAsync(GitHubDeviceFlow.TokenUrl, tokenForm);
                if (attempt != _attempt)
                {
                    return;
                }
                if (tokenError != null)
                {
                    // 네트워크가 잠깐 끊긴 것일 수 있으니 기한까지는 계속 묻는다.
                    continue;
                }

                GitHubDeviceFlow.TokenReply reply = GitHubDeviceFlow.ParseTokenReply(tokenJson);
                switch (GitHubDeviceFlow.Interpret(reply))
                {
                    case GitHubDeviceFlow.PollOutcome.Wait:
                        continue;
                    case GitHubDeviceFlow.PollOutcome.SlowDown:
                        intervalSeconds += GitHubDeviceFlow.SlowDownSeconds;
                        continue;
                    case GitHubDeviceFlow.PollOutcome.Success:
                        string signInError = await GitHubAccount.SignInAsync(reply.access_token);
                        SetStep(signInError == null ? Step.Done : Step.Failed, signInError);
                        return;
                    default:
                        SetStep(Step.Failed, reply.error_description ?? reply.error);
                        return;
                }
            }
            SetStep(Step.Failed, GitText.Get("github.expired"));
        }

        /// <summary>
        /// 기다리기를 그만둔다. 이미 보낸 요청의 답은 버린다.
        /// </summary>
        public void Cancel()
        {
            _attempt++;
            SetStep(Step.Idle, null);
        }

        private void SetStep(Step step, string error)
        {
            Current = step;
            Error = error;
            StateChanged?.Invoke();
        }

        /// <returns>(응답 JSON, 오류 메시지). 성공이면 오류는 null.</returns>
        private static async Task<(string json, string error)> PostAsync(string url, WWWForm form)
        {
            using UnityWebRequest request = UnityWebRequest.Post(url, form);
            request.SetRequestHeader("User-Agent", UserAgent);
            request.SetRequestHeader("Accept", "application/json");
            await GitHubAccount.SendAsync(request);
            return request.result == UnityWebRequest.Result.Success
                ? (request.downloadHandler.text, null)
                : (null, request.error);
        }
    }
}
