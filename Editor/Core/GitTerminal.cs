using System;
using System.Diagnostics;
using System.IO;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// OS 파일 관리자(Windows 파일 탐색기, macOS Finder, Linux 파일 관리자). 메뉴 문구도 OS 이름을 따른다.
    /// </summary>
    public static class GitFileManager
    {
        /// <summary>
        /// "폴더 열기" 문구 키. 예: Windows → "os.openInExplorer".
        /// </summary>
        public static string OpenLabelKey(RuntimePlatform platform)
        {
            return platform switch
            {
                RuntimePlatform.WindowsEditor => "os.openInExplorer",
                RuntimePlatform.OSXEditor => "os.openInFinder",
                _ => "os.openInFileManager",
            };
        }

        /// <summary>
        /// "파일을 골라 보여 주기" 문구 키. 예: macOS → "os.revealInFinder".
        /// </summary>
        public static string RevealLabelKey(RuntimePlatform platform)
        {
            return platform switch
            {
                RuntimePlatform.WindowsEditor => "os.revealInExplorer",
                RuntimePlatform.OSXEditor => "os.revealInFinder",
                _ => "os.revealInFileManager",
            };
        }

        public static string OpenLabel => GitText.Get(OpenLabelKey(Application.platform));
        public static string RevealLabel => GitText.Get(RevealLabelKey(Application.platform));

        /// <summary>
        /// 폴더 자체를 연다(그 폴더 안이 보인다).
        /// </summary>
        public static void OpenFolder(string fullPath)
        {
            UnityEditor.EditorUtility.OpenWithDefaultApp(fullPath);
        }

        /// <summary>
        /// 파일·폴더가 든 곳을 열고 그것을 골라 둔다.
        /// </summary>
        public static void Reveal(string fullPath)
        {
            UnityEditor.EditorUtility.RevealInFinder(fullPath);
        }
    }

    /// <summary>
    /// 저장소 폴더에서 OS 터미널을 연다. Windows 는 Windows Terminal 이 있으면 그것을, 없으면 PowerShell 을 쓴다.
    /// </summary>
    public static class GitTerminal
    {
        /// <summary>
        /// 실행할 프로그램과 인자. 파일을 건드리지 않는 순수 규칙이라 테스트로 확인한다.
        /// </summary>
        public readonly struct Launch
        {
            public readonly string FileName;
            public readonly string Arguments;

            public Launch(string fileName, string arguments)
            {
                FileName = fileName;
                Arguments = arguments;
            }
        }

        /// <param name="windowsTerminalPath">찾은 wt.exe 경로. 없으면 null.</param>
        public static Launch Plan(RuntimePlatform platform, string directory, string windowsTerminalPath)
        {
            switch (platform)
            {
                case RuntimePlatform.WindowsEditor:
                    // PowerShell 은 작업 폴더를 ProcessStartInfo.WorkingDirectory 로 받는다.
                    return windowsTerminalPath != null
                        ? new Launch(windowsTerminalPath, GitCommand.BuildArguments(new[] { "-d", directory }))
                        : new Launch("powershell.exe", "-NoExit");
                case RuntimePlatform.OSXEditor:
                    return new Launch("open", GitCommand.BuildArguments(new[] { "-a", "Terminal", directory }));
                default:
                    return new Launch("x-terminal-emulator", string.Empty);
            }
        }

        /// <summary>
        /// 터미널을 연다. 실패하면 이유를 돌려주고, 성공하면 null.
        /// </summary>
        public static string Open(string directory)
        {
            Launch launch = Plan(Application.platform, directory, FindWindowsTerminal());
            try
            {
                ProcessStartInfo startInfo = new(launch.FileName, launch.Arguments)
                {
                    WorkingDirectory = directory,
                    UseShellExecute = true,
                };
                Process.Start(startInfo)?.Dispose();
                return null;
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is InvalidOperationException || e is FileNotFoundException)
            {
                return $"{launch.FileName}: {e.Message}";
            }
        }

        /// <summary>
        /// Windows Terminal 은 스토어 앱이라 PATH 대신 사용자별 WindowsApps 폴더에 실행 별칭(wt.exe)을 둔다.
        /// </summary>
        private static string FindWindowsTerminal()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return null;
            }
            string alias = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps", "wt.exe");
            return File.Exists(alias) ? alias : null;
        }
    }
}
