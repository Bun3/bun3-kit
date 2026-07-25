# bun3 모노레포 구조 및 네이밍 규약 설계

- 날짜: 2026-07-26
- 상태: 승인 대기
- 범위: 레포 구조 전환, 공용 코드 전략, 네임스페이스/네이밍 규약, 마이그레이션 개요

## 1. 배경과 목표

현재 `Bun3/unity` 레포는 Unity 프로젝트 전체이며, 자작 패키지 `com.bun3.core`,
`com.bun3.ui`를 임베디드 패키지로 포함한다. 목표는 이를 확장해 다음을 하나의
프레임워크 생태계로 만드는 것이다.

- **dotnet**: 서버·Unity가 함께 쓰는 공용 .NET 라이브러리
- **server**: 세션·랭킹·채팅·tick 루프 등 서버 재사용 기능의 모듈 라이브러리
- **unity**: 기존 Unity 패키지 개발 프로젝트
- **실제 게임/서버 프로젝트(모노레포 외부)**: 위 산출물을 NuGet/UPM으로 소비.
  서버 앱은 별도 template 레포에서 생성(6장)

## 2. 결정 사항 요약

| 항목 | 결정 |
|---|---|
| 레포 형태 | 단일 모노레포, git은 최상위 하나만 (A안) |
| 전환 방법 | 기존 `Bun3/unity` 레포를 승격: 전체를 `unity/` 하위로 `git mv`, 레포명 변경 |
| 공용 코드 위치 | `dotnet/src/com.bun3.common` — UPM 패키지이자 NuGet 소스인 이중 포장 |
| 공용 코드 이름 | `Bun3.Common` (Dotnet/Shared 대신 역할 기술형 이름 채택) |
| 외부 Unity 소비 | UPM git URL + `?path=` (소스 배포, NuGet dll 아님) |
| 외부 서버 소비 | NuGet 패키지 참조 (`dotnet pack` → GitHub Packages 또는 nuget.org) |
| 서버 재사용 모델 | fork 아님. 기능은 `Bun3.Server.*` 모듈 라이브러리로, 프로젝트 시작점은 별도 `bun3-server-template` 레포(GitHub Template) |
| 기존 패키지 | 풀 리네임: `com.bun3.unity.core` / `com.bun3.unity.ui` |
| 공개 범위 | 전체 비공개로 시작. 필요 시 공개 전환 또는 subtree split은 추후 |

## 3. 최종 레포 구조

```
bun3-kit/                        ← git 루트 (이름은 GitHub 리네임 시 확정)
├── README.md                          ← 워크스페이스 개요
├── Bun3.sln                           ← dotnet/ + server/ 프로젝트를 묶는 솔루션 (unity 제외)
├── dotnet/
│   └── src/
│       └── com.bun3.common/           ← 공용 코드 원본 (단일 진실원본)
│           ├── package.json           ← UPM 패키지 정의
│           ├── Bun3.Common.asmdef
│           ├── Bun3.Common.csproj     ← netstandard2.1, NuGet 팩용
│           └── Runtime/…​/*.cs
├── server/
│   └── src/
│       ├── Bun3.Server.Core/          ← 호스팅 부트스트랩, 기본 모델 구조, 공통 파이프라인
│       └── Bun3.Server.<Module>/      ← Ticking, Sessions, Ranking, Chat 등 (필요 시 추가)
└── unity/                             ← 기존 unity 레포 내용 전체
    ├── Assets/
    ├── Packages/
    │   ├── manifest.json              ← "com.bun3.common": "file:../../dotnet/src/com.bun3.common"
    │   ├── com.bun3.unity.core/       ← 구 com.bun3.core
    │   └── com.bun3.unity.ui/         ← 구 com.bun3.ui
    ├── ProjectSettings/
    ├── unity.sln                      ← Unity가 자동 생성/재생성 (수동 솔루션에 편입하지 않음)
    └── .gitignore                     ← 기존 파일 그대로 (rooted 패턴은 이 폴더 기준으로 동작)
```

