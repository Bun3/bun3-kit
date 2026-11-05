# Sound Catalog 테이블 인스펙터와 카탈로그 단일 관리 설계

- 상태: 설계 초안 (2026-11-05)
- 적용 패키지: `com.bun3.unity.audio` 0.8.0 → 0.9.0
- 첫 적용 게임: 쥬라기 낙원(JP). 카탈로그 키 147개 중 103개가 아직 클립이 없다.
- 시안: 브레인스토밍 중 만든 로컬 HTML 시안(`.superpowers/brainstorm/` 아래 `catalog-table-v5.html`, 커밋 안 함). 이 문서만으로 만들 수 있게 아래에 모두 적는다.

## 의도

JP에서 사운드를 조정해 보니 불편한 점이 많았다.

- 키 하나를 고치려면 Def 에셋 147개 중에서 파일을 찾아 하나씩 열어야 했다.
- 소리끼리 볼륨을 나란히 비교할 방법이 없었다.
- 공용 프로파일을 쓰는 Def는 인스펙터에서 값을 고치면 같은 프로파일을 쓰는 소리가 전부 바뀌었다.
- 프로파일을 비우면 예전에 남아 있던 개인 값이 갑자기 되살아났다.
- 카탈로그 인스펙터는 클립 없는 키마다 경고 상자를 띄워 103개가 늘어섰다.

목표는 사운드를 카탈로그 하나로 관리하는 것이다.

- Def 데이터는 카탈로그 파일 안에 들어간다.
- 카탈로그 인스펙터 한 화면에서 모든 키를 찾고, 비교하고, 고치고, 추가·삭제한다.
- 클립 경고는 그 화면의 해당 행에서 보인다.
- 프로파일에서 오는 값은 비활성으로 보인다. 사용자가 한 칸을 개인 값으로 바꾸면 그 칸만 프로파일을 떠난다.
- 홀드처럼 차오르는 소리도 카탈로그 키로 재생한다. 지금은 별도 에셋(`ChargeSoundDef`)이 Def를 직접 가리킨다.

## 사용자 결정 (원문)

- "SoundCatalog가 테이블 형태로 돼야 하고, 그 테이블 안에서 모든 키에 대한 데이터를 조절 할 수 있어야 할 것 같아."
- "Profile을 통해서 자동으로 정해지는 건 Disable 처리로 보이게만 하고, 우클릭을 하거나 특정 액션을 통해서 해당 컬럼을 수정하면 그때는 프로파일을 받는게 아니라 개인 데이터겠지?"
- "어쨌든 한 인스펙터에서 테이블 뷰를 통해서 모든 사운드 관리와 추가/삭제가 가능해야 할 것 같아. 클립 설정 안됐다고 경고도 거기서 같이 보이고."
- 가장 번거로웠던 일: "원하는 키 찾기, 볼륨 조절, 데이터 비교, 검색 등등."
- 시안 검토 후: "펼친 목록으로 하자. 그리고 각 컬럼마다 툴팁으로 어떤 값인지 설명 적어주자."
- 문서 1차 검토 후:
  - "휴지통이 아니라 바로 지워야지"
  - "삭제시에만 확인 창 있어도 충분할듯"
  - "어차피 Def와 Acoustics, 이번 테이블까지 다 bun3-kit 범위니까 잘 포함해봐도 좋지 않을까? 사실 Def를 다른 곳에서 사용하게 할 생각 없거든. 아예 카탈로그 하나로만 관리하는게 관리 주체 입장에서 제일 좋고"
  - "Def 만드는 경로를 지정하는게 제일 좋지 않을까?" — 아래 결정으로 Def 파일이 없어져 필요 없어졌다.
- 저장 방식: "내가 생각한 건 Def를 아예 카탈로그 안에 넣는 방식이긴 했어. 파일 하나로."
  - 처음에 Claude는 Def 파일을 따로 두는 쪽을 추천했고, 사용자가 위와 같이 정했다.
