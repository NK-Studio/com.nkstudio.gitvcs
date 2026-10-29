using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// macOS FSEvents 로 폴더 하나(하위 폴더 포함)의 변경을 받는다. <see cref="WindowsDirectoryWatcher"/> 의 macOS 짝이다.
    ///
    /// Unity Mono 의 <see cref="System.IO.FileSystemWatcher"/> 는 폴더를 통째로 훑는 폴링이라 Library 가 있는 프로젝트에는 무겁다.
    /// FSEvents 는 OS 가 바뀐 경로만 알려 준다. 전용 디스패치 큐에서 콜백이 오고, 콜백은 그 스레드에서 <c>onChanged</c> 를 부른다.
    /// 도메인 리로드 전에 <see cref="Dispose"/> 로 스트림을 멈추고, 진행 중인 콜백이 끝날 때까지 기다린다(리로드된 코드로 네이티브 콜백이 들어오지 않게).
    /// </summary>
    public sealed class MacDirectoryWatcher : IDisposable
    {
        private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string LibSystem = "/usr/lib/libSystem.dylib";

        private const ulong EventIdSinceNow = 0xFFFFFFFFFFFFFFFFUL;
        private const uint CreateFlagNoDefer = 0x02;
        private const uint CreateFlagWatchRoot = 0x04;
        private const uint CreateFlagFileEvents = 0x10;
        private const uint EventFlagMustScanSubDirs = 0x01;
        private const uint EventFlagUserDropped = 0x02;
        private const uint EventFlagKernelDropped = 0x04;
        private const uint StringEncodingUtf8 = 0x08000100;

        /// <summary>이벤트를 모으는 시간(초). 저장 한 번에 여러 이벤트가 와도 한 번에 받는다.</summary>
        private const double Latency = 0.1;

        public static bool IsSupported => Application.platform == RuntimePlatform.OSXEditor;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void StreamCallback(IntPtr stream, IntPtr info, IntPtr count, IntPtr paths, IntPtr flags, IntPtr ids);

        private readonly string _root;
        private readonly string _realRoot;
        private readonly Action<string> _onChanged;
        private readonly Action _onOverflow;
        // 네이티브 쪽이 들고 있는 콜백이 GC 로 사라지지 않게 필드로 붙잡는다.
        private readonly StreamCallback _callback;
        private readonly IntPtr _pathString;
        private readonly IntPtr _pathArray;
        private readonly IntPtr _queue;
        private IntPtr _stream;
        private int _disposed;
        private int _inCallback;

        /// <param name="onChanged">바뀐 항목의 전체 경로(<paramref name="directory"/> 기준 표기). 감시 스레드에서 불린다.</param>
        /// <param name="onOverflow">이벤트를 잃었을 때(커널·사용자 큐 넘침). 감시 스레드에서 불린다.</param>
        public MacDirectoryWatcher(string directory, Action<string> onChanged, Action onOverflow)
        {
            _root = directory.Replace('\\', '/').TrimEnd('/');
            // FSEvents 는 심볼릭 링크를 푼 실제 경로로 알려 준다(/tmp → /private/tmp). 받은 경로를 감시한 표기로 되돌리려고 기억한다.
            _realRoot = RealPath(_root) ?? _root;
            _onChanged = onChanged;
            _onOverflow = onOverflow;
            _callback = OnEvents;

            _pathString = CFStringCreateWithCString(IntPtr.Zero, Utf8(_root), StringEncodingUtf8);
            _pathArray = CFArrayCreate(IntPtr.Zero, new[] { _pathString }, (IntPtr)1, IntPtr.Zero);
            _stream = FSEventStreamCreate(IntPtr.Zero, _callback, IntPtr.Zero, _pathArray, EventIdSinceNow, Latency,
                CreateFlagNoDefer | CreateFlagWatchRoot | CreateFlagFileEvents);
            if (_stream == IntPtr.Zero)
            {
                Release();
                throw new InvalidOperationException("FSEventStreamCreate failed: " + _root);
            }
            _queue = dispatch_queue_create("com.nkstudio.gitvcs.watcher", IntPtr.Zero);
            FSEventStreamSetDispatchQueue(_stream, _queue);
            if (FSEventStreamStart(_stream) == false)
            {
                Release();
                throw new InvalidOperationException("FSEventStreamStart failed: " + _root);
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1)
            {
                return;
            }
            Release();
        }

        private void Release()
        {
            if (_stream != IntPtr.Zero)
            {
                FSEventStreamStop(_stream);
                FSEventStreamInvalidate(_stream);
                // 이미 들어온 콜백이 끝날 때까지 기다린다(최대 1초). 그 뒤에는 새 콜백이 오지 않는다.
                for (int i = 0; i < 100 && Volatile.Read(ref _inCallback) > 0; i++)
                {
                    Thread.Sleep(10);
                }
                FSEventStreamRelease(_stream);
                _stream = IntPtr.Zero;
            }
            if (_queue != IntPtr.Zero)
            {
                dispatch_release(_queue);
            }
            if (_pathArray != IntPtr.Zero)
            {
                CFRelease(_pathArray);
            }
            if (_pathString != IntPtr.Zero)
            {
                CFRelease(_pathString);
            }
        }

        private void OnEvents(IntPtr stream, IntPtr info, IntPtr count, IntPtr paths, IntPtr flags, IntPtr ids)
        {
            Interlocked.Increment(ref _inCallback);
            try
            {
                if (Volatile.Read(ref _disposed) == 1)
                {
                    return;
                }
                long total = count.ToInt64();
                for (long i = 0; i < total; i++)
                {
                    uint flag = unchecked((uint)Marshal.ReadInt32(flags, (int)(i * sizeof(uint))));
                    if ((flag & (EventFlagMustScanSubDirs | EventFlagUserDropped | EventFlagKernelDropped)) != 0)
                    {
                        _onOverflow?.Invoke();
                        continue;
                    }
                    string path = PtrToUtf8(Marshal.ReadIntPtr(paths, (int)(i * IntPtr.Size)));
                    if (path != null)
                    {
                        _onChanged?.Invoke(ToWatchedPath(path));
                    }
                }
            }
            catch (Exception e)
            {
                // 네이티브 스레드로 예외가 새면 에디터가 죽는다.
                Debug.LogException(e);
            }
            finally
            {
                Interlocked.Decrement(ref _inCallback);
            }
        }

        private string ToWatchedPath(string path)
        {
            if (_realRoot != _root && path.StartsWith(_realRoot, StringComparison.Ordinal))
            {
                return _root + path.Substring(_realRoot.Length);
            }
            return path;
        }

        private static byte[] Utf8(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            Array.Resize(ref bytes, bytes.Length + 1);
            return bytes;
        }

        private static string PtrToUtf8(IntPtr pointer)
        {
            if (pointer == IntPtr.Zero)
            {
                return null;
            }
            int length = 0;
            while (Marshal.ReadByte(pointer, length) != 0)
            {
                length++;
            }
            byte[] bytes = new byte[length];
            Marshal.Copy(pointer, bytes, 0, length);
            return Encoding.UTF8.GetString(bytes);
        }

        private static string RealPath(string path)
        {
            IntPtr resolved = realpath(Utf8(path), IntPtr.Zero);
            if (resolved == IntPtr.Zero)
            {
                return null;
            }
            try
            {
                return PtrToUtf8(resolved)?.TrimEnd('/');
            }
            finally
            {
                free(resolved);
            }
        }

        [DllImport(CoreServices)]
        private static extern IntPtr FSEventStreamCreate(IntPtr allocator, StreamCallback callback, IntPtr context, IntPtr pathsToWatch, ulong sinceWhen, double latency, uint flags);

        [DllImport(CoreServices)]
        private static extern void FSEventStreamSetDispatchQueue(IntPtr stream, IntPtr queue);

        [DllImport(CoreServices)]
        [return: MarshalAs(UnmanagedType.U1)]
        private static extern bool FSEventStreamStart(IntPtr stream);

        [DllImport(CoreServices)]
        private static extern void FSEventStreamStop(IntPtr stream);

        [DllImport(CoreServices)]
        private static extern void FSEventStreamInvalidate(IntPtr stream);

        [DllImport(CoreServices)]
        private static extern void FSEventStreamRelease(IntPtr stream);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, byte[] text, uint encoding);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFArrayCreate(IntPtr allocator, IntPtr[] values, IntPtr count, IntPtr callbacks);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr value);

        [DllImport(LibSystem)]
        private static extern IntPtr dispatch_queue_create(string label, IntPtr attributes);

        [DllImport(LibSystem)]
        private static extern void dispatch_release(IntPtr queue);

        [DllImport(LibSystem)]
        private static extern IntPtr realpath(byte[] path, IntPtr resolved);

        [DllImport(LibSystem)]
        private static extern void free(IntPtr pointer);
    }
}
