# Unity 텔레메트리 GameAnalytics 저장소 설계

> 상태: 설계 초안(2026-11-05). 앞선 설계 [Unity 텔레메트리 패키지 설계](2026-11-02-unity-telemetry-design.md)에 저장소 하나를 더한다. 첫 사용처는 쥬라기 낙원(JP)이다.
> 조사 자료: JP 저장소 `docs/notes/2026-11-05-gameanalytics-unity-sdk.md`(GameAnalytics Unity SDK 1차 출처 조사). 이 문서의 SDK 사실은 그 메모를 따르며, 확인 못 한 사실은 §8에 빌더 확인 항목으로 둔다.

## 1. 의도

PostHog와 GameAnalytics를 같은 플레이테스트 기록으로 함께 써 보고 하나를 고른다. GameAnalytics는 무료이고 게임용 기본 대시보드(DAU·리텐션·세션 길이·진행 퍼널)가 SDK만으로 채워진다. PostHog는 속성을 자유롭게 붙여 원하는 질문을 직접 만든다. 둘을 실제로 써 봐야 고를 수 있다.

다른 bun3 게임도 같은 선택을 하게 되므로 GameAnalytics 연결은 처음부터 bun3-kit 패키지로 제공한다. 게임은 자기 기록을 GameAnalytics 형식으로 바꾸는 변환 표만 넘긴다.

## 2. 사용자 결정 (원문 인용)

1. **GameAnalytics를 붙여 PostHog와 비교한다.**
   - 원문: "GameAnalytics도 연결하고 싶어."
   - 계기를 묻자 원문: "그냥 무료라길래 붙여보고 두개 비교해서 고르려고"
2. **bun3-kit에서 처음부터 제공한다. 판단 기준은 따로 정하지 않는다.**
   - Claude는 평가 기간에는 JP에만 붙이고 고른 뒤 kit로 옮기자고 했다.
   - 원문: "bun3-kit에서 처음부터 제공은 해주자. 판단은 그냥 사용 좀 해보고 적절히 고르려고."
3. **게임 코드의 기록 호출은 그대로 두고, 분석 이벤트 몇 개만 변환 표를 만든다. 나머지는 이벤트 이름만 보낸다.**
   - 원문: "GameAnalytics 도입하려면 이벤트 옮겨야해?"
   - Claude 답: 기록 호출은 옮기지 않는다. GameAnalytics 웹 화면은 이벤트 이름(콜론 5단계)과 숫자 값 하나로만 거르고 묶으며, 사용자 정의 필드는 웹 화면에 나오지 않는다(유료 원본 내보내기에서만 보인다). 그래서 이벤트마다 어떤 속성을 이름에 넣을지 정한 변환 표가 필요하다. 비교용으로 분석 이벤트 몇 개만 표를 만들고 나머지는 이름만 보내자고 제안했다.
   - 원문: "ㄱㄱ"
4. **GameAnalytics 게임 키와 시크릿 키도 환경 묶음에 넣어 커밋한다.**
   - Claude 질문: 시크릿 키는 SDK 구조상 어차피 빌드에 들어가는 요청 서명 값이고 GameAnalytics의 안내가 없다. PostHog 키처럼 커밋할지 물었다.
   - 원문: "ㅇㅇ 넣자."

앞선 설계의 결정을 그대로 따른다. 특히 결정 11(저장소를 더하면 그 옵션도 환경 묶음으로 전달), 결정 4(가리기는 채팅 본문·인증 토큰만), "기록과 저장을 엮지 않는다"(저장소는 빌더에서 코드로 붙이고 분류별 스위치를 두지 않는다).

## 3. 목표 · 비목표 · 안티목표

