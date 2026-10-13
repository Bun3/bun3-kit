# 플레이어 작업과 영속 저장

플레이어별 DB version 없이 서버와 Admin 사이에 수정 권한을 인계하려면 [수정 권한 인계와 Admin 명령 사용법](player-write-authority.md)을 참고한다. 아래 `PlayerStore<TPlayer>` 방식의 버전 검사는 계속 지원하며, 새 `OwnedPlayerStore<TPlayer>`와 공통 저장 실행기를 공유한다.

`PlayerRegistry<TPlayer>`의 저장 어댑터 경로는 로그인, 주기 저장, 연결 종료, 유예 만료, 다인 작업을 같은 `PlayerStore<TPlayer>`에 연결한다. 서버는 하나의 권위 있는 프로세스와 하나의 관계형 DB를 전제로 한다. 게임은 플레이어 키 해석과 권한 검사, 스키마, SQL, 상태 직렬화 및 결과 형식을 구현한다. 프레임워크는 참여자 키를 ordinal 순서로 잠그고, 로드와 적용, 모든 쓰기 및 작업 영수증을 하나의 DB 트랜잭션으로 묶는다.

## 등록과 어댑터

호스팅에서는 다음처럼 저장 어댑터 팩토리를 전달한다. 이 경로의 registry와 ticker는 동일 인스턴스이며, 어댑터는 registry 생성 시 한 번 만든다. 전체 컴파일되는 SQLite 예제는 [SqlitePlayerStore](../server/tests/Bun3.Server.Tests/Helpers/SqlitePlayerStore.cs), DI 등록 검증은 [PlayerOperationHostingTests](../server/tests/Bun3.Server.Tests/PlayerOperationHostingTests.cs)에 있다.

```csharp
builder.Services.AddPlayerServer<GameSession, GamePlayer, GameRequest, GameResponse, GameUpdate>(
    store: sp => new GamePlayerStore(sp.GetRequiredService<DbDataSource>()),
    configure: players =>
    {
        players.OnPlayerOperation<GamePlayer, TransferRequest, TransferResponse>(
            (actorKey, request) => new TransferOperation(actorKey, request));
    });
```

`GamePlayerStore : PlayerStore<GamePlayer>`는 `CreateConnection`, `LockVersionAsync`, `LoadPlayerAsync`, `CaptureChanges`, `WritePlayerAsync`, `ReadOperationAsync`, `WriteOperationAsync`, `WriteAuditAsync`를 구현한다. `LockVersionAsync`는 모든 참가자의 anchor 행을 transaction이 끝날 때까지 보호하고 현재 영속 버전을 반환한다. `WritePlayerAsync`는 `WHERE key = ... AND version = expectedVersion` 조건으로 상태와 버전을 함께 갱신하고 정확히 한 행을 갱신했을 때만 true를 반환한다. 처음부터 존재하지 않는 계정의 생성과 anchor 행 준비는 게임의 계정 생성 절차에서 처리한다. 콜백은 전달받은 `DbConnection`과 `DbTransaction`을 그대로 사용하고 내부에서 별도 commit이나 연결을 만들지 않는다.

예제 스키마의 `players(key TEXT PRIMARY KEY, version INTEGER, gold INTEGER)`는 실제 게임의 정규화된 계정·인벤토리·업적 테이블로 바꿀 수 있다. 상태의 정규화 열과 JSON/protobuf blob 선택, blob schema version과 이행은 게임 책임이다. `operations`에는 유일한 `id`, `kind`, `actor`, 정렬된 `participants` blob, 32 byte `fingerprint`, `schema_version`, 직렬화된 `result`를 저장한다. `audits`에는 `player_key`, `operation_id`, `event_name`, UTC 발생 시각, 소유된 payload blob을 저장한다. 영수증의 ID만 비교해서는 안 되며, 재요청 시 종류·발신자·참여자·fingerprint도 모두 일치해야 한다.

## 이체와 오프라인 PvP

