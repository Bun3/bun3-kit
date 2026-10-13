# Player Operations and Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 온라인·유예 캐시·오프라인 참여자의 변경을 같은 실행 규칙과 DB 트랜잭션으로 확정하고, 저장 장애 시 모든 관련 메모리를 안전하게 폐기한다.

**Architecture:** Players가 키별 실행 권한과 수명주기, 작업 실행기를 소유한다. 게임의 PlayerStore는 SQL 매핑만 제공하고 프레임워크가 DB transaction과 결과 분류를 소유한다. RPC는 요청별 진입을, Core는 posted work와 세션 실행 문맥을 확장해 기존 경로가 공통 권한을 우회하지 않게 한다.

**Tech Stack:** Core/Rpc/Players netstandard2.1 + C# 9, Hosting/tests net10.0, ADO.NET, 기존 Google.Protobuf, NUnit 4.1.0, 테스트 전용 Microsoft.Data.Sqlite 10.0.0.

**Spec:** [승인된 스펙](../specs/2026-10-06-player-operations-and-persistence-design.md).

상태: 여섯 작업 구현·작업별 리뷰 및 최종 전체 리뷰 지적 수정 완료. 서버 테스트 601개 통과, 전체 빌드 경고·오류 0. SDD로 실행했으며 마지막 수정은 작업자 실행 한도로 직접 수행·자체 재검토했다. 초기 Task 6의 구현 전 RED 증거 한계는 아래에 기록한다. 커밋·외부 게임 변경·배포는 하지 않았다.

## Global Constraints

- 첫 구현은 하나의 권위 있는 프로세스와 하나의 관계형 DB를 대상으로 한다.
- 기존 미커밋 변경은 보존한다. 외부 게임·운영 DB 변경, 패키지 배포, 커밋은 이 작업에 포함하지 않는다.
- 신규 공용 기능은 `server/src/Bun3.Server.Players/`에 둔다. DB 제공자 라이브러리는 추가하지 않고 BCL의 `DbConnection`/`DbTransaction`을 사용한다. 테스트 프로젝트의 제공자 참조만 허용한다.
- `ItemInventory`와 `AchievementManager`의 단독 사용 및 메모리 Transaction의 선택적 사용은 유지한다. Players에서 이 패키지들을 필수 참조하지 않는다.
- 첫 구현은 참여자 수 상한을 설정 가능하게 하고 기본값은 16으로 둔다.
- Core/Rpc/Players는 netstandard2.1, C# 9, 블록 namespace와 영어 XML 문서를 유지한다. Hosting의 기존 net10.0 타깃은 유지한다. 라이브러리 await에 `ConfigureAwait(false)`를 사용한다.
- 고빈도 단일 요청/tick에서 추가 문자열 포맷·키 배열·클로저를 만들지 않는다.
- 제품 변경 시 Core `0.7.0 → 0.8.0`, Rpc `0.6.0 → 0.7.0`, Players `0.5.0 → 0.6.0`, Hosting `0.6.0 → 0.7.0`으로 올린다. 별도 패키지를 추가하지 않는다.
- 새 파일의 폴더와 namespace를 일치시킨다. 타입 종류별 폴더를 추가하지 않는다. partial은 `Type.Role.cs`, 단독 generic 파일은 `Type{T}.cs` 형태로 둔다.
- 현재 linked worktree에서 실행한다. Task 1 전에 HEAD/status와 기존 tracked/untracked 파일의 기준 상태를 로컬 실행 기록으로 보존한다. HEAD diff만으로 이번 변경을 리뷰하지 않는다.

## Review Focus

1. 로그인 반환 직후 handler가 응답을 만들 때 다른 작업이 Player를 변경하는 경우: 로그인에서 확보한 권한을 현재 RPC 요청 종료까지 인계한다. Task 2 `Login_reply_reads_state_before_waiting_operation_runs`.
2. 여러 키 중 일부를 잡은 뒤 취소되거나 마지막 권한 참조가 제거되는 경우: 중복 gate·대기자 누락·권한 누수가 없어야 한다. Task 2 `Canceled_multi_acquire_releases_prefix_and_preserves_waiters`.
3. Commit은 성공했는데 connection Dispose 또는 update 전송이 실패하는 경우: 성공을 rollback/unknown으로 바꾸지 않는다. Task 3 `Dispose_error_preserves_committed_outcome`, Task 5 `Delivery_error_does_not_invalidate_committed_players`.
4. 같은 작업 ID를 다른 발신자/참여자/입력으로 재사용하는 경우: 과거 결과 노출·재실행 없이 충돌로 거절한다. Task 4 `Receipt_identity_mismatch_rejects_without_apply`.
5. 정상 종료와 무효화가 교차해 과거 Player의 저장용 hook을 호출하는 경우: 저장 없이 제거하며 새 Player를 제거하지 않는다. Task 6 `Shutdown_and_old_detach_cannot_save_or_remove_replacement`.

## 파일 배치와 의존 순서

아래 경로는 모두 repository root 기준이다. Task는 1 → 2 → 3 → 4 → 5 → 6 순서다. 동시에 구현자가 같은 공유 파일을 수정하지 않는다. 각 작업은 RED → 구현 → GREEN → 스펙/품질 리뷰로 마무리하고 커밋 대신 작업별 diff를 기록한다.