**목표**
- 새 패키지 `com.bun3.unity.telemetry.gameanalytics`가 허브 기록을 GameAnalytics 이벤트로 보낸다.
- GameAnalytics 기본 대시보드(세션·DAU·리텐션·세션 길이)가 채워진다. 이 값은 SDK가 세션을 직접 센다.
- 게임이 넘긴 변환 표로 고른 이벤트를 디자인·진행 이벤트로 보낸다. 표에 없는 분석 이벤트는 이름만 디자인 이벤트로 보내 횟수를 센다.
- 사용자 ID는 PostHog와 같은 플레이어 ID(`distinct_id`, JP는 SteamID)다. 두 도구의 사용자 수를 비교할 수 있어야 한다.
- 웹 화면에서 환경(`dev`/`playtest`/`release`)을 거를 수 있다.
- 수집 거부(동의 끄기)를 따른다.

**비목표**
- 사용자 정의 필드(custom fields) 전송. 무료 웹 화면에 나오지 않는다.
- GameAnalytics의 오류 이벤트, 자원·결제 이벤트, 원격 설정, A/B 테스트, 광고 이벤트.
- 원본 내보내기·데이터 웨어하우스(PipelineIQ, 유료).
- 두 도구 중 하나를 고르는 기준이나 비교 보고서.

**안티목표**
- GameAnalytics 때문에 PostHog·Sentry 전송이나 게임 진행이 깨지면 실패다. SDK 호출의 예외는 저장소 밖으로 나가지 않는다.
- 평가 뒤 떼기 어려우면 실패다. GameAnalytics 관련은 kit 패키지 하나, 게임의 변환 표 파일 하나, 환경 묶음의 옵션 항목, 매니페스트 의존성 한 줄, SDK 설정 에셋에만 둔다.
- 오류를 GameAnalytics에도 보내 Sentry와 이중으로 수집하면 실패다(SDK 자동 오류 수집을 끈다).
- 플레이어 ID나 매치 ID처럼 값이 무한히 늘어나는 것을 이벤트 이름에 넣으면 실패다(GameAnalytics는 고유 이름 약 1만 개를 권장 상한으로 둔다).

## 4. 구조

### 4.1 패키지

- 위치: `unity/Packages/com.bun3.unity.telemetry.gameanalytics` (PostHog·Sentry 저장소 패키지와 나란히).
- 의존: `com.bun3.unity.telemetry`, `com.cysharp.unitask`(코어와 같게). GameAnalytics SDK(`com.gameanalytics.sdk`)는 Sentry 패키지가 `io.sentry.unity`를 다루는 방식과 같게, 패키지 `package.json`에 넣지 않고 쓰는 프로젝트의 `manifest.json`에 넣는다. asmdef는 SDK 어셈블리를 참조한다.
- SDK 설치 경로: 핀 없는 git URL을 먼저 쓴다(`https://github.com/GameAnalytics/GA-SDK-UNITY.git`, 저장소 루트에 `package.json`이 있다). git 설치가 안 되면 OpenUPM 범위 레지스트리(`package.openupm.com`, 범위 `com.gameanalytics`)를 쓴다. 어느 쪽이든 실제 버전은 `packages-lock.json`이 고정한다. kit 프로젝트와 JP 모두 같은 경로를 쓴다.
- 버전 `0.1.0`, CHANGELOG·README 포함. 코드와 README는 영어.

### 4.2 옵션 (환경 묶음)

`GameAnalyticsSinkOptions : TelemetrySinkOptions`
- `GameKey`, `SecretKey`: GameAnalytics 게임 키. 둘 중 하나라도 비면 저장소를 붙이지 않는다. 쓰는 게임의 설정 에셋에 넣어 커밋한다(결정 4).
- `Tiers`: 받을 단계. 기본값은 분석만(`Analytics`). 잦은 행동·디버깅은 고유 이름이 늘고 대시보드에 쓸모가 적다.

빌더 확장 `AddGameAnalytics(GameAnalyticsMap map = null)`이 환경 묶음에 옵션과 키가 있을 때만 저장소를 붙인다. PostHog 확장과 같은 모양이다.

### 4.3 변환 계약

게임이 넘기는 변환 함수:

```csharp
// output은 저장소가 재사용하는 목록이다. 반환값 false = 기본 변환(이름만).
public delegate bool GameAnalyticsMap(TelemetryRecord record, List<GameAnalyticsEvent> output);
```

