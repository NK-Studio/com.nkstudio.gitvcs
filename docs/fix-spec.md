# Git VCS 수정 스펙

- 기준 커밋: `e09f997` (0.1.0) · Unity 6000.7.0b5 · macOS · 2026-10-29
- 근거: Unity 에디터에서 직접 조작한 실사용 테스트, GitKraken 12.6.1 의 Pull Requests 패널 조사
- 경로는 `Editor/` 기준, 줄 번호는 기준 커밋 시점

## 공통 원칙

1. 묻기·알리기·입력은 모두 Git 창(또는 도구 창) 안의 UI Toolkit 오버레이로 띄운다. OS 대화상자와 별도 모달 창은 쓰지 않는다.
2. 클릭하면 0.1초 안에 결과(예측 상태, 진행 표시, 비활성화)가 보인다.
3. 낙관적 예측은 작업이 어떻게 끝나든 반드시 거둔다.
4. `.meta` 와 폴더는 한 묶음으로 다루고, git 이 파일을 바꾼 뒤에는 에디터 메모리가 디스크를 덮어쓰지 않게 한다.
5. 오류는 원인에 맞는 다음 행동을 준다. "새로고침 후 다시 시도" 는 분류되지 않은 오류에만 쓴다.
6. 툴바 버튼은 문구 길이와 무관하게 제자리에 있다.

## POP · 팝업 UI 통일

현재 `IDialogService`(`UI/Services/IDialogService.cs`)가 `EditorUtility.DisplayDialog` 로 구현되어 있고, UI Element 카드(`GitConfirmDialog`, `GitErrorDialog`)는 일부 경로만 쓴다.

| ID | 요구 |
|---|---|
| POP-01 | `IDialogService` 를 `ConfirmAsync` / `ChooseAsync` / `AlertAsync` / `PromptAsync` 비동기 API 로 바꾸고 기본 구현은 `GitConfirmDialog` 오버레이. 위험 동작은 `danger` 옵션으로 경고색 + 기본 포커스 취소 |
| POP-02 | `Dialogs.Confirm/Alert/Choose` 직접 호출 47곳을 모두 비동기 API 로 전환 (아래 목록) |
| POP-03 | `GitTextPromptWindow`(`ShowModalUtility`, 9곳)를 오버레이 입력 카드로 교체. 입력 중 검증, 실패해도 닫지 않고 값 유지 |
| POP-04 | `GitToolWindowBase` 계열 도구 창도 자신의 루트를 팝업 호스트로 쓴다 |
| POP-05 | 호스트가 없으면 OS 대화상자로 떨어지지 않는다. Git 창을 열어 그 위에 띄우고, 열 수 없으면 취소로 처리하고 경고 로그 |
| POP-06 | Project Settings > Git 의 git init 확인·실패(`UI/GitSettingsGUI.cs:426, 436`)를 설정 화면 안 인라인 확인으로 |
| POP-07 | `UI/Window/GitWindow.cs:279` 직접 `DisplayDialog` 제거, 터미널 열기 실패 알림 하나로 통합 |
| POP-08 | 우클릭 메뉴는 항목을 고르면 즉시 닫고, 팝업이 열리면 메뉴·드롭다운·툴팁을 모두 닫는다 |
| POP-09 | 카드 종류: 확인 / 선택 / 알림 / 입력 / 오류. 막 클릭으로 닫지 않음, Esc 취소, 한 번에 하나(나머지는 대기열) |
| POP-10 | 병합 충돌은 빨간 오류 대신 안내 카드 + [충돌 보기] |
| POP-11 | 오류 카드에 해결 버튼: Push 거절 → Pull, 리모트 없음 → 리모트 추가, Pop 실패 → 로컬 변경 처리 후 Pop |
| POP-12 | 카드를 닫을 때 포커스를 돌려줘 `FocusController has unprocessed focus events` 경고 제거 |
| POP-13 | 소스에 `DisplayDialog`, `DisplayDialogComplex`, `ShowModalUtility` 가 있으면 실패하는 EditMode 테스트 |

### OS 대화상자 호출부 (관찰 = 테스트 중 OS 창으로 뜨는 것을 확인)

| 위치 | 종류 | 상황 | 관찰 |
|---|---|---|---|
| `UI/Views/BranchActions.cs:54` | Alert | 브랜치 이름 오류 | ✔ |
| `UI/Views/BranchActions.cs:67, 81, 87` | Confirm/Alert | 브랜치 삭제, 강제 삭제, 삭제 실패 | ✔ |
| `UI/Views/BranchActions.cs:101` | Alert | 이름 바꾸기 오류 | |
| `UI/Views/BranchActions.cs:113` | Confirm | 브랜치 푸시 | ✔ |
| `UI/Views/BranchActions.cs:130, 157` | Confirm/Alert | 원격 브랜치 삭제, 업스트림 없음 | |
| `UI/Views/CommitActions.cs:55, 90, 116, 143` | Alert | 리비전·브랜치·태그 이름 오류 | |
| `UI/Views/CommitActions.cs:187, 205, 257, 287, 293, 306` | Confirm/Alert | 리베이스, 실패, 다시 쓰기, fixup, drop, 빈 메시지 | |
| `UI/Views/CommitActions.cs:350` | Confirm | 병합·리셋·체리픽·되돌리기 | ✔ |
| `UI/Views/RefActions.cs:69, 78, 87, 109` | Confirm | 태그 푸시·삭제, stash 삭제 | ✔ |
| `UI/Views/RefActions.cs:132, 154, 160` | Alert/Confirm | stash 브랜치 이름, 터미널 실패, 워크트리 삭제 | ✔ |
| `UI/Views/PendingChangesView.cs:300` | Confirm | 변경 버리기 | |
| `UI/Views/GraphWorkspaceView.cs:345` | Confirm | 모두 버리기 | |
| `UI/Elements/FileActionMenu.cs:89, 203, 232, 256, 262` | Confirm/Alert | 마커째 해결, 패치 실패, 파일 삭제, 새 파일 이름 | ✔ |
| `UI/Elements/CommitFileMenu.cs:106` | Confirm | 파일을 리비전으로 되돌리기 | |
| `UI/Elements/StagingLists.cs:505` | Choose | 내장 저장소 처리 | |
| `UI/Elements/OperationBanner.cs:72, 82` | Confirm | Skip / Abort | |
| `UI/Elements/GitDiffPanel.cs:1964` | Confirm | Discard Hunk | ✔ |
| `UI/Window/GitToolbar.cs:244` | Alert | 터미널 실패 | |
| `UI/Window/GitAccountDialog.cs:41` | Alert | 계정 연결 실패 | |
| `UI/Window/GitConfirmDialog.cs:58–105`, `GitErrorDialog.cs:41` | 대체 경로 | 호스트 없을 때 | |
| `UI/Services/IGitOperationRunner.cs:102, 135, 146` | Alert/Confirm | 실패, index.lock | |
| `UI/Services/GitDiffService.cs:279–322` | Alert/Confirm | 외부 diff 도구 | |
| `UI/Window/GitWindow.cs:279`, `UI/GitSettingsGUI.cs:426, 436` | DisplayDialog 직접 | 터미널, git init | |
| `UI/Window/GitTextPromptWindow.cs:39` | 별도 모달 창 | 이름·메시지 입력 9곳 | ✔ |

