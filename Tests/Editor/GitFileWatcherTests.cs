using System;
using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;

namespace GitVersionControl.Tests
{
    /// <summary>
    /// 실시간 변경 감지: 바뀐 경로가 어떤 새로고침을 부르는지(<see cref="GitFileWatcher.Classify"/>)와
    /// Windows 감시기(<see cref="WindowsDirectoryWatcher"/>)가 실제 파일 변경을 전달하는지.
    /// </summary>
    public sealed class GitFileWatcherTests
    {
        private const string Root = "C:/repo";
        private const string GitDir = "C:/repo/.git";

        [TestCase("C:/repo/Assets/a.cs", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/README.md", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/ProjectSettings/ProjectSettings.asset", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/Packages/com.foo/Editor/x.cs", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/Library/ArtifactDB", GitChangeKind.None)]
        [TestCase("C:/repo/Library", GitChangeKind.None)]
        [TestCase("C:/repo/Temp/x.tmp", GitChangeKind.None)]
        [TestCase("C:/repo/Logs/AssetImportWorker0.log", GitChangeKind.None)]
        [TestCase("C:/repo/obj/Debug/a.dll", GitChangeKind.None)]
        [TestCase("C:/repo/UserSettings/GitVersionControl.asset", GitChangeKind.None)]
        [TestCase("C:/repo/.vs/state.bin", GitChangeKind.None)]
        [TestCase("C:/repo/LibraryNotes.md", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/Assets/Library/a.cs", GitChangeKind.WorkingTree)]
        public void Test_Classify_WorkingTree(string path, GitChangeKind expected)
        {
            Assert.AreEqual(expected, GitFileWatcher.Classify(path, Root, GitDir, string.Empty));
        }

        [TestCase("C:/repo/.git/index", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/.git/HEAD", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/refs/heads/main", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/refs/heads/feature/login", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/refs/remotes/origin/main", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/packed-refs", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/FETCH_HEAD", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/MERGE_HEAD", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/config", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/rebase-merge/done", GitChangeKind.Full)]
        [TestCase("C:/repo/.git/index.lock", GitChangeKind.None)]
        [TestCase("C:/repo/.git/refs/heads/main.lock", GitChangeKind.None)]
        [TestCase("C:/repo/.git/objects/ab/cdef0123", GitChangeKind.None)]
        [TestCase("C:/repo/.git/objects", GitChangeKind.None)]
        [TestCase("C:/repo/.git/logs/HEAD", GitChangeKind.None)]
        [TestCase("C:/repo/.git/hooks/pre-commit", GitChangeKind.None)]
        [TestCase("C:/repo/.git/lfs/tmp/x", GitChangeKind.None)]
        [TestCase("C:/repo/.git/COMMIT_EDITMSG", GitChangeKind.None)]
        [TestCase("C:/repo/.git/ORIG_HEAD", GitChangeKind.None)]
        [TestCase("C:/repo/.git", GitChangeKind.None)]
        [TestCase("C:/repo/.git/objectsx", GitChangeKind.Full)]
        public void Test_Classify_GitDir(string path, GitChangeKind expected)
        {
            Assert.AreEqual(expected, GitFileWatcher.Classify(path, Root, GitDir, string.Empty));
        }

        [TestCase(@"C:\repo\Assets\a.cs", GitChangeKind.WorkingTree)]
        [TestCase(@"C:\repo\.git\HEAD", GitChangeKind.Full)]
        [TestCase(@"c:\REPO\library\x", GitChangeKind.None)]
        [TestCase(@"C:\REPO\.GIT\INDEX", GitChangeKind.WorkingTree)]
        public void Test_Classify_IgnoresSeparatorAndCase(string path, GitChangeKind expected)
        {
            Assert.AreEqual(expected, GitFileWatcher.Classify(path, @"C:\repo\", @"C:\repo\.git\", string.Empty));
        }

        [TestCase("C:/other/a.cs")]
        [TestCase("C:/repository/a.cs")]
        [TestCase("C:/repo")]
        [TestCase("C:/repo/")]
        [TestCase("C:/repo/sub/.git/HEAD")]
        [TestCase("")]
        [TestCase(null)]
        public void Test_Classify_OutsideOrIrrelevant_IsNone(string path)
        {
            Assert.AreEqual(GitChangeKind.None, GitFileWatcher.Classify(path, Root, GitDir, string.Empty));
        }

        [Test]
        public void Test_Classify_WithoutRoots_IsNone()
        {
            // 감시를 멈춘 직후 감시 스레드에서 늦게 온 이벤트.
            Assert.AreEqual(GitChangeKind.None, GitFileWatcher.Classify("C:/repo/a.cs", null, null, null));
        }

        /// <summary>Unity 프로젝트가 저장소 하위 폴더에 있으면 그 아래의 Library 만 무시한다.</summary>
        [TestCase("C:/repo/client/unity/Library/x", GitChangeKind.None)]
        [TestCase("C:/repo/client/unity/Temp/x", GitChangeKind.None)]
        [TestCase("C:/repo/client/unity/Assets/a.cs", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/Library/notes.md", GitChangeKind.WorkingTree)]
        [TestCase("C:/repo/server/main.go", GitChangeKind.WorkingTree)]
        public void Test_Classify_ProjectInSubfolder(string path, GitChangeKind expected)
        {
            Assert.AreEqual(expected, GitFileWatcher.Classify(path, Root, GitDir, "client/unity/"));
        }

        /// <summary>worktree: git 폴더가 작업 트리 밖에 있고, 작업 트리의 .git 은 파일이다.</summary>
        [TestCase("C:/main/.git/worktrees/wt/HEAD", GitChangeKind.Full)]
        [TestCase("C:/main/.git/worktrees/wt/index", GitChangeKind.WorkingTree)]
        [TestCase("C:/wt/.git", GitChangeKind.None)]
        [TestCase("C:/wt/Assets/a.cs", GitChangeKind.WorkingTree)]
        public void Test_Classify_Worktree(string path, GitChangeKind expected)
        {
            Assert.AreEqual(expected, GitFileWatcher.Classify(path, "C:/wt", "C:/main/.git/worktrees/wt", string.Empty));
        }

        // ── WindowsDirectoryWatcher ── 임시 폴더에 실제로 파일을 쓰고 콜백을 기다린다.

        private const int EventTimeoutMs = 5000;

        private string _dir;
        private ConcurrentQueue<string> _events;
        private int _overflows;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "GitVcsWatcherTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _events = new ConcurrentQueue<string>();
            _overflows = 0;
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
                // 감시 핸들이 늦게 닫히는 등. 임시 폴더라 남아도 괜찮다.
            }
        }

        private WindowsDirectoryWatcher StartWatcher()
        {
            if (WindowsDirectoryWatcher.IsSupported == false)
            {
                Assert.Ignore("ReadDirectoryChangesW 는 Windows 에디터에서만 쓴다.");
            }
            return new WindowsDirectoryWatcher(_dir, path => _events.Enqueue(path.Replace('\\', '/')), () => Interlocked.Increment(ref _overflows));
        }

        private string Full(string relative)
        {
            return Path.Combine(_dir, relative).Replace('\\', '/');
        }

        /// <summary>조건을 만족하는 이벤트가 올 때까지 기다린다.</summary>
        private bool WaitFor(Func<string, bool> match, int timeoutMs = EventTimeoutMs)
        {
            Stopwatch watch = Stopwatch.StartNew();
            while (watch.ElapsedMilliseconds < timeoutMs)
            {
                if (_events.Any(match))
                {
                    return true;
                }
                Thread.Sleep(20);
            }
            return false;
        }

        [Test]
        public void Test_Watcher_ReportsCreatedFile()
        {
            using WindowsDirectoryWatcher watcher = StartWatcher();
            File.WriteAllText(Path.Combine(_dir, "a.txt"), "x");

            Assert.IsTrue(WaitFor(p => p == Full("a.txt")), "이벤트: " + string.Join(", ", _events));
        }

        [Test]
        public void Test_Watcher_ReportsNestedFileWithFullPath()
        {
            Directory.CreateDirectory(Path.Combine(_dir, "Assets", "Deep"));
            using WindowsDirectoryWatcher watcher = StartWatcher();
            File.WriteAllText(Path.Combine(_dir, "Assets", "Deep", "b.cs"), "x");

            Assert.IsTrue(WaitFor(p => p == Full("Assets/Deep/b.cs")), "이벤트: " + string.Join(", ", _events));
        }

        [Test]
        public void Test_Watcher_ReportsModifyDeleteAndRename()
        {
            string file = Path.Combine(_dir, "c.txt");
            File.WriteAllText(file, "x");
            using WindowsDirectoryWatcher watcher = StartWatcher();

            File.AppendAllText(file, "more");
            Assert.IsTrue(WaitFor(p => p == Full("c.txt")), "수정이 오지 않았다");

            while (_events.TryDequeue(out _))
            {
            }
            File.Move(file, Path.Combine(_dir, "d.txt"));
            // 이름 바꾸기는 옛 이름과 새 이름을 둘 다 알린다.
            Assert.IsTrue(WaitFor(p => p == Full("c.txt")), "옛 이름이 오지 않았다");
            Assert.IsTrue(WaitFor(p => p == Full("d.txt")), "새 이름이 오지 않았다");

            File.Delete(Path.Combine(_dir, "d.txt"));
            Assert.IsTrue(WaitFor(p => p == Full("d.txt")), "삭제가 오지 않았다");
        }

        [Test]
        public void Test_Watcher_ReportsManyEventsWithoutLosingTheLast()
        {
            using WindowsDirectoryWatcher watcher = StartWatcher();
            for (int i = 0; i < 200; i++)
            {
                File.WriteAllText(Path.Combine(_dir, $"f{i}.txt"), "x");
            }

            // 버퍼가 넘치면 개별 경로 대신 overflow 로 알린다 — 둘 중 하나는 반드시 와야 한다.
            Assert.IsTrue(WaitFor(p => p == Full("f199.txt")) || _overflows > 0);
        }

        [Test]
        public void Test_Watcher_DisposeStopsPromptlyAndReleasesFolder()
        {
            WindowsDirectoryWatcher watcher = StartWatcher();
            Stopwatch watch = Stopwatch.StartNew();
            watcher.Dispose();
            Assert.Less(watch.ElapsedMilliseconds, 1500, "Dispose 가 감시 스레드를 오래 기다렸다");

            while (_events.TryDequeue(out _))
            {
            }
            File.WriteAllText(Path.Combine(_dir, "after.txt"), "x");
            Thread.Sleep(300);
            Assert.IsFalse(_events.Any(), "Dispose 뒤에도 이벤트가 왔다");

            // 핸들을 놓았으면 감시하던 폴더를 지울 수 있다.
            Directory.Delete(_dir, true);
            Assert.IsFalse(Directory.Exists(_dir));
        }

        [Test]
        public void Test_Watcher_DisposeTwiceIsSafe()
        {
            WindowsDirectoryWatcher watcher = StartWatcher();
            watcher.Dispose();
            Assert.DoesNotThrow(watcher.Dispose);
        }

        [Test]
        public void Test_Watcher_MissingFolderThrows()
        {
            if (WindowsDirectoryWatcher.IsSupported == false)
            {
                Assert.Ignore("ReadDirectoryChangesW 는 Windows 에디터에서만 쓴다.");
            }
            Assert.Throws<Win32Exception>(() => new WindowsDirectoryWatcher(Path.Combine(_dir, "missing"), _ => { }, () => { }));
        }

        [Test]
        public void Test_Watcher_ReportsOverflowWhenWatchedFolderIsDeleted()
        {
            string sub = Path.Combine(_dir, "watched");
            Directory.CreateDirectory(sub);
            if (WindowsDirectoryWatcher.IsSupported == false)
            {
                Assert.Ignore("ReadDirectoryChangesW 는 Windows 에디터에서만 쓴다.");
            }
            using WindowsDirectoryWatcher watcher = new(sub, _ => { }, () => Interlocked.Increment(ref _overflows));
            // FILE_SHARE_DELETE 로 열었으니 감시 중에도 지울 수 있고, 감시기는 끝나며 전체 새로고침을 부탁한다.
            Directory.Delete(sub, true);

            Stopwatch watch = Stopwatch.StartNew();
            while (_overflows == 0 && watch.ElapsedMilliseconds < EventTimeoutMs)
            {
                Thread.Sleep(20);
            }
            Assert.Greater(_overflows, 0);
        }
    }
}
