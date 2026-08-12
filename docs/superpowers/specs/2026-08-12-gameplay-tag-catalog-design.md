# GameplayTag 카탈로그·컨테이너 설계

- 상태: 승인됨
- 작성일: 2026-08-12
- 적용 패키지: `Bun3.Gameplay`, Unity 전용 에디터 어댑터
- 근거 조사: [`2026-08-12-unreal-gas-gameplay-tag-ownership.md`](../../research/2026-08-12-unreal-gas-gameplay-tag-ownership.md)
- 대체 범위: `2026-08-10-gameplay-framework-design.md`의 §2 태그 결정, §3 태그 계층 데이터 비목표, §7 GameplayTag, §8의 전 객체 `TagSet` 소유 결정

## 1. 목적

서버와 Unity 클라이언트가 같은 태그 정의와 런타임 동작을 공유한다. 태그는 분류, 필터링,
상태 판정, 게임 로직 분기에 광범위하게 사용되며 프레임당 수만~수십만 회 조회될 수 있다.
핫패스에서는 문자열 처리와 할당을 제거하고, 태그 추가·제거와 계층 조회는 Unreal
GameplayTag의 의미를 따른다.

작성자는 Unity 에디터 또는 텍스트 편집기로 하나의 JSON 파일을 관리한다. 실행 중에는 새
태그를 등록하지 않는다. 모딩을 위한 런타임 등록은 실제 요구가 생길 때 별도 설계한다.

## 2. 결정 요약

| 축 | 결정 |
|---|---|
| 작성 정본 | 사람이 읽고 수정하는 단일 JSON 파일의 점 구분 전체 경로 |
| 태그 문법 | `^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*$` |
| 이름 상한 | 전체 경로 255 ASCII 문자, 계층 16단계 |
| 대소문자 | ASCII 대소문자를 무시해 동일 태그로 취급; 명시 선언 표기는 표시용으로 보존 |
| 부모 | 전체 경로에서 암시적으로 생성 |
| 로드 | 공용 코어가 JSON을 기동 시 한 번 읽고 검증한 뒤 카탈로그 동결 |
| 런타임 정체성 | `0 = None`, 나머지는 결정적으로 부여한 `ushort` 인덱스 |
| 문자열 조회 | 기동·데이터 로드 같은 저빈도 경로의 해시 테이블에서만 수행 |
| 핫패스 | `ushort` 비교와 정렬 배열 조회; 문자열 해시·비교와 충돌 probe 없음 |
| 카탈로그 일치 | 정규화된 의미로 계산한 SHA-256 fingerprint 비교 |
| 일반 집합 | `TagContainer`: 중복 없는 명시 태그 집합, 최대 64개 |
| 동적 상태 | `TagCountContainer`: 최대 64종의 exact 및 부모 누적 카운트 저장 |
| 소유 모델 | 정의는 `TagContainer`, 여러 출처가 기여하는 gameplay subject만 `TagCountContainer` |
| 영속 저장 | 경로 문자열; 런타임 인덱스를 DB·세이브·버전 경계에 단독 저장하지 않음 |
| 네트워크 | fingerprint가 같은 세션에서 `ushort` 인덱스 사용 |
| 스레드 | 카탈로그는 동결 후 읽기 안전; 컨테이너는 단일 소유자 전제 |

## 3. 모듈과 seam

### 3.1 `TagCatalog`

공용 `Bun3.Gameplay`에 위치하는 불변 모듈이다. JSON 파싱, 문법 검증, 부모 생성,
결정적 인덱싱, 이름 해석, 계층 판정, fingerprint 계산을 작은 인터페이스 뒤에 숨긴다.

호스트는 파일 시스템 경로를 코어에 넘기지 않고 UTF-8 JSON 스트림을 제공한다. 서버의 파일
로더와 Unity의 `TextAsset`/프로젝트 파일 로더는 이 입력 seam의 어댑터이며, 실제 태그 의미와
검증은 두 환경 모두 같은 공용 구현을 사용한다. JSON 라이브러리 선택은 이 모듈의 구현
세부사항이며 공개 인터페이스가 아니다.