| 작업 | 제품 파일 | 테스트/문서 |
|---|---|---|
| 1 | Core/Session.cs, Rpc/RpcSession.cs·RpcRuntime.cs·RpcConfig.cs·RpcRequestHandler.cs | RpcExecutionTests.cs, 기존 SessionPostTests.cs·RpcGateTests.cs |
| 2 | Players/PlayerExecutionTable.cs·PlayerExecutionScope.cs·PlayerExecutionLane.cs, PlayerRegistry.cs·PlayerRegistry.Execution.cs·PlayerRegistry.Lifecycle.cs, PlayerSession.cs·Player.cs·PlayerTicker.cs·PlayerReleaseReason.cs·AssemblyInfo.cs, Core/DisconnectCode.cs | PlayerExecutionTests.cs, 기존 PlayersTests.cs·PlayerTickingTests.cs, Helpers/PlayerExecutionHarness.cs |
| 3 | Players/PlayerStore{TPlayer}.cs·PlayerStoreScope{TPlayer}.cs·PlayerStoreResult.cs·PlayerSaveData.cs·PlayerAuditRecord.cs·PlayerOperationRecord.cs, PlayerRegistry.Persistence.cs | PlayerStoreTests.cs, PlayerStoreSqliteTests.cs, Helpers/SqlitePlayerStore.cs·FaultingPlayerStore.cs |
| 4 | Players/PlayerOperation{TPlayer,TResult}.cs·PlayerOperationContext{TPlayer}.cs·PlayerOperationResult{TResult}.cs·IPlayerResultCodec{TResult}.cs·ProtobufPlayerResultCodec{TResult}.cs, PlayerRegistry.Operations.cs·PlayerRegistry.Invalidation.cs, PlayersOptions.cs, Rpc/RpcStatus.cs | PlayerOperationTests.cs, PlayerOperationRecoveryTests.cs, Helpers/PlayerOperationHarness.cs |
| 5 | Players/PlayersConfig.cs·PlayerSession.cs·PlayerRegistry.Notifications.cs·PlayerRegistry.Persistence.cs·Player.Diagnostics.cs, Rpc/RpcSession.cs·RpcRuntime.cs | PlayerOperationRpcTests.cs·PlayerOperationNotificationTests.cs·PlayerOperationAuditTests.cs, Protos/player_operations.proto |
| 6 | Hosting/PlayersServiceCollectionExtensions.cs, 관련 4 csproj, Core/Transaction.cs의 계약 문서 | PlayerOperationHostingTests.cs·PlayerOperationE2ETests.cs·PlayerExecutionAllocationTests.cs, docs/player-operations-and-persistence.md |

표의 Core/Rpc/Players/Hosting은 각각 `server/src/Bun3.Server.*`, 테스트는 `server/tests/Bun3.Server.Tests/`를 가리킨다. 설명 문서는 한국어, 제품 코드와 주석은 영어로 작성한다.

## 고정할 interface와 실행 결정

### A. Core/Rpc 확장 지점

Task 1에서 다음 시그니처를 제공한다. 아래 코드는 시그니처 목록이며 메서드 본문은 생략했다. Session의 기본 구현은 기존 동작을 유지한다.

```csharp
// Session: RunAsync 전체 수명에서 동기적으로 enter하고 finally에서 dispose.
protected virtual IDisposable? EnterExecutionScope();
protected virtual ValueTask OnPostedWorkAsync(Func<ValueTask> work);

// RpcRequestHandler.cs: 등록 때 한 번 만드는 delegate.
public delegate ValueTask<(int Status, IMessage? Response)> RpcRequestHandler(
    RpcSession session, IMessage request);

// RpcSession: Runtime이 registration을 직접 호출하지 않고 이 hook으로 호출.
protected internal virtual ValueTask<(int Status, IMessage? Response)> InvokeRequestAsync(
    IMessage request, RpcRequestHandler handler);
public virtual ValueTask SendUpdateAsync(IMessage update);
protected byte[] EncodeUpdate(IMessage update);
```

`RpcConfig.Registration.Invoke`는 `RpcRequestHandler`로 바꾸고 typed session cast를 등록 때 생성한 adapter 안에서 수행한다. `IRpcRuntime`에 `byte[] EncodeUpdate(IMessage update)`를 추가해 기존 envelope/oneof 검증을 재사용한다. 요청당 closure를 만들지 않는다. Control/Ping에는 요청 hook을 적용하지 않는다.

### B. 권한과 수명

`PlayerExecutionTable`은 internal이며 `AcquireAsync(string key, CancellationToken ct)`와 `AcquireAsync(IReadOnlyList<string> keys, CancellationToken ct)`가 `ValueTask<PlayerExecutionScope>`를 반환한다. 다인 입력은 정렬·중복 제거된 불변 키 목록이다. Scope는 IDisposable이며 중복 Dispose/과거 토큰으로 새 보유자의 권한을 해제하지 않는다. `RetainKey(string key)`/`ReleaseKey(string key)`로 캐시 Entry가 자신의 gate를 pin해 단일 요청마다 gate를 생성하지 않는다. 대기자/보유자/cache pin이 사라지기 전 table에서 gate를 제거하지 않는다. 복구 필요 키도 복구 완료까지 pin한다.

`PlayerExecutionLane`은 현재 소유 범위와 요청의 권한 인계를 기록하는 internal 문맥이다. Session.EnterExecutionScope에서 AsyncLocal holder를 세션 수명에 한 번 설치한다. 요청마다 AsyncLocal에 새 객체를 설정하지 않고 holder의 현재 범위를 enter/leave한다. background ExecuteAsync에는 독립 holder를 설치한다. AsyncLocal을 async Acquire 메서드 안에서 설정하면 호출자에게 전달된다고 가정하지 않는다. fire-and-forget로 문맥/Player를 유출하는 것은 지원하지 않는다.

`PlayerSession`이 InvokeRequestAsync/OnPostedWorkAsync를 sealed override한다. 일반 요청은 한 키를 얻고 바인딩·세대를 재검사한 뒤 handler를 실행한다. 로그인 요청은 SignIn이 확보한 Scope를 현재 요청 frame에 넘겨서 응답 객체 생성까지 보호한다. 요청 밖 SignIn은 반환 전에 권한을 해제한다. 로그인 성공 후 handler가 별도 상태 변경을 하는 사용은 금지한다. 정상 요청에서 ExecuteAsync로 권한을 확장하거나 hook에서 SignIn/RetireAll에 재진입하면 기다리지 않고 InvalidOperationException을 던진다.

`Player`에 `protected internal virtual ValueTask OnReleasedAsync(PlayerReleaseReason reason)`을 추가한다. enum은 `Retired`, `TemporaryCompleted`, `TemporaryAborted`, `Invalidated`다. 저장하지 않는 자원 정리 전용이다. store 경로에서는 정상 저장 후 이 hook만 호출하고 기존 OnRetiredAsync를 호출하지 않는다. loader 경로는 정상 종료의 기존 OnRetiredAsync를 유지하지만 무효화 시에는 두 경로 모두 저장용 hook을 호출하지 않는다. Entry에 영속 버전(long), 메모리 세대(long), 유효성 상태를 둔다.

