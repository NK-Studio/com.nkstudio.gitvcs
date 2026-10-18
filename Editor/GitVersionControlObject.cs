using UnityEditor;
using UnityEditor.VersionControl;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// Project Settings > Version Control > Mode 드롭다운의 "Git" 항목.
    ///
    /// Unity 는 모드가 "Git" 으로 바뀔 때 이 객체를 만들어 <see cref="OnActivate"/> 를 부르고, 다른 모드로 바뀔 때
    /// <see cref="OnDeactivate"/> 를 부른다. 활성 객체는 도메인 리로드를 거쳐 다시 직렬화되므로 그때는 OnEnable 만 온다.
    /// 확장 인터페이스(오버레이·설정 UI·팝업 메뉴)는 기본 <see cref="VersionControlObject.GetExtension{T}"/> 가
    /// <c>this as T</c> 로 찾아 쓴다.
    ///
    /// Unity Version Control(collab-proxy)과 같은 확장 포인트를 쓰므로 둘은 동시에 활성화되지 않는다.
    /// </summary>
    [VersionControl(ModeName)]
    public sealed class GitVersionControlObject : VersionControlObject,
        ISettingsInspectorExtension, IIconOverlayExtension, IPopupMenuExtension
    {
        public const string ModeName = "Git";

        public override bool isConnected => GitRepository.IsRepository;

        private void OnEnable()
        {
            GitRepository.Start();
            GitRepository.Changed += Repaint;
        }

        private void OnDisable()
        {
            GitRepository.Changed -= Repaint;
        }

        public override void OnActivate()
        {
            GitRepository.Start();
        }

        public override void OnDeactivate()
        {
            GitRepository.Stop();
        }

        public override void Refresh()
        {
            GitRepository.RequestRefresh(immediate: true);
        }

        public void OnInspectorGUI()
        {
            GitSettingsGUI.Draw();
        }

        public void DrawOverlay(string assetPath, IconOverlayType type, Rect rect)
        {
            if (type != IconOverlayType.Project || GitVcsSettings.instance.ShowOverlays == false)
            {
                return;
            }
            GitIconOverlay.Draw(assetPath, rect);
        }

        public void DisplayPopupMenu(Rect rect)
        {
            GenericMenu menu = new();
            menu.AddItem(new GUIContent(GitText.Get("common.openGitWindow")), false, GitWindow.Open);
            menu.AddItem(new GUIContent(GitText.Get("common.refresh")), false, () => GitRepository.RequestRefresh(immediate: true));
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent(GitText.Get("common.vcsSettings")), false, () => SettingsService.OpenProjectSettings("Project/Version Control"));
            menu.DropDown(rect);
        }

        private static void Repaint()
        {
            // 설정 창은 IMGUI 라 상태가 바뀌어도 스스로 다시 그리지 않는다.
            SettingsService.RepaintAllSettingsWindow();
        }
    }
}