솔루션은 두 개다: Unity가 생성·관리하는 `unity/unity.sln`과, 우리가 관리하는
루트 `Bun3.sln`(dotnet + server). Unity 생성 csproj는 재생성 대상이므로 루트
솔루션에 포함하지 않는다. 루트 `Bun3.sln`은 자신이 묶는 두 폴더(dotnet/, server/)의
공통 조상에 위치한다.

미래 확장(비범위, 예약만): `web/`(TS 등 타 언어), `schema/`(proto/OpenAPI 등
언어 중립 계약 + 코드젠). 타 언어는 형제 폴더로 추가하며 레포를 분리하지 않는다.

모노레포 외부에는 다음 레포들이 존재한다(6장 참조):

```
bun3-server-template      ← GitHub Template 레포. 서버 모듈 조립 뼈대만 (얇게 유지)
<각 프로젝트 서버 레포>     ← template에서 생성. 도메인 코드를 채우고,
                             베이스 개선은 NuGet 버전 업데이트로 수신
```

## 4. Git 전략

- git은 **최상위 하나만** 둔다. 중첩 git(gitlink)과 서브모듈은 사용하지 않는다.
- 기존 레포를 그대로 승격하므로 **히스토리가 완전 보존**된다. subtree merge,
  filter-repo 불필요.
- GitHub 레포 리네임 시 구 URL은 리다이렉트되지만, UPM `?path=` 소비처가 있다면
  `unity/` 프리픽스를 반영해 갱신해야 한다.
- 대용량 에셋이 늘어나면 Git LFS 도입을 검토한다(현 시점 비범위).

## 5. 공용 코드 전략 (com.bun3.common)

한 폴더가 UPM 패키지(package.json + asmdef)이면서 동시에 NuGet 소스(csproj)가
되는 이중 포장. 소스는 이 한 곳에만 존재한다.

### 소비 매트릭스

| 소비자 | 방법 | 반영 시점 |
|---|---|---|
| 모노레포 내 `server/` | `<ProjectReference>` | 즉시 |
| 모노레포 내 `unity/` | `file:../../dotnet/src/com.bun3.common` 로컬 패키지 (mutable) | 즉시 |
| 외부 서버/닷넷 프로젝트 | NuGet 참조 (`Bun3.Common` 패키지) | 릴리스 시 |
| 외부 Unity 프로젝트 | `https://github.com/Bun3/<repo>.git?path=dotnet/src/com.bun3.common#<tag>` | 릴리스 시 |

### 규칙

1. `Bun3.Common`에는 **UnityEngine 의존 코드 금지**. netstandard2.1 호환만 허용.
2. 외부 NuGet 의존성은 Unity에도 공급 가능한 것만 허용하며, 없는 것을 원칙으로 한다.
3. Unity가 생성하는 `.meta` 파일은 커밋한다. csproj는 `.cs`만 컴파일하므로 충돌 없음.
4. Unity 의존 코드는 `com.bun3.unity.core`가 `com.bun3.common`을 참조하는 방향으로만.
   역방향 참조 금지.
5. csproj는 `Directory.Build.props` 등으로 `.meta`, `package.json` 등 비-C# 파일이
   NuGet 패키지에 포함되지 않도록 관리한다.
6. 공통 MSBuild 설정(`Directory.Build.props`)은 **레포 루트에 두지 않는다.**
   MSBuild가 상위 폴더로 탐색하므로 루트에 두면 Unity 생성 csproj까지 영향을 받는다.
   `dotnet/`, `server/` 폴더 단위로 배치한다.

## 6. 서버 재사용 전략 (모듈 라이브러리 + template 레포)

