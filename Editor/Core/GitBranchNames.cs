using System;
using System.Collections.Generic;
using System.Linq;

namespace GitVersionControl
{
    /// <summary>
    /// 브랜치 이름을 쓰는 동안 바로 알려 줄 문제. 만들 때는 git(<c>check-ref-format --branch</c>)이 한 번 더 확인한다.
    /// </summary>
    public enum BranchNameProblem
    {
        None,

        /// <summary>아직 아무것도 쓰지 않았다(안내 없이 만들기 버튼만 끈다).</summary>
        Empty,

        Whitespace,

        /// <summary>git 이 이름에 받지 않는 글자(~ ^ : ? * [ \ 와 제어 문자).</summary>
        InvalidCharacter,

        /// <summary>"-" 나 "/" 로 시작한다.</summary>
        BadStart,

        /// <summary>"/" · "." · ".lock" 으로 끝난다.</summary>
        BadEnd,

        /// <summary>"..", "//", "@{" 가 들어 있거나 이름이 "@" 하나다.</summary>
        BadSequence,

        /// <summary>폴더 조각("a/.b")이 "." 으로 시작한다.</summary>
        DotComponent,

        /// <summary>같은 이름의 로컬 브랜치가 이미 있다.</summary>
        Exists,
    }

    /// <summary>
    /// 브랜치 만들기 창이 쓰는 이름 검사(git-check-ref-format 의 규칙 중 자주 걸리는 것). 순수 함수라 테스트로 확인한다.
    /// </summary>
    public static class GitBranchNames
    {
        private static readonly char[] InvalidCharacters = { '~', '^', ':', '?', '*', '[', '\\' };

        /// <param name="existing">이미 있는 로컬 브랜치 이름. Windows 는 참조 파일 이름의 대소문자를 가리지 않으므로 대소문자 없이 비교한다.</param>
        public static BranchNameProblem Check(string name, IEnumerable<string> existing)
        {
            if (string.IsNullOrEmpty(name))
            {
                return BranchNameProblem.Empty;
            }
            if (name.Any(char.IsWhiteSpace))
            {
                return BranchNameProblem.Whitespace;
            }
            if (name.IndexOfAny(InvalidCharacters) >= 0 || name.Any(char.IsControl))
            {
                return BranchNameProblem.InvalidCharacter;
            }
            if (name.StartsWith("-", StringComparison.Ordinal) || name.StartsWith("/", StringComparison.Ordinal))
            {
                return BranchNameProblem.BadStart;
            }
            if (name.EndsWith("/", StringComparison.Ordinal) || name.EndsWith(".", StringComparison.Ordinal) || name.EndsWith(".lock", StringComparison.Ordinal))
            {
                return BranchNameProblem.BadEnd;
            }
            if (name == "@" || name.Contains("..") || name.Contains("//") || name.Contains("@{"))
            {
                return BranchNameProblem.BadSequence;
            }
            if (name.Split('/').Any(part => part.StartsWith(".", StringComparison.Ordinal)))
            {
                return BranchNameProblem.DotComponent;
            }
            if (existing != null && existing.Any(other => string.Equals(other, name, StringComparison.OrdinalIgnoreCase)))
            {
                return BranchNameProblem.Exists;
            }
            return BranchNameProblem.None;
        }
    }
}
