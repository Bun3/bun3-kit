# Player Write Authority Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** WorldServer 운영 중에는 서버가 Admin 명령을 처리하고, 명시적 종료·점검 인계 후에는 Admin이 같은 명령을 실행하도록 하며 플레이어별 영속 version 없는 저장을 지원한다.

**Architecture:** DB 제어 행의 상태와 실행 인스턴스 ID로 수정 권한을 보호하고, 모든 저장 트랜잭션이 이 행과 대상 플레이어 행을 같은 순서로 잠근다. 기존 version 기반 PlayerStore와 새 권한 기반 저장 타입은 공통 트랜잭션 실행기를 공유한다. 영속 Admin 명령은 현재 권한을 가진 실행기에서 처리하고 변경·receipt·명령 완료를 함께 커밋한다.

**Tech Stack:** Players netstandard2.1/C# 9, Hosting/tests net10.0, BCL ADO.NET, 기존 NUnit 4.1.0, 테스트 전용 Npgsql 10.0.3/PostgreSQL 18.

**Spec:** [승인된 설계](../specs/2026-10-08-player-write-authority-design.md).

상태: 2026-10-08 사용자 승인 후 구현 반영. 작업자 실행 한도로 여섯 작업을 직접 구현·자체 리뷰했으며 독립 작업자/최종 리뷰는 수행하지 못했다. RED/GREEN 기록과 기존 dirty baseline을 기준으로 한 작업별 diff는 `.superpowers/sdd/2026-10-08-player-write-authority/`에 보존한다. Docker 실행기 검증은 환경 제약으로 미완료이며 같은 실제 DB 테스트를 네이티브 PostgreSQL 18.6으로 검증한다.

## Global Constraints

- 정상 종료 또는 점검 전환이 확인된 경우부터 지원한다. ping 실패·heartbeat 만료·프로세스 조회 실패로 DB의 상태를 `Offline`으로 바꾸지 않는다.
- 첫 구현은 한 작업이 하나의 소유 범위와 DB 안에서 끝나도록 제한한다. 소유 범위를 idlez의 `world_id`와 동일하다고 가정하지 않는다.
- 모든 플레이어 저장 경로가 이 계약에 참여한다. 주기 저장, 로그인 관련 쓰기, 종료 저장, Admin, 배치가 별도 연결로 우회 쓰기하면 보장하지 않는다.
- 기존 버전 검사 방식은 유지하고, 명시적으로 선택하는 수정 권한 기반 저장 방식을 추가한다. version 검사를 끄는 boolean이나 가짜 고정 버전을 허용하지 않는다.
- Items/Achievements의 단독 사용, 메모리 Transaction 선택 사용, 기존 loader 기반 등록은 유지한다.
- 별도 Admin UI, 외부 게임 수정, 운영 DB 접속, 커밋·배포는 포함하지 않는다. 기존 미커밋 변경을 보존하고 작업별 baseline diff로 리뷰한다.
- 신규 재사용 코드는 Players/Hosting 안에 둔다. 제품 패키지에 PostgreSQL·ORM·Admin 웹 프레임워크 의존성을 추가하지 않는다.
- Players는 netstandard2.1/C# 9, 영어 XML 문서, 블록 namespace, `ConfigureAwait(false)`를 유지한다. Hosting/tests의 기존 net10.0을 유지한다.
- 단일 요청·tick 진입의 추가 객체·배열·closure·문자열 할당을 피한다. 프로세스 수명에 만든 gate와 값 타입 진입 토큰을 사용한다.
- 제품 변경 시 Players `0.6.0 → 0.7.0`, Hosting `0.7.0 → 0.8.0`으로 올린다. 이 계획에서는 Core/Rpc 공개 계약 변경을 요구하지 않는다.
- 각 Task는 RED → 최소 구현 → GREEN → 스펙/품질 리뷰 순서로 끝낸다. 커밋 대신 실행 기록과 작업별 diff를 남긴다. 컴파일 오류만 발생한 테스트를 동작 검증 RED로 기록하지 않는다.

## Review Focus

1. 같은 문자열인 관리자 ID와 플레이어 키, 또는 접수 후 변조된 payload: 서로 다른 요청자로 구분하고 접수 당시 입력을 보존한다. Task 1 `Equal_keys_of_different_actor_kinds_do_not_match`, Task 4 `Submission_copies_mutable_input`.
2. version 없는 기준 행이 없거나 다른 소유 범위에 속함: version 부재와 대상 부재를 혼동하지 않고 로드·Apply 전에 거절한다. Task 3 `Missing_or_foreign_anchor_never_loads_or_applies`.
3. 플레이어 실행 권한을 잡은 상태에서 서버 소유권 상실을 감지함: 같은 키를 재획득해 교착하지 않고 모든 진입을 닫고 캐시를 폐기한다. Task 3 `Authority_loss_invalidates_after_releasing_current_lane`.
4. 롤백 후 실패 기록을 남기기 전에 다른 worker가 같은 명령을 완료함: 기존 성공을 실패로 덮어쓰지 않는다. Task 4 `Failure_recording_observes_competing_terminal_result`.
5. 기동/종료 전환 커밋은 성공했지만 응답이 유실됨: 동일 전환 ID로 결과를 확인하고 뒤이어 기동한 다른 인스턴스를 덮어쓰지 않는다. Task 2 `Transition_retry_preserves_later_owner`, Task 5 `Offline_ack_loss_does_not_reopen_old_registry`.

## 파일 배치와 작업 순서

아래 `P`, `H`, `T` 경로 별칭을 파일 목록에 사용한다. 각각 `server/src/Bun3.Server.Players/`, `server/src/Bun3.Server.Hosting/`, `server/tests/Bun3.Server.Tests/`다. 새 폴더는 만들지 않으며 partial은 `Type.Role.cs`, 독립 generic은 `Type{T}.cs`로 명명한다.

| 순서 | 검토 가능한 결과 | 주요 책임 |
|---|---|---|
| 1 | 관리자 요청자를 플레이어와 구분하는 작업 | Actor/receipt/RPC 인증 |
| 2 | DB 수정 권한의 전환·보호 계약 | 상태 전이, DB guard, 전환 중복 방지 |
| 3 | version 없는 저장과 기존 버전 저장 공존 | 공통 저장 수명, registry, 무효화 |
| 4 | 영속 Admin 명령의 중복 없는 DB 효과 | 접수, 등록, 실행, 결과 공동 커밋 |
| 5 | 안전한 기동·점검·종료 및 worker 운영 | 진입 차단, drain, Hosting, 수동 복구 |
| 6 | 실제 PostgreSQL 경쟁 실행 검증과 사용 예제 | 통합 테스트, 문서, 패키지 버전 |

