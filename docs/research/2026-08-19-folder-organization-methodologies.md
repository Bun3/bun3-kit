# 폴더/파일 구성 방법론 조사 — 1차 사료 기준

- 조사일: 2026-08-19
- 방법: 각 방법론의 원전(공식 문서·원저자 블로그·실제 소스코드)을 직접 열어 확인. 2차 요약 블로그는 근거로 쓰지 않음.
- dotnet/runtime 파일 실태는 GitHub API(`repos/dotnet/runtime/contents`, `git/trees`)와 raw 파일로 직접 확인함.

---

## 1. Package-by-feature vs Package-by-layer

### 1-1. javapractices.com — "Package by feature, not layer" (원전급 권고 글)

- 출처: http://www.javapractices.com/topic/TopicAction.do?Id=205 (John O'Hanley 운영)
- 핵심 규칙: **한 기능에 관련된 모든 항목(그 기능만의 항목)을 하나의 디렉터리/패키지에 둔다.**

원문 인용:

> "Package-by-feature uses packages to reflect the feature set. It tries to place all items related to a single feature (and only that feature) into a single directory/package. This results in packages with high cohesion and high modularity, and with minimal coupling between packages."

> "…deleting a feature can reduce to a single operation - deleting a directory. (Deletion operations might be thought of as a good test for maximum modularity…)"

> "package-by-feature aggressively prefers package-private as the default scope, and only increases the scope of an item to public only when needed."

package-by-layer에 대한 비판 원문:

> "Here, each feature has its implementation spread out over multiple directories… This results in packages with low cohesion and low modularity, with high coupling between packages. As a result, editing a feature involves editing files across different directories."

결론(원문 소제목): **"Recommendation: Use Package By Feature"** — 단, "typical business applications" 전제이며, 기능 간 참조 자체를 금지하는 것은 아니고 가시성 최소화(package-private 기본)를 요구한다.

### 1-2. Spring Boot 공식 문서 — "Structuring Your Code"

- 출처: https://docs.spring.io/spring-boot/reference/using/structuring-your-code.html
- 공식 예제("typical layout")가 **도메인(기능)별 패키지**를 보여준다: `customer/` 아래 `Customer, CustomerController, CustomerService, CustomerRepository`, `order/` 아래 `Order, OrderController, OrderService, OrderRepository`. 즉 controller/service/repository 레이어 폴더가 아니라 customer/order 기능 폴더.
- 메인 클래스 규칙 원문: "We generally recommend that you locate your main application class in a root package above other classes." (`@SpringBootApplication`의 컴포넌트 스캔 기준점이 되기 때문)

### 1-3. Angular 공식 스타일 가이드

- 출처: https://angular.dev/style-guide (공식, 구 angular.io 스타일 가이드의 "Folders-by-feature structure" 항목을 계승)
- 원문 인용:

> "Organize your project by feature areas"

> "Avoid creating subdirectories based on the type of code that lives in those directories. For example, avoid creating directories like `components`, `directives`, and `services`."

- 예시로 영화관 앱의 `show-times/`, `reserve-tickets/` 기능 폴더를 제시. **타입(종류)별 폴더를 명시적으로 금지 수준으로 회피 권고**하는 것이 특징.

**정리**: Java(javapractices) → Spring Boot 공식 예제 → Angular 공식 가이드 모두 같은 방향: 최상위 분기는 기능, 레이어(타입)별 최상위 분기는 비권장. 근거는 응집도·삭제 용이성·가시성 축소.

---

## 2. Screaming Architecture — Robert C. Martin (2011)

- 출처: https://blog.cleancoder.com/uncle-bob/2011/09/30/Screaming-Architecture.html
- 핵심 주장: **소스 최상위 구조를 보면 "무엇을 하는 시스템인지"가 비명을 지르듯 드러나야 한다.** 건물 도면을 보면 집인지 도서관인지 알 수 있듯이, 헬스케어 시스템 코드베이스를 처음 본 개발자는 프레임워크를 배우기 전에 "이건 헬스케어 시스템이다"라고 알아봐야 한다.
- 원문 인용:

> "Architectures are not (or should not be) about frameworks. Architectures should not be supplied by frameworks."

