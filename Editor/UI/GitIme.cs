using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 코드로 입력란에 포커스를 줄 때 한글 IME 를 바로 켠다.
    /// Unity 는 실제 OS 입력(마우스·키)을 처리할 때에야 창에 IME 를 붙여서, 클릭 없이 포커스를 받은 입력란(메뉴로 연 대화 상자 등)에
    /// 바로 치면 첫 키가 IME 를 거치지 않고 들어간다("내" 가 "sㅐ" 가 된다). 포커스를 가진 창에 기본 IME 를 직접 붙여 그 첫 키도 조합되게 한다.
    /// </summary>
    internal static class GitIme
    {
        private const uint IaceDefault = 0x0010;

        [DllImport("user32.dll")]
        private static extern IntPtr GetFocus();

        [DllImport("imm32.dll")]
        private static extern bool ImmAssociateContextEx(IntPtr hWnd, IntPtr hImc, uint flags);

        /// <summary>
        /// root 아래 입력란이 포커스를 받을 때마다 <see cref="Wake"/> 한다. 같은 콜백은 한 번만 붙으므로 여러 번 불러도 된다.
        /// </summary>
        public static void Attach(VisualElement root)
        {
            root.RegisterCallback<FocusInEvent>(OnFocusIn, TrickleDown.TrickleDown);
        }

        private static void OnFocusIn(FocusInEvent evt)
        {
            if (evt.target is VisualElement target && (target is TextField || target.GetFirstAncestorOfType<TextField>() != null))
            {
                Wake();
                // 메뉴(별도 OS 창)에서 연 대화 상자면 지금은 메뉴 창이 아직 OS 포커스를 쥐고 있다 — 메뉴가 닫혀 Unity 창으로 돌아온 뒤 한 번 더 붙인다.
                target.schedule.Execute(Wake).StartingIn(150);
            }
        }

        /// <summary>
        /// 포커스를 가진 창에 기본 IME 를 붙인다. Windows 가 아니면 아무것도 하지 않는다.
        /// </summary>
        public static void Wake()
        {
            if (Application.platform != RuntimePlatform.WindowsEditor)
            {
                return;
            }
            try
            {
                IntPtr window = GetFocus();
                if (window != IntPtr.Zero)
                {
                    ImmAssociateContextEx(window, IntPtr.Zero, IaceDefault);
                }
            }
            catch (Exception)
            {
                // imm32 를 못 쓰는 환경이면 예전처럼 첫 키만 IME 를 놓친다.
            }
        }
    }
}