카탈로그의 주요 인터페이스는 다음 책임으로 제한한다.

- UTF-8 JSON에서 완성된 불변 카탈로그 생성
- 경로를 `GameplayTag`로 해석하는 `TryGet`과 필수 참조용 `GetRequired`
- wire 인덱스를 범위 검사해 복원하는 `TryGetByIndex`와 `GetRequiredByIndex`
- 태그의 표시 경로와 직접 부모 조회
- exact 비교와 조상·자손 관계 판정
- tag count와 fingerprint 제공
- 이 카탈로그에 결합된 `TagContainer`와 `TagCountContainer` 생성

파일 감시, 런타임 재로드, 런타임 등록은 제공하지 않는다.

### 3.2 `GameplayTag`

`GameplayTag`는 내부에 `ushort` 런타임 인덱스 하나를 갖는 값 타입이다. 기본값과 인덱스
0은 `None`이다. equality와 hash code는 인덱스 자체를 사용한다. raw `ushort`를 받는 public
constructor는 제공하지 않는다. `TryGetByIndex`는 `0..TagCount`를 허용하며 0을 `None`으로
복원하고, 범위를 벗어나면 false를 반환한다. `GetRequiredByIndex`는 같은 범위 밖에서
`ArgumentOutOfRangeException`을 던진다.

태그 값은 같은 카탈로그 안에서만 비교할 수 있다. 하나의 simulation world는 하나의 동결된
카탈로그를 사용하며, 서로 다른 카탈로그에서 얻은 `GameplayTag`를 섞지 않는 것이 인터페이스
불변식이다. 이 제약을 값마다 카탈로그 참조를 넣어 검사하지 않는다. 2바이트 값 타입과
핫패스 지역성을 유지하기 위한 의도적인 선택이다.

두 컨테이너는 `TagCatalog.CreateContainer(expectedExactKinds)`와
`TagCatalog.CreateCountContainer(expectedExactKinds)`를 통해 생성하고 해당 카탈로그 참조를
보유한다. 계층 판정과 mutation은 이 참조의 부모·subtree 배열을 사용하므로 호출마다 카탈로그를
전달하지 않는다. 컨테이너끼리 수행하는 `HasAny`/`HasAll`은 같은 카탈로그 인스턴스인지 검사하고
다르면 거부한다. 단일 `GameplayTag` 값은 출처를 싣지 않으므로 다른 카탈로그의 값을 섞지 않는
불변식은 호출자 책임이다.

### 3.3 `TagContainer`

Unreal의 `FGameplayTagContainer`에 대응하는 중복 없는 집합이다.

- 직접 보유하는 명시 태그는 최대 64개다.
- 같은 태그를 다시 추가해도 상태가 변하지 않는다.
- `HasExact`는 명시 태그만 검사한다.
- `Has`는 자식 태그 보유가 부모 태그 질의를 만족한다.
- `HasAny`, `HasAll`과 Exact 변형은 같은 방향의 계층 의미를 따른다.
- 빈 query에 대해 `HasAny`는 false, `HasAll`은 true이며 Exact 변형도 같다.
- `None`은 보유할 수 없고 `Has`와 `HasExact` 질의에는 false를 반환한다.
- `Remove`는 명시적으로 추가된 태그만 제거한다.
- 카운트와 부여 출처 의미는 갖지 않는다.

`Add`는 새로 삽입하면 true, 중복이면 false를 반환한다. `Remove`는 실제로 제거하면 true,
없으면 false를 반환한다. 두 mutation 모두 `None`에는 `ArgumentException`을 던지고, 새 65번째
종류 추가에는 `InvalidOperationException`을 던진다. 실패 시 상태는 변하지 않는다.

정의·분류·조건용 컨테이너가 주 사용처다. `AbilityDef`, `EffectDef`, `ItemDef`, required/blocked
조건과 태그 필터가 이에 해당한다.

