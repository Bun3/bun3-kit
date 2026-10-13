# 프로퍼티 변경 기록을 이용한 메모리 롤백 조사

- 조사일: 2026-09-29
- 질문: `private` 필드를 프로퍼티 setter로만 바꾸고, 변경 전 값을 기록하면 `TState` 전체 복사 없이 롤백할 수 있는가?
- 범위: 실제 메모리 복원 경로가 있는 OSS 4개. dirty 표시나 DB 변경 감지만 제공하는 사례와 구별한다.
- 검증: 공식 문서, 고정 커밋의 구현, 일부 upstream 테스트 소스를 읽었다. 라이브러리 실행·벤치마크·Bun3 통합 테스트는 하지 않았다.
- 상태: 조사 결과이며 구현 결정은 아니다. 이번 작업은 이 문서만 추가하며, 패키지 도입이나 현재 롤백 코드 변경은 포함하지 않는다.

## 1. 조사 결과

**제안한 방식은 실제로 사용된다.** 가장 직접적인 C# 예시는 DefaultUnDo의 `UnDoField<T>`이다. 프로퍼티가 보관 중인 이전 값과 새 값을 명령에 넣고, 미커밋 트랜잭션이 끝나면 명령을 역순 실행한다. C# ChangeTracking은 다른 선택을 한다. 필드별 최초 변경 전 값만 사전에 남기고 `RejectChanges()`로 복원한다. NMF는 모델 생성기가 변경 이벤트를 넣어주며, mobx-keystone은 관찰 가능한 모델의 변경을 기록해 실패한 action을 되돌린다. 각 구현 근거는 아래에 연결했다.

공통 조건은 **추적 대상의 모든 변경 경로가 프레임워크를 거친다**는 것이다. 프로퍼티 대입, 컬렉션 추가·삭제, 컬렉션 원소의 내부 필드 변경은 서로 다른 경로다. 하나의 프로퍼티 setter를 추적한다고 그 안의 모든 가변 객체가 자동으로 추적되지는 않는다. 이는 아래 구현들의 추적 경계를 종합한 판단이다.

| 사례 | 변경 감지 방식 | 기록 단위 | 실제 복원 | 이번 제안과의 관계 |
|---|---|---|---|---|
| DefaultUnDo | `UnDoField<T>.Value` setter와 컬렉션 래퍼 | 매 대입의 setter + old/new 값 | 미커밋 scope의 명령을 역순 Undo | private 필드와 프로퍼티 형태에 가장 가까움 |
| ChangeTracking | Castle가 `virtual` setter를 가로챔 | 객체별·프로퍼티별 최초 old 값 | `RejectChanges()`가 프로퍼티에 재대입 | 필드별 최초 기록 방식의 직접 사례 |
| NMF Models | 생성된 setter의 old/new 이벤트 | 변경 이벤트를 모델 변경 명령으로 변환 | `ModelTransaction`이 역전한 변경 집합 적용 | 반복적인 추적 코드의 생성 사례 |
| mobx-keystone | 관찰 가능한 객체·배열의 변경 훅 | 실제 old 값과 복원 함수 | transaction action 실패 시 역순 복원 | 선택적 트랜잭션과 객체 동일성 복원 사례 |

## 2. DefaultUnDo: 프로퍼티 setter와 변경 명령

검토 커밋: `f5b92088d32c737ca0d1b5fb7a660754140db0a9`.

아래는 실제 공개 API로 구성한 설명용 예제다. Bun3 API가 아니며, 이 예제를 별도로 컴파일·실행하지는 않았다.

```csharp
using DefaultUnDo;

public sealed class EquipmentState
{
    private readonly UnDoField<int> _level;

    public EquipmentState(IUnDoManager manager)
    {
        _level = new UnDoField<int>(manager, 3);
    }

    public int Level
    {
        get => _level.Value;
        set => _level.Value = value;
    }
}
```

