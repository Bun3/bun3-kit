# Unity Frame Debugger 덤프/분석 툴 설계

날짜: 2026-08-29
상태: 초안 (growninja 드로우콜 최적화 세션에서 검증된 프로토타입 기반)

## 목표

에디터 Frame Debugger의 캡처 내용을 **텍스트/JSON으로 덤프하고 자체 분석 리포트까지 뽑는**
Editor 전용 패키지. 사람과 AI 에이전트가 GUI 없이 드로우콜 구성·배칭 브레이크 원인을
읽을 수 있게 한다.

동기: Frame Debugger는 내부 API(비공개, 리플렉션 필수)뿐이라 공식/서드파티 CLI 툴링이
없다. unity-cli에도, Unity 공식 스킬 21종에도 해당 기능이 없음을 확인(2026-08-29).
AI 주도 최적화 루프(캡처 → 집계 → 수정 → 재캡처)에서 매 게임마다 다시 짜게 될 코드.

## 원본 프로토타입

growninja `Client2/Assets/Editor/FrameDebuggerDump.cs` (2022.3.62에서 동작 검증).
핵심 확인 사항:

- `FrameDebuggerUtility`는 에디터 어셈블리 모듈 분리 때문에 `typeof(Editor).Assembly`로
  못 찾는다 — 전체 로드 어셈블리에서 타입명으로 검색해야 함.
- 상세 데이터(`FrameDebuggerEventData`)는 **limit이 가리키는 이벤트만** 준비된다.
  전 이벤트 덤프는 `EditorApplication.update`에서 limit을 한 칸씩 옮기며 리플레이 완료를
  폴링(이벤트 인덱스 일치 검증 + 타임아웃 스킵)하는 비동기 순회가 필요.
- 게임이 실시간(서버 패킷 등)으로 진행 중이면 순회 도중 이벤트 수가 변한다 —
  드리프트 허용(집계 목적엔 무방) + 이벤트 수 축소 시 조기 종료로 대응.
- `ComponentInstanceID` → `EditorUtility.InstanceIDToObject`로 게임오브젝트 경로 복원.
- `GetBatchBreakCauseStrings()`로 브레이크 원인 인덱스 → 문자열 매핑.

## 배치 결정

- **신규 패키지 `com.bun3.unity.diagnostics`** (`unity/Packages/` 임베디드).
  기존 굵은 패키지(core/ui/window) 우선 관례가 있지만, 셋 다 `"unity": "6000.3"`이라
  2022.3 게임(현행 라이브 게임)에 UPM 설치가 불가하다. 이 툴은 구버전 게임의 최적화가
  주 용도이므로 **min `"unity": "2022.3"`을 독자적으로 유지하는 것이 신규 분리의 근거**.
- Editor 전용: `Editor/` + asmdef(`Bun3.Unity.Diagnostics.Editor`, Editor 플랫폼만).
  Runtime 폴더 없음.
- 네임스페이스: `Bun3.Unity.Diagnostics`.
- 의존성 없음(UniTask 금지 — 2022.3 게임에 의존성 없이 꽂히는 게 가치). 비동기 순회는
  `EditorApplication.update`로 충분.
- 게임 레포 소비: git URL
  (`https://github.com/Bun3/bun3-kit.git?path=unity/Packages/com.bun3.unity.diagnostics`).

## 기능

### 1. 캡처 덤프 (프로토타입 승계)

- 메뉴 `Tools/Bun3/Frame Debugger Dump` + 프로그래밍 API(`FrameDebuggerDumper.DumpAsync`).
- 전제: 플레이 중 Pause + Frame Debugger Enable (에디터 pause는 자동 처리).
- 이벤트당 한 줄: 인덱스, 이벤트 타입, 셰이더/패스, vtx/draw/instance 수,
  배칭 브레이크 원인, 렌더타깃, 게임오브젝트 경로.
- 리플렉션 필드 접근은 전부 이름 기반 + 실패 허용(버전 간 필드 변화 흡수).

### 2. 분석 리포트 (신규 — 이번 세션에서 수동으로 한 분석의 자동화)

덤프와 동시에 요약 섹션 생성:

- 셰이더별 / 브레이크 원인별 / (셰이더×원인)별 콜 수 집계, 내림차순.
- **인터리브 감지**: 서로 다른 셰이더·머티리얼이 A-B-A-B로 교차하는 구간을 찾아
  구간 범위·교차 쌍·낭비 콜 수를 보고 (예: 그림자 스프라이트 ↔ Spine 교차).
- 연속 동일 셰이더 구간(run-length) 상위 목록 — 배칭 잘 되는 곳/깨지는 곳 대비.
- 단일 쿼드(vtx=4) 드로우 카운트 — UI 아틀라스 미적용 신호.
- 게임오브젝트 경로 프리픽스별 콜 수 상위 목록.

### 3. 출력

- `.md`(사람/AI 겸용 리포트) + `.json`(구조화 소비용) 동시 출력, 프로젝트 루트
  `FrameDebuggerDump/` 하위에 타임스탬프 파일명.
- 콘솔에 요약 3줄(총 콜 수, 최다 브레이커, 리포트 경로).

### 4. unity-cli 연동 (Unity 6 전용, 조건부 컴파일)

- `com.unity.pipeline` 존재 시(asmdef version define) `[CliCommand("framedebugger_dump")]`
  노출 → `unity command framedebugger_dump`로 AI가 터미널에서 캡처~리포트까지 자동 실행.
- 2022.3에서는 심볼 미정의로 자연 제외. 메뉴/API 경로는 양쪽 공통.

## 버전 호환

| 대상 | 근거 |
|---|---|
| 2022.3 LTS | growninja에서 동작 검증 완료(프로토타입) |
| 6000.x | bun3-kit unity 프로젝트에서 검증 필요 — 내부 타입/필드명 변화 가능성. 리플렉션 이름 기반 + 필드 누락 허용으로 흡수, 안 되면 버전 분기 |

2021 이하는 비지원(스펙 아웃).

## 테스트

- EditMode 테스트: 리플렉션 바인딩이 현재 에디터 버전에서 전부 해결되는지
  (타입/메서드/프로퍼티 존재 검증) — 에디터 업그레이드 시 침묵 파손 방지가 목적.
- 분석 로직(집계·인터리브 감지)은 덤프 라인 파싱과 분리해 순수 함수로 두고 유닛 테스트.
- 실 캡처 E2E는 수동(플레이 모드 + 실제 씬 필요).

## 미결

- 인터리브 감지의 최소 구간 길이/보고 임계값 기본치 (초안: 교차 4회 이상).
- JSON 스키마 확정 (초안: 이벤트 배열 + 집계 오브젝트).
- growninja 쪽 기존 `FrameDebuggerDump.cs`는 패키지 도입 시 제거.
