# Bun3.Server.Items 설계 (서버 아이템 카탈로그 + 스택 인벤토리)

- 날짜: 2026-08-17
- 상태: 승인 대기 (합리적 기본값으로 병렬 작업 진행 — 결정 사항은 워크트리 코멘트로 공유)
- 범위: `Bun3.Server.Items` 패키지 신설 — 아이템 정의 카탈로그 계약(id 인터닝,
  기동 시 로드/검증), 스택 인벤토리 컨테이너(원자적 조작 + 실패 사유),
  복수 아이템 전부-아니면-전무 트랜잭션, Players 저장 패턴 연계.
- 선행 문서: `2026-08-06-server-players-design.md` (MarkDirty/OnSaveAsync 계약),
  `2026-08-12-gameplay-tag-catalog-design.md` (카탈로그 인터닝 선례)

## 1. 배경과 입력 결정

첫 소비자는 idlez류 방치형이지만 장르 무관 프레임워크 몫만 담는다.
idlez-server 실물 조사(`PlayerItem.proto`): 아이템은 "정의 id + 수량" 스택과
"고유 id + 레벨/경험치/만료" 인스턴스의 두 형태로 쓰였다.

| 질문 | 결정 |
|---|---|
| 아이템 정의 형태 | **게임 몫** — 프레임워크는 정의를 불투명 `TDefinition`으로만 보관 |
| 인스턴스 아이템(고유 id·레벨 등) | **게임 몫으로 제외** — 프레임워크는 스택만. 인스턴스는 게임 도메인 상태(스키마·성장 규칙)와 분리 불가 |
| 슬롯 | **게임 몫** — 컨테이너는 무슬롯 (ItemId → 수량 맵) |
| 수량 타입 | **long 기본 + BigNum 확장** — 값타입 제네릭 ops 시섬으로 두 구현 제공 |
| maxStack | **프레임워크 메타데이터** — 정의 등록 시 선택 지정(long). 프레임워크는 숫자만 알고 도메인은 모름 |
| 저장/DB | **게임 몫** — 컨테이너는 변경 통지(onChanged)만 제공, 게임이 `Player.MarkDirty`를 연결 |
| 동시성 | **락 없음** — Player 상태는 세션 액터 안에서 단일 스레드 접근(Players 계약과 동일). 문서로 명시 |

## 2. 패키지 구조

```
server/src/Bun3.Server.Items/     netstandard2.1 · → Bun3.Gameplay (BigNum 읽기 전용)
├── ItemId.cs                      인터닝된 정의 식별자 (readonly struct, default=None)
├── ItemCatalog.cs                 비제네릭 코어 — 인터닝 문자열/maxStack/역색인
├── ItemCatalogOfT.cs              ItemCatalog<TDefinition> — 불투명 정의 보관
├── ItemCatalogBuilder.cs          기동 시 1회 빌드 + 검증 델리게이트 시섬
├── ItemCatalogException.cs        빌드/검증 실패
├── ItemError.cs                   실패 사유 enum
├── IQuantityOps.cs                수량 산술 시섬 (struct 제약 제네릭 — 무박싱)
├── LongQuantityOps.cs             long 기본 구현
├── BigNumQuantityOps.cs           BigNum 구현 (방치형 확장)
├── ItemDelta.cs                   부호 있는 변경량 { ItemId, TQuantity }
├── ItemStack.cs                   열거 항목 { ItemId, TQuantity }
└── ItemStackContainer.cs          제네릭 컨테이너 + long/BigNum sealed 특수화
```

Bun3.Gameplay 의존은 BigNum 하나 때문이다. 분리 패키지(Bun3.Server.Items.BigNum)로
쪼개는 안은 기각 — 파일 1개를 위한 패키지·버전 관리 비용이 더 크다. Gameplay 패키지는
netstandard2.1이라 TFM 문제도 없다.

## 3. 카탈로그 — id 인터닝과 기동 시 검증

GameplayTag Catalog와 같은 원칙: **문자열 id는 기동 시 1회 인터닝**, 핫패스는
`ItemId`(정수 인덱스) 전용. 게임마다 달라지는 정의 스키마는 `TDefinition`으로 불투명하게.