> "The Web is a delivery mechanism, and your application architecture should treat it as such."

- 즉 최상위 구조가 Rails/Spring/ASP 같은 프레임워크나 전달 수단(웹)을 외치면 잘못이고, 유스케이스·도메인을 외쳐야 한다. 프레임워크는 세부사항으로 밀어내고 유스케이스 중심 구조를 지키라는 결론.

---

## 3. Vertical Slice Architecture — Jimmy Bogard (2018)

- 출처: https://www.jimmybogard.com/vertical-slice-architecture/
- 핵심 규칙 원문:

> "Minimize coupling between slices, and maximize coupling in a slice."

- 요청(request) 단위로 코드를 세로로 자른다. 각 슬라이스는 UI부터 영속성까지 자기 요청 처리를 스스로 결정하며, 슬라이스마다 동일한 패턴을 강제하지 않는다.
- 레이어 강제("Controller MUST talk to a Service that MUST use a Repository")가 만들어내는 추상화 대부분은 슬라이스 구조에서는 사라진다고 주장.
- 기능 추가에 대한 원문:

> "New features only add code, you're not changing shared code and worrying about side effects."

- 공유 코드는 최소화하고, 각 슬라이스는 단순한 Transaction Script로 시작해 코드 냄새가 나타날 때 도메인 모델로 리팩터링한다(팀의 리팩터링 역량이 전제 조건).

---

## 4. .NET 진영 실태

### 4-1. Framework Design Guidelines — 네임스페이스 명명

- 출처: https://learn.microsoft.com/en-us/dotnet/standard/design-guidelines/names-of-namespaces (Cwalina & Abrams, FDG 2판 전재)
- 템플릿 원문: `` `<Company>.(<Product>|<Technology>)[.<Feature>][.<Subnamespace>]` ``
- 주요 DO/DON'T:
  - DO: 회사명 접두 + 버전 독립적 제품명. PascalCasing. 적절하면 복수형(`System.Collections`).
  - DON'T: 조직도 기반 이름 금지. **네임스페이스와 그 안의 타입에 같은 이름 금지** ("do not use `Debug` as a namespace name and then also provide a class named `Debug`").
  - DON'T: `Element`, `Node`, `Log`, `Message` 같은 범용 타입명 금지(충돌 위험 — `FormElement`, `XmlNode`처럼 한정).
- FDG는 **폴더 규칙은 명시하지 않는다**(네임스페이스·어셈블리 명명만 다룸). 폴더=네임스페이스 정렬은 도구 차원의 기본값으로 확립됨:
  - **IDE0130 "Namespace does not match folder structure"** — 옵션 `dotnet_style_namespace_match_folder`의 기본값이 `true`. 출처: https://learn.microsoft.com/en-us/dotnet/fundamentals/code-analysis/style-rules/ide0130

### 4-2. dotnet/runtime 실태 — 폴더=네임스페이스, 동명 제네릭/비제네릭 파일 배치

`src/libraries/System.Private.CoreLib/src` 전체 트리(1,843개 경로)를 GitHub API로 직접 확인(2026-08-19, main 브랜치).

- **폴더=네임스페이스 정렬**: `System/`, `System/Collections/`, `System/Collections/Generic/` 등 폴더가 네임스페이스를 그대로 미러링.
- **백틱 파일명은 0건**: CoreLib src 1,843개 경로 중 `` ` ``를 포함한 파일명은 하나도 없다. `` Tuple`1.cs `` 같은 metadata 방식 파일명은 BCL 소스에 실재하지 않는다.
- **제네릭/비제네릭 동명 타입은 한 파일에 함께 둔다**:
  - `System/Tuple.cs` 1개 파일에 `static class Tuple` + `Tuple<T1>` ~ `Tuple<T1,…,TRest>` 제네릭 클래스 8개가 전부 들어 있다.
    https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Tuple.cs
  - `System/Action.cs` 1개 파일에 `Action`(0~16개 인자 델리게이트 17개) + `Comparison<T>` + `Converter<TInput,TOutput>` + `Predicate<T>`까지 들어 있다 — "파일명 = 첫 타입명" 규칙조차 엄격히 지키지 않고, 관련 소형 타입 묶음을 한 파일에 둔다.
    https://github.com/dotnet/runtime/blob/main/src/libraries/System.Private.CoreLib/src/System/Action.cs
  - `System/Nullable.cs` 1개 파일에 `Nullable<T>` 구조체 + `static class Nullable`.
