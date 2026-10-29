using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 사용자에게 묻고 알리는 창. 뷰·흐름 클래스가 팝업을 직접 만들지 않게 해서, 대화상자 없이 흐름을 시험할 수 있게 한다.
    /// 모두 Git 창(또는 보조 창) 위에 덮어 띄우는 카드다 — 운영체제 대화상자는 쓰지 않는다.
    /// 띄울 곳이 없으면(배치 모드) 묻지 않고 취소로 본다.
    /// </summary>
    public interface IDialogService
    {
        /// <param name="danger">확인이 되돌릴 수 없는 일(버리기·삭제·강제)이면 true. 빨강이고 기본 포커스가 취소다.</param>
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        Task<bool> ConfirmAsync(string title, string message, string ok, bool danger = false, string cancel = null, ActionGlyphKind icon = ActionGlyphKind.Comment);

        /// <summary>
        /// 세 갈래로 묻는다. 0 = <paramref name="ok"/>(기본), 1 = 취소, 2 = <paramref name="alt"/>.
        /// </summary>
        Task<int> ChooseAsync(string title, string message, string ok, string alt, bool okDanger = false, bool altDanger = false, string cancel = null,
            ActionGlyphKind icon = ActionGlyphKind.Comment);

        Task AlertAsync(string title, string message, ActionGlyphKind icon = ActionGlyphKind.Comment);

        /// <summary>
        /// 글을 받는다. 취소하면 null.
        /// </summary>
        /// <param name="validate">입력할 때마다 부른다. 문제가 있으면 그 설명(확인 버튼이 꺼진다), 없으면 null.</param>
        Task<string> PromptAsync(string title, string message, string initialValue, bool multiline = false, Func<string, string> validate = null,
            ActionGlyphKind icon = ActionGlyphKind.Pencil);

        /// <summary>
        /// 여러 개 중 하나를 고르게 한다. 고른 번호, 취소면 -1.
        /// </summary>
        Task<int> PickAsync(string title, string message, IReadOnlyList<string> options, int selected = 0, ActionGlyphKind icon = ActionGlyphKind.Comment);

        /// <summary>열린 씬·에셋의 저장 안 된 수정을 디스크에 쓴다. 사용자가 취소하면 false.</summary>
        bool SaveOpenWork();
    }

    /// <summary>
    /// <see cref="IDialogService"/> 의 에디터 구현: <see cref="GitConfirmDialog"/>·<see cref="GitPromptDialog"/> 카드.
    /// 카드를 얹을 곳은 <see cref="HostProvider"/>(보통 그 창의 <see cref="GitServices.PopupHost"/>), 없으면 <see cref="GitPopupHosts"/> 가 고른다.
    /// </summary>
    public sealed class OverlayDialogService : IDialogService
    {
        public Func<VisualElement> HostProvider { get; set; }

        private VisualElement Host => HostProvider?.Invoke();

        public Task<bool> ConfirmAsync(string title, string message, string ok, bool danger = false, string cancel = null, ActionGlyphKind icon = ActionGlyphKind.Comment)
        {
            return GitConfirmDialog.ConfirmAsync(Host, icon, title, message, ok, danger, cancel);
        }

        public Task<int> ChooseAsync(string title, string message, string ok, string alt, bool okDanger = false, bool altDanger = false, string cancel = null,
            ActionGlyphKind icon = ActionGlyphKind.Comment)
        {
            return GitConfirmDialog.ChooseAsync(Host, icon, title, message, ok, alt, okDanger, altDanger, cancel);
        }

        public Task AlertAsync(string title, string message, ActionGlyphKind icon = ActionGlyphKind.Comment)
        {
            return GitConfirmDialog.AlertAsync(Host, icon, title, message);
        }

        public Task<string> PromptAsync(string title, string message, string initialValue, bool multiline = false, Func<string, string> validate = null,
            ActionGlyphKind icon = ActionGlyphKind.Pencil)
        {
            return GitPromptDialog.ShowAsync(Host, icon, title, message, initialValue, multiline, validate);
        }

        public Task<int> PickAsync(string title, string message, IReadOnlyList<string> options, int selected = 0, ActionGlyphKind icon = ActionGlyphKind.Comment)
        {
            return GitPickDialog.ShowAsync(Host, icon, title, message, options, selected);
        }

        public bool SaveOpenWork()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo() == false)
            {
                return false;
            }
            AssetDatabase.SaveAssets();
            return true;
        }
    }
}