```csharp
var builder = new ItemCatalogBuilder<MyItemDef>();
builder.Register("potion.small", new MyItemDef(...), maxStack: 999);
builder.Register("gold", new MyItemDef(...));                  // maxStack 없음 = 무제한
builder.AddValidator(catalog => { /* 게임 규칙 검증, 실패 시 ItemCatalogException */ });
ItemCatalog<MyItemDef> catalog = builder.Build();              // 검증 델리게이트 일괄 실행
```

- `ItemCatalog`(비제네릭): `Count`, `TryGet(string, out ItemId)`, `GetRequired(string)`,
  `GetIdString(ItemId)`(인터닝 문자열 반환 — 무할당), `GetMaxStack(ItemId)`,
  `Contains(ItemId)`. 컨테이너는 이 비제네릭 코어만 참조한다(수량 로직은 정의 무관).
- `ItemCatalog<TDefinition>`: `GetDefinition(ItemId)` 추가.
- `ItemId`: 내부 `index + 1` 저장 — `default(ItemId) == ItemId.None`이 자연 성립.
  같은 프로세스에 카탈로그가 2개면 교차 사용을 인덱스 범위로만 걸러낸다(태그 카탈로그와
  같은 한계 — 단일 카탈로그 운용이 계약).
- 빌드 후 카탈로그는 불변. 로드 델리게이트(정의 소스)는 게임이 빌더를 채우는 코드
  자체다 — Auth의 검증기처럼 별도 인터페이스를 파지 않는다(호출 지점이 기동 1곳뿐).

## 4. 수량 시섬 — long 기본, BigNum 확장

netstandard2.1에는 generic math가 없으므로 값타입 ops 제네릭으로 devirtualize한다:

```csharp
public interface IQuantityOps<TQuantity>
{
    TQuantity Zero { get; }
    int Compare(TQuantity a, TQuantity b);
    bool TryAdd(TQuantity a, TQuantity b, out TQuantity result);  // false = 산술 오버플로
    bool TryGetMaxStack(long maxStack, out TQuantity result);     // 카탈로그 maxStack 변환
}
public struct LongQuantityOps : IQuantityOps<long> { ... }        // checked 덧셈
public struct BigNumQuantityOps : IQuantityOps<BigNum> { ... }    // 실용 범위 오버플로 없음
```

- 뺄셈은 별도 멤버가 없다 — 부호 있는 델타의 덧셈으로 통일(`TryAdd(current, -5)`).
- BigNum 덧셈은 보존 범위(유효 18~19자리) 밖 항을 흡수하는 **손실 연산**이다.
  방치형 수량 의미론으로 수용하고 XML 문서에 명시한다. 전량 소모(잔량 == 소모량)는
  BigNum 뺄셈이 정확히 Zero를 반환하므로 안전하다.

## 5. 컨테이너 — 원자적 조작과 실패 사유

```csharp
public class ItemStackContainer<TQuantity, TOps> where TOps : struct, IQuantityOps<TQuantity>
public sealed class ItemStackContainer : ItemStackContainer<long, LongQuantityOps>
public sealed class BigNumItemStackContainer : ItemStackContainer<BigNum, BigNumQuantityOps>
```

- 저장소: `Dictionary<ItemId, TQuantity>` (struct 키, IEquatable — 무박싱).
  0이 되면 엔트리 제거 — 열거는 항상 보유 스택만.
- 생성: `(ItemCatalog catalog, int capacity = 0, Action? onChanged = null)`.
  `onChanged`는 성공한 변경 연산당 1회 — 게임은 `player.MarkDirty`(메서드 그룹,
  생성 시 1회 할당)를 넘기면 Players 저장 주기와 그대로 맞물린다.
- 실패 사유: `enum ItemError { None, UnknownItem, InvalidAmount, Insufficient, ExceedsMaxStack }`
  — `ExceedsMaxStack`은 maxStack 초과와 산술 오버플로를 통합(둘 다 "더 못 담음").