이체 요청은 인증된 발신자 키와 요청으로 `PlayerOperation<TPlayer,TResult>`를 만든다. 생성 시 작업 ID, 종류, 발신자, 참여 키, 32 byte 입력 fingerprint, 결과 codec을 고정한다. `Validate(context)`에서 잔액, 소유권, 대상 및 PvP 규칙을 검사하고 게임 거절 코드를 반환한다. `Apply(context)`는 동기적으로 양측 상태를 바꾸고 `MarkDirty()` 및 필요 시 `RecordAudit()`를 호출한 뒤 결과를 반환한다. 온라인 A와 오프라인 B도 같은 코드 경로다. B는 잠금 아래 임시 로드되어 저장 후 release되며 로그인·tick hook을 호출하지 않는다. [실제 이체 구현과 SQLite E2E](../server/tests/Bun3.Server.Tests/PlayerOperationE2ETests.cs), [작업 클래스](../server/tests/Bun3.Server.Tests/Helpers/PlayerOperationHarness.cs)를 참고한다.

오프라인 PvP 정산도 참가자를 공격자·방어자로 선언하고 같은 `Validate`/`Apply` 규칙을 사용한다. [컴파일되는 PvP 정산 예제](../server/tests/Bun3.Server.Tests/PlayerOperationE2ETests.cs)는 승자 A와 오프라인 패자 B의 보상을 한 작업에서 계산해 SQLite의 공동 커밋으로 저장한다. 작업 중 외부 API 호출이나 async 게임 로직을 실행하지 않는다. 필요 콘텐츠는 진입 전 준비한다. 선택적인 메모리 `Transaction`은 Apply 안에서 도메인 반응까지 끝내고 닫는다. SQL 저장 동안 메모리 rollback scope를 유지하지 않는다. 기존 `ItemInventory`/`AchievementManager`의 단독 사용과 메모리 Transaction 선택 사용은 그대로 가능하다.

## 저장 실패, 재시도, 종료

`RecordAudit()`는 `PlayerStore`가 설정된 registry의 변경 실행 범위에서만 사용할 수 있다. 일반 요청·posted work·접속/종료 hook·작업 Apply에서 호출한 감사 데이터는 해당 상태와 같은 DB 트랜잭션으로 저장된다. loader와 기존 저장 hook만 사용하는 registry에서는 버퍼에 기록하거나 Dirty를 표시하기 전에 예외로 거절한다.

기존 저장 hook에서 어댑터로 이전할 때 `OnSaveAsync`와 저장용 `OnRetiredAsync`를 중복 실행하지 않는다. 어댑터 경로는 이 hook들을 호출하지 않고 저장 없는 `OnReleasedAsync(PlayerReleaseReason)`로 자원을 정리한다. 초기 전환 시 기존 계정의 영속 버전 anchor를 마련하고, 모든 쓰기 경로가 기대 버전을 조건으로 갱신하도록 이행한다. 구형 외부 writer가 무조건 덮어쓰면 공동 커밋과 복구 보장이 깨진다.

Validate 거절, 로드 실패, Apply 전 취소에서는 기존 캐시의 dirty 상태를 보존한다. Apply 이후 쓰기 실패가 rollback 확인으로 끝나면 모든 영향받은 캐시 객체를 무효화하고 임시 객체를 정리한다. 메모리 변경을 같은 객체에서 재저장하지 않는다. 다음 로그인이나 작업은 잠금과 DB anchor를 거쳐 실제 확정 상태를 새로 로드한다. commit ACK가 불명확하면 결과는 `Unknown`이며 참여자 메모리를 폐기한다. anchor에서 이전 writer 종료를 확인하기 전에는 새 로드/재시도를 통과시키지 않는다. 확정된 영수증이 있으면 같은 ID와 동일한 입력에 저장된 결과를 반환하고 Apply를 다시 실행하지 않는다. 영수증이 없다면 새 실행이 가능하다. commit 성공 뒤 알림 전송 실패는 DB 결과를 취소하지 않는다.

호스트 종료는 새 로그인과 작업 진입을 먼저 닫고 tick과 서버를 중지한 뒤, 캐시가 비어 있더라도 이미 입장한 임시 참여자 작업이 끝날 때까지 기다리고 유효한 캐시만 최종 저장한다. 미완료 DB 작업을 임의의 timeout으로 성공/rollback 판정하지 않는다. SQLite 테스트는 실제 원자적 rollback과 영수증 재생을 확인한다. PostgreSQL/MySQL의 행 잠금, 격리 및 네트워크 장애 보장은 각 운영 어댑터에서 별도로 검증해야 한다.