- 기존 Def 옮기기: "안으로 옮기는 버튼말고, 그냥 너가 한번 안으로 옮겨. 애초에 일회용 버튼이잖아."
- 차지 사운드: "원래 .hold를 차오르는 소리로 쓰려고 했어." 차지를 카탈로그로 옮기고 JP 홀드 상호작용에 쓰는 것을 이번 범위에 넣는다.

## 목표

- **찾기:** 키 검색, 필터 칩, 열 정렬.
- **비교:** 볼륨 막대와 정렬로 소리끼리 크기를 한눈에 본다.
- **조절:** 칸에서 바로 고치고, 여러 행에 한 번에 넣는다.
- **프로파일:** 칸마다 프로파일 값을 받거나 개인 값으로 덮어쓴다. Acoustics 안의 음향 프로파일도 같다.
- **관리:** 표에서 소리를 추가·복제·이름 변경·삭제한다. Def는 카탈로그 파일 안에만 있다.
- **경고:** 클립 없는 키를 해당 행에 표시한다.
- **설명:** 열마다 무슨 값인지 툴팁이 있다.
- **차지:** 키 하나로 차오르는 반복음과 시작·완료·취소음을 재생한다.

## 비목표

- 런타임의 키 조회 방식(`SoundCatalog.TryGet`)과 `SoundDef` 타입은 바꾸지 않는다.
- 공용 프로파일 에셋(Playback·Routing·Concurrency·Spatial·Acoustic)은 카탈로그 안으로 넣지 않는다. 음성 설정 같은 카탈로그 밖에서도 쓰기 때문이다. (Claude 판단)
- 공용 프로파일 자체의 값은 표에서 고치지 않는다. 프로파일 에셋의 인스펙터에서 고친다. (Claude 판단)
- `MusicDef`는 바꾸지 않는다.
- 기존 Def를 옮기는 기능은 kit에 넣지 않는다. JP는 Claude가 한 번 옮긴다.

## 안티 목표 (이렇게 되면 실패)

- 한 소리를 고쳤는데 알리지 않고 다른 소리까지 바뀐다.
- 칸을 덮어쓰거나 프로파일을 떼는 순간 들리는 소리가 바뀐다.
- 재생할 때마다 읽는 `Effective*` 값이나 차지 진행도 갱신에 할당이나 눈에 띄는 비용이 생긴다.
- JP를 옮긴 뒤 이전과 다르게 들리는 소리가 생긴다. 의도한 차지 변화는 예외다.

감수하는 비용: 모든 소리가 파일 하나에 있어서, 두 사람이 동시에 다른 소리를 고쳐도 Git 충돌이 날 수 있다.

## 설계

### 0. Def는 카탈로그 파일 안에 있다

- Unity는 에셋 파일 하나에 여러 오브젝트를 담을 수 있다. 이를 하위 에셋이라 한다. 모델 파일 안에 메시·머티리얼이 들어 있는 것과 같다.
- `SoundDef`를 카탈로그 에셋의 하위 에셋으로 저장한다(`AssetDatabase.AddObjectToAsset`).
  - 런타임에서는 지금과 같은 `SoundDef` 오브젝트다. 재생·미리 불러오기·준비 훅 API는 그대로다.
- 하위 에셋 Def는 프로젝트 창과 오브젝트 선택 창에 보이지 않게 숨긴다(`HideFlags.HideInHierarchy`). 관리는 카탈로그 표에서만 한다. (Claude 판단)
- 하위 에셋 이름은 키와 같게 둔다.
- 키 하나에 Def 하나다.
  - 두 키가 같은 Def를 가리키면 표가 두 행을 오류로 표시한다. 그 행에서 "Duplicate for this key"로 바로 고칠 수 있다.
  - 카탈로그 밖의 Def 파일을 가리키는 항목도 오류로 표시한다.
  - 런타임 `ValidateOrThrow`는 바꾸지 않는다. (Claude 판단)