- `true`를 돌려주고 `output`에 0개를 넣으면 그 기록은 보내지 않는다. 1개 이상이면 넣은 순서대로 보낸다.
- `false`면 기본 변환: 디자인 이벤트 `{기록 이름}`, 값 없음.
- `GameAnalyticsEvent`는 두 종류다.
  - 디자인: `Id`(콜론으로 나눈 1~5단계), `Value`(선택, 숫자 하나).
  - 진행: `Status`(Start/Complete/Fail), `Progression`(1~3단계).
- 저장소가 보내기 전에 이름을 다듬는다. 단계마다 GameAnalytics가 허용하지 않는 문자는 `_`로 바꾸고 64자로 자르며, 빈 단계는 `none`이 된다. 5단계를 넘으면 앞의 5단계만 쓴다. 값이 NaN·무한대면 값 없이 보낸다. 다듬은 결과가 원래와 다르면 실행마다 한 번만 경고를 남긴다(엔진 로그로 남아 확인할 수 있다).

### 4.4 초기화와 사용자 ID

- 저장소는 첫 기록을 받기 전에는 SDK를 초기화하지 않는다. 첫 기록이 오면 그 기록의 공통 속성으로 다음을 차례로 한 뒤 초기화한다.
  1. 사용자 ID = `distinct_id`(SDK의 custom id). SDK 규칙상 초기화 전에만 정할 수 있다.
  2. 빌드 = 공통 속성 `build`.
  3. 사용자 정의 차원 01 = 공통 속성 `environment`. SDK는 허용 값 목록에 없는 값을 버리므로, 목록에 그 값이 있게 한 뒤 지정한다. 허용 값 목록은 SDK 설정 에셋(Unity의 GameAnalytics 설정 창 Setup 탭, Custom Dimensions 01)에 있다. 문서가 "대시보드 Settings의 Setup 탭"이라고도 적어 웹 화면 등록이 따로 필요한지는 확인 못 했다(§8).
  4. 게임 키와 시크릿 키 = 옵션 값.
  5. SDK의 자동 오류 수집(`SubmitErrors`)을 끈다. 그 밖에 우리 기록·세션 말고 스스로 보내는 기능이 있으면 함께 끈다(빌더가 SDK 설정 기본값을 확인).
- 첫 분석 기록을 기다리는 이유: JP처럼 플랫폼 로그인(Steam) 전에는 `distinct_id`가 임시 ID다. 분석 단계 기록(JP `app_started`)은 로그인 뒤에 나온다. 그래서 GameAnalytics 세션은 첫 분석 기록부터 센다. 첫 기록 전의 몇 초는 세션 길이에 들어가지 않는다.
- 사용자 ID는 한 실행에서 바꾸지 않는다(SDK 제약).
- SDK가 요구하는 GameObject는 저장소가 초기화 때 만들고 씬이 바뀌어도 남긴다.
- SDK는 Windows 빌드 종료 때 `OnApplicationQuit`에서 1.5초를 기다린다(조사 메모). 평가 기간에는 그대로 둔다.
- SDK 호출은 메인 스레드에서만 한다. 허브가 저장소의 모든 멤버를 메인 스레드에서 부르므로(`ITelemetrySink` 계약) `Write`에서 바로 초기화하고 보낸다.
- 에디터: 게임의 에디터 묶음에 옵션을 넣지 않으면 붙지 않는다. SDK도 에디터에서는 보내지 않는다.

### 4.5 동의

- 허브는 이미 원격 저장소(`Remote = true`)에 동의가 꺼져 있으면 기록을 넘기지 않는다. 그래서 동의가 꺼진 채 시작하면 첫 기록이 오지 않아 SDK도 초기화되지 않고 세션도 보내지 않는다.
- 초기화한 뒤 동의가 바뀌면 `Telemetry.ConsentChanged`를 받아 SDK 전송을 끄고 켠다(`SetEnabledEventSubmission`). Sentry 저장소의 동의 처리와 같은 구독 방식이다. 지금 동의 값은 코어가 새로 여는 `TelemetryHub.RemoteAllowed`(원격 저장소에 보내도 되는지, 허브가 기록을 거를 때 쓰는 것과 같은 답)로 읽는다. 코어 버전은 patch를 올린다.
- 동의를 끄면 SDK가 세션 신호도 멈추는지는 확인 못 했다(§8).

