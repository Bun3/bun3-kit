# 비속어 필터 조사 — 한국어·영어, 성능 우선

- 조사일: 2026-10-10
- 방법: GitHub 저장소 소스·LICENSE·GitHub API(별·마지막 push, 2026-10-10 기준), Steamworks SDK 헤더·공식 문서, Unicode 데이터를 직접 확인했다. 비공식 출처만 있는 내용은 [2차]로 표기한다.
- 목적: jurassic-paradise(Unity, Windows/Steam, 한국어·영어)의 닉네임과 이후 텍스트 채팅에 쓸 비속어 필터를 `Bun3.*` 공용 패키지로 만들기 전에 매처·사전·우회 패턴·라이선스를 검증한다.
- 결정: [Bad word filter 스펙](../superpowers/specs/2026-10-12-bad-word-filter-design.md). §8의 권장(ToolGood.Words 매처)과 달리 매처는 직접 구현한다. 근거는 스펙의 Decisions에 있다.

표기 규칙:

- **[확인]** 1차 출처(저장소 소스·LICENSE·공식 문서)를 직접 열어 확인했다. 저장소 별(★)과 마지막 push 날짜는 2026-10-10 GitHub API 값이다.
- **[2차]** 공식이 아닌 출처에서만 확인했다.
- **[제안]** 조사자의 의견·권장이다.
- **[추정]** 계산·추론일 뿐 실측하지 않았다.
- **[미확인]** 1차 출처로 확인하지 못했다.

## 조사 내용

### 1. 요약

**[제안]** 판정 위치와 구성은 다음과 같다.

- **판정:** 호스트의 접속 승인 단계에서 한다.
- **정규화:** 표 기반 자체 정규화 계층을 만든다. 버퍼를 재사용하고 한글·두벌식·라틴 스트림을 분리한다.
- **매처:** ToolGood.Words의 Aho-Corasick DFA(`StringSearchEx2`/`WordsSearchEx2`, Apache-2.0)를 쓴다.
- **Steam 필터:** Steam 연동 후 `ISteamUtils::FilterText`는 수신자별 표시 마스킹 보조로만 쓴다. 판정에는 쓰지 않는다.

정확도는 매처보다 **정규화·사전·허용 목록**의 품질이 결정한다. 한국어 사전은 korcen(MIT)과 doublems(MIT)를 시드로 직접 큐레이션한다.

### 2. 현재 코드 상태

- **[확인]** `NicknameRules.TryNormalize`의 처리 순서는 다음과 같다.
  1. Trim 후 NFC로 정규화한다.
  2. 16 text elements·UTF-8 60바이트 상한을 검사한다.
  3. 제어문자, Unicode `Format`(Cf) 범주, `<`, `>`를 거절한다.

  ZWSP(U+200B), ZWJ(U+200D), WJ(U+2060), BOM(U+FEFF), soft hyphen(U+00AD)은 모두 Cf라 이미 거절된다.
- **[확인]** 한글 채움 문자는 Unicode 범주가 `Lo`라 통과하고 "보이는 문자"로 계산된다. 대상은 U+3164 HANGUL FILLER, U+115F, U+1160, U+FFA0이다(Unicode 16.0 데이터 확인). 따라서 다음 두 가지가 지금 가능하다.
  - 공백처럼 보이는 닉네임
  - 글자 사이에 채움 문자를 끼우는 우회
- **[확인]** 호스트는 `NicknameAdmission.Approve`(ConnectionApproval)에서 같은 정규화로 재검증한다. 방 이름은 `{닉네임}님의 룸`으로 재사용된다.
- 비속어 필터 관련 기획·코드는 아직 없다.

### 3. 후보 비교표 — 매처·라이브러리