- **동명 파일은 폴더(=네임스페이스)로 구분**: 비제네릭 `IEnumerable`은 `System/Collections/IEnumerable.cs`, 제네릭 `IEnumerable<T>`는 `System/Collections/Generic/IEnumerable.cs` — 파일명은 동일하고 폴더가 구분자다. `ICollection/IList/IDictionary/IEnumerator`도 동일 패턴.
- **아리티 충돌 시 예외적 명명**: `Lazy<T>`는 `Lazy.cs`, `Lazy<T,TMetadata>`는 `LazyOfTTMetadata.cs` — 백틱도 `{T}`도 아닌 "OfT" 서술식 파일명을 쓴 사례.
- **partial 분할 관례**: `Type.측면.cs` 패턴이 광범위 — `MemoryExtensions.cs` / `MemoryExtensions.Trim.cs` / `MemoryExtensions.Globalization.cs`, `SpanHelpers.cs` / `SpanHelpers.Byte.cs` / `SpanHelpers.BinarySearch.cs` / `SpanHelpers.T.cs` 등.
- runtime의 코딩 가이드(https://github.com/dotnet/runtime/blob/main/docs/coding-guidelines/coding-style.md)에는 파일명 규칙이 아예 없다(스타일은 EditorConfig로 강제, 파일 배치는 관례).

---

## 5. Unity 공식 — 프로젝트 구성 가이드

- 출처: https://unity.com/how-to/organizing-your-project (e-book "Best practices for organizing your Unity project" 요약 페이지, 원문 직접 확인)
- **공식 예시는 asset-type 폴더다.** 원문 인용:

> "While there is no set folder structure, the following two sections show examples of how you might set up your Unity project. These structures are **both based on splitting up your project by asset type**."

> "If you download one of the Template or Starter projects from the Unity Hub, you'll notice that the subfolders are split up by asset type."

- 단, 단일 정답은 없다고 명시: "Although there is no single way to organize a Unity project, here are some key recommendations:" — 이어지는 권고: 컨벤션 문서화, 이름에 공백 금지(CamelCase), 실험용 sandbox 폴더 분리, 루트 레벨 추가 폴더 회피, 서드파티 에셋과 자체 에셋 분리, 빈 폴더 주의(.keep/.meta 이슈).
- **코드에 대해서는 네임스페이스=폴더 정렬을 권고**. 원문 인용:

> "Note: When using namespaces in your code, break up your folder structure by namespace for better organization."

- 즉 Unity 공식 입장: **에셋은 타입별 폴더(공식 예시 2종 모두), 코드는 네임스페이스 미러링 폴더.** feature 폴더를 금지하지는 않지만 공식 예시로 제시하지도 않는다.
- 패키지(UPM) 코드의 최상위 구조는 별도 관례가 확립돼 있다 — `package.json` + `Runtime/`, `Editor/`, `Tests/`, `Samples~/`, `Documentation~/`: https://docs.unity3d.com/Manual/cus-layout.html ("This is only a convention and doesn't affect the asset import pipeline.")

---

## 6. StyleCop Analyzers SA1649 — 제네릭 파일명 규정

- 출처: https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/SA1649.md , https://github.com/DotNetAnalyzers/StyleCopAnalyzers/blob/master/documentation/Configuration.md
- 규칙명: **SA1649 FileNameMustMatchTypeName** — "A violation of this rule occurs when the file name of a C# file does not contain the name of the first type in the file." (partial 타입은 예외로 제외됨)
- 제네릭 파일명 원문(SA1649.md):

> "For generics that are defined as `Class1<T>` the name of the file needs to be `Class1{T}.cs` or `` Class1`1.cs `` depending on the `fileNamingConvention` setting."

- 두 컨벤션(Configuration.md의 표):

| `fileNamingConvention` 값 | `Class1<T1,T2,T3>`의 파일명 |
|---|---|
| `stylecop` | `Class1{T1,T2,T3}.cs` |
| `metadata` | `` Class1`3.cs `` |

- 기본값 원문: **"When the `fileNamingConvention` property is not set, the `stylecop` convention is used as default."** → 기본은 `{T}` 중괄호 방식.
- 참고: 4-2에서 확인했듯 BCL 자체는 어느 쪽도 따르지 않는다(제네릭을 비제네릭 동반 타입과 같은 `Tuple.cs`류 파일에 합침). SA1649 컨벤션은 "타입당 1파일 + 파일명=타입명"을 강제하는 팀을 위한 규정이다.

---

## 이 레포(bun3-kit Unity 패키지)에 적용 시 시사점

현재 구조: `unity/Packages/com.bun3.unity.ui/Runtime/Popups/{Core,Stack,Services,Manager}`, 전 파일 단일 네임스페이스 `Bun3.Unity.UI.Popups`, partial 분할(`PopupStack.Push.cs`, `Popup.CloseScope.cs` 등), 제네릭 파일 `Popup{TResult}.cs`.

- **package-by-feature (javapractices/Spring/Angular)**: `Popups/`가 곧 feature 패키지라 대원칙과 정확히 일치 — 폴더째 삭제 가능, 팝업 관련 코드가 한 곳에 응집. 내부의 `Core|Stack|Services|Manager`는 feature 내부의 역할(mini-layer) 분기인데, Angular의 "avoid `components`/`services` 폴더" 기준으로는 어긋나는 지점. 단 원전들의 비판 대상은 "기능이 여러 최상위 레이어 폴더에 흩어지는 것"이므로, 단일 feature 내부 30여 파일의 가독용 소분류는 비판의 본체가 아니다.
- **Screaming Architecture (Martin)**: 패키지 이름과 최상위 폴더가 `Popups`를 외치므로 부합. 이 레포 자체가 "전달 수단(프레임워크) 코드"라서 게임 도메인을 외칠 수는 없고, Martin의 기준은 오히려 이 패키지를 소비하는 게임 레포 쪽에 적용된다 — 게임 레포 최상위가 `Popups/UI/Network`가 아니라 게임 유스케이스를 외치게 하는 것이 이 원칙의 실천.
- **Vertical Slice (Bogard)**: 요청 단위 앱 아키텍처라 재사용 라이브러리에는 직접 적용 대상이 아니다. 다만 "slice 내부 결합 극대화" 원칙은 팝업 관련 전부를 `Popups/` 아래 두는 현 구조를 지지하고, "공유 추상화 최소화"는 Core의 인터페이스/델리게이트를 실제 필요분만 유지하라는 경계로 읽힌다.
- **.NET FDG + dotnet/runtime 실태**: 어긋나는 지점 하나 — `Core/Stack/Services/Manager` 폴더가 네임스페이스에 반영되지 않아(전부 `Bun3.Unity.UI.Popups`) IDE0130 기본값(폴더=네임스페이스) 기준으로는 위반이다. BCL이라면 폴더를 없애고 평평하게 두거나(runtime의 `System/` 직속처럼 파일명·partial 접미사로 구분) 폴더당 서브네임스페이스를 팠을 것. 반면 partial 분할 명명(`PopupStack.Push.cs`)은 `MemoryExtensions.Trim.cs`와 동일 패턴으로 BCL 실태와 정확히 일치. `Popup`/`Popup<TResult>`를 BCL식으로 하면 한 `Popup.cs`에 합치는 것도 정당한 선택지다.
- **Unity 공식**: UPM 패키지 레이아웃(`Runtime/` 하위 코드)은 공식 관례 그대로. asset-type 폴더 권고는 Assets(에셋) 대상이라 코드 패키지엔 해당 없음. 코드에 대한 Unity 권고("네임스페이스로 폴더를 나눠라")는 위 IDE0130과 같은 방향이므로, 소분류 폴더를 유지하려면 네임스페이스를 맞추고, 단일 네임스페이스를 유지하려면 폴더를 줄이는 쪽이 원전들과 정합적이다.
- **SA1649**: `Popup{TResult}.cs`는 StyleCop 기본(stylecop) 컨벤션과 정확히 일치 — 백틱(metadata) 방식은 기본값도 아니고 BCL 실사례도 없으므로 채택할 이유 없음. 현재 명명 유지가 맞다.