```csharp
var manager = new UnDoManager();
var state = new EquipmentState(manager);

using (var transaction = manager.BeginTransaction())
{
    state.Level = 5;
    state.Level = 10;
    // Commit is deliberately omitted.
}

// state.Level is restored to 3 by the recorded setters.
```

`UnDoField<T>.Value`는 쓰기 요청 시 내부 `Set` 메서드, 새 값, 현재 값을 manager에 넘긴다. `ValueUnDo<T>`가 이 세 가지를 보관하고, `Do()`는 새 값, `Undo()`는 이전 값을 setter에 전달한다. 전체 `EquipmentState`를 복사하는 경로가 아니다. [필드 래퍼](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoField.cs#L20-L63), [명령의 payload와 복원](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/ValueUnDo.cs#L45-L128)

예제의 트랜잭션에는 `3 → 5`, `5 → 10` 명령이 순서대로 들어간다. 미커밋 `Dispose()`는 뒤에서부터 `10 → 5`, `5 → 3`을 수행한다. 트랜잭션 안의 `_commands`는 매 명령을 추가한다. 일반 undo history에는 인접한 호환 명령을 시간 간격으로 합치는 기능도 있지만, 이것을 트랜잭션 내 필드별 최초 기록으로 해석하면 안 된다. [트랜잭션 구현](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoManager.cs#L13-L96), [history 병합](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/Internal/UnDoStack.cs#L26-L40)

복원 시에는 공개 `Value` setter가 아니라 내부 `Set`을 호출하므로 해당 필드의 명령을 다시 등록하지 않는다. 다만 `PreSet`/`PostSet` 훅은 복원에도 실행된다. 따라서 dirty 알림을 `PostSet`에 연결할 수 있지만, Bun3의 commit 후 알림 정책과는 별도 조율이 필요하다. [내부 setter와 훅](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoField.cs#L55-L80)

컬렉션도 별도 래퍼를 사용한다. `IList<T>.AsUnDo(manager)`의 추가·삭제·인덱서 변경이 명령을 만들며, 리스트 삭제 명령은 원본 리스트, 인덱스, 삭제한 값을 보관한다. `Clear`는 사라질 원소들을 배열로 보관한다. 전체 객체 그래프를 복제하지 않아도 **삭제할 데이터는 복구를 위해 남겨야 한다**. 원소 객체 내부의 프로퍼티는 원소 쪽에서 다시 추적해야 한다. [리스트 래퍼](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/Internal/UnDoIList.cs#L40-L52), [리스트 복원 payload](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/ListUnDo.cs#L13-L69), [Clear 보관](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/Extensions/IUnDoManagerExtensions.cs#L69-L79)

적용 시 구별할 점:

- manager는 필수이며 트랜잭션 scope가 없어도 일반 undo history를 쌓는다. Bun3의 “활성 트랜잭션이 없으면 롤백 기록도 없음”과 동일하지 않다. [생성자](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoField.cs#L29-L42), [명령 등록 분기](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoManager.cs#L207-L232)
- old/new가 참조형이면 그 참조를 보관한다. 래퍼를 거치지 않은 가변 객체 내부 변경까지 복제해 보존하지 않는다. 이는 `ValueUnDo<T>`의 단순 값 저장에서 확인되는 경계다. [값 저장](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/ValueUnDo.cs#L68-L82)
- 명령 객체·델리게이트를 쓰며, `Do()` 완료 후 history에 추가한다. 임의의 setter/콜백이 일부 변경 후 예외를 내는 경우까지 Bun3가 요구하는 예외 계약과 같다고 간주하면 안 된다. 별도의 통합 검증 대상이다. [실행과 등록 순서](https://github.com/Doraku/DefaultUnDo/blob/f5b92088d32c737ca0d1b5fb7a660754140db0a9/source/DefaultUnDo/UnDoManager.cs#L207-L232)

## 3. ChangeTracking: 프로퍼티별 최초 값 보관

검토 커밋: `b7e411a0967bb83f3b2fec4c47c9167f18467906`.

`order.AsTrackable()`로 만든 proxy를 평소처럼 수정하고, `RejectChanges()`로 되돌린다. Castle DynamicProxy를 이용하며, 공식 사용 조건은 상속 가능한 클래스와 public virtual 멤버다. getter/setter를 생성된 proxy가 감싸므로 사용자가 모든 setter에 기록 코드를 직접 넣지는 않는다. [공식 사용법과 제약](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/README.md)

실제 interceptor는 setter 호출 전에 현재 값을 읽고, 호출 완료 후 실제 새 값을 읽는다. 최초의 유효한 변경이면 `Dictionary<string, object>`에 프로퍼티 이름과 이전 값을 추가한다. 이미 기록되어 있으면 최초 값을 유지하고, 값이 원래 값으로 돌아오면 항목을 지운다. `Level: 3 → 5 → 10`이면 기록은 `Level: 3` 하나다. **쓰기 전에 old 값을 읽지만, 기록을 사전에 추가하는 시점은 setter가 성공한 뒤**다. [최초 값 기록](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ChangeTrackingInterceptor.cs#L39-L78)

`RejectChanges()`는 자식들의 변경을 거부한 뒤 보관한 값들을 프로퍼티에 재대입한다. 이때 `_InRejectChanges`를 켜서 같은 interceptor가 복원을 신규 변경으로 기록하지 않도록 한다. 단순 dirty 인터페이스만 구현한 것이 아니라 실제 복원 코드가 있다. [복원과 기록 억제](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ChangeTrackingInterceptor.cs#L321-L352)

복합 프로퍼티는 가능한 자식들을 다시 proxy로 감싸고, 컬렉션은 항목의 추가·삭제·변경 상태를 추적한다. `RejectChanges()`와 `AcceptChanges()`를 호출할 수 있어 별도 트랜잭션 객체가 필수는 아니다. 원래 POCO를 그대로 사용할 수도 있지만 추적 기능은 만들어진 proxy를 통해 접근해야 한다. [자식 proxy](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ComplexPropertyInterceptor.cs#L44-L114), [컬렉션 추적](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ChangeTrackingCollectionInterceptor.cs#L30-L38)

이 사례는 **필드별 최초 보관**의 참고 자료로 유용하다. 컬렉션의 완전한 구조 롤백 근거로 가져오기는 어렵다. 확인한 `RejectChanges()`는 삭제 항목을 원래 인덱스에 삽입하지 않고 리스트 끝에 `Add`한다. 또한 `_InRejectChanges` 해제에 `finally`가 없어 복원 setter의 예외 계약을 그대로 채택할 이유는 없다. 이는 소스에서 읽은 제한이며 이번 조사에서 실행으로 재현한 결과는 아니다. [컬렉션 복원 순서](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ChangeTrackingCollectionInterceptor.cs#L139-L154), [복원 flag 범위](https://github.com/joelweiss/ChangeTracking/blob/b7e411a0967bb83f3b2fec4c47c9167f18467906/Source/ChangeTracking/ChangeTrackingInterceptor.cs#L336-L345)

## 4. NMF Models: 생성된 setter에서 트랜잭션까지

검토 커밋: `6944496b0842987011adc067d69ad7855940d55f`.

NMF는 메타모델에서 모델 코드를 생성하고, 모델 변경 이벤트를 기록·적용·역전한다. 생성된 프로퍼티는 private backing field를 사용하며, 이전 값과 새 값을 이벤트 인자로 만든 다음 변경 전 이벤트, 필드 대입, 변경 후 이벤트를 수행한다. 반복적인 setter 코드를 도구가 생성한다는 점이 Bun3의 “게임에는 스키마만 남긴다”는 방침에 가깝다. [공식 개요](https://nmfcode.github.io/models/index.html), [실제 생성된 setter](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/AttributePropertyChange.cs#L44-L86)

`ModelChangeRecorder`는 루트에 연결된 bubbled event를 받는다. 범위는 해당 루트와 containment 하위 모델이며, 여러 루트에 연결할 수도 있다. 관찰한 이벤트를 순서대로 모은 뒤 `AffectedElement`, `Feature`, `OldValue`, `NewValue`가 있는 변경 모델로 변환한다. 속성 값은 타입 메타데이터를 이용해 직렬화하고, 모델 참조 변경은 모델 객체를 보관한다. 이벤트들을 최초 필드값 사전 하나로 축약하는 방식은 아니다. [범위 설명](https://nmfcode.github.io/models/RecordingChanges.html), [이벤트 수집](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/ModelChangeRecorder.cs#L190-L203), [프로퍼티 변경 payload](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/ModelChangeRecorder.cs#L907-L951)

`ModelTransaction`은 recorder를 시작하고, 미커밋 `Dispose()`에서 `Rollback()`한다. 복원 전에 recorder를 중지하고, 역전한 변경 집합을 적용한다. 역전 과정은 변경 순서를 뒤집고 old/new를 교환한다. 복원 자체는 모델의 속성 설정 경로를 이용하지만, 해당 recorder가 중지되었으므로 자기 롤백을 다시 기록하지 않는다. [트랜잭션 수명](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/ModelTransaction.cs#L23-L63), [역순 변경 집합](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/ModelChangeSetExtensions.cs#L37-L47), [old/new 역전](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/InvertChanges.cs#L43-L56), [속성 복원](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/ApplyChanges.cs#L46-L53)

컬렉션 변경은 별도 변경 타입으로 처리하며, 인덱스가 있는 삭제는 같은 인덱스의 삽입으로 역전한다. 그러나 일반 collection reset은 복원에 필요한 삭제 값들을 보관하지 않아 `Invert()`가 `NotSupportedException`을 던진다. “변경 알림이 있다”는 사실과 “모든 변경을 복구할 정보가 있다”는 사실이 다르다는 구체적인 사례다. [인덱스 삭제 역전과 reset 제한](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/InvertChanges.cs#L226-L247), [공식 제한 설명](https://nmfcode.github.io/models/RecordingChanges.html#inverting-model-changes)

생성된 모델 자체는 recorder/트랜잭션 없이도 수정할 수 있다. 기록 기능은 recorder를 연결하고 시작하는 별도 경로다. 다만 이는 NMF 모델·메타데이터·전용 컬렉션을 전제로 하므로 임의 POCO에 얇은 setter 하나를 추가하는 라이브러리는 아니다. [생성 setter](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/AttributePropertyChange.cs#L69-L87), [recorder 연결과 시작](https://github.com/NMFCode/NMF/blob/6944496b0842987011adc067d69ad7855940d55f/Models/Models/Changes/ModelChangeRecorder.cs#L63-L109)

## 5. mobx-keystone: 실패한 action을 변경 기록으로 복원

검토 커밋: `4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252`.

TypeScript 사례지만 선택적 트랜잭션에 가깝다. `transactionMiddleware` 또는 `@transaction`이 연결된 action에서 예외가 발생하면 실행 중의 상태 변경을 되돌린다. 검토한 현재 구현은 `ChangeRollbackRecorder`가 실제 old 값과 복원 함수를 저장한다. 과거 설명이나 검색 요약의 “inverse patch replay”라는 표현을 현재 구현 설명으로 사용하면 부정확하다. [공식 사용법](https://mobx-keystone.js.org/action-middlewares/transaction-middleware/), [현재 트랜잭션 구현](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/actionMiddlewares/transactionMiddleware.ts#L38-L108)

여기서는 대입 전 setter 코드가 아니라 MobX의 변경 완료 이벤트가 제공하는 `oldValue`를 사용한다. public patch/deep-change listener에 전달하기 전에 롤백 기록을 만든다. listener가 다른 변경을 일으키기 전에 복원 정보를 남기려는 순서다. [객체 변경 훅](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/tweaker/tweakPlainObject.ts#L234-L244), [배열 변경 훅](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/tweaker/tweakArray.ts#L227-L232)

복원 명령의 내용은 다음과 같다. 전체 트리의 복사본으로 교체하지 않고 실제 대상과 이전 값을 보관한다. splice에서 삭제한 구간은 얕게 복사해 남긴다. 같은 필드의 반복 변경은 각각 기록하며, 같은 원소들로 같은 구간을 교체하는 splice는 생략한다. [기록 조건과 복원 함수](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/tweaker/changeRollback.ts#L90-L134)

| 변경 | 보관하는 정보 | 복원 |
|---|---|---|
| 객체 프로퍼티 갱신·삭제 | 대상 객체, 키, 이전 값 | 이전 값을 다시 설정 |
| 객체 프로퍼티 추가 | 대상 객체, 키 | 추가한 키 제거 |
| 배열 인덱스 변경 | 대상 배열, 인덱스, 이전 값 | 해당 인덱스 복원 |
| 배열 splice | 배열, 시작 인덱스, 추가 개수, 삭제한 원소들 | 추가분을 제거하고 삭제분 삽입 |

action 종료 시 recorder를 먼저 해제한다. 실패했으면 복원 함수를 역순 실행하고, 복원 listener 하나가 예외를 내더라도 나머지 복원을 시도한다. 관련 테스트 소스에는 원소 삭제 → 분리된 원소 수정 → 재삽입 → 실패 후, 기존 원소 객체의 동일성과 값이 복원되는 검사가 있다. 이는 해당 추적 모델의 테스트이며 임의 객체 그래프 전체의 보장으로 확대할 수 없다. [recorder 해제와 역순 복원](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/actionMiddlewares/transactionMiddleware.ts#L76-L108), [객체 동일성 테스트](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/test/actionMiddlewares/transactionMiddleware.test.ts#L315-L338)

transaction middleware가 없는 일반 action은 예외가 나도 그 action의 모든 변경을 자동 취소하지 않는다. 또한 롤백 중에도 보상 변경 이벤트가 발생한다. Bun3의 “commit 전 외부 알림 보류”와 동일한 이벤트 계약이 아니다. 이 라이브러리에는 별도로 snapshot/patch 기능도 있으므로 전체 라이브러리가 snapshot을 전혀 사용하지 않는다고 주장하지 않는다. [일반 action의 테스트](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/test/actionMiddlewares/transactionMiddleware.test.ts#L99-L104), [보상 변경과 다른 기능 설명](https://github.com/xaviergonz/mobx-keystone/blob/4b9bfd7e2bbd0fb8d20784616a01fc019ac3e252/packages/lib/src/tweaker/changeRollback.ts#L7-L10), [공식 복원 의미](https://mobx-keystone.js.org/action-middlewares/transaction-middleware/)

## 6. Bun3에 적용한다면

다음은 조사에 따른 제안이며, 현재 구현된 기능이 아니다.

현재 `ItemInstance`의 `Flags`와 `ExpiresAt` setter에는 이미 `CaptureForRollback()` → 값 대입 → dirty 표시 순서가 있다. 문제는 프레임워크가 알지 못하는 `TState` 안의 `Level`과 `Options` 변경이다. 사용자 제안은 이 통제된 변경 경로를 게임 상태 안쪽으로 확장하자는 것이다. [현재 ItemInstance](../../server/src/Bun3.Server.Items/ItemInstance.cs), [현재 상태 캡처](../../server/src/Bun3.Server.Items/ItemInventory.Transaction.cs)

이 방식은 가변 `TState`의 깊은 복사를 대체할 후보이다. 현재 별도로 수행하는 `_instances`와 `_regenBasis` 사전의 전체 보관까지 없애려면, 아이템 생성·삭제와 재생 기준 변경도 이전 값과 구조 변경 기록으로 바꿔야 한다. 상태 프로퍼티 추적을 추가하는 것만으로 이 두 사전의 보관 방식까지 자동으로 바뀌지는 않는다. [현재 컨테이너 보관과 복원](../../server/src/Bun3.Server.Items/ItemInventory.Transaction.cs)

구조는 다음처럼 정리할 수 있다. 아래 `BeforeFieldChange`와 `RestoreField`는 설명용 이름이며 기존 Bun3 API가 아니다.

```text
Level setter
  → 같으면 종료
  → BeforeFieldChange(대상, 필드 ID, 이전 값)
  → private backing field에 새 값 대입
  → dirty 표시

활성 트랜잭션 없음: 기록 생략, 대입과 dirty 처리
Commit: 기록 폐기, 기존 commit 알림 계약 수행
Rollback: 기록을 역순 순회 → RestoreField로 backing field 복원
          → 트랜잭션 전 dirty 상태 복원, 미발행 알림 폐기
```

1. **상태와 컬렉션의 변경 경로를 통제한다.** 일반 필드 쓰기, raw `List<T>` 변경, 원소 내부의 추적되지 않는 mutable 객체를 열어두면 다시 복사나 다른 복원 계약이 필요하다. 컬렉션 getter만 readonly로 만드는 것으로 원소 내부의 변경까지 막을 수는 없다.
2. **최초 기록과 매 변경 기록을 구분한다.** 단일 트랜잭션의 독립적인 scalar 필드는 최초 값 한 번으로 충분할 수 있다. 생성·삭제·참조 교체·소유권 변경·중첩 scope가 섞이면 원래 작업 순서를 보존해야 한다. 우선 매 변경을 역순 복원하는 규칙을 명확히 정하고, 최초 기록으로 줄일 수 있는 항목을 검증하는 편이 설명하기 쉽다.
3. **일반 대입과 복원 대입을 분리한다.** 복원은 다시 undo를 등록하거나 사용자 validation/외부 알림을 실행하지 않는 내부 경로가 필요하다. dirty 상태도 단순히 모두 false가 아니라 트랜잭션 전 상태로 돌아가야 한다.
4. **반복 코드는 프레임워크가 맡는다.** NMF처럼 생성으로 setter와 필드별 복원 코드를 만드는 방식이나 DefaultUnDo처럼 필드 래퍼를 사용하는 방식을 비교할 수 있다. source generator는 후보이며 이번 조사에서 Bun3용 구현을 추가하지 않았다.
5. **선택 기능 계약을 유지한다.** `ItemInventory`/`AchievementManager`를 Transaction 없이 사용할 수 있고, 연결되어도 비활성일 때 기록하지 않는 현재 계약을 유지한다. DefaultUnDo의 항상 켜진 undo history를 그대로 가져오면 이 계약과 달라진다.

전체 상태 복사 없이 롤백할 수 있다는 근거는 충분하다. 다만 `Clear`는 삭제 원소를, 프로퍼티 교체는 이전 참조를, 원소 내부 변경은 해당 필드의 이전 값을 남겨야 한다. **없애려는 것은 전체 상태의 일괄 복사이지, 복구에 필요한 이전 정보 자체가 아니다.** 기록 수·할당·복원 시간은 구현과 사용량에 따라 달라지며 이번 조사로 성능 우위를 입증하지는 않았다.

## 7. 확인 범위와 다음 설계의 입력

직접 확인한 경로는 DefaultUnDo의 필드·명령·트랜잭션·리스트/Clear, ChangeTracking의 setter interception·RejectChanges·자식/컬렉션, NMF의 생성 setter·recorder·역전/적용·ModelTransaction, mobx-keystone의 변경 recorder·transaction middleware다. upstream 테스트 소스는 계약을 확인하기 위해 읽었으며 실행 성공을 의미하지 않는다.

다음 설계에서 결정할 사항은 필드 래퍼 대 코드 생성, 상태 객체의 소유자 연결과 교체, 추적 컬렉션의 범위, 기록 압축 여부다. 현재 패키지의 구현과 사용 계약은 [롤백 가이드](../outgame-rollback.md)를 따른다. 이 문서는 그 계약을 자동으로 대체하지 않는다.