### 3.4 `TagCountContainer`

Unreal GAS의 `FGameplayTagCountContainer`에 대응한다. 같은 태그를 여러 Ability, Effect,
장비 또는 게임 코드가 동시에 부여할 수 있는 gameplay subject의 현재 상태 저장소다.

exact와 aggregate count는 양의 32비트 signed integer 범위에서 관리한다.

- `Add(tag, count)`는 exact count와 모든 조상의 aggregate count를 증가시킨다.
- `Remove(tag, count)`는 실제 exact count에서 제거한 양만큼 자신과 모든 조상을 감소시킨다.
- 요청량이 exact count보다 크면 0까지만 제거한다.
- `ExactCount`는 직접 부여된 횟수만 반환한다.
- `Count`는 자신과 모든 자손에서 기여한 합을 반환한다.
- `HasExact`와 `Has`는 각각 해당 count가 0보다 큰지 검사한다.
- `HasAny`, `HasAll`과 Exact 변형은 `TagContainer`와 같은 계층 방향과 빈 query 의미를 따른다.
- `None`은 보유할 수 없고 `Has`와 `HasExact` 질의에는 false를 반환한다.
- `None` 또는 0 이하 count를 사용한 mutation은 거부한다.
- exact count가 양수인 명시 태그 종류는 최대 64개다. 새 65번째 종류 추가는 실패한다.
- 덧셈 overflow는 모든 변경을 사전 검사한 뒤 예외를 던져 부분 갱신을 남기지 않는다.

`Add`는 성공 시 반환값이 없고, `Remove`는 실제로 제거한 count를 반환한다. 존재하지 않는
태그의 유효한 `Remove`는 0을 반환한다. `None`에는 `ArgumentException`, 0 이하 count에는
`ArgumentOutOfRangeException`, 새 65번째 종류에는 `InvalidOperationException`, exact 또는
aggregate 덧셈 overflow에는 `OverflowException`을 던진다. 모든 예외 경로는 상태를 보존한다.

## 4. JSON 작성 형식

### 4.1 기본 스키마

게임 저장소는 하나의 태그 카탈로그 JSON을 소유한다. 프레임워크 패키지는 게임 도메인 태그를
포함하지 않고 스키마, 로더, 검증기와 에디터 도구만 제공한다.

```json
{
  "schemaVersion": 1,
  "tags": [
    {
      "name": "Ability.Movement.Jump",
      "comment": "기본 점프 능력"
    },
    {
      "name": "State.Dead.Ghost",
      "comment": ""
    }
  ],
  "redirects": [
    {
      "from": "State.Killed",
      "to": "State.Dead"
    }
  ]
}
```

`schemaVersion`과 `tags`는 필수다. v1에서 `schemaVersion`은 정수 `1`만 허용한다. `tags`는
빈 배열일 수 있다. `redirects`는 선택 필드이며 생략하면 빈 배열이다. tag 행의 `name`과
redirect 행의 `from`/`to`는 필수고, `comment`만 선택이다. schema에 없는 필드는 오타를
숨기지 않도록 거부한다.

`comment`는 선택 필드이며 Unicode를 허용한다. 태그 이름 제한은 `name`, redirect의 `from`과
`to`에만 적용한다. 배열 순서는 identity, 인덱스와 fingerprint에 영향을 주지 않는다. 명시된
노드의 표시 casing은 해당 선언을 사용한다. 선언되지 않은 암시적 부모는 canonical path로 먼저
오는 명시 descendant의 해당 세그먼트 표기를 사용한다. 따라서 암시 부모의 표시도 배열 순서에
영향받지 않는다. 에디터는 diff를 안정적으로 유지하도록 대소문자 무시 경로 순으로 기록한다.

### 4.2 이름 규칙

전체 경로는 다음 정규식과 일치해야 한다.

```regex
^[A-Za-z0-9]+(?:\.[A-Za-z0-9]+)*$
```