- 에디터 코드용 공개 API를 둔다. 표와 게임 셋업 코드가 같은 API를 쓴다.
  - 키로 Def를 추가하고 그 Def를 돌려준다.
  - 키로 찾는다.
  - 이름을 바꾼다.
  - 복제한다.
  - 삭제한다.
  - 이름과 모양은 만드는 사람이 정한다.

### 1. 런타임: 칸별 덮어쓰기 (`SoundDef`)

지금은 묶음 단위다. 예를 들어 `PlaybackProfile`이 있으면 Volume·Pitch·Loop가 모두 프로파일 값을 쓴다.

바꿀 내용:

- `SoundDef`에 필드별 덮어쓰기 표시를 직렬화해 둔다. 대상은 프로파일이 가진 모든 필드다.
  - Playback: Volume, Pitch, Loop
  - Routing: MixerGroup, VolumeGroup
  - Concurrency: MaxInstances, Cooldown, Priority
  - Spatial: Spatial, Occlusion, OcclusionVolumeAtFull, Acoustics(선택 전체를 한 칸으로 본다)
- 각 `Effective*`는 "프로파일이 있고 그 필드를 덮어쓰지 않았으면 프로파일 값, 아니면 개인 값"을 돌려준다. `EffectiveAcoustics`도 같은 규칙이다.
- 표시가 하나도 없으면 지금과 똑같이 동작한다.
- Acoustics 안의 음향 프로파일(`SoundAcousticSelection.Profile`)도 같은 규칙을 따른다.
  - `SoundAcousticSettings`의 필드마다 덮어쓰기 표시를 둔다: DistanceAttenuation, AttenuationProfile, MinDistance, MaxDistance, InheritSpatialBlend, SpatialBlendProfile, MonoDistance, FullSpatialDistance.
  - `Resolve()`는 필드마다 프로파일 값 또는 개인 값을 골라 구조체로 돌려준다. 힙 할당은 없다.
  - 이 칸은 drawer 하나(`SoundAcousticSelectionDrawer`)가 그린다. Def뿐 아니라 Spatial Profile 인스펙터와 게임의 음성 음향 설정(JP `GameConfig.voiceAcoustics`)도 같은 칸별 UI를 갖게 된다. 표시가 없으면 동작은 지금과 같다.
  - README의 "프로파일을 붙이거나 떼도 개인 값을 덮어쓰지 않는다" 약속을 "뗄 때 프로파일 값을 개인 값으로 복사한다"로 바꾼다.
- 표시를 저장하는 방식은 만드는 사람이 정한다. 비트 플래그 정수 하나를 추천한다. `Effective*`는 비트 검사만 하고 할당하지 않는다.

### 2. 에디터 동작 규칙 (표 상세 칸과 Def 인스펙터 공통)

- **덮어쓰기(Override):** 프로파일의 현재 값을 그 필드의 개인 값으로 복사한 뒤 표시를 켠다. 그래서 덮어쓴 순간에는 소리가 바뀌지 않는다.
- **되돌리기(Revert to profile):** 표시만 끈다. 개인 값은 남겨 두지만 쓰이지 않는다.
- **묶음째 떼기(Use local):** 묶음의 프로파일을 뗄 때 그 묶음의 프로파일 값을 개인 값으로 먼저 복사하고 프로파일을 비운다. 프로파일 칸을 직접 None으로 바꿀 때도 같다. (Claude 판단)
- **프로파일에서 오는 칸:** 비활성으로 보이고 바로 고칠 수 없다.
  - 표에서 더블클릭하면 "우클릭 → Override로 고칠 수 있다"는 안내만 띄운다.
  - 여러 행에 한 번에 값을 넣는 동작은 대상 칸을 자동으로 덮어쓰기 상태로 만든 뒤 넣는다.
- **Def 인스펙터:** Def가 숨겨져 있어 주로 표의 상세 칸에서 고친다. 그래도 Def가 선택되면 같은 규칙으로 그린다.
  - 공용 값은 바로 고치지 않는다. "Edit profile" 버튼으로 프로파일 에셋을 선택한다. (Claude 판단)
