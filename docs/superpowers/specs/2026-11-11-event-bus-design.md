# 이벤트 버스 — 할당 없는 struct 이벤트와 상태 재생

> 상태: 초안 (2026-11-11)
> 패키지: `com.bun3.common` (`Bun3.Common.Events`), 0.5.0 → 0.6.0
> 첫 사용처: 쥬라기 낙원 `docs/superpowers/specs/2026-11-11-dark-vision-mvp-event-bus-design.md`(암흑 시야 MVP). 게임 쪽 범위 루트·서버 분리 원칙은 그 문서가 정본이다.

## 1. 의도

뷰가 모델 상태를 매 프레임 직접 읽거나 정적 이벤트로 받는 대신, 주입받은 버스 인스턴스를 구독해 값을 받게 한다. 두 번째 게임에서도 똑같이 짜게 될 기반이므로 프레임워크가 지원한다. 도메인을 모르고 UnityEngine에도 의존하지 않는다.

## 2. 사용자 결정 (2026-11-10~11 쥬라기 낙원 대화)

1. 큐가 아니라 즉시 전달하는 **버스**로 간다. — "버스로 가자."
2. 정적 전역이 아니라 **인스턴스를 주입**한다. 범위는 어느 인스턴스를 받았는지로 구분한다. — "그럴거면 차라리 DI 구조로 Bus 객체를 받는게 더 나을듯?"
3. 이벤트는 **struct**로 하고 발행에 할당이 없어야 한다. — "버스 이벤트도 struct로 해서 할당이 발생 안되도록 할 수 있나?"
4. 버스를 **bun3-kit `com.bun3.common`**에 둔다. — "이 이벤트 버스를 bun3-kit에 패키지 코드로 넣는 건 어떻게 생각해?" → "ㅇㅇ"

## 3. API

```csharp
namespace Bun3.Common.Events
{
    public delegate void EventBusHandler<T>(in T evt) where T : struct;
    public interface IStateEvent { }

    public sealed class EventBus : IDisposable
    {
        public EventBus(Action<Exception> onHandlerException = null);
        public void Publish<T>(in T evt) where T : struct;
        public void Subscribe<T>(EventBusHandler<T> handler) where T : struct;
        public void Unsubscribe<T>(EventBusHandler<T> handler) where T : struct;
        public bool TryGetLast<T>(out T evt) where T : struct;
        public void Dispose();
    }
}
```

`System.EventHandler<T>`와 이름이 겹치지 않도록 델리게이트는 `EventBusHandler<T>`로 한다.

## 4. 규칙

### 4.1 할당
- 타입별 `Channel<T>`를 처음 쓰일 때 한 번 만들고 `Dictionary<Type, IChannel>`에 둔다. 발행은 `typeof(T)` 조회와 캐스팅뿐이라 할당이 없다.
- 이벤트는 `in`으로 전달한다. 구독자는 배열에 두고 멀티캐스트 결합을 쓰지 않는다.
- 구독 배열이 찰 때만 두 배로 늘린다. 구독 시 델리게이트 생성은 호출자 몫이다.

### 4.2 상태 이벤트와 마지막 값
- `IStateEvent`를 구현한 타입만 마지막 값을 `Channel<T>`의 `T` 필드에 보관한다(박싱 없음). 판정은 채널 생성 시 한 번 한다.
- 상태 이벤트는 구독자가 없어도 발행하면 값을 보관한다. 새 구독자는 구독 즉시 마지막 값을 한 번 받는다.
- 순간 이벤트는 보관·재생하지 않으며 `TryGetLast`는 항상 `false`다.

### 4.3 디스패치
- 구독 순서대로 호출한다. 순서에 의존하지 않는 것을 전제로 한다.
- 발행 중 구독 해제는 칸을 비우고 가장 바깥 발행이 끝난 뒤 압축한다. 해제된 구독자는 같은 발행 안에서도 더 이상 호출되지 않는다.
- 발행 중 새 구독은 그 발행에서 호출되지 않고 다음 발행부터 받는다.
- 같은 구독자를 두 번 구독하면 무시한다. 없는 구독자 해제는 무시한다.
- 구독자 안의 같은 타입 재발행은 허용하되 중첩 깊이 8을 넘으면 `InvalidOperationException`을 던진다.

### 4.4 예외
- `onHandlerException`이 있으면 구독자 예외를 넘기고 나머지 구독자를 계속 호출한다. Unity 쪽은 `Debug.LogException`을 넘긴다.
- 없으면 나머지 구독자를 모두 호출한 뒤 첫 예외를 원래 스택으로 다시 던진다. 오류를 조용히 삼키지 않기 위해서다.

### 4.5 수명과 스레드
- `Dispose`는 모든 구독과 마지막 값을 지운다. 이후 `Publish`·`Subscribe`는 `ObjectDisposedException`, `Unsubscribe`·`TryGetLast`는 무시·`false`다(정리 순서에 덜 민감하게).
- 스레드 안전하지 않다. 한 스레드(Unity는 메인 스레드)에서만 쓴다.

## 5. 테스트 (`common/tests/Bun3.Common.Tests/EventBusTests.cs`)

- 발행이 구독자에게 값을 전달하고, 해제 뒤에는 받지 않는다.
- 상태 이벤트: 늦은 구독자가 마지막 값을 즉시 받는다. 순간 이벤트는 받지 않는다. `TryGetLast` 결과.
- 발행 중 해제된 구독자는 같은 발행에서 호출되지 않는다. 발행 중 추가된 구독자는 다음 발행부터 받는다.
- 중복 구독 무시.
- 재발행 깊이 초과 시 예외.
- 예외 처리기가 있을 때 나머지 구독자 계속 호출, 없을 때 전부 호출 후 재던짐.
- `Dispose` 뒤 동작.
- 예열 뒤 발행 1,000회의 할당이 0바이트(`GC.GetAllocatedBytesForCurrentThread`).