- 각 세그먼트는 ASCII 알파벳 또는 숫자 한 글자 이상이다.
- 숫자로만 된 세그먼트도 허용한다. `State.123`은 유효하다.
- `.`은 세그먼트 사이의 계층 구분자로만 허용한다.
- 공백, `_`, `-`, 비 ASCII 문자와 빈 세그먼트는 거부한다.
- `.`을 포함한 전체 경로는 255 ASCII 문자 이하여야 하고 계층 깊이는 16 이하여야 한다.
- 숫자 문자열을 수치로 정규화하지 않는다. `State.01`과 `State.1`은 다른 태그다.
- `State.Dead`와 `state.dead`는 같은 태그다.
- 대소문자만 다른 선언이 둘 이상 있으면 자동 병합하지 않고 중복 오류를 낸다.
- 유효한 단일 선언의 원래 대소문자는 에디터와 로그의 표시용 경로로 보존한다.

정체성 비교, 중복 검사, 정렬용 canonical path와 fingerprint는 ASCII 문자를 소문자로 접은
결과를 사용한다. 현재 문화권에 영향을 받는 비교나 변환은 사용하지 않는다.

### 4.3 암시적 부모

`State.Dead.Ghost`가 선언되면 `State`와 `State.Dead`를 암시적으로 생성한다. 작성 JSON에
부모를 반복해서 적을 필요가 없다. 부모를 명시한 경우 해당 행의 comment를 표시 정보로
사용한다. 암시적 부모와 명시적 부모는 런타임 의미가 동일하다.

명시 태그와 암시 부모를 합친 전체 노드 수가 65,535개를 넘으면 로드가 실패한다.

### 4.4 rename과 redirect

에디터에서 rename 또는 move를 수행하면 기존 전체 경로에서 새 활성 경로로 가는 redirect를
추가한다. redirect는 세이브·DB·이전 데이터의 문자열을 현재 태그로 해석하는 저빈도 경로에만
사용한다.

- `from`은 활성 태그와 겹치면 안 된다.
- `to`는 현재 활성 태그여야 한다.
- 대소문자만 바뀐 rename에는 redirect를 만들지 않는다.
- redirect chain과 cycle은 허용하지 않는다. 에디터는 항상 최종 활성 태그를 직접 가리킨다.
- redirect source 중복과 target 누락은 로드 오류다.

부모 노드를 rename 또는 move하면 그 노드와 모든 descendant의 전체 경로가 함께 바뀐다.
에디터는 이 변경을 한 트랜잭션으로 계산해 바뀐 모든 이전 활성 경로에서 새 활성 경로로 직접
redirect를 만든다. 기존 redirect가 이동된 subtree 안의 경로를 target으로 삼고 있었다면 그
target도 새 최종 경로로 다시 쓴다. 경로·redirect 충돌이 하나라도 생기면 JSON을 변경하지 않고
전체 작업을 실패시킨다.

## 5. 로드와 결정적 인덱싱

로드 순서는 다음과 같다.

1. JSON과 `schemaVersion`을 엄격하게 파싱한다.
2. 태그·redirect 문법, 대소문자 무시 중복과 redirect 관계를 검증한다.
3. 모든 암시적 부모 노드를 생성한다.
4. canonical path로 트리를 만들고 각 형제 노드를 ASCII 대소문자 무시 순으로 정렬한다.
5. 루트부터 deterministic preorder로 순회해 `1..N`의 `ushort` 인덱스를 부여한다.
6. 인덱스별 표시 이름, 직접 부모와 subtree end 배열을 생성한다.
7. 이름 해석용 대소문자 무시 해시 테이블을 생성한다.
8. 정규화된 의미 데이터로 fingerprint를 계산한다.
9. 완성된 카탈로그를 동결하고 모든 중간 mutable 상태를 버린다.

preorder로 각 subtree가 연속 구간이 되므로 다음 관계를 정수 비교만으로 판정할 수 있다.

```text
ancestor.Index <= tag.Index <= SubtreeEnd[ancestor.Index]
```