범위 밖(유지): 파일·폴더 선택 창(`EditorUtility.Open*/Save*Panel`), 메인 툴바 `GenericMenu`.

## P0 · 데이터 손상·막힘

| ID | 현상 | 원인 | 요구 |
|---|---|---|---|
| P0-01 | 충돌 파일을 Stage File 한 뒤 다시 고쳐도(`MM`) Unstaged 에 안 나옴 | `UI/Elements/StagingLists.cs:250` `RunPredicted` 의 `_prediction` 이 남음 | 예측은 작업이 끝나면(성공·실패·예외) 반드시 제거, 시간 제한, 실제 상태 우선 |
| P0-02 | 갈라진 브랜치에서 Pull 실패, 선택지 없음 | `Core/GitOperations.cs:177` `pull --ff-only` | ff 불가 시 병합/리베이스 선택 카드. 기본값은 `pull.rebase` 설정 |
| P0-03 | 충돌 마커가 남은 파일을 hover Stage File · Stage Hunk 로 경고 없이 스테이징 | 경고가 Mark as Resolved 에만 있음 | 모든 스테이징 경로에서 마커 검사 후 위험 확인 |
| P0-04 | 나란히 보기에서 수정 줄의 오른쪽 + 만 누르면 추가 줄만 스테이징 → 원래 줄과 새 줄이 모두 index 에 | `UI/Elements/GitDiffPanel.cs` `BindSide` | 수정 쌍(삭제+추가)을 한 단위로 스테이징·버리기 |
| P0-05 | Stash 뒤 Unity 가 메모리 상태로 URP 에셋을 다시 써서 Pop 실패 | git 작업 전후 에셋 저장·재임포트 없음 | 작업 전 `SaveAssets`, 후 바뀐 에셋 강제 재임포트. Pop 실패 시 해결 버튼 |
| P0-06 | 커밋 성공 뒤에도 메시지가 입력칸에 남음(브랜치 전환·병합 중에도) | | 커밋 성공 즉시 비우기, 병합 중에는 `MERGE_MSG` |

## P1 · 기능 버그

| ID | 현상 | 요구 |
|---|---|---|
| P1-01 | 병합 커밋이 "Merge into Current Branch" 로 표시 — `Core/GitStrings.cs:506`, `:749` 키 `commit.merge` 중복 | 목록용 키 분리, 중복 키 검사 테스트 |
| P1-02 | 리모트 이름 `origin` 고정 (`Core/GitOperations.cs:184`, 브랜치 Push 메뉴) | 리모트가 여럿이면 선택, 없으면 리모트 추가 안내 |
| P1-03 | 리모트 없을 때 Commit & Push 가 "failed" — 실제로는 커밋 성공 | 리모트 없으면 버튼 비활성, 결과를 커밋/푸시로 나눠 표시 |
| P1-04 | 행 툴팁이 아래 행 클릭을 가로챔 | 툴팁 `pickingMode = Ignore`, 클릭 시 숨김 |
| P1-05 | 낡은 툴팁·상태 문구(Continue 툴팁, "2 outgoing", "Track … failed") | 툴팁은 열 때마다 다시 계산, 상태 문구는 다음 작업 시작 또는 10초 뒤 지움 |
| P1-06 | Stash 직후 그래프에 `refs/stash` 없음 | stash·pop·drop 은 전체 새로고침 |
| P1-07 | Esc 한 번에 서랍 전체가 닫힘 | 가장 위 하나만 닫음 |
| P1-08 | 워크트리 생성 중 "This folder isn't empty." 거짓 오류 | 작업 중 검증 중지 |
| P1-09 | `Unknown pseudo class "last-child"` 경고 | 지원하지 않는 의사 클래스 제거 |
| P1-10 | PR 목록이 Fetch 로 갱신 안 됨 | Fetch 뒤 PR 목록 다시 읽기 |
| P1-11 | Add Remote > GitHub 저장소 목록 캐시 | 드롭다운 열 때 다시 읽기 + 새로고침 버튼 |
| P1-12 | 같은 경로가 Staged·Unstaged 양쪽인 경우 표시 | 회귀 테스트 |

## P2 · 반응 속도

