# Bun3.Server v1 하드닝 — DEFER 항목 정리

2026-08-17. 과거 리뷰에서 DEFER된 서버 하드닝 항목을 현재 코드와 대조하고, 유효한 것만 수정한다.
범위: Bun3.Server.Core / Transport.Tcp / Hosting + 테스트 갭. Gameplay 계열 프로젝트는 불가침.

## 항목별 대조 결과와 방침

| 항목 | 현재 코드 대조 | 판정 | 방침 |
|---|---|---|---|
| ServerBase `_running` 미참조 | `IsRunning`으로 읽기는 하나 `HandleConnected`가 검사하지 않음 — 정지 후 유입 연결이 세션으로 등록됨 | 유효(부분) | `HandleConnected` 진입 시 `!_running`이면 즉시 `connection.Close()`. `StartAsync`는 transport 기동 **전에** `_running=true`(기동 직후 유입 경합 방지, 실패 시 롤백) |
| ServerBase 중복 connection id 덮어쓰기 | `_sessions[id] = entry` 인덱서 대입 — 기존 세션 엔트리가 조용히 유실 | 유효 | `TryAdd` + 실패 시 에러 로그 + 신규 연결 Close. **파생 수정**: 중복 연결을 닫으면 그 OnClosed/OnPacket이 같은 id로 오므로, `HandleClosed`/`HandlePacket`에 connection 참조 동일성 검사 추가(원래 세션 오폭 방지). 제거는 `ICollection<KVP>.Remove`로 원자화(netstandard2.1에는 `TryRemove(KVP)` 없음) |
| ServerBase 미지 id 프레임 무음 드랍 | `HandlePacket`에서 TryGetValue 실패 시 무음 리턴 | 유효 | Debug 로그 추가(정지/킥 경합 시 유입될 수 있어 Warning은 소음) |
| ServerBase StopAsync 드레인 미관찰/타이머 미취소 | `Task.Delay(timeout, ct)`가 드레인 완료 후에도 타이머 유지, drain 태스크 미관찰 | 유효 | linked CTS로 드레인 완료 시 타이머 취소 + `await drain`으로 관찰(Completion은 TrySetResult 전용이라 폴트하지 않지만 규율상) |
| Tcp `_boundPort` 비원자 읽기 | `int?` 필드 — 토런 리드 가능 | 유효 | `int` 필드(-1 센티널) + `Volatile.Read/Write`, 프로퍼티에서 null 변환 |
| Tcp 수신 루프 태스크 미관찰 | `_ = Task.Run(...)` — finally의 `OnClosed`가 던지면 무관찰 폴트 | 유효 | `ContinueWith(OnlyOnFaulted)` 에러 로그(연결당 1회 할당 — 핫패스 아님) |
| Tcp StopAsync vs accept 백오프 100ms 경합 | 백오프 `Task.Delay(100)`이 취소 불가 — Stop이 최대 100ms 지연 + 정지된 리스너 재-accept의 throw에 의존 | 유효 | `_stopCts` 추가, 백오프를 `Task.Delay(100, token)`으로, 취소 시 루프 탈출. Start/Stop 단일 사용 제약은 문서화된 대로 유지 |
| Hosting AddServer 중복 등록 | 세션 타입 2개 등록 시 리스너 싱글턴 공유 → 기동 시 "Listener is already started." 크래시(등록 시점엔 무증상) | 유효 | `AddServerTransport`에서 리스너 디스크립터 존재 검사 → **등록 시점에** 명확한 메시지로 throw. AddServer/AddRpcServer/AddPlayerServer 중복·혼용 전부 커버. 관련 XML remarks 갱신 |

주: 같은 세션 타입의 AddServer 이중 호출은 현재 우연히 동작(호스티드 서비스는 TryAddEnumerable 닫힌 제네릭 dedupe, 싱글턴은 마지막 디스크립터 승리)하지만 configure 람다가 중복 적용되는 등 의도가 불명확 — 등록 시점 throw로 통일한다.

## 테스트 갭 (전부 신규)

| 갭 | 테스트 |
|---|---|
| 종료 전 큐 프레임 미처리 | SessionActorTests: 첫 패킷 블록 → 추가 큐잉 → Close → 잔여 미처리 확인 |
| OnHandlerError 예외 경로 | SessionActorTests: 훅 자체가 throw → 세션 종료 |
| 이중 OnClosed | SessionActorTests: RaiseClosed 2회 → OnDisconnected 1회, 무해 |
| 정지 후 유입 연결 킥 | SessionActorTests: StopAsync 후 Connect → 즉시 Close |
| 중복 id 거부 | SessionActorTests: 같은 id Connect 2회 → 신규만 Close, 기존 세션 유지 |
| StopAsync ct 전파 | SessionActorTests: 드레인 불가 세션 + ct 취소 → 즉시 반환 |
| 원격 리셋 non-null 에러 | TcpTransportTests: linger 0 close(RST) → OnClosed error non-null |
| SendAsync ct 전파 | TcpTransportTests: 선취소 ct → OCE throw, 연결은 열린 채 유지 |
| AddServer 중복 등록 | HostingTests: 2회 등록 → 등록 시점 InvalidOperationException |

## 진행

Core → Tcp → Hosting → 테스트 순으로 수정, 전체 서버 테스트 그린 확인 후 커밋(퍼블리시·머지 없음).
