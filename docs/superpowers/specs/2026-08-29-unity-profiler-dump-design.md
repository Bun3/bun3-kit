# Unity Profiler 덤프/분석 툴 설계

날짜: 2026-08-29
상태: 승인 (frame-debugger-dump 후속 — 타 프로젝트에서 실수요 발생)

## 목표

에디터 Profiler 프레임 버퍼를 **텍스트/JSON으로 덤프하고 자체 분석 리포트까지 뽑는** 기능.
`com.bun3.unity.diagnostics`에 추가한다(신규 패키지 아님). 사람과 AI 에이전트가 GUI 없이
CPU 마커 계층·GC 할당·프레임 스파이크를 읽을 수 있게 한다.

동기: unity-cli `get_performance_stats`는 집계 카운터 스냅샷뿐이라 "이 프레임 어디서
몇 ms 먹었나"를 못 본다. Profile Analyzer는 GUI 조작 필수. 프로파일러 캡처를
텍스트로 덤프하는 공식/서드파티 CLI 없음(2026-08-29 확인).

## Frame Debugger 덤프와의 차이 (설계를 단순화하는 두 가지)

- **공개 API만 사용** — `ProfilerDriver`(UnityEditorInternal, 접근 가능),
  `UnityEditor.Profiling.HierarchyFrameDataView`/`FrameDataView`. 리플렉션 없음,
  따라서 바인딩 테스트 불필요(컴파일이 곧 계약 검증).
- **동기 읽기** — 버퍼의 프레임 데이터는 즉시 읽힌다. 순회 상태 머신 불필요.
  유일한 비동기는 녹화 헬퍼(N프레임 후 자동 중지)뿐.

## 배치 결정

- 기존 패키지 `com.bun3.unity.diagnostics`에 추가, 버전 0.1.0 → 0.2.0.
- min Unity **2022.3 유지**. 사용하는 프로파일러 API 자체 하한은 2019.3+이지만,
  동거 중인 Frame Debugger 덤프가 2022.3 검증/2021 이하 비지원이므로 패키지 min이
  그쪽에 묶인다. (미래에 min을 내릴 일이 생기면 Frame Debugger 코드를
  `#if UNITY_2022_3_OR_NEWER`로 감싸는 선택지가 있다 — 현재는 하지 않음.)
- `Editor/` 평평하게, 네임스페이스 `Bun3.Unity.Diagnostics`, 의존성 추가 없음.

## 캡처 워크플로 — "버퍼를 분석한다"

버퍼를 어떻게 채웠는지는 툴이 관여하지 않는다(승인된 결정):

- Profiler 창에서 수동 녹화 → 덤프.
- `RecordAsync(frameCount)` / CLI `profiler_record`로 녹화 시작, N프레임 도달 시
  자동 중지(`EditorApplication.update`에서 `ProfilerDriver.lastFrameIndex` 감시).
- 디바이스/빌드 캡처 파일: `ProfilerDriver.LoadProfile(path)`로 버퍼에 로드 → 동일 분석.
  (`LoadProfile`은 기존 버퍼를 대체한다 — 리포트에 소스가 live인지 file인지 기록.)

풀 오케스트레이션(녹화~덤프 원커맨드)은 스코프 아웃 — 필요해지면 위에 얹는다.

## 구성 (frame-debugger 4분할 승계)

| 파일 | 역할 |
|---|---|
| `ProfilerCapture.cs` (internal) | `ProfilerDriver` 버퍼 → 순수 데이터 추출. 프레임별 `HierarchyFrameDataView`(메인 스레드)에서 마커 행(name, selfMs, calls, gcAllocBytes), `FrameDataView.frameTimeMs/frameGpuTimeMs`, 렌더 스레드("Render" 이름 매칭) 프레임당 총 ms |
| `ProfilerDumpAnalyzer.cs` (public, 순수) | 집계·스파이크 분석. 유닛 테스트 대상 |
| `ProfilerDumpReportWriter.cs` (internal) | md/json 렌더링 |
| `ProfilerDumper.cs` (public) | `Dump(budgetMs = 33.3)` 동기, `LoadAndDump(path)`, `RecordAsync(frameCount)`, 메뉴 `Tools/Bun3/Profiler Dump` |
| `Cli/` 기존 어셈블리에 추가 | `profiler_record --frames N`, `profiler_record_status`, `profiler_dump [--file path] [--budget-ms X]` |

데이터 모델(전부 `[Serializable]`, JsonUtility 소비):

- `ProfilerFrameStat` — frameIndex, cpuMs, gpuMs(가용 시, 아니면 0), renderThreadMs, gcAllocBytes.
- `ProfilerMarkerStat` — name, totalSelfMs, avgSelfMs, maxSelfMs, callCount, gcAllocBytes(집계는 마커 이름 기준, self-time 합산 — 트리 중복 계상 방지).
- 스파이크 항목 — frameIndex, cpuMs, 해당 프레임 top 10 마커(self-time).

## 분석 리포트

- **프레임 개요**: 프레임 수, cpu ms median/평균/p95/최악, gpu ms(가용 시), 프레임당 GC alloc 합/평균/최악.
- **스파이크**: cpu ms 최악 5프레임 + 예산 초과 프레임(기본 33.3ms, 인자 조정) — 각각 top 10 마커.
- **마커 집계**: 전 프레임 합산 self-time top 20 (총/평균/최대 ms, 호출 수).
- **GC**: 마커별 GC alloc 총합 top 20.
- **렌더 스레드**: 프레임당 렌더 스레드 총 ms(메인 대비) — 마커 트리는 메인 스레드만(v0).
- 메모리 상세(스냅샷·오브젝트 단위)는 Memory Profiler 영역 — 스코프 아웃.

## 출력

- `<프로젝트 루트>/ProfilerDump/ProfilerDump_<yyyyMMdd_HHmmss>.md` + `.json`
  실제 산출 형태: `{ timestamp, source(live|file:경로), frames[], analysis }` —
  마커 집계(topMarkersBySelfTime/topMarkersByGcAlloc)는 별도 `markers[]`가 아니라 `analysis` 내부에 있다.
- 콘솔 3줄: 프레임 수/최악 cpu ms, 최다 self-time 마커, 리포트 경로.
- 버퍼가 비었으면 실패 메시지(녹화 방법 안내 포함).

## 테스트

- 분석기(백분위·스파이크 선정·마커 집계)는 순수 함수 유닛 테스트.
- 캡처 리더는 공개 API — 컴파일이 계약 검증, 별도 바인딩 테스트 없음.
- 실 캡처 E2E는 수동(플레이 + 녹화 필요). `RecordAsync`의 자동 중지는
  에디터가 프레임을 생산해야 해서 자동화 제외.

## 미결

- p95 계산은 단순 정렬 후 인덱싱(프레임 수가 적으면 최악값과 동일해질 수 있음 — 허용).
- 렌더 스레드 이름 매칭이 플랫폼/버전별로 다를 가능성 — "Render Thread" 우선,
  실패 시 renderThreadMs=0으로 저하(실패 허용 원칙 승계).