Task 1 → 2 → 3 → 4 → 5 → 6 순서로 실행한다. 각 Task가 고정하는 interface를 후속 Task가 재사용한다. 현재 dirty worktree의 HEAD·status·대상 파일 내용을 실행 시작 시 `.superpowers/` 아래 무시되는 실행 기록으로 보존하고, 이번 변경만 분리해 리뷰한다. 실행 전 `using-git-worktrees` 지침으로 현재 격리 상태를 확인하며 기존 작업을 이동하거나 초기화하지 않는다.

### Task 1: 작업 요청자와 참여 플레이어 분리

**Files:** Create `P/PlayerOperationActor.cs`, `T/PlayerOperationActorTests.cs`. Modify `P/PlayerOperation{TPlayer,TResult}.cs`, `P/PlayerOperationRecord.cs`, `P/PlayerRegistry.Operations.cs`, `P/PlayersConfig.cs`, `P/PlayerOperationDiagnostics.cs`, `T/PlayerOperationRpcTests.cs`, `T/Helpers/SqlitePlayerStore.cs`.

**Interfaces:** Consumes 기존 PlayerOperation 생성자·ExecuteAsync·RPC 등록. Produces 다음 값 타입과 overload:

```csharp
public enum PlayerOperationActorKind { Player, Administrator }
public readonly struct PlayerOperationActor
{
    public PlayerOperationActor(PlayerOperationActorKind kind, string key);
    public PlayerOperationActorKind Kind { get; }
    public string Key { get; }
}
// PlayerOperation<TPlayer,TResult>의 새 protected overload, 기존 string actorKey overload 유지.
protected PlayerOperation(Guid id, string kind, PlayerOperationActor actor,
    IReadOnlyList<string> participantKeys, ReadOnlyMemory<byte> inputFingerprint,
    IPlayerResultCodec<TResult> codec);
public PlayerOperationActor Actor { get; }
// PlayerOperationRecord에도 같은 Actor 인자의 public 생성자 overload와 Actor getter 추가.
```

- [x] **Step 1:** 새 테스트에 `Admin_actor_is_not_loaded_as_participant`(참여자 A만 로드, 관리자 키 로드 0회), `Equal_keys_of_different_actor_kinds_do_not_match`(receipt 재사용 거절), `Player_actor_must_be_a_participant`(생성 거절), `Default_or_unknown_actor_is_rejected`(빈 키/default/미정의 enum 거절)를 작성한다. `Game_rpc_rejects_admin_actor`는 인증 플레이어와 같은 Key라도 Administrator 종류를 거절하고 Apply가 0회임을 검증한다.

```csharp
// Equal_keys_of_different_actor_kinds_do_not_match
Assert.That(replay.Status, Is.EqualTo(RpcStatus.OperationConflict));
Assert.That(applyCount, Is.Zero);
// Admin_actor_is_not_loaded_as_participant
Assert.That(loadedKeys, Is.EqualTo(new[] { "player:a" }));
```

- [x] **Step 2:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter "FullyQualifiedName~PlayerOperationActorTests|FullyQualifiedName~PlayerOperationRpcTests"`로 RED를 기록한다. 신규 signature가 필요한 경우 먼저 동작 없는 최소 선언을 추가한 뒤 의미 있는 assertion 실패를 확인한다.
- [x] **Step 3:** 기존 string 생성자는 Player 종류로 위임하고 ActorKey getter를 호환 별칭으로 유지한다. ParticipantKeys는 관리자 작업에서도 비어 있을 수 없다. receipt identity와 진단 필드에 Actor.Kind/Key를 포함하고, RPC는 Player 종류와 인증된 키 일치를 모두 검사한다. 기존 receipt는 어댑터에서 누락된 종류를 Player로 복원한다. SQLite 테스트 스키마에 actor_kind를 추가해 새 정보가 round-trip되는지 확인한다.
- [x] **Step 4:** 위 명령과 `dotnet test server/tests/Bun3.Server.Tests -c Release --filter "FullyQualifiedName~PlayerOperationTests|FullyQualifiedName~PlayerStoreSqliteTests"`를 실행한다. 모든 선택 테스트 통과와 기존 생성자의 소스 호환을 확인한다.
- [x] **Step 5:** Task diff와 테스트 결과를 기록하고 다음 Task 전에 스펙/품질 리뷰를 완료한다.

### Task 2: DB 수정 권한의 상태 전환과 저장 보호

**Files:** Create `P/PlayerWriteAuthority.cs`, `P/PlayerWriteAuthorityStore.cs`, `P/PlayerWriteAuthorityRecord.cs`, `P/PlayerWriteBinding.cs`, `P/PlayerWriteTransition.cs`, `P/PlayerWriteAuthorityException.cs`, `T/PlayerWriteAuthorityTests.cs`, `T/Helpers/AuthorityStoreHarness.cs`.

**Interfaces:** Consumes BCL DbConnection/DbTransaction과 ILogger. Produces 상태·binding·전환 결과 및 아래 coordinator/어댑터 계약. 열거형과 긴밀한 값 타입은 위 대응 파일에 함께 둔다.

```csharp
public enum PlayerWriteState { Offline, Starting, Online, Draining, RecoveryRequired }
public enum PlayerWriteRole { Server, OfflineAdministrator }
public enum PlayerWriteLock { Shared, Exclusive }
public enum PlayerWriteTransitionOutcome { Committed, Conflict, Unknown }
public readonly struct PlayerWriteBinding // Role, InstanceId 읽기 전용. Server만 nonempty Guid.
{
    public PlayerWriteBinding(PlayerWriteRole role, Guid instanceId);
}
public sealed class PlayerWriteAuthority
{
    public PlayerWriteAuthority(string scopeKey, PlayerWriteAuthorityStore store, ILogger? logger = null);
    public string ScopeKey { get; }
    public ValueTask<PlayerWriteAuthorityRecord> ReadAsync(CancellationToken ct = default);
    internal ValueTask<PlayerWriteTransitionResult> TransitionAsync(PlayerWriteTransition transition, CancellationToken ct = default);
    internal ValueTask ProtectAsync(DbConnection db, DbTransaction tx,
        PlayerWriteBinding binding, PlayerStoreAccess access, CancellationToken ct);
}
// PlayerWriteAuthorityStore: protected abstract mapping hooks, internal wrappers로 coordinator에서 호출.
protected abstract DbConnection CreateConnection();
protected abstract ValueTask<PlayerWriteAuthorityRecord?> LockAsync(DbConnection db, DbTransaction tx,
    string scopeKey, PlayerWriteLock mode, CancellationToken ct);