JSON 배열 순서, 공백, 들여쓰기, comment와 표시용 대소문자는 런타임 인덱스와 fingerprint를
바꾸지 않는다. 활성 canonical path, 계층, redirect와 schema version이 달라지면 fingerprint가
달라진다.

fingerprint 입력은 다음 canonical byte stream으로 고정한다. 모든 정수는 unsigned big-endian,
모든 문자열은 BOM 없는 UTF-8이며 문자열 길이는 byte 수를 나타내는 `uint32`다.

1. ASCII magic `BTAG`
2. `uint32 schemaVersion`
3. `uint32` 전체 활성 노드 수
4. 런타임 인덱스 순서로 각 canonical path의 길이와 bytes
5. `uint32` redirect 수
6. canonical `from` 순서로 각 redirect의 `from` 길이/bytes와 `to` 길이/bytes

이 byte stream의 SHA-256 digest 32바이트가 카탈로그 fingerprint다. comment, 명시·암시 부모
구분과 표시 casing은 simulation 의미가 아니므로 입력에 포함하지 않는다.

## 6. 런타임 데이터 구조와 성능 계약

### 6.1 카탈로그

카탈로그의 전역 데이터는 전체 태그 수에 비례하는 조밀 배열로 둔다. 5천 개와 5만 개 모두
같은 구현을 사용한다. `ushort` 부모 배열은 각각 약 10 KB와 100 KB이므로 프로세스당 하나인
카탈로그에서는 문제가 되지 않는다.

이름 해시 테이블은 JSON·세이브·설정 값을 `GameplayTag`로 바꾸는 저빈도 경로에만 사용한다.
이미 해석된 `GameplayTag`를 받는 simulation 핫패스는 이 테이블에 접근하지 않는다.

### 6.2 컨테이너

전체 65,535개에 비례하는 bitset이나 count 배열을 각 객체에 두지 않는다. 전체 bitset 하나는
컨테이너당 약 8 KiB이며 객체가 많을 때 working set을 불필요하게 키운다.

`TagContainer`는 최대 64개의 명시 인덱스를 오름차순 배열로 저장한다. `HasExact`는 이 배열을
이진 탐색한다. 계층 `Has`는 질의 태그 인덱스의 lower bound를 찾고 그 후보가 카탈로그의
`SubtreeEnd` 안에 있는지만 검사한다. 따라서 별도 부모 캐시 없이 단일 태그 질의당 최대 7회의
인덱스 비교로 끝나며 조회 중 할당하지 않는다.

`TagCountContainer`는 활성 exact 태그와 조상만 인덱스순 배열에 저장하고, 하나의 entry에서
exact count와 aggregate count를 함께 관리한다. 깊이 16, exact 종류 64 제한에 따라 entry는
최대 1,024개다. 단일 `ExactCount`/`Count`/`Has` 질의는 이진 탐색의 최대 11회 인덱스 비교로
끝난다. 해시 충돌, load factor와 무제한 probe에 따른 지연 편차가 없다.

`Add`와 `Remove`는 최대 16개의 조상 인덱스를 수집하고 count·종류 상한을 모두 사전 검증한 뒤,
정렬 entry 배열을 최대 한 번 merge 또는 compact한다. CPU 작업은 기존 entry 최대 1,024개와
조상 최대 16개로 제한되며 부분 갱신을 남기지 않는다. 생성 시 `expectedExactKinds`로 용량을
예약할 수 있고, 예약 범위 안의 mutation은 할당하지 않는다.

| 연산 | 목표 비용 |
|---|---|
| `GameplayTag` equality/hash | O(1) |
| 부모·조상 판정 | O(1) |
| `TagContainer` 단일 태그 조회 | O(log M), 최대 7회 인덱스 비교 |
| `TagContainer.HasAny/HasAll` | O(Q log M), 최대 7Q회 인덱스 비교 |
| `TagCountContainer` 단일 태그 조회 | O(log K), 최대 11회 인덱스 비교 |
| `TagCountContainer.HasAny/HasAll` | O(Q log K), 최대 11Q회 인덱스 비교 |
| `TagCountContainer.Add`, `Remove` | O(K + D), 최대 1회 배열 merge/compact |

