# Bun3 Bad Words

Allocation-free multilingual bad word detection, and masking, for short user text (nicknames, chat lines). Twelve languages are bundled: Korean, English, Spanish, Portuguese, French, Italian, German, Dutch, Tagalog, Russian, Japanese and Chinese. Every language applies to every input, whatever the UI language. netstandard2.1, no dependencies, no UnityEngine reference.

## Usage

```csharp
using Bun3.BadWords;

bool rejected = BadWordFilter.Default.ContainsBadWord(nickname);

// "you f.u.c.k off" -> "you ******* off"; returns the same instance when nothing is blocked.
string shown = BadWordFilter.Default.Mask(chatLine);

// Consumer-specific additions on top of the bundled dictionary; the attribute lists the field in the editor tester.
[BadWordTesterSource]
static readonly BadWordFilter Filter = new BadWordFilter(BadWordFilter.DefaultEntries.Concat(new[] { "customword", "!allowedword" }));
```

## Editor tester

`Window > Bun3 > Bad Word Tester` checks text line by line against `Default` or any static field marked `[BadWordTesterSource]`, and shows the stream and normalized span that blocked each line. Fields are re-read after every script reload.

`BadWordFilter.Default` builds on first access; touch it during boot to prewarm. Instances are immutable and thread-safe. `ContainsBadWord` allocates no managed memory; `MarkBadWords` and `Mask` allocate.

## Masking

`MarkBadWords(text, marks)` flags every character of each blocked word that no allow entry covers, and returns true exactly when `ContainsBadWord` does. A word's mark runs from the first to the last character that formed it, so separators inside it are marked too (`f.u.c.k` is marked whole, `시 발` from `시` to `발`). Use it to mask text that carries markup the filter should not see, such as rich-text tags: scan the text with the tags removed and map the flags back. `Mask(text)` replaces marked characters with `*` and keeps whitespace.

## Entry grammar

`[!][|]word[|]` - `!` allow entry (suppresses block matches it fully covers), `|` word boundary before/after (Latin and Cyrillic entries only). The scripts the entry contains pick the stage (any Hangul, any Cyrillic letter, any kana or Han character); an entry that mixes stages is rejected:

| Entry contains | Stage | Allowed characters | Boundaries |
|---|---|---|---|
| Hangul | Korean | Hangul and spaces | no |
| Cyrillic | Cyrillic | Cyrillic letters, Latin `a c e o p x y` in any case and `B H K M T`, spaces | yes |
| kana or Han | kana/Han | kana, Han, the iteration mark, the prolonged sound mark, the middle dot, spaces; at least two characters after normalization | no |
| anything else | Latin | Latin letters, digits, symbols | yes |

Rejected: mixed stages, letters of any other script (Greek, Arabic; Cyrillic look-alikes inside a Latin word) and characters outside the Basic Multilingual Plane.

## What is normalized

- Removed: zero-width/format characters, combining marks, Hangul fillers.
- Mapped: fullwidth ASCII, Latin-1 accents, sharp s to `s`, Cyrillic/Greek look-alikes.
- Korean: digits, spaces and symbols between Hangul are dropped, and loose compatibility jamo are composed into syllables. Latin text is never read as Korean keyboard input, so Korean typed with the IME off (`tlqkf`) passes.
- Latin: leetspeak digits, separators as word boundaries, spaced single letters joined (`f u c k`), stretched letters (`fuuuck`).
- Cyrillic (runs only when the text has Cyrillic): lowercase, yo to ye, short i to i, Latin look-alikes folded into Cyrillic, separators as boundaries, single letters joined, stretched letters collapsed.
- Kana/Han (runs only when the text has kana or Han): hiragana and halfwidth katakana folded to katakana with voiced marks combined, Traditional Han mapped to Simplified, every other character dropped.

## Limits

Not handled: initial-consonant projection of syllables, symbols imitating jamo, digit puns mixed into Korean words, two-letter splits (`fu ck`), self-censoring (`f*ck`), mathematical alphanumerics, conjoining jamo (pass NFC text). Korean entries may contain only Hangul and spaces.

More limits: a leet digit next to an end boundary is not folded (`ass1`); `!` is not read as `i`; a precomposed syllable followed by loose jamo is not recombined; more than 32 matches in one scan count as a bad word, so an allowed word repeated 33 or more times blocks; Latin entries containing symbols treat those symbols as word boundaries. Latin Extended-A letters are not folded, romanized Japanese and Chinese are not covered, leet digits are not folded in Cyrillic, Han outside the Basic Multilingual Plane is dropped, masking can only cover whole characters that formed a word (a final consonant typed as a loose jamo is masked with the syllable that took it), Russian inflections outside the bundled roots may pass, and there are no Cebuano- or Irish-specific words. The kana/Han stream has no word boundaries, so its entries also match across a junction of two words and across dropped spaces and punctuation: two ordinary words can join into an entry. Such cases are handled with allow entries as they are found.

Memory: inputs over 256 characters use a per-thread scratch buffer that grows to twice the longest input seen on that thread and is kept; the first longer input on each thread allocates. Cap input length in the consumer; nicknames and chat lines are far below it.

## Default dictionary

Sources live in `Data~/` (not imported by Unity): one file per language plus `zh-simplified.txt`, the Traditional to Simplified Han pairs. `Runtime/BadWordFilter.Default.cs` and `Runtime/TextNormalizer.Simplified.cs` are generated by the same command; regenerate with `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`. Bump the package version for any data change. Licenses: `Third Party Notices.md`.

## License note

Part of the dictionary is derived from LDNOOBW under CC-BY-4.0. Consumers must reproduce its notice from `Third Party Notices.md` (for example on a credits or licenses screen) and must not apply DRM to builds that include it.