| ID | 측정 | 요구 |
|---|---|---|
| P2-01 | 툴바 버튼이 상태 문구·브랜치 이름에 따라 30px 넘게 이동, 실제 오클릭 | 상태 문구 영역 폭 고정 |
| P2-02 | Stage File 약 1초 뒤 반영 | 낙관적 갱신 (P0-01 선행) |
| P2-03 | 커밋 후 그래프 2~2.5초 | 진행 표시 |
| P2-04 | Discard Hunk 2초, 언어 전환 1~2초 무표시 | 진행 표시 |
| P2-05 | 새로고침 중 Stage All 무시 | 대기열에 넣어 실행 |
| P2-06 | macOS 작업 트리 변경이 포커스 복귀 때만 반영, diff 휠 1칸=1줄 | Refresh 즉시 실행, 휠 줄 수 OS 설정, Cmd+Home/End |

## P3 · UX·Unity 워크플로

| ID | 현상 | 요구 |
|---|---|---|
| P3-01 | Stage Folder 가 폴더 자신의 `.meta` 를 빠뜨림 → 브랜치 전환 뒤 빈 폴더 재생성 경고 | 폴더 단위 동작에 `폴더.meta` 포함 |
| P3-02 | `.meta` 없는 새 에셋 커밋 경고 없음 | 커밋 전 짝 검사 경고 |
| P3-03 | 커밋 상세는 `.meta` 를 묶지 않음 | 변경 패널과 같은 묶음 |
| P3-04 | 충돌 시 패널 자동 전환 없음, 충돌 구역 없음, 진행 중 Pull/Push 활성 | 자동 전환, 충돌 구역, 진행 중 비활성 |
| P3-05 | 브랜치 이름 칸 placeholder 잘림, 실시간 검증 없음, 오류 시 입력값 버림 | POP-03 |
| P3-06 | Discard Hunk 후 diff 화면이 닫힘 | 남은 변경이 있으면 유지 |
| P3-07 | "되돌릴 수 없음" 문구 vs 툴바 Undo | 문구 정리 |
| P3-08 | 리모트 메뉴에 Fetch 만, 빈 리모트 안 보임 | 이름 변경·URL 수정·삭제, 빈 리모트 표시 |
| P3-09 | PR 제목 기본값이 병합 커밋, 생성 후 브라우저가 포커스 가져감, PR 클릭 무반응 | 병합 커밋 건너뛰기, 브라우저 열기 끔, 클릭 시 그래프 선택 |
| P3-10 | 첫 Push 후 PR 제안 없음, 브랜치 삭제 시 원격 삭제 선택 없음 | 결과 카드에 [PR 만들기], 체크박스 |
| P3-11 | 언어 전환 후 영어 혼재 | git 용어 외 번역 |
| P3-12 | 토글·비활성 버튼 상태 구분, 작은 + 버튼, 레인 색 변화, untracked diff 헤더 | 각각 수정 |

## PR · GitKraken 과 같은 Pull Requests 패널

