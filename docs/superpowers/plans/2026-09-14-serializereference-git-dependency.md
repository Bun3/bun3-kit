# SerializeReference 외부 Git 의존성 전환

사용자 승인: bun3 안의 외부 패키지 복사본을 없애고 공식 호환 버전 또는 별도 포크 Git URL로 관리한다.

- 공식 HEAD `5fadcb6564f0c6e988641e2e4a4aba4b6b1caafe`의 패키지는 1.7.0이며 `AdvancedTypePopup.GetItem`이 여전히 `children`을 호출한다.
- 원본 포크 `Bun3/Unity-SerializeReferenceExtensions`에서 기존 Unity 6000.5 `childList` 조건부 호환 수정만 관리한다. 원저작자·라이선스·어셈블리·GUID를 유지한다.
- 프로젝트 규칙에 따라 URL에는 `#핀`을 넣지 않고 UPM이 생성한 `packages-lock.json`에 해석한 커밋을 기록한다.
- 게임 설치 도구에서 외부 패키지를 bun3 로컬 경로 목록에서 분리한다. core 패키지는 버전 의존성을 선언하고 소비 프로젝트가 Git URL을 직접 설치한다.
- UPM API로 게임·bun3를 전환한 후 로컬 복사본을 작업 폴더 밖에 보관한다. 공식 테스트와 두 프로젝트 컴파일을 검증한다.

## 검증 및 출처

- 공개 원본 포크: https://github.com/Bun3/Unity-SerializeReferenceExtensions
- 호환 커밋: `16ef22984bba7b55b5168f1bf9a55e3f9fbbb049`. 소스 변경은 접근자 조건부 분기 한 곳이고, 추가 변경은 패키지 버전과 루트 `FORK.md`다. 원본 저장소에 PR이나 메시지는 보내지 않았다.
- Unity 6000.3.14f1, 6000.5.5f1에서 원본 EditMode 테스트 각각 25/25 통과. Unity 2021.3은 설치되어 있지 않아 최소 지원 버전 실행을 검증했다고 주장하지 않는다.
- 게임 Unity 6000.5.5f1에서 `PackageInfo.source == Git`, 해석 커밋 일치, 실제 `GetItem`의 자식 발견/미발견 결과를 검증하고 종료 0을 확인했다.
- 기존 복사본은 `E:/Temp/mackysoft-url-migration-20260914/vendored-backup`에 보관했다. 작업 저장소의 배포 패키지에 포함하지 않는다.
- core 0.5.2, window 0.4.3은 MackySoft 버전 의존성을 선언한다. 프로젝트에서 Git 소스를 먼저 설치하는 절차와 게임 설치 도구를 갱신했다.
- 로그와 원본 XML: `E:/Temp/mackysoft-url-migration-20260914/`의 `upstream-tests-63.xml`, `upstream-tests-65.xml`, `game-install.log`, `game-verify.log`.
- bun3 Unity 6000.3.14f1에서도 동일한 Git 소스·커밋과 드롭다운 검색 검증이 통과했고 종료 0을 확인했다(`bun3-install.log`, `bun3-verify.log`). 양쪽 manifest와 lock 파일이 같은 포크 URL/커밋을 가리키며 embedded 패키지 디렉터리는 없다.
- 임시 설치/검증 스크립트와 해당 Unity 생성 메타데이터를 제거했다. 별도 포크의 호환 커밋만 GitHub에 게시했고, 게임·bun3-kit 작업 트리 변경은 커밋하지 않았다.