`M`은 명시 태그 수로 최대 64, `K`는 exact 태그와 조상 entry 수로 최대 1,024, `D`는 태그
깊이로 최대 16이다. `HasAny`/`HasAll`은 query의 각 명시 태그에 이 단일 태그 비용을 적용한다.
모든 조회는 steady-state 할당 0이어야 한다. 내부 배열은 공개 인터페이스 밖에 숨기고 .NET과
Unity 실측으로 검증한다.

### 6.3 성능 검증 행렬

구현 완료 조건에 다음 benchmark와 allocation gate를 포함한다.

- 카탈로그 크기 `N`: 5,000 / 50,000
- 명시 태그 수 `M`: 8 / 32 / 64
- 계층 깊이 `D`: 1 / 4 / 8 / 16
- exact hit, parent hit, miss를 분리
- 단일 조회와 프레임당 10만 회 batch를 분리
- `Add`/`Remove`와 read-heavy 혼합 workload를 분리
- .NET 서버와 Unity Mono/IL2CPP에서 p50/p95/p99 시간과 GC allocation 측정
- 조회 경로 `GC Alloc = 0`
- instrumented test로 7/11회 조회 비교 상한과 mutation의 16단계·1회 merge 상한 검증

절대 시간 임계값은 구현 머신에 종속시키지 않는다. 대신 현재 `Dictionary + 모든 보유 태그의
부모 순회` 구현과 동일 동작 baseline을 같은 프로세스에서 함께 측정하고, 모든 read-heavy
케이스의 p50/p95/p99와 10만 회 batch가 회귀하지 않아야 한다. 타이밍 분포와 별개로 위 비교·순회
상한과 allocation gate는 반드시 통과해야 한다. 이 구조적 상한이 일정한 성능의 완료 기준이며,
평균 시간만 빠른 해시 구현으로 대체하지 않는다.

## 7. 소유 모델

모든 GameplayObject가 카운트 저장소를 갖지 않는다. 필요한 저장소는 태그의 의미로 결정한다.

| 대상 | 보유 태그 형태 |
|---|---|
| `Unit` 또는 ASC에 대응하는 gameplay subject | 하나의 중심 `TagCountContainer` |
| `AbilityDef` | 분류·required·blocked용 `TagContainer` |
| `EffectDef`/`EffectSpec` | asset 분류·granted·required용 `TagContainer` |
| `ItemDef` | 정적 분류·필터용 `TagContainer` |
| `ItemInstance` | 기본적으로 별도 count 저장소 없음 |
| 가변 태그 스택이 실제로 필요한 ItemInstance | 선택적 item 전용 count/stack 저장소 |

Ability 실행 중 owned tag, 지속 Effect의 granted tag, 장비 기여와 loose tag는 gameplay
subject의 중심 `TagCountContainer`를 증감한다. Ability와 Effect 자체의 분류 태그는 subject
상태로 자동 복사하지 않는다.

## 8. 서버·클라이언트·영속화

### 8.1 서버와 Unity

서버와 클라이언트는 같은 semantic JSON에서 같은 인덱스와 fingerprint를 생성한다. 세션 또는
락스텝 시뮬레이션 시작 전에 fingerprint를 비교하고 다르면 진행을 거부한다. 이는 태그 이름뿐
아니라 계층과 redirect 해석이 다른 실행을 막는다.

fingerprint가 일치한 연결 안에서는 태그를 `ushort`로 전송한다. 네트워크 메시지마다 문자열을
보내지 않는다. 수신 decoder는 public raw constructor 대신 카탈로그의 `TryGetByIndex`를
사용한다. `TagCount`보다 큰 wire 값은 프로토콜 오류로 해당 메시지를 거부하며, 연결 종료 여부는
호스트의 기존 malformed-message 정책을 따른다. 범위 밖 값을 simulation에 전달하지 않는다.