### C. DB 매핑과 transaction 수명

`PlayerStore<TPlayer>`는 abstract이고 생성자는 `protected PlayerStore(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)`다. 다음 protected abstract mapping만 게임이 구현한다. DB 연결을 반환한 뒤 Open/Begin/Commit/Rollback/Dispose는 프레임워크가 수행한다.

```csharp
DbConnection CreateConnection();
ValueTask<long?> LockVersionAsync(DbConnection db, DbTransaction tx,
    string accountKey, CancellationToken ct);
ValueTask<TPlayer> LoadPlayerAsync(DbConnection db, DbTransaction tx,
    string accountKey, CancellationToken ct);
object CaptureChanges(TPlayer player);
ValueTask<bool> WritePlayerAsync(DbConnection db, DbTransaction tx,
    string accountKey, long expectedVersion, object changes, CancellationToken ct);
ValueTask<PlayerOperationRecord?> ReadOperationAsync(DbConnection db, DbTransaction tx,
    Guid operationId, CancellationToken ct);
ValueTask WriteOperationAsync(DbConnection db, DbTransaction tx,
    PlayerOperationRecord record, CancellationToken ct);
ValueTask WriteAuditAsync(DbConnection db, DbTransaction tx,
    string accountKey, IReadOnlyList<PlayerAuditRecord> records, CancellationToken ct);
```

LockVersionAsync는 해당 anchor를 transaction 종료까지 보호하고 현재 버전 또는 미존재(null)를 반환한다. WritePlayerAsync는 상태 변경과 `expectedVersion → expectedVersion + 1` 조건부 갱신을 수행하고 정확히 한 버전 행을 갱신했는지 반환한다. false는 전체 실패다. 버전 overflow는 쓰기 전에 실패시킨다. 신규 계정/anchor 생성은 게임의 계정 생성 흐름이며 오프라인 대상 미존재를 자동 생성으로 처리하지 않는다.

`PlayerOperationRecord`는 ID·Kind·ActorKey·ParticipantKeys·32-byte InputFingerprint·ResultSchemaVersion·ResultPayload를 소유하는 불변 값이다. 생성 시 목록/bytes를 복사한다. `PlayerAuditRecord`는 OperationId·EventName·OccurredAtUtc·Payload를 소유한다. `PlayerSaveData`는 internal이며 Key·ExpectedVersion·CapturedDirtyVersion·Changes·AuditRecords를 저장한다. CaptureChanges는 메모리 Transaction 종료 후 호출하며 그 반환 객체는 DB 결과 처리까지 소유한다.

internal `PlayerStoreScope<TPlayer> : IAsyncDisposable`가 열린 DB transaction을 소유한다. Store의 `internal ValueTask<PlayerStoreScope<TPlayer>> OpenScopeAsync(IReadOnlyList<string> keys, ILogger logger, CancellationToken ct)`는 정렬된 전체 anchor를 보호한다. Scope는 `long? GetVersion(string key)`, `ValueTask<TPlayer> LoadAsync(string key, CancellationToken ct)`, `ValueTask<PlayerOperationRecord?> ReadOperationAsync(Guid id, CancellationToken ct)`, `ValueTask WriteAsync(PlayerSaveData data, CancellationToken ct)`, `ValueTask WriteOperationAsync(PlayerOperationRecord record, CancellationToken ct)`, `CommitAsync(CancellationToken ct)`, `RollbackAsync(Exception cause)`를 제공한다. 마지막 두 메서드는 `ValueTask<PlayerStoreResult>`이며 `PlayerStoreOutcome`은 Committed/RolledBack/Unknown, `Error`는 Exception?다. Dispose 오류는 받은 Logger로 별도 보고하고 이미 확인된 Outcome을 바꾸지 않는다.

DB transaction을 연 뒤 receipt 확인·임시 상태 로드·동기 Validate/Apply·쓰기·commit까지 같은 Scope를 사용한다. DB transaction 안에 외부 API/클라이언트 응답/비동기 게임 로직을 넣지 않는다. 메모리 Transaction은 Apply 안에서 끝내므로 DB I/O 동안 Undo scope가 유지되지 않는다. 단일 로그인/주기 저장도 같은 StoreScope를 사용한다. cold load는 anchor 보호 이후 읽어서 과거 writer의 종료를 확인한다.

Commit 호출 전 쓰기 오류는 Rollback ACK를 받으면 RolledBack, ACK가 불명확하면 Unknown이다. Commit 호출을 시도한 뒤 예외는 이후 rollback 시도의 성공 여부와 무관하게 Unknown이다. 정상 Commit 반환 뒤 cleanup 오류는 Committed다. 저장 결과 불명과 취소를 일반 rollback으로 바꾸지 않는다.

### D. 작업과 결과

```csharp
public abstract class PlayerOperation<TPlayer, TResult>
    where TPlayer : Player where TResult : class
{
    protected PlayerOperation(Guid id, string kind, string actorKey,
        IReadOnlyList<string> participantKeys, ReadOnlyMemory<byte> inputFingerprint,
        IPlayerResultCodec<TResult> codec);
    public abstract int Validate(PlayerOperationContext<TPlayer> context);
    public abstract TResult Apply(PlayerOperationContext<TPlayer> context);
}

public interface IPlayerResultCodec<TResult> where TResult : class
{
    int SchemaVersion { get; }
    ReadOnlyMemory<byte> Encode(TResult result);
    TResult Decode(int schemaVersion, ReadOnlyMemory<byte> payload);
}

// PlayerRegistry<TPlayer>
public PlayerRegistry(PlayerStore<TPlayer> store, PlayersOptions? options = null,
    ILogger? logger = null);
public ValueTask<PlayerOperationResult<TResult>> ExecuteAsync<TResult>(
    PlayerOperation<TPlayer, TResult> operation, CancellationToken ct = default)
    where TResult : class;
```

