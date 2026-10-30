# Changelog

이 패키지의 바뀐 점을 버전별로 적는다. 형식은 [Keep a Changelog](https://keepachangelog.com/ko/1.1.0/), 버전은 [Semantic Versioning](https://semver.org/lang/ko/) 을 따른다.

## [1.0.0] - 2026-10-30

첫 정식 버전. Unity 기본 Version Control 모드(Visible Meta Files) 그대로 컴퓨터에 설치된 git 으로 동작한다.
Unity 6000.0(6.0) 이상. 6.0.84 와 6.7.0b5 에서 확인했다.

### 시작 화면
- GitHub 연결 화면: 로그인 코드는 글자마다 칸 상자로 보이고 복사하면 차례로 반짝인다. 연결 화면에서 Git 창으로 넘어갈 때 전환 연출.
- 프로젝트가 아직 저장소가 아니면 로그인보다 먼저 같은 화면에서 Git 저장소를 만든다(Unity 용 .gitignore 포함). 만든 뒤 로그인하지 않았으면 GitHub 연결을 선택으로 권한다.
- 로그인하지 않고 쓰는 중이면 ⋮ 메뉴 "GitHub 로그인…" 으로 연결 화면에 돌아갈 수 있다.

### Git 창
- GitKraken·Fork 처럼 쓰는 Git 창: 왼쪽 사이드바(로컬·원격 브랜치, 태그, 스태시, 워크트리, 서브모듈, Pull Request), 가운데 세로 커밋 그래프, 오른쪽 스테이징·커밋 패널.
- 위 작업 막대: 실행 취소·다시 실행, 페치·풀·푸시, 브랜치, 스태시·팝, 터미널, 새로고침.
- Fork 식 브랜치 탐색기(커밋 목록 + 커밋·변경·파일 트리), Gluon 식 워크스페이스, 체인지 세트 보기.
- 테마: JetBrains 다크·라이트·시스템(에디터 스킨을 따름). UI 배율 조절(Ctrl+휠), IBM Plex Sans KR 글꼴, diff·blame 코드 글꼴 Jetendard.
- 우클릭 메뉴는 테마를 따르는 UI Toolkit 메뉴. 메뉴 모양을 git 식(Fork·GitKraken) 또는 Unity Version Control 식으로 고를 수 있다.
- 실시간 변경 감지와 주기적 자동 페치로 새로고침 없이 상태를 반영한다.
- 처음 읽는 동안 사이드바와 목록 칸에 같은 모양의 스켈레톤(위에서 아래로 흐르는 물결)을 보이고, 모두 읽으면 한꺼번에 걷는다. 빨리 읽히면 보이지 않는다.
- 툴팁은 Unity 기본 툴팁 대신 테마·UI 배율을 따르는 말풍선으로 뜬다(Git 창·보조 창).

### 커밋 그래프
- 커밋 원에 작성자 아바타(GitHub 프로필 사진, 없으면 머리글자). Claude·OpenAI·Gemini 공동 작성자는 로고로.
- 브랜치/태그 이름표: 현재 브랜치 체크 표시, 로컬(노트북)·원격(구름) 아이콘, 같은 커밋을 가리키는 로컬·원격은 이름표 하나로 합침, "+n" 에 마우스를 올리면 참조 목록을 펼침.
- 칸(브랜치/태그·그래프·메시지) 순서 바꾸기와 폭 조절. 좁힐 때 이름은 글자 단위로 말줄임되고 이름표는 끊김 없이 줄어 가장 좁을 때 아이콘 하나가 칸 가운데에 온다. 그래프 칸은 가지 간격이 이어서 줄고 영향받는 선부터 흐려진다.
- 창이 좁아지면 앞 칸을 줄여 맨 오른쪽 칸이 밀려나지 않는다. 칸·패널 경계를 끄는 동안에는 참조 목록이 열리지 않는다.
- 커밋 우클릭 메뉴: 체크아웃, 브랜치·태그 만들기, cherry-pick, revert, reset, rebase, 병합 등.

### 스테이징·커밋
- 파일·폴더 단위 Stage·Unstage·버리기, 에셋과 .meta 를 한 줄로 묶어 보기(기본).
- diff 뷰어에서 줄·hunk 단위 Stage·Unstage·버리기(GitKraken 식 부분 스테이징).
- 커밋 제목·설명, git 커밋 템플릿(commit.template) 안내문, 마지막 커밋 고치기(Amend), 커밋 후 푸시.
- 충돌로 멈춘 rebase·병합·cherry-pick·revert 를 Git 창에서 풀고 이어가기·취소.

### diff·파일
- 코드 diff(Split·Inline·Hunk 보기, 공백 무시, 줄 바꿈), 글 끌어 고르고 복사, "이 파일 편집" 으로 그 자리에서 고치고 저장.
- 파일 히스토리와 Blame.
- 이미지 diff(스와이프·어니언 스킨·차이 표시·반전 차이·속성 보기). PSD·PSB 미리보기와 diff. Git LFS 원본 받기.

### GitHub
- Preferences > Git 에서 GitHub 로그인(Device Flow), 저장소마다 계정 고르기.
- Pull Request 목록·검색·필터, PR 만들기, PR 상세(대화·리뷰 작성·승인·변경 요청·Draft 전환·닫기/다시 열기).

### 저장소 관리
- 원격 추가, 워크트리, 서브모듈 추가·편집(원격 주소 바꾸기·저장소 열기·지우기)·동기화 표시.
- git init 때 Unity 용 .gitignore 를 함께 만든다.
- Project 창 상태 아이콘과 우클릭 Git 메뉴.
- 남은 git 잠금 파일 처리, 인증 실패 안내, 오류 팝업(쉬운 설명·git 원문).

### 설정
- 개인 설정은 Preferences > Git(자동 페치, rebase, .meta 묶기, 테마, 메뉴 모양, GitHub 계정).
- Version Control 모드를 Visible Meta Files 로 유지한다.

### 입력
- 한글 IME: 입력란 조합이 끊기지 않게, 맥 NFD 한글을 한 글자로 합쳐 표시.

## [0.1.0] - 2026-10-18

- 첫 공개. Project 창 Git 상태 표시, 변경 사항 창(검토·버리기·커밋), Project Settings > Git.

[1.0.0]: https://github.com/NK-Studio/com.nkstudio.gitvcs/releases/tag/1.0.0
[0.1.0]: https://github.com/NK-Studio/com.nkstudio.gitvcs/commit/e5b5232
