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

        [TestCase(1f, -120f, 1.1f)]
        [TestCase(1f, 3f, 0.9f)]
        [TestCase(1f, 0f, 1f)]
        [TestCase(2f, -1f, 2f)]
        [TestCase(0.6f, 1f, 0.6f)]
        public void Test_UiScale_NextScale_WheelUpEnlargesWithinRange(float current, float wheelDeltaY, float expected)
        {
            Assert.AreEqual(expected, UiScaleController.NextScale(current, wheelDeltaY), 0.0001f);
        }

        [TestCase(1.0999f, 1.1f)]
        [TestCase(5f, 2f)]
        [TestCase(0.1f, 0.6f)]
        public void Test_UiScale_Clamp_SnapsToStepAndRange(float scale, float expected)
        {
            Assert.AreEqual(expected, UiScaleController.Clamp(scale), 0.0001f);
        }

        [TestCase(true, "JetBrainsDark")]
        [TestCase(false, "JetBrainsLight")]
        public void Test_GitTheme_SheetName_FollowsBrightness(bool isDark, string expected)
        {
            Assert.AreEqual(expected, GitTheme.SheetName(isDark));
        }

        [TestCase(GitThemeMode.Dark, false, true)]
        [TestCase(GitThemeMode.Light, true, false)]
        [TestCase(GitThemeMode.System, true, true)]
        [TestCase(GitThemeMode.System, false, false)]
        public void Test_GitTheme_ResolveIsDark_SystemFollowsEditorSkin(GitThemeMode mode, bool editorIsDark, bool expected)
        {
            Assert.AreEqual(expected, GitTheme.ResolveIsDark(mode, editorIsDark));
        }

        [Test]
        public void Test_GitTheme_EverySheetExists()
        {
            foreach (bool isDark in new[] { true, false })
            {
                string path = $"Packages/com.nkstudio.gitvcs/Editor/UI/Themes/{GitTheme.SheetName(isDark)}.tss";
                Assert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.ThemeStyleSheet>(path), path);
            }
        }

        [TestCase(RuntimePlatform.WindowsEditor, "os.openInExplorer", "os.revealInExplorer")]
        [TestCase(RuntimePlatform.OSXEditor, "os.openInFinder", "os.revealInFinder")]
        [TestCase(RuntimePlatform.LinuxEditor, "os.openInFileManager", "os.revealInFileManager")]
        public void Test_FileManager_LabelsFollowOs(RuntimePlatform platform, string openKey, string revealKey)
        {
            Assert.AreEqual(openKey, GitFileManager.OpenLabelKey(platform));
            Assert.AreEqual(revealKey, GitFileManager.RevealLabelKey(platform));
        }

        [TestCase(380f, 50f, 0, 1000f, 430f)]
        [TestCase(380f, 50f, 1, 1000f, 330f)]
        [TestCase(380f, 900f, 0, 1000f, 880f)]
        [TestCase(380f, 900f, 1, 1000f, 80f)]
        public void Test_SplitPaneClamp_DraggedSize_FollowsPointerWithinLimits(float start, float delta, int fixedIndex, float available, float expected)
        {
            Assert.AreEqual(expected, SplitPaneClamp.DraggedSize(start, delta, fixedIndex, available));
        }

        [TestCase(260f, 600f, 260f)]
        [TestCase(260f, 300f, 180f)]
        [TestCase(260f, 150f, 80f)]
        public void Test_SplitPaneClamp_Fit_LeavesRoomForFlexedPane(float preferred, float available, float expected)
        {
            Assert.AreEqual(expected, SplitPaneClamp.Fit(preferred, available, minFlexed: 120f, minFixed: 80f));
        }

        [TestCase(900f, 2000f, 720f)]
        [TestCase(100f, 2000f, 280f)]
        [TestCase(500f, 2000f, 500f)]
        [TestCase(500f, 300f, 280f)]
        public void Test_SplitPaneClamp_Fit_KeepsPaneWithinStyledLimits(float preferred, float available, float expected)
        {
            // 커밋 상세 칸(min-width 280, max-width 720): 너무 좁게도 넓게도 끌리지 않고, 창이 좁으면 최소 폭을 지킨다.
            Assert.AreEqual(expected, SplitPaneClamp.Fit(preferred, available, minFlexed: 120f, minFixed: 280f, maxFixed: 720f));
            Assert.AreEqual(expected, SplitPaneClamp.DraggedSize(preferred, 0f, 1, available, 280f, 720f));
        }

        [TestCase(0.5f, 0.5f)]
        [TestCase(0.1f, 0.25f)]
        [TestCase(0.95f, 0.8f)]
        [TestCase(0f, 0.5f)]
        [TestCase(float.NaN, 0.5f)]
        public void Test_SplitPaneClamp_ClampRatio(float ratio, float expected)
        {
            // Fork 형태 탐색기의 아래 칸 비율: 25%~80%, 깨진 값(처음 · 0 · NaN)은 반.
            Assert.AreEqual(expected, SplitPaneClamp.ClampRatio(ratio));
        }

        [TestCase(900f, 1600f, 480f)]
        [TestCase(900f, 800f, 320f)]
        [TestCase(230f, 800f, 230f)]
        [TestCase(230f, 500f, 160f)]
        public void Test_SplitPaneClamp_DraggedSize_SidebarLeavesRoomForContent(float wanted, float available, float expected)
        {
            // 창 본체: 사이드바(앞 칸, 160~480)를 끌어도 본문(min-width 480)을 밀어내지 못하고, 창이 좁으면 사이드바 최소 폭이 우선한다.
            Assert.AreEqual(expected, SplitPaneClamp.DraggedSize(wanted, 0f, 0, available, 160f, 480f, 480f));
        }
    }
}