Operation은 `Id`, `Kind`, `ActorKey`, `ParticipantKeys`, `InputFingerprint`, `Codec` 불변 getter와 복사한 정렬/중복 제거 키를 제공한다. ActorKey는 참여자에 포함되어야 한다. Validate는 0 또는 음수 게임 거절 코드를 반환한다. 읽기 전용 계약이며 예상 가능한 거절은 이 단계에서 끝낸다. Apply의 null 결과·예외·codec 실패는 변경 후 실패다. Context는 OperationId, ParticipantKeys, `TPlayer GetPlayer(string key)`를 제공하고 수명 종료 후 접근을 거부한다.

`PlayerOperationResult<TResult>`는 `PlayerOperationOutcome Outcome`, `int Status`, `TResult? Value`를 제공한다. enum은 Committed/AlreadyCommitted/Rejected/Failed/Unknown이다. Committed/AlreadyCommitted만 Status=0과 non-null Value를 갖고, 나머지는 nonzero Status와 null Value를 갖는다. 구조화된 실패 원인은 로그에 남기고 클라이언트 결과에 예외를 노출하지 않는다.

`ProtobufPlayerResultCodec<TResult>(MessageParser<TResult> parser, int schemaVersion = 1)`는 `where TResult : class, IMessage<TResult>`이며 제공하지 않은 버전의 Decode를 거절한다. 작업 ID 재사용 때 actor/kind/keys/fingerprint가 일치해야 하며 결과를 decode해서 반환한다. 최초 결과도 Encode한 bytes를 Decode해서 반환해 DB 기록과 응답이 같은 내용을 갖게 한다. 이 Decode까지 commit 전에 완료한다.

### E. 등록·알림·로그

```csharp
// PlayersConfig<TSession>; Wrap에서 TSession/operation의 TPlayer 일치를 검증.
public void OnPlayerOperation<TPlayer, TReq, TRes>(
    Func<string, TReq, PlayerOperation<TPlayer, TRes>> factory)
    where TPlayer : Player
    where TReq : class, IMessage<TReq>
    where TRes : class, IMessage<TRes>;

// Player: 게임 ActionLog sink가 payload를 직렬화한 뒤 호출.
public void RecordAudit(string eventName, ReadOnlySpan<byte> payload);

// PlayerRegistry<TPlayer>: Hosting에서 drain 전 admission을 닫음.
public void StopAcceptingOperations();
```

factory는 인증된 actor key와 요청만 받는다. 내부 등록 metadata는 Wrap에서 snapshot하며 요청 factory가 원래 actor를 변경하면 거절한다. 다인 요청은 단일 권한을 먼저 잡지 않고 전체 작업 실행기로 간다. 정상 RPC 등록도 공통 PlayerSession hook을 통과한다. store 없는 registry의 다인 등록은 Wrap에서 즉시 실패한다.

RPC에서는 public background 진입을 직접 호출하지 않고 `internal ValueTask<PlayerOperationResult<TResult>> ExecuteForSessionAsync<TResult>(PlayerSession<TPlayer> session, PlayerOperation<TPlayer, TResult> operation, CancellationToken ct)`를 사용한다. 진입 당시 Player 참조/세대를 캡처하고 전체 권한 확보 후 같은 바인딩인지 재검사한다. 대기 중 logout/NewWins로 stale해졌다면 Apply를 실행하지 않는다. public ExecuteAsync는 인증을 마친 서버 내부 작업용이다.

RpcStatus에 InvalidOperation=4, PersistenceFailure=5, OutcomeUnknown=6, OperationConflict=7, PlayerNotFound=8을 추가한다. DisconnectCode.PersistenceFailure=6을 추가한다. InvalidOperation은 입력/중첩 호출의 framework 거절, OperationConflict는 기존 receipt의 신원/입력 불일치다. SQL/버전 실패는 PersistenceFailure, commit 결과 불명은 OutcomeUnknown이다. transport가 이미 닫혔다면 응답 전달 여부와 작업 Outcome을 분리한다.

PlayerSession.SendUpdateAsync는 sealed override하며 활성 작업 중 EncodeUpdate로 완성된 packet bytes를 고정해 참여자별 버퍼에 넣고 완료된 ValueTask를 반환한다. Player.PushUpdateAsync의 true는 이 경우 전달 예약을 뜻한다. commit 후 바인딩/세대를 확인하고 전송을 시작한 다음 권한을 해제한다. 네트워크 await와 실패 관찰은 권한 밖에서 수행한다. 실패/unknown에서는 버퍼를 버린다. 다른 참가자 B의 콜백에서도 같은 버퍼가 적용된다.

RecordAudit는 현재 실행 중인 플레이어의 작업 ID를 붙여 불변 bytes를 복사하고 pending audit에 보관한다. 일반 요청·posted work·tick·상태 변경 수명 hook은 실행 frame에 프레임워크 Guid 작업 ID를 사용하고, 다인 요청은 전달된 ID를 사용한다. 임시 로드·Validate·scope 밖 호출은 기록/변경 경로가 아니므로 거절한다. pending audit도 저장 대상이므로 Player를 Dirty로 만든다. Capture 시 이번 묶음이 소유하며 commit 후 완료, 실패 시 Player와 함께 폐기한다. 원래 ActionLog sink 안에서 받은 span을 그대로 보관하지 않는다.

관측은 ILogger의 고정 template/EventId와 typed 값으로 구현한다. 작업 ID, 저장 시도 ID, 참여자 모드/세대/DB 버전, 단계/소요 시간/오류를 기록한다. 단계 이름은 스펙 §9와 동일하게 사용한다. SQL mapping 예외의 원본 DbException과 ErrorCode를 로그에 남기고 SQL 본문/연결 문자열을 자동 기록하지 않는다.

## Task 1: 세션과 RPC 실행 확장 지점

**Files:** 표의 Task 1 제품/테스트 파일. Core·Rpc csproj의 버전도 여기서 올린다.

**Interfaces:** 소비: 현재 RpcConfig 등록과 Session actor. 제공: 계약 A의 다섯 메서드와 RpcRequestHandler. Players 참조를 추가하지 않는다.

