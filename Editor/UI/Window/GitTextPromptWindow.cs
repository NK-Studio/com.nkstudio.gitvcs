using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// 글 한 줄을 묻는 작은 모달 창(Unity 에디터에는 입력 대화상자가 없다). 확인하면 입력한 글, 취소하면 null.
    /// 여러 줄(<see cref="AskMultiline"/>)은 커밋 메시지를 고칠 때 쓴다: Enter 는 줄바꿈, Ctrl+Enter 가 확인.
    /// </summary>
    public sealed class GitTextPromptWindow : EditorWindow
    {
        private string _message;
        private string _value;
        private bool _confirmed;
        private bool _multiline;

        public static string Ask(string title, string message, string initialValue)
        {
            return Prompt(title, message, initialValue, false, new Vector2(420, 120));
        }

        public static string AskMultiline(string title, string message, string initialValue)
        {
            return Prompt(title, message, initialValue, true, new Vector2(520, 300));
        }

        private static string Prompt(string title, string message, string initialValue, bool multiline, Vector2 size)
        {
            GitTextPromptWindow window = CreateInstance<GitTextPromptWindow>();
            window.titleContent = new GUIContent(title);
            window._message = message;
            window._value = initialValue ?? string.Empty;
            window._multiline = multiline;
            Rect main = EditorGUIUtility.GetMainWindowPosition();
            window.position = new Rect(main.center - size * 0.5f, size);
            window.minSize = size;
            window.maxSize = size;
            window.ShowModalUtility();
            return window._confirmed ? window._value : null;
        }

        private void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.style.paddingLeft = 10;
            root.style.paddingRight = 10;
            root.style.paddingTop = 10;
            root.style.paddingBottom = 10;

            Label message = new(_message);
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.marginBottom = 6;
            root.Add(message);

            TextField field = new() { value = _value, multiline = _multiline };
            if (_multiline)
            {
                field.style.flexGrow = 1;
                field.style.whiteSpace = WhiteSpace.Normal;
                field.verticalScrollerVisibility = ScrollerVisibility.Auto;
            }
            field.RegisterValueChangedCallback(evt => _value = evt.newValue);
            field.RegisterCallback<KeyDownEvent>(evt =>
            {
                bool isEnter = evt.keyCode == KeyCode.Return || evt.keyCode == KeyCode.KeypadEnter;
                if (isEnter && (_multiline == false || evt.actionKey))
                {
                    Confirm();
                }
                else if (evt.keyCode == KeyCode.Escape)
                {
                    Close();
                }
            }, TrickleDown.TrickleDown);
            root.Add(field);

            VisualElement buttons = new();
            buttons.style.flexDirection = FlexDirection.Row;
            buttons.style.justifyContent = Justify.FlexEnd;
            buttons.style.marginTop = 10;
            buttons.Add(new Button(Close) { text = GitText.Get("common.cancel") });
            buttons.Add(new Button(Confirm) { text = GitText.Get("common.ok") });
            root.Add(buttons);

            field.schedule.Execute(() => field.Focus());
        }

        private void Confirm()
        {
            _confirmed = true;
            Close();
        }
    }
}
