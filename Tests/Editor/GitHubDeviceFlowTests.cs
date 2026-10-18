using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// GitHub 로그인(Device Flow) 응답 읽기와, 승인을 기다리는 동안 다음에 할 일.
    /// </summary>
    public sealed class GitHubDeviceFlowTests
    {
        [Test]
        public void Test_ParseDeviceCode_ReadsCodeAndInterval()
        {
            GitHubDeviceFlow.DeviceCode code = GitHubDeviceFlow.ParseDeviceCode(
                "{\"device_code\":\"d1\",\"user_code\":\"WDJB-MJHT\",\"verification_uri\":\"https://github.com/login/device\",\"expires_in\":900,\"interval\":5}");

            Assert.AreEqual("d1", code.device_code);
            Assert.AreEqual("WDJB-MJHT", code.user_code);
            Assert.AreEqual("https://github.com/login/device", code.verification_uri);
            Assert.AreEqual(900, code.expires_in);
            Assert.AreEqual(5, code.interval);
        }

        [Test]
        public void Test_ParseDeviceCode_ErrorReply_ReturnsNull()
        {
            Assert.IsNull(GitHubDeviceFlow.ParseDeviceCode("{\"error\":\"device_flow_disabled\"}"));
        }

        [TestCase("{\"access_token\":\"gho_x\",\"token_type\":\"bearer\"}", GitHubDeviceFlow.PollOutcome.Success)]
        [TestCase("{\"error\":\"authorization_pending\"}", GitHubDeviceFlow.PollOutcome.Wait)]
        [TestCase("{\"error\":\"slow_down\",\"interval\":10}", GitHubDeviceFlow.PollOutcome.SlowDown)]
        [TestCase("{\"error\":\"access_denied\"}", GitHubDeviceFlow.PollOutcome.Failed)]
        [TestCase("{\"error\":\"expired_token\"}", GitHubDeviceFlow.PollOutcome.Failed)]
        public void Test_Interpret_MapsGitHubReply(string json, GitHubDeviceFlow.PollOutcome expected)
        {
            Assert.AreEqual(expected, GitHubDeviceFlow.Interpret(GitHubDeviceFlow.ParseTokenReply(json)));
        }

        [TestCase(0, 5)]
        [TestCase(8, 8)]
        public void Test_IntervalSeconds_FallsBackToFive(int interval, int expected)
        {
            Assert.AreEqual(expected, GitHubDeviceFlow.IntervalSeconds(interval));
        }
    }
}
