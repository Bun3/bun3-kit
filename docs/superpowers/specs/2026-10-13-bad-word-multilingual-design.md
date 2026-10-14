# Bad Word Filter Multilingual Design

**Date:** 2026-10-13
**Target:** `common/src/com.bun3.badwords` 0.2.0 (same constraints as 0.1.0: netstandard2.1, C# 9, no dependencies, zero allocation per call)
**Builds on:** [`2026-10-12-bad-word-filter-design.md`](2026-10-12-bad-word-filter-design.md) (0.1.0). Everything not changed here stays as specified there.
**Research:** [`docs/research/2026-10-13-bad-word-multilingual-sources.md`](../../research/2026-10-13-bad-word-multilingual-sources.md)
**First consumer:** jurassic-paradise. Its language menu: English, Español (Latinoamérica), Português (Brasil), Português, 한국어, Русский, Nederlands, Bisaya, Français, Deutsch, Italiano, 日本語, Español, 简体中文, 繁體中文, Gaeilge.

## Goal

Ship strong-expression entries for every language the first consumer plans to support, so a consumer can add a UI language without dictionary work. Add the engine stages those languages need (Cyrillic, kana/Han) without raising the cost of Korean, English or other Latin-script input.

## Non-goals (0.2.0)

- Cebuano-specific and Irish lists. No public list exists for either. Tagalog (LDNOOBW `fil`) covers the profanity Philippine players commonly type. Irish speakers are bilingual and the English list covers them.
- Runtime language selection. See Decision 1.
- Latin Extended-A folding (`œ`, `č`). This is still deferred to the chat milestone.
- Romanized Japanese and Chinese (`chinko`, pinyin). No public source covers them.
- Political or censorship terms, and masking (`Mask`).

## Decisions (from design discussion)

1. **Every language applies to every input.** A nickname is shown to players of all UI languages. The host's re-validation must also agree with the client whatever language either side runs. `ContainsBadWord` therefore has no language parameter.
2. **Data licenses: MIT, Unicode License and CC-BY-4.0.** This supersedes Decision 4 of the 0.1.0 spec.
   - **LDNOOBW (CC-BY-4.0)** is the only public source for de, nl, ru, zh and tl. `Third Party Notices.md` carries its attribution, the license link and a statement that the lists were modified. Consumers must reproduce these notices.
   - **DRM:** CC-BY-4.0 §2(a)(5)(C) forbids applying effective technological measures (DRM) to the material. Compiling the list into an assembly is a format change, which the license allows. A DRM wrapper on the shipped executable is not. The first consumer ships without a DRM wrapper.
   - **Still excluded:**
     - CC-BY-SA, which covers Wiktionary and the frequency lists;
     - NC, ND and GPL licenses;
     - unlicensed lists;
     - scrapes labelled Unlicense: LEv145/mat-words, whose upstream rights are unknown.
   - **cuss (MIT)** names Wiktionary and websites among its sources. Only short roots are taken from it, never phrases. The research doc records this residual risk.
3. **One stage per script.**
   - **Latin stage:** shared by en, es, pt, fr, it, de, nl and tl.
   - **New Cyrillic stage.**
   - **New kana/Han stage:** shared by ja and zh.
   - A new stage runs only when the input contains its script.
   - **Rejected alternatives:**
     - One merged non-Hangul stage. Cyrillic↔Latin look-alike folding can only go one way, so it loses either Cyrillic entries or Latin evasions. Kana/Han also have no word boundaries.
     - One filter per language. The Latin stream would be scanned eight times, and language sets could diverge between peers.
4. **Strong expressions only**, the same bar as 0.1.0. Mild insults pass, as `바보` does in Korean.
   - **Owner ruling 2026-10-14:** words at the level of English `shit` and milder pass in every language (`mierda`, `merda`, `merde`, `Scheiße`, `クソ`). The `scheiße` example under "Step 0 and stream C" shows the `ß` mechanism only; it is not a default entry.
   - **Owner ruling 2026-10-14:** plain sexual words (sex, masturbation, orgasm) are blocked in every language, matching the 0.1.0 Korean and English lists.
5. **One release.** 0.2.0 contains every language. 0.1.0 merges first and is not extended.
6. **No native review.** Frequency-list sweeps and an owner spot-check replace it.
7. **Owner ruling 2026-10-14: Latin text is not read as Korean keyboard input.** Stream B of 0.1.0 is removed: Korean typed with the IME off (`tlqkf`) passes, and Latin words such as `sra`, `torri` and `rotor` no longer match Korean entries through it. The `Short` pattern flag existed only for stream B and is removed. The `en.txt` entries that spell a Korean word entirely in 2-beolsik keys (`tlqkf`, `wlfkf`) are removed with it; romanized forms (`sibal`) stay.

## Layout changes

```
common/src/com.bun3.badwords/
  package.json, Bun3.BadWords.csproj   // version 0.2.0; description no longer says "Korean and English"
  README.md                            // languages, grammar changes, CC-BY note
  Third Party Notices.md               // + LDNOOBW (CC-BY-4.0, modified), cuss, MosasoM, Citrine (MIT); Unihan under the Unicode section
  Data~/es.txt pt.txt fr.txt it.txt de.txt nl.txt tl.txt ru.txt ja.txt zh.txt   // new, one per language
  Data~/zh-simplified.txt              // Traditional→Simplified pairs extracted from Unihan
  Runtime/
    BadWordFilter.cs                   // classification, new automata, scan order
    TextNormalizer.cs                  // ß; Cyrillic and Cjk streams
    TextNormalizer.Simplified.cs       // <auto-generated/> Traditional→Simplified table
common/tests/Bun3.BadWords.Tests/
  DefaultEntriesGenerator.cs           // reads every Data~/*.txt; renders both generated files
```

The data files are grouped by language for curation only. The generator concatenates them, and the engine assigns each entry by script, not by file. Each file header names its sources, the pinned commits and the licenses.

## Entry grammar changes

An entry is classified by the raw characters it contains, before step-0 look-alike folding (which would turn Cyrillic into Latin):

| Contains | Stage | Allowed characters | `\|` boundaries | Minimum length |
|---|---|---|---|---|
| Hangul | Korean (unchanged) | Hangul, spaces | rejected | — |
| Cyrillic U+0400–U+04FF | Cyrillic | Cyrillic letters, the Latin look-alikes of stream D, spaces | allowed | — |
| Kana or Han (ranges in stream E) | Kana/Han | kana, Han, `々`, `ー`, `・`, spaces | rejected | 2 after normalization |
| none of the above | Latin (unchanged) | Latin letters, leet digits and symbols as in 0.1.0 | allowed | — |

`ArgumentException` is also thrown when an entry:
- mixes stages (for example `卖B`, `AV女優`, Hangul with Cyrillic);
- holds a letter of any other script (Greek, Arabic, …). This is the 0.1.0 fix that stops `хуй` from silently becoming the Latin pattern `xy`;
- holds a surrogate pair. Stream E cannot match characters outside the BMP;
- is a kana/Han entry shorter than two characters after normalization (`性`, `屌`).

Look-alike folding during classification follows each stream. A Cyrillic entry containing Latin `x` (`траxать`, from LDNOOBW) is folded to Cyrillic, not rejected.

## Normalization changes

```
text
 ├─ A. Hangul stream      → KO   (unchanged)
 ├─ C. Latin stream       → EN, ENc   (ß added)
 ├─ D. Cyrillic stream    → RU, RUc   (new; only if the text has Cyrillic)
 └─ E. kana/Han stream    → CJK       (new; only if the text has kana or Han)
```

Every stream output remains no longer than its input, so the existing scratch buffers are reused.

### Step 0 and stream C — `ß`

- `ß` U+00DF and `ẞ` U+1E9E fold to `s`. They are "other" today, which splits `scheiße` into `schei e`.
- German entries are written with `ß` and register as `scheise`. The input `scheiße` matches in scan 1. The input `scheisse` matches in scan 2, because ENc collapses the doubled letter. No length expansion is needed.

### Stream D — Cyrillic

- **Gate:** the stream is skipped unless the text contains a character in U+0400–U+04FF. Latin-only input therefore never reaches the Cyrillic automata.
- **Per character:**
  - Format, NonSpacingMark, EnclosingMark and Hangul fillers are removed, as in step 0. Decomposed `й` and `ё` lose their marks and become `и` and `е`.
  - Fullwidth ASCII is mapped to ASCII first.
  - A Cyrillic letter is lowercased with `char.ToLowerInvariant`; then `ё`→`е` and `й`→`и`.
  - Latin look-alikes map to Cyrillic. Any case: `a`→`а`, `c`→`с`, `e`→`е`, `o`→`о`, `p`→`р`, `x`→`х`, `y`→`у`. Uppercase only: `B`→`в`, `H`→`н`, `K`→`к`, `M`→`м`, `T`→`т`.
  - Everything else is a **boundary**. Consecutive boundaries collapse to one.
- **Single-letter join:** as in stream C (`х у й` → `хуй`).
- **Scans:** D1 runs the stream against RU. D2 runs it with runs of identical letters collapsed against **RUc**. RUc is built like ENc: block patterns without a doubled letter, plus every allow pattern in collapsed form.
- **Boundaries** are checked as in stream C. Spaces stay boundaries on purpose:
  - Joining words makes `где батарея` contain `ебат`.
  - `|бля` must not match `рубля`.

### Stream E — kana/Han

- **Gate:** the stream is skipped unless the text contains kana (U+3041–U+30FF, U+FF66–U+FF9F) or Han (U+3400–U+4DBF, U+4E00–U+9FFF, U+F900–U+FAFF).
- **Per character:**
  - Hiragana U+3041–U+3096 → katakana (+0x60).
  - Halfwidth katakana U+FF66–U+FF9D → fullwidth, using the 56-entry table given by their Unicode `<narrow>` decompositions.
  - Voiced marks (U+3099, U+309B, U+FF9E) combine with the preceding katakana:
    - `カキクケコサシスセソタチツテトハヒフヘホ` take +1;
    - `ウ` becomes `ヴ`.
  - Semi-voiced marks (U+309A, U+309C, U+FF9F) give +2 on `ハヒフヘホ`.
  - A mark that cannot combine is dropped. Step 0 must not remove U+3099 and U+309A before this rule runs.
  - Han is mapped to Simplified Chinese through `TextNormalizer.Simplified`. Characters missing from the table are kept.
  - Katakana, Han and `々` (U+3005) are kept. **Everything else is dropped without a boundary**: spaces, `ー`, `・`, digits, Latin, Hangul and symbols. Japanese and Chinese do not separate words with spaces, so inserted separators are evasion (`ち ん こ`, `ちーんこ`). This is the same choice as stream A.
- **Scan:** E runs against CJK. There is no collapsed scan, because doubled kana are ordinary (`ちんちん`).
- **Traditional→Simplified table:**
  - **Source:** `Data~/zh-simplified.txt` holds pairs extracted from Unihan `kSimplifiedVariant`. Its header records the Unicode version.
  - **Pairs kept:** BMP pairs only, and only the first listed target. Self-mappings are dropped.
  - **Generated file:** the generator renders the pairs into `TextNormalizer.Simplified.cs` as two `const string`s sorted by source.
  - **Lookup:** binary search, which allocates nothing.
  - **Up-to-date check:** the same check as `BadWordFilter.Default.cs` (`BUN3_BADWORDS_REGEN=1` rewrites it).

### Matching and scan order

Matching rules are unchanged. Scan order: A, C1, C2, D1, D2, E. The first unsuppressed block span wins. `Explain` adds the stream kinds `Cyrillic`, `CyrillicCollapsed` and `Cjk`, and no longer reports `Keyboard`. The `Short` flag is removed with stream B (Decision 7).

## Curation (one-time for 0.2.0)

| File | Sources (pinned commit) | Notes |
|---|---|---|
| es | cuss `es` confidence 2 (`6bab3fe`), LDNOOBW `es` (`5faf2ba`) | Spain and Latin America merged |
| pt | cuss `pt` and `pt-pt`, LDNOOBW `pt` | Brazil and Portugal merged |
| fr | LDNOOBW `fr` roots, short roots from cuss `fr` | phrases dropped |
| it | cuss `it` confidence 2, LDNOOBW `it` | inflection runs reduced to one root |
| de | LDNOOBW `de` | written with `ß` |
| nl | LDNOOBW `nl` | idioms and ordinary words removed in bulk |
| tl | LDNOOBW `fil` | `bobo` and `tanga` dropped (es, pt everyday words) |
| ru | LDNOOBW `ru` Cyrillic and Latin transliterations, plus hand-written mat roots | the `ебать` family matches `еб` only at word start or after a prefix vowel, `ъ` or `ь` (`\|еба`, `заебал`, `отъебись`), never after a consonant (`тебе`, `хлеб`, `колебание`); mat written without `ъ` (`отебись`) passes |
| ja | MosasoM `Sexual.txt` and `Offensive.txt` (`e24de6e`), Citrine exceptions as `!` entries (`e6a3731`) | derived `*_with_mask` files unused; political slurs dropped |
| zh | LDNOOBW `zh` | single-Han and mixed-script entries dropped or rewritten |

Latin transliterations of any language (ru `pizda`, `huy`) go through the Latin stage. A file may hold Latin-stage entries.

**Rules**

1. **Keep:** explicit sexual terms, slurs and harsh insults, including plain sexual words (sex, masturbation, orgasm), which are blocked in every language (owner ruling 2026-10-14).
2. **Drop:**
   - ordinary words and mild insults (ja `バカ` `アホ` `クソ` `デブ` `ブス`, zh `笨蛋` `白痴`);
   - drug, crime and political terms;
   - phrases already covered by a root.
3. **Cross-language everyday words are never added.** This covers a word that is common in any target language, compared in normalized form: `con`, `bitte`, `nicht`, `del`, `mama`, `negro`, `comer`, `fica`, and it `ano` against es `año`→`ano`.
4. **Short Latin roots that sit inside common words** get a leading boundary (`|cono` against `economy`, `|puta` against `computation`) or are dropped.
5. **Kana/Han false positives** found by the sweep get `!` entries (`!フェラーリ`, `!カフェラテ`, `!ちんちん電車`).

**Sweeps** (opt-in `SweepCorpus`, corpora outside the repo)
- **Corpora:** FrequencyWords, top 20,000 words for en, ko, es, pt, pt_br, fr, it, de, nl, tl, ru, ja, zh_cn and zh_tw, where available. It is CC-BY-SA, so it is never committed; the report records its commit. The 0.1.0 corpora (NSMC, google-10000) are re-run.
- **Review:** every hit is reviewed. It stays only if the word is a strong expression in some target language. Otherwise it is resolved by deletion, a boundary or an `!` entry.
- **Report:** results are written to `docs/research/` (hit counts per corpus before and after).
- **Owner spot-check:** before merge, the owner checks a sample of arguable kept words per language, as with R1 in 0.1.0.

## Tests

Additions to the 0.1.0 suites, in both runners unless noted:

1. **Normalizer cases:**
   - `ß`/`ẞ`;
   - stream D: lowercasing, `ё`/`й`, every look-alike row, gate off for Latin-only text, single-letter join;
   - stream E: hiragana, halfwidth, combining and spacing voiced marks, a mark with nothing to combine, Traditional→Simplified, dropped separators, gate off for text without kana or Han.
2. **Rule regression with fake dictionaries:**
   - a Cyrillic block entry matched through a mixed-script input (`xуй`);
   - `|бля` not matching `рубля`;
   - a kana entry matched from hiragana, katakana and halfwidth input;
   - a Traditional input matching a Simplified entry;
   - a kana/Han allow covering a block.
3. **Constructor validation:**
   - mixed stages;
   - another script's letter (`αβγ`);
   - a surrogate;
   - a one-character Han entry;
   - `|` on a kana/Han entry.
4. **Default dictionary smoke:**
   - **Must block:** two or more strong words per language.
   - **Must pass:** `con`, `bitte`, `nicht`, `del`, `mama`, `año`, `economy`, `computation`, `тебе`, `хлеб`, `рубля`, `где батарея`, `男性`, `奶茶`, `フェラーリ`, and every 0.1.0 must-pass case.
5. **Zero allocation:**
   - Inputs that pass through every scan without a match in each gated stream: Latin-only, Cyrillic, and kana/Han, each in a ≤256-character and a >256-character form.
   - The test text is checked to produce no match. This repeats the lesson of I3 in 0.1.0, where the "clean" text matched early and skipped the later scans.
6. **Thread safety:** the mixed inputs include Cyrillic and kana/Han.
7. **Generated files up to date** (dotnet only): both generated files.
8. **Build cost report:** `Default` build time and retained bytes, printed and not asserted.

## Performance targets

- **Korean, English and other Latin input:** cost per call stays at the 0.1.0 level (the gate checks add one pass over the text). Measured before and after.
- **Cyrillic and kana/Han input:** a 16-character input stays within a few microseconds.
- **`Default` build:** measured and recorded. If a cold build in a fresh process exceeds 50 ms on the machine that measured 0.1.0 (about 28 ms), the cause is investigated before merge.

## Consumer impact

- The public API signatures are unchanged.
- **Behavior changes:**
  - `DefaultEntries` grows.
  - Entries that 0.1.0 accepted but silently shrank are now rejected. These are entries with letters of another script.
  - Latin text is no longer read as Korean keyboard input (Decision 7), so `tlqkf` passes.
- **Consumer obligations:**
  - Reproduce the LDNOOBW attribution with the other notices.
  - Keep CC-BY-4.0 data out of DRM-wrapped builds.
- **jurassic-paradise:**
  - Update `packages-lock.json`.
  - Update `docs/third-party/bun3-badwords.md` with the LDNOOBW attribution and the no-DRM condition.
  - Its tests use fake dictionaries and need no change.

## Known limits (added)

| Not handled | Reason |
|---|---|
| Cebuano-specific and Irish words | no public list (Non-goals) |
| Russian inflections outside the curated roots | roots are substring patterns; the sweep bounds their length |
| Leet digits in Cyrillic (`3`→`з`, `6`→`б`) | false-positive risk with digits in nicknames; add on real cases |
| Han outside the BMP (CJK Extension B+) | stream E keeps UTF-16 units; such entries are rejected |
| Japanese Scunthorpe cases not yet seen | handled by `!` entries as they appear |
| Romanized Japanese and Chinese | no source (Non-goals) |

## Acceptance

- `dotnet build` with zero warnings; `dotnet test` green.
- The bun3-kit Unity project compiles the package and its EditMode tests are green.
- Zero-allocation tests are green in both runners for all three gated input kinds.
- The smoke lists pass with the curated dictionary.
- A sweep report exists, and every remaining hit is a reviewed strong expression.
- `Third Party Notices.md` lists every source with its license text and the LDNOOBW modification notice.
- README documents the languages, the grammar changes and the CC-BY obligation.
- Package version is 0.2.0.
