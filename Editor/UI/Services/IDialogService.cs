using UnityEditor;
using UnityEditor.SceneManagement;

namespace GitVersionControl
{
    /// <summary>
    /// 사용자에게 묻고 알리는 창. 뷰·흐름 클래스가 EditorUtility 를 직접 부르지 않게 해서, 대화상자 없이 흐름을 시험할 수 있게 한다.
    /// </summary>
    public interface IDialogService
    {
        /// <param name="cancel">null 이면 현재 언어의 "취소".</param>
        bool Confirm(string title, string message, string ok, string cancel = null);

        /// <summary>
        /// 세 갈래로 묻는다. 0 = <paramref name="ok"/>(기본), 1 = 취소, 2 = <paramref name="alt"/>.
        /// </summary>
        int Choose(string title, string message, string ok, string alt, string cancel = null);

        void Alert(string title, string message);

        /// <summary>열린 씬·에셋의 저장 안 된 수정을 디스크에 쓴다. 사용자가 취소하면 false.</summary>
        bool SaveOpenWork();
    }

    public sealed class EditorDialogService : IDialogService
    {
        public bool Confirm(string title, string message, string ok, string cancel = null)
        {
            return EditorUtility.DisplayDialog(title, message, ok, cancel ?? GitText.Get("common.cancel"));
        }

        public int Choose(string title, string message, string ok, string alt, string cancel = null)
        {
            // DisplayDialogComplex: 0 = ok, 1 = cancel, 2 = alt.
            return EditorUtility.DisplayDialogComplex(title, message, ok, cancel ?? GitText.Get("common.cancel"), alt);
        }

        public void Alert(string title, string message)
        {
            EditorUtility.DisplayDialog(title, message, GitText.Get("common.ok"));
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

    /// <summary>
    /// Git 창 안에 띄우는 대화상자. 알림(<see cref="Alert"/>)은 Git 창 위 팝업(<see cref="GitAlertDialog"/>)으로 띄우고,
    /// 창이 없거나 본체가 패널에 붙어 있지 않으면 운영체제 대화상자로 돌아간다.
    /// 확인·세 갈래 묻기는 부른 쪽이 답을 바로 받아야 해서 그대로 운영체제 대화상자로 묻는다(창 안 확인은 <see cref="GitConfirmDialog"/>).
    /// </summary>
    public sealed class PopupDialogService : IDialogService
    {
        private readonly IDialogService _fallback;
        private readonly System.Func<UnityEngine.UIElements.VisualElement> _host;

        /// <param name="host">팝업을 얹을 요소를 그때그때 돌려준다(<see cref="GitServices.PopupHost"/>).</param>
        public PopupDialogService(IDialogService fallback, System.Func<UnityEngine.UIElements.VisualElement> host)
        {
            _fallback = fallback;
            _host = host;
        }

        public bool Confirm(string title, string message, string ok, string cancel = null) => _fallback.Confirm(title, message, ok, cancel);

        public int Choose(string title, string message, string ok, string alt, string cancel = null) => _fallback.Choose(title, message, ok, alt, cancel);

        /// <summary>
        /// 팝업은 기다리지 않는다 — 부른 쪽은 바로 이어 가고, 사용자는 읽은 뒤 닫는다.
        /// </summary>
        public void Alert(string title, string message)
        {
            _ = GitAlertDialog.ShowAsync(_host(), _fallback, title, message);
        }

        public bool SaveOpenWork() => _fallback.SaveOpenWork();
    }
}
