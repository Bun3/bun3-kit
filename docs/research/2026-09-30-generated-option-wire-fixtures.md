# 생성 옵션 저장 형식 fixture 검증

2026-09-30. 테스트 전용 `GeneratedOptionMapping`은 생성 상태와 대표 저장 DTO 사이의 명시적 매핑이다. 게임별 매핑을 자동 제공하지 않는다. 실제 게임 DLL을 로드하거나 DB에 연결하지 않았으며, 아래 결과는 독립 fixture에서 확인한 범위다.

## 형식과 원본 대응

근거는 [기존 게임 조사](2026-09-29-game-state-generation-and-persistence.md)와 [승인된 스펙의 저장 형식 계약](../superpowers/specs/2026-09-29-generated-option-state-design.md#7-저장-형식과-변환)이다. 전체 게임 타입을 복사하지 않고 필드 번호가 고정된 작은 독립 스키마를 사용한다.

| Fixture | 대표 번호 | 원본 대응 |
|---|---|---|
| `TrackedOptions` / `TrackedOptionEntry` | root `reroll_options=2`, `product_option=15`; entry `id=1`, `pool_id=2`, `level=3` | idlez `Server/Commons/Types/Players/PlayerItem.cs`의 옵션 구조 |
| `OptionWire` / `OptionEntryWire` | root `entries=1`, `version=2`, `mainOptionLevel=3`, `petOption=5`; entry `id=1`, `level=2` | growdaemon `network.proto:682`, growninja `Commons/network.cs:1399`의 공통 root 구조 |

Google fixture는 일반 proto3 `int32`를 사용하며 임의의 `optional`을 추가하지 않는다. protobuf-net fixture는 nullable backing과 `ShouldSerializeX`/`ResetX`를 가진 scalar 프로퍼티를 사용한다. `Extensible` 기반 클래스는 `IExtensible`을 구현하여 알 수 없는 wire 필드를 보관한다.

`Bun3.Server.Tests`의 복원 결과(`project.assets.json`)에서 확인한 실제 버전은 Google.Protobuf **3.29.3**, Grpc.Tools **2.68.1**, Newtonsoft.Json **13.0.2**, protobuf-net **2.4.6**이다. protobuf-net은 테스트 프로젝트에만 `[2.4.6]`, `PrivateAssets="all"`로 고정했다. 이 버전의 `Serializer.DeepClone`, `Extensible.GetValue`, nullable backing/ShouldSerialize 및 명시적 0 출력은 실행 테스트로 확인했다. 복원/컴파일 경고 억제 설정은 추가하지 않았다.

## 독립 입력

테스트 상수는 직렬화기로 생성한 기대값이 아니라 다음을 직접 인코딩한 입력이다.

```text
Google wire:
12 09 08 0B 10 07 18 03 98 06 65
12 09 08 16 10 08 18 04 98 06 66
7A 09 08 21 10 09 18 05 98 06 67
98 06 84 07

protobuf-net wire:
0A 07 08 0B 10 03 98 06 65
0A 07 08 16 10 04 98 06 66
10 02 18 03
2A 07 08 21 10 05 98 06 67
98 06 84 07
```

두 입력의 entry A/B는 각각 id 11/22, level 3/4이다. 특별 자식은 id 33, level 5다. Google 입력의 poolId는 각각 7/8/9이고, protobuf-net root의 version/mainOptionLevel은 2/3이다. field 99의 varint tag는 `98 06`, root 값은 900(`84 07`), A/B/특별 자식 값은 101/102/103(`65`/`66`/`67`)이다. field 99는 fixture에서 예약하거나 선언하지 않았다.

JSON 입력은 `RerollOptions`, `ProductOption`, `Id`, `PoolId`, `Level`이라는 Newtonsoft 기본 CLR 프로퍼티 이름과 숫자 값을 사용한다. 알려지지 않은 `FutureRoot` 객체와 자식별 `Future` 객체/문자열을 추가했다. `JObject.ToObject<TrackedOptions>()`와 `JObject.FromObject(...)`를 사용하고 `Google.Protobuf.JsonFormatter`로 바꾸지 않았다. 따라서 이 fixture가 검증하는 JSON은 protobuf JSON 표준의 camelCase 형식이 아니다.

## 보존 범위

| 구분 | JSON / Google DTO | Google wire | protobuf-net wire |
|---|---|---|---|
| scalar 미설정 / 명시적 0 | 둘 다 0으로 읽고 숫자 0으로 출력 | 둘 다 0, wire에서 기본 scalar 생략 | nullable 상태로 구분하고 명시적 0을 출력 |
| 단일 자식 null / 빈 객체 | null / `{}` 구분. 빈 객체는 알려진 0 필드를 출력 | absent / 길이 0 메시지 구분 | absent / 길이 0 메시지 구분 |
| 빈 반복 목록 | 누락/빈 목록은 같은 빈 상태로 정규화 | 반복 필드 부재와 빈 목록은 동일 | 반복 필드 부재와 빈 목록은 동일 |
| 알 수 없는 데이터 | 원본 JObject의 속성 병합 | 메시지 Clone의 UnknownFieldSet | IExtensible 데이터의 DeepClone |
| 목록 재정렬 | 상태 객체 identity에 연결된 속성이 따라감 | 같은 노드의 unknown field가 따라감 | 같은 노드의 unknown field가 따라감 |

매핑 객체는 `ConditionalWeakTable`로 원본 DTO/JSON을 노드별로 소유한다. 생성 상태의 추적 필드에는 raw DTO가 없다. 로딩은 연결 전 생성 프로퍼티로 값을 채우고 마지막에 인벤토리에 연결한다. 저장은 원본 메타데이터 복제본의 알려진 필드를 현재 상태로 덮고 목록을 현재 순서로 다시 만든다. `[A,B] → [B,A]`, 기존 노드 제거, 새 노드 추가, 값 수정 뒤 왕복 및 root/자식 unknown 보존을 검증한다. 새 노드는 이전 위치의 메타데이터를 물려받지 않는다. 제거된 상태 객체를 메타데이터가 강한 키로 붙잡지 않는다.

JSON unknown 속성은 wire unknown 필드와 서로 다른 보존 영역이다. 형식 간 변환에서 unknown 정보 이전을 약속하지 않는다. 이 fixture는 같은 매핑 객체로 로드/저장하는 수명을 사용하며, 모든 게임 스키마/serializer 설정/JSON 이름 변형을 지원하는 범용 변환기가 아니다.

## 저장 실패와 Undo

테스트 전용 `PendingOptionSave.Capture()`는 `DrainChanges` 결과를 즉시 불변 record와 JSON 문자열로 만든 뒤 읽기 전용 목록으로 보관한다. `ItemChange.Instance`는 가변 참조이므로 I/O에 직접 넘기지 않는다. pending이 있으면 다시 Drain하지 않는다. 진행 중 저장은 하나이며 실패하면 같은 payload를 유지하고 성공하면 해당 payload만 해제한다. JSON/blob 메타데이터 복제는 저장 시 호환성 비용이며 Undo 스냅샷이 아니다.

TCS로 v1 실패 → 같은 v1 재시도 중 v2 변경 → v1 ACK → 다음 Drain/저장에서 v2 Updated 전송을 제어한다. 캡처 뒤 상태를 바꿔도 v1 문자열은 변하지 않는다. transaction rollback은 새 저장 변경을 남기지 않으며 이전 pending을 유지한다. memory commit도 pending을 지우지 않는다.

Player 테스트는 실제 `PlayerRegistry`, `RpcServer`, TCP 세션의 detach 저장과 reconnect 경로를 사용한다. `TrySaveAsync`의 접근성을 바꾸거나 직접 호출하지 않는다. 세션 actor에 게시한 초기 변경과 저장 hook의 TCS로 제어한 변경만 생성 상태를 수정한다. I/O는 이미 만들어진 payload만 받는다. `Task.Delay`로 순서를 맞추지 않는다.

**현재 계약에서 재시도는 그 시점의 Player dirtyVersion을 캡처한다.** 실패한 v1 payload를 보유한 채 이미 v2가 dirty인 상태에서 재시도하면, v1 ACK만으로 현재 캡처 버전까지 저장됐다고 처리할 수 있다. 따라서 저장 hook은 pending ACK 후 `inventory.HasChanges`가 남으면 `MarkDirty()`를 다시 호출해야 한다. 다음 저장이 v2 payload를 보내고 나서야 clean 상태가 된다.

이 조정의 필요성은 재현 테스트로 확인했다. hook의 ACK 후 재호출 한 줄을 임시로 제거하면 `Player_session_save_keeps_new_dirty_version_until_its_payload_is_saved(True)`가 `IsDirty: Expected True, But was False`로 실패했다. 일반 저장 중 변경 시나리오는 계속 통과했다. 재호출을 복구한 최종 두 세션 시나리오는 모두 통과한다. 이는 현행 계약의 사용 예제이며 새로운 제품 저장 큐가 아니다.

실제 DB transaction, COMMIT 결과 불명확성, 프로세스 종료 후 pending 복구, 실제 게임 DLL 및 전체 프로토콜 호환성은 이 fixture에서 검증하지 않았다.
