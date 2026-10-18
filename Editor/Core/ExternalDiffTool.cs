using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 외부 비교 프로그램 프리셋. 인자 틀의 {LEFT}·{RIGHT} 는 비교할 두 파일, {LEFT_TITLE}·{RIGHT_TITLE} 은 창에 보일 이름이다.
    /// 프로그램 위치는 사용자가 정할 수 있고, 비워 두면 흔한 설치 위치와 PATH 에서 찾는다.
    /// </summary>
    public static class ExternalDiffTool
    {
        public const string LeftToken = "{LEFT}";
        public const string RightToken = "{RIGHT}";
        public const string LeftTitleToken = "{LEFT_TITLE}";
        public const string RightTitleToken = "{RIGHT_TITLE}";

        /// <summary>
        /// Custom 을 처음 고를 때 채워 둘 인자 틀.
        /// </summary>
        public const string DefaultCustomArguments = "\"{LEFT}\" \"{RIGHT}\"";

        public static bool IsExternal(DiffEngine engine)
        {
            return engine != DiffEngine.BuiltIn && engine != DiffEngine.GitDifftool;
        }

        /// <summary>
        /// 프리셋의 인자 틀. Custom 은 사용자가 쓴 것을 쓴다.
        /// </summary>
        public static string ArgumentTemplate(DiffEngine engine, RuntimePlatform platform)
        {
            switch (engine)
            {
                case DiffEngine.VisualStudioCode:
                    return "--diff \"{LEFT}\" \"{RIGHT}\"";
                case DiffEngine.Rider:
                    return "diff \"{LEFT}\" \"{RIGHT}\"";
                case DiffEngine.BeyondCompare:
                    // Windows 판은 /옵션, macOS 의 bcomp 는 -옵션을 받는다.
                    return platform == RuntimePlatform.WindowsEditor
                        ? "\"{LEFT}\" \"{RIGHT}\" /title1=\"{LEFT_TITLE}\" /title2=\"{RIGHT_TITLE}\""
                        : "\"{LEFT}\" \"{RIGHT}\" -title1=\"{LEFT_TITLE}\" -title2=\"{RIGHT_TITLE}\"";
                case DiffEngine.P4Merge:
                    return "-nl \"{LEFT_TITLE}\" -nr \"{RIGHT_TITLE}\" \"{LEFT}\" \"{RIGHT}\"";
                case DiffEngine.WinMerge:
                    return "/e /u /dl \"{LEFT_TITLE}\" /dr \"{RIGHT_TITLE}\" \"{LEFT}\" \"{RIGHT}\"";
                default:
                    throw new ArgumentOutOfRangeException(nameof(engine), engine, "No preset arguments.");
            }
        }

        /// <summary>
        /// 인자 틀의 자리표시자를 바꾼다. 따옴표는 틀이 감싸므로 값에 따옴표가 있으면 지운다(Windows 경로에는 올 수 없다).
        /// </summary>
        public static string Expand(string template, string left, string right, string leftTitle, string rightTitle)
        {
            static string Clean(string value) => (value ?? string.Empty).Replace("\"", string.Empty);
            return template
                .Replace(LeftTitleToken, Clean(leftTitle))
                .Replace(RightTitleToken, Clean(rightTitle))
                .Replace(LeftToken, Clean(left))
                .Replace(RightToken, Clean(right));
        }

        /// <summary>
        /// 프리셋 프로그램을 찾는다. 못 찾으면 null(사용자가 경로를 지정해야 한다).
        /// </summary>
        public static string FindExecutable(DiffEngine engine)
        {
            return Candidates(engine, Application.platform).FirstOrDefault(File.Exists) ?? FindOnPath(PathNames(engine, Application.platform));
        }

        private static IEnumerable<string> Candidates(DiffEngine engine, RuntimePlatform platform)
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            bool isWindows = platform == RuntimePlatform.WindowsEditor;
            bool isMac = platform == RuntimePlatform.OSXEditor;

            switch (engine)
            {
                case DiffEngine.VisualStudioCode:
                    if (isWindows)
                    {
                        yield return Path.Combine(localAppData, "Programs", "Microsoft VS Code", "Code.exe");
                        yield return Path.Combine(programFiles, "Microsoft VS Code", "Code.exe");
                    }
                    if (isMac)
                    {
                        yield return "/Applications/Visual Studio Code.app/Contents/Resources/app/bin/code";
                    }
                    break;
                case DiffEngine.Rider:
                    if (isWindows)
                    {
                        yield return Path.Combine(localAppData, "Programs", "Rider", "bin", "rider64.exe");
                        foreach (string path in NewestFirst(Path.Combine(programFiles, "JetBrains"), "JetBrains Rider*", Path.Combine("bin", "rider64.exe")))
                        {
                            yield return path;
                        }
                    }
                    if (isMac)
                    {
                        yield return "/Applications/Rider.app/Contents/MacOS/rider";
                    }
                    break;
                case DiffEngine.BeyondCompare:
                    if (isWindows)
                    {
                        yield return Path.Combine(programFiles, "Beyond Compare 5", "BCompare.exe");
                        yield return Path.Combine(programFiles, "Beyond Compare 4", "BCompare.exe");
                        yield return Path.Combine(programFilesX86, "Beyond Compare 4", "BCompare.exe");
                    }
                    if (isMac)
                    {
                        yield return "/Applications/Beyond Compare.app/Contents/MacOS/bcomp";
                    }
                    break;
                case DiffEngine.P4Merge:
                    if (isWindows)
                    {
                        yield return Path.Combine(programFiles, "Perforce", "p4merge.exe");
                    }
                    if (isMac)
                    {
                        yield return "/Applications/p4merge.app/Contents/MacOS/p4merge";
                    }
                    break;
                case DiffEngine.WinMerge:
                    if (isWindows)
                    {
                        yield return Path.Combine(programFiles, "WinMerge", "WinMergeU.exe");
                        yield return Path.Combine(localAppData, "Programs", "WinMerge", "WinMergeU.exe");
                    }
                    break;
            }
        }

        private static string[] PathNames(DiffEngine engine, RuntimePlatform platform)
        {
            bool isWindows = platform == RuntimePlatform.WindowsEditor;
            switch (engine)
            {
                case DiffEngine.VisualStudioCode:
                    return isWindows ? new[] { "Code.exe" } : new[] { "code" };
                case DiffEngine.Rider:
                    return isWindows ? new[] { "rider64.exe" } : new[] { "rider" };
                case DiffEngine.BeyondCompare:
                    return isWindows ? new[] { "BCompare.exe" } : new[] { "bcomp", "bcompare" };
                case DiffEngine.P4Merge:
                    return isWindows ? new[] { "p4merge.exe" } : new[] { "p4merge" };
                case DiffEngine.WinMerge:
                    return isWindows ? new[] { "WinMergeU.exe" } : Array.Empty<string>();
                default:
                    return Array.Empty<string>();
            }
        }

        /// <summary>
        /// 버전마다 폴더 이름이 다른 설치(JetBrains Rider 2025.2 등)는 이름순 끝(=최신)부터 본다.
        /// </summary>
        private static IEnumerable<string> NewestFirst(string parent, string pattern, string relative)
        {
            if (Directory.Exists(parent) == false)
            {
                return Array.Empty<string>();
            }
            return Directory.GetDirectories(parent, pattern).OrderByDescending(dir => dir, StringComparer.OrdinalIgnoreCase).Select(dir => Path.Combine(dir, relative));
        }

        private static string FindOnPath(IEnumerable<string> names)
        {
            string[] directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator);
            foreach (string name in names)
            {
                foreach (string directory in directories)
                {
                    if (string.IsNullOrWhiteSpace(directory))
                    {
                        continue;
                    }
                    string candidate = Path.Combine(directory.Trim(), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
            }
            return null;
        }
    }
}