- 단건: `GetQuantity`(미보유=Zero), `TryAdd(id, amount)`, `TryRemove(id, amount)`
  (amount는 양수 강제), `Contains(id)`, `Count`, `Clear()`.
- 로드: `TryLoad(id, quantity)` — onChanged 미발화, maxStack 검사는 수행
  (정의 변경으로 상한이 줄었으면 게임이 실패를 보고 정책 결정). 0 이하는 InvalidAmount.
- 열거: `Enumerator` struct(딕셔너리 struct 열거자 래핑) → `ItemStack<TQuantity>` —
  foreach 무할당. 저장 직렬화는 게임이 이 열거로 수행.

### 트랜잭션 — 전부-아니면-전무

```csharp
Span<ItemDelta<long>> deltas = stackalloc ItemDelta<long>[3];
deltas[0] = new ItemDelta<long>(potion, -5);      // 소모
deltas[1] = new ItemDelta<long>(gold, +100);      // 지급
deltas[2] = new ItemDelta<long>(potion, -2);      // 같은 id 중복 허용
ItemError err = container.TryApply(deltas, out int failedIndex);
```

- 2패스: ①검증(순차 시뮬레이션 — 같은 id의 앞선 델타 누적을 반영해 각 단계에서
  Insufficient/ExceedsMaxStack/오버플로 판정) ②적용(실패 불가능 상태에서 일괄 반영).
  실패 시 컨테이너는 완전 무변경, `failedIndex`가 원인 델타를 가리킨다.
- 검증 패스의 같은-id 누적은 앞선 델타 재스캔(O(n²))으로 무할당 처리 — 지급/소모
  배치는 실무에서 소수 항목이다.
- 델타 0은 InvalidAmount — 호출측 버그를 조기에 드러낸다.
- 이동: `TryMoveTo(target, id, amount)` — 소스 Insufficient·대상 ExceedsMaxStack을
  모두 선검증 후 반영, 양쪽 onChanged 각 1회. 두 컨테이너는 같은 카탈로그여야 한다
  (다르면 UnknownItem 계열이 아닌 ArgumentException — 구성 오류).

### 무할당 규율

핫패스(TryAdd/TryRemove/TryApply/TryMoveTo/GetQuantity/열거)에서 클로저·LINQ·문자열
할당 없음. 할당 지점은 생성 시 딕셔너리·onChanged 델리게이트, 그리고 딕셔너리 성장뿐.
테스트에서 `GC.GetAllocatedBytesForCurrentThread`로 검증한다.

## 6. 소유 경계 요약

| 프레임워크 (이 패키지) | 게임 |
|---|---|
| id 인터닝·카탈로그 불변화·검증 실행 시점 | 정의 스키마(TDefinition)·정의 소스(DB/JSON)·검증 규칙 |
| 스택 수량 원자 연산·실패 사유·트랜잭션 | 슬롯 배치·인스턴스 아이템·아이템 효과 |
| 변경 통지(onChanged) | MarkDirty 연결·OnSaveAsync 직렬화·DB 매핑 |
| maxStack 숫자 강제 | maxStack 값의 밸런스 결정 |

## 7. 테스트 계획

`server/tests/Bun3.Server.Tests`(net10, NUnit)에 추가:

- 카탈로그: 인터닝(같은 참조 반환)·중복 id 거부·검증 델리게이트 실행/실패 전파·
  None/범위 밖 ItemId 거부.
- 컨테이너 단건: 추가/제거/잔량/미보유 Zero·Insufficient·maxStack·long 오버플로·
  0 이하 amount 거부·0 도달 시 엔트리 제거.
- 트랜잭션: 혼합 지급+소모 성공·중간 실패 시 완전 무변경·같은 id 중복 델타 누적 판정·
  failedIndex 정확성.
- 이동: 성공·소스 부족·대상 상한·카탈로그 불일치.
- BigNum: long 컨테이너와 동일 시나리오 차등(differential) 실행 + 전량 소모 정확성.
- 무할당: 워밍업 후 TryAdd/TryRemove/TryApply/열거 루프의 할당 0 검증.
- dirty 연계: onChanged 호출 횟수(성공 연산당 1회, 실패·로드 시 0회).
