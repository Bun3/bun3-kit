# 비속어 필터 다국어 확장 조사 — 공개 사전 후보와 엔진 요구사항

- 조사일: 2026-10-13
- 방법: GitHub 저장소를 직접 clone해 LICENSE 파일과 데이터 파일을 읽었다. 엔트리 수는 내려받은 파일을 직접 센 값이다. 오탐 예시는 후보 데이터와 [FrequencyWords](https://github.com/hermitdave/FrequencyWords)(MIT, 2018 OpenSubtitles 빈도, 측정용으로만 사용)·[google-10000-english-no-swears](https://github.com/first20hours/google-10000-english)로 직접 계산했다. 블로그 요약은 쓰지 않았다.
- 전제: 이전 조사 [2026-10-10-bad-word-filter-research.md](2026-10-10-bad-word-filter-research.md)가 korcen, doublems, dsojevic, obscenity, LDNOOBW를 이미 평가했다. 이 문서는 그 위에 목표 언어(es-ES, es-LA, pt, pt-BR, fr, de, it, nl, ceb, ga, ru, ja, zh-Hans, zh-Hant)를 추가한다.
- 엔진 기준: bun3-kit `com.bun3.badwords`(커밋 `1333a868994eaa2253abdce8ff584755d6a3d85d`)의 `Runtime/TextNormalizer.cs`, `Runtime/BadWordFilter.cs`, `README.md`.
- 범위 기준: 강한 욕설, 성적 표현, 패드립, 인종·비하 슬러만. 순한 말과 정치·검열 용어는 제외한다.

표기: **[확인]** 저장소 파일·LICENSE를 직접 열어 확인. **[제안]** 조사자 의견. **[추정]** 계산·추론. **[미확인]** 확인하지 못함.

## 1. 요약

| 언어 | 최선 후보 | 라이선스 | 엔트리 수 | 판정 |
|---|---|---|---|---|
| Español (España / Latinoamérica) | [wooorm/cuss](https://github.com/wooorm/cuss) `es` (등급 0-2) | MIT(단, 출처 일부 CC-BY·웹사이트 — 6절) | 650 (등급 2만 501) | 데이터만 추가 가능 (큐레이션 필수) |
| Português / Português (Brasil) | cuss `pt`(브라질) + `pt-pt`(포르투갈) | MIT | 173 (등급 2: 144) + 44 (등급 2: 23) | 데이터만 추가 가능 (소규모) |
| Français | cuss `fr` | MIT(출처 Wiktionary — CC-BY-SA 의심) | 737 (등급 2: 695, 대부분 관용구) | 데이터만 추가 가능 (구 단위 항목 제거 필요) |
| Deutsch | LDNOOBW `de` | CC-BY-4.0 | 66 | 엔진 작업 필요 (ß 처리) + MIT 목록 없음 |
| Italiano | cuss `it` | MIT(출처 Wikipedia·Wiktionary·Treccani) | 1,178 (등급 2: 907) | 데이터만 추가 가능 (큐레이션 필수) |
| Nederlands | LDNOOBW `nl` | CC-BY-4.0 | 190 (관용구·일반어 다수) | 공개 목록 부족 (MIT 없음, 품질 낮음) |
| Bisaya (Cebuano) | 없음 | — | 0 | 공개 목록 없음 |
| Gaeilge (Irish) | 없음 | — | 0 | 공개 목록 없음 |
| Русский | LDNOOBW `ru` / LEv145/mat-words | CC-BY-4.0 / Unlicense(출처 스크래핑) | 151 (키릴 82, 라틴 전사 69) / 1,735 (굴절형 포함) | 엔진 작업 필요 (키릴 스트림) |
| 日本語 | [MosasoM/inappropriate-words-ja](https://github.com/MosasoM/inappropriate-words-ja) | MIT | 330 (Sexual 281 + Offensive 49) | 엔진 작업 필요 (가나 통일·단어 경계 없음) |
| 简体中文 | LDNOOBW `zh` | CC-BY-4.0 | 319 (간체 위주) | 엔진 작업 필요 (한자 스트림) |
| 繁體中文 | 전용 목록 없음. LDNOOBW `zh`의 번체 15개뿐 | CC-BY-4.0 | 15 | 엔진 작업 필요 (OpenCC류 간번 변환 + 간체 목록 공유) |

한 줄 결론은 3개다.

- MIT만으로 채워지는 언어는 es, pt, fr, it(전부 cuss)와 ja(MosasoM)다. de, nl, ru Cyrillic, zh는 CC-BY인 LDNOOBW 외에 쓸 만한 공개 목록이 없다.
- ceb와 ga는 공개 단어 목록이 없다(검색 결과 없음, 2절 참고).
- 현재 엔진은 ß(독일어), 키릴 문자(러시아어), 가나·한자(일본어·중국어)를 사실상 처리하지 못한다. 3절과 5절에 근거를 적었다.

## 2. 후보 저장소 일반

### 2.1 확인한 저장소 표

| 저장소 | 라이선스(LICENSE 파일) | 확인한 커밋 | 마지막 커밋 | 언어 | 등급 메타 | 비고 |
|---|---|---|---|---|---|---|
| [LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words](https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words) | CC-BY-4.0 | `5faf2ba42d7b1c0977169ec3611df25a3c08eb13` | 2020-07-13 | ar cs da de en eo es fa fi fil fr fr-CA-u-sd-caqc hi hu it ja kab ko nl no pl pt ru sv th tlh tr zh | 없음(줄당 한 항목) | 정체. ceb·ga 없음 **[확인]** |
| [dsojevic/profanity-list](https://github.com/dsojevic/profanity-list) | MIT | `c27924319aa9bd6f917e3782b4f4b6604a50b652` | 2021-10-23 | 영어만(`en.json`, `en.txt`, `emoji.*`) | 심각도·태그 | **비영어 파일 없음** **[확인]** |
| [2toad/profanity](https://github.com/2toad/profanity) (`@2toad/profanity`) | MIT | `19d6da192d61bd14544b7e417ea2613a2ba8357f` | 2026-03-23 | ar zh en fr de hi it ja ko pt ru es | 없음 | 비영어는 **기계번역**(아래) |
| [chucknorris-io/swear-words](https://github.com/chucknorris-io/swear-words) | CC-BY-4.0 | `eb443d787c66a93ace6fc0600c89b871c6e0e10f` | 2019-07-26 | LDNOOBW와 같은 언어 집합 | 없음 | LDNOOBW의 옛 복사본. 줄 수만 조금 다르고(예: es 68, fr 95, zh 294) 새 정보 없음 **[확인]** |
| [Jenyay/Obscene-Words-List](https://github.com/Jenyay/Obscene-Words-List) | CC-BY-4.0 | `975d4874fd9bd48f93acb56f851ac638591c70cc` | 2018-08-31 | LDNOOBW 포크 | 없음 | 새 정보 없음 **[확인]** |
| [censor-text/profanity-list](https://github.com/censor-text/profanity-list) | Unlicense(LICENSE 파일) | `fe2e5b55130f7f387c42f88695cd9e488302715c` | 2024-06-08 (GitHub API `archived: true`) | LDNOOBW 파생 + `uk` 등 | 없음 | LDNOOBW 파일과 줄 단위로 일부만 다름(ru는 70줄 추가·70줄 삭제, es 53/63). 데이터 출처가 CC-BY인 LDNOOBW라 Unlicense 표기는 신뢰할 수 없다 |
| [wooorm/cuss](https://github.com/wooorm/cuss) | MIT | `6bab3fef250481e34ba55bc400fac5c6d25f1429` | 2023-04-29 | en, es, fr, it, pt, pt-pt, ar-latn | **0-2 확신도** | 활동 정체 |
| [MosasoM/inappropriate-words-ja](https://github.com/MosasoM/inappropriate-words-ja) | MIT | `e24de6e185135f92c3491ef61931c26905bd3b1f` | 2021-12-02 | 일본어 | 파일 분리(Sexual / Offensive) | 정체 |
| [EbiseLutica/Citrine](https://github.com/EbiseLutica/Citrine) `Resources/ngwords.txt` | MIT (LICENSE 파일 첫 줄 "The MIT License (MIT)". GitHub API는 NOASSERTION) | `e6a37316bc8f3009febe0e0db77d9e29872e2d4d` | 2022-08-05 | 일본어 | 예외(`-`) 지원 | 191줄 |
| [LEv145/mat-words](https://github.com/LEv145/mat-words) | Unlicense | `b9407185e26ff1c80593c3e7c9afb876fa3f43cd` | 2022-05-21 | 러시아어 | 없음 | russki-mat.net를 스크래핑(`parse.py`) |
| [Konsheng/Sensitive-lexicon](https://github.com/Konsheng/Sensitive-lexicon) | MIT | `d967c30b053fa40b06c5a0dddf0be493f2dfae46` | 2026-08-17 | 중국어 | 파일 분리 | 정치·GFW 목록 혼재(3.9절) |
| [dablelv/dirty-cnwords](https://github.com/dablelv/dirty-cnwords) | **LICENSE 파일 없음** | `b24eb9fafaf9e929a6a3cd5470df5d64b0a4cae4` | 2022-12-09 | 중국어 | 폴더 분리(curse 268, porn 10,267, polity 등) | 무라이선스라 제외 |

### 2.2 @2toad/profanity는 쓸 수 없다

**[확인]** [contribute.md](https://github.com/2toad/profanity/blob/19d6da192d61bd14544b7e417ea2613a2ba8357f/contribute.md)는 "self-hosted LibreTranslate로 core English list를 번역한다"고 적는다. `src/data/profane-words.ts`의 비영어 항목은 영어 단어의 번역 결과라 그대로 오염되어 있다. 직접 읽은 예는 다음과 같다.

- de(368개): `aus`(전치사 "…에서"), `in %`, `fehler`(오류), `knochen`(뼈), `glocke`(종), `metzger`(정육점 주인)
- es(363개): `aus`, `a dólares`, `a)`, `hueso`(뼈)
- ja(317개): `ログイン`(로그인), `パスワード`(비밀번호), `人気カテゴリー`, `$$$$`
- ru: `полный`(가득 찬), `вес`(무게), `$`
- zh: `中国`, `作为美元`, `页:1`, 한 글자 `头`·`长`·`黄`·`鸡`·`爱`

it 168줄은 LDNOOBW `it`와 같은 내용이다. MIT 라벨이 붙어 있어도 데이터 품질 때문에 후보에서 제외한다.

### 2.3 Bisaya(Cebuano)와 Gaeilge(Irish)

- **[확인]** LDNOOBW 디렉터리 목록에 `ceb`, `ga`가 없다. `fil`(필리핀어, 14줄)이 있으나 Tagalog다(`putang ina`, `tangina`, `bobo` 등). Cebuano 고유 욕설을 담지 않았고 Cebuano에서 어느 정도 겹치는지 **[미확인]**이다.
- **[확인]** GitHub 저장소 검색(`cebuano profanity`, `bisaya badwords`, `irish swear words`)과 웹 검색 모두 전용 단어 목록 저장소를 반환하지 않았다.
- **[2차]** Wiktionary에 `Category:Cebuano vulgarities`, `Category:Irish vulgarities` 범주가 있다(예: [burikat](https://en.wiktionary.org/wiki/burikat), [póg mo thóin](https://en.wiktionary.org/wiki/p%C3%B3g_mo_th%C3%B3in)). 항목 수는 **[미확인]**이다. Wiktionary 텍스트는 CC-BY-SA라 현재 정책과 맞지 않는다.
- **[제안]** 두 언어는 "공개 목록 없음"으로 보고, 원어민 감수 목록을 직접 만들거나 이번 범위에서 제외하는 것이 맞다.

## 3. 언어별

### 3.1 Español (España / Latinoamérica)

- **LDNOOBW `es`**(CC-BY-4.0, 68줄) **[확인]**
  - 스페인식(`Gilipollas`, `Coño`, `Hijoputa`, `Soplapollas`)과 중남미식(`Pendejo`, `Verga`, `Concha de tu madre`, `Pinche`)이 섞여 있다. 지역 태그는 없다.
  - 심각한 노이즈가 있다. `Asesinato`, `Drogas`, `infierno`, `martillo`, `Heroína`, `Orina`, `Racista`, `Nazi`, `Sexo`, `Pedo`(방귀·취함), `Pis`, `Trio`, `Hacer el amor` 같은 일반어·정치어가 들어 있다.
  - 규칙 범위(약한 단어 통과)에는 맞지 않아 절반 이상 걸러야 한다.
- **cuss `es`**(MIT, 650개, 확신도 0:0 / 1:149 / 2:501) **[확인]**
  - 출처는 README에 `naughty-words`(LDNOOBW 계열), revistagq.com, taringa.net, mundoxat.om으로 적혀 있다([README Data 절](https://github.com/wooorm/cuss/blob/6bab3fef250481e34ba55bc400fac5c6d25f1429/readme.md)).
  - 등급 2에도 일반어가 섞여 있다. `boniato`(고구마), `brocha`(붓), `butifarra`(소시지), `bolas`, `bruta`, `alcornoque`. 일반 욕설이 아니라 사전에서 긁은 "모욕어 전반"이다.
  - 아르헨티나식(`boludo`)이 있어 중남미 커버리지가 LDNOOBW보다 넓다.
  - README는 "Use this for researching natural language. Don't use it to make a 'profanity filter'"라고 명시한다. 항목은 확률 정보일 뿐 차단 목록이 아니다.
- **지역 구분:** 두 목록 모두 es-ES와 es-LA를 구분하지 않는다. 닉네임은 모든 플레이어에게 보이므로 어차피 합쳐서 하나로 쓴다. 지역 분리는 필요 없다.
- **판정:** cuss 등급 2에서 일반어를 손으로 걸러내면 데이터만 추가 가능하다.

### 3.2 Português / Português (Brasil)

- **cuss `pt`**(브라질, MIT, 173개: 등급 1 29 / 2 144), **`pt-pt`**(포르투갈, 44개: 등급 1 21 / 2 23) **[확인]**
  - `pt` 출처는 aprenderpalavras.com, `pt-pt` 출처는 Wikipedia와 "common culture"다(README).
  - `pt`에 2글자 `cu`(등급 2)가 있다. 단어 경계 없이는 `cu`가 `curso`, `cultura`, `acumular` 등에 걸린다.
  - 등급 1에는 `burro`, `pau`, `peru`, `macaco`, `vaca`, `galinha` 같은 일반어가 있다.
- **LDNOOBW `pt`**(CC-BY-4.0, 76줄) **[확인]**
  - 노이즈가 많다. `cerveja`, `comer`, `heroína`, `cocaína`, `saco`, `burro`, `inferno`, `aborto`, `heterosexual`, `bissexual`, `camisinha`, `bumbum`.
  - 브라질식과 포르투갈식이 섞여 있다(`paneleiro`, `vai-te foder`는 PT-PT).
- **판정:** cuss만으로 데이터 추가가 가능하다. 언어 규모가 작아 등급 2 144개 + 23개를 사람이 검토하는 것이 현실적이다.

### 3.3 Français

- **cuss `fr`**(MIT, 737개: 등급 0 1 / 1 41 / 2 695) **[확인]**
  - 출처는 wiktionary.org다. **[확인]** Wiktionary 본문은 CC-BY-SA인데 cuss는 MIT로 배포한다. 출처 상속 문제는 6절 참고.
  - 대부분이 구·관용구다(`avoir de la merde dans les yeux`, `avoir le cul bordé de nouilles`, `aller se faire enculer`). Aho-Corasick 부분문자열 매칭에는 짧은 어근(`merde`, `enculé`, `salope`, `connard`)이 더 낫고 구는 대부분 불필요하다.
  - 2글자 약어 `PD`, `TG`, `tg`, 일반어 `chat`, `banane`, `bonne`, `faire`, `christ`, `vieille`, `bat`은 등급 0-1이다.
- **LDNOOBW `fr`**(CC-BY-4.0, 91줄) **[확인]**
  - 짧은 어근 위주라 엔진 형태에 맞는다. 단 일반어가 섞여 있다. `con`, `cul`, `bite`, `bitte`, `bordel`, `pipi`, `caca`, `folle`, `tanche`, `bourré`, `gerbe`, `nègre`, `negro`.
  - `nègre`, `negro`는 슬러이면서 es·it·pt에서는 일반 단어(색상)다(4절).
- **판정:** 데이터는 추가할 수 있으나 `con`, `cul` 같은 초단어는 경계 규칙이 필요하다(엔진 `|…|` 경계는 영어 항목에만 허용). 4절 참고.

### 3.4 Deutsch

- **LDNOOBW `de`**(CC-BY-4.0, 66줄)가 사실상 유일한 후보다 **[확인]**.
  - 어근 중심이고 노이즈가 비교적 적다. `arsch`, `fotze`, `hurensohn`, `schlampe`, `wichser`, `kanake`, `neger`, `nigger`, `schwuchtel`.
  - 일반어가 일부 있다. `nackt`, `penis`, `porno`, `pinkeln`, `popel`, `nippel`, `orgasmus`, `bimbo`, `bonze`, `mufti`, `ische`, `rosette`, `dödel`, `kimme`.
  - `scheiße`, `möse`, `schiesser` 같은 표기가 ß·움라우트에 의존한다(5.1절).
- cuss에는 `de`가 없고, 2toad는 번역물(2.2절)이다. **MIT 후보가 없다.**
- **판정:** MIT 정책으로는 데이터가 없다. 허용하더라도 ß 처리가 필요하다.

### 3.5 Italiano

- **cuss `it`**(MIT, 1,178개: 등급 0 158 / 1 113 / 2 907) **[확인]**
  - 출처는 Wikipedia·Wiktionary(속어·비속어·방언 범주)·Treccani·`chucknorris-io/swear-words`다(README Data 절). Wiktionary 파생이라 6절의 출처 상속 문제가 있다.
  - 활용형 전개로 불어난 항목이 많다(`arrapa`, `arrapamenti`, `arrapamento`, `arrapando`, `arrapano`, `arrapare`, `arrapate`, `arrapati`, `arrapato`).
  - 등급 2에도 일반어가 섞인다. `ano`, `azz`, `azzo`.
  - 등급 0-1은 `cammello`, `canna`, `carciofo`, `cadavere`, `cappella`, `biga`, `arare`(밭 갈다) 같은 일반어다. 등급 2만 써도 큐레이션이 필요하다.
- **LDNOOBW `it`**(CC-BY-4.0, 168줄)과 2toad `it`(168개)는 서로 같은 내용이다 **[확인]**.
  - 일반어가 많이 섞여 있다. `battere`, `montare`, `tirare`, `sbattere`, `pompa`, `sega`, `pesce`, `vacca`, `biga`, `fava`, `balle`, `palle`, `mona`, `regina`.
- **판정:** cuss 등급 2에서 큐레이션하면 데이터만 추가 가능하다.

### 3.6 Nederlands

- **LDNOOBW `nl`**(CC-BY-4.0, 190줄) 하나뿐이다. 2toad에는 `nl`이 없고 cuss에도 없다 **[확인]**.
- 품질이 낮다. 성적·방탕 관용구와 일반어가 대량으로 섞여 있다.
  - 관용구: `aardappels afgieten`, `buiten de pot piesen`, `de hond uitlaten`, `gras maaien`, `driehoog achter wonen`, `voor jan lul`.
  - 일반어: `pot`, `gat`, `hol`, `paal`, `poot`, `pik`, `del`, `balen`, `beurt`, `bekken`, `beffen`, `shit`, `nicht`, `mof`, `aso`, `muts`, `schatje`, `watje`, `toeter`, `standje`, `reet`.
  - 관용구 항목은 규칙에 맞지 않아 제외해야 한다.
- **판정:** 공개 목록 부족. 직접 큐레이션이 사실상 필수다.

### 3.7 Русский

- **LDNOOBW `ru`**(CC-BY-4.0, 151줄) **[확인]**: 키릴 82개 + 라틴 전사(translit) 69개다.
  - 라틴 전사 예: `pizda`, `zaebis`, `sukin syn`, `ebalo`. 이 69개는 현재 엔진의 영어 스트림에서 그대로 처리 가능하다.
  - 키릴 항목은 사전형 원형과 일부 활용형이다(`ебать`, `ебло`, `хуй`, `хуёвый`, `блядь`). 전체 굴절을 포함하지 않는다.
  - 일반어가 섞여 있다. `голый`, `какая разница`, `секс`, `мент`, `фига`, `хрен`, `хохол`(민족 비하일 수 있음), `хапать`, `гол`(라틴 전사 `gol`은 축구 용어와 겹침).
  - 혼합 문자 항목 `траxать`(라틴 x가 키릴 사이에 있음)가 있다. 출처에서 이미 혼합 문자를 쓰고 있다는 증거다.
- **LEv145/mat-words**(Unlicense 표기, `data/mat.txt` 1,735줄, `raw_mat.txt` 98줄) **[확인]**
  - `parse.py`가 [russki-mat.net](http://www.russki-mat.net/e/mat_slovar.htm)의 어휘 98개를 스크래핑하고, 나머지 1,735개는 어형 변화를 생성한 것이다(`блядиада`, `блядиады`, `блядиаде`, …).
  - 격 변화 전개형이 그대로 들어 있다. 부분문자열 매칭에서는 어근 몇 개로 충분하므로 크기만 불어난다.
  - 라이선스는 Unlicense이지만 스크래핑 대상 사이트의 권리는 **[미확인]**이다.
- **어근 vs 전체형:** 두 목록 모두 어근 패턴이 아니라 단어 목록이다. 러시아어 마트 필터는 일반적으로 어근(`хуй/хуе/хуя`, `пизд`, `еб/ёб`, `бляд`)에 대한 부분 문자열로 만든다. 이 엔진의 매처(Aho-Corasick 부분문자열)와 잘 맞는다. 어근 목록은 직접 만들어야 한다 **[제안]**.
- **현재 엔진의 위험:** 키릴 항목은 엔진이 거부하지 않고 조용히 줄어든다(5.2절).
- **판정:** 엔진 작업 필요(키릴 스트림). 데이터는 LDNOOBW 키릴 82개 + 직접 만든 어근이 현실적이다.

### 3.8 日本語

- **MosasoM/inappropriate-words-ja**(MIT) **[확인]**
  - `Sexual.txt` 281개, `Offensive.txt` 49개. README는 "단어 자체가 부적절하다고 단정할 수 있는 것만 사람이 수집했다"고 밝히고, 예컨대 "ローション"은 바디로션도 있어 뺐다고 한다.
  - `Sexual_with_mask.txt`(2,630줄)와 `Sexual_with_bopo.txt`(150줄)는 스크립트로 생성한 파생이다. 쓰지 않는다.
  - 한 글자 항목은 `唖` 하나다.
  - 짧은 일반어가 들어 있다. `イク`(行く의 가나 표기), `ナンパ`, `カス`, `クソ`, `ハゲ`, `バカ`, `アホ`, `デブ`, `ブス`, `ボケ`, `エロ`, `ハーレム`, `愛人`, `催眠`, `体位`, `座位`, `ホ別`, `リフレ`.
  - 라틴 문자 항목이 있다. `3P`, `SM`, `SEX`, `SOD`, `NTR`, `AV女優`. 이 중 `SM`, `SOD`, `NTR`은 영어 스트림의 2-3글자 패턴이 되어 영어 일반 텍스트에 걸린다.
  - 반각 가나 항목 `ﾁﾝﾁﾝ`이 있다.
  - 정치 성향 비하어가 섞여 있다(`ネトウヨ`, `パヨク`, `非国民`). 범위 밖이므로 제거한다.
- **Citrine `ngwords.txt`**(MIT, 191줄) **[확인]**
  - 헤더가 "대소문자·히라가나·카타카나·반각카나를 구별하지 않고 공백을 제거한 뒤 판정한다"고 적는다. 이 엔진이 일본어를 지원하려면 해야 할 정규화를 그대로 설명하는 셈이다.
  - 예외 구문(`-ぱちんこ`, `-フェラーリ`, `-カフェラテ`, `-フェライト`, `-スレイプニル`, `-ちんちん電車`, `-ウルトラマンコスモス`)이 많다. 일본어에서 부분 문자열 매칭의 Scunthorpe 문제가 얼마나 크고 이 엔진의 허용 항목(`!`) 기능이 필수인지를 보여주는 실제 증거다.
  - 정규화 규칙이 이 엔진과 맞다는 점에서 데이터보다 설계 참고 가치가 크다.
- **LDNOOBW `ja`**(CC-BY-4.0, 180줄) **[확인]**
  - 순수 가나 93개, 한자 포함 82개, 라틴 포함 6개(`3p`, `sm`, `xx` 등).
  - 일반어가 대량으로 섞여 있다. `女の子`, `女子高生`, `人種`, `嫌い`, `ファンタジー`, `フック`, `ラバー`, `いたずら`, `お尻`, `裸`, `ゲイ`, `デブ`, `ぽっちゃり`, `ローター`, `プレイボーイ`.
  - 위 중 MosasoM과 겹치는 성적 어휘가 많아 새로 얻는 정보가 적다.
- [1never/open2ch-dialogue-corpus](https://github.com/1never/open2ch-dialogue-corpus) `data/ng_words.txt`(Apache-2.0, 333줄, ref `177246f19e59eba1db4596268041ad3fd05884e2`, 저장소 마지막 push 2021-06-06)는 `氏ね`, `浮気`, `不倫`, `漏`, `痴` 등 한 글자·일반어가 섞여 있다 **[확인]**. 커밋 해시와 품질 전체는 **[미확인]**이다.
- 이 밖에 [eggplants/awesome-japanese-censored-words](https://github.com/eggplants/awesome-japanese-censored-words)가 일본어 목록 색인이다. 각 라이선스(MIT: Qman11010101/censorbot, Xeltica/Citrine, el-ideal-ideas/MocaBot; Apache-2.0: SudachiTra, open2ch; BSD-3: mlask `vulgar_uncoded.txt`; GPL-3.0: PyukiWiki; CC-BY-NC-SA-4.0: citizenlab/chat-censorship; 무라이선스: 그 외)는 이 색인의 표기이고 `MocaBot`의 `Offensive.txt`는 MosasoM의 `Offensive.txt`와 같은 49줄·같은 첫 단어 구성이다(바이트 단위로는 완전 동일하지 않음) **[확인: 색인 표기만, 각 저장소 LICENSE는 Citrine·MosasoM·open2ch만 직접 확인]**.
- **판정:** 데이터는 MosasoM(MIT)으로 가능하다. 단 가나 항목이 현재 엔진에서는 거부되므로 엔진 작업이 선행되어야 한다.

### 3.9 简体中文 / 繁體中文

- **LDNOOBW `zh`**(CC-BY-4.0, 319줄) **[확인]**
  - 간체 위주. 번체 전용 글자가 있는 항목은 15개뿐이다(`姦`, `幹`, `爛`, `白癡`, `賤`, `雞巴`, `陰莖`, `鳩`, `咸家鏟`, `冚家鏟`, `笨實`, `粉腸`, `躝癱`, `你老闆`, `硬膠`). 광둥어 속어(`冚家鏟`, `屎忽`, `躝癱`)도 일부 섞여 있다.
  - 한 글자 항목이 21개다: `乳 卵 奶 奸 妓 姦 屄 屌 幹 性 淫 爛 肏 賤 逼 㞗 尻 鳩 撚 柒 閪`.
  - 일반어·약한 단어가 있다. `性`, `成人`, `激情`, `私服`, `迷药`, `老二`, `月经`, `情色`, `笨蛋`, `白痴`, `放荡`, `淫`, `色情`.
  - 라틴이 섞인 항목이 12개다(`卖B`, `妈B`, `干死CS`, `干死GM`, `13.`, `懒8`, `鸡8`). 한자와 라틴이 섞인 항목은 엔진이 한 문자 종류만 허용하는 구조와 맞지 않는다.
  - 중국 본토 정치·검열 용어는 `六四`, `法轮`, `习近平`, `共产党` 문자열 검색 결과 0개다 **[확인]**.
  - BMP 밖 한자 항목이 있다(`𨳒`, `𨳊`, `𡳞`, `𨶙`, `𨳍`). 엔진은 서로게이트를 `Other`로 바꾼다(5.4절).
- **2toad `zh`**(292개): 번역물이라 쓸 수 없다(2.2절).
- **Konsheng/Sensitive-lexicon**(MIT) **[확인]**
  - 파일 15개 이상, 합쳐 약 87,000줄. `政治类型`, `反动词库`, `GFW补充词库`(6,414줄), `贪腐词库`, `新思想启蒙` 등 정치·검열 목록이 한 저장소에 섞여 있다. 이 프로젝트의 범위(정치·검열 용어 제외)에 어긋난다.
  - `色情词库`(929줄)·`色情类型`(304줄)은 별도 파일이라 성적 표현만 따로 뽑을 수는 있다. 하지만 `插b`처럼 한자·라틴 혼합 항목이 있고 `成人电影` 등 일반 성인물 용어가 많다. 욕설(辱骂) 범주 파일이 없다.
  - README 라이선스 절이 있으나 출처를 표기하지 않는다(파일명 `网易前端过滤敏感词库`, `零时-Tencent`로 보아 타사 목록을 포함한 것으로 보임). 출처 권리는 **[미확인]**이다.
- **dablelv/dirty-cnwords** `curse/all.txt`(268줄)는 욕설 전용이라 내용상 가장 맞지만 **LICENSE 파일이 없다**. 제외한다.
- **houbb/sensitive-word**(Apache-2.0, 저장소 라이선스) 사전은 열어보지 않았다. 정치 범주 포함 여부가 **[미확인]**이다.
- **번체:** 번체 전용 목록은 찾지 못했다. 간체 목록을 번체→간체 변환 후 공유하는 방식이 현실적이다(5.4절).
- **판정:** 데이터는 LDNOOBW `zh`(CC-BY)가 사실상 유일한 현실적 후보다. 번체는 간체 목록 + 변환으로 해결해야 하므로 두 언어 모두 엔진 작업이 필요하다.

## 4. 언어 간 오탐 위험

모든 목록이 하나의 라틴 스트림에 올라가므로, 한 언어의 욕설이 다른 언어(또는 영어)의 일상어이거나 흔한 부분 문자열이면 오탐이 난다. 아래는 후보 데이터 [LDNOOBW](https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words/tree/5faf2ba42d7b1c0977169ec3611df25a3c08eb13) 각 언어 파일을 엔진처럼 소문자·악센트 제거한 뒤 [FrequencyWords](https://github.com/hermitdave/FrequencyWords) 상위 20,000위와 비교한 결과다. 순위는 해당 언어 빈도 목록의 순위다.

### 4.1 한 언어의 욕설이 다른 언어의 일상 단어인 경우

| 욕설 목록 (출처) | 항목 | 일상어로 쓰이는 언어와 순위 |
|---|---|---|
| fr | `con` | es 19위(전치사 "~와"), it 22위(전치사 "~와"), pt 13,122위 |
| fr | `bitte` | **de 88위("부탁합니다")** |
| fr | `peter` | de 971위, es 1,004위, pt 880위 등(인명 Peter) |
| nl | `nicht` | **de 6위("아니다")**. nl에서는 "nicht"이 동성애자 비하어 |
| nl | `del` | es 31위, it 37위(관사 "del") |
| nl | `hol` | de 722위("hol" = 가져와) |
| nl | `pot` | en 2,448위, fr 2,968위 |
| it | `fica` | pt 280위("여기 있다"/"있다"의 활용형) |
| it | `balle` | fr 845위 |
| it | `mona` | en 5,659위, es 4,889위, de 4,890위(Mona 등 인명·단어) |
| it | `regina` | en·es·fr 등(여왕) |
| es | `pis` | fr 2,591위("더 나쁜", pis) |
| es | `nazi` | en 5,114위, pt 7,222위 등 |
| pt | `mama` | **de 467위, es 196위, nl 517위** (모두 "엄마") |
| pt | `comer` | es 488위("먹다") |
| pt | `saco` | es 2,769위("자루") |
| pt | `burro` | es 6,252위, it 3,834위("당나귀") |
| pt | `cona` | es 8,546위 |
| fr/es/it/pt | `negro` | es 739위, pt 1,255위, it 4,509위(색상 "검정") |
| fil | `bobo` | es 6,705위("바보", 약한 모욕), pt 등 |
| fil | `tanga` | pt 11,679위("끈 팬티") |
| ru(전사) | `gol` | es 8,119위, it 14,364위("골") |
| en | `sex`, `sexy`, `porno`, `penis`, `anal`, `viagra` | es·pt·fr·it·de·nl 전부 |

특히 `con`(es 19위, it 22위)과 `bitte`(de 88위), `nicht`(de 6위), `del`(es 31위, it 37위)은 해당 언어 플레이어의 닉네임에서 가장 흔한 단어 수준이라 영어 경계(`|…|`) 없이 넣으면 치명적이다. 이 엔진의 경계 기능은 영어 항목에만 허용되고 효과도 단어 단위일 뿐이다. 굴절이 많은 언어에서는 경계가 도움이 안 되므로 **MIT/CC-BY 여부와 무관하게 이런 초단어는 사전에 넣지 않는 편이 안전하다** **[제안]**.

### 4.2 욕설 항목이 영어 일반어의 부분 문자열인 경우

경계 없는 항목을 영어 상위 10,000단어에 대해 부분 문자열로 검사한 결과다(`google-10000-english-no-swears`, 정확히 같은 단어는 제외).

| 항목 (목록) | 포함하는 영어 단어 수 | 예 |
|---|---|---|
| `ass` (en) | 69 | classifieds, assurance, associated |
| `tit` (en) | 38 | petition, untitled |
| `cul` (fr) | 26 | difficulty, molecules, calculation |
| `hol` (nl) | 37 | hollow, alcohol, threshold |
| `gat` (nl) | 22 | aggregate, irrigation, gate |
| `fica` (it) | 18 | certificate, verification, certification |
| `aso` (nl) | 13 | seasons, reason, reasons |
| `conne` (fr) | 9 | connect, connected, connectors |
| `anal` (en) | 9 | analyses, analysts, canal |
| `pute` (fr) | 6 | disputes, computed, compute |
| `cesso` (it) | 4 | processors, accessory |
| `semen` (en, es, pt) | 4 | advertisement, basement |
| `cono` (es `coño`) | 4 | economy, economic |
| `puta` (es, pt) | 3 | computation, reputation |
| `pis` (es) | 3 | episode, therapist |
| `hure` (de) | 2 | brochure |
| `mose` (de `möse`) | 1 | Moses |
| `mona` (it) | 1 | Monaco |
| `monta` (it) | 1 | Montana |
| `pau` (pt) | 1 | Paul |
| `gol` (ru 전사) | 5 | mongolia, angola, gold |

주의: `coño`는 엔진의 악센트 제거(`ñ`→`n`)로 `cono`가 되어 "economy"에 걸린다. 같은 규칙에서 스페인어 `año`(해)도 `ano`가 되어, 이탈리아어 목록의 `ano`(항문, cuss `it` 등급 2)와 충돌한다 **[추정: 엔진 코드와 cuss 데이터 기준]**.

`|…|` 경계는 영어 항목에만 쓸 수 있으므로, 영어가 아닌 언어의 위 항목은 허용 목록(`!`)을 대량으로 만들거나(기존 영어 방식) 경계가 필요한 항목을 빼야 한다. 일본어(Citrine 예외 목록)와 중국어(`性`)는 경계 개념이 없어 더 심하다(5절).

### 4.3 한자 공유에 따른 일본어·중국어 간 오탐

일본어와 중국어는 같은 한자 항목에 서로의 일상어가 걸린다. LDNOOBW `zh`의 한 글자 항목 중 `性`(性格, 男性), `奶`(奶茶), `乳`(牛乳), `卵`(日本語에서 알), `鳩`(광둥어 속어이지만 일본어에서는 비둘기, 鳩山 성씨)이 그 예다. 어느 언어 UI를 쓰든 닉네임은 모두에게 보이므로, 한자 한 글자 항목은 전부 금지하고 2글자 이상만 허용하는 정책이 필요하다 **[제안]**.

## 5. 정규화 요구사항 (현재 엔진 기준)

엔진 근거는 `Runtime/TextNormalizer.cs`다. 관련 규칙은 다음과 같다.

- 입력 문자는 `Clean`을 거친다. U+00C0..U+00FF만 아스키로 접고(`?`로 표시된 글자는 `Other`), 전각 ASCII를 반각으로 바꾸고, 키릴·그리스 모양 닮은꼴 일부(`Lookalike`)를 라틴으로 바꾸고, Format/Mn/Me를 지운다.
- `Latin()`은 `LatinLetter`가 알파벳으로 돌려주지 않는 문자를 공백(구분자)으로 처리한다.
- 항목 등록(`BadWordFilter` 생성자)은 한글도 라틴 알파벳도 없는 항목만 거부한다. 라틴 알파벳이 하나라도 있으면(닮은꼴 포함) 통과시키고, 나머지 문자는 조용히 버린다.
- 모든 출력은 입력보다 길 수 없다(TextNormalizer 클래스 주석).

### 5.1 Deutsch: ß

- **[확인]** U+00DF(ß)는 `latin1` 표에서 `?`다. `Clean`이 `Other`로 바꾸고 `LatinLetter`가 공백으로 처리한다. `scheiße`는 `schei e`(두 토큰)가 되어 `scheisse`와 `scheiße`가 모두 항목과 다르게 정규화된다.
- **[확인]** LDNOOBW `de`에 `scheiße`, `scheisser`, `schiesser`가 있고 `ß` 항목이 존재한다.
- **[제안]** 1글자 보존 규칙(출력 ≤ 입력)을 지키려면 ß→`s`로 접고 사전 항목도 같은 규칙으로 접는다. 또는 `ss`로 확장하는 경우 길이 제약을 완화해야 한다. ä/ö/ü는 이미 a/o/u로 접힌다(`möse`→`mose`, "Moses"와 충돌, 4.2절).
- 같은 `?` 목록에 `æ`, `ø`, `þ`, `ð`, `×`, `÷`도 있다. 덴마크·노르웨이어는 대상이 아니므로 무방하다. `œ`(U+0153, 프랑스어 `cœur`, `bœuf`)와 `ĳ`(네덜란드어)는 Latin-1 밖이라 접히지 않는다.

### 5.2 Русский

- **[확인]** 키릴 문자는 `Lookalike`의 약 20자만 라틴으로 접힌다(`а в с е н і к м о р т х у` 등). 나머지(`б г д ж з й л п ф ц ч ш щ ъ ы ь э ю я` 등)는 `LatinLetter`에서 구분자가 된다.
- **[확인]** 항목 등록은 닮은꼴이 하나라도 있으면 통과한다. 키릴 항목 `хуй`는 `x`, `y`, 그리고 `й`(구분자)가 되어 **`xy` 두 글자 패턴이 조용히 등록**된다. `xylophone`, `xy` 등 영어 텍스트에 걸린다. 닮은꼴이 없는 항목(`бля`, `блядь`)은 "no Hangul or Latin letters"로 거부된다. 즉 지금 키릴 항목을 목록에 넣으면 일부는 예외, 일부는 잘못된 짧은 패턴이 된다.
- **[확인]** 러시아어 일상어도 거꾸로 라틴으로 접힌다. FrequencyWords 상위 30,000 중 닮은꼴 글자로만 구성된 1,392개가 라틴 문자열이 된다(`как`→`kak`, `все`→`bce`, `мама`→`mama`, `место`→`mecto`). 이 엔진의 영어 사전에 걸린 경우는 LDNOOBW `en`(3글자 이상 순문자 항목)을 대상으로 검사했을 때 0건이었다.
- **필요한 것** **[제안]**
  - 키릴을 키릴로 유지하는 별도 스트림(한글처럼). 소문자화, `ё→е`, `й` 처리.
  - 라틴 닮은꼴(`x`, `y`, `a`, `e`, `o`, `c`, `p` 등)을 **키릴 쪽으로** 접는 반대 방향 규칙. 현재 방향(키릴→라틴)은 `xуй` 같은 혼합 문자 우회를 라틴 `xyi`류로 만들어 키릴 사전에 못 맞춘다.
  - 라틴 전사(`pizda`, `huy`)는 영어 스트림에 그대로 올릴 수 있다. LDNOOBW `ru`의 69개가 이미 그 형태다.
  - 어근 중심 사전(3.7절).

### 5.3 日本語

- **[확인]** 히라가나·가타카나는 `Clean`을 그대로 통과하고(분류 Lo) 항목 등록에서 "no Hangul or Latin letters"로 거부된다. MosasoM·Citrine·LDNOOBW의 가나 항목은 전부 현재 엔진에 등록할 수 없다.
- **필요한 것** **[제안]** (Citrine 헤더의 규칙과 같은 내용)
  - 히라가나→가타카나 통일(코드 포인트 +0x60).
  - 반각 가타카나(U+FF61~FF9F, `ﾁﾝﾁﾝ`)를 전각으로 변환. 탁점·반탁점이 분리 문자로 올 때 합치기(`Clean`이 Mn을 지우므로 분리형 U+3099/309A는 탁점이 사라진다).
  - 장음 `ー`, 중점 `・`, 공백·기호를 구분자로 처리(Citrine은 공백을 제거한다).
  - 한자 항목은 별도로 한자 스트림이 필요하다(중국어와 공유).
  - 로마자: 후보 목록에는 로마자(`chinko`, `manko`) 항목이 없다 **[미확인: 공개 일본어 로마자 비속어 목록은 찾지 못함]**. 영어 스트림에 직접 만든 항목을 넣는 방식만 가능하다.
  - 단어 경계가 없어 Scunthorpe가 심하다. Citrine이 이를 `-フェラーリ` 같은 예외 8개 이상으로 푼다. 이 엔진의 `!` 허용 항목이 필수다.
  - `Other`로 처리되는 서로게이트 한자(CJK 확장 B)는 매칭 불가.

### 5.4 中文 (간체·번체)

- **[확인]** 한자도 `Clean`을 그대로 통과하고 항목 등록에서 거부된다. 한자 스트림이 필요하다.
- **번체→간체 변환 데이터** **[확인]**
  - [OpenCC](https://github.com/BYVoid/OpenCC) `8cf737a4e193db27d675b2ce9867d84aace33745`(2026-10-09), 라이선스 Apache-2.0(LICENSE 첫 줄 "Apache License"). 문자 매핑: `data/dictionary/STCharacters.txt` 4,029줄(40,055바이트), `TSCharacters.txt` 5,062줄(109,582바이트), `TWVariants.txt` 54줄, `HKVariants.txt` 78줄. 구(句) 매핑 `STPhrases.txt`는 49,270줄이라 쓰지 않는다.
  - [Unihan](https://www.unicode.org/Public/UCD/latest/ucd/Unihan.zip) `Unihan_Variants.txt`: `kSimplifiedVariant` 7,291줄, `kTraditionalVariant` 6,840줄(Unicode License. 기존 스펙의 "MIT + Unicode License" 허용 범위 안).
  - **[제안]** 닉네임 판정에는 번체→간체의 문자 단위 매핑 5천 줄 안팎이면 충분하다. 표를 빌드 시 생성해 소스에 싣는다. 일대다 매핑(`发`: 發/髮 등)은 판정용으로 첫 후보만 써도 된다.
- **한 글자 항목 vs 부분 문자열:** `性`, `奶`, `乳`, `卵` 같은 한 글자 항목은 일반어 대부분에 걸린다. 같은 이유로 `日` 같은 한 글자 항목은 `日本語` 같은 일본어·중국어 일상어 전체에 걸린다(LDNOOBW `zh`에는 `日` 단독 항목이 없고 `日你` 등 2글자 이상만 있다 **[확인]**). 한 글자 한자 항목은 금지하는 것이 안전하다 **[제안]**.
- **혼합 문자 항목** (`卖B`, `干死CS`, `妈个B`)은 한 항목이 한자·라틴을 섞고 있어 현재 구조(한글 항목은 한글만)와 같은 제약이 필요하다. 라틴 쪽을 버리고 한자만 두는 변환이 현실적이다.
- **광둥어** 한자(`屌`, `閪`, `撚`, `柒`, `𨳒` 등)는 번체·간체 변환 대상이 아니라 그대로 넣어야 한다.

### 5.5 그 밖의 언어

- es, pt, fr, it, nl, ga, ceb는 라틴 알파벳과 Latin-1 악센트 안이다. 현재 엔진으로 처리된다. 단 `ñ`→`n`, `ç`→`c` 접기가 4.2절의 충돌을 만든다.
- 아포스트로피·하이픈은 단어 경계가 된다. 프랑스어 엘리지옹(`l'enculé`)은 잘 갈라진다. `fils de pute` 같은 구는 영어처럼 공백 정규화로 맞는다.
- 아이리시 팔라다(á é í ó ú)는 Latin-1이라 접힌다. 목록이 없어 실제 효과는 **[미확인]**.

## 6. 라이선스 정책 선택지

기존 결정: MIT + Unicode License만 허용, CC-BY(LDNOOBW)·NC·ND·GPL·무라이선스 제외.

| 정책 | 추가로 쓸 수 있는 것 | 언어별 효과 |
|---|---|---|
| MIT + Unicode(현행) | cuss(es 650, fr 737, it 1,178, pt 173, pt-pt 44), MosasoM(ja 330), Citrine(ja 191), Unicode Unihan(번체→간체) | es, pt, fr, it, ja는 가능. de, nl, ru 키릴, zh, ceb, ga는 없음 |
| + CC-BY-4.0 | LDNOOBW 전체: de 66, nl 190, ru 151, zh 319, ja 180, es 68, fr 91, it 168, pt 76 | de, nl, ru, zh의 유일한 공개 출발점. 크레딧 표기 의무 |
| + Apache-2.0 | OpenCC(번체→간체 변환 데이터), open2ch `ng_words.txt`(ja 333), SudachiTra(미검사), houbb/sensitive-word(zh, 미검사) | zh 변환 데이터는 Unihan(Unicode License)으로도 가능하므로 필수는 아님. ja 보강 정도 |
| + CC0 / Unlicense / WTFPL | CC0·WTFPL 후보는 찾지 못함 **[미확인]**. Unlicense: LEv145/mat-words(ru 1,735) | ru 보강. 단 스크래핑 출처 권리 **[미확인]** |
| + BSD | mlask `vulgar_uncoded.txt`(BSD-3, 미검사) | 규모·품질 **[미확인]** |

**출처 상속 주의** **[확인]**
- cuss(MIT)의 es는 `naughty-words`(CC-BY 계열)를, fr·it는 Wiktionary(CC-BY-SA)와 Wikipedia를, es는 웹사이트(revistagq.com 등)를 출처로 밝힌다. MIT 라벨이 출처의 의무를 지우는지는 **[미확인]**이며 이 문서에서 법적 판단은 하지 않는다.
- censor-text/profanity-list의 Unlicense 표기도 같은 문제다(LDNOOBW 파생).
- 정책 결정 전에 cuss의 `es`·`fr`·`it` 출처를 MIT-only로 볼지 별도로 확인해야 한다.

**[제안]** MIT-only를 유지하면 이번 범위에서는 es, pt, fr, it, ja만 사전을 준비할 수 있고, 그조차 출처 상속 문제가 남는다. de, nl, ru, zh는 (1) CC-BY 허용으로 정책을 바꾸고 크레딧 화면에 LDNOOBW를 넣거나, (2) 원어민 감수로 직접 작성하는 두 길뿐이다. ceb, ga는 어느 정책이든 직접 작성해야 한다.

## 7. 플랫폼 퍼스트파티 다중 언어 필터

- **[확인]** [ISteamUtils 공식 문서](https://partner.steamgames.com/doc/api/ISteamUtils): `InitFilterText`는 "게임이 실행 중인 언어의 사전을 불러온다". 해당 언어에 필터가 없으면 `false`를 돌려주고 `FilterText()`는 입력을 그대로 통과시킨다. 법적으로 필수인 필터는 항상 적용되고, 그 외는 컨텍스트와 사용자 설정에 따라 달라진다.
- **[확인]** 문서는 지원 언어 목록을 내놓지 않는다(위 페이지 확인). 검색으로도 목록은 찾지 못했다. 지원 언어는 **[미확인]**이다.
- 이 필터는 게임 실행 언어 한 개의 사전만 쓰므로 "모든 플레이어에게 모든 언어를 검사"하는 이 프로젝트 요구와 다르다. 이전 조사(2026-10-10)의 결론인 "표시 시점 보조 계층으로만 사용"이 그대로 유효하다.

## 8. 미확인 목록

- ceb, ga 전용 목록의 존재(검색 결과 없음). Wiktionary 범주의 항목 수.
- `surge-ai/profanity`: GitHub 저장소 `surge-ai/profanity`는 지금 404다(Hugging Face 데이터셋은 웹 검색 결과로만 확인). 다국어 포함 여부와 일본어 수록 여부는 확인하지 못했다.
- `asyncee/python-obscene-words-filter`(러시아어 정규식 필터)와 `FlacSy/bdw` 저장소: PyPI 메타데이터는 MIT로 표기하지만 GitHub에서 404라 내용을 열 수 없었다.
- `thetemirbolatov/Russian-Profanity-Dataset--RU-MAT-`(MIT 표기): README와 LICENSE만 있고 데이터 파일을 찾지 못했다.
- open2ch `ng_words.txt` 전체 품질, SudachiTra, mlask, houbb/sensitive-word 사전의 정치어 포함 여부.
- LEv145/mat-words 스크래핑 원본 사이트(russki-mat.net)의 이용 조건.
- Steam FilterText 지원 언어 목록.
- 일본어 로마자(romaji) 비속어 공개 목록.

## 9. 출처

저장소 (확인한 커밋 해시)

- LDNOOBW: https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words (`5faf2ba42d7b1c0977169ec3611df25a3c08eb13`)
- dsojevic/profanity-list: https://github.com/dsojevic/profanity-list (`c27924319aa9bd6f917e3782b4f4b6604a50b652`)
- 2toad/profanity: https://github.com/2toad/profanity (`19d6da192d61bd14544b7e417ea2613a2ba8357f`), https://github.com/2toad/profanity/blob/19d6da192d61bd14544b7e417ea2613a2ba8357f/contribute.md
- chucknorris-io/swear-words: https://github.com/chucknorris-io/swear-words (`eb443d787c66a93ace6fc0600c89b871c6e0e10f`)
- Jenyay/Obscene-Words-List: https://github.com/Jenyay/Obscene-Words-List (`975d4874fd9bd48f93acb56f851ac638591c70cc`)
- censor-text/profanity-list: https://github.com/censor-text/profanity-list (`fe2e5b55130f7f387c42f88695cd9e488302715c`)
- wooorm/cuss: https://github.com/wooorm/cuss (`6bab3fef250481e34ba55bc400fac5c6d25f1429`)
- MosasoM/inappropriate-words-ja: https://github.com/MosasoM/inappropriate-words-ja (`e24de6e185135f92c3491ef61931c26905bd3b1f`)
- Citrine: https://github.com/EbiseLutica/Citrine (`e6a37316bc8f3009febe0e0db77d9e29872e2d4d`), ngwords.txt 해시 고정 URL: https://raw.githubusercontent.com/Xeltica/Citrine/8a6038e845a9d8ed29a691373cd1a3ceace8b761/Resources/ngwords.txt
- open2ch-dialogue-corpus: https://github.com/1never/open2ch-dialogue-corpus (ref `177246f19e59eba1db4596268041ad3fd05884e2`, `data/ng_words.txt`)
- awesome-japanese-censored-words: https://github.com/eggplants/awesome-japanese-censored-words (`790b837df9875c317690f064ef1061ec45feb20f`)
- LEv145/mat-words: https://github.com/LEv145/mat-words (`b9407185e26ff1c80593c3e7c9afb876fa3f43cd`)
- Konsheng/Sensitive-lexicon: https://github.com/Konsheng/Sensitive-lexicon (`d967c30b053fa40b06c5a0dddf0be493f2dfae46`)
- dablelv/dirty-cnwords: https://github.com/dablelv/dirty-cnwords (`b24eb9fafaf9e929a6a3cd5470df5d64b0a4cae4`)
- OpenCC: https://github.com/BYVoid/OpenCC (`8cf737a4e193db27d675b2ce9867d84aace33745`)
- houbb/sensitive-word: https://github.com/houbb/sensitive-word (Apache-2.0, 사전 미검사)

측정 보조 자료

- FrequencyWords(MIT): https://github.com/hermitdave/FrequencyWords (`content/2018/{en,es,pt,fr,de,it,nl,ru}/*_50k.txt`)
- google-10000-english: https://github.com/first20hours/google-10000-english (`google-10000-english-no-swears.txt`)
- Unihan: https://www.unicode.org/Public/UCD/latest/ucd/Unihan.zip (`Unihan_Variants.txt`)

플랫폼·위키

- Steamworks ISteamUtils: https://partner.steamgames.com/doc/api/ISteamUtils
- Wiktionary: https://en.wiktionary.org/wiki/burikat, https://en.wiktionary.org/wiki/p%C3%B3g_mo_th%C3%B3in

엔진 (이 저장소)

- `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs`, `Runtime/BadWordFilter.cs`, `README.md` (커밋 `1333a868994eaa2253abdce8ff584755d6a3d85d`)
- 이전 조사: `docs/research/2026-10-10-bad-word-filter-research.md`, 스펙: `docs/superpowers/specs/2026-10-12-bad-word-filter-design.md`

## 0.2.0 큐레이션 기록

0.2.0 데이터 작업의 최종 기록이다. 항목 수는 `Data~/` 파일을 직접 센 값이고(차단 = `!`가 없는 항목, 허용 = `!` 항목), 나머지 수치는 작업별 보고서(SDD 폴더)에서 옮겼다. 조사 단계의 후보와 평가는 위 1~9절이 원본이다.

### 1. 파일별 항목 수 (차단 / 허용)

| 파일 | 차단 | 허용 | 비고 |
|---|---|---|---|
| ko | 994 | 416 | 0.1.0과 동일 |
| en | 594 | 657 | 0.1.0은 606 / 645 |
| es | 27 | 7 | |
| pt | 22 | 3 | |
| fr | 21 | 7 | |
| it | 25 | 8 | |
| de | 17 | 6 | |
| nl | 13 | 10 | |
| tl | 6 | 0 | |
| ru | 68 | 38 | 차단은 키릴 55 + 라틴 음역 13 |
| ja | 22 | 39 | |
| zh | 35 | 38 | |
| 합계 | 1,844 | 1,229 | 기본 항목 3,073개 (`BadWordFilter.DefaultEntries`), 0.1.0은 2,661개 |

`zh-simplified.txt`는 항목이 아니라 번체 → 간체 쌍 2,813개다(Unicode 17.0.0 `Unihan_Variants.txt`의 `kSimplifiedVariant`; BMP 쌍만, 첫 번째 대상만, 자기 자신으로의 매핑 제외). `Runtime/TextNormalizer.Simplified.cs`로 생성된다.

### 2. 추출에서 건너뛴 후보

- 라틴 언어(Task 5): 후보 2,336개, 건너뜀 87개. 문법 위반 4개(프랑스어 곡선 아포스트로피 `nulach’` `s’archifoutre`, 네덜란드어 쉼표 `reet trappen, voor zijn`, 숫자 `standje-69`)와 DROP 집합 83개(es 17, pt 14, fr 13, it 17, de 7, nl 13, tl 2). 예: es `mierda` `culo` `bastardo` `idiota`, pt `merda` `cu` `burro`, fr `con` `merde` `bitte`, it `ano` `fica` `negro`, de `kacke` `scheiße` `penis`, nl `del` `nicht` `gat`, tl `bobo` `tanga`.
- 러시아어·일본어·중국어(Task 6): 후보 ru 142 / ja 319 / zh 268, 건너뜀 94개. ru 9(DROP `секс` `хрен` `фига` 등, 백틱이 든 `piz`dyulina`), ja 35(라틴 단독 `3P` `SM`, 혼합 `AV女優` `Gスポット`, 한자 한 글자 `唖`, DROP `バカ` `アホ` `クソ` `デブ` `ハゲ`), zh 50(한자 한 글자 `乳` `奸` `逼` 등, BMP 밖 `𨳒` `𨳊`, 혼합 `卖B` `妈B` `干x娘`, DROP `笨蛋` `白痴` `色情`).

### 3. 소유자 판정과 삭제 규칙이 제거한 것

- 약한 욕 규칙(소유자 2026-10-14): `shit` 수준 이하는 모든 언어에서 통과한다. es `mierda` `cagar` `cabrón` `boludo`, pt `merda` `babaca` `porra`, fr `merde` `chiant` `salaud`, it `cazzata` `stronzata` `scemo`, de `kacke` `scheiße` `arsch`, nl `hufter` `rotzak` `godverdomme`, tl `ulol`, ru `говно` `дерьмо` `срать` `жопа`, ja `えっち` `スケベ` `クソガキ` `畜生`, zh `狗屁` `吃屎` `王八蛋` `白癡`.
- 다른 대상 언어의 일상어(규칙 3): es `coño` `concha` `polla` `puto` `coger`, pt `cona` `pinto` `veado` `bicha`, fr `baiser` `bite` `chatte` `pédé`, it `troia` `scopare` `figa` `negri` `finocchio`, de `vögeln` `möse` `poppen`, nl `eikel` `naaien` `kut`, tl `bobo` `tanga` `paki` `bayag`, ru `хохол` `petuh` `huy` `suka`, ja `めくら` `びっこ` `かたわ` `アスペ` `土方`, zh `他妈` `妈的` `你妈` `日你` `干你` `十三点`.
- 자위·정액급(wank 수준) 속어: pt `punheta`, tl `jakol`, ru `дрочить`, ja `センズリ` `オナホ`, zh `打飞机`.
- 범죄·정치·장르 용어: zh `卖淫` `嫖娼` `三级片`, ja `ロリコン` `スカトロ` `支那`, es `asesinato` `drogas`.
- 두 언어 이상에서 같은 철자로 접히는 중복은 한 파일에만 둔다(`|puta`는 es.txt, `强奸` `轮奸`은 일본어 `強姦` `輪姦`까지 덮는 zh.txt).
- 후보 → 최종 차단(최종 수치는 이후 리뷰 보정과 소유자 반영을 포함): 라틴 7개 언어 2,336 → 131 (큐레이션 직후 93), ru 142 → 68, ja 319 → 22, zh 268 → 35.

### 4. 스윕별로 추가한 경계와 허용

- 라틴(Task 5, 1~4차 스윕 + 5만 목록): 경계 `|puta` `|pute|` `|viado` `|fick` `|hure|` `|hoer` `|flikker|` `|nutte|` `foder|` `stronza|` `|niquer` `|incula` `|fott` `|puttan` `|coglion` `|teef`. en 0.1.0 항목 수정: 삭제 `kunt` `domme` `paki` `finocchio`(다른 언어 일상어), 경계 `|quim|` `|cialis|` `|spic|` `|fag|` `|fags|` `|dolcett|` `|giral|` `|rimming` `|felch`, 허용 17개 추가 후 정리. 대표 허용: de `!schinken` `!kikeriki`, fr `!rappel` `!salopette` `!pique-nique`, nl `!benutten` `!ietwat` `!hoera`, es `!putada` `!pendejada` `!rapero`, it `!puttanesca` `!puttanata`, en `!cummings` `!rapper` `!thumping`.
- 허용 범위 점검(Task 5 리뷰 보정): 허용이 차단어를 다시 여는 경우를 잡아 `!snigger` `!thorny` `!thumping` `!racoon` `!bonner` `!fodder` `!fagott` `!lesso`를 경계 추가·축소·삭제했다. 접힌 키가 차단 키와 같아지는 충돌은 `!rapping`만 남겼다(판단 9: `rapping`은 상위 2만 일반어, 정확한 `raping`은 계속 차단, 늘인 `raaaping`만 통과).
- 붙여 쓴 형태 15개 추가(`hijodeputa` `filhodaputa` `niquetamere` `fickdich` 등), 활용형 추가(de `fickt`, nl `neukt`, pt `fodeu`, es `jodido`, it `stronzi`, fr `nique`).
- 러시아어(Task 6 보정): 초안의 `ебат ебан ебуч ебло`는 일상어 990형을 막아(скребание, хлеб-, колеб-, шебутной 등 29개 군) 단어 첫머리 또는 접두 모음·ъ·ь 뒤의 `еб`만 막는 방식으로 교체했다(`|еба |ебу |ебл ... аеб оеб уеб ъеб ьеб`). 일상어 허용은 `!колебал !хлебал !гребан !хребет !эстебан !педикюр !скипидар !психуй ... !|бляшк !|сучков` 등. 결과: 153만 형태 목록에서 오탐 990개 → 60개(모두 욕설 또는 сука/сучок 동형).
- 일본어: Citrine 허용 26개 중 15개 유지(차단어가 삭제된 죽은 허용 11개 삭제), 결합 허용 `!カチンコ !キッチンコーナー !カフェラッテ !ペリフェラル !ロックフェラー !背中出し !グレイプ !あけおめこ !おめことし` 및 〜き違い 7개. `クンニ` `ガイジ` `スペルマ`는 허용으로 구분이 불가능해 삭제했다.
- 중국어: 초안의 `傻比`(装傻比较好) 삭제. `机掰` 결합 허용 6개와 `鸡掰` 추가. 성 단어(Task 6b 보정)의 짧은 허용 8개를 삭제하고 전체 구절 허용 24개로 교체했다(`!当做爱好 !理性交流 !一次性交易 !异性交往 !亲自慰问 !聊以自慰` 등).
- 타갈로그어(Task 6b 보정): `tatalik|` → `gtatalik|`(matatalik, pinakamatatalik 보호).

### 5. 스윕 적중 수 (서로 다른 사유 / 적중 합계)

2만 위 목록(`SweepCorpus`). 0.1.0 데이터만 → 큐레이션 직후 → 최종(5b1061f). ru·ja·zh의 "큐레이션 직후"는 Task 6 첫 스윕 값이다.

| 코퍼스 | 0.1.0 항목만 | 큐레이션 직후 | 최종 |
|---|---|---|---|
| en | 36/67 | 37/68 | 33/61 |
| ko | 27/51 | 27/51 | 27/51 |
| es | 6/12 | 12/22 | 13/21 |
| pt | 6/12 | 12/22 | 12/16 |
| pt_br | 8/17 | 18/30 | 17/23 |
| fr | 9/26 | 17/45 | 17/25 |
| it | 11/16 | 22/50 | 17/38 |
| de | 12/14 | 25/34 | 22/37 |
| nl | 19/28 | 29/45 | 19/29 |
| tl | 12/68 | 15/73 | 12/24 |
| ru | 0/0 | 17/30 | 22/35 |
| ja | 0/0 | 2/2 | 5/5 |
| zh_cn | 5/9 | 12/18 | 16/22 |
| zh_tw | 3/5 | 10/13 | 14/17 |
| en-common | 5/5 | 5/5 | 4/4 |
| NSMC | 203/2543 | 203/2543 | 202/2542 |

- 손질 전 원본 후보를 그대로 넣었을 때: en 155/483, es 160/732, pt 190/732, fr 185/680, it 228/886, de 130/677, nl 173/645, tl 69/358, ru 13/20, ja 36/45, zh_cn 50/60, zh_tw 40/48.
- 5만 목록(참고, 저장소 밖) 최종: de 36/70, en 71/134, es 27/56, fr 37/60, it 40/94, ja 6/6, nl 34/63, pt 31/48, pt_br 38/62, ru 32/80, zh_cn 22/37, zh_tw 21/30.
- 말뭉치 출처: FrequencyWords 커밋 `525f9b560de45753a5ea01069454e72e9aa541c6`(CC-BY-SA, 저장소에 넣지 않음).

### 6. 의도적으로 남긴 적중

- 새 항목이 자기 자신이나 활용형에 걸린 것: es `puta` `joder` `gilipollas` `follar`, pt `foda-se` `foder` `caralho` `buceta`, fr `connard` `salope` `pute` `encul-`, it `cazzo` `stronzo` `puttana` `fanculo`, de `arschloch` `schlampe` `hurensohn` `fick`, nl `klootzak` `hoer` `neuken` `flikker`, tl `tangina`, ru `хуй` `пизда` `ебать` `сука` `мудак` `шлюха`, zh `他妈的` `傻逼` `婊子` `鸡巴` `贱人`, ja `中出し`.
- `|puta`가 프랑스어 `putain`(순위 344)을 막는다: 프랑스어의 핵심 욕이라 유지하고 소유자 확인 사항으로 올렸다.
- 강간 계열(`强奸` `轮奸` `レイプ`, 일본어 `強姦` `輪姦`)은 뉴스에서도 쓰이지만 영어 `|rape`와 한국어 `강간`과 같은 기준이라 유지했다.
- 수용한 한계: `neuken` → `verneuken`, `|puta` → `putaria`, fr `râpe`/`rappe`(영어 `rape`로 접힘), `!rapping`·`!|seeks|`의 늘인 철자 통과, 경계 어근에 붙여 쓴 닉네임(`sexogratis`, `seksfilm`).
- 0.1.0 영어 항목이 같은 뜻의 외래어에 걸리는 것: `sex` `viagra` `orgasm` `incest` `pedophile` `clit` `vibrator` `pansy` `nudity` 등은 0.1.0에서 소유자가 승인한 기준이라 유지했다.

### 7. 소유자 확인 결과 (2026-10-14, Task 6 이후)

- 모든 언어의 평범한 성 단어(섹스·자위, 모호하지 않은 오르가슴)를 차단한다. 새 항목: en `masturb`, es `|sexo|`, fr `|sexe|`, it `|sesso|`, de `selbstbefriedigung`, nl `|seks|`, tl `gtatalik|` `gtalik|`, ru `|секс|`+격변화 4개 `мастурб` `|онани` `оргазм`, ja `セックス` `オナニー` `オーガズム`, zh `做爱` `性交` `自慰` `手淫`. 슬랭(`дрочить` `jakol` `punheta` `センズリ` `打飞机`)과 격식어(de `Geschlechtsverkehr`, en `intercourse`)와 zh `高潮`는 통과한다.
- 영어 `pissing` `shithead`(`shit head`)는 약한 욕 규칙에 따라 삭제했다(`piss pig`는 페티시 용어라 유지).
- 언어별 유지/제외 목록은 제시된 대로 승인됐다(제외한 `ビッチ` `獣姦` `近親相姦` `ポコチン` `ちんちん` `支那` 등은 되살리지 않음).
- 영어 자판 읽기(2벌식 스트림 B)를 제거한다(스펙 결정 7): `tlqkf` `qudtls` `tq` `sra` `torri` `rotor`가 통과하고 한글 `ㄴㄱㅁ`는 계속 막는다. en의 자판 철자 항목 `tlqkf tlqkd wlfkf rotorl rotprl |tlqk|`도 함께 삭제했다(`sibal`류 로마자 표기와 혼합 철자 `tlbal`, `|siqk|`는 유지). 이로 사라진 한국어 자판 오탐: es·pt `sra`, it `torri` `tutorgirl`, en-common `qt`, 5만 목록의 `rotor` `rosmarin` `rosnar` `rothond` 등. NSMC에서는 영문 자판으로 쓴 한국어 리뷰 2건이 통과로 바뀌었다(203/2543 → 201/2541).
- 이 결정의 항목 수 증감(715b577): 새 차단 23개, 새 허용 18개, 삭제 3개(3,019 → 3,057). 그 커밋 메시지의 "RED 26개 / 차단 25·허용 16 추가"는 오기이며 정확한 값은 RED 25개(새 차단 23 + 약한 욕 2), 차단 23·허용 18 추가다.
- 이후 리뷰 보정(5b1061f)으로 항목은 3,073개가 되었다(허용 +24 −8, 차단 1 교체). 네 가지 판단은 유지했다: 영어 `!rapping`, 중국어 `这叫做爱` 차단(그 대가로 `叫做爱丽丝`도 차단), `性交易` 차단, 러시아어 `!страхуй`·`!страхуе`의 끝 경계 없음.

### 8. 비용

- 스캔 비용(`ReportsScanCost`, Release, 입력당 10만 회, 3회 중앙값, ns), 0.1.0 기준선 → 0.2.0: Player One 1316 → 977, 탈출친구 391 → 383, f.u.c.k you 781 → 417, привет как дела 826 → 1882, こんにちは今日はいい天気 459 → 290, 你好世界朋友们 306 → 215. 한국어·영어·라틴 입력은 모두 기준선 이하(2벌식 스트림 제거 덕분)이고, 키릴 입력은 새 스트림 비용으로 늘었지만 5,000 ns 안이다. 원시 값(키릴/가나/한자): 1회 2271/964/198, 2회 1882/290/244, 3회 1736/264/215.
- 기본 필터 빌드 비용(`ReportsDefaultBuildCost`): 항목 3,073개, `buildMs=18.4`, `retainedBytes=419088`(기준 50 ms 이하).