| 이름 | 유형 | 언어 | 라이선스 | 성능 특성 | Unity 적합성 | 유지보수 |
|---|---|---|---|---|---|---|
| Steamworks `ISteamUtils::FilterText` | 플랫폼 API(Steam 클라이언트) | 게임 언어별 사전. 한국어 지원 **[미확인]** | Steamworks SDK 계약 | 네이티브 호출. C# 래퍼가 호출마다 문자열 할당 | Steam 실행 필수, 현재 미연동, 수신자 기준 결과 | Valve |
| [ToolGood.Words](https://github.com/toolgood/ToolGood.Words) | C# AC DFA(더블 어레이 + 알파벳 압축) | 문자 단위(언어 무관). 내장 정규화는 중국어·전각·대소문자 위주 | Apache-2.0 | `ContainsAny(string)` 무할당. 자체 벤치: 4.8만 어휘에서 3억 자/초 | netstandard2.1 DLL 약 0.9MB. 사전 빌드 시 약 96MB 일시 할당 → Save/Load로 회피 | ★5,187, 2025-12-04. `IllegalWordsSearch`는 Obsolete(무료 수정 중단) |
| [NReco.Text.AhoCorasickDoubleArrayTrie](https://github.com/nreco/AhoCorasickDoubleArrayTrie) | C# AC 더블 어레이(hankcs Java 포트) | 문자 단위 | Apache-2.0 | `Matches(string)` 무할당. `char[]`(IList) 버퍼 입력 가능 | netstandard2.0, Save/Load | ★54, 2022-07-12(정체) |
| [mganss/AhoCorasick](https://github.com/mganss/AhoCorasick) | C# AC(노드별 `Dictionary<char,Trie>`) | 문자 단위 | MIT | `yield` 이터레이터와 매치마다 객체 할당. 노드마다 접두 문자열을 보관해 메모리가 큼 | netstandard2.0 | ★72, 2026-09-28 |
| [ProfanityDetector](https://github.com/stephenhaunts/ProfanityDetector) | C# 단어 목록 + Regex | 영어 | MIT(목록 출처 불명) | `Regex`·`Split`·`ToLower` 반복 할당 | netstandard2.0 | ★167, 2024-06-20 |
| .NET `SearchValues<string>` | BCL(.NET 9+) | — | MIT | AC + Teddy(SIMD) | **Unity Mono에 없음**. 소스 참고용 | dotnet/runtime |
| [obscenity](https://github.com/jo3-l/obscenity) | TS 라이브러리 | 영어 프리셋 | MIT | 패턴별 RegExp | 설계 이식 참고 | ★182, 2026-10-07 |
| [korcen](https://github.com/Tanat05/korcen) | Python 키워드 필터 | 한국어(+영어는 better_profanity 의존) | MIT(2026-05-27 Apache-2.0→MIT) | 정규식 alternation | 사전·오탐 목록 참고 | ★30, 2026-05-27 |
| [better_profanity](https://github.com/snguyenthanh/better_profanity) | Python | 영어 | MIT | 로드 시 변형 전개 + 단어 집합 조회 | 참고만(단어 경계 전제라 한국어 부적합) | ★234, 2024-05-23 |
| [rustrict](https://github.com/finnbear/rustrict) | Rust | 영어·이모지 | MIT/Apache-2.0 | O(n) 커스텀 트라이, release 3MB/s | 설계 참고 | ★122, 2026-08-16 |
| [es-hangul](https://github.com/toss/es-hangul) | TS 한글 유틸 | 한국어 | MIT | — | 두벌식 변환·자모 조합 알고리즘 참고 | ★1,886, 2026-08-27 |

### 4. 후보 상세

#### 4.1 Steamworks 텍스트 필터

**[확인] API 정의**(SDK 헤더 주석 [isteamutils.h](https://github.com/rlabrecque/Steamworks.NET/blob/master/CodeGen/steam/isteamutils.h), [공식 문서](https://partner.steamgames.com/doc/api/ISteamUtils)):

- `InitFilterText(unFilterOptions = 0)`
  - 게임 실행 언어의 사전을 불러온다.
  - 해당 언어를 지원하지 않으면 `false`를 반환한다. 이때 `FilterText`는 입력을 그대로 통과시킨다.
  - 사용자는 Steam 계정 환경설정에서 필터 동작을 바꿀 수 있다.
- `FilterText(eContext, sourceSteamID, input, out, outSize)`
  - "법적으로 요구되는 필터"는 항상 적용된다.
  - 그 외 필터는 컨텍스트와 사용자 설정에 따라 추가된다.
  - 반환값은 필터된 문자 수다.
- `ETextFilteringContext`
  - `Unknown=0`
  - `GameContent=1`: 법적 필수 필터만 적용
  - `Chat=2`
  - `Name=3`: 캐릭터·아이템 이름
- 사전 변경 콜백 `FilterTextDictionaryChanged_t`가 있다.

**[확인] 수신자 기준 동작:** Valve 직원 답변(TomG, 2024-02-13, [Steam Labs 포럼](https://steamcommunity.com/groups/SteamLabs/discussions/13/4202490257581758313))의 요지는 다음과 같다.

- 사용자마다 설정 페이지에서 필터를 조절한다.
- 그래서 같은 문장도 플레이어마다 다르게 보일 수 있다.
- 콘솔 플랫폼의 요구사항은 Valve도 모른다.

**[2차] 기본 설정과 테스트:**

- 기본값은 모르는 사람의 강한 욕설·비하어를 기호로 가리고, 친구의 글은 필터하지 않는다([HowToGeek](https://www.howtogeek.com/694787/how-to-disable-steams-chat-filter-or-customize-it/)).
- 자기 글은 자기 화면에서 가려지지 않는다. 테스트하려면 두 번째 계정이 필요하다([GodotSteam 튜토리얼](https://godotsteam.com/tutorials/text_filtering/)).

**[확인] C# 래퍼(둘 다 MIT):**

- Steamworks.NET `SteamUtils.FilterText(ETextFilteringContext, CSteamID, string, out string, uint)`: 호출마다 `AllocHGlobal`, UTF-8 핸들, 결과 `string`을 할당한다.
- Facepunch.Steamworks `SteamUtils.FilterText(TextFilteringContext, SteamId, string) → string`: 32KB 풀 버퍼를 쓰지만 결과 `string`은 할당한다.

**[미확인]** 다음 사항은 확인하지 못했다.

- 지원 언어 목록(한국어 포함 여부)
- Steam 오프라인 모드에서의 동작
- 사전이 로컬에 캐시되는지
- Valve가 사용을 요구하는지: [Steamworks 콘텐츠 설문 문서](https://partner.steamgames.com/doc/gettingstarted/contentsurvey)에는 채팅·사용자 생성 텍스트를 필터하라는 의무 문구가 없었다.

**[제안]**

- Steam 필터는 **표시하는 클라이언트가 자기 설정으로** 거는 필터다. 호스트 판정(닉네임 거절)에 쓰면 호스트 개인의 Steam 설정이 방 규칙이 되므로 판정용으로는 맞지 않다.
- 연동 후 각 클라이언트가 이름·채팅을 표시할 때 `Name`/`Chat` 컨텍스트와 발신자 SteamID로 호출하는 보조 계층으로만 쓴다.
- 한국어 지원은 게임 언어를 한국어로 두고 `InitFilterText` 반환값으로 바로 확인할 수 있다.

#### 4.2 ToolGood.Words (C#, Apache-2.0)

- **[확인]** 패키지와 클래스 구성:
  - NuGet 3.1.0.3(2025-12-04)이며 대상은 `netstandard2.1;net8.0;net9.0;net10.0`이다. 의존 패키지는 없다.
  - DLL은 약 0.9MB다. 쓰지 않는 병음·간번체 사전이 내장 리소스로 들어 있다.
  - 매처는 `StringSearch*`(문자열 반환)와 `WordsSearch*`(위치·인덱스 반환)의 일반·Ex·Ex2·Ex3 변형으로 나뉜다. 정규식 일부를 지원하는 `StringMatch`/`WordsMatch`도 있다.
- **[확인]** `StringSearchEx2` 구현([소스](https://github.com/toolgood/ToolGood.Words/blob/master/csharp/ToolGood.Words/internals/BaseSearchEx2.cs)):
  - 실패 링크를 goto 표에 미리 합친 DFA를 `_key/_next/_check` int 배열(더블 어레이)로 저장한다.
  - 패턴에 나오는 문자만 빈도순 조밀 인덱스로 바꾸는 `_dict`(int[65536], 256KB)를 쓴다.
  - 패턴에 없는 문자를 만나면 상태가 루트로 돌아간다. 그래서 구분자 제거는 호출 전 정규화가 맡아야 한다.
  - `ContainsAny(string)`은 반복문만 있어 할당이 없다. `FindFirst`/`FindAll`/`Replace`는 결과 객체나 `StringBuilder`를 할당한다.
  - Regex와 리플렉션을 쓰지 않는다.
- **[확인]** 사전 빌드 비용: `SetKeywords`의 `build()`가 `int[0xFFFFFF]` 1개와 `bool[0xFFFFFF]` 2개를 일시 할당한다. 합계 약 96MB다. `Save`/`Load`가 있으므로 에디터에서 미리 빌드해 바이너리로 싣는다.
- **[확인]** `IllegalWordsSearch`를 쓰지 않는 이유:
  - 기능 자체는 갖췄다. 건너뛸 문자 목록, 전각→반각, 대소문자 무시, 중복 문자 축약, ASCII 영숫자 단어 경계 검사가 있다.
  - 하지만 클래스 전체에 `[Obsolete]`가 붙어 있다. 문구 요지는 "마지막 오픈소스 버전이며 더 이상 무료로 버그를 고치지 않는다(2020-10-08 이전 버그까지 수정)"이다.
  - 그래서 이 클래스 대신 Ex2와 자체 정규화를 조합한다.
- **[확인]** 성능 근거는 README의 자체 측정이다.
  - i7-8750H에서 4.8만 어휘 기준 `StringSearchEx2.Replace`가 초당 3억 자를 넘는다.
  - `StringSearchEx2.ContainsAny`가 `Regex.IsMatch`보다 8.8만 배 빠르다(키워드 수에 비례).
  - Ex3 포인터판은 측정 편차가 컸다.
- **[확인]** unsafe 코드: 라이브러리는 자체 빌드에서 `AllowUnsafeBlocks`를 쓴다. Unity의 `allowUnsafeCode`는 사전 정의 어셈블리·asmdef를 **소스 컴파일할 때** `/unsafe`를 넘기는 설정이다([Unity 문서](https://docs.unity3d.com/ScriptReference/PlayerSettings-allowUnsafeCode.html)). 미리 빌드된 DLL에는 영향이 없다.
- **[미확인]** 중국어 변환(`Translate.cs`)이 쓰는 `BrotliStream` 참조가 Unity Mono·IL2CPP에서 문제를 일으키는지 확인하지 못했다. 매처 경로는 이 코드를 호출하지 않는다.

#### 4.3 NReco.Text.AhoCorasickDoubleArrayTrie (C#, Apache-2.0)

- **[확인]** hankcs의 Java 구현을 포팅했다(원본도 Apache-2.0). 대상은 netstandard2.0이다.
  - `Matches(string)`는 할당 없이 상태를 전이한다.
  - `ParseText(IList<char>, start, length, Func<Hit,bool>)`에는 정규화 결과 `char[]`를 그대로 넘길 수 있어 문자열을 새로 만들지 않는다. 대신 문자마다 인터페이스 호출 비용이 생긴다.
  - `Save`/`Load`를 지원한다.
- **[확인]** 원본 저장소의 벤치마크([hankcs README](https://github.com/hankcs/AhoCorasickDoubleArrayTrie)), Java·단순 AC 대비:
  - 영어(12.7만 어휘): 5.95배, 초당 약 3,342만 자
  - 중국어(14.6만 어휘): 9.11배
- **[추정]** 더블 어레이는 문자 코드를 base 오프셋에 더한다. 한글 음절(U+AC00~)처럼 넓은 범위의 문자가 많으면 배열이 희소해질 수 있으므로 실측이 필요하다. ToolGood은 `_dict` 알파벳 압축으로 이 문제를 피한다.

#### 4.4 mganss/AhoCorasick, ProfanityDetector, .NET SearchValues

- **[확인] mganss:**
  - 노드마다 `Dictionary<char,Trie>`와 접두 문자열(`Word = Word + c`)을 만든다.
  - `Search`는 `yield`로 `WordMatch`를 돌려준다.
  - 유지보수는 활발하지만 무할당·메모리 목표에는 맞지 않는다.
- **[확인] ProfanityDetector:**
  - 영어 전용이다.
  - `ContainsProfanity`는 호출마다 `new Regex(...)`를 만든다. `CensorString`은 `Regex.Replace`, `Split`, `ToLower`를 반복한다.
  - README에 목록 출처가 "인터넷에서 모은 목록(확인 불가)"으로 적혀 있다.
  - 쓰지 않는 쪽을 권한다.
- **[확인] .NET 9+ `SearchValues<string>`:**
  - [dotnet/runtime 소스](https://github.com/dotnet/runtime/tree/main/src/libraries/System.Private.CoreLib/src/System/SearchValues/Strings)에 `AhoCorasick`, `Teddy`, `RabinKarp` 구현이 있다.
  - Unity(Mono/.NET Standard 2.1)에서는 쓸 수 없으며 MIT 참고 구현일 뿐이다.

#### 4.5 obscenity (TS) — 이식할 설계

- **[확인]** 변환기(transformer) 파이프라인([가이드](https://github.com/jo3-l/obscenity/tree/main/docs/guide)):
  - 동형 문자(confusables) 해석, leet 해석, ASCII 소문자화, 중복 문자 축약을 문자 코드 단위로 적용한다.
  - 상태 있는 변환기도 쓴다. 예: 직전 문자를 기억하는 중복 축약.
  - 문자를 "건너뛰기"(undefined 반환)로 처리할 수 있다.
- **[확인] 차단·허용 분리:** 차단 목록과 허용 목록에 **서로 다른 변환기**를 적용한다. 허용어가 덮은 구간 안의 차단 매치는 버린다. 허용 구간은 텍스트를 지우지 않고 위치로 억제한다.
- **[확인] 패턴 문법:**
  - `?`: 임의 1문자
  - `[]`: 선택적 부분
  - 맨 앞·뒤의 `|`: 단어 경계 assertion. 예: `|tit`는 `substitute`에 매치하지 않는다.
- **[확인]** 영어 프리셋의 설정과 0.2.0(2024-01-05) 변경:
  - 중복 축약 기본 임계값은 1이다. `s`, `g`, `b`, `e`, `o`, `l`만 2로 둔다. 예: `ass`, `bigger` 보존.
  - 0.2.0에서 `skipNonAlphabetic`(구분자 제거)을 기본 프리셋에서 뺐다. 숫자 패턴을 금지할 수 없는 문제(issue #23)와 누락 문제(#46) 때문이다. 대가로 `f u c k` 같은 띄어 쓴 우회는 기본으로 잡지 못한다.
  - 같은 버전에서 `NfaMatcher`(AC 계열)를 제거해 지금은 `RegExpMatcher`만 남았다.
- **[확인]** leet 표는 `a→@4`, `e→3`, `i→1|!`, `o→0`, `s→$5`, `t→7` 등이다. confusables 표는 [gc/confusables](https://github.com/gc/confusables)(MIT)에서 가져왔다.

#### 4.6 korcen (Python, 한국어) — 사전과 정규화 참고

- **[확인]** 처리 순서([korcen.py](https://github.com/Tanat05/korcen/blob/main/korcen/korcen.py)):
  1. 소문자화한다.
  2. 단일 문자 정규화 표를 적용한다. 예: 수학 영숫자·전각·원문자 → 라틴 문자, `^`·`人` → ㅅ, `甘`·`田` → ㅂ, 🐶 → 개, `0`/`o` → ㅇ.
  3. 다중 문자 치환을 적용한다. 예: `77` → ㄲ, `刀卜` → 까.
  4. 모든 공백을 제거한다.
  5. 등급별 치환을 적용한다. 예: 년/련/놈 통일.
  6. **오탐 패턴을 텍스트에서 삭제**한다.
  7. 허용 문자 밖의 문자를 삭제한다.
  8. 차단 패턴 alternation 정규식으로 검색한다.
- **[확인]** 우회 형태를 패턴에 직접 나열한다.
  - 두벌식 영타 형태(`tlqkf`, `wlfkf` 등)
  - 초성 형태(`ㅅㅂ`, `ㅂㅅ`, `ㅈㄹ`)
  - 한 어근에 대한 수십~수백 개의 발음 변형
  - `tq`/`qt`처럼 짧은 영타는 **입력 전체가 정확히 일치할 때만** 차단한다.
- **[확인]** 등급은 `general`, `minor`, `sexual`, `belittle`, `race`, `parent`, `politics`, `japanese`, `chinese`, `special`, `english`이다.
- **[확인]** 오탐 목록에 한국어 띄어쓰기 없는 매칭의 함정이 그대로 드러난다. 예: `시발점`, `시발택시`, `다시바`, `시바견`, `새끼손`, `고양이새끼`, `정보`, `감자`.
- **[제안]** 오탐 문자열을 텍스트에서 삭제하는 방식은 삭제 후 앞뒤가 붙어 새 매치를 만들 수 있다. 이식할 때는 obscenity처럼 구간 억제로 바꾼다.

#### 4.7 better_profanity, rustrict, es-hangul

- **[확인] better_profanity:**
  - 로드할 때 단어마다 leet 변형을 모두 전개한다(`handjob` → 16개).
  - 입력을 단어 단위로 나눠 집합에서 조회하므로 단어 경계에 의존한다.
  - README가 0.7.0의 성능 문제 때문에 0.6.1 사용을 권한다.
  - korcen이 영어 필터로 이 라이브러리에 의존한다.
- **[확인] rustrict:**
  - 대체 철자, 반복 문자, 동형 문자, 띄어 쓰기(`c r_a-p`), 악센트, 양방향 문자, 자기 검열(`f*ck`)을 처리한다.
  - 단어 내부 오탐(`assassin`)과 두 단어에 걸친 오탐(`push it`)을 방지한다.
  - Regex 없이 O(n) 트라이로 동작하며 release 빌드에서 3MB/s다.
  - 사전에 단어를 추가하면 `false_positive_finder`가 사전·단어 목록에 대고 **오탐을 자동으로 찾아** 안전 목록을 만든다.
  - 한계로 "주로 영어·이모지"를 명시한다.
- **[확인] es-hangul(Toss):**
  - `convertQwertyToHangul('vm론트') → '프론트'`처럼 두벌식 영타를 한글로 바꾼다.
  - 대문자는 Shift 쌍자음(`R` → ㄲ, `E` → ㄸ)으로 해석하고, 그 밖의 대문자는 소문자와 같게 본다.
  - `assemble`/`disassemble`/`getChoseong`가 있다.
  - [inko](https://github.com/738/inko)(MIT)도 같은 영타↔한글 변환을 제공한다.

### 5. 단어 목록·데이터셋

| 이름 | 언어 | 규모 | 등급·태그 | 라이선스 | 상업 재배포 | 비고 |
|---|---|---|---|---|---|---|
| [korcen 패턴·오탐 목록](https://github.com/Tanat05/korcen) | 한국어 | 등급별 수백 패턴 + 오탐 목록 | 등급 분류 | MIT | 가능(고지) | **[제안]** 한국어 시드 1순위 |
| [doublems/korean-bad-words](https://github.com/doublems/korean-bad-words) | 한국어 | 약 3,577줄 | 없음 | MIT | 가능(고지) | 2019-02-10 이후 정체. 정리가 필요하다 |
| [LDNOOBW](https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words) | en 403줄, ko 72줄 외 다국어 | 소형 | 없음 | **CC-BY-4.0** | 가능(**저작자 표시 필수**) | Shutterstock 자동완성 차단용 목록이다 |
| [dsojevic/profanity-list](https://github.com/dsojevic/profanity-list) | 영어 | 434항목 / 809 매치 | 심각도 1–4, 태그 6종, `exceptions`, `allow_partial` | MIT | 가능 | 대부분 심각도 3에서 시작했다고 README에 명시. 2021-10-23 이후 정체 |
| [censor-text/profanity-list](https://github.com/censor-text/profanity-list) | en 2,487줄, ko | — | 없음 | Unlicense 표기 | **주의** | `ko.txt`가 LDNOOBW `ko`와 **바이트 단위로 동일**하다(md5 일치). CC-BY-4.0으로 취급한다 |
| surge-ai/profanity | 영어 | 1,600+([블로그](https://www.surgehq.ai/blog/the-obscenity-list-surge)) | 심각도 1–3 | **[미확인]** | 제외 | GitHub 저장소가 404다(2026-10-10) |
| [2runo/Curse-detection-data](https://github.com/2runo/Curse-detection-data) | 한국어 문장 | 5,825문장(욕설 여부 라벨) | 0/1 | MIT | **주의** | 일베·오유 등 커뮤니티 댓글이라 원문 저작권이 불명확하다. 로컬 테스트 말뭉치로만 쓴다 |
| [NSMC](https://github.com/e9t/nsmc) | 한국어 영화평 | 20만 | 감성 | CC0 | 가능 | **[제안]** 오탐 탐색용 일반 문장 말뭉치 |
| [Unicode confusables(UTS #39)](https://www.unicode.org/reports/tr39/) | 동형 문자 | — | — | [Unicode License V3](https://www.unicode.org/license.txt) | 가능(고지) | 동형 문자 정규화 표의 원본 |
| [smilegate-ai/korean_unsmile_dataset](https://github.com/smilegate-ai/korean_unsmile_dataset) | 한국어 혐오 | 18,742 | 다중 라벨 | 데이터 **CC-BY-NC-ND 4.0**(코드는 Apache-2.0) | **불가** | 상업 사용은 Smilegate에 문의하라고 README에 명시 |

**라이선스 적신호 [확인]:**

- **NC·ND(상업 사용 불가):**
  - Smilegate UnSmile 데이터(CC-BY-NC-ND 4.0)
  - 나무위키 욕설 문서(CC BY-NC-SA 2.0 KR)
  - Kaggle `korean-hate-chat-data`(CC BY-NC-SA 4.0)
  - 뒤의 두 가지는 [korean-profanity-resources](https://github.com/Tanat05/korean-profanity-resources) 색인의 표기이며 원 페이지는 **[미확인]**이다.
- **SA(동일 조건 변경 허락):** K-MHaS, BEEP!, APEACH는 CC BY-SA 4.0으로 색인에 표기되어 있다(**[미확인]**). 파생 목록을 게임에 싣기보다 테스트 용도로만 쓴다.
- **GPL-3.0:** [rominf/profanity-filter](https://github.com/rominf/profanity-filter), [FrostCo/AdvancedProfanityFilter](https://github.com/FrostCo/AdvancedProfanityFilter), [irochi-moe/GuRoYeokSiBal](https://github.com/irochi-moe/GuRoYeokSiBal), [Whale0928/profanity-filter-api](https://github.com/Whale0928/profanity-filter-api). 코드를 가져오지 않는다.
- **라이선스 없음·불명:**
  - [hlog2e/bad_word_list](https://github.com/hlog2e/bad_word_list)(LICENSE 없음)
  - korean-profanity-resources에 들어 있는 "LoL 필터링 리스트 2020"과 `slang.csv`(색인 스스로 "확인 필요"로 표기, 게임사 목록일 가능성)
  - ProfanityDetector의 내장 목록(출처 불명)
- **고지 의무:** MIT·Apache-2.0·CC-BY-4.0·Unicode License V3 모두 크레딧·라이선스 고지가 필요하다.

### 6. 한국어 우회 패턴과 정규화 파이프라인

#### 6.1 우회 유형별 대응

| 유형 | 예 | 정규화 대응 [제안] | 오탐 위험 |
|---|---|---|---|
| 초성 약어 | ㅅㅂ, ㅂㅅ | 따로 입력된 호환 자모를 그대로 매칭한다. **완성 음절을 초성으로 투영해 매칭하지 않는다** | 투영하면 `수박`의 초성도 ㅅㅂ이 된다 |
| 자모 분리 | ㅅㅣㅂㅏㄹ | 따로 입력된 자모를 IME 오토마타로 음절로 재조합한다 | 낮음 |
| 숫자·기호·공백 삽입 | 시1발, 시.발, 시 발 | 한글 스트림에서 비한글 문자를 건너뛴다(원문 인덱스는 보존) | **높음**: `다시 발견` → `다시발견`에 `시발`이 생긴다. 허용 목록을 보강하거나 1음절 조각 사이에서만 건너뛴다 |
| 유사 발음 | 씨빨, 시바, 쉬발 | 런타임 퍼지 대신 **오프라인에서 사전 변형을 전개**한다(korcen 방식) | 변형이 늘수록 허용 목록 검증이 필요하다 |
| 두벌식 영타 | tlqkf(= 시발) | 라틴 문자 연속열 → 두벌식 자모 → 조합 → 한국어 사전 | 짧은 결과(2음절 이하)는 입력 전체 일치만 인정한다(korcen의 `tq`·`qt` 방식) |
| 로마자 | sibal | 영어 스트림 사전에 로마자 패턴을 추가한다 | 영단어 충돌을 허용 목록으로 처리한다 |
| 전각·반각 | ｓｉｂａｌ, 반각 한글 자모 | U+FF01–FF5E → ASCII, U+FFA0–FFDC → 호환 자모로 바꾸는 표 | 낮음 |
| 폭 0·채움 문자 | ZWSP, U+3164 | Cf와 U+115F/U+1160/U+3164/U+FFA0을 건너뛴다 | 낮음 |
| 동형 문자·이모지 | 𝐬, ⓢ, `^`→ㅅ, 🐶→개 | confusables 표와 korcen의 한글 모양 표를 적용한다. 수학 영숫자는 서로게이트 쌍이므로 코드포인트 단위로 처리한다 | 기호·숫자→자모 매핑은 오탐 위험이 있어 한국어 스트림에만 적용한다 |
| 반복 | 시이이발, fuuuck | 연속 중복을 축약한다(obscenity의 문자별 임계값). 한글의 `ㅇ+모음` 늘이기는 사전 변형으로 처리한다 | `ss`, `gg`처럼 정상 이중 문자 |
| 한자 | 始發點 | 한글 스트림에서 제외되므로 문제없다. 한글 `시발점`은 허용 목록으로 처리한다 | — |

- **[확인]** 표준 NFKC를 그대로 쓰면 호환 자모 ㅅ(U+3145)가 조합형 초성 ᄉ(U+1109)로 바뀐다. U+3164도 U+1160으로 바뀐다(Unicode 16.0 데이터로 확인). 그래서 NFKC를 통째로 적용하기보다 **필요한 범위만 자체 표로** 매핑하는 편이 자모 처리와 충돌하지 않는다.
- **[확인]** 한글 음절의 자모 분해·조합은 [Unicode 표준 §3.12](https://www.unicode.org/versions/latest/core-spec/chapter-3/)의 산술식으로 표 없이 계산한다. 식은 `S = 0xAC00 + (L×21 + V)×28 + T`다.
- **[2차]** 두벌식 자판(KS X 5002)의 영타 대응은 다음과 같다. es-hangul 구현과 korcen의 `tlqkf` 패턴으로 대조했다.

  | 구분 | 대응 |
  |---|---|
  | 자음 | q=ㅂ w=ㅈ e=ㄷ r=ㄱ t=ㅅ a=ㅁ s=ㄴ d=ㅇ f=ㄹ g=ㅎ z=ㅋ x=ㅌ c=ㅊ v=ㅍ |
  | 모음 | y=ㅛ u=ㅕ i=ㅑ o=ㅐ p=ㅔ h=ㅗ j=ㅓ k=ㅏ l=ㅣ b=ㅠ n=ㅜ m=ㅡ |
  | Shift | Q=ㅃ W=ㅉ E=ㄸ R=ㄲ T=ㅆ O=ㅒ P=ㅖ |

- 영어의 Scunthorpe 문제는 단어 경계 플래그와 허용 목록으로 대응한다. 경계 플래그는 obscenity의 `|`, dsojevic의 `allow_partial:false`에 해당한다. 한국어는 신뢰할 단어 경계가 없으므로 **허용 목록과 오탐 자동 탐색**이 핵심이다.

#### 6.2 정규화 파이프라인 [제안]

```
원문(NFC, 기존 TryNormalize 결과)
 └─ 0. 문자 정리 표 (char→char; 서로게이트는 별도 처리, 원문 인덱스 맵 유지)
      - Cf·한글 채움 문자·결합 부호 → 건너뜀
      - 전각·반각·동형 문자 → 기준 문자, ASCII 대문자 → 소문자
 ├─ A. 한글 스트림: 비한글 건너뜀 → 낱자모를 음절로 조합 → AC(한국어 차단+허용)
 ├─ B. 두벌식 스트림: 라틴 연속열 → 두벌식 자모 → 조합 → AC(한국어, 짧은 결과는 전체 일치만)
 └─ C. 라틴 스트림: leet 치환 → 중복 축약 → AC(영어 차단+허용, 경계 플래그)
 → 허용 구간이 덮는 차단 매치를 제거한다 → 결과(차단 여부·등급·원문 위치)
```

- 버퍼(`char[]`, 인덱스 맵)는 최대 입력 길이로 한 번만 만들어 재사용하므로 검사할 때마다 할당이 생기지 않는다.
- 차단어와 허용어를 **한 자동자**에 플래그로 함께 넣으면 스트림마다 한 번만 훑으면 된다.

### 7. 성능 고려

- **[확인] 복잡도 비교**
  - AC: 패턴 수와 무관하게 O(입력 길이 + 매치 수)다.
  - 패턴별 정규식이나 `string.Contains` 반복: O(입력 길이 × 패턴 수)다.
  - 실측 근거: ToolGood README의 `ContainsAny`와 `Regex.IsMatch` 비교(8.8만 배), hankcs의 단순 AC 대비 5.95–9.11배, rustrict의 3MB/s.
- **[확인] 시작 비용**
  - ToolGood Ex2 빌드는 약 96MB를 일시 할당하고, 문자 리맵 표가 256KB를 상주한다.
  - ToolGood과 NReco 모두 `Save`/`Load`로 빌드를 건너뛸 수 있다.
- **[추정] 상주 메모리:** 5천~2만 패턴(평균 3~5자)의 노드 수는 많아야 10만 개 안팎이다. 더블 어레이 int 배열 3개와 결과 표를 합해 수 MB 이내로 본다. 실측이 필요하다.
- **[확인] 할당**
  - ToolGood `ContainsAny(string)`는 무할당이다. 다만 정규화 결과를 `string`으로 만들 때 검사당 1회 할당이 생긴다.
  - NReco `ParseText(IList<char>)`에 `char[]`를 넘기고 델리게이트를 캐시하면 0회다.
  - Steam `FilterText` 래퍼는 호출마다 할당한다.
- **[제안] 호출 빈도 관점**
  - 닉네임은 접속 승인 때 1회 검사한다.
  - 채팅은 메시지당 1회이며 최대 8명이다.
  - 둘 다 프레임 경로가 아니라서 검사당 1회 할당도 GC 부담이 무시할 수준이다. 무할당이 필수인 곳은 매 프레임 경로뿐이다.
  - 그래도 버퍼 재사용 구조로 만들어 두면 이후 채팅량이 늘어도 구조를 바꿀 필요가 없다.
- **로컬이 아닌 대안(참고만)**
  - [Perspective API](https://perspectiveapi.com/): **[확인]** 서비스 종료가 예고됐다. 2026년 2월 이후 사용·할당량 요청을 처리하지 않으며 2026-12-31에 종료한다.
  - [Vivox Safe Text](https://docs.unity.com/en-us/safe-text/requirements): **[확인]** Unity Authentication과 Vivox 패키지(16.0+)가 필요하다. 한국어 지원·가격은 **[미확인]**이다.
  - Azure AI Content Safety, CleanSpeak: 조사하지 않았다.
  - 공통 한계: 네트워크 왕복 지연과 비용이 들고 별도 백엔드 계정이 필요하다. 리슨 호스트·오프라인 구조와 맞지 않는다.

### 8. 권장 조합 [제안]

1. **판정층(호스트, 필수):** §6.2 정규화와 ToolGood.Words `StringSearchEx2`/`WordsSearchEx2`를 조합한다. NuGetForUnity로 넣는다.
   - 근거 ①: 공개 벤치마크에서 가장 빠른 C# AC 구현이며, 판정 경로에 할당·Regex·리플렉션이 없다.
   - 근거 ②: Apache-2.0이라 상업 재배포가 가능하고, netstandard2.1을 대상으로 한다. 2025-12까지 유지보수됐다.
   - 근거 ③: `Save`/`Load`가 있어 에디터에서 사전을 미리 빌드하면 런타임 96MB 일시 할당과 빌드 시간을 없앨 수 있다.
   - 대안: 문자열 할당까지 0으로 만들거나 0.9MB DLL을 피하고 싶다면 두 가지가 있다. NReco(`char[]` 입력) 또는 약 200줄의 자체 Span 기반 AC다. 입력이 16자인 규모에서는 매처 간 차이가 체감되지 않는다.
2. **사전 구성**
   - 한국어: korcen(MIT) 패턴과 오탐 목록, doublems(MIT)를 시드로 음절 기준으로 재정리하고 등급을 붙인다.
   - 영어: dsojevic(MIT)를 기본으로 하고 LDNOOBW `en`(CC-BY-4.0)으로 보강한다. LDNOOBW는 크레딧 표기가 필요하다.
   - 허용 목록: korcen 오탐 목록을 쓰고, rustrict식 오탐 자동 탐색을 NSMC(CC0) 같은 일반 말뭉치에 돌려 보강한다.
3. **표시층(Steam 연동 후, 선택):** 이름·채팅을 표시할 때 `FilterText(Name/Chat, 발신자 SteamID)`를 호출한다. Valve 사전과 사용자 개인 설정을 존중하는 보조 장치이며 판정에는 쓰지 않는다.
4. **닉네임 처리:** 마스킹보다 거절을 권한다. 방 이름 `{닉네임}님의 룸`, 머리 위 이름, 결과 화면 등 여러 곳에 그대로 재사용되기 때문이다. 클라이언트 입력 단계에서 같은 필터로 미리 알려 주고, 호스트가 다시 검증한다.
5. **채팅을 도입하면:** 같은 필터로 마스킹하고, 어몽어스처럼 퀵챗 전용 모드를 둘지 검토한다.

### 9. 어몽어스 레퍼런스(동작만, `AmogUsCloning/docs/CHAT.md`)

- 입력은 두 가지다. 자유 채팅과 프리셋 문구를 버튼으로 고르는 퀵챗이다.
- 계정별 채팅 권한(`FreeChatOrQuickChat`/`QuickChatOnly`)을 서버가 강제한다. 미성년·제재 계정은 퀵챗만 쓸 수 있다.
- 자유 채팅에는 텍스트 필터(욕설·개인정보)와 서버 측 검열이 적용된다. 남용은 신고로 처리한다.
- 채팅 자체는 호스트가 중계하는 RPC 브로드캐스트다. 판정·필터·권한은 서버와 클라이언트 필터가 담당한다.
- 기본 안전장치는 퀵챗 전용이다. 자유 채팅은 명시적 권한이 있어야 열린다.

## 1차 출처로 확인하지 못한 항목

- Steam 텍스트 필터의 지원 언어(한국어 포함 여부), 오프라인 동작, 사전 캐시 위치, Valve의 사용 요구 여부
- surge-ai 목록의 현재 위치와 라이선스
- K-MHaS, BEEP!, APEACH, Kaggle 데이터셋, 나무위키의 라이선스(색인 표기만 확인)
- Vivox Safe Text의 한국어 지원과 가격
- ToolGood `BrotliStream` 참조의 Unity Mono·IL2CPP 호환성

## 0.1.0 큐레이션 기록

기본 사전(`Data~/ko.txt`, `Data~/en.txt`)을 만든 과정과 판단 기준이다. 원본 출처는 korcen, dsojevic/profanity-list(severity 3-4만), doublems/korean-bad-words이고 모두 MIT다. 정제에 쓴 말뭉치는 NSMC(한국어 영화평 20만 건)와 google-10000-english(욕설 제외판)다.

### 추출
- 스크립트 추출 직후: 한국어 1,137개, 영어 1,265개(allow 포함). 한글·알파벳 단일 문자열로 표현할 수 없는 패턴 234개는 건너뛰었다(예: `18놈`, `t발`, `시val`, `si바`, `10발놈`, `丨발`).
- dsojevic는 severity 2(`shit`, `dick`, `cock` 등)를 제외하고 3-4만 가져왔다. 3글자 이하 korcen 라틴 패턴은 `|…|` 단어 경계로 감쌌다.

### 영어 거짓 양성 점검 (google-10000-english)
- 점검 전 16개 이유(약 33단어)가 걸렸다. 점검 후 6단어만 남았다.
- 경계 변환 6건: `cum` -> `|cum|`, `leso` -> `|leso|`, `rape`·`raping`·`rapist` -> `|rape`·`|raping`·`|rapist`, 중복이던 `sex` 항목 삭제(`|sex|`가 이미 있다. `sexual`, `sexuality`는 통과).
- allow 추가 3건: `!specialist`, `!pakistan`, `!expansys`.
- 의도적으로 차단 유지: `cialis`, `nutten`, `thumbzilla`, `voyeurweb`(스팸·성인 사이트명). `h`, `qt` 단독 입력은 두벌식 스트림에서 `ㅗ`, `ㅂㅅ`가 되어 차단된다(R2에서 해소).

### 한국어 거짓 양성 점검 (NSMC)
- 차단된 줄: 3,575 -> 2,910. 남은 항목은 대부분 실제 욕설이다.
- 항목 삭제 7건(일반 단어와 충돌): `ㅆㅍ`(두벌식 `tv`와 충돌, 375건), `좇`·`쫒`(좇다·쫓다 활용형), `텐가`(-ㄹ 텐가), `랄지`, `깔보`(깔보다), `엄마없는`(일반 구절).
- allow 49건 추가(가장 짧은 덮개): `보지만`·`보지도`·`를보지`·`고보지`·`어보지`·`면보지`, `다운지`·`러운지`·`여운지`·`거운지`·`로운지`·`까운지`(-운지), `해줫`·`쳐줫`·`점줫`·`가줫`·`여줫`, `자위대`, `닥쳐올`·`닥쳐온`·`닥쳐왔`·`닥쳐오`, `낚시바`·`역시바`, `자지만`·`자지난`, `야스토리`·`야스쿠`, `이게년`·`떻게년`, `시방영`·`시방송`·`시방식`, `토리발`·`무리발`, `다시불`·`역시불`, `야발견`, `어색스`, `임새까`·`새까맣`·`새까먹`, `렇게같은`, `없니엄마`·`머니엄마`·`머니아빠`, `윤발씨`, `리치발`, `새끼공룡`(공룡 게임 닉네임 대비).
- 프로젝트 allow: `!다시발`.
- 의도적으로 차단 유지: `새끼`(동물 새끼도 막는다. `새끼공룡`만 예외), `섹스`, `포르노`, `자위`, `빨갱이`, `급식충`, `김치녀` 등 비하 표현, `꺼져`, `또라이`, `듣보잡`. 한 단어 비하 표현은 기획 기준(강한 표현)에 맞는지 소유자 검토가 필요하다.
- 영어 소유자 검토 목록: `domination`, `collared`, `collaring`, `fingering`, `fingered`, `intercourse`, `nude`, `topless`, `bareback`, `skeet`, `playboy`, `bastard`, `panties`, `grope`, `enby`, `cishet`.
- 소유자 결정(2026-10-13): 위 한국어 10개(`새끼`, `빨갱이`, `급식충`, `김치녀`, `꺼져`, `또라이`, `듣보잡`, `개드립`, `개소리`, `운지`)와 영어 16개를 모두 허용했다. 단독 `새끼` 항목에 기대 막히던 합성어 `개새끼`, `미친새끼`, `장애새끼`, `고아새끼`, `느그새끼`, `애새끼`는 따로 추가해 계속 차단한다. `새키`·`쌔끼`·`ㅅㄲ` 같은 철자 변형은 그대로 차단한다. 일상어 `세끼`(하루 세끼)도 같은 날 허용했다. `개세끼`는 별도 항목으로 계속 차단된다.

### doublems 추가
- 미탐 2,260개 중 강한 표현만 259개 추가: 부모 욕설 변형(`니미랄`, `네에미`, `닝기미` 등 철자 변형), `개새끼`·`병신`·`씨발`·`지랄`·`좆` 철자 변형, 성적 표현(`강간`, `윤간`, `야동`, `돌림빵`, `빠구리`, `사까시` 등), 비하 표현(`쌍년`, `호로자식`, `정박아` 등).
- 가져오지 않은 것: 성인 사이트·상품명 계열, 정치 표현, 약한 표현(`존나`, `졸라`, `쓰레기`, `양아치`, `미친`).
- NSMC 재점검 후 24개를 되돌렸다(일반 어휘와 충돌): `니미`, `걸레`, `후장`, `딩신`, `둥신`, `정액`, `도라이`, `니에비`, `니이미`, `니엠`, `씨벨`, `씨봉`, `애액`, `디랄`, `상년`, `족같네`, `족까`, `오랄`, `미친놈`·`미친넘`·`미친년`·`미친뇬`·`미친자식`, `죽일놈`. 그 결과 NSMC 차단 줄은 3,249개가 되었다(추가분은 `야동` 157, `강간` 82 등 대부분 실제 사용).

### 최종 수치
- `ko.txt` 1,439개(allow 428), `en.txt` 1,267개(allow 644), 합계 2,706개.
- 빌드 비용: `entries=2706 buildMs=3.1 retainedBytes=385488`.
- 우려 사항(단독 영문자가 `ㅗ` 같은 짧은 항목과 맞는 문제): R2 키보드 스트림 규칙으로 해소되었다.

### 수정 1차 (리뷰 반영)
- 항목 삭제: `게놈`(genome), `리발`·`치발`·`야발`·`발씨`·`시방`·`씨방`(짧은 한글 패턴이 단어 사이를 넘나듦), `새까`(새까만), `광년`, `쪼까`, `개쉽`(개- 강조). 이 항목만 덮던 allow 12건(`새까맣` 등)도 함께 삭제했다.
- 영어: korcen 라틴형 4글자 7개에 `|…|` 경계를 붙였다. 단, `|siva|`는 "Siva"라는 이름 자체를 막으므로 삭제했다.
- 키보드 스트림 규칙(R2): 한 글자 스트림은 스캔하지 않고, 짧은 패턴은 입력에 한글이 전혀 없고 스트림 전체와 일치할 때만 매치한다(`h`, `공룡h`, `가qt`는 통과, `qt`·`가tlqkf`는 차단).
- 결과: ko 1,416 + en 1,266 = 2,682개(`entries=2682 buildMs=3.1 retainedBytes=401728`), NSMC 차단 줄 3,249 -> 3,213.
