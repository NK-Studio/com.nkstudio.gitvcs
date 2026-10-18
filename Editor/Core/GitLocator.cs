using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace GitVersionControl
{
    /// <summary>
    /// 실행할 git 을 찾는다. Unity 에는 git 이 들어 있지 않으므로 머신에 설치된 것을 쓴다.
    ///
    /// 에디터 프로세스의 PATH 만 보면 놓치는 경우가 많다.
    /// - Windows: git 설치 전에 켠 Hub·에디터, 다른 프로그램이 줄인 환경으로 띄운 에디터는 PATH 에 git 이 없다.
    ///   → 레지스트리의 사용자·시스템 PATH(지금 값)와 기본 설치 위치를 본다.
    /// - macOS: Dock·Hub 로 띄운 앱은 셸 설정을 읽지 않아 PATH 가 <c>/usr/bin:/bin:/usr/sbin:/sbin</c> 뿐이다.
    ///   그 PATH 에서 찾히는 <c>/usr/bin/git</c> 은 Xcode Command Line Tools 의 껍데기라, 도구가 없으면 설치 창을 띄우고 실패한다.
    ///   → Homebrew·MacPorts 와 로그인 셸의 PATH 를 먼저 보고, /usr/bin/git 은 Command Line Tools 가 설치돼 있을 때만 쓴다.
    /// </summary>
    public static class GitLocator
    {
        /// <summary>
        /// 찾은 결과. 백그라운드에서 만들어 캐시하므로 문구 대신 번역 키를 담고, 읽을 때 현재 언어로 바꾼다.
        /// 출처가 고유명사("Homebrew")면 키 자리에 그대로 둔다(<see cref="GitText.Get"/> 는 없는 키를 그대로 돌려준다).
        /// </summary>
        public readonly struct Location
        {
            public readonly string Path;
            private readonly string _sourceKey;
            private readonly string _hintKey;
            private readonly string _hintArgument;

            public Location(string path, string sourceKey, string hintKey = null, string hintArgument = null)
            {
                Path = path;
                _sourceKey = sourceKey;
                _hintKey = hintKey;
                _hintArgument = hintArgument;
            }

            public bool Found => _sourceKey != null;

            /// <summary>어디서 찾았는지(설정에 보여 준다). 못 찾았으면 null.</summary>
            public string Source => _sourceKey == null ? null : GitText.Get(_sourceKey);

            /// <summary>못 찾았을 때 사용자에게 보여 줄 해결 방법.</summary>
            public string Hint => _hintKey == null ? null : _hintArgument == null ? GitText.Get(_hintKey) : GitText.Format(_hintKey, _hintArgument);
        }

        private const string XcodeShim = "/usr/bin/git";
        private const int LoginShellTimeoutMs = 3000;

        private static readonly object Lock = new();
        private static string s_cachedConfigured;
        private static Location? s_cached;
        private static string s_searchPath;
        private static string[] s_loginShellPath;

        private static bool IsWindows => Environment.OSVersion.Platform == PlatformID.Win32NT;

        // Mono 는 macOS 에서도 PlatformID.Unix 를 돌려주므로 OS 고유 경로로 구분한다.
        private static bool IsMac => IsWindows == false && Directory.Exists("/System/Library/CoreServices");

        /// <summary>설정 값이 바뀌기 전까지는 처음 찾은 결과를 쓴다. 매 git 호출마다 디스크를 뒤지지 않으려고.</summary>
        public static Location Resolve(string configured)
        {
            lock (Lock)
            {
                if (s_cached.HasValue && s_cachedConfigured == configured)
                {
                    return s_cached.Value;
                }
            }

            // macOS 의 로그인 셸 조회는 수 초 걸릴 수 있으므로 잠금 밖에서 찾는다.
            Location found = Find(configured);
            lock (Lock)
            {
                s_cached = found;
                s_cachedConfigured = configured;
                return found;
            }
        }

        /// <summary>
        /// UI 용: 찾아 둔 결과가 있으면 바로 돌려주고, 없으면 백그라운드에서 찾기 시작하고 false 를 돌려준다.
        /// 설정 창을 그릴 때 로그인 셸을 기다리느라 에디터가 멈추지 않게 한다.
        /// </summary>
        public static bool TryGetCached(string configured, out Location location, Action onResolved = null)
        {
            lock (Lock)
            {
                if (s_cached.HasValue && s_cachedConfigured == configured)
                {
                    location = s_cached.Value;
                    return true;
                }
            }
            location = default;
            Task.Run(() => Resolve(configured)).ContinueWith(_ => onResolved?.Invoke(), TaskScheduler.FromCurrentSynchronizationContext());
            return false;
        }

        /// <summary>git 을 새로 설치했거나 PATH 를 바꾼 뒤 다시 찾게 한다.</summary>
        public static void ClearCache()
        {
            lock (Lock)
            {
                s_cachedConfigured = null;
                s_cached = null;
                s_searchPath = null;
                s_loginShellPath = null;
            }
        }

        /// <summary>
        /// git 자식 프로세스에 넘길 PATH. 에디터 PATH 에 git 을 찾은 곳들을 합쳐서, git 이 다시 부르는
        /// git-lfs·credential manager·ssh 도 찾게 한다.
        /// </summary>
        public static string SearchPath
        {
            get
            {
                lock (Lock)
                {
                    if (s_searchPath != null)
                    {
                        return s_searchPath;
                    }
                }
                string built = string.Join(Path.PathSeparator.ToString(), CandidateDirectories().Distinct(StringComparer.OrdinalIgnoreCase));
                lock (Lock)
                {
                    s_searchPath = built;
                    return built;
                }
            }
        }

        private static Location Find(string configured)
        {
            string value = string.IsNullOrWhiteSpace(configured) ? "git" : configured.Trim();

            // 경로를 직접 적었으면 그대로 쓴다.
            if (value.IndexOfAny(new[] { '/', '\\' }) >= 0)
            {
                if (File.Exists(value) == false)
                {
                    return new Location(value, null, "locator.missingFile", value);
                }
                if (IsMac && value == XcodeShim && HasXcodeDeveloperTools() == false)
                {
                    return new Location(value, null, XcodeHint);
                }
                return new Location(value, "locator.sourceSettings");
            }

            string name = IsWindows && value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) == false ? value + ".exe" : value;
            bool skippedXcodeShim = false;

            foreach ((string directory, string source) in SearchOrder())
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(directory, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (File.Exists(candidate) == false)
                {
                    continue;
                }
                if (IsMac && candidate == XcodeShim && HasXcodeDeveloperTools() == false)
                {
                    skippedXcodeShim = true;
                    continue;
                }
                return new Location(candidate, source);
            }

            string hint = IsWindows
                ? "locator.notFoundWindows"
                : skippedXcodeShim
                    ? XcodeHint
                    : IsMac
                        ? "locator.notFoundMac"
                        : "locator.notFoundLinux";
            return new Location(value, null, hint);
        }

        private const string XcodeHint =
            "locator.xcodeMissing";

        /// <summary>
        /// (폴더, 출처) 를 우선순위 순서로. 같은 폴더가 여러 번 나와도 첫 번째만 의미가 있다.
        /// macOS 는 Homebrew 를 에디터 PATH 보다 앞에 둔다 — 에디터 PATH 에는 대개 /usr/bin(Xcode 껍데기)만 있기 때문이다.
        /// </summary>
        private static IEnumerable<(string directory, string source)> SearchOrder()
        {
            if (IsMac)
            {
                foreach (string directory in new[] { "/opt/homebrew/bin", "/usr/local/bin" })
                {
                    yield return (directory, "Homebrew");
                }
                yield return ("/opt/local/bin", "MacPorts");
                foreach (string directory in LoginShellPath())
                {
                    yield return (directory, "locator.sourceLoginShell");
                }
                foreach (string directory in Split(Environment.GetEnvironmentVariable("PATH")))
                {
                    yield return (directory, "locator.sourceEditorPath");
                }
                yield return ("/Applications/Xcode.app/Contents/Developer/usr/bin", "Xcode");
                yield return ("/Library/Developer/CommandLineTools/usr/bin", "Xcode Command Line Tools");
                yield return ("/usr/bin", "Xcode Command Line Tools");
                yield break;
            }

            foreach (string directory in Split(Environment.GetEnvironmentVariable("PATH")))
            {
                yield return (directory, "locator.sourceEditorPath");
            }

            if (IsWindows)
            {
                foreach (string directory in Split(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User))
                             .Concat(Split(Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine))))
                {
                    yield return (directory, "locator.sourceSystemPath");
                }
                foreach (string root in new[]
                         {
                             Environment.GetEnvironmentVariable("ProgramFiles"),
                             Environment.GetEnvironmentVariable("ProgramW6432"),
                             Environment.GetEnvironmentVariable("ProgramFiles(x86)"),
                             Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs"),
                         })
                {
                    if (string.IsNullOrEmpty(root) == false)
                    {
                        yield return (Path.Combine(root, "Git", "cmd"), "locator.sourceDefault");
                    }
                }
                yield break;
            }

            foreach (string directory in new[] { "/usr/local/bin", "/usr/bin", "/bin" })
            {
                yield return (directory, "locator.sourceDefault");
            }
        }

        private static IEnumerable<string> CandidateDirectories()
        {
            // 자식 프로세스 PATH 는 에디터 PATH 를 앞에 두어 원래 동작을 바꾸지 않고, 찾은 곳들을 뒤에 붙인다.
            foreach (string directory in Split(Environment.GetEnvironmentVariable("PATH")))
            {
                yield return directory;
            }
            List<string> found = new();
            foreach ((string directory, string _) in SearchOrder())
            {
                found.Add(directory);
                yield return directory;
            }
            // Git for Windows 의 셸 스크립트 명령(git submodule 등)은 sh·sed·basename 을 PATH 에서 찾는다. 그 도구가 있는 usr/bin 을
            // 맨 뒤에 붙인다 — 맨 뒤라 에디터 PATH 의 같은 이름 프로그램(find·sort 등)을 가리지 않는다.
            foreach (string directory in Split(Environment.GetEnvironmentVariable("PATH")).Concat(found).ToList())
            {
                foreach (string tools in GitForWindowsTools(directory))
                {
                    yield return tools;
                }
            }
        }

        /// <summary>
        /// git.exe 가 있는 Git for Windows 폴더(cmd, bin, mingw64/bin)라면 셸 스크립트 명령에 필요한 폴더들:
        /// usr/bin(sh·sed·basename), mingw64/libexec/git-core(git-sh-setup), mingw64/bin. 아니면 없음.
        /// </summary>
        private static IEnumerable<string> GitForWindowsTools(string directory)
        {
            if (IsWindows == false || string.IsNullOrEmpty(directory))
            {
                return Array.Empty<string>();
            }
            try
            {
                string full = Path.GetFullPath(directory).TrimEnd('\\', '/');
                if (File.Exists(Path.Combine(full, "git.exe")) == false)
                {
                    return Array.Empty<string>();
                }
                DirectoryInfo folder = new(full);
                DirectoryInfo root = folder.Name.Equals("bin", StringComparison.OrdinalIgnoreCase) && folder.Parent != null
                                     && folder.Parent.Name.Equals("mingw64", StringComparison.OrdinalIgnoreCase)
                    ? folder.Parent.Parent
                    : folder.Parent;
                if (root == null || File.Exists(Path.Combine(root.FullName, "usr", "bin", "sh.exe")) == false)
                {
                    return Array.Empty<string>();
                }
                return new[]
                {
                    Path.Combine(root.FullName, "usr", "bin"),
                    Path.Combine(root.FullName, "mingw64", "libexec", "git-core"),
                    Path.Combine(root.FullName, "mingw64", "bin"),
                }.Where(Directory.Exists).ToArray();
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is NotSupportedException || e is UnauthorizedAccessException)
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Command Line Tools 나 Xcode 가 실제로 설치돼 있는지. 없으면 /usr/bin/git 을 실행하는 순간 설치 창이 뜨고 실패한다.
        /// </summary>
        private static bool HasXcodeDeveloperTools()
        {
            return File.Exists("/Library/Developer/CommandLineTools/usr/bin/git")
                   || File.Exists("/Applications/Xcode.app/Contents/Developer/usr/bin/git");
        }

        /// <summary>
        /// macOS: 사용자의 로그인 셸이 만드는 PATH(.zprofile 의 brew shellenv, asdf, nix 등). Dock 으로 띄운 에디터는 이것을 모른다.
        /// 셸 설정이 멈추거나 느려도 에디터가 기다리지 않도록 시간 제한을 둔다. 실패하면 빈 목록.
        /// </summary>
        private static string[] LoginShellPath()
        {
            lock (Lock)
            {
                if (s_loginShellPath != null)
                {
                    return s_loginShellPath;
                }
            }

            string[] result = Array.Empty<string>();
            try
            {
                string shell = Environment.GetEnvironmentVariable("SHELL");
                if (string.IsNullOrEmpty(shell) || File.Exists(shell) == false)
                {
                    shell = "/bin/zsh";
                }
                ProcessStartInfo startInfo = new(shell, "-l -c \"printf '%s' \\\"$PATH\\\"\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using Process process = Process.Start(startInfo);
                if (process != null)
                {
                    process.StandardInput.Close();
                    Task<string> output = Task.Factory.StartNew(() => process.StandardOutput.ReadToEnd(), TaskCreationOptions.LongRunning);
                    Task.Factory.StartNew(() => process.StandardError.ReadToEnd(), TaskCreationOptions.LongRunning);
                    if (process.WaitForExit(LoginShellTimeoutMs) && output.Wait(500))
                    {
                        result = Split(output.Result.Trim()).ToArray();
                    }
                    else
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }
                }
            }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception || e is IOException || e is InvalidOperationException)
            {
                // 셸을 못 띄워도 다른 후보로 찾는다.
            }

            lock (Lock)
            {
                s_loginShellPath = result;
                return result;
            }
        }

        private static IEnumerable<string> Split(string pathVariable)
        {
            if (string.IsNullOrEmpty(pathVariable))
            {
                return Array.Empty<string>();
            }
            return pathVariable
                .Split(Path.PathSeparator)
                .Select(d => Environment.ExpandEnvironmentVariables(d.Trim().Trim('"')))
                .Where(d => d.Length > 0);
        }
    }
}