protected abstract ValueTask WriteAsync(DbConnection db, DbTransaction tx, string scopeKey,
    PlayerWriteAuthorityRecord record, PlayerWriteTransition transition, CancellationToken ct);
protected abstract ValueTask VerifyRecoveryAsync(DbConnection db, DbTransaction tx, string scopeKey, CancellationToken ct);
```

`PlayerStoreAccess`는 internal enum `Load, Save, Operation, Recovery`. Record는 불변 `State, Guid? InstanceId, Guid TransitionId, ReadOnlyMemory<byte> TransitionFingerprint`. Transition은 불변 `Id, ExpectedState, ExpectedInstanceId, NextState, NextInstanceId, OperatorKey, Reason`; 생성자는 이 순서로 받는다. fingerprint는 범위 키와 위 필드의 길이 구분된 정규 표현에 대한 SHA-256이며 프레임워크가 계산한다. 어댑터는 제어 행과 함께 그 32 bytes를 보관한다. Result는 `Outcome, Record, Exception? Error`. 전환 요청/Record의 ID는 nonempty이며 DB 제어 행 초기화는 게임의 신규 설치·마이그레이션 단계에서만 수행한다. 제어 행 부재는 오류다.

- [x] **Step 1:** `Only_one_start_claim_commits`, `Heartbeat_failure_never_changes_authority`, `Transition_retry_preserves_later_owner`, `Transition_id_reuse_with_changed_reason_conflicts`, `Missing_control_row_denies_access`, `Shared_guard_uses_the_supplied_transaction`, `Failed_audit_rolls_back_transition`를 작성한다. 테스트 fake는 동일 tx 참조, 호출 순서와 장애 분류를 관찰하며 실제 DB 잠금 보장은 Task 6에서 검증한다.

```csharp
// Transition_retry_preserves_later_owner
Assert.That(retry.Outcome, Is.EqualTo(PlayerWriteTransitionOutcome.Conflict));
Assert.That((await authority.ReadAsync()).InstanceId, Is.EqualTo(newInstanceId));
// Shared_guard_uses_the_supplied_transaction
Assert.That(observedTransaction, Is.SameAs(playerTransaction));
```

- [x] **Step 2:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter FullyQualifiedName~PlayerWriteAuthorityTests`로 의미 있는 RED를 기록한다.
- [x] **Step 3:** coordinator가 전환용 연결/tx 수명을 소유한다. 전환은 배타 잠금 아래 expected 상태·owner를 비교하고 제어 행과 감사 기록을 같이 저장한다. 정상 허용 edge는 Offline→Starting(new ID)→Online(same ID)→Draining(same ID)→Offline(null)이다. Starting/Draining→RecoveryRequired, 운영자·사유가 있는 Online→RecoveryRequired 및 RecoveryRequired→Offline도 허용한다. 잘못된 edge/owner/기본 binding은 변경 없이 거절한다.
- [x] **Step 4:** 저장 보호는 caller가 준 tx에서 Shared로 읽고 역할·상태를 검증한다. Server는 자신의 Online에서 전 access, Draining에서 Save/Recovery 및 진입 완료된 Operation, Starting에서 Load만 허용한다. OfflineAdministrator는 Offline에서 Operation/Recovery만 허용한다. Draining의 신규 실행 차단은 Task 5의 로컬 진입 gate와 결합한다. 이 보호가 끝나도 DB 잠금은 caller의 tx 종료까지 유지되어야 한다.
- [x] **Step 5:** 같은 전환 ID+같은 내용 재요청은 이미 확정된 결과를 사용한다. 더 새로운 전환이 있으면 Conflict이며 되돌리지 않는다. 커밋 호출 이후 통신 실패는 Unknown; 재조회로 소유 상태를 확인한다. 별도 연결의 advisory lock 또는 메모리 캐시 조회만으로 쓰기를 허용하지 않는다. 위 테스트를 GREEN으로 만들고 작업별 diff·리뷰를 기록한다.

### Task 3: 공통 저장 수명과 version 없는 저장 타입

**Files:** Create `P/PlayerPersistence{TPlayer}.cs`, `P/OwnedPlayerStore{TPlayer}.cs`, `P/PlayerStoreAnchor.cs`, `P/PlayerRegistry.Authority.cs`, `T/OwnedPlayerStoreTests.cs`, `T/Helpers/OwnedStoreHarness.cs`. Modify `P/PlayerStore{TPlayer}.cs`, `P/PlayerStoreScope{TPlayer}.cs`, `P/PlayerRegistry.cs`, `P/PlayerRegistry.Persistence.cs`, `P/PlayerRegistry.Operations.cs`, `P/PlayerRegistry.Lifecycle.cs`, `P/PlayerRegistry.Invalidation.cs`, `P/PlayerSaveData.cs`, `P/PlayerOperationDiagnostics.cs`.

**Interfaces:** Consumes Task 2 `ProtectAsync`와 기존 키별 실행 권한. Produces 아래 저장 계층. 현재 `PlayerStore<TPlayer>`의 protected mapping signature는 유지한다.

```csharp
// 공통 연결/tx 수명, Load/Capture/receipt/audit hooks를 이 base로 이동.
public abstract class PlayerPersistence<TPlayer> where TPlayer : Player
{
    private protected PlayerPersistence(IsolationLevel isolationLevel);
    internal abstract ValueTask<PlayerStoreAnchor> LockAnchorAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct);
    internal abstract ValueTask WriteChangesAsync(DbConnection db, DbTransaction tx, PlayerSaveData data, CancellationToken ct);
    internal abstract ValueTask ProtectAsync(DbConnection db, DbTransaction tx, PlayerStoreAccess access, CancellationToken ct);
}
public abstract class PlayerStore<TPlayer> : PlayerPersistence<TPlayer> where TPlayer : Player
// 기존 ctor, LockVersionAsync, version을 받는 WritePlayerAsync 그대로 유지.
public abstract class OwnedPlayerStore<TPlayer> : PlayerPersistence<TPlayer> where TPlayer : Player
{
    protected OwnedPlayerStore(PlayerWriteAuthority authority, PlayerWriteBinding binding,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted);
    public PlayerWriteAuthority Authority { get; }
    public PlayerWriteBinding Binding { get; }
    protected abstract ValueTask<bool> LockPlayerAsync(DbConnection db, DbTransaction tx, string key, CancellationToken ct);
    protected abstract ValueTask WritePlayerAsync(DbConnection db, DbTransaction tx, string key, object changes, CancellationToken ct);
}
// PlayerRegistry: 기존 PlayerStore constructor를 이 overload로 위임.
public PlayerRegistry(PlayerPersistence<TPlayer> store, PlayersOptions? options = null, ILogger? logger = null);
```