- [x] **Step 1 — 실패 테스트 작성:** RpcExecutionTests에서 `Request_hook_encloses_handler_and_async_completion`은 순서가 enter/handler-start/handler-end/exit이고 handler fault에도 exit 1회임을 검증한다. `Gate_rejection_and_ping_skip_request_hook`은 handler/hook 호출 0회다. SessionPostTests에 `Posted_work_uses_hook`와 `Session_scope_disposes_when_disconnect_hook_throws`를 추가한다.

```csharp
Assert.That(events, Is.EqualTo(new[] { "enter", "handler-start", "handler-end", "exit" }));
Assert.That(scopeDisposeCalls, Is.EqualTo(1));
```

- [x] **Step 2 — RED 확인:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~RpcExecutionTests|FullyQualifiedName~SessionPostTests'`에서 미구현 hook 또는 기대 순서 실패를 확인한다. 도구/restore 실패를 RED로 인정하지 않는다.
- [x] **Step 3 — 구현:** A의 시그니처를 추가하고 Session.RunAsync/RpcRuntime을 연결한다. EncodeUpdate가 기존 UpdateMap 검증을 사용하게 한다. 기존 오류 정책·packet ownership·handler status를 보존한다.
- [x] **Step 4 — GREEN 확인:** 위 명령과 `--filter 'FullyQualifiedName~RpcGateTests|FullyQualifiedName~RpcServerTests|FullyQualifiedName~SessionActorTests'`를 통과시킨다.
- [x] **Step 5 — 작업 리뷰:** 요청당 closure/새 delegate 부재, scope finally 순서와 API 문서를 검토하고 Task 1 diff/결과를 기록한다. 커밋하지 않는다.

## Task 2: 플레이어 실행 권한과 수명주기 통합

**Files:** 표의 Task 2. Players/PlayerReleaseReason.cs·AssemblyInfo.cs를 추가하고 Core/DisconnectCode.cs에 PersistenceFailure=6을 정의한다. AssemblyInfo에는 `InternalsVisibleTo("Bun3.Server.Tests")`를 선언해 internal 실행 모듈 자체를 검증한다. Players csproj 버전을 0.6.0으로 올린다.

**Interfaces:** 소비: A. 제공: B의 table/scope/lane, 공통 단일 요청/posted work 실행, `internal ValueTask InvalidateAsync(IReadOnlyList<string> keys, bool requireRecovery, Exception cause)`의 잠금 보유 상태용 경로. 이 단계의 legacy 저장 실패는 requireRecovery=true이며 다음 로드가 새 객체를 반환해야 한다.

- [x] **Step 1 — 실패 테스트 작성:** 아래 사례를 public RPC/registry/posted work에서 재현한다. barrier는 TaskCompletionSource(RunContinuationsAsynchronously)로 만든다.

| 테스트 | 검증 |
|---|---|
| Login_reply_reads_state_before_waiting_operation_runs | SignIn 직후 handler를 멈추면 대기 중인 동일 키 lifecycle 작업이 실행되지 않음. handler 반환 후 실행 |
| Duplicate_login_waits_for_old_handler | 기존 handler의 await 중 새 로그인 미완료, 이전 작업 종료 후 바인딩 교체, old posted callback 0회 |
| Posted_tick_and_detach_share_player_ownership | 임계구간 동시 진입 수 최대 1, stale tick은 Save 0회 |
| Canceled_multi_acquire_releases_prefix_and_preserves_waiters | internal table interface에서 일부 획득 후 취소해도 이후 A와 B 권한 획득 모두 완료, 살아 있는 대기자와 다른 gate를 만들지 않음 |
| Reentrant_lifecycle_fails_without_deadlock | OnAttached/posted callback 안의 SignIn/RetireAll/권한 확장을 즉시 거절, 후속 정상 요청 성공 |
| Failed_legacy_save_discards_player_without_retirement_save | 저장 1회 실패, 재시도 0회, OnRetired 저장 0회, 새 로그인 loader 호출 증가 |
| Mutating_attach_or_detach_hook_failure_invalidates_player | OnAttached/OnDetached의 변경 후 예외를 각각 주입하면 캐시/바인딩 제거, 정상 종료 저장 0회, 다음 로그인 새 객체 |

```csharp
Assert.That(registry.TryGet("guest:a"), Is.Null);
Assert.That(saveCalls, Is.EqualTo(1));
Assert.That(retirementSaveCalls, Is.Zero);
```

- [x] **Step 2 — RED 확인:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayerExecutionTests|FullyQualifiedName~PlayerTickingTests'`.
- [x] **Step 3 — 구현:** stripe를 table로 교체하고 요청/posted/tick/SignIn/detach/retire를 같은 권한에 연결한다. PlayerRegistry를 Execution/Lifecycle partial로 나누되 registry snapshot/Wrap 등 외부 계약을 유지한다. TrySaveAsync의 예외 삼키기·다음 주기 재시도를 제거하고 registry가 무효화한다. OnAttached/OnDetached의 상태 변경 예외도 무효화로 처리하며 성공 바인딩이나 정상 종료 저장으로 진행하지 않는다. cleanup 실패는 다른 객체 정리를 막지 않는다. 메모리 세대와 현재 바인딩을 권한 획득 뒤 재검사한다.
- [x] **Step 4 — GREEN 확인:** 위 명령과 `--filter 'FullyQualifiedName~PlayersTests|FullyQualifiedName~PlayersE2ETests'`를 통과시킨다. 기존 `Failed_save_keeps_dirty_and_retries_next_period`를 폐기·새 로딩 계약 테스트로 교체한다.
- [x] **Step 5 — 작업 리뷰:** 다인 Acquire의 순서/취소/refcount, 로그인 요청으로의 scope 인계, Session lane의 설치·복원을 검토한다. table 자체의 internal interface 검증과 RPC/registry를 통한 통합 검증을 구분하고 테스트를 위해 공개 API를 늘리지 않는다.

## Task 3: DB 저장 어댑터와 단일 저장 통합

**Files:** 표의 Task 3와 PlayerRegistry.cs·Player.cs. 테스트 csproj에 `<PackageReference Include="Microsoft.Data.Sqlite" Version="[10.0.0]" PrivateAssets="all" />`를 추가한다.

