using UnityEditor;
using UnityEngine;

namespace GitVersionControl
{
    /// <summary>
    /// 충돌을 푸는 중에 Unity 를 끄려 하면 한 번 묻는다(<see cref="GitConflictSafety"/>).
    /// 컴파일 오류가 남은 채로 끄면 다음에 Safe Mode 로 열려 Git 창을 쓸 수 없고, 패키지 목록이 충돌한 채면 이 패키지가 빠질 수 있다.
    /// 그래도 끌 수는 있다 — 터미널이나 다른 Git 도구로 풀 사람도 있으니 막지는 않는다.
    /// 묻기는 운영체제 대화상자 대신 Git 창 위 카드로 한다: 종료를 한 번 멈추고 카드를 띄운 뒤, [그래도 끄기] 면 File > Exit 로 다시 끈다
    /// (그때는 묻지 않고 지나가며, 저장 안 한 씬 묻기 같은 Unity 의 평소 종료 과정을 그대로 거친다).
    /// </summary>
#if UNITY_6000_7_OR_NEWER
    [Unity.Scripting.LifecycleManagement.NoAutoStaticsCleanup]
#endif
    [InitializeOnLoad]
    internal static class GitQuitGuard
    {
        private static bool s_confirmed;
        private static bool s_asking;

        static GitQuitGuard()
        {
            EditorApplication.wantsToQuit -= OnWantsToQuit;
            EditorApplication.wantsToQuit += OnWantsToQuit;
        }

        private static bool OnWantsToQuit()
        {
            if (Application.isBatchMode || GitRepository.IsRunning == false || GitRepository.IsRepository == false)
            {
                return true;
            }
            GitConflictRisk risk = GitConflictSafety.Check(GitRepository.Entries,
                GitRepository.InProgressOperation != GitOperationKind.None, EditorUtility.scriptCompilationFailed);
            if (risk == GitConflictRisk.None)
            {
                return true;
            }
            if (s_confirmed)
            {
                return true;
            }
            if (s_asking == false)
            {
                AskAsync(risk);
            }
            return false;
        }

        private static async void AskAsync(GitConflictRisk risk)
        {
            s_asking = true;
            try
            {
                bool quit = await GitConfirmDialog.ConfirmAsync(null, ActionGlyphKind.Info, GitText.Get("safety.quitTitle"),
                    GitText.Get("safety.quit" + risk), GitText.Get("safety.quitAnyway"), okDanger: true);
                if (quit)
                {
                    s_confirmed = true;
                    EditorApplication.ExecuteMenuItem("File/Exit");
                }
            }
            finally
            {
                s_asking = false;
            }
        }
    }
}