base로 이동하는 기존 protected abstract hooks는 `CreateConnection`, `LoadPlayerAsync`, `CaptureChanges`, `ReadOperationAsync`, `WriteOperationAsync`, `WriteAuditAsync`다. 기존 인자·반환형은 변경하지 않는다. 내부 `OpenScopeAsync`에 PlayerStoreAccess 인자를 추가한다. `PlayerStoreAnchor`는 internal readonly struct로 `bool Exists`, `long? Version`을 가지며 Missing/Versioned/Unversioned 생성을 구분한다. Entry와 PlayerSaveData의 영속 version은 nullable로 바꾸고 메모리 Dirty 세대는 그대로 유지한다.

- [x] **Step 1:** `Save_without_version_column_succeeds`, `Missing_or_foreign_anchor_never_loads_or_applies`, `Unknown_commit_reload_still_waits_for_anchor`, `Authority_loss_invalidates_after_releasing_current_lane`, `Existing_versioned_adapter_detects_stale_write`를 작성한다. 부재/다른 범위는 load와 Apply 0회, 권한 상실은 gate 닫힘·캐시 제거·후속 save 0회, 정상 저장은 version SQL/증가 호출 0회를 검증한다.

```csharp
// Missing_or_foreign_anchor_never_loads_or_applies
Assert.That(loadCount, Is.Zero);
Assert.That(applyCount, Is.Zero);
// Authority_loss_invalidates_after_releasing_current_lane
await completedCleanup.WaitAsync(TimeSpan.FromSeconds(5));
Assert.That(registry.TryGet("player:a"), Is.Null);
Assert.That(writesAfterAuthorityLoss, Is.Zero);
```