**Interfaces:** 소비: B. 제공: C의 Store/Scope/records와 D의 store 기반 registry 생성자. Scope.LoadAsync는 `ValueTask<TPlayer>`이고 GetVersion은 long?다. 영속 버전은 성공 시에만 Entry로 전달한다.

- [x] **Step 1 — 실패 테스트 작성:** SQLite는 fixture별 임시 파일과 새 connection을 사용한다. schema는 players(key TEXT PRIMARY KEY, version INTEGER, gold INTEGER), operations(id TEXT PRIMARY KEY, kind, actor, participants BLOB, fingerprint BLOB, schema_version INTEGER, result BLOB), audits(player_key, operation_id, event_name, occurred_at, payload BLOB)다. adapter는 IsolationLevel.Serializable과 transaction 내 anchor 접근을 사용한다. tests는 registry의 로그인·변경·detach 저장을 통해 실행한다.

| 테스트 | 검증 |
|---|---|
| Single_save_updates_state_and_version_atomically | A=100, version=3에서 90/4를 함께 저장. audit 통합은 Task 5에서 검증 |
| Write_failure_rolls_back_and_discards_cached_player | write 이후 예외 시 DB=100/3, 캐시 없음, 다음 로그인=100 |
| Version_mismatch_preserves_external_writer | 외부에서 version=4로 변경 후 이전 version=3 저장은 덮어쓰지 않음 |
| Dispose_error_preserves_committed_outcome | Commit 성공 뒤 Dispose 오류에도 DB=90/4이고 저장 실패로 재저장하지 않음 |
| Load_waits_for_previous_writer | 이전 transaction 종료 barrier 전 loader가 반환하지 않음 |

```csharp
Assert.That((persistedGold, persistedVersion), Is.EqualTo((90L, 4L)));
Assert.That(registry.TryGet("guest:a")!.IsDirty, Is.False);
```

- [x] **Step 2 — RED 확인:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayerStoreTests|FullyQualifiedName~PlayerStoreSqliteTests'`.
- [x] **Step 3 — 구현:** C의 ADO 수명과 결과 분류를 구현한다. store 생성자는 loader 생성자와 명확히 다른 경로이고 store가 양쪽 로더 역할을 한다. 주기/detach/정상 retire도 공통 StoreScope를 사용한다. 보호된 anchor 미존재와 버전 불일치를 구분한다. 정상 retire는 저장 성공 후 OnReleased(Retired), 실패는 무효화이며 재저장하지 않는다.
- [x] **Step 4 — GREEN 확인:** 위 명령과 Task 2의 Players/Ticking 회귀를 통과시킨다. fake DbConnection/DbTransaction은 Commit 직전/직후 예외·Rollback/Dispose 예외를 독립적으로 주입할 수 있게 한다.
- [x] **Step 5 — 작업 리뷰:** 모든 command가 같은 connection/transaction을 받는지, mapper false/예외가 전파되는지, Commit 진입 여부로 Unknown을 분류하는지 확인한다. SQLite의 단일 writer 동작을 PostgreSQL/MySQL 행 잠금 보장으로 일반화하지 않는다.

## Task 4: 온라인·오프라인 공동 작업과 복구

**Files:** 표의 Task 4, PlayerRegistry.Persistence.cs, PlayerStoreResult.cs. PlayersOptions.MaxOperationParticipants=16을 추가한다.

**Interfaces:** 소비: B/C. 제공: D 전체, E의 내부 `ExecuteForSessionAsync`, `StopAcceptingOperations()`와 RpcStatus 값. `InvalidateAsync`는 동일 권한 아래 현재 세대를 비교하고 캐시 제거·deauthorize·pending data 폐기·OnReleased(Invalidated)를 수행한다. requireRecovery=true인 키는 다음 cold load가 DB anchor를 보호하고 성공할 때까지 정상 상태를 반환하지 않는다.

- [x] **Step 1 — 실패 테스트 작성:** 처음 DB는 A=100/version=3, B=20/version=8이다. TransferOperation은 10을 이동하고 `TransferResult`의 long SenderBalance/RecipientBalance에 결과를 담는다. Validate 부족액 코드는 -10, 정상 Apply 카운터를 가진다. 이 단계의 session 대기 검증은 내부 ExecuteForSessionAsync를 harness에서 호출하며 RPC 등록은 Task 5가 연결한다.

| 테스트 | 검증 |
|---|---|
| Online_to_offline_transfer_is_one_commit | DB=90/30, versions=4/9, B의 attach/tick=0, 임시 B release=1, receipt=1 |
| Grace_target_reuses_unsaved_state | 캐시 B=25이면 로드 없이 35 저장, 기존 B 참조 유지 |
| Login_during_offline_operation_waits | B 임시 로드/Apply/Commit barrier 동안 로그인 대기, 이후 DB=30 로드 |
| Opposite_participant_order_and_disjoint_work | A-B/B-A는 직렬, A-B/C-D는 Apply 구간 병렬, 동일 키 중복을 한 번만 획득 |
| Reject_and_load_failure_preserve_existing_dirty_state | A의 이전 미저장 상태 유지, Apply=0, 기존 객체 무효화 0회 |
| Apply_or_second_write_failure_invalidates_every_participant | 기존 A/B 캐시 제거, partial DB 반영 없음, 임시 자원 전부 정리 |
| Receipt_identity_mismatch_rejects_without_apply | actor/kind/keys/fingerprint 각각 불일치 시 conflict, 과거 결과 노출 없음 |
| Repeated_id_returns_frozen_result_once | 동일 요청 2회에도 Apply=1, receipt=1, 첫/재요청 값 동일 |
| Lost_commit_ack_blocks_reuse_until_database_settles | 실제 commit 유/무 두 분기에서 Unknown, 복구 barrier 전 로그인/Apply 미실행, 이후 DB 실제값 로드 |
| Cancellation_after_apply_does_not_abandon_save | client ct 취소 뒤에도 writer 결과/정리 관찰, 늦은 저장이 새 객체를 덮어쓰지 않음 |
| Invalid_keys_and_nested_operation_fail_without_leaks | 빈 ID/키, 17명의 고유 참여자, 중첩 Execute 거절. 다음 정상 작업 성공 |
| Queued_operation_rechecks_requesting_session | 권한 대기 중 요청 세션이 교체되면 stale 작업 Apply=0, 새 세션 작업 정상 완료 |

