# Bun3 Bad Words

Allocation-free Korean and English bad word detection for short user text (nicknames, chat lines). netstandard2.1, no dependencies, no UnityEngine reference.

## Usage

```csharp
using Bun3.BadWords;

bool rejected = BadWordFilter.Default.ContainsBadWord(nickname);

// Consumer-specific additions on top of the bundled dictionary.
var filter = new BadWordFilter(BadWordFilter.DefaultEntries.Concat(new[] { "customword", "!allowedword" }));
```

`BadWordFilter.Default` builds on first access; touch it during boot to prewarm. Instances are immutable and thread-safe. `ContainsBadWord` allocates no managed memory.

## Entry grammar

`[!][|]word[|]` — `!` allow entry (suppresses block matches it fully covers), `|` word boundary before/after (English only). Entries containing Hangul are Korean; others are English.

## What is normalized

- Removed: zero-width/format characters, combining marks, Hangul fillers.
- Mapped: fullwidth ASCII, Latin-1 accents, Cyrillic/Greek look-alikes.
- Korean: digits, spaces and symbols between Hangul are dropped, and loose compatibility jamo are composed into syllables. Latin letters are also read as 2-beolsik keyboard input (`tlqkf`). Single Latin letters and short initial-consonant forms typed among Hangul are not treated as keyboard input.
- English: leetspeak digits, separators as word boundaries, spaced single letters joined (`f u c k`), stretched letters (`fuuuck`).

## Limits

Not handled: initial-consonant projection of syllables, symbols imitating jamo, digit puns mixed into Korean words, two-letter splits (`fu ck`), self-censoring (`f*ck`), mathematical alphanumerics, conjoining jamo (pass NFC text). Korean entries may contain only Hangul and spaces.

More limits: a leet digit next to an end boundary is not folded (`ass1`); `!` is not read as `i`; a precomposed syllable followed by loose jamo is not recombined; more than 32 matches in one scan count as a bad word, so an allowed word repeated 33 or more times blocks; English entries containing symbols treat those symbols as word boundaries.

Memory: inputs longer than the shared `ArrayPool`'s maximum array length (about 1M chars on some runtimes) may allocate. Cap input length in the consumer; nicknames and chat lines are far below it.

## Default dictionary

Sources live in `Data~/` (not imported by Unity). `Runtime/BadWordFilter.Default.cs` is generated; regenerate with `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`. Bump the package version for any data change. Licenses: `Third Party Notices.md`.