### 4.6 SDK 설정 에셋

- SDK는 `Resources/GameAnalytics/Settings` 설정 에셋을 읽는다. 쓰는 프로젝트(JP)에 이 에셋을 둔다.
- 키는 환경 묶음 옵션이 정본이다. 저장소가 초기화 직전에 에셋 객체에 키를 넣는다. 런타임에 키를 넣을 수 없으면 키를 설정 에셋에 두고, 옵션에는 키가 있다는 표시만 남긴다(§8).
- 자동 오류 수집 끄기도 런타임에 할 수 없으면 설정 에셋에서 끈다.

### 4.7 JP 적용

**변환 표** (`JpGameAnalytics.Map`, JP 텔레메트리 폴더 파일 하나). 분석 단계 기록만 온다.

| JP 기록 | GameAnalytics |
|---|---|
| `match_entered` | 진행 Start `match` |
| `match_ended` | 진행 Complete `match`(`result` win) 또는 Fail `match`(lose·left) + 디자인 `match_end:{role}:{reason}` |
| `mission_step_finished` | 디자인 `mission:{kind}:{mission}:{result}`, 중단이면 `:{cause}`를 더한다. 값 = `work_seconds`, 없으면 `seconds`, 없으면 값 없음 |
| `mission_completed` | 디자인 `mission_done:{kind}:{mission}:{finisher\|contributor\|none}`(`by_me` 참이면 finisher, 아니고 `my_steps` > 0이면 contributor). 값 = `my_steps` |
| `meeting_ended` | 디자인 `meeting:{reason}:{outcome}`, 값 = `seconds` |
| `status_changed` | 디자인 `status:{to}`, `cause`가 있으면 `:{cause}` |
| `sabotage_used` | 디자인 `sabotage:{type}` |
| `stage_changed` | 디자인 `stage:{track}:{stage}` |
| `app_started`, `app_quit` | 보내지 않음(SDK 세션이 대신한다) |
| 그 밖의 분석 기록 | 기본 변환(이름만) |

- `role`은 공통 속성에서 읽는다. 없으면(역할을 받기 전에 떠남) `unknown`.
- 이름에 들어가는 값은 모두 정해진 목록(미션 이름, 결과, 원인, 역할 등)이다. 플레이어 번호·ID·방 이름은 넣지 않는다.

**설정**
- `TelemetryConfig`의 `Dev`·`Playtest`·`Release` 묶음에 `GameAnalyticsSinkOptions`(분석 단계, 키)를 더한다. `Editor` 묶음에는 넣지 않는다.
- `TelemetryBootstrap.Builder`에 `.AddGameAnalytics(JpGameAnalytics.Map)` 한 줄.
- `manifest.json`에 SDK 의존성, `Resources/GameAnalytics/Settings` 에셋.
- 서드파티 고지 `docs/third-party/gameanalytics-unity.md`(MIT, 버전·커밋).
- 운영 문서(`docs/playtest-guide.md`): 연결된 서비스 표에 GameAnalytics 추가.
- 빌드 버전: 네트워크·게임 규칙 변경이 없으므로 patch를 올린다.

**사람 할 일** (계정이 필요해 사용자가 한다. 운영 문서에만 적는다)
- GameAnalytics 가입, 게임(플랫폼 Windows) 만들기, 게임 키·시크릿 키 전달.
- 차원 값은 JP의 SDK 설정 에셋에 `dev`, `playtest`, `release`로 넣는다(구현 작업). 웹 화면에서 환경으로 거를 수 없으면 그때 웹 쪽 등록을 찾는다.

## 5. 제약

