using System;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 파일 히스토리 카드의 "2시간 전" 같은 상대 시간(<see cref="GitDisplay.RelativeTime"/>). 문구는 지금 언어의 번역문과 비교한다.
    /// </summary>
    public sealed class GitRelativeTimeTests
    {
        private static readonly DateTime Now = new(2026, 10, 26, 12, 0, 0);

        [Test]
        public void Test_RelativeTime_UnderAMinute_IsJustNow()
        {
            Assert.AreEqual(GitText.Get("time.justNow"), GitDisplay.RelativeTime(Now.AddSeconds(-30), Now));
        }

        [Test]
        public void Test_RelativeTime_FutureDate_IsJustNow()
        {
            Assert.AreEqual(GitText.Get("time.justNow"), GitDisplay.RelativeTime(Now.AddMinutes(5), Now));
        }

        [TestCase(-5, "time.minutesAgo", 5)]
        [TestCase(-59, "time.minutesAgo", 59)]
        [TestCase(-60, "time.hoursAgo", 1)]
        [TestCase(-60 * 23, "time.hoursAgo", 23)]
        [TestCase(-60 * 24, "time.daysAgo", 1)]
        [TestCase(-60 * 24 * 29, "time.daysAgo", 29)]
        [TestCase(-60 * 24 * 30, "time.monthsAgo", 1)]
        [TestCase(-60 * 24 * 364, "time.monthsAgo", 12)]
        [TestCase(-60 * 24 * 365 * 2, "time.yearsAgo", 2)]
        public void Test_RelativeTime_PicksLargestUnit(int minutes, string key, int amount)
        {
            Assert.AreEqual(GitText.Format(key, amount), GitDisplay.RelativeTime(Now.AddMinutes(minutes), Now));
        }

        [Test]
        public void Test_RelativeTime_MinValue_IsEmpty()
        {
            Assert.AreEqual(string.Empty, GitDisplay.RelativeTime(DateTime.MinValue, Now));
        }
    }
}