- **Undo:**
  - 값 수정은 Undo로 되돌릴 수 있다. 여러 Def를 한 번에 고친 동작은 Undo 한 번으로 되돌아간다.
  - 추가·복제·이름 변경·삭제는 Undo 대상이 아니다.
    - 확인 창은 삭제에만 띄운다.
    - 이 동작이 끝나면 카탈로그와 Def의 Undo 기록을 비운다. 그래야 Undo가 지워진 Def를 가리키는 항목을 되살리지 않는다. (Claude 판단)
- **Play 중 수정:** 할 수 있다.
  - 런타임은 재생할 때마다 Def 값을 읽으므로 다음 재생부터 반영된다.
  - 에셋 수정이라 Play를 멈춰도 남는다.

### 3. 표 인스펙터 (`SoundCatalog`)

`SoundCatalogEditor`를 표로 바꾼다. 같은 표를 크기 조절 가능한 에디터 창으로도 연다. 창은 인스펙터의 "Open in Window" 버튼과 메뉴에서 연다. 창 위쪽에는 대상 카탈로그 선택 칸이 있다.

**행:** 카탈로그 항목 하나가 한 행이다. 기본은 카탈로그 순서의 펼친 목록이다. "Group by prefix"를 켜면 키 앞부분으로 묶어 접고 편다. 묶는 규칙은 특정 게임 키에 기대지 않게 일반적으로 정한다. 예: 마디가 둘 이하인 `bgm.title`은 첫 마디, 그보다 긴 키는 앞 두 마디(`sfx.raptor`).

**열 (기본 표시):**

| 묶음 | 열 | 표시 |
|---|---|---|
| — | 선택 | 체크 상자. 여러 행 편집용 |
| — | 미리 듣기 | ▶. 클립이 없으면 비활성 |
| — | Key | 키. 마지막 마디를 강조 |
| — | Clips | 클립 수. 0이면 ⚠ |
| Playback | Volume | 0~1 막대(Min까지 진하게, Min~Max는 옅게)와 숫자. 같으면 숫자 하나 |
| Playback | Pitch | 범위 숫자 |
| Playback | Loop | 체크 |
| Routing | Volume Group | 문자열 |
| Concurrency | Max | 0이면 ∞ |
| Concurrency | Cooldown | 초. 0이면 — |
| Concurrency | Priority | 0~255 |
| Spatial | Spatial | `SpatialMode`(None·Positional·Follow) |

Mixer Group, Occlusion, Occlusion Volume At Full, Acoustics, 프로파일 연결, 차지 설정은 상세 칸에서 고친다. 열 머리글 우클릭으로 이 값들을 열로 켜고 끌 수 있으면 좋다(선택 사항).

**칸 상태:**
- 개인 값: 보통 글씨, 고칠 수 있다.
- 프로파일 값: 회색 비활성과 연결 아이콘. 툴팁 "From <프로파일 이름>".
- 덮어쓴 값: 굵은 글씨와 왼쪽 파란 줄(프리팹 오버라이드와 같은 표시). 툴팁 "Overrides <프로파일 이름>".

**툴팁:** 열과 묶음 머리글마다 영어 설명을 단다(kit UI 규칙). 시안의 문구를 기준으로 하되 실제 동작과 맞춘다.
- Key: `SoundSystem.Play`에 넘기는 이름이다.
- Clips: 재생마다 하나를 무작위로 고르고, 0이면 재생하지 않으며 대체 키가 있으면 그 키가 재생된다.
- Volume: 0~1이고 재생마다 Min~Max에서 고르며, 플레이어의 그룹 볼륨이 그 위에 곱해진다.
- Pitch: 배율이고 재생마다 범위에서 고른다.
- Loop: 코드가 멈출 때까지 반복한다.
- Volume Group: 어느 플레이어 볼륨 설정이 적용되는지 정한다.
- Max: 동시에 재생되는 수이고, 넘으면 가장 오래된 것을 교체한다.
- Cooldown: 이 시간 안에 온 요청은 무시한다.
- Priority: 0이 가장 중요하다. 채널이 꽉 차면 덜 중요한 소리를 교체하고, 모든 재생보다 덜 중요한 새 소리는 내지 않는다.
- Spatial: None은 위치 없이, 나머지는 월드 위치에서 거리 감쇠·차폐와 함께 들린다.