- kit 패키지 코드는 영어만, 빌드 경고 0, 공개 멤버 영어 XML 문서(kit 규칙).
- JP 코드 규칙: 문자열 연결·보간 금지(ZString), 매 프레임 할당 금지. 변환 표는 기록 하나당 몇 번 불리므로 출력 목록을 재사용한다.
- GameAnalytics SDK 호출은 메인 스레드에서만.
- GameAnalytics는 데이터를 미국에서 처리한다(DPA 부속서). 개인정보 처리방침에 PostHog(EU)·Sentry(US)와 함께 적는다.

## 6. 테스트

- kit EditMode
  - 이름 다듬기: 허용하지 않는 문자, 64자 초과, 6단계 이상, 빈 단계, NaN 값.
  - 변환 결과: `false`면 이름만, `true`·0개면 보내지 않음, 여러 개면 순서대로.
  - 초기화 순서: 첫 기록 전에는 초기화하지 않고, 첫 기록에서 사용자 ID → 빌드 → 차원 → 키 → 초기화 순서로 부른다. 동의 변경이 전송 켜고 끄기로 이어진다.
  - SDK 정적 호출은 테스트가 바꿔 끼울 수 있는 얇은 인터페이스 뒤에 둔다(PostHog의 `IPostHogTransport`와 같은 방식).
- JP EditMode: 변환 표 대표 1개(`match_ended` → 진행 + 디자인)와 경계 1개(중단 원인이 붙는 `mission_step_finished`).
- 수동: 플레이테스트 빌드를 실행하고 GameAnalytics 실시간 화면에서 세션과 디자인 이벤트가 보이는지 사용자가 확인한다.

## 7. Claude 판단

1. **GameAnalytics에는 분석 단계만 보낸다.** 잦은 행동·디버깅 기록은 고유 이름을 늘리고 기본 대시보드에 쓸모가 적다. 틀리면 GameAnalytics에서 아이템·공격 횟수를 못 본다(옵션 `Tiers`만 바꾸면 된다).
2. **환경은 사용자 정의 차원 01로 나눈다.** GameAnalytics에는 환경 개념이 없고, 웹 화면에서 거를 수 있는 것은 미리 등록한 사용자 정의 차원뿐이다. 게임을 환경마다 따로 만드는 방법도 있지만 PostHog처럼 하나로 두고 거르는 쪽을 맞췄다. 틀리면 환경별 게임 키를 묶음마다 다르게 넣으면 된다.
3. **SDK 초기화는 첫 분석 기록까지 미룬다.** 사용자 ID는 초기화 전에만 정할 수 있고, JP의 SteamID는 시작 몇 초 뒤에 생긴다. 틀리면(시작하자마자 초기화) 실행마다 임시 ID 사용자가 생겨 사용자 수가 부풀려진다.
4. **변환 함수 하나가 기록 하나를 0~n개 이벤트로 바꾼다.** `match_ended`처럼 진행과 디자인 이벤트를 함께 내야 하는 기록이 있다.
5. **`app_started`·`app_quit`은 보내지 않는다.** SDK의 세션 시작·끝과 겹친다.
6. **매니페스트 의존 방식은 Sentry와 같게 한다.** kit 패키지 `package.json`에 외부 SDK를 넣지 않는다.
7. **kit 작업은 kit main으로 바로 들어간다.** JP는 이미 모든 bun3-kit 패키지를 브랜치 없는 main 주소로 쓰므로(JP `unity/CLAUDE.md`), 개발 브랜치 예외를 새로 두지 않는다. kit 커밋을 main에 올린 뒤 JP는 매니페스트에 새 패키지 한 줄을 더하고 `packages-lock.json`을 갱신한다. 2026-11-05 오디오 폐기 경고 수정과 같은 흐름이다. 푸시는 사용자가 요청할 때 한다. 틀리면 kit 개발 브랜치와 JP 매니페스트의 브랜치 주소 예외가 필요하다.
8. **끝나지 않은 진행은 그대로 둔다.** 크래시나 강제 종료로 `match_entered`의 Start만 남아도 짝을 맞추지 않는다. `match_ended`의 `left`는 Fail이다. JP는 매치에 들어간 뒤에만 `match_ended`를 남기므로(`MatchEndTelemetry`) `left`도 항상 Start 뒤에 온다. 틀리면 GameAnalytics의 매치 완료율이 실제보다 높거나 낮게 보인다.
9. **SDK 설정 에셋은 에디터 스크립트로 만든다.** SDK 창 없이 설정 에셋을 코드로 만들고(키는 비움, 런타임에 옵션 값을 넣는다), 열린 JP 에디터에서 컴파일 경고를 확인한다. SDK 창이 꼭 필요하면 사용자가 한 번 연다. DLL이 많은 패키지를 더할 때 에디터가 멈춘 적이 있어(Sentry), 멈추면 에디터를 닫고 다시 열어 받는다.

