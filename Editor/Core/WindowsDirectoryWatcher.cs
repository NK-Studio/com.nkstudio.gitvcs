using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Win32 ReadDirectoryChangesW 로 폴더 하나(하위 폴더 포함)의 변경을 받는다.
    ///
    /// Unity Mono 의 <see cref="FileSystemWatcher"/> 는 Windows 에서도 폴더를 통째로 다시 훑는 폴링(DefaultWatcher)이라,
    /// Library 가 있는 프로젝트를 재귀로 감시하면 무겁고 이벤트도 늦거나 오지 않는다. 그래서 OS 알림을 직접 받는다.
    /// 전용 스레드가 overlapped 읽기를 걸어 두고, 이벤트나 정지 신호를 기다린다. 콜백은 그 스레드에서 불린다.
    /// </summary>
    public sealed class WindowsDirectoryWatcher : IDisposable
    {
        private const uint FileListDirectory = 0x0001;
        private const uint FileShareAll = 0x00000001 | 0x00000002 | 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
        private const uint FileFlagOverlapped = 0x40000000;
        private const uint NotifyFilter = 0x00000001 /* FILE_NAME */ | 0x00000002 /* DIR_NAME */ | 0x00000008 /* SIZE */ | 0x00000010 /* LAST_WRITE */;
        private const int BufferSize = 64 * 1024; // 네트워크 드라이브는 64KB 를 넘으면 실패한다.
        private const int ErrorOperationAborted = 995;

        public static bool IsSupported => Application.platform == RuntimePlatform.WindowsEditor;

        private readonly string _root;
        private readonly Action<string> _onChanged;
        private readonly Action _onOverflow;
        private readonly SafeFileHandle _handle;
        private readonly ManualResetEvent _stop = new(false);
        private readonly Thread _thread;

        /// <param name="onChanged">바뀐 항목의 전체 경로. 감시 스레드에서 불린다.</param>
        /// <param name="onOverflow">이벤트를 잃었을 때(버퍼 넘침·읽기 실패). 감시 스레드에서 불린다.</param>
        public WindowsDirectoryWatcher(string directory, Action<string> onChanged, Action onOverflow)
        {
            _root = Path.GetFullPath(directory).TrimEnd('\\', '/') + "\\";
            _onChanged = onChanged;
            _onOverflow = onOverflow;
            _handle = CreateFileW(_root, FileListDirectory, FileShareAll, IntPtr.Zero, OpenExisting, FileFlagBackupSemantics | FileFlagOverlapped, IntPtr.Zero);
            if (_handle.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                _handle.Dispose();
                throw new Win32Exception(error);
            }
            _thread = new Thread(Run) { IsBackground = true, Name = "GitVcs DirectoryWatcher" };
            _thread.Start();
        }

        public void Dispose()
        {
            _stop.Set();
            // 걸어 둔 읽기가 취소돼 버퍼를 놓을 때까지 기다린다. 도메인 리로드 전에 스레드가 끝나야 한다.
            _thread.Join(2000);
        }

        private void Run()
        {
            IntPtr buffer = Marshal.AllocHGlobal(BufferSize);
            // OVERLAPPED: Internal·InternalHigh(포인터 크기 둘) + Offset·OffsetHigh(8) + hEvent.
            int eventOffset = IntPtr.Size * 2 + 8;
            IntPtr overlapped = Marshal.AllocHGlobal(eventOffset + IntPtr.Size);
            using ManualResetEvent ioDone = new(false);
            WaitHandle[] waits = { ioDone, _stop };
            bool ioPending = false;
            try
            {
                while (true)
                {
                    ioDone.Reset();
                    for (int i = 0; i < eventOffset; i++)
                    {
                        Marshal.WriteByte(overlapped, i, 0);
                    }
                    Marshal.WriteIntPtr(overlapped, eventOffset, ioDone.SafeWaitHandle.DangerousGetHandle());

                    if (ReadDirectoryChangesW(_handle, buffer, BufferSize, true, NotifyFilter, out _, overlapped, IntPtr.Zero) == false)
                    {
                        // 감시하던 폴더가 지워지는 등. 다시 걸 수 없으니 전체 새로고침만 부탁하고 끝낸다.
                        _onOverflow();
                        return;
                    }
                    ioPending = true;

                    if (WaitHandle.WaitAny(waits) == 1)
                    {
                        return;
                    }

                    bool ok = GetOverlappedResult(_handle, overlapped, out int bytes, false);
                    ioPending = false;
                    if (ok == false)
                    {
                        if (Marshal.GetLastWin32Error() == ErrorOperationAborted)
                        {
                            return;
                        }
                        _onOverflow();
                        continue;
                    }
                    if (bytes == 0)
                    {
                        // 버퍼보다 많은 변경이 몰렸다.
                        _onOverflow();
                        continue;
                    }
                    Dispatch(buffer);
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            finally
            {
                if (ioPending)
                {
                    CancelIoEx(_handle, overlapped);
                    GetOverlappedResult(_handle, overlapped, out _, true);
                }
                _handle.Dispose();
                Marshal.FreeHGlobal(overlapped);
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary>FILE_NOTIFY_INFORMATION 목록: NextEntryOffset(4) · Action(4) · FileNameLength(4, 바이트) · FileName(UTF-16).</summary>
        private void Dispatch(IntPtr buffer)
        {
            int offset = 0;
            while (true)
            {
                IntPtr entry = buffer + offset;
                int next = Marshal.ReadInt32(entry, 0);
                int nameBytes = Marshal.ReadInt32(entry, 8);
                string name = Marshal.PtrToStringUni(entry + 12, nameBytes / 2);
                _onChanged(_root + name);
                if (next == 0)
                {
                    break;
                }
                offset += next;
            }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadDirectoryChangesW(SafeFileHandle directory, IntPtr buffer, int bufferLength, bool watchSubtree,
            uint notifyFilter, out int bytesReturned, IntPtr overlapped, IntPtr completionRoutine);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(SafeFileHandle file, IntPtr overlapped, out int bytesTransferred, bool wait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIoEx(SafeFileHandle file, IntPtr overlapped);
    }
}
