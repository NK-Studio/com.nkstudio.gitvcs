using NUnit.Framework;
using UnityEngine;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 작업 막대·레이아웃의 순수 규칙: 터미널 실행 계획, 분할 칸 크기 맞춤.
    /// </summary>
    public sealed class ToolbarLogicTests
    {
        [Test]
        public void Test_TerminalPlan_WindowsWithWindowsTerminal_PassesDirectory()
        {
            GitTerminal.Launch launch = GitTerminal.Plan(RuntimePlatform.WindowsEditor, "C:/My Repo", "C:/wt.exe");

            Assert.AreEqual("C:/wt.exe", launch.FileName);
            Assert.AreEqual("-d \"C:/My Repo\"", launch.Arguments);
        }

        [Test]
        public void Test_TerminalPlan_WindowsWithoutWindowsTerminal_UsesPowerShell()
        {
            GitTerminal.Launch launch = GitTerminal.Plan(RuntimePlatform.WindowsEditor, "C:/repo", null);

            Assert.AreEqual("powershell.exe", launch.FileName);
        }

        [Test]
        public void Test_TerminalPlan_Mac_OpensTerminalApp()
        {
            GitTerminal.Launch launch = GitTerminal.Plan(RuntimePlatform.OSXEditor, "/Users/me/repo", null);

            Assert.AreEqual("open", launch.FileName);
            Assert.AreEqual("-a Terminal /Users/me/repo", launch.Arguments);
        }

        [TestCase(260f, 600f, 260f)]
        [TestCase(260f, 300f, 180f)]
        [TestCase(260f, 150f, 80f)]
        public void Test_SplitPaneClamp_Fit_LeavesRoomForFlexedPane(float preferred, float available, float expected)
        {
            Assert.AreEqual(expected, SplitPaneClamp.Fit(preferred, available, minFlexed: 120f, minFixed: 80f));
        }
    }
}