**찾기:**
- 검색어는 공백으로 나눈 모든 단어가 키에 들어 있으면 남긴다. 대소문자는 무시한다.
- 필터 칩: All, Missing clips, Has clips, Uses profile, Overrides, Spatial(None이 아닌 것). 카탈로그에 있는 Volume Group 값마다 칩도 하나씩 둔다(예: music). 칩마다 개수를 표시한다. Uses profile·Overrides는 음향 프로파일도 센다.
- 열 머리글을 누르면 정렬한다. 누를 때마다 내림차순 → 오름차순 → 해제 순서로 바뀐다. Volume·Pitch는 범위의 가운데 값으로 정렬한다.
- 검색어·칩·정렬·묶기 상태는 사용자별로 기억한다(EditorPrefs 또는 SessionState).

**조절:**
- 더블클릭(또는 선택 후 Enter)으로 칸을 고친다. 범위는 `0.75-0.9`처럼 두 값, 숫자 하나면 Min=Max다.
- 체크와 열거형은 바로 바꾼다.
- Ctrl+클릭과 Shift+클릭으로 여러 행을 고른다. 두 행 이상이면 일괄 편집 줄이 나타나 Volume(고정값)과 Priority를 한 번에 넣는다.
- 칸 우클릭 메뉴:
  - Override with local value / Revert to profile (프로파일이 있는 칸만)
  - Select profile asset
  - Copy <열> to selected rows
  - Preview
  - Duplicate sound…, Rename key…
  - Delete sound

**미리 듣기:** 클립 하나를 무작위로 골라 그 Def의 Volume·Pitch를 적용해 들려준다. 볼륨 비교가 목적이라 원본 그대로 재생하면 부족하다. 게임의 그룹 볼륨은 에디터에서 알 수 없어 적용하지 않는다. 적용이 불가능한 환경이면 원본을 재생하고 그 사실을 표에 표시한다. (Claude 판단)

**추가·복제·이름 변경·삭제:**
- **Add Sound:** 키를 입력받는다. 빈 키와 중복 키는 거절한다. 카탈로그 안에 하위 에셋 Def를 만들고 항목을 넣는다. 기본값은 `SoundDef` 기본값이다.
- **Duplicate:** 새 키를 받아 Def를 카탈로그 안에 복사하고 항목을 넣는다.
- **Rename key:** 항목의 키와 Def 이름을 함께 바꾼다. 새 이름 입력 칸에 "게임 코드의 키 문자열은 바뀌지 않는다"는 안내를 한 줄 둔다.
- **Delete:** 확인 창을 띄운 뒤 항목과 그 Def를 바로 지운다. 되살리려면 Git을 쓴다.

**경고와 검증:**
- 클립이 없는 키는 Clips 칸에 ⚠ 0, Key 칸 왼쪽에 노란 줄을 표시한다.
  - Addressable 클립(`BUN3_ADDRESSABLES`)이 설정된 Def는 경고 대신 "Addressable" 표시를 한다.
- 빈 키·중복 키·Def 없는 항목·카탈로그 밖 Def를 가리키는 항목·Def를 공유하는 항목은 표 위에 오류 줄로 알리고 해당 행을 표시한다. 지금처럼 오류 하나로 화면 전체를 멈추지 않는다.

**상세 칸:** 행을 고르면 표 아래에 나온다.
- 클립 목록: 끌어다 놓기 가능. Addressable 클립이 있으면 같이 보인다.
- 묶음별 프로파일 칸과 Use local 버튼.
- Mixer Group.
- Spatial·Occlusion·Acoustics.
- 차지 설정(4절).
- 미리 듣기·멈춤.
- 칸별 덮어쓰기 규칙은 표와 같다.