- [x] **Step 2:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter "FullyQualifiedName~OwnedPlayerStoreTests|FullyQualifiedName~PlayerStoreTests"`로 RED를 기록한다.
- [x] **Step 3:** 공통 tx 실행을 base로 옮기고 모든 store scope가 권한 보호 → 정렬된 기준 행 잠금 순서를 따르게 한다. version 방식은 비교·증가를 유지하고 owned 방식은 존재 여부만 사용한다. Owned 어댑터의 기준 행 잠금은 Authority.ScopeKey 안의 플레이어임을 확인해야 한다. 성공 여부는 콜백 반환이 아닌 실제 commit으로 판단한다.
- [x] **Step 4:** 온라인 작업은 기존 캐시, offline binding 작업은 항상 임시 객체를 사용한다. offline binding registry의 SignIn/Wrap/ticker 등록을 거절하고 grace sweep을 시작하지 않는다. 권한 상실은 즉시 새 진입을 닫되, 현재 lane을 해제한 뒤 전체 캐시를 저장 없이 무효화한다. 다른 lane을 가진 callback 안에서 전체 drain을 기다리지 않는다.
- [x] **Step 5:** 테스트를 GREEN으로 만들고 기존 `PlayerOperationRecoveryTests`, `PlayerStoreSqliteTests`, `PlayerExecutionAllocationTests`를 실행한다. rollback/unknown 복구의 기준 행 잠금, dirty 완료 시점, nullable version 진단을 확인하고 diff·리뷰를 기록한다.

### Task 4: 영속 명령 접수와 처리 결과의 공동 커밋

**Files:** Create `P/PlayerCommandEnvelope.cs`, `P/PlayerCommandRecord.cs`, `P/PlayerCommandStore.cs`, `P/PlayerCommandQueue.cs`, `P/PlayerCommandCatalog{TPlayer}.cs`, `P/PlayerCommandDispatcher{TPlayer}.cs`, `P/PlayerRegistry.Commands.cs`, `P/PlayerCommandExecution.cs`, `T/PlayerCommandTests.cs`, `T/PlayerCommandRecoveryTests.cs`, `T/Helpers/CommandStoreHarness.cs`. Modify `P/PlayerRegistry.Operations.cs`, `P/PlayerStoreScope{TPlayer}.cs`.

**Interfaces:** Consumes Task 1 Actor/receipt, Task 3 registry/store scope. Produces immutable envelope/record, mapping hooks, catalog와 dispatcher. Envelope 생성자 순서는 `(Guid id, string scopeKey, PlayerOperationActor actor, string kind, int inputSchemaVersion, IReadOnlyList<string> participantKeys, ReadOnlyMemory<byte> inputFingerprint, ReadOnlyMemory<byte> inputPayload)`로 한다. 키는 ordinal 정렬·중복 제거, ID/종류/범위/입력 스키마는 유효값, fingerprint는 기존 작업과 같은 32 bytes, actor는 Administrator다. 입력 배열을 복사한다.

Envelope는 생성자 인자와 대응하는 PascalCase getter를 제공한다. Record는 `PlayerCommandEnvelope Envelope, DateTimeOffset AcceptedAtUtc, PlayerCommandStatus Status, int StatusCode, int ResultSchemaVersion, ReadOnlyMemory<byte> ResultPayload`를 생성자로 받고 복사한다. PlayerCommandStatus enum은 `Pending, Committed, Rejected, Failed, Unknown`; Unknown은 커밋 불명 응답용이며 DB의 확정 상태로 쓰지 않는다. 결과 없음은 `GetAsync`의 null로 나타내고 같은 ID에 다른 내용은 `PlayerCommandConflictException`으로 거절한다(Record 파일에 함께 정의).

```csharp
public sealed class PlayerCommandQueue
{
    public PlayerCommandQueue(PlayerCommandStore store, ILogger? logger = null);
    public ValueTask<PlayerCommandRecord> SubmitAsync(PlayerCommandEnvelope command, CancellationToken ct = default);
    public ValueTask<PlayerCommandRecord?> GetAsync(Guid commandId, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<Guid>> FindPendingAsync(string scopeKey, int limit, CancellationToken ct = default);
    public ValueTask<IReadOnlyList<Guid>> FindPendingAsync(string scopeKey, int limit,
        IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct = default);
}
public sealed class PlayerCommandCatalog<TPlayer> where TPlayer : Player
{
    public void Register<TResult>(string kind, int inputSchemaVersion, bool supportsOffline,
        Func<PlayerCommandEnvelope, PlayerOperation<TPlayer, TResult>> factory) where TResult : class;
    public IReadOnlyList<(string Kind, int InputSchemaVersion)> GetSupportedInputs(bool offline);
}
public sealed class PlayerCommandDispatcher<TPlayer> where TPlayer : Player
{
    public PlayerCommandDispatcher(PlayerCommandQueue queue, PlayerCommandCatalog<TPlayer> catalog,
        PlayerRegistry<TPlayer> registry);
    public ValueTask<PlayerCommandRecord?> ExecuteAsync(Guid commandId, CancellationToken ct = default);
}
// PlayerCommandStore: protected abstract hooks + internal wrappers.
protected abstract DbConnection CreateConnection();
protected abstract ValueTask<PlayerCommandRecord> InsertOrReadAsync(DbConnection db, DbTransaction tx, PlayerCommandEnvelope command, CancellationToken ct);
protected abstract ValueTask<PlayerCommandRecord?> ReadAsync(DbConnection db, DbTransaction tx, Guid id, bool forUpdate, CancellationToken ct);
protected abstract ValueTask<IReadOnlyList<Guid>> FindPendingAsync(DbConnection db, DbTransaction tx, string scopeKey, int limit,
    IReadOnlyList<(string Kind, int InputSchemaVersion)>? supportedInputs, CancellationToken ct);
protected abstract ValueTask CompleteAsync(DbConnection db, DbTransaction tx, PlayerCommandRecord record, CancellationToken ct);
// PlayerRegistry.Commands.cs: reuse the execution path shared with public ExecuteAsync.
internal ValueTask<PlayerCommandRecord?> ExecuteCommandAsync<TResult>(PlayerOperation<TPlayer, TResult> operation,
    PlayerCommandEnvelope envelope, PlayerCommandStore commands, CancellationToken ct) where TResult : class;
```

- [x] **Step 1:** `Submission_copies_mutable_input`(접수 후 원본 bytes/keys 수정에도 저장된 입력 동일), `Duplicate_id_with_different_actor_scope_or_payload_conflicts`(접수표 한 행, Apply 0회), `Admin_actor_is_not_a_loaded_player`, `Factory_cannot_change_accepted_identity`, `Offline_unsupported_command_remains_pending` 테스트를 작성한다. 미등록 종류·입력 스키마도 Pending으로 유지하고 실행 가능한 배포를 기다린다.

```csharp
// Submission_copies_mutable_input
Assert.That(saved.Envelope.InputPayload.ToArray(), Is.EqualTo(acceptedBytes));
// Failure_recording_observes_competing_terminal_result
Assert.That(finalRecord.Status, Is.EqualTo(PlayerCommandStatus.Committed));
Assert.That(committedGrantCount, Is.EqualTo(1));
```

- [x] **Step 2:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter "FullyQualifiedName~PlayerCommandTests|FullyQualifiedName~PlayerCommandRecoveryTests"`로 RED를 기록한다.
- [x] **Step 3:** Queue가 접수·조회용 tx를 소유하고 어댑터는 SQL만 제공한다. 중복 접수는 모든 불변 필드와 payload까지 비교한다. Catalog는 종류+입력 스키마를 key로 중복 등록을 거절하고 dispatcher 생성 시 고정한다. Factory는 입력 복원만 수행하고 기존 상태를 읽지 않는다. 반환한 operation의 Id/Actor/Kind/키/fingerprint가 접수 정보와 다른 경우 Apply 전에 거절한다. dispatcher는 owned store와 동일한 소유 범위에 대해서만 실행한다.
- [x] **Step 4:** 기존 작업 실행기에 optional 내부 PlayerCommandExecution 문맥을 연결한다. 후보 조회 → local 키 권한 → store scope의 제어 행 Shared → 키순 기준 행 잠금 → 명령 행 잠금/재조회 순서다. 기존 ExecuteAsync 호출은 command 문맥 없이 같은 경로를 사용한다. command store의 쓰기에는 player scope의 동일 db/tx만 전달한다. 성공 receipt와 명령 Committed 표시를 저장한 뒤 commit하며 그 전에는 응답·알림을 성공으로 보내지 않는다. 게임 규칙 거절은 player 쓰기 없이 명령 Rejected와 사유를 기록하고 해당 tx를 명시적으로 commit한다.
- [x] **Step 5:** `Success_receipt_command_and_players_commit_together`(세 저장 중 하나 실패 시 모두 rollback), `Two_workers_apply_a_command_once`, `Commit_ack_loss_replays_stored_result`, `Failure_recording_observes_competing_terminal_result`, `Canceled_before_apply_leaves_pending`, `Cancellation_after_apply_does_not_abandon_persistence`를 추가해 RED→GREEN을 기록한다. 결과 불명은 캐시 폐기 후 같은 DB 잠금을 얻어 조회한다. 확정 rollback의 실패 표시도 새 tx에서 같은 잠금 순서와 terminal 결과 검사를 거친다. 기존 DB scope를 dispose한 후 보유 중인 local 키 권한을 유지한 채 새 DB scope만 열며, 같은 lane에서 키 권한을 중복 획득하지 않는다. 다른 프로세스의 worker가 먼저 완료했다면 기존 결과를 사용한다. 결과 확인·실패 기록이 끝날 때까지 바깥 command 실행의 admission 카운트를 유지한다.
- [x] **Step 6:** 위 필터와 기존 `PlayerOperationNotificationTests`, `PlayerOperationAuditTests`, `PlayerOperationRecoveryTests`를 GREEN으로 확인하고 diff·리뷰를 기록한다. 개인정보 payload를 진단 로그에 출력하지 않고 CommandId·actor·scope·단계·DB 결과만 연결한다.

### Task 5: 전체 진입 차단, 기동·점검 인계와 Hosting

**Files:** Create `P/PlayerAdmissionGate.cs`, `P/PlayerDrainResult.cs`, `P/PlayerRegistry.Shutdown.cs`, `P/PlayerServerLifecycle{TPlayer}.cs`, `P/PlayerWriteRecoveryConfirmation.cs`, `H/PlayerCommandServiceCollectionExtensions.cs`, `H/PlayerCommandWorker{TPlayer}.cs`, `H/PlayerCommandWorkerOptions.cs`, `H/PlayersLifetimeService{TSession,TPlayer,TRequest,TResponse,TUpdate}.cs`, `T/PlayerWriteLifecycleTests.cs`, `T/PlayerCommandHostingTests.cs`. Modify `P/PlayerWriteAuthority.cs`, `P/PlayerRegistry.cs`, `P/PlayerRegistry.Operations.cs`, `P/PlayerRegistry.Lifecycle.cs`, `P/PlayerRegistry.Persistence.cs`, `P/PlayerRegistry.Authority.cs`, `P/PlayerSession.cs`, `P/PlayerTicker.cs`, `H/PlayersServiceCollectionExtensions.cs`, `T/PlayerExecutionAllocationTests.cs`.

**Interfaces:** Consumes Tasks 2–4. Produces coordinator와 drain 결과, 온라인/오프라인 worker 등록. 별도 public raw 상태 토글은 만들지 않는다.

```csharp
// PlayerRegistry; 기존 RetireAllAsync는 이 경로로 위임하고 결과를 로그에 남긴다.
public ValueTask<PlayerDrainResult> DrainAsync(CancellationToken ct = default);
// PlayerDrainResult: IsQuiescent, UnresolvedKeys, DiscardedKeys, Errors 읽기 전용.
// 같은 파일의 PlayerServerShutdownResult: Drain, Transition, OfflineConfirmed 읽기 전용.
public sealed class PlayerServerLifecycle<TPlayer> where TPlayer : Player
{
    public PlayerServerLifecycle(PlayerRegistry<TPlayer> registry); // owned Server binding 필수
    public ValueTask<PlayerWriteTransitionResult> PrepareAsync(CancellationToken ct = default);
    public ValueTask<PlayerWriteTransitionResult> ActivateAsync(CancellationToken ct = default);
    public ValueTask<PlayerWriteTransitionResult> BeginDrainAsync(CancellationToken ct = default);
    public ValueTask<PlayerServerShutdownResult> CompleteDrainAsync(CancellationToken ct = default);
}
// PlayerWriteAuthority: 수동 복구만 노출. 이전 프로세스 종료·자동 재기동 차단 확인은 운영자 책임.
public ValueTask<PlayerWriteTransitionResult> RecoverAsync(PlayerWriteRecoveryConfirmation confirmation, CancellationToken ct = default);
// Confirmation: Id, ExpectedInstanceId, OperatorKey, Reason을 생성자로 받고 불변 보관.
// Hosting extensions:
public static IServiceCollection AddPlayerCommands<TPlayer>(this IServiceCollection services,
    Func<IServiceProvider, PlayerCommandStore> store, Action<PlayerCommandCatalog<TPlayer>> commands,
    Action<PlayerCommandWorkerOptions>? options = null) where TPlayer : Player;
public static IServiceCollection AddOfflinePlayerCommands<TPlayer>(this IServiceCollection services,
    Func<IServiceProvider, OwnedPlayerStore<TPlayer>> players, Func<IServiceProvider, PlayerCommandStore> commands,
    Action<PlayerCommandCatalog<TPlayer>> configure, Action<PlayerCommandWorkerOptions>? options = null) where TPlayer : Player;
```

PlayerAdmissionGate는 registry별 한 객체와 readonly 진입 scope를 사용한다. 요청·posted work·로그인·작업·detach/sweep 진입의 active 수를 추적하며 종료 관리 작업에는 별도 내부 정리 진입을 사용한다. 재진입은 기존 lane 계약을 지키고 같은 task가 자신을 drain하지 않는다. WorkerOptions는 PollInterval=1초, BatchSize=32, MaxConcurrency=4이며 각각 양수만 허용한다. queue의 글로벌 FIFO는 보장하지 않는다.

- [x] **Step 1:** `Maintenance_flag_alone_does_not_allow_offline_write`, `Drain_waits_for_handler_posted_work_and_offline_operation`, `Detached_save_cannot_start_after_final_drain`, `Unknown_save_prevents_offline_publication`, `Release_hook_failure_prevents_offline_publication`, `Offline_ack_loss_does_not_reopen_old_registry`를 작성한다. Barrier로 진행 중 작업을 멈춘 동안 Offline이 아니고 직접 쓰기 0회임을 확인한다. 미실행 post는 폐기하고 이미 진입한 작업만 정리한다.

```csharp
// Unknown_save_prevents_offline_publication
Assert.That(shutdown.OfflineConfirmed, Is.False);
Assert.That((await authority.ReadAsync()).State, Is.Not.EqualTo(PlayerWriteState.Offline));
// Release_hook_failure_prevents_offline_publication
Assert.That(shutdown.Drain.IsQuiescent, Is.False);
Assert.That(shutdown.Drain.Errors, Is.Not.Empty);
```

- [x] **Step 2:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter "FullyQualifiedName~PlayerWriteLifecycleTests|FullyQualifiedName~PlayerCommandHostingTests"`로 RED를 기록한다.
- [x] **Step 3:** owned Server registry는 Prepare/Activate 전 일반 진입이 닫혀 있어야 한다. Prepare는 Offline→Starting, Activate는 자신의 Starting→Online 확정 후 로컬 진입을 연다. BeginDrain은 모든 새 변경과 command 실행 진입을 먼저 닫고 Online→Draining을 수행한다. 기존 loader/versioned registry는 현행 시작 동작을 유지한다. public StopAcceptingOperations도 전체 변경 진입 차단으로 확장하며 이미 Apply한 작업은 끝까지 저장한다.
- [x] **Step 4:** Drain은 입장한 작업/요청이 끝난 뒤 cached player들을 저장·폐기하고 sweep/detach/cleanup까지 종료를 집계한다. 모든 저장 결과를 보고하므로 현재 SaveAsync가 예외를 삼켜 반환하는 것을 성공으로 보지 않는다. rollback 확정은 DiscardedKeys와 진단 로그에 남기고 정리 가능, Unknown은 동일 기준 행 보호와 DB 확정 상태 확인까지 UnresolvedKeys에 남긴다. Errors에는 callback/cleanup 실패처럼 종료 완료를 증명하지 못하게 하는 오류를 넣는다. IsQuiescent는 active writer 0·캐시 0·미종료 cleanup 0·unresolved 0·Errors 0인 경우만 true다.
- [x] **Step 5:** CompleteDrain은 IsQuiescent 확인 후에만 제어 행을 Exclusive로 보호해 Offline을 발행한다. ShutdownResult는 로컬 Drain 결과와 nullable Transition 결과를 구분하며 OfflineConfirmed는 해당 전환의 커밋을 확인한 경우에만 true다. 실패/취소는 이전 Draining 상태 또는 RecoveryRequired를 유지하며 cache를 재활성화하지 않는다. 응답 유실로 실제 전환만 완료된 경우에는 같은 ID의 DB 확인까지 Unknown이다. RecoverAsync는 expected old instance와 운영자·사유를 검사하고 Exclusive 장벽 뒤 Store.VerifyRecoveryAsync로 명령/receipt 일관성을 확인한다. 확인 실패 시 Offline 금지. 정상 전환 API와 분리한 복구 tx에서 RecoveryRequired 감사와 Offline 전환을 함께 확정할 수 있다. heartbeat에서 이 API를 호출하는 경로는 만들지 않는다.
- [x] **Step 6:** 기존 Hosting 파일의 lifetime 클래스를 새 파일로 분리한다. 새 PlayerPersistence factory overload를 추가하고 기존 PlayerStore factory overload는 위임해 호환성을 유지한다. owned 서버의 시작은 Prepare → 초기화 → Activate → transport/tick/command worker 시작, 종료는 command 신규 실행 차단 → BeginDrain → tick/transport 종료 → worker 완료 대기 → CompleteDrain 순서다. Online 이후 listener 시작 실패도 동일 정리 경로로 회수한다. 취소·실패를 catch/finally에서 무조건 Offline으로 바꾸지 않는다.
- [x] **Step 7:** 온라인 AddPlayerCommands는 lifetime이 시작/정지하는 worker를 등록하고, AddOfflinePlayerCommands는 세션·listener·ticker 없는 임시 registry와 자체 hosted worker를 등록한다. OfflineAdministrator binding을 강제한다. 역할 불일치는 시작 실패이며 실행 시 scope를 재검사한다. 물리 DB 매핑 일치는 아래 구현 조정에 따라 배포 검증한다. Offline worker는 전환 상태에서 Pending을 유지하고, 이미 진행 중인 DB 작업은 기동 전환의 Exclusive 잠금보다 먼저 끝난다. DB 불통 시 연결 문자열을 로그에 쓰지 않고 polling을 중단 또는 제한하여 재시도한다.
- [x] **Step 8:** `Second_server_cannot_activate`, `Restart_loads_admin_committed_state`, `Offline_worker_never_attaches_or_ticks`, `New_request_after_gate_close_does_not_enter_handler`, `Command_worker_stops_before_offline_publication`를 추가 검증한다. 위 필터와 기존 PlayersHosting/PlayerOperationHosting/PlayerExecution/PlayerTicking 테스트 및 할당 테스트를 GREEN으로 확인하고 diff·리뷰를 기록한다.

### Task 6: PostgreSQL 통합 검증과 사용 문서

**Files:** Create `T/PostgresAuthorityTests.cs`, `T/PostgresCommandTests.cs`, `T/Helpers/PostgresAuthorityFixture.cs`, `T/Helpers/PostgresOwnedPlayerStore.cs`, `T/Helpers/PostgresCommandStore.cs`, `T/run-postgres-tests.ps1`, `docs/player-write-authority.md`. Modify `T/Bun3.Server.Tests.csproj`, `P/Bun3.Server.Players.csproj`, `H/Bun3.Server.Hosting.csproj`, `docs/player-operations-and-persistence.md`, `docs/superpowers/specs/2026-10-06-player-operations-and-persistence-design.md`, `docs/superpowers/specs/2026-10-08-player-write-authority-design.md`.

**Interfaces:** Consumes 앞선 모든 public API. Produces 테스트 전용 PostgreSQL 어댑터와 재현 가능한 실행 스크립트. 제품 DB 제공자 패키지는 추가하지 않는다. 테스트 의존성은 `<PackageReference Include="Npgsql" Version="[10.0.3]" PrivateAssets="all" />`로 고정한다. [Npgsql 공식 패키지](https://www.nuget.org/packages/Npgsql/10.0.3)는 net10.0을 지원한다. 이 버전은 계획 작성 시 확인한 값이며 실행 중 임의 업데이트하지 않는다.

- [x] **Step 1:** 테스트용 테이블 `write_authority(scope_key,state,instance_id,transition_id,transition_fingerprint)`, `players(scope_key,key,gold)`, commands/operations/audits를 구성한다. players에는 version을 만들지 않는다. OwnedPlayerStore는 제어 행 Shared → scope와 key 조건의 플레이어 행 FOR UPDATE, authority 전환은 제어 행 FOR UPDATE로 매핑한다. VerifyRecoveryAsync는 해당 scope의 command/receipt identity·terminal 상태 불일치를 발견하면 실패한다. 실제 row lock 동작의 근거는 [PostgreSQL 18 공식 문서](https://www.postgresql.org/docs/18/explicit-locking.html#LOCKING-ROWS)다.
- [x] **Step 2:** NUnit Category `PostgreSQL`로 `Startup_waits_for_offline_transaction`, `Offline_transaction_denied_after_start_claim`, `Unrelated_players_save_concurrently`, `Two_starting_instances_have_one_winner`, `Old_instance_write_after_restart_is_rejected`, `Unknown_commit_resolution_waits_for_previous_writer`, `Two_offline_workers_commit_one_effect`, `Player_receipt_command_audit_are_atomic`, `Control_row_cannot_change_between_guard_and_commit`를 작성한다. 서로 다른 연결과 barrier를 사용하고 연결별 tx/lock 진입 이벤트를 관찰한다. 임의 sleep 시간만으로 배타성을 판단하지 않는다. 커밋 성공 후 응답 실패는 실제 Npgsql transaction을 감싸는 테스트 decorator로 주입한다.

```csharp
// Two_offline_workers_commit_one_effect: initial gold 100, one accepted grant of 7.
Assert.That(await ReadGoldAsync("player:a"), Is.EqualTo(107));
Assert.That(await ReadReceiptCountAsync(commandId), Is.EqualTo(1));
// Unrelated_players_save_concurrently: both guards acquired before either is released.
await Task.WhenAll(firstGuardEntered, secondGuardEntered).WaitAsync(TimeSpan.FromSeconds(5));
```

- [x] **Step 3:** 실행 스크립트는 이 작업만의 Docker PostgreSQL 18 컨테이너를 loopback의 임의 포트에 시작하고 `BUN3_POSTGRES_TEST_CONNECTION`을 테스트 프로세스에만 전달한다. 테스트 DB명은 `bun3_authority_tests`, 데이터는 임시로 두고 컨테이너 ID/label을 보관해 자신이 만든 컨테이너만 finally에서 정리한다. 기존 컨테이너나 운영 접속 설정을 사용하지 않는다. 이미지 digest와 서버 버전을 결과에 기록한다. 테스트 fixture는 환경변수가 없으면 명시적으로 skip하며, 스크립트는 DB 준비 실패·0개 실행·선택 테스트 skip을 성공으로 취급하지 않는다.
- [ ] **Step 4:** `powershell -NoProfile -File server/tests/Bun3.Server.Tests/run-postgres-tests.ps1`로 RED→GREEN을 확인한다. 스크립트 내부 테스트 명령은 `dotnet test server/tests/Bun3.Server.Tests -c Release --filter TestCategory=PostgreSQL --logger trx`다. Docker CLI만 존재한다고 engine/이미지/DB 준비 완료를 주장하지 않는다. 환경 문제로 실행하지 못하면 해당 검증을 미완료로 남기며 fake/SQLite 통과로 대체하지 않는다.
- [x] **Step 5:** 문서에 제어 행 초기 설치, 기존 version 어댑터 유지 예제, OwnedPlayerStore 매핑, Admin 명령 접수/상태 조회, 온라인/종료 중 같은 지급 명령, 점검·재개, 비정상 종료 후 운영자 확인 복구를 작성한다. receipt actor_kind 이행과 모든 writer의 동시 전환 조건을 설명한다. 기존 2026-10-06 스펙에는 이 추가 설계로 확장되었다는 링크를 넣고 기존 검증 기록을 덮어쓰지 않는다. Players/Hosting 버전을 지정값으로 올린다.
- [x] **Step 6:** `dotnet test server/tests/Bun3.Server.Tests -c Release --filter TestCategory!=PostgreSQL`와 `dotnet build Bun3.sln -c Release -warnaserror`를 실행한다. 선택된 일반 테스트 전부 통과, 실제 PostgreSQL 테스트 전부 통과, 전체 빌드 경고·오류 0을 각각 확인한다. 변경 후 우려가 없는 동일 검증을 반복하지 않는다.
- [x] **Step 7:** 이번 baseline 이후 diff 전체를 리뷰해 권한 우회 저장, 기존 mode 회귀, 핫패스 할당, 중복 명령, 종료 중 재저장을 확인한다. 결과와 남은 환경 제약을 기록하고 구현 상태·사용 문서를 실제 검증 결과에 맞춰 갱신한다. 커밋·배포·외부 게임 적용은 하지 않는다.

## 스펙 대응과 실행 인계

| 승인된 요구 | 구현·검증 작업 |
|---|---|
| 버전 저장 유지 + version 없는 저장 | Task 3, Task 6 |
| 명시적 종료·점검만 직접 수정 허용 | Task 2, Task 5 |
| 기동 경쟁·이전 인스턴스의 지연 저장 차단 | Task 2, Task 3, Task 6 |
| 온라인·미접속에 같은 게임 규칙 적용 | Task 3, Task 4 |
| Admin 요청자와 대상 구분 | Task 1, Task 4 |
| 영속 접수·동일 ID 재요청·공동 commit | Task 4, Task 6 |
| DB 실패 시 메모리 폐기·결과 불명 확인 | Task 3, Task 4, Task 5 |
| 작업·writer·cleanup 종료 확인 | Task 5 |
| 진단/감사 기록, mapper 경계, 실제 DB 잠금 | Task 2, Task 4, Task 6 |
| Items/Achievements/loader 방식 유지 | Task 3 회귀, Task 6 전체 검증 |

이 계획은 새 저장 방식과 명령 처리를 여섯 리뷰 단위로 나눈다. 구현 과정의 조정은 아래에 기록한다. 커밋·배포·외부 게임 적용은 수행하지 않았다.

## 구현 조정과 검증 한계

- 작업자 실행 한도로 작업별/최종 독립 리뷰를 자체 리뷰로 대체했다. 기존 변경을 보존하기 위해 커밋 대신 baseline과 작업별 snapshot/diff를 남겼다.
- Docker backend가 `hasNoVirtualization=true`를 보고해 실제 컨테이너 검증은 미완료다. 공식 EDB PostgreSQL 18.6 바이너리, SCRAM 인증, 임시 loopback DB에서 동일한 경쟁 실행 테스트를 수행했다. 시스템 서비스나 설정은 변경하지 않았다.
- Task 5의 시작 시 매핑 일치 검증은 역할/범위 계약에 한정한다. 제공자에 독립적인 연결 팩토리만으로 물리 DB 동일성을 증명할 수 없으므로 Queue와 PlayerStore의 같은 DB 매핑은 배포 검증에 포함한다. 실제 명령은 플레이어 transaction에서 접수 행을 다시 확인한 뒤에만 Apply한다.
- 후보 조회에 등록된 종류·입력 버전 필터를 추가했다. 미지원 Pending이 배치 제한을 소진하지 않도록 DB 매핑은 LIMIT 전에 이 조건을 적용한다. worker 기아 회귀 테스트와 실제 PostgreSQL 조회 테스트로 검증한다.
- 기존 `RetireAllAsync`도 진행 중인 로그인 로딩을 기다리도록 변경했다. 그 계약과 맞지 않는 기존 barrier 테스트를 수정했다. 기다림의 취소 예외는 기존 공개 API 계약대로 유지한다.
- 이미 확인된 성공 receipt를 결과 codec 오류 때문에 Failed로 바꾸지 않도록 복구 경로에서 원본 결과 schema/payload를 보존한다.

## 최종 검증 결과

- 일반 서버 테스트: 673 통과, 실패 0, skip 0 (`final-general-v3.trx`).
- 실제 PostgreSQL 18.6: 12 통과, 실패 0, skip 0 (`final-postgres-v2.trx`). DB 잠금 대기를 실제 서버에서 관찰했다.
- `dotnet build Bun3.sln -c Release -warnaserror`: 경고 0, 오류 0 (`final-build-v2.log`).
- worker 미지원 후보 기아: RED 1 실패 → GREEN 26 통과(관련 명령 테스트 포함).
- 종료 계약·기존 취소 동작: 관련 테스트 34 통과. 일반 전체 테스트에서 발견한 취소 예외 회귀를 복원했다.
- Docker 실행기 검증은 위 Task 6 Step 4처럼 미완료다. 테스트용 네이티브 DB는 PID/실행 파일/data directory를 확인하고 정상 종료했다.
- 자체 리뷰만 수행했다. 별도 작업자 리뷰가 수행된 것으로 간주하지 않는다.