서버는 프로젝트 간 재사용률이 높지만, 재사용은 fork(소스 복제)가 아니라
**모듈 라이브러리(NuGet 참조)**로 달성한다. fork 모델은 도메인 코드와 베이스
코드가 같은 파일에 섞이면서 프로젝트 수만큼 merge 충돌이 누적되어 각 fork가
발산한다. 라이브러리 모델에서는 베이스 개선이 패키지 버전 업데이트로 충돌 없이
모든 프로젝트에 전파된다.

### 구성 요소

- **모듈 라이브러리 (모노레포 `server/src/`)**: 프로젝트마다 폼이 같은 기능을
  ASP.NET Core 확장 패턴(`services.AddBun3X(...)`)으로 제공한다.
  - `Bun3.Server.Core`: 호스팅 부트스트랩, 기본 모델 구조, 공통 파이프라인
  - 이후 필요에 따라: `Bun3.Server.Ticking`(고정 타임스텝 tick 루프),
    `Bun3.Server.Sessions`, `Bun3.Server.Ranking`, `Bun3.Server.Chat` 등
- **template 레포 (`bun3-server-template`, 모노레포 외부 별도 소형 레포)**:
  GitHub Template Repository("Use this template")로 지정한 얇은 조립 뼈대.
  모듈들을 조립하는 Program.cs + 빈 `Domain/` 폴더 + appsettings + Dockerfile
  수준만 포함한다. fork가 아니므로 히스토리 연결 없는 독립 레포가 생성된다.
- **실제 프로젝트 레포**: template에서 생성 → 도메인 코드를 채움 → 베이스
  개선은 `Bun3.Server.*` / `Bun3.Common` NuGet 버전 업데이트로 수신.

### 설계 원칙

1. 프로젝트별 차이는 라이브러리 설계로 흡수한다: 도메인 타입은 제네릭
   (`AddBun3Ranking<TScore>`), 동작 차이는 옵션/인터페이스 주입으로.
2. 상시 루프(tick)도 같은 분해를 따른다: 루프 메커니즘(고정 타임스텝,
   accumulator, graceful shutdown)은 `Bun3.Server.Ticking`이 BackgroundService로
   소유하고, tick 내부의 호출 계층(Server.Tick → Player.Tick → …)은 프로젝트의
   `ITickable` 구현이 소유한다. HTTP 엔드포인트 모듈과 tick 루프는 같은 호스트에
   공존 가능하다.
3. 탈출구: 특정 프로젝트가 모듈 내부를 깊게 수정해야 하면 **그 모듈 하나만**
   소스를 프로젝트로 복사한다. 발산 범위를 모듈 단위로 격리한다.
4. template은 얇게 유지한다. template에 로직이 쌓이기 시작하면 라이브러리로
   내려야 한다는 신호다.

## 7. 네임스페이스 / 네이밍 규약

### 원칙: `Bun3.<플랫폼>.<모듈>[.<기능>]` + 이름 정렬

**네임스페이스 = asmdef 이름 = 어셈블리 이름 = UPM 패키지명(com. 제거, PascalCase)**
이 넷을 항상 일치시킨다.

| 계층 | 값 | 의미 |
|---|---|---|
| 루트 | `Bun3` | 소유자 |
| 플랫폼 | `Common` / `Unity` / `Server` | 실행 환경. Common은 전 플랫폼 공유 |
| 모듈 | `Core`, `UI`, … | 패키지/라이브러리 단위 |
| 기능 | `Threading`, `Attributes`, … | 폴더 = 네임스페이스 매핑 |

### 접미 규칙

- Editor 어셈블리: `<루트 네임스페이스>.Editor` (예: `Bun3.Unity.Core.Editor`)
- 테스트: `<루트 네임스페이스>.Tests`
- 샘플: `<루트 네임스페이스>.Samples.<샘플명>`

### 리네임 매핑

