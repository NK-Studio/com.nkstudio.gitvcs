using System;
using UnityEngine.UIElements;

namespace GitVersionControl
{
    /// <summary>
    /// Git 창 왼쪽 탭 하나. 하위 클래스는 자기 UXML 을 받아 요소를 찾고(<see cref="Root"/>), 저장소가 바뀔 때 무엇을 다시 읽을지만 정한다.
    ///
    /// 비활성 탭은 저장소가 바뀌어도 바로 다시 읽지 않고, 선택될 때(<see cref="OnActivated"/>) 읽는다 —
    /// 파일을 저장할 때마다 보이지도 않는 탭이 git log 를 돌리지 않게.
    /// </summary>
    public abstract class GitView : IDisposable
    {
        protected readonly GitServices Services;

        public VisualElement Root { get; }
        public abstract string Title { get; }

        /// <summary>탭 이름 옆에 붙는 숫자. 없으면 null.</summary>
        public virtual string Badge => null;

        public bool IsActive { get; private set; }

        /// <summary><see cref="Badge"/> 가 바뀌었을 수 있을 때.</summary>
        public event Action BadgeChanged;

        protected GitView(GitServices services, string layoutName)
        {
            Services = services;
            Root = GitUiAssets.CloneLayout(layoutName);
            Root.SetVisible(false);
            SplitPaneClamp.AttachAll(Root);
        }

        public void Activate()
        {
            IsActive = true;
            Root.SetVisible(true);
            OnActivated();
        }

        public void Deactivate()
        {
            IsActive = false;
            Root.SetVisible(false);
        }

        /// <summary><see cref="IGitRepositoryState.Changed"/> 마다 모든 탭에 온다.</summary>
        public virtual void OnRepositoryChanged()
        {
        }

        /// <summary>
        /// 창의 새로고침 버튼을 눌렀을 때, 보고 있는 탭에만 온다. 저장소 상태가 그대로여도 자기 내용(git log 등)을 강제로 다시 읽는다.
        /// </summary>
        public virtual void Reload()
        {
        }

        /// <summary>작업 실행기의 바쁨 상태가 바뀔 때(버튼을 끄고 켜려고).</summary>
        public virtual void OnBusyChanged(bool isBusy)
        {
        }

        public virtual void Dispose()
        {
        }

        protected virtual void OnActivated()
        {
        }

        protected void NotifyBadgeChanged()
        {
            BadgeChanged?.Invoke();
        }
    }
}
