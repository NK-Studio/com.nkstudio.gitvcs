using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 패키지의 모든 선 아이콘(<see cref="ActionGlyph"/>)이 같은 굵기를 쓰는지 지키는 하네스.
    /// 아이콘마다 굵기를 따로 정하면(예: 1.75 칸, 채운 Material 윤곽 1.25 칸) 사이드바·버튼에서 나란히 놓였을 때 두께가 들쭉날쭉해진다.
    /// </summary>
    public sealed class ActionGlyphRulesTests
    {
        private const string SourcePath = "Packages/com.nkstudio.gitvcs/Editor/UI/Elements/ActionGlyph.cs";

        private static string Source()
        {
            string path = Path.GetFullPath(SourcePath);
            Assume.That(File.Exists(path), "ActionGlyph.cs 를 찾을 수 없음: " + path);
            return File.ReadAllText(path);
        }

        [Test]
        public void StrokeWidth_IsTheSharedConstant()
        {
            Assert.AreEqual(1.6f, ActionGlyph.StrokeUnits, "모든 아이콘이 기준으로 삼는 굵기. 바꾸면 모든 아이콘이 함께 바뀐다.");
        }

        [Test]
        public void EveryLineWidth_UsesStrokeUnits()
        {
            // 굵기를 정하는 줄은 모두 StrokeUnits 로 계산하거나 잠시 바꾼 값을 되돌리는 것뿐이어야 한다(숫자를 직접 적지 않는다).
            string[] offending = Regex.Matches(Source(), @"lineWidth\s*=\s*([^;]+);")
                .Cast<Match>()
                .Select(m => m.Groups[1].Value.Trim())
                .Where(value => value.Contains("StrokeUnits") == false && value != "width")
                .ToArray();
            CollectionAssert.IsEmpty(offending, "아이콘별 굵기 대신 ActionGlyph.StrokeUnits 를 쓴다.");
        }

        [Test]
        public void EveryKind_IsDrawn()
        {
            string source = Source();
            string[] missing = Enum.GetNames(typeof(ActionGlyphKind))
                .Where(name => source.Contains("case ActionGlyphKind." + name + ":") == false)
                .ToArray();
            CollectionAssert.IsEmpty(missing, "그리는 코드가 없는 아이콘 종류.");
        }
    }
}