| 현재 | 변경 후 |
|---|---|
| `com.bun3.core` | `com.bun3.unity.core` |
| `com.bun3.ui` | `com.bun3.unity.ui` |
| `Bun3.Core.*` | `Bun3.Unity.Core.*` |
| `Bun3.UI.*` | `Bun3.Unity.UI.*` |
| `Bun3.Core.Editor.Editor` (오기) | `Bun3.Unity.Core.Editor` |
| `Unity9a6b65d5b34d8ea74ae5.Unitycore` (자동 생성 쓰레기) | 삭제 후 규약 네임스페이스로 정리 |
| asmdef `bun3.core` 등 소문자 이름 | `Bun3.Unity.Core` 형태로 통일 |

### 리네임 주의사항

- 어셈블리/네임스페이스 이동은 직렬화 데이터의 타입 참조를 깨뜨릴 수 있다.
  특히 `[SerializeReference]`(SerializeReferenceExtensions 사용 중)는 어셈블리
  한정 타입명을 저장하므로, 이동 대상 타입에는
  `[UnityEngine.Scripting.APIUpdating.MovedFrom]` 어트리뷰트를 부여해 마이그레이션한다.
- `.meta` GUID는 파일 이동만으로는 변하지 않으므로 에셋 참조는 유지된다.
  meta 파일을 삭제·재생성하지 않도록 폴더/파일 이동은 반드시 Unity 에디터 또는
  meta 동반 이동으로 수행한다.
- `com.bun3.ui`의 `dependencies`가 `com.bun3.core: 0.3.0`을 지정하므로 패키지명
  변경 시 함께 갱신한다.

## 8. 버전 및 릴리스

- 패키지별 SemVer 독립 버전. 태그는 `com.bun3.common/v0.1.0` 형식(패키지 경로
  프리픽스). 서버 모듈도 동일: `Bun3.Server.Core/v0.1.0`.
- `com.bun3.common`은 `package.json`의 `version`과 csproj `<Version>` 두 곳을
  릴리스 시 동일하게 맞춘다(자동화는 필요해지면 도입).
- NuGet publish 대상(GitHub Packages vs nuget.org)은 첫 릴리스 시점에 결정한다.

## 9. 마이그레이션 단계 (개요 — 상세는 구현 계획에서)

1. **레포 승격**: 루트에서 전체를 `unity/`로 `git mv` → 커밋 → GitHub 레포 리네임 →
   루트 `README.md` 추가. Unity 프로젝트는 새 경로에서 열어 정상 동작 확인.
2. **dotnet/ 신설**: `com.bun3.common` 뼈대(package.json, asmdef, csproj) + 루트 `Bun3.sln`.
   unity `manifest.json`에 `file:` 참조 추가, Unity에서 패키지 인식 확인.
3. **풀 리네임**: 패키지 폴더/package.json/asmdef/네임스페이스 일괄 변경,
   `MovedFrom` 부여, 쓰레기 네임스페이스 제거. 컴파일 및 기존 씬/에셋 참조 확인.
4. **공용 코드 이주**: 기존 `Bun3.Core.Utils`, `Bun3.Core.Threading` 등에서
   UnityEngine 비의존 코드를 선별해 `Bun3.Common`으로 이동(개별 검토 필요 —
   Threading은 UniTask 의존이면 Unity 측 잔류).
5. **server/ 뼈대**: `Bun3.Server.Core` 최소 프로젝트 생성 +
   `ProjectReference`(→ Bun3.Common) 연결 확인.
6. 각 단계는 독립 커밋으로 분리하고, 단계마다 Unity 컴파일 + 기존 테스트 통과를
   완료 조건으로 한다.

## 10. 비범위 (이번 작업에서 하지 않음)

- CI/CD 파이프라인, NuGet publish 자동화
- `schema/`, `web/` 등 타 언어 폴더 실체화
- Git LFS 도입
- 기존 코드의 기능 변경·리팩터링 (이동과 리네임만 수행)
- 서버 기능 모듈(Ticking/Sessions/Ranking/Chat)의 실제 구현
- `bun3-server-template` 레포 생성 (서버 모듈이 최소 1개 릴리스된 후 진행)