```csharp
Assert.That(result.Outcome, Is.EqualTo(PlayerOperationOutcome.Committed));
Assert.That((result.Value!.SenderBalance, result.Value.RecipientBalance), Is.EqualTo((90L, 30L)));
Assert.That(applyCalls, Is.EqualTo(1));
```

- [x] **Step 2 — RED 확인:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayerOperationTests|FullyQualifiedName~PlayerOperationRecoveryTests'`.
- [x] **Step 3 — 구현:** 정렬된 키 권한 확보 → binding 재검사 → StoreScope 전체 anchor 보호 → receipt 검사 → 기존/임시 참여자 확보 → 읽기 전용 Validate → 동기 Apply 및 결과 고정 → 모든 변경/감사/receipt 쓰기 → Commit → 완료 표시 또는 전체 무효화 순서로 실행한다. 동일 receipt는 Apply 없이 반환한다. 버전 불일치로 기존 메모리가 stale임을 알면 Apply 전이라도 해당 작업의 기존 참여자들을 무효화한다.
- [x] **Step 4 — GREEN 확인:** 위 명령과 Task 3의 실제 SQLite 원자 저장 테스트를 다인 write failure까지 확장해 통과시킨다. caller cancellation은 Apply 전까지만 사용하고 이후 DB 정리 수명과 분리한다. Apply 전의 acquire/load await 뒤에는 admission을 재검사하고, Apply 이후에는 admission 종료와 무관하게 확정/정리를 완료한다.
- [x] **Step 5 — 작업 리뷰:** temp/cache 분기와 정상/오류 release 정확히 1회, DB ACK 전 saved version 미전진, StoreScope 종료 확인과 결과 고정, 중복 ID의 비공개 결과 보호를 확인한다. 자동 mutation 재시도나 임의 read만으로 Unknown 해소를 구현하지 않는다.

## Task 5: RPC 등록·커밋 후 통지·감사 연결

**Files:** 표의 Task 5 및 PlayerRegistry.Persistence.cs. 테스트 csproj에 새 proto를 추가하고 기존 players_game.proto는 변경하지 않는다. PlayerReleaseReason/DisconnectCode는 Task 2, RpcStatus 값은 Task 4 결과를 재사용한다.

**Interfaces:** 소비: A/D와 B의 요청 frame, Task 4의 RpcStatus 값. 제공: E의 OnPlayerOperation/RecordAudit와 알림 보류. PlayerSession은 metadata상 다인 요청이면 단일 owner 획득 없이 factory → registry로 진입한다. factory 결과 ActorKey가 인증된 키와 같아야 한다.

- [x] **Step 1 — 실패 테스트 작성:** player_operations.proto는 별도 namespace와 login/get_balance/transfer 요청·응답, balance_changed update를 정의한다. 모든 oneof case를 fixture에서 등록한다.

| 테스트 | 검증 |
|---|---|
| Operation_rpc_uses_current_envelope_on_replay | RPC request ID가 바뀌어도 도메인 결과 동일, current request ID로 응답 |
| Raw_rpc_registration_still_acquires_player_owner | config.Rpc.OnRequest 등록도 보호되어 동시 진입 0회 |
| Missing_store_or_wrong_player_type_fails_at_wrap | 설정 오류를 첫 요청 전 검출 |
| Target_callback_update_waits_for_commit | A와 B 각각의 update가 Commit barrier 전 0개, 이후 각 1개 |
| Deferred_message_is_frozen_and_failure_drops_it | 큐에 넣은 뒤 메시지를 바꿔도 원래 bytes 전달, rollback/unknown 시 전달 0개 |
| Delivery_error_does_not_invalidate_committed_players | DB/receipt 유지, Apply 재실행 0회, DeliveryFailed 기록 |
| Audit_span_is_owned_and_saved_with_state | 원본 buffer 변경/해제 후에도 고정 audit 저장, 감사 SQL 실패 시 전체 rollback |
| Domain_reactions_finish_before_capture | Items의 onApplied가 업적/보상을 바꾼 결과까지 같은 저장에 포함, 메모리 rollback 로그는 없음 |
| Throwing_diagnostic_sink_does_not_change_outcome | ILogger 예외에도 Commit/정리 결과 유지, audit mapping 오류와 구분 |

```csharp
Assert.That(updatesBeforeCommit, Is.Zero);
Assert.That(updatesAfterCommit, Is.EqualTo(2));
Assert.That(receiptCount, Is.EqualTo(1));
```

- [x] **Step 2 — RED 확인:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayerOperationRpcTests|FullyQualifiedName~PlayerOperationNotificationTests|FullyQualifiedName~PlayerOperationAuditTests'`.
- [x] **Step 3 — 구현:** 등록/바인딩 snapshot, frozen packet/result, 참여자별 update 버퍼, immutable audit pending 버퍼를 연결한다. 전달 작업은 권한 아래 시작만 하고 밖에서 await/관찰한다. 응답/알림 실패로 DB 성공을 실패로 바꾸지 않는다. 승인 스펙의 단계와 correlation 필드를 고정 logger template로 기록한다.
- [x] **Step 4 — GREEN 확인:** 위 명령과 `--filter 'FullyQualifiedName~Rpc|FullyQualifiedName~OutgameNotificationTests|FullyQualifiedName~OutgameLedgerIntegrationTests'`를 통과시킨다. 미등록 update 타입은 EncodeUpdate 단계에서 검출되어 commit 후 직렬화 실패로 미뤄지지 않아야 한다.
- [x] **Step 5 — 작업 리뷰:** actor factory의 상태 접근 금지와 mutable 결과/메시지 복사, peer B 경로의 동일 보류, Task 밖으로 Player 참조가 새는 예시 부재를 확인한다.

## Task 6: 호스팅·종료·사용 예와 전체 검증

**Files:** 표의 Task 6와 PlayerRegistry.Lifecycle.cs·PlayerRegistry.Invalidation.cs. Hosting 버전을 0.7.0으로 올린다.

