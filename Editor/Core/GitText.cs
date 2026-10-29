using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    public enum GitLanguage
    {
        Korean,
        English,
    }

    /// <summary>
    /// 화면 글자 번역. 번역문은 <see cref="GitStrings"/> 표 한곳에 두고, 코드는 키로만 찾는다.
    /// 새 언어를 더하려면 <see cref="GitLanguage"/> 와 표의 열만 늘리면 된다.
    ///
    /// git 은 백그라운드 스레드에서도 메시지를 만들기 때문에, 현재 언어는 메인 스레드에서 정적 필드로 복사해 두고 그 값만 읽는다
    /// (ScriptableSingleton 을 다른 스레드에서 건드리지 않으려고).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitText
    {
        private static volatile int s_language = -1;

        /// <summary>언어가 바뀐 뒤(메인 스레드). 창·설정 화면이 글자를 다시 그린다.</summary>
        public static event Action LanguageChanged;

        public static GitLanguage Language
        {
            get
            {
                if (s_language < 0)
                {
                    s_language = (int)GitVcsSettings.instance.Language;
                }
                return (GitLanguage)s_language;
            }
        }

        [InitializeOnLoadMethod]
        private static void LoadLanguage()
        {
            s_language = (int)GitVcsSettings.instance.Language;
        }

        public static void SetLanguage(GitLanguage language)
        {
            if (Language == language)
            {
                return;
            }
            GitVcsSettings.instance.Language = language;
            s_language = (int)language;
            LanguageChanged?.Invoke();
        }

        /// <summary>시스템 언어가 한국어면 한국어, 아니면 영어.</summary>
        public static GitLanguage SystemDefault => Application.systemLanguage == SystemLanguage.Korean ? GitLanguage.Korean : GitLanguage.English;

        /// <summary>
        /// 키의 번역문. 없는 키는 키를 그대로 돌려준다 — 고유명사("Homebrew")는 표에 넣지 않아도 되고, 빠진 번역은 화면에서 바로 눈에 띈다.
        /// </summary>
        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }
            if (GitStrings.Table.TryGetValue(key, out string[] texts) == false)
            {
                return key;
            }
            int index = (int)Language;
            return index < texts.Length && string.IsNullOrEmpty(texts[index]) == false ? texts[index] : texts[0];
        }

        public static string Format(string key, params object[] args)
        {
            string text = string.Format(Get(key), args);
            return Language == GitLanguage.English ? SingularizeOne(text) : text;
        }

        /// <summary>
        /// 영어 문구의 "1 commits" 를 "1 commit" 으로. 문구마다 단수·복수 키를 따로 두지 않으려고, 숫자 1 바로 뒤의 정해진 명사만 바꾼다
        /// ("#1"·"v1" 같은 번호와 "1 is" 같은 다른 낱말은 그대로). 한국어는 단수·복수가 없어 손대지 않는다. 순수 함수라 테스트로 확인한다.
        /// </summary>
        public static string SingularizeOne(string text)
        {
            return string.IsNullOrEmpty(text) || text.IndexOf('1') < 0 ? text : OnePlural.Replace(text, match => "1 " + Singular[match.Groups[1].Value]);
        }

        private static readonly Dictionary<string, string> Singular = new()
        {
            ["commits"] = "commit", ["files"] = "file", ["comments"] = "comment", ["changes"] = "change", ["lines"] = "line",
            ["conflicts"] = "conflict", ["branches"] = "branch", ["stashes"] = "stash", ["tags"] = "tag", ["remotes"] = "remote",
            ["items"] = "item", ["entries"] = "entry", ["hunks"] = "hunk", ["lanes"] = "lane", ["assets"] = "asset",
            ["days"] = "day", ["hours"] = "hour", ["minutes"] = "minute", ["seconds"] = "second", ["reviews"] = "review",
        };

        // 앞이 글자·숫자·'#'·'.' 이 아닌 "1" 뒤에 한 칸 띄고 정해진 명사가 오는 곳.
        private static readonly System.Text.RegularExpressions.Regex OnePlural = new(
            @"(?<![\w#.])1 (" + string.Join("|", Singular.Keys) + @")\b", System.Text.RegularExpressions.RegexOptions.Compiled);

        /// <summary>UXML 의 <c>text="@키"</c> 처럼 '@' 로 시작하면 번역하고, 아니면 그대로 둔다.</summary>
        public static string Resolve(string text)
        {
            return text != null && text.Length > 1 && text[0] == '@' ? Get(text.Substring(1)) : text;
        }

        /// <summary>언어 선택 메뉴에 보일 이름. 각 언어 자기 말로 적어 어떤 언어로 보고 있든 찾을 수 있게 한다.</summary>
        public static string NativeName(GitLanguage language)
        {
            return language == GitLanguage.Korean ? "한국어" : "English";
        }

        public static IEnumerable<GitLanguage> AllLanguages => (GitLanguage[])Enum.GetValues(typeof(GitLanguage));
    }
}