**상태 줄:** 전체 수, 보이는 수, 클립 없는 수, 프로파일 사용 수, 덮어쓰기 있는 수.

**규모:** 키 1000개에서도 스크롤·검색이 끊기지 않게 행을 가상화한다. IMGUI(`MultiColumnHeader`·`TreeView`)와 UI Toolkit(`MultiColumnListView`) 중 무엇을 쓸지는 만드는 사람이 정한다. 패키지 최소 버전은 Unity 6000.3이다.

### 4. 차지 사운드를 카탈로그 키로

지금 `ChargeSoundDef`는 Def 네 개(Loop·Start·Complete·Cancel)를 직접 가리키는 별도 에셋이다. 카탈로그 단일 관리와 맞지 않아 키 방식으로 바꾼다.

- **차지 설정:** `SoundDef`에 차지 설정을 더한다. 값은 지금 `ChargeSoundDef`와 같다.
  - `VolumeByProgress`: 기본 0→1
  - `PitchByProgress`: 기본 0.667→1
  - `CompleteFadeOut`: 기본 0.05초
  - `CancelFadeOut`: 기본 0.15초
  - 이 설정은 그 Def가 차지 반복음으로 재생될 때만 쓰인다. 프로파일은 두지 않는다. (Claude 판단)
- **부분 소리는 이름 규칙으로 찾는다.** 반복음 키가 `K`이면 시작은 `K.start`, 완료는 `K.complete`, 취소는 `K.cancel`이다. 예: `sfx.rootbox.hold`, `sfx.rootbox.hold.start`. 부분 소리 키가 없거나 비어 있으면 그 부분은 소리가 없다.
- **정적 API:** `SoundSystem.PlayCharge(key, fallbackKey)`. 위치 고정판과 Transform 추적판도 둔다. 돌려주는 값은 기존 `ChargeSoundHandle`(`SetProgress`·`Complete`·`Cancel`)이다.
  - 반복음은 `key`가 재생 가능하면 그것, 아니면 `fallbackKey`를 쓴다. 클립이 빈 Def는 없는 키로 본다(0.8.0 규칙).
  - 부분 소리도 같은 규칙이다. 각각 `key`의 부분이 비어 있으면 `fallbackKey`의 같은 부분을 쓴다.
  - 둘 다 없으면 잘못된 핸들을 조용히 돌려준다.
- **할당:** 재생 때마다 부분 키 문자열을 만들지 않는다. 예를 들어 카탈로그 색인을 만들 때 반복음별 부분 Def를 미리 찾아 둔다. 방법은 만드는 사람이 정한다.
- **`ChargeSoundDef` 제거:** `ChargeSoundDef` 타입과 이를 받는 `PlayCharge(ChargeSoundDef…)`를 지운다. 1.0 전이라 minor 버전(0.9.0)에서 깨지는 변경으로 CHANGELOG에 적는다. JP는 쓰지 않는다. (Claude 판단)
- **표:** 차지 설정은 상세 칸에서 곡선으로 고친다. Loop가 켜진 Def에만 보인다.

### 5. JP 반영

- **Def 옮기기(Claude가 한 번 실행):**
  - JP Def 147개를 `SoundCatalog.asset` 안의 하위 에셋으로 옮긴다. 모든 직렬화 값을 그대로 복사한다. 클립·프로파일 참조·덮어쓰기 없음 상태가 그대로다.
  - 항목을 새 Def로 다시 연결하고, 옛 Def 파일(`Assets/_Game/Data/Audio/<키>.asset`)을 지운다.
  - 옮긴 뒤 키별 Effective 값과 클립이 옮기기 전과 같은지 비교해 기록한다.
  - 이 스크립트는 커밋하지 않는다.