**Interfaces:** 소비: E의 StopAcceptingOperations와 기존 RetireAllAsync. Hosting에 기존 AddPlayerServer와 같은 generic parameters/나머지 optional arguments를 가진 overload를 추가하되 loader 인자는 `Func<IServiceProvider, PlayerStore<TPlayer>> store`로 바꾼다. DI에서 store를 한 번 만들고 registry와 ticker가 같은 인스턴스를 사용한다.

- [x] **Step 1 — 테스트 작성:** `Store_registration_routes_every_save_through_same_adapter`는 periodic/detach/retire 모두 공통 store이고 legacy save hook=0임을 검증한다. `Shutdown_and_old_detach_cannot_save_or_remove_replacement`는 오래된 세션 종료가 새 세대/새 객체에 영향 0임을 검증한다. `Shutdown_stops_admission_before_drain`은 새 작업/로그인을 거절하고 기존 Apply 이후 저장을 정리한 뒤 valid cache만 저장함을 검증한다. `Uncontended_single_owner_has_no_per_iteration_allocation`은 warmup 후 synchronous 내부 owner 진입 경로에서 10,000회 추가 allocation=0을 검증하며 transport/protobuf 할당과 분리한다.

```csharp
Assert.That(legacySaveCalls, Is.Zero);
Assert.That(allocatedBytesAfterWarmup, Is.Zero);
Assert.That(registry.TryGet("guest:a"), Is.SameAs(replacement));
```
- **Step 2 — 사전 RED 증거 미확보:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release --filter 'FullyQualifiedName~PlayerOperationHostingTests|FullyQualifiedName~PlayerOperationE2ETests|FullyQualifiedName~PlayerExecutionAllocationTests'`를 실행했으나 최초 실패는 fixture/기대값 오류였다. 제품 기능 부재를 증명하는 사전 RED로 보고하지 않는다.
- [x] **Step 3 — 구현:** Host stop의 첫 단계에서 admission을 닫고 tick 중지 → server drain → 유효 Player retire 순서로 처리한다. 시간 제한 도달이 미완료 DB 작업의 취소/rollback 완료라는 뜻이 아니므로 결과를 관찰하고 실패 키를 재사용하지 않는다. 대기 작업을 버려놓고 registry를 새 쓰기에 개방하지 않는다. docs/player-operations-and-persistence.md에 실제 컴파일되는 adapter/transfer/오프라인 PvP 예제와 save-failure migration을 작성하고 예제는 E2E fixture와 같은 interface로 검증한다.
- [x] **Step 4 — 기능 GREEN:** 위 명령과 `--filter 'FullyQualifiedName~PlayersHostingTests|FullyQualifiedName~PlayersE2ETests'`를 통과시킨다. 구현에 맞춰 Transaction의 session-only XML을 배타 실행 문맥으로 정리하고 docs/outgame-rollback.md의 저장 실패 설명을 갱신한다. 특정 DB를 선택하기 전 실제 운영 연결/네트워크 장애 보장을 완료했다고 쓰지 않는다.
- [x] **Step 5 — 전체 검증:** `dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release`와 `dotnet build Bun3.sln -c Release -warnaserror`를 실행한다. 통과 후 새 변경/실패 없이 반복하지 않는다. `git diff --check`와 신규 파일의 공백/링크 검사도 실행한다.
- [x] **Step 6 — 최종 리뷰:** 시작 시점의 dirty baseline 대비 변경을 포함해 전체 스펙·동시성·실패 경로를 검토한다. 검증한 DB 제공자, 배포 한계, 미실행 검증을 보고한다. 외부 게임 수정·커밋·배포 없이 결과를 사용자에게 정리한다.

## 스펙 대응과 계획 검토

| 스펙 검증 항목 | 담당 작업 |
|---|---|
| §11.1–4 온라인/오프라인/유예/로그인 경쟁 | Task 4 |
| §11.5–6 요청·틱·수명·NewWins | Task 2, 5, 6 |
| §11.7–11 검증 실패·공동 rollback·unknown·취소·버전 | Task 3, 4, 6 |
| §11.12 재요청 | Task 4, 5 |
| §11.13–15 도메인 반응·알림·로그 | Task 5 |
| §11.16 회귀 | 각 Task의 GREEN 및 Task 6 전체 검증 |
| §11.17 잘못된 입력·재진입 | Task 2, 4 |

SQLite 10.0.0은 repository의 .NET 10 테스트 계열에 맞춘 테스트 전용 pin이다. [NuGet 패키지](https://www.nuget.org/packages/Microsoft.Data.Sqlite/10.0.0)와 [transaction 동작](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/transactions)을 확인했다. SQLite는 동시 writer가 제한되므로 서로 겹치지 않는 Player의 실행 병렬성은 controllable store로 검증하고, SQLite에서는 실제 원자 저장·재로딩·receipt를 검증한다. ADO의 async transaction API는 [netstandard2.1 계약](https://learn.microsoft.com/en-us/dotnet/api/system.data.common.dbconnection.begintransactionasync?view=netstandard-2.1)을 사용한다.

실행 기록은 이 계획의 SDD ledger와 작업별 보고서에 보관한다. 현재 구현의 사용법은 [플레이어 작업과 영속 저장](../../player-operations-and-persistence.md)을 참고한다. 초기 Task 6에서 실제 구현 결여를 보여 주는 사전 RED는 확보하지 못했고 fixture/기대값 실패 후 GREEN을 확인했다. 최종 전체 리뷰의 네 항목 수정은 별도로 6개 회귀 테스트의 RED→GREEN을 확인했다. 최종 수정에는 별도 독립 재리뷰 대신 자체 검토를 수행했다.

계획 대비 내부 파일 배치: Task 5 알림·진단은 `PlayerOperationNotifications.cs`와 `PlayerOperationDiagnostics.cs`로 분리했다. 취득 오류의 원인·실제 rollback 결과·단계 보존은 `PlayerStoreAcquisitionException.cs`가 담당한다. 테스트용 SQLite native dependency는 보안 경고를 제거한 `SQLitePCLRaw.lib.e_sqlite3 [3.53.3]`으로 고정했으며 런타임의 제공자 독립성은 유지했다.
