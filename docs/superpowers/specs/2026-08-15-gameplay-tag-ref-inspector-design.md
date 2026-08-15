# GameplayTagRef Inspector 설계

## 목표

Unity 직렬화 자산에는 안정적인 canonical 태그 경로를 저장하고, Inspector에서는 기존 병합
GameplayTag Picker를 사용해 선택한다. 런타임의 2바이트 `GameplayTag` 인덱스 handle은 변경하지 않는다.

## 핵심 결정

### `GameplayTagRef`와 `GameplayTag`를 분리한다

`GameplayTag`는 한 `TagCatalog` 안에서만 의미가 있는 2바이트 인덱스다. Catalog에 태그가 추가되거나
삭제되면 같은 인덱스가 다른 태그를 가리킬 수 있으므로 Unity 자산에 직접 직렬화하지 않는다.

Unity 전용 authoring reference인 `GameplayTagRef`를 새로 제공한다.

```csharp
[SerializeField]
private GameplayTagRef _attackTag;
```

- 직렬화 정본은 `ability.attack` 같은 canonical 경로 문자열이다.
- 기본값과 빈 문자열은 `None`이다.
- 새 코드에서 생성한 값과 Picker 선택 결과는 소문자 canonical 경로다.
- 기존 자산의 raw 문자열은 삭제하거나 조용히 다른 값으로 교체하지 않는다.
- 런타임 변환은 `TryResolve(TagCatalog, out GameplayTag)` 또는
  `ResolveRequired(TagCatalog)`로 명시적으로 수행한다.
- 전역 Catalog를 암묵적으로 찾거나 Play/Edit 상태에 따라 다른 Catalog를 선택하지 않는다.

### Unity Adapter assembly를 둔다

코어 `Bun3.Gameplay` assembly는 `noEngineReferences`와 .NET 서버 호환성을 유지한다.
`GameplayTagRef`는 새 runtime assembly `Bun3.Gameplay.Unity`에 둔다.

```text
Bun3.Gameplay                 UnityEngine 비의존 코어, GameplayTag/TagCatalog
       ^
       |
Bun3.Gameplay.Unity           Unity 직렬화 Adapter, GameplayTagRef
       ^
       |
Bun3.Gameplay.Editor          PropertyDrawer와 Picker 연결
```

`GameplayTagRef`의 namespace는 호출자가 기존 tag namespace 하나만 사용하도록
`Bun3.Gameplay.Tags`로 유지한다. NuGet 코어 빌드에서는 Unity Adapter 소스를 제외하고, UPM 패키지에는
adapter assembly와 `.meta`를 포함한다.

## Inspector interface

한 줄 field는 label, 현재 raw 경로를 표시하는 dropdown, `None` clear 버튼으로 구성한다.

- dropdown 클릭 시 기존 `GameplayTagPickerWindow.ShowLive`를 연다.
- Picker는 Source root가 없는 병합 Runtime Catalog tree, 이름 filter, 검색 중 자동 expand, 이전 expand 복원,
  수직·수평 scroll을 그대로 사용한다.
- 선택 결과는 canonical 경로 문자열이며 모든 선택 대상에 `SerializedProperty`로 적용한다.
- 다중 object에서 값이 다르면 mixed value를 표시하고, 새 선택 또는 clear는 모든 대상에 적용한다.
- `SerializedObject.ApplyModifiedProperties` 경로를 사용해 Undo와 prefab override를 보존한다.
- Workspace가 invalid해도 기존 raw 문자열은 계속 표시한다. Picker는 diagnostics를 보여 주고 신규 선택만
  비활성화한다.
- 문법 오류 또는 현재 Catalog에 없는 raw 경로는 warning icon과 tooltip으로 표시한다.
- 빈 값은 유효한 `None`이며 경고하지 않는다.

Inspector의 Workspace 조회는 고정 `ProjectSettings/GameplayTags.json`과 기존 development resolver를 사용한다.
파일 IO와 compile을 매 OnGUI마다 반복하지 않도록 짧은 공유 cache를 두되, cache는 last-good Catalog로
대체하지 않고 현재 Workspace 상태를 그대로 보존한다.

## 런타임 의미

`GameplayTagRef` 생성자는 입력을 검증하고 소문자로 canonicalize한다. Unity 역직렬화로 들어온 raw 값은
getter에서 변경하지 않는다.

```csharp
var reference = new GameplayTagRef("Ability.Attack");
reference.Path;                         // "ability.attack"
reference.TryResolve(catalog, out tag); // 현재 Catalog/Redirect 기준
```

- empty reference의 `TryResolve`는 `GameplayTag.None`과 함께 true다.
- 문법이 잘못됐거나 Catalog에 없는 경로는 `TryResolve`가 false다.
- `ResolveRequired`는 empty면 `None`, 문법 오류면 `ArgumentException`, 미등록 경로면
  `KeyNotFoundException`을 던진다.
- equality와 hash는 저장된 경로의 ordinal 비교다.

## 변경 및 패키지 경계

- `GameplayTag` layout, binary Catalog, Source JSON schema는 변경하지 않는다.
- `GameplayTagRef`는 Unity 자산 전용이며 server hosting이나 Published Catalog 포맷에 포함되지 않는다.
- 구조화 reference search/migration은 이번 범위에 포함하지 않는다.
- `GameplayTagContainer` 또는 복수 태그 Inspector는 이번 범위에 포함하지 않는다.
- additive Unity interface 추가이므로 Gameplay NuGet/UPM 버전을 `0.11.0`으로 올린다.
- UPM description은 코어가 UnityEngine 비의존이고 별도 Unity Adapter가 있음을 정확히 표현한다.

## 검증

- `GameplayTagRef` canonicalization, empty, resolve, unknown, invalid, equality를 Unity EditMode에서 검증한다.
- `SerializedProperty`를 통한 단일/다중 대상 선택, clear, Undo를 실제 `ScriptableObject`로 검증한다.
- invalid Workspace에서 raw 값 보존과 Picker 선택 차단을 검증한다.
- 기존 Picker filter/expand/scroll 테스트를 유지한다.
- generated Unity runtime/editor/test project를 warning-as-error로 빌드한다.
- 전체 Unity EditMode를 실행하고 C#/GUI diagnostics가 0인지 확인한다.
- UPM archive에 Unity Adapter C#과 대응 `.meta`가 정확히 한 번 포함되고 version이 `0.11.0`인지 확인한다.
