using System;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// GitHub OAuth Device Flow 의 순수 규칙(네트워크 없음): 요청 주소·양식, 응답 읽기, 승인 대기 중 다음에 할 일.
    /// 흐름: 기기 코드 받기 → 사용자가 브라우저에서 코드 입력·승인 → 토큰 받을 때까지 정해진 간격으로 묻기.
    /// https://docs.github.com/apps/oauth-apps/building-oauth-apps/authorizing-oauth-apps#device-flow
    /// </summary>
    public static class GitHubDeviceFlow
    {
        public const string DeviceCodeUrl = "https://github.com/login/device/code";
        public const string TokenUrl = "https://github.com/login/oauth/access_token";
        public const string GrantType = "urn:ietf:params:oauth:grant-type:device_code";

        /// <summary>
        /// slow_down 을 받으면 묻는 간격을 이만큼 늘린다(GitHub 규칙).
        /// </summary>
        public const int SlowDownSeconds = 5;

        [Serializable]
        public sealed class DeviceCode
        {
            public string device_code;
            public string user_code;
            public string verification_uri;
            public int expires_in;
            public int interval;
        }

        [Serializable]
        public sealed class TokenReply
        {
            public string access_token;
            public string error;
            public string error_description;
        }

        public enum PollOutcome
        {
            /// <summary>
            /// 아직 승인하지 않았다. 같은 간격으로 다시 묻는다.
            /// </summary>
            Wait,

            /// <summary>
            /// 너무 자주 물었다. 간격을 늘려 다시 묻는다.
            /// </summary>
            SlowDown,

            Success,

            /// <summary>
            /// 거절·만료·잘못된 요청. 처음부터 다시 해야 한다.
            /// </summary>
            Failed,
        }

        public static DeviceCode ParseDeviceCode(string json)
        {
            DeviceCode code = JsonUtility.FromJson<DeviceCode>(json ?? string.Empty);
            return code == null || string.IsNullOrEmpty(code.device_code) || string.IsNullOrEmpty(code.user_code) ? null : code;
        }

        public static TokenReply ParseTokenReply(string json)
        {
            return JsonUtility.FromJson<TokenReply>(json ?? string.Empty) ?? new TokenReply { error = "invalid_response" };
        }

        public static PollOutcome Interpret(TokenReply reply)
        {
            if (string.IsNullOrEmpty(reply.access_token) == false)
            {
                return PollOutcome.Success;
            }
            switch (reply.error)
            {
                case "authorization_pending":
                    return PollOutcome.Wait;
                case "slow_down":
                    return PollOutcome.SlowDown;
                default:
                    return PollOutcome.Failed;
            }
        }

        /// <summary>
        /// GitHub 가 간격을 주지 않거나 0 이하로 주면 5초(문서의 최소값).
        /// </summary>
        public static int IntervalSeconds(int interval)
        {
            return interval > 0 ? interval : 5;
        }
    }
}
