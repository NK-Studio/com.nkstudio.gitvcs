using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 메인 도구 막대 Git 버튼의 순수 규칙: 버튼 글자·툴팁, 메뉴 순서·구분선·하위 메뉴.
    /// 문구는 키를 그대로 돌려주는 함수로 만들어 언어 설정과 무관하게 검사한다.
    /// </summary>
    public sealed class MainToolbarMenuTests
    {
        private static string KeyAsText(string key) => key;

        [Test]
        public void Test_ButtonText_NoBranch_ShowsGitOnly()
        {
            Assert.AreEqual("Git", GitMainToolbarMenu.ButtonText(null));
            Assert.AreEqual("Git", GitMainToolbarMenu.ButtonText("  "));
        }

        [Test]
        public void Test_ButtonText_WithBranch_AppendsBranch()
        {
            Assert.AreEqual("Git · main", GitMainToolbarMenu.ButtonText("main"));
        }

        [Test]
        public void Test_ButtonText_LongBranch_IsShortenedToMaxLength()
        {
            string branch = new('a', GitMainToolbarMenu.MaxBranchLength + 5);

            string text = GitMainToolbarMenu.ButtonText(branch);

            string shown = text.Substring("Git · ".Length);
            Assert.AreEqual(GitMainToolbarMenu.MaxBranchLength, shown.Length);
            Assert.IsTrue(shown.EndsWith("…"));
        }

        [Test]
        public void Test_ButtonText_BranchAtMaxLength_IsNotShortened()
        {
            string branch = new('a', GitMainToolbarMenu.MaxBranchLength);

            Assert.AreEqual("Git · " + branch, GitMainToolbarMenu.ButtonText(branch));
        }

        [Test]
        public void Test_Tooltip_WithBranch_UsesBranchFormat()
        {
            string tooltip = GitMainToolbarMenu.Tooltip("main", key => key == "mainToolbar.tooltipBranch" ? "branch: {0}" : key);

            Assert.AreEqual("branch: main", tooltip);
        }

        [Test]
        public void Test_Tooltip_NoBranch_UsesPlainTooltip()
        {
            Assert.AreEqual("mainToolbar.tooltip", GitMainToolbarMenu.Tooltip(null, KeyAsText));
        }

        [Test]
        public void Test_Build_CommandsInUnityVcsOrder()
        {
            IReadOnlyList<GitMainToolbarMenuItem> items = GitMainToolbarMenu.Build(KeyAsText);

            GitMainToolbarCommand[] commands = items.Where(item => item.IsSeparator == false).Select(item => item.Command).ToArray();
            CollectionAssert.AreEqual(new[]
            {
                GitMainToolbarCommand.OpenWindow,
                GitMainToolbarCommand.HideFromToolbar,
                GitMainToolbarCommand.OpenProjectSettings,
                GitMainToolbarCommand.OpenPreferences,
                GitMainToolbarCommand.OpenHelp,
            }, commands);
        }

        [Test]
        public void Test_Build_SeparatorsBetweenGroups_NotAtEnds()
        {
            IReadOnlyList<GitMainToolbarMenuItem> items = GitMainToolbarMenu.Build(KeyAsText);

            Assert.IsFalse(items[0].IsSeparator);
            Assert.IsFalse(items[items.Count - 1].IsSeparator);
            Assert.AreEqual(3, items.Count(item => item.IsSeparator));
            for (int i = 1; i < items.Count; i++)
            {
                Assert.IsFalse(items[i].IsSeparator && items[i - 1].IsSeparator, $"구분선이 {i} 에서 연달아 나온다.");
            }
        }

        [Test]
        public void Test_Build_SettingsItems_AreInSettingsSubmenu()
        {
            IReadOnlyList<GitMainToolbarMenuItem> items = GitMainToolbarMenu.Build(KeyAsText);

            GitMainToolbarMenuItem project = items.Single(item => item.IsSeparator == false && item.Command == GitMainToolbarCommand.OpenProjectSettings);
            GitMainToolbarMenuItem preferences = items.Single(item => item.IsSeparator == false && item.Command == GitMainToolbarCommand.OpenPreferences);
            Assert.AreEqual("mainToolbar.settings/mainToolbar.projectSettings", project.Path);
            Assert.AreEqual("mainToolbar.settings/mainToolbar.preferences", preferences.Path);
        }

        [Test]
        public void Test_Build_EveryKey_IsInTranslationTable()
        {
            List<string> usedKeys = new();
            GitMainToolbarMenu.Build(key =>
            {
                usedKeys.Add(key);
                return key;
            });
            usedKeys.AddRange(new[] { "mainToolbar.tooltip", "mainToolbar.tooltipBranch", "mainToolbar.hidden", "mainToolbar.attachFailed", "settings.mainToolbar", "settings.mainToolbarTooltip" });

            foreach (string key in usedKeys)
            {
                // GitText.Get 은 표에 없는 키를 그대로 돌려준다.
                Assert.AreNotEqual(key, GitText.Get(key), $"{key} 가 번역 표에 없다.");
            }
        }

        [Test]
        public void Test_SettingsPaths_MatchRegisteredProviders()
        {
            Assert.AreEqual("Project/Version Control", GitMainToolbarMenu.ProjectSettingsPath);
            Assert.AreEqual("Preferences/Git", GitMainToolbarMenu.PreferencesPath);
            Assert.AreEqual("https://github.com/NK-Studio/com.nkstudio.gitvcs", GitMainToolbarMenu.HelpUrl);
        }
    }
}