- **셋업 코드:** `M1EditorSetup.EnsureSoundCatalog`와 `SirenSabotageSetup`이 Def 파일을 만들거나 경로로 읽는 부분을 0절의 kit 에디터 API로 바꾼다. `GeneratedAssetVersion`을 올린다.
- **홀드 상호작용을 차지로:**
  - 대상은 Hold 형식 상호작용 19개다.
  - `PlayerInteractor`는 지금 `.hold.start`, `.hold`(반복), `.hold.complete`, `.hold.cancel`을 따로 재생한다. 이를 `PlayCharge(물체 .hold 키, sfx.interaction.hold)` 하나로 바꾼다.
  - 홀드하는 동안 매 프레임 진행도를 `SetProgress`로 넘긴다. 끝나면 완료는 `Complete`, 취소는 `Cancel`이다.
  - `.hold` 반복음은 차지 기본 곡선을 쓴다. 볼륨·피치가 진행도를 따라 올라가는 것이 의도한 변화다.
- **버전·문서:**
  - packages-lock을 kit main으로 올린다. bundleVersion은 patch만 올린다.
  - `docs/sound-authoring-guide.md`의 "기존 효과음 바꾸기"와 홀드 소리 설명을 표·차지 기준으로 고친다.
- **확인:** JP 카탈로그를 열어 찾기·정렬·덮어쓰기·추가·삭제를 직접 확인한다. Play에서 상자 홀드 소리가 차오르는지 듣는다.

## 검증

- kit EditMode 테스트(kit에서 실행):
  - `Effective*`가 프로파일 없음·프로파일 있음·덮어씀 세 경우에 맞는 값을 돌려준다(대표 필드와 음향 설정).
  - 덮어쓰기·묶음째 떼기가 프로파일 값을 먼저 복사한다.
  - 에디터 API로 추가·이름 변경·복제·삭제한 뒤 카탈로그가 `ValidateOrThrow`를 통과하고, Def가 카탈로그의 하위 에셋이다.
  - `PlayCharge(key, fallbackKey)`가 반복음과 부분 소리를 키 규칙과 대체 키로 찾고, 빈 키는 잘못된 핸들을 돌려준다.
- 표 그리기는 테스트하지 않는다. 실제 에디터에서 JP 카탈로그로 확인한다.
- JP는 자동 테스트를 더하지 않는다(소리 연출). 컴파일 경고 0건과 Play 확인으로 검증한다.
- kit 규칙을 지킨다:
  - 경고 0건, XML 문서, 영어 코드·UI 문구
  - `package.json` 0.9.0, CHANGELOG(깨지는 변경 포함), README 갱신
  - `BUN3_ADDRESSABLES`가 있을 때와 없을 때 모두 컴파일된다.

## 만드는 사람에게 맡김

- IMGUI와 UI Toolkit 중 선택, 세부 스타일, 열 너비, 단축키
- 덮어쓰기 표시의 직렬화 방식, 에디터 API의 이름과 모양
- 차지 부분 소리를 미리 찾아 두는 방법
- 창 메뉴 경로, 사용자별 상태 저장 키 이름
- 열 표시 켜고 끄기의 지원 여부

## Claude 판단 목록 (사용자 확인용)

1. 하위 에셋 Def는 프로젝트 창과 선택 창에서 숨긴다. 관리는 표에서만 한다.
2. 공용 프로파일 에셋은 카탈로그 밖에 그대로 둔다. 음성 설정 등에서도 쓰기 때문이다.
3. 공용 프로파일 값은 표에서 고치지 않고 프로파일 에셋에서 고친다.
4. 프로파일을 묶음째 뗄 때 프로파일 값을 개인 값으로 먼저 복사한다.
5. 미리 듣기에 Def의 Volume·Pitch를 적용한다.
6. 키 이름을 바꾸면 Def 이름도 함께 바꾼다.
7. 툴팁은 영어로 쓴다.
8. 추가·복제·이름 변경·삭제 뒤에는 Undo 기록을 비운다.
9. 키 하나에 Def 하나 규칙은 표에서만 오류로 보이고, 런타임 검증은 그대로 둔다.
10. 차지 설정에는 프로파일을 두지 않는다.
11. `ChargeSoundDef`는 지우고 키 방식으로 바꾼다. 버전은 0.9.0이다.
