# InterfaceRef — Unity 오브젝트를 인터페이스 타입으로 직렬화

> 상태: 구현 (2026-11-12). 패키지 `com.bun3.unity.core` 0.7.0.
> 근거: 쥬라기 낙원 뷰 분리 설계(`jurassic-paradise/docs/superpowers/specs/2026-11-12-view-isolation-mvp-design.md` §3.1).

## 1. 의도

로직 어셈블리가 뷰의 구체 타입을 모른 채 인스펙터에서 뷰를 연결하게 한다. Unity는 인터페이스 타입 필드를 직렬화하지 않고, `[SerializeReference]`(와 SerializeReference Extensions)는 `UnityEngine.Object`를 가리킬 수 없다. 두 번째 게임에서도 같은 래퍼를 짜게 되므로 프레임워크가 지원한다.

## 2. API

```csharp
namespace Bun3.Unity.Core.Serialization
{
    [Serializable]
    public struct InterfaceRef<T> where T : class
    {
        public InterfaceRef(UnityEngine.Object target);
        public T Value { get; }                                  // 비었거나 파괴됐거나 T가 아니면 null
        public static UnityEngine.Object Resolve(UnityEngine.Object dropped);
    }
}
```

- 직렬화 필드 이름은 `target`(`UnityEngine.Object`)이다.
- `Value`는 Unity의 파괴 판정(`== null`)을 따른다. 파괴된 컴포넌트는 null이다.
- `Resolve`: `T`를 구현한 오브젝트는 그대로, `GameObject`면 그 위에서 `T`를 구현한 첫 컴포넌트, 그 밖은 null.
- 배열·리스트 필드(`InterfaceRef<T>[]`)를 지원한다(일반 직렬화 struct).

## 3. 에디터

`InterfaceRefDrawer`(`Bun3.Unity.Core.Editor.Serialization`)가 `ObjectField`를 그리고 놓인 값을 `Resolve`로 바꿔 저장한다. 받아들일 수 없는 값은 저장하지 않는다. 라벨 툴팁에 `T` 이름을 표시한다.

## 4. 검증

런타임 테스트 `InterfaceRefTests`: 할당한 컴포넌트 반환, 파괴 후 null, GameObject에서 구현 컴포넌트 선택(없으면 null).
