using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 팝업 카드(<see cref="GitConfirmDialog"/>·<see cref="GitPromptDialog"/>·<see cref="GitErrorDialog"/>)를 얹을 곳들.
    /// Git 창과 보조 창(<see cref="GitToolWindowBase"/>)이 자기 내용 루트를 등록한다.
    /// 운영체제 대화상자로 떨어지지 않도록, 부른 쪽이 호스트를 못 주면 여기서 찾고, 그래도 없으면 Git 창을 열어 그 위에 띄운다.
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    public static class GitPopupHosts
    {
        /// <summary>Git 창을 연 뒤 내용이 만들어지기를 기다리는 최대 틀 수.</summary>
        private const int OpenWaitFrames = 30;

        private static readonly List<(EditorWindow window, VisualElement host)> s_hosts = new();

        public static void Register(EditorWindow window, VisualElement host)
        {
            s_hosts.RemoveAll(entry => entry.window == null || entry.window == window);
            s_hosts.Add((window, host));
        }

        public static void Unregister(EditorWindow window)
        {
            s_hosts.RemoveAll(entry => entry.window == null || entry.window == window);
        }

        public static bool IsUsable(VisualElement host)
        {
            return host != null && host.panel != null;
        }

        /// <summary>
        /// 지금 띄울 곳. <paramref name="preferred"/> 가 쓸 수 있으면 그것, 아니면 포커스된 창, 아니면 열린 아무 창. 없으면 null.
        /// </summary>
        public static VisualElement Find(VisualElement preferred = null)
        {
            if (IsUsable(preferred))
            {
                return preferred;
            }
            s_hosts.RemoveAll(entry => entry.window == null);
            EditorWindow focused = EditorWindow.focusedWindow;
            foreach ((EditorWindow window, VisualElement host) in s_hosts)
            {
                if (window == focused && IsUsable(host))
                {
                    return host;
                }
            }
            for (int i = s_hosts.Count - 1; i >= 0; i--)
            {
                if (IsUsable(s_hosts[i].host))
                {
                    return s_hosts[i].host;
                }
            }
            return null;
        }

        /// <summary>
        /// <see cref="Find"/> 로 못 찾으면 Git 창을 열고 내용이 만들어질 때까지 기다린다.
        /// 그래도 없으면(배치 모드 등) null — 부른 쪽은 묻지 않고 취소로 처리한다.
        /// </summary>
        public static async Task<VisualElement> ResolveAsync(VisualElement preferred = null)
        {
            VisualElement host = Find(preferred);
            if (host != null || Application.isBatchMode)
            {
                return host;
            }
            GitWindow.Open();
            for (int frame = 0; frame < OpenWaitFrames && host == null; frame++)
            {
                await NextFrameAsync();
                host = Find();
            }
            if (host == null)
            {
                Debug.LogWarning("[Git] " + GitText.Get("popup.noHost"));
            }
            return host;
        }

        private static Task s_tail = Task.CompletedTask;

        /// <summary>
        /// 팝업 카드 하나를 띄운다. 이미 떠 있는 카드가 있으면 닫힐 때까지 기다렸다가 띄운다(한 번에 하나).
        /// 띄울 곳이 없으면 <paramref name="cancelled"/> 를 돌려준다(운영체제 대화상자로 묻지 않는다).
        /// </summary>
        /// <param name="show">호스트를 받아 카드를 띄우고, 닫힐 때 끝나는 작업을 돌려준다.</param>
        public static async Task<T> ShowAsync<T>(VisualElement preferred, Func<VisualElement, Task<T>> show, T cancelled)
        {
            Task previous = s_tail;
            TaskCompletionSource<bool> mine = new(TaskCreationOptions.RunContinuationsAsynchronously);
            s_tail = mine.Task;
            try
            {
                await previous;
                VisualElement host = await ResolveAsync(preferred);
                if (host == null)
                {
                    return cancelled;
                }
                // 열린 툴팁·드롭다운이 카드 위에 남지 않게, 띄우기 전에 포인터를 놓게 한다.
                host.panel.ReleasePointer(PointerId.mousePointerId);
                return await show(host);
            }
            finally
            {
                mine.TrySetResult(true);
            }
        }

        /// <summary>
        /// 카드를 닫는다. 바로 숨겨 다시 누르지 못하게 하고, 포커스를 띄우기 전 요소로 돌려준 뒤 다음 틀에 떼어 낸다
        /// (포커스가 있는 요소를 같은 틀에 떼면 UI Toolkit 이 "FocusController has unprocessed focus events" 를 남긴다).
        /// </summary>
        public static void Close(VisualElement overlay, Focusable restoreFocus)
        {
            overlay.style.display = DisplayStyle.None;
            overlay.pickingMode = PickingMode.Ignore;
            if (restoreFocus is VisualElement element && element.panel != null)
            {
                element.Focus();
            }
            else
            {
                overlay.focusController?.focusedElement?.Blur();
            }
            overlay.schedule.Execute(overlay.RemoveFromHierarchy);
        }

        private static Task NextFrameAsync()
        {
            TaskCompletionSource<bool> done = new(TaskCreationOptions.RunContinuationsAsynchronously);
            void Tick()
            {
                EditorApplication.update -= Tick;
                done.TrySetResult(true);
            }
            EditorApplication.update += Tick;
            return done.Task;
        }
    }
}