### 8.2 DB·세이브·리플레이

런타임 인덱스는 카탈로그 내용이 바뀌면 달라질 수 있으므로 버전 경계를 넘는 영구 식별자가
아니다. DB와 세이브에는 전체 경로 문자열을 저장하고 로드 시 redirect를 거쳐 해석한다.

동일 fingerprint를 함께 저장하고 재생하는 고정 버전 리플레이는 `ushort`를 사용할 수 있다.
fingerprint가 없거나 다르면 경로 기반 migration 없이는 재생하지 않는다.

## 9. Unity 에디터 어댑터

`com.bun3.gameplay` UPM의 Unity 전용 Editor 어셈블리는 공용 카탈로그의 작성 어댑터를
제공한다. v1 UI는 Unreal의 기본 관리 경험에서 다음 기능을 발췌한다.

- 검색 가능한 계층 트리
- 루트 태그와 자식 태그 추가
- rename과 move
- 안전한 삭제
- comment 편집과 tooltip 표시
- raw JSON 변경 후 reload/validation
- 오류 위치와 원인을 보여 주는 validation 창

에디터는 동일한 공용 로더로 저장 결과를 다시 읽어 검증한 뒤 기록한다. rename/move는 redirect를
갱신한다. 삭제는 기본적으로 leaf tag만 허용하고 확인을 요구한다. 자식이 있는 노드는 명시적인
subtree 삭제를 선택해야 하며, 외부 데이터에 남은 경로는 각 데이터 로더의 `GetRequired`
검증에서 미등록 참조 오류로 드러난다. JSON은 사람이 직접 수정할 수 있으며 에디터 전용 asset
형식으로 변환하지 않는다.

서버와 공용 코어는 Unity 타입이나 에디터 어셈블리를 참조하지 않는다.

## 10. 오류 처리

다음 오류는 기동·에디터 validation에서 fail-fast한다.

- 잘못된 JSON 또는 지원하지 않는 schema version
- 알 수 없는 필드나 누락된 필수 필드
- 태그 문법 위반
- 전체 경로 255자 또는 깊이 16 초과
- 대소문자만 다른 중복을 포함한 tag 중복
- 암시 부모 포함 65,535개 초과
- 활성 tag와 redirect source 충돌
- redirect target 누락, 중복, chain 또는 cycle

미등록 문자열은 자동 등록하지 않는다. 선택적 데이터에는 `TryGet`, 반드시 존재해야 하는
코드·설정 참조에는 오류에 전체 경로를 포함하는 `GetRequired`를 사용한다.

컨테이너 mutation 오류는 변경 전 상태를 보존한다. 특히 aggregate count overflow는 조상
전체를 사전 검사한 다음 한 번에 반영한다.

## 11. 스레드와 Burst

완성된 `TagCatalog`는 불변이므로 여러 스레드에서 읽을 수 있다. `TagContainer`와
`TagCountContainer`는 simulation owner 한 곳이 mutation하는 것을 전제로 하며 자체 locking을
제공하지 않는다.

Burst는 공용 코어의 필수 의존성이 아니다. Unity simulation이 실제로 Job/Burst로 이동할 때
동일한 `ushort` 인덱스와 카탈로그 배열을 `NativeArray` 형태로 노출하는 Unity 전용 읽기
어댑터를 추가한다. 런타임 카탈로그가 동결되어 있으므로 generation invalidation이나 동적
재빌드는 필요하지 않다.

## 12. 테스트

### 12.1 카탈로그 계약