GitKraken 12.6.1 에서 `www` 에 PR 3개(#2 담당자 지정, #3, #4 draft)를 만들고 조사한 결과.

### 구성

- 구역 머리: `PULL REQUESTS` + 전체 개수
- 머리 아래: `Search pull requests` 검색칸 + 필터 추가 버튼(깔때기+)
- 필터 그룹(기본 4개): My Pull Requests, Assigned To Me, Awaiting My Review, All Pull Requests. 각 그룹 아래 리모트(GitHub 아이콘 + `origin`)로 한 번 더 묶고 그 아래 PR
- PR 줄: 상태 아이콘 + `#번호 제목`, draft 는 제목에 표시. 호버하면 카드(제목 #번호, "작성자 wants to merge head → base")

### 그룹(필터) ⋮ 메뉴

```
Edit this filter
Remove this filter
─────────────
Move this filter up      (첫 그룹 제외)
Move this filter down    (마지막 그룹 제외)
```

- 필터 추가/편집 서랍: `Filter name`, `Filter Pull Requests`(GitHub 검색 문법 쿼리), `Create Filter`/`Edit Filter`, 아래에 문법 도움말(author, assignee, involves, review-requested, reviewed-by, base, head, draft, label, review, status, no, created, updated)
- 기본 쿼리: My = `author:@me`, Assigned = `assignee:@me`, Awaiting = `review-requested:@me`, All = (조건 없음)

### PR ⋮ 메뉴

```
View pull request #N on GitHub.com
Copy link for pull request #N
─────────────
View changes in #N
Go to <head> in graph
─────────────
Checkout <remote>/<head>
Create Worktree from Pull Request
```

(GitKraken 의 "Review on GitKraken.dev" 는 대응 서비스가 없어 제외)

### PR 클릭

PR 상세 화면: `#번호`, 제목, 상태 배지(Open/Draft), "작성자 wants to merge head into base", 설명, 충돌 여부, `Merge Pull Request` + 방식 드롭다운(Create a merge commit / Squash and merge / Rebase and merge), 오른쪽에 바뀐 파일 수·Reviewers·Assignees·Labels·Branch.

## 검증

- 자동: POP-13, P1-01 중복 키, P0-01 예측 해제, P0-04 패치, P0-02 인자, P0-03 마커 검사
- 수동: 테스트 시나리오 재실행 — OS 창 0개, 클릭 0.1초 내 반응, 콘솔 경고 0개

## 진행 상태

검증(1차): `dotnet build`(패키지·테스트 csproj) 오류 0, Unity 재컴파일 오류 0, EditMode 테스트 804개 중 795 통과·8 건너뜀·1 실패
(`GitCommitTemplateTests` 의 `C:/` Windows 경로 케이스 — 이번에 건드리지 않은 코드, macOS 에서만 실패). 새 회귀 테스트 41개(`FixSpecRegressionTests`) 모두 통과.
화면 확인은 화면이 잠긴 상태라 마우스 대신 에디터 명령(창 캡처·코드 실행)으로 했다.

| ID | 상태 | 비고 |
|---|---|---|
| POP-01~05, 07, 09, 12, 13 | 완료 | `OverlayDialogService`·`GitPopupHosts`·`GitPromptDialog`·`GitPickDialog`. 소스의 `DisplayDialog`/`ShowModalUtility` 0건(테스트로 고정) |
| POP-06 | 완료 | 설정 화면 git init 은 화면 안 확인 영역 |
| POP-08 | 완료 | OS 모달이 사라져 메뉴 잔상 원인 제거 |
| POP-10 | 완료 | 충돌은 안내 카드 + [충돌 보기] |
| POP-11 | 완료 | Push 거절→Pull, 리모트 없음→리모트 추가, Pop 막힘→버리고 Pop. git 메시지를 영어(`LC_MESSAGES=C`)로 받아 오류 분류가 동작하게 함 |
| P0-01 | 완료 | 예측 3초 수명 + 자기 예측만 해제 |
| P0-02 | 완료 | `pull.rebase` 설정 우선, ff 불가 시 병합/리베이스 선택 |
| P0-03 | 완료 | 목록·hunk·줄·파일 전체 스테이징 모두 마커 확인 |
| P0-04 | 완료 | 나란히 보기에서 같은 행의 짝 줄을 함께 스테이징 |
| P0-05 | 완료(완화) | 작업 전 `SaveAssets`, 후 동기 재임포트, Pop 막힘 해결 버튼. URP 가 옛 버전 에셋을 스스로 업그레이드해 다시 쓰는 것은 막을 수 없다 |
| P0-06 | 완료 | 커밋과 Push 분리, 커밋 성공 즉시 입력칸 비움 |
| P1-01~04, 06~11 | 완료 | |
| P1-05 | 부분 | 상태 문구는 10초 뒤 정리. 이미 떠 있는 Unity 기본 툴팁 글은 Unity 가 다시 읽지 않는다 |
| P1-12 | 완료 | 기존 `StagingLayoutTests` 가 검증, 원인은 P0-01 |
| P2-01, 02, 04, 05 | 완료 | |
| P2-03 | 완료(3차) | 커밋 클릭→그래프 반영 약 0.9초(아래 3차 개발 참고) |
| P2-06 | 완료(3차) | macOS FSEvents 감시(`MacDirectoryWatcher`) 추가 — 바깥에서 고친 파일이 Refresh 없이 약 0.6초 안에 보임 |
| P3-01~05, 07~12 | 완료 | |
| P3-06 | 의도된 동작 | 그 파일에 남은 Unstaged 변경이 없을 때만 닫힘 |
| PR 패널 | 완료 | 검색 칸·필터 추가, 필터(묶음) ⋮: 고치기/지우기/위로/아래로, 원격 줄, PR ⋮: GitHub 보기·링크 복사·변경 보기·그래프로 가기·체크아웃·워크트리. 필터 조건은 로컬 평가 + GitHub 검색 API |
| PR 상세 화면 | 완료(3차·4차) | PR 을 누르면 상세 서랍(`PullRequestDetailDrawer`). 대화·리뷰 작성·Draft 전환·닫기/다시 열기는 4차 |

## 2차 실사용 테스트 (2026-10-29)

마우스·키보드로 실제 조작. 다른 환경(Windows, Git 창 닫힘, 보조 창)은 제외.
검증: Unity 재컴파일 오류 0, EditMode 테스트 810개 중 801 통과·8 건너뜀·1 실패(1차와 같은 `C:/` 경로 케이스). 새 회귀 테스트 6개 추가.

### 통과

| 영역 | 확인한 것 |
|---|---|
| 스테이징 | Stage File 누르자마자 Staged 로 이동, 스테이징 뒤 또 고친 파일(MM)은 양쪽 목록에, 나란히 보기에서 한 줄 Stage 시 `-row 3`/`+row 3 CHANGED` 짝이 함께 들어감, 충돌 표식 경고(Stage File·Stage All) |
| 커밋 상자 | 커밋 직후 입력칸 비움, 새 에셋을 .meta 없이 커밋하면 경고(폴더 .meta 포함) → Include .meta Files 로 함께 커밋 |
| Push | 원격 둘이면 고르기 카드, 첫 Push 뒤 PR 만들기 제안 → 서랍이 브랜치·제목 채워 열림, 거절 → [Pull] 제안 |
| Pull | 갈라짐 → Merge/Rebase 카드, Merge 성공 |
| 충돌 | 안내 카드, 진행 배너(Continue/Abort/Skip), 진행 중 Pull·Push 꺼짐, 오른쪽 클릭으로 한쪽 고르기 → Continue, Abort |
| Stash·Pop | 막힘 → 버리고 Pop, 끝까지 복원 |
| PR 패널 | 필터 ⋮ 고치기·지우기(확인 카드)·위로·아래로, GitHub 검색 조건(`review:none` 3개, `review:approved` 0개), PR 메뉴 변경 보기·워크트리 만들기·체크아웃(작업 변경이 있으면 까닭 안내), 검색 칸 포커스·`#2` 거르기 |
| 원격 메뉴 | Prune, 이름 바꾸기(중복 이름 검사), URL 고치기, 삭제(확인 카드) |
| 툴바 | 배지·꺼짐 상태가 바뀌어도 버튼 자리 그대로 |

### 새로 찾아 고친 것

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| R2-01 | 변경 묶음(Hunk) 보기 → 통합(Inline) 보기로 바꾸면 diff 가 빈 화면 | 가변 → 고정 높이로 바꾼 틀에 ListView 내용 높이가 0 으로 남음 → 바꾼 다음 틀에 한 번 더 채움(`GitDiffPanel.ApplyLayout`) |
| R2-02 | 충돌 카드 [충돌 보기] 가 오른쪽 패널만 바꾸고 파일을 열지 않음 | 첫 충돌 파일을 골라 diff 를 엶(`StagingLists.SelectFirstConflict`, `GraphWorkspaceView.ShowConflicts`) |
| R2-03 | 병합이 끝나도 커밋 제목 칸에 "Merge branch …" 가 남음 | 채운 준비 메시지를 메모리에만 들고 있어 병합 중 스크립트를 다시 읽으면 잊음 → `StagingState.PreparedMessage` 로 저장 |
| R2-04 | Rebase 로 Pull 하면 작업 중 변경이 있을 때 "cannot pull with rebase" 로 실패 | `pull --rebase --autostash`(GitKraken 과 같음) |
| R2-05 | Rebase 충돌 카드에 "0 file(s)", 파일 목록 없음 | git 출력에 "Merge conflict in" 이 없으면 `git diff --name-only --diff-filter=U` 로 다시 물음 |
| R2-06 | Stash 뒤에도 새 파일(untracked)이 작업 폴더에 그대로 남음 | `GIT_LITERAL_PATHSPECS=1` 이면 `stash -u` 가 안의 매직 경로(`:/`)를 글자로 읽어 지우지 않음 → stash push 만 끄고 경로는 `:(literal)` 로 감쌈(`GitCommand.IsStashPush`·`LiteralStashPaths`) |
| R2-07 | Pop 할 새 파일이 이미 있으면 "already exists" 로 멈추며 추적 파일만 반쯤 적용, 카드의 버리고 Pop 도 실패 | Pop 전에 stash 의 새 파일과 겹치는 파일을 찾아, 내용이 같으면 조용히 치우고 다르면 카드로 물음. 추적 파일로 막혀 다시 할 때도 먼저 치움(`SyncFlows.UntrackedCollisionsAsync`) |
| R2-08 | 직접 만든 PR 필터를 이름 그대로 두고 다시 고치면 이름이 사라짐 | 저장(JsonUtility)을 거치면 null 번역 키가 "" 가 되는데 `!= null` 로 검사 → `GitPullRequestFilters.KeepsDefaultName` |
| R2-09 | PR 줄 끝 ⋮ 가 안 보임(오른쪽 클릭만 됨) | TreeView 줄 내용이 줄어들지 않아(flex-shrink 0) 깊이 들여쓴 긴 제목이 사이드바 밖으로 밀어냄 → 사이드바 트리에서 줄어들게 하고 제목은 말줄임 |

### 남은 관찰(고치지 않음)

- .meta 누락 경고는 새로 추가한 에셋만 본다. 이미 추적 중인데 .meta 가 커밋된 적 없는 파일은 대상이 아니다(설계).
- Pull 버튼 툴팁의 받을 커밋 수는 Fetch 전에는 0 으로 보인다.
- 원격 URL 고치기는 형식을 검사하지 않는다(GitKraken 도 같음).
- 바깥에서 고친 파일은 Refresh 를 눌러야 보인다(P2-06 의 FSEvents 미구현과 같은 건). 이미 떠 있는 Unity 툴팁 글이 남는 것은 P1-05.
- `pull --rebase` 는 병합 커밋을 풀어 로컬 커밋을 다시 적용하므로, 이미 병합으로 해결한 충돌이 다시 나올 수 있다(git 동작).

## 3차 개발 (2026-10-29)

작은 것부터 큰 순서로 진행. Windows 확인은 macOS 개발을 마친 뒤 마지막에 한다.
검증: Unity 재컴파일 오류 0, EditMode 테스트 835개 중 827 통과·8 건너뜀·실패 0(1·2차의 `C:/` 경로 실패도 고침). 새 회귀 테스트 추가.

| 항목 | 내용 |
|---|---|
| Windows 경로 테스트 | `GitCommitTemplate.ResolvePath` 가 macOS 에서 `C:/…` 를 상대 경로로 보던 것 — 드라이브 문자 경로도 절대 경로로 본다 |
| 원격 URL 검사 | URL 고치기 카드·리모트 추가 서랍이 `https://…`·`git@host:owner/repo`·있는 폴더 경로만 받는다(`GitRemoteForm.IsPlausibleUrl`). "/" 같은 글은 막고 까닭을 보인다 |
| Pull 툴팁 | 받을 커밋 수 아래에 "마지막 Fetch: n분 전 / 아직 Fetch 하지 않음"(`GitToolbar.FetchAgeText`) |
| .meta 누락 경고 | 새 에셋뿐 아니라, 이미 추적 중인데 .meta 가 저장소에 없는 에셋을 고쳐 커밋할 때도 경고 |
| PR 필터 추가 아이콘 | 검색 칸 옆 돋보기를 Material "filter_plus" 아이콘(`ActionGlyphKind.FilterAdd`)으로 바꿈 |
| 커밋 후 그래프 반영(P2-03) | 측정: 커밋 클릭→새 HEAD 약 0.8초→그래프 1.0~1.3초. 원인: Unity(Mono)에서 git 프로세스 띄우기 1회 30~60ms, 입력 없는 에디터 틱 간격 약 0.1초(메인 스레드로 돌아올 때마다), 작업 목록 다시 그리기(200ms+)가 끝나야 그래프 git 이 시작. 수정: 새로고침 git 8회→4회(config 한 번·HEAD/upstream 은 for-each-ref 에서), 즉시 새로고침은 다음 틱을 기다리지 않음, 그래프 읽기·커밋 git 사슬을 백그라운드 한 번으로 묶음, 그래프 git 을 목록 그리기보다 먼저 시작·뷰를 사이드바보다 먼저 알림, git 이 도는 동안·직후 0.3초만 Git 창이 스스로 다시 그려 틱을 깨움. 결과 약 0.9초 |
| 바깥 파일 변경 감지(P2-06) | macOS FSEvents(`MacDirectoryWatcher`, CoreServices P/Invoke·전용 디스패치 큐). 도메인 리로드 전에 스트림을 멈추고 진행 중 콜백을 기다린다. 실패하면 예전 폴링으로 돌아간다. 유휴 중 새로고침이 반복되지 않음 확인 |
| PR 상세 화면 | PR 을 누르면 열리는 서랍: 번호·제목·상태 배지(Open/Draft/Merged/Closed), "누가 어디로 커밋 n개를 합치려 함", 파일·줄·댓글 수, 병합 가능 여부(충돌·보호 규칙·뒤처짐·검사 실패·계산 중), 병합 방식(merge/squash/rebase) + Merge(확인 카드, 본 때의 head SHA 를 함께 보내 그 사이 새 커밋이 오면 거절), GitHub 보기·바뀐 것 보기·체크아웃, 설명, 리뷰어·담당자·라벨·브랜치, 바뀐 파일 목록. 병합하면 PR 목록을 다시 읽고 fetch |

### 남은 것

- Windows 에서 전체 확인(1·2차의 다른 환경 항목 포함).
- PR 상세: 리뷰어·담당자·라벨 고치기(4차에서 대화·리뷰·Draft·닫기는 끝남).
- 커밋 후 그래프 반영의 나머지 시간은 사이드바가 같은 순간 띄우는 git 10여 개와의 프로세스 경합이다.
- 이미 떠 있는 Unity 기본 툴팁 글이 남는 것(P1-05)은 Unity 쪽 한계.

## 4차 개발 — PR 대화·리뷰·상태 바꾸기 (2026-10-29)

검증: Unity 재컴파일 오류 0, EditMode 테스트 837개 중 829 통과·8 건너뜀·실패 0. 시험 저장소 PR #2 로 실제 확인 뒤 되돌림(코멘트 지움, open·draft=false).

| 항목 | 내용 | 확인 |
|---|---|---|
| 대화 보기 | 일반 코멘트(`issues/{n}/comments`)·리뷰(`pulls/{n}/reviews`)·코드 줄 코멘트(`pulls/{n}/comments`)를 시간순으로 합쳐 보인다. 승인은 초록, 변경 요청은 빨강, 줄 코멘트는 "파일:줄 에 코멘트". 아직 안 낸(PENDING) 리뷰와 본문 없는 코멘트 리뷰는 뺀다 | 코멘트가 대화에 나옴 |
| 코멘트 쓰기 | 쓰고 [Comment] → 대화·댓글 수를 다시 읽고 칸을 비운다. 실패하면 글을 남기고 오류 카드 | 확인 |
| 리뷰 쓰기 | [Approve](글 없이 됨)·[Request changes](글 필요). 자기 PR 승인·변경 요청은 GitHub 가 거절 → 오류 카드 | 실제 제출은 권한 확인에서 막혀 하지 않음(코드·테스트로만 확인) |
| Draft ↔ Ready | REST 에 없어 GraphQL(`convertPullRequestToDraft`·`markPullRequestReadyForReview`, PR 의 `node_id`). GraphQL 은 실패해도 200 이라 `errors` 를 읽는다 | Draft → Ready 왕복 확인 |
| 닫기·다시 열기 | `PATCH pulls/{n}` state. 닫기는 확인 카드(위험 버튼). 병합된 PR 에는 버튼 없음 | 닫기(카드) → 다시 열기 확인 |
| 사이드바 반영 | 상태가 바뀌면 PR 목록을 다시 읽는다(Draft 표시·묶음) | — |

## 5차 — 남은 시험·다듬기·스크롤바 (2026-10-29)

검증: Unity 재컴파일 오류 0, EditMode 테스트 844개 중 836 통과·8 건너뜀·실패 0. 화면은 마우스 대신 에디터 명령·창 캡처로 확인(사용자가 다른 앱 사용 중).

| 항목 | 결과 |
|---|---|
| Git 창이 닫힌 상태의 팝업 | 통과 — Git 창을 모두 닫고 띄울 곳 없는 확인 카드를 요청하면 Git 창이 열리고 그 위에 카드, 답(OK)이 돌아옴. 두 카드를 한꺼번에 요청하면 하나씩 차례로 뜨고 각자 답이 돌아옴 |
| 보조 창 위 팝업 | 통과 — Git 창 없이 비교 창만 열려 있으면 비교 창 위에 카드(Git 창을 열지 않음). 비교 창을 닫은 뒤에는 낡은 호스트를 쓰지 않고 Git 창을 열어 띄움 |
| 승인·변경 요청 실제 제출 | 통과 — 다른 계정(hykim-nk, 쓰기 권한 협업자)으로 패키지에 로그인해 NK-Studio 의 PR #2 에 Approve·Request changes 를 냄. GitHub 에 APPROVED·CHANGES_REQUESTED 로 기록되고 대화에 초록·빨강으로 보임. 보호 규칙이 없는 저장소라 변경 요청 뒤에도 병합 가능(clean)으로 보이는 것은 GitHub 와 같음 |
| 사이드바 다시 읽기 미루기 | 저장소가 바뀌면 보이는 뷰(그래프)의 git 이 끝난 뒤(최대 1초) 사이드바·stash 개수·브랜치 목록 미리 읽기를 시작. 같은 조건(Unity 가 뒤에 있음) 비교: 커밋→그래프 평균 1.34초 → 1.21초(약 10%, 흔들림 큼) |
| 영어 단수·복수 | `GitText.Format` 이 영어일 때 "1 commits"·"1 files" 같은 곳을 단수로(정해진 명사만, "#1"·"v1" 은 그대로) |
| 스크롤바 | Unity 기본(화살표·각진 막대) 대신 화살표 없는 6px 둥근 반투명 막대, 마우스 올림·끌기에서 진해짐. 밝음·어두움 테마 토큰(--git-scroll-thumb…) |
| 새로 찾아 고친 것 | 스크립트가 Unity 가 뒤에 있을 때 다시 컴파일되면(외부 IDE 저장 등) Git 연동 시작이 `delayCall` 에 걸려 앞으로 돌아올 때까지 "Starting Git integration." 에 머묾 → 업데이트 틱에서도 한 번 시작 |

## 6차 — UI 다듬기 (2026-10-29)

사용자가 화면을 보며 짚은 것을 고쳤다. 화면 확인은 대부분 실제 창 백그라운드 캡처, 어두운 스킨은 에디터 스킨을 잠시 바꿔 확인한 뒤 되돌렸다.

| 항목 | 내용 | 확인 |
|---|---|---|
| Preferences > Git | 화면 여백, 라벨 칸 220px(체크박스가 라벨에 붙지 않게), "일반" 묶음 제목, GitHub 계정을 카드(둥근 아바타·세로 가운데 이름·오른쪽 로그아웃)로 | 로그인한 화면 확인. 로그인 전·로그인 중(코드 입력) 화면은 로그아웃해야 볼 수 있어 미확인 |
| 패키지 아이콘 | Unity 내장 VersionControl 아이콘은 밝은 스킨에도 밝은 회색만 있어 밝은 툴바·탭에서 안 보였다 → 패키지 PNG 두 벌(밝은 스킨 #555 / 어두운 스킨 d_ #C4C4C4). 툴바용 `GitToolbar`(그림 약 63%)와 창 탭용 `GitWindow`(약 51%)를 따로 둔다(`GitIcons.Toolbar`·`Window`) | 밝은·어두운 스킨, 에디터를 켠 채 스킨 전환 시 툴바·탭 모두 바로 따라 바뀜 |
| 사이드바 아이콘 굵기 | 선 아이콘(1.6 칸)과 Material 윤곽 채움 아이콘(1.25 칸)이 섞여 굵기가 달랐다 → 나무는 윤곽 덧긋기로 1.6, 구름·필터+ 는 선으로 다시 그림(곡선 윤곽을 덧그으면 이음매에 가시) | 확인 |
| 사이드바 줄·검색 칸 | 줄 높이 24 → 28. PR 검색 칸은 바로 아래 자식 줄 화살표 위치부터(TreeView 는 들여쓰기를 화살표 칸 여백으로 넣어, 화살표 칸을 숨긴 만큼 19px 를 직접 띄움), 높이 24·글자 12 | 확인 |
| 검색창 돋보기 | Unity 기본 PNG(키우면 흐릿) 대신 벡터 아이콘(`ActionGlyph.ReplaceSearchIcon`: 사이드바·Workspace·브랜치 탐색기) | 확인 |
| 스크롤바 | 화살표 없는 6px 둥근 반투명 막대(5차) | 밝은·어두운 테마 모두 확인 |
| 서랍 전환 | 다른 서랍이 열린 채 새 서랍을 열면 두 서랍이 0.4초 겹쳐 미끄러지며(앞쪽 서랍은 밑에 깔려) 버벅였다 → 이전 서랍은 바로 닫고 새 서랍은 움직임 없이 바로, 열리는 서랍은 맨 앞. 처음 열기·닫기는 그대로 미끄러짐 | 상태로 확인(전환 직후 새 서랍만 보임). 손으로 빠르게 누르는 체감은 사용자 확인 필요 |
| Create Pull Request 서랍 | 고르기 칸 글자 줄어듦·말줄임, ⌄ 칸 28px, 가운데 화살표를 저장소 칸 높이로, 체크박스 간격 | 확인 |
| 서랍 보조 버튼 | Unity 기본 회색 대신 흰 바탕·얇은 테두리(취소·Browse·View on GitHub…) | Create PR·Create Worktree·PR 상세에서 확인 |
| PR 서랍 열 때 깜빡임 | 칸을 비웠다 다시 채우며 "Select…"·병합 커밋 제목이 잠깐 보였다 → 지난 참조로 같은 프레임에 채움, 처음엔 다 읽을 때까지 숨김, 병합 아닌 제목은 기억해 바로 씀 | 프레임별 값 기록으로 확인(두 번째부터 첫 프레임에 값이 다 있음, 처음엔 제목만 한 프레임 늦음) |

## 7차 — Windows 확인·설정 정리 (2026-10-29)

Windows 11 · Unity 6000.7.0b5 · Git for Windows 2.53.0. 새 Unity 프로젝트(gitTest)에 git URL 로 설치한 뒤 패키지를 Packages/ 에 내려받아(embedded) 고치며 확인.
원격은 로컬 bare 저장소(`origin`, Windows 경로)와 시험 저장소(`github` = `NK-Studio/gitvcs-pr-test-www`, Push 하지 않음) 둘.
검증: 설치 직후 EditMode 844/844 통과(건너뜀 0), 아래 수정 뒤 849/849(새 회귀 테스트 5개). Unity 재컴파일 오류 0. 마우스·키보드로 실제 조작.

### 통과

| 영역 | 확인한 것 |
|---|---|
| 설정 | Project Settings 의 git init 화면 안 확인 → 저장소 생성, Git 창 바로 반영 |
| 원격 | 원격 없을 때 Push → 리모트 추가 카드, "/" 거부, `E:\…\gitTest-remote.git` 받음, 빈 원격 표시, GitHub 탭 저장소 목록 |
| 커밋 | 커밋 직후 입력칸 비움, .meta 없는 새 에셋 경고 → ".meta 도 함께 커밋" 이 Unity 임포트를 돌려 새로 생긴 .meta 까지 커밋 |
| 바깥 변경 | 탐색기·터미널에서 만든 파일이 Refresh 없이 약 1초 안에 보임(`WindowsDirectoryWatcher`) |
| 스테이징 | Stage 즉시 이동, 스테이징 뒤 또 고친 파일(MM) 양쪽 표시, 나란히 보기 오른쪽 + 로 수정 짝 함께 스테이징(CRLF 경고 있는 저장소에서도) |
| 충돌 | 진행 배너·Pull/Push 꺼짐·충돌 구역·병합 메시지 채움, 마커 경고(그대로 Stage), 우클릭 ours 로 해결 → 이어가기, 중단 → 시작 전 상태 |
| Stash·Pop | 새 파일까지 stash, Pop 때 같은 내용 새 파일은 조용히 치우고 다른 내용만 카드 → 버리고 Pop 으로 끝까지 복원 |
| 브랜치 | 이름 칸 실시간 검사(공백·`..`), 커밋 안 한 변경 안내 후 전환, 새 브랜치 Push 때 원격 둘이면 고르기 카드 |
| PR | 묶음·원격 줄·호버 카드·⋮ 메뉴, 그래프로 가기, 상세 서랍, Draft ↔ Ready, 닫기(확인 카드) → 다시 열기(GitHub 에서 상태 확인 후 원래대로) |
| 콘솔 | 실행 중 경고·오류 없음(FocusController 경고 없음) |

### 새로 찾아 고친 것

| ID | 증상 | 원인 → 수정 |
|---|---|---|
| W-01 | 개인 설정(언어·테마·git 경로·Diff 등)이 Project Settings 에 있음 | Project Settings 는 저장소에 매인 것(GitHub 계정·자동 Fetch·rebase 멈추기·.meta 묶기·저장소)만, 나머지는 Preferences > Git 으로. 값은 EditorPrefs 에 두어 모든 프로젝트 공통, 처음 열 때 UserSettings 값을 옮김. git 이 백그라운드에서 읽으므로 필드를 캐시로 씀(`GitVcsSettings.OnEnable`) |
| W-02 | Windows 에서 갈라진 Pull 이 묻지 않고 바로 병합 | Git for Windows 설치 프로그램이 시스템 gitconfig 에 `pull.rebase=false` 를 늘 써 둠 → 시스템 범위 값은 무시(`ParseScopedPullRebase`, `--show-scope`) |
| W-03 | Fetch 전 Pull 이 충돌하면 빨간 "Pull 실패"(fetch 줄만 보임) | fetch 줄은 stderr, CONFLICT 는 stdout 인데 `GitResult.Message` 가 stderr 만 씀 → 실패면 둘 다 |
| W-04 | Fork·Unity VCS 배치에서 [충돌 보기] 무반응 | GitKraken 뷰에만 연결 → `GitView.ShowConflicts` 로 세 배치 모두 |
| W-05 | 한국어 중단 확인 카드의 두 버튼이 모두 "취소" | `inprogress.abort` → "중단", "{0} 을(를)" 띄어쓰기 |
| W-06 | Fork 배치 Shift+Stage(전부) 무반응 | Button 의 Clickable 이 수정 키 없는 클릭만 받음 → Shift 활성기 추가(모든 OS) |
| W-07 | 로컬 폴더 원격에 첫 Push 해도 PR 만들기 제안 → 빈 서랍 | 올린 원격이 GitHub 일 때만 제안(`PushedToGitHubAsync`) |
| W-08 | 자동 Fetch·간격, rebase 충돌 시 멈추기, .meta 묶어 보기·짝 옮기기가 Project Settings 에 남음 | 이 값들도 원래 UserSettings(저장소에 안 올라감)라 팀 공유가 아니었다 → Preferences > Git·EditorPrefs 로. Project Settings 에는 이 저장소의 GitHub 계정과 저장소 상태(git init)만 |

### 남은 관찰(고치지 않음)

- 패키지를 embedded 로 두면 Unity 6.7 분석기 경고(UAL0010·UAL0013) 16개: `GitCommand`·`GitIntegration`·`GitPullRequestFilter`·`GitShadow`·`HoverCallout` 에 `NoAutoStaticsCleanup` 이 없음. git URL 설치에서는 안 보임.
- Draft PR 상세의 병합 안내가 "GitHub 에서 Ready for review 로 바꾸면" — 바로 옆에 Ready for review 버튼이 있다.
- 한국어 글의 "{0} 을(를)" 띄어쓰기가 9곳 더 있다.
- Push 뒤 Unity 기본 툴팁 글이 잠깐 남는 것(P1-05).

## 남은 일과 시험 흔적 (2026-10-29)

- **Windows 전체 확인**: 7차에서 했다. 확인하지 않은 것: 대화·코멘트·리뷰 쓰기(GitHub 에 흔적이 남아 하지 않음), Hunk 버리기, 워크트리, 언어 전환 체감 속도.
- **시험 흔적은 Windows 확인을 위해 일부러 남겨 둔다**(사용자 결정). Windows 에서도 같은 원격·PR·브랜치로 Push·Pull·충돌·PR 화면(대화·리뷰·Draft·닫기)을 다시 해 보려는 것이다. Windows 확인이 끝나면 지운다.
  - www 저장소의 `origin` 원격 → 시험 저장소 `NK-Studio/gitvcs-pr-test-www`(비공개)
  - 그 저장소의 PR #2~4(#2 에는 hykim-nk 의 승인·변경 요청 기록이 남아 있다 — GitHub 리뷰는 지울 수 없다), 브랜치 `feature/pr-menu-a`·`feature/pr-menu-b`·`feature/pr-menu-draft`·`test/round2`
  - hykim-nk 협업자 권한은 시험 뒤 뺐다(다시 시험하려면 다시 초대).
- **하지 않기로 한 것**: PR 상세에서 리뷰어·담당자·라벨 고치기(추가 기능), Unity 기본 툴팁 글이 남는 것(P1-05)·URP 에셋 자동 업그레이드(P0-05)는 Unity 쪽 한계로 종료. 커밋 후 그래프 반영 속도(약 0.9~1.2초)는 요청이 있을 때만 더 다룬다.
- **미확인**: Preferences 의 로그인 전·로그인 중 화면, 서랍을 손으로 빠르게 바꾸는 체감.