## 8. 빌더에게 맡기는 것 (구현 전에 확인)

조사에서 확인하지 못해 구현하면서 확인한 SDK 사실과 그 결과다. 처음 체크리스트(대안 포함)는 Git 이력에 있다.

**구현 때 확인한 결과 (2026-11-05, SDK 8.3.1, lock 6f6d36f9의 소스 기준)**
- 설치: 핀 없는 git URL로 설치된다. 런타임 asmdef 이름은 `GameAnalyticsSDK`.
- 설정 객체: `GameAnalytics.SettingsGA`(Resources에서 지연 로드, 에셋이 없으면 플레이어에서는 null). 키는 정적 `Settings.UpdateKeys(index, gameKey, secretKey)`, 빌드는 `Build[index]`와 `BuildNumberAutoDetect[index] = false`, 플랫폼 항목이 없으면 `AddPlatform`. 모두 런타임에 바꿀 수 있어 대안(설정 에셋에 키 두기)은 쓰지 않는다.
- 자동 보고: `SubmitErrors`·`NativeErrorReporting`·`SubmitFpsAverage`·`SubmitFpsCritical`·`EnableFPSHistogram`·`EnableMemoryHistogram`·`EnableMemoryTracking`·`EnableHardwareTracking`·`EnableSDKInitEvent`가 공개 필드라 초기화 전에 끈다. 정보 로그(`InfoLogBuild`)도 끈다.
- 사용자 정의 차원: `Initialize`가 설정 객체의 `CustomDimensions01` 목록을 허용 값으로 넘긴다. 목록에 환경 이름을 넣고, 초기화 전(`GA_Setup.SetAvailableCustomDimensions01` 후 `SetCustomDimension01`)과 초기화 뒤에 한 번씩 지정한다. 웹 화면 등록 필요 여부는 실측에서 본다.
- 컴포넌트: `GameAnalytics` 컴포넌트는 초기화에 필요 없지만 종료 때 세션을 끝낸다(`OnApplicationQuit`, Windows 1.5초 대기). 없으면 저장소가 만들고, 컴포넌트의 `Awake`가 씬을 넘어 남긴다.
- 사용자 ID: `SetCustomId`는 래퍼 호출 하나뿐이고 설정 플래그가 없다.
- 이름 규칙: `GAValidator`의 `^[A-Za-z0-9\s\-_\.\(\)\!\?]{1,64}(:...){0,4}$`. 다듬기는 공백 중 일반 공백만 허용한다.
- 실측(2026-11-05, 플레이테스트 빌드 0.50.1): 네이티브 DLL이 Unity 6000.5 Mono 플레이어에서 동작한다(`GA_USE_MONO_WRAPPER` 불필요). 사용자 ID는 SteamID, SDK 상태에 `dimension01` = `playtest`가 저장되고, 메뉴에서 보낸 디자인 이벤트가 Realtime의 Live Events에 들어온다. 서버는 이벤트 묶음을 모두 받았다.
- 아직 미확인: 전송 끄기가 종료 때 세션 끝 신호도 멈추는지, 웹 화면에서 차원 01로 거를 수 있는지(Live Events에는 차원 필터가 없다).

## 9. 남은 질문 / 다음 할 일

- 평가가 끝나면 고르지 않은 저장소를 뗀다. 떼는 범위는 §3 안티목표의 목록이다.