- 유효·무효 이름 행렬과 숫자 전용 세그먼트
- 전체 경로 255/256자와 깊이 16/17 경계
- 대소문자 무시 lookup과 대소문자 중복 오류
- `State.01`과 `State.1` 구분
- 암시 부모 생성과 표시 casing 보존
- JSON 배열 순서·공백·comment·casing 변화에 독립적인 인덱스와 fingerprint
- semantic tag·redirect 변화에 따른 fingerprint 변화
- 동일 fixture의 .NET/Unity 인덱스·부모·subtree end·fingerprint 일치
- 65,535개 성공과 65,536개 실패
- 미등록 tag의 `TryGet` 실패와 `GetRequired` 예외
- wire index 0/최댓값/범위 초과 복원과 raw public constructor 부재
- 런타임 등록 인터페이스가 존재하지 않음

### 12.2 컨테이너 계약

- `Has`와 `HasExact`의 계층 방향
- `HasAny`/`HasAll`과 Exact 변형, 빈 query 의미
- `None` mutation 거부와 단일 질의 false
- 서로 다른 카탈로그의 컨테이너 query 거부
- `TagContainer` 중복 Add와 명시 Remove
- 두 컨테이너 각각 최대 64종과 65번째 추가 실패의 원자성
- `None`, 0 이하 count, overflow의 정확한 예외 타입과 상태 보존
- 여러 출처가 같은 tag를 부여한 count 의미
- 형제 tag가 공통 부모에 기여하는 aggregate count
- 일부·초과 Remove와 조상 count 일관성
- exact 및 aggregate overflow의 원자성
- 조회 hot path allocation 0

### 12.3 통합 계약

- 서버와 Unity가 같은 JSON으로 같은 결과 생성
- fingerprint 불일치 handshake 거부
- 문자열 저장→redirect→현재 tag 복원
- 부모 rename/move 시 모든 descendant와 기존 redirect target의 원자적 갱신
- Ability/Effect/장비 기여의 추가·제거 후 subject 상태 일관성

## 13. 기존 구현에서의 전환

- mutable `TagRegistry`와 `GetOrRegister`를 제거하고 불변 `TagCatalog`로 교체한다.
- `GameplayTag.Handle`의 공개 `int` 표현을 숨기고 내부 `ushort` 인덱스로 교체한다.
- 현재 `TagSet`은 역할을 분명히 하기 위해 `TagCountContainer`로 대체한다.
- 일반 분류·조건용 `TagContainer`를 별도로 추가한다.
- 기존 동적 등록 호환 wrapper는 제공하지 않는다. 새 요구와 반대되는 동작을 남기지 않는다.
- 패키지 공개 계약이 바뀌므로 NuGet과 UPM 버전을 함께 `0.5.0`으로 올린다.

## 14. 비목표

- 런타임 태그 등록과 모딩 카탈로그 병합
- 65,535개를 넘는 태그 또는 catalog sharding
- 64비트 hash를 GameplayTag의 영구 정체성으로 사용
- 전체 카탈로그 binary artifact 생성
- 전체 태그 C# source generation
- 복합 `GameplayTagQuery` 표현식 언어
- tag count 변화 event/delegate 구독
- Burst용 mutable container
- Unity 외 에디터 UI

## 15. 기각한 대안

### 15.1 64비트 hash-only identity

카탈로그 변경에도 값이 유지되지만 `GameplayTag`가 8바이트가 되고, 부모·이름 조회에
hash-to-entry lookup이 필요하며 충돌 검증과 영구 hash 계약이 추가된다. 런타임 등록이 없고
영속 정본이 문자열인 현재 요구에서는 복잡성의 대가를 회수하지 못한다.

### 15.2 JSON 외 compact binary 산출물

기동 파싱 비용과 런타임 JSON 의존성을 줄일 수 있지만 별도 파일의 생성·배포·동기화가 필요하다.
현재는 단일 JSON 관리 단순성을 우선하며, 실제 기동 시간이 문제가 될 때 같은 의미 모델에서
추가할 수 있다.

### 15.3 컨테이너별 전체 bitset/dense count 배열

조회는 매우 빠르지만 65,535개 기준 bitset 하나가 약 8 KiB이고 dense `int` count 배열은 약
256 KiB다. gameplay subject 수만큼 반복되는 비용이 크므로 기본 표현으로 사용하지 않는다.
