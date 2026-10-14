# Bad Word Filter Multilingual (0.2.0) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `com.bun3.badwords` 0.2.0 — strong-expression default entries for every language jurassic-paradise plans to support, plus the Cyrillic and kana/Han engine stages those languages need, without raising the per-call cost of Korean, English or other Latin-script input.

**Architecture:** Entries are assigned to a stage by their raw characters (Korean, Latin, Cyrillic, kana/Han). Two new normalized streams join the existing A/B/C: stream D (Cyrillic, scanned plain and collapsed) and stream E (kana/Han, kana folded to katakana and Traditional Han mapped to Simplified through a generated Unihan table). Each new stream runs only when the input contains its script. The default dictionary grows from one data file per language under `Data~/`.

**Tech Stack:** netstandard2.1, C# 9, NUnit (dotnet: NUnit 4.1 via `common/tests/Bun3.BadWords.Tests`; Unity: Test Framework NUnit 3), Unity 6000.3.14f1 (bun3-kit `unity/` project), Python 3 (scratch-only extraction scripts).

**Spec:** `docs/superpowers/specs/2026-10-13-bad-word-multilingual-design.md` (builds on `docs/superpowers/specs/2026-10-12-bad-word-filter-design.md`; research: `docs/research/2026-10-13-bad-word-multilingual-sources.md`)

## Global Constraints

- Work on branch `Bun3/bad-word-filter` in worktree `E:/Projects/orca/workspace/bun3-kit/bad-word-filter` (fast-forwarded to `main` 197d5ba on 2026-10-14). Merging back into `main` and pushing need the owner's explicit approval at that time.
- Package path `common/src/com.bun3.badwords`; version `0.2.0` in both `package.json` and `Bun3.BadWords.csproj` (Task 7). The public API signatures do not change.
- netstandard2.1, `LangVersion` 9.0, block namespaces, **no dependencies**, `noEngineReferences: true`.
- `ContainsBadWord` allocates nothing: no strings, closures, LINQ, boxing or iterators on the call path. Every stream output stays no longer than its input, so the existing stack/scratch buffers are reused.
- English only in code, comments, README and package metadata. **No Hangul, Cyrillic, kana or Han characters in `Runtime/` or `README.md`** — write `\uXXXX` escapes in code and describe characters in words in the README. Non-Latin literals are allowed only as test data and in `Data~/*.txt`.
- Every public member has an English XML doc comment. `dotnet build` reports `0 Warning(s)`; the Unity log has no `warning CS` lines for `Bun3.BadWords*`.
- Shared tests (`Tests/Editor`) compile against NUnit 3 (Unity) and NUnit 4.1 (dotnet): use only `Assert.That(..., Is.*)`, `Assert.Throws<T>`, `Assert.Pass`, `Assert.Ignore`, `[Test]`, `[TestCase]`. Tests that need .NET Unicode data (`string.Normalize`) live in the dotnet-only project folder `common/tests/Bun3.BadWords.Tests/`.
- Comments only for constraints or contracts the code cannot show; no narration comments.
- Data licenses: MIT, Unicode License V3 and CC-BY-4.0 only. Excluded: CC-BY-SA (Wiktionary, frequency lists), NC, ND, GPL, unlicensed lists, LEv145/mat-words.
- **Strong expressions only** (spec Decision 4). **Owner ruling 2026-10-14: mild words pass** — shit-level words and milder (`mierda`, `merda`, `merde`, `Scheiße`/`scheisse`, `kacke`, `culo`, `bastardo`, `クソ`, `バカ`, `アホ`, `笨蛋`, `白痴`) are never entries. The spec's `scheiße` example illustrates the `ß` mechanism only; `scheiße` is not a default entry.
- Cross-language everyday words are never entries, compared in normalized form (`con`, `bitte`, `nicht`, `del`, `mama`, `negro`, `comer`, `fica`, `ano`).
- Downloaded sources are untrusted data: keep them in their own scratchpad folder, keep scripts in a different folder, run Python with `-I`. Frequency corpora are never committed.
- `.meta` files come only from Unity (Task 7). `Data~` gets no `.meta`.
- Never stage unrelated changes: `unity/ProjectSettings/ProjectSettings.asset`, `unity/Assets/Plugins/Dissonance/Resources/.WelcomeState.json`. Unity runs rewrite `unity/Packages/packages-lock.json` with unrelated drift — revert it with `git checkout -- unity/Packages/packages-lock.json`.
- Known pre-existing failure, not ours: `Bun3.Server.Tests` `Uncontended_single_owner_has_no_per_iteration_allocation` (from d8cf10f). Run only the badwords tests.
- Commits: follow the `writing-gitmoji-commits` skill (gitmoji title + Korean Why/Changes/Impact/Results body, one `-m` per paragraph). Trailer: `Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>`.
- Commands used throughout (run from the worktree root in Git Bash):
  - `dotnet test common/tests/Bun3.BadWords.Tests` (all badwords tests)
  - `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj -c Release` (warning check)
  - Regenerate both generated files: `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`
  - `<scratchpad>` below means the session scratchpad directory, never the repo.

## Review Focus

1. **Russian everyday inflections that contain a mat root** (`колебание` via `ебан`, `застрахуйте` via `хуй`, `оскорблять` via `бля`) — Russian players' ordinary words must pass; pinned in Task 6's `PassesEverydayWordsInOtherScripts` and the root allows of Task 6 Step 4.
2. **Input whose gated stream normalizes to nothing** (only a Cyrillic combining mark `\u0483`, only kana marks `\u3099\u309A`, only `ー・`) — must return `false` without touching an empty span incorrectly; pinned in Task 2 and Task 3 `PassesOrdinaryText`.
3. **Supplementary-plane Han inside a CJK word** (`傻\uD840\uDC00逼`) — surrogates are dropped like separators, so it must still block; pinned in Task 3 `BlocksEvasionForms`.
4. **Other-language roots inside English nicknames** (`brochure`, `certificate`, `difficulty`, `Monaco`, `Moses`, `computer`) — must pass; pinned in Task 5 `PassesEverydayWordsInLatinScriptLanguages`.
5. **Latin-only text spelled entirely with Cyrillic look-alikes** (`cop` against a Cyrillic entry `сор`) — the Cyrillic stream must not run without real Cyrillic; pinned in Task 2 `CyrillicStreamRunsOnlyOnCyrillicInput`.

---

### Task 1: Scan cost baseline and sharp s

**Files:**
- Modify: `common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs` (add `ReportsScanCost`)
- Modify: `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs:15-30` (`latin1` table, `Clean`)
- Test: `common/src/com.bun3.badwords/Tests/Editor/TextNormalizerTests.cs`, `common/src/com.bun3.badwords/Tests/Editor/BadWordFilterTests.cs`

**Interfaces:**
- Produces: `ReportsScanCost` (an `Assert.Pass` report used again in Task 7). `TextNormalizer.Clean('\u00DF')` and `Clean('\u1E9E')` return `'s'`.

- [ ] **Step 1: Add the scan cost report**

Add to `DefaultDictionaryTests.cs` (add `using System.Text;`):

```csharp
        [Test]
        public void ReportsScanCost()
        {
            const int iterations = 100000;
            string[] inputs = { "Player One", "탈출친구", "f.u.c.k you", "привет как дела", "こんにちは今日はいい天気", "你好世界朋友们" };
            var report = new StringBuilder();
            foreach (string input in inputs)
            {
                for (int i = 0; i < 1000; i++) BadWordFilter.Default.ContainsBadWord(input);
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < iterations; i++) BadWordFilter.Default.ContainsBadWord(input);
                watch.Stop();
                report.Append(input).Append('=').Append((watch.Elapsed.TotalMilliseconds * 1000000 / iterations).ToString("F0")).Append("ns ");
            }
            Assert.Pass(report.ToString());
        }
```

- [ ] **Step 2: Record the 0.1.0 baseline**

Run three times:
`dotnet test common/tests/Bun3.BadWords.Tests -c Release --filter ReportsScanCost --logger "console;verbosity=detailed"`

Expected: PASS with a line such as `Player One=…ns 탈출친구=…ns …`. Write the per-input median of the three runs to `<scratchpad>/scan-cost-baseline.txt` and to the SDD ledger. Task 7 compares against it.

- [ ] **Step 3: Commit the report**

```bash
git add common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs
git commit -m "✅ 기본 필터 입력별 스캔 비용 보고 테스트 추가" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 4: Write the failing sharp s tests**

In `TextNormalizerTests.CleansCharacters` add:

```csharp
        [TestCase('\u00DF', 's')]
        [TestCase('\u1E9E', 's')]
```

In `TextNormalizerTests.LatinStreamNormalizesLettersAndBoundaries` add:

```csharp
        [TestCase("Stra\u00DFe", "strase")]
        [TestCase("STRA\u1E9EE", "strase")]
```

In `BadWordFilterTests`, replace the `[TestCase("stra\u00DFe")]` line of `MalformedEntryThrows` with `[TestCase("\u00FEorn")]` (thorn still has no ASCII base), and add:

```csharp
        [Test]
        public void SharpSMatchesBothSpellings()
        {
            var german = new BadWordFilter(new[] { "stra\u00DFe" });
            Assert.That(german.ContainsBadWord("Stra\u00DFe"), Is.True);
            Assert.That(german.ContainsBadWord("strasse"), Is.True);
            Assert.That(german.ContainsBadWord("STRA\u1E9EE"), Is.True);
        }
```

- [ ] **Step 5: Run the tests to verify they fail**

Run: `dotnet test common/tests/Bun3.BadWords.Tests --filter "FullyQualifiedName~TextNormalizerTests|FullyQualifiedName~SharpSMatchesBothSpellings"`
Expected: FAIL — `Clean('\u00DF')` returns `'\uFFFF'`; the filter constructor throws `ArgumentException` for `stra\u00DFe`.

- [ ] **Step 6: Fold sharp s**

In `TextNormalizer.cs` change index 31 of `latin1` from `?` to `s`:

```csharp
        // U+00C0..U+00FF folded to lowercase ASCII; '?' marks letters without an ASCII base.
        const string latin1 = "aaaaaa?ceeeeiiii?nooooo?ouuuuy?saaaaaa?ceeeeiiii?nooooo?ouuuuy?y";
```

and in `Clean`, after the fullwidth line:

```csharp
            if (c == '\u1E9E') return 's';
```

`strasse` matches through the collapsed scan (`strase` has no doubled letter), so no length expansion is needed.

- [ ] **Step 7: Run all badwords tests**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: all PASS except `SweepCorpus` (Ignored).

- [ ] **Step 8: Commit**

```bash
git add common/src/com.bun3.badwords/Runtime/TextNormalizer.cs common/src/com.bun3.badwords/Tests/Editor/TextNormalizerTests.cs common/src/com.bun3.badwords/Tests/Editor/BadWordFilterTests.cs
git commit -m "🌐 독일어 ß·ẞ를 s로 정규화" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Entry stages and the Cyrillic stream

**Files:**
- Modify: `common/src/com.bun3.badwords/Runtime/BadWordFilter.cs` (whole file below)
- Modify: `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs` (add `IsCyrillic`, `HasCyrillic`, `Cyrillic`, `CyrillicLetter`)
- Test: `common/src/com.bun3.badwords/Tests/Editor/TextNormalizerTests.cs`, `BadWordFilterTests.cs`, `BadWordFilterPerformanceTests.cs`

**Interfaces:**
- Consumes: `TextNormalizer.Clean`, `Lookalike`, `Latin`, `Collapse`, `HasDoubledLetter`, `JoinSingleLetters` (private, same class), `PatternSet`, `AhoCorasick.TryFindBlocked`.
- Produces:
  - `internal static bool TextNormalizer.IsCyrillic(char c)` — U+0400..U+04FF.
  - `internal static bool TextNormalizer.HasCyrillic(ReadOnlySpan<char> text)`.
  - `internal static char TextNormalizer.CyrillicLetter(char c)` — stream D's per-character fold: a lowercase Cyrillic letter, `TextNormalizer.Removed`, or `' '` (boundary).
  - `internal static int TextNormalizer.Cyrillic(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)`.
  - In `BadWordFilter`: `enum Stage { Latin, Korean, Cyrillic }`, `static Stage Parse(string entry, out string key, out PatternFlags flags)`, `static void AddCollapsible(string key, PatternFlags flags, PatternSet plain, PatternSet collapsed)`, fields `latin`, `latinCollapsed`, `cyrillic`, `cyrillicCollapsed` (renamed from `english`, `englishCollapsed`), `StreamKind.Cyrillic` and `StreamKind.CyrillicCollapsed`. Task 3 extends `Stage`, `Parse`, `Find` and the constructor.

- [ ] **Step 1: Write the failing normalizer tests**

Add to `TextNormalizerTests.cs`:

```csharp
        [TestCase("ХУЙ", "хуи")]
        [TestCase("Ёж", "еж")]
        [TestCase("x\u0443\u0439", "хуи")]
        [TestCase("X\u0423\u0419", "хуи")]
        [TestCase("\uFF58\u0443\u0439", "хуи")]
        [TestCase("\u0445\u0443\u0438\u0306", "хуи")]
        [TestCase("aceopxy", "\u0430\u0441\u0435\u043E\u0440\u0445\u0443")]
        [TestCase("ACEOPXY", "\u0430\u0441\u0435\u043E\u0440\u0445\u0443")]
        [TestCase("BHKMT", "\u0432\u043D\u043A\u043C\u0442")]
        [TestCase("bhkmt", "")]
        [TestCase("х у й", "хуи")]
        [TestCase("  где   батарея! ", "где батарея")]
        [TestCase("хуй123", "хуи")]
        [TestCase("시발хуй", "хуи")]
        public void CyrillicStreamFoldsCaseAndLookalikes(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Cyrillic, text), Is.EqualTo(expected));

        [TestCase("hello", false)]
        [TestCase("cop", false)]
        [TestCase("시발", false)]
        [TestCase("x\u0443\u0439", true)]
        [TestCase("\u0483", true)]
        public void DetectsCyrillic(string text, bool expected) =>
            Assert.That(TextNormalizer.HasCyrillic(text), Is.EqualTo(expected));
```

- [ ] **Step 2: Write the failing filter tests**

In `BadWordFilterTests.cs`, extend the shared filter's entries with `"хуй", "|бля", "пизд", "ебат", "!застрахуй",`.

Add to `BlocksEvasionForms`:

```csharp
        [TestCase("хуй")]
        [TestCase("ХУЙ")]
        [TestCase("x\u0443\u0439")]
        [TestCase("X\u0423\u0419")]
        [TestCase("х у й")]
        [TestCase("хууууй")]
        [TestCase("\u0445\u0443\u0438\u0306")]
        [TestCase("иди нахуй")]
        [TestCase("бля")]
        [TestCase("ну бля")]
        [TestCase("пиздец")]
```

Add to `PassesOrdinaryText`:

```csharp
        [TestCase("рубля")]
        [TestCase("застрахуйте")]
        [TestCase("где батарея")]
        [TestCase("тебе")]
        [TestCase("хлеб")]
        [TestCase("\u0483")]
```

Add to `ExplainNamesTheStreamAndNormalizedSpan`:

```csharp
            Assert.That(filter.Explain("x\u0443\u0439"), Is.EqualTo("Cyrillic:хуи"));
            Assert.That(filter.Explain("хууууй"), Is.EqualTo("CyrillicCollapsed:хуи"));
```

In `MalformedEntryThrows`, delete `[TestCase("\u0445\u0443\u0439")]` (now a valid Cyrillic entry) and add:

```csharp
        [TestCase("\uC2DC\u0445")]
        [TestCase("\u0445\u0443\u04391")]
        [TestCase("\u0445-\u0443\u0439")]
        [TestCase("\u0483")]
        [TestCase("fuck\uD83D\uDE00")]
```

`fu\u0441k` stays: Latin `f`, `u`, `k` have no Cyrillic look-alike, so the entry mixes Cyrillic with other characters.

Add:

```csharp
        [Test]
        public void CyrillicStreamRunsOnlyOnCyrillicInput()
        {
            // Every letter of this Cyrillic entry has a Latin look-alike; the middle one is written in Latin.
            var lookalikes = new BadWordFilter(new[] { "\u0441o\u0440" });
            Assert.That(lookalikes.ContainsBadWord("\u0441\u043E\u0440"), Is.True);
            Assert.That(lookalikes.ContainsBadWord("co\u0440"), Is.True);
            Assert.That(lookalikes.ContainsBadWord("cop"), Is.False);
            Assert.That(lookalikes.ContainsBadWord("COP"), Is.False);
        }
```

- [ ] **Step 3: Extend the allocation and concurrency tests**

Replace `BadWordFilterPerformanceTests.cs` with (keep the existing `#if` block structure):

```csharp
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
#if UNITY_5_3_OR_NEWER
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
#endif

namespace Bun3.BadWords.Tests
{
    public class BadWordFilterPerformanceTests
    {
        static readonly BadWordFilter filter = new BadWordFilter(new[] { "시발", "!시발점", "ㅅㅂ", "fuck", "|ass|", "хуй" });

        [Test]
        public void ContainsBadWordDoesNotAllocate()
        {
            string[] blocked = WithLongForms("시1발 f.u.c.k 시발점 tlqkf class", "x\u0443\u0439 привет");
            // Clean texts must not match so every scan, the gated ones included, runs; the asserts also warm up the code paths.
            string[] clean = WithLongForms("hello world class Player One 안녕하세요", "привет как дела хлеб");
            foreach (string text in blocked) Assert.That(filter.ContainsBadWord(text), Is.True, text);
            foreach (string text in clean) Assert.That(filter.ContainsBadWord(text), Is.False, text);
            // Unity's Mono ArrayPool drops pooled arrays on every GC; long inputs must not depend on a pool surviving one.
            GC.Collect();
#if UNITY_5_3_OR_NEWER
            Assert.That(() =>
            {
                foreach (string text in blocked) filter.ContainsBadWord(text);
                foreach (string text in clean) filter.ContainsBadWord(text);
            }, Is.Not.AllocatingGCMemory());
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                foreach (string text in blocked) filter.ContainsBadWord(text);
                foreach (string text in clean) filter.ContainsBadWord(text);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
#endif
        }

        [Test]
        public void ConcurrentCallsMatchSequentialResults()
        {
            string[] inputs = { "시발", "시발점", "fuck you", "class", "tq", "ㅅㅂ", "hello", new string('x', 400) + "시발", "x\u0443\u0439", "привет" };
            var expected = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) expected[i] = filter.ContainsBadWord(inputs[i]);
            var actual = new bool[4000];
            Parallel.For(0, actual.Length, i => actual[i] = filter.ContainsBadWord(inputs[i % inputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i % inputs.Length]));

            string[] defaultInputs = { "시발점", "Player", "fuck", "tlqkf", "hello", "хуй" };
            var defaultExpected = new bool[defaultInputs.Length];
            for (int i = 0; i < defaultInputs.Length; i++) defaultExpected[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i]);
            Parallel.For(0, actual.Length, i => actual[i] = BadWordFilter.Default.ContainsBadWord(defaultInputs[i % defaultInputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(defaultExpected[i % defaultInputs.Length]));
        }

        // Each text and a repetition over 256 characters, so both the stack and the per-thread buffer paths run.
        static string[] WithLongForms(params string[] texts)
        {
            var result = new List<string>();
            foreach (string text in texts)
            {
                string longText = text;
                while (longText.Length <= 300) longText += " " + text;
                result.Add(text);
                result.Add(longText);
            }
            return result.ToArray();
        }
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAILS — `TextNormalizer.Cyrillic` and `HasCyrillic` do not exist.

- [ ] **Step 5: Add stream D to `TextNormalizer`**

Add to `TextNormalizer.cs` (next to `Latin`):

```csharp
        internal static int Cyrillic(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char letter = CyrillicLetter(raw);
                if (letter == Removed) continue;
                if (letter != ' ') work[count++] = letter;
                else if (count > 0 && work[count - 1] != ' ') work[count++] = ' ';
            }
            if (count > 0 && work[count - 1] == ' ') count--;
            return JoinSingleLetters(work.Slice(0, count), destination);
        }

        internal static bool IsCyrillic(char c) => c >= '\u0400' && c <= '\u04FF';

        internal static bool HasCyrillic(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
                if (IsCyrillic(c)) return true;
            return false;
        }

        // Stream D folds the other way from Lookalike: Latin letters drawn like Cyrillic ones become Cyrillic.
        internal static char CyrillicLetter(char c)
        {
            if (c >= '\uFF01' && c <= '\uFF5E') c = (char)(c - 0xFEE0);
            if (IsCyrillic(c) && char.IsLetter(c))
            {
                char lower = char.ToLowerInvariant(c);
                if (lower == '\u0451') return '\u0435';
                return lower == '\u0439' ? '\u0438' : lower;
            }
            char lookalike = c switch
            {
                'a' or 'A' => '\u0430',
                'c' or 'C' => '\u0441',
                'e' or 'E' => '\u0435',
                'o' or 'O' => '\u043E',
                'p' or 'P' => '\u0440',
                'x' or 'X' => '\u0445',
                'y' or 'Y' => '\u0443',
                'B' => '\u0432',
                'H' => '\u043D',
                'K' => '\u043A',
                'M' => '\u043C',
                'T' => '\u0442',
                _ => Removed,
            };
            if (lookalike != Removed) return lookalike;
            return Clean(c) == Removed ? Removed : ' ';
        }
```

`Clean` already removes format characters, combining marks (decomposed `й` and `ё` lose their marks) and Hangul fillers, so the last line reuses it for removal only.

- [ ] **Step 6: Replace `BadWordFilter.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>
    /// Detects bad words in short user text such as nicknames and chat lines.
    /// Immutable after construction and safe to share across threads.
    /// </summary>
    /// <remarks>
    /// Partial: <c>BadWordFilter.cs</c> (API, entry parsing, scanning) and the generated <c>BadWordFilter.Default.cs</c> (default entries).
    /// </remarks>
    public sealed partial class BadWordFilter
    {
        const int StackLimit = 256;
        const PatternFlags Boundaries = PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd;

        // Unity's Mono ArrayPool drops pooled arrays on every GC, so long inputs reuse a per-thread buffer instead.
        [ThreadStatic] static char[] scratch;

        readonly AhoCorasick korean;
        readonly AhoCorasick latin;
        readonly AhoCorasick latinCollapsed;
        readonly AhoCorasick cyrillic;
        readonly AhoCorasick cyrillicCollapsed;

        /// <summary>The filter built from <see cref="DefaultEntries"/> on first access. Touch it during boot to prewarm.</summary>
        public static BadWordFilter Default => DefaultFilter.Instance;

        /// <summary>The bundled Korean and English entries, for composing consumer-specific filters.</summary>
        public static IReadOnlyList<string> DefaultEntries => DefaultEntryList.Instance;

        /// <summary>Builds a filter from entries in the form <c>[!][|]word[|]</c>.</summary>
        /// <param name="entries">
        /// Words to block. A leading <c>!</c> marks an allow entry that suppresses block matches it fully covers.
        /// A leading or trailing <c>|</c> requires a word boundary there (Latin and Cyrillic entries only).
        /// Entries containing Hangul are Korean, entries containing Cyrillic are Cyrillic, and the rest are Latin.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        /// <exception cref="ArgumentException">
        /// An entry is null, has no letters, has letters of an unsupported script or a character outside the Basic Multilingual Plane,
        /// mixes scripts, mixes a script with characters its stream would drop, or puts a boundary on a Korean entry.
        /// </exception>
        public BadWordFilter(IEnumerable<string> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var koreanSet = new PatternSet();
            var latinSet = new PatternSet();
            var latinCollapsedSet = new PatternSet();
            var cyrillicSet = new PatternSet();
            var cyrillicCollapsedSet = new PatternSet();
            foreach (string entry in entries)
            {
                switch (Parse(entry, out string key, out PatternFlags flags))
                {
                    case Stage.Korean:
                        koreanSet.Add(key, flags);
                        break;
                    case Stage.Cyrillic:
                        AddCollapsible(key, flags, cyrillicSet, cyrillicCollapsedSet);
                        break;
                    default:
                        AddCollapsible(key, flags, latinSet, latinCollapsedSet);
                        break;
                }
            }
            korean = koreanSet.Build();
            latin = latinSet.Build();
            latinCollapsed = latinCollapsedSet.Build();
            cyrillic = cyrillicSet.Build();
            cyrillicCollapsed = cyrillicCollapsedSet.Build();
        }

        enum Stage
        {
            Latin,
            Korean,
            Cyrillic,
        }

        enum StreamKind
        {
            None,
            Hangul,
            Keyboard,
            Latin,
            Collapsed,
            Cyrillic,
            CyrillicCollapsed,
        }

        /// <summary>Returns true when <paramref name="text"/> contains a blocked word that no allow entry covers.</summary>
        /// <remarks>
        /// Allocates no managed memory once warmed up. Inputs over 256 characters use a per-thread buffer that grows to twice
        /// the longest input seen on that thread and is kept, so the first longer input on each thread allocates.
        /// </remarks>
        public bool ContainsBadWord(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            Span<char> buffer = text.Length <= StackLimit ? stackalloc char[StackLimit * 2] : Scratch(text.Length * 2);
            return Find(text, buffer.Slice(0, text.Length), buffer.Slice(text.Length, text.Length), out _) != StreamKind.None;
        }

        static char[] Scratch(int length)
        {
            char[] buffer = scratch;
            if (buffer == null || buffer.Length < length) scratch = buffer = new char[length];
            return buffer;
        }

        // Diagnostics for dictionary curation; allocates.
        internal string Explain(ReadOnlySpan<char> text)
        {
            var work = new char[text.Length];
            var stream = new char[text.Length];
            StreamKind kind = Find(text, work, stream, out Match match);
            if (kind == StreamKind.None) return null;
            char[] source = kind == StreamKind.Collapsed || kind == StreamKind.CyrillicCollapsed ? work : stream;
            return kind + ":" + new string(source, match.Start, match.End - match.Start);
        }

        StreamKind Find(ReadOnlySpan<char> text, Span<char> work, Span<char> stream, out Match match)
        {
            int hangulLength = TextNormalizer.Hangul(text, work, stream);
            if (korean.TryFindBlocked(stream.Slice(0, hangulLength), false, out match)) return StreamKind.Hangul;
            // Stream B drops Hangul: a one-letter stream is not readable as Korean, and short patterns only count for Hangul-free input.
            int length = TextNormalizer.Keyboard(text, work, stream);
            if (length > 1 && korean.TryFindBlocked(stream.Slice(0, length), true, out match, hangulLength > 0)) return StreamKind.Keyboard;
            length = TextNormalizer.Latin(text, work, stream);
            if (length > 0)
            {
                if (latin.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Latin;
                length = TextNormalizer.Collapse(stream.Slice(0, length), work);
                if (latinCollapsed.TryFindBlocked(work.Slice(0, length), false, out match)) return StreamKind.Collapsed;
            }
            if (TextNormalizer.HasCyrillic(text))
            {
                length = TextNormalizer.Cyrillic(text, work, stream);
                if (cyrillic.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Cyrillic;
                length = TextNormalizer.Collapse(stream.Slice(0, length), work);
                if (cyrillicCollapsed.TryFindBlocked(work.Slice(0, length), false, out match)) return StreamKind.CyrillicCollapsed;
            }
            match = default;
            return StreamKind.None;
        }

        // Classified by raw characters: look-alike folding would turn a Cyrillic entry into a Latin one.
        static Stage Parse(string entry, out string key, out PatternFlags flags)
        {
            if (entry == null) throw new ArgumentException("Entries must not contain null.", "entries");
            string body = entry.Trim();
            flags = PatternFlags.None;
            if (body.StartsWith("!", StringComparison.Ordinal))
            {
                flags |= PatternFlags.Allow;
                body = body.Substring(1);
            }
            if (body.StartsWith("|", StringComparison.Ordinal))
            {
                flags |= PatternFlags.BoundaryStart;
                body = body.Substring(1);
            }
            if (body.EndsWith("|", StringComparison.Ordinal))
            {
                flags |= PatternFlags.BoundaryEnd;
                body = body.Substring(0, body.Length - 1);
            }

            bool hangul = false, cyrillic = false, latinLetter = false, other = false;
            foreach (char c in body)
            {
                if (char.IsSurrogate(c)) throw Malformed(entry, "has a character outside the Basic Multilingual Plane");
                if (TextNormalizer.IsCyrillic(c))
                {
                    cyrillic = true;
                    continue;
                }
                char clean = TextNormalizer.Clean(c);
                if (clean == TextNormalizer.Removed || char.IsWhiteSpace(clean)) continue;
                if (TextNormalizer.IsHangul(clean)) hangul = true;
                // Greek look-alikes would otherwise register as Latin letters.
                else if (TextNormalizer.IsLatinLetter(clean) && TextNormalizer.Lookalike(c) == TextNormalizer.Removed) latinLetter = true;
                else if (char.IsLetter(c)) throw Malformed(entry, "has letters of an unsupported script");
                else other = true;
            }
            if (hangul && cyrillic) throw Malformed(entry, "mixes scripts");

            var work = new char[body.Length];
            var normalized = new char[body.Length];
            if (hangul)
            {
                // Stream A drops non-Hangul characters, so a Korean entry with digits or symbols would silently shrink.
                if (latinLetter || other) throw Malformed(entry, "mixes Hangul with other characters");
                if ((flags & Boundaries) != 0) throw Malformed(entry, "uses a word boundary on a Korean entry");
                int length = TextNormalizer.Hangul(body, work, normalized);
                key = new string(normalized, 0, length);
                if (length < 2 || TextNormalizer.ContainsJamo(key)) flags |= PatternFlags.Short;
                return Stage.Korean;
            }
            if (cyrillic)
            {
                // Stream D turns every other character into a boundary, which would silently split the entry.
                foreach (char c in body)
                    if (TextNormalizer.CyrillicLetter(c) == ' ' && !char.IsWhiteSpace(c)) throw Malformed(entry, "mixes Cyrillic with other characters");
                key = new string(normalized, 0, TextNormalizer.Cyrillic(body, work, normalized));
                if (key.Length == 0) throw Malformed(entry, "has no letters");
                return Stage.Cyrillic;
            }
            if (!latinLetter) throw Malformed(entry, "has no letters");
            key = new string(normalized, 0, TextNormalizer.Latin(body, work, normalized));
            return Stage.Latin;
        }

        static void AddCollapsible(string key, PatternFlags flags, PatternSet plain, PatternSet collapsed)
        {
            plain.Add(key, flags);
            if ((flags & PatternFlags.Allow) != 0)
            {
                var work = new char[key.Length];
                collapsed.Add(new string(work, 0, TextNormalizer.Collapse(key, work)), flags);
            }
            else if (!TextNormalizer.HasDoubledLetter(key))
            {
                collapsed.Add(key, flags);
            }
        }

        static ArgumentException Malformed(string entry, string reason) =>
            new ArgumentException("Entry '" + entry + "' " + reason + ".", "entries");

        static class DefaultEntryList
        {
            internal static readonly IReadOnlyList<string> Instance = Array.AsReadOnly(CreateDefaultEntries());
        }

        static class DefaultFilter
        {
            internal static readonly BadWordFilter Instance = new BadWordFilter(DefaultEntryList.Instance);
        }
    }
}
```

The class summary and `DefaultEntries` doc are finalized in Task 7.

- [ ] **Step 7: Run all badwords tests**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: all PASS except `SweepCorpus` (Ignored), including `GeneratedDefaultIsUpToDate` (the default data has no Cyrillic entries yet).

Run: `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj -c Release`
Expected: `0 Warning(s)`.

- [ ] **Step 8: Commit**

```bash
git add common/src/com.bun3.badwords/Runtime/BadWordFilter.cs common/src/com.bun3.badwords/Runtime/TextNormalizer.cs common/src/com.bun3.badwords/Tests/Editor
git commit -m "🌐 키릴 문자 스트림과 문자 체계별 항목 분류 추가" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 3: The kana/Han stream

**Files:**
- Modify: `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs` (add `IsCjk`, `HasCjk`, `Kana`, `Cjk` and helpers)
- Modify: `common/src/com.bun3.badwords/Runtime/BadWordFilter.cs` (stage, constructor, `Find`, docs)
- Create: `common/tests/Bun3.BadWords.Tests/UnicodeTableTests.cs` (dotnet-only)
- Test: `TextNormalizerTests.cs`, `BadWordFilterTests.cs`, `BadWordFilterPerformanceTests.cs`

**Interfaces:**
- Consumes: Task 2's `Stage`, `Parse`, `Find`, constructor.
- Produces:
  - `internal static bool TextNormalizer.IsCjk(char c)` — kana U+3041..U+30FF and U+FF66..U+FF9F, `々` U+3005, Han U+3400..U+4DBF, U+4E00..U+9FFF, U+F900..U+FAFF.
  - `internal static bool TextNormalizer.HasCjk(ReadOnlySpan<char> text)`.
  - `internal static char TextNormalizer.Kana(char c)` — hiragana U+3041..U+3096 to katakana (+0x60), halfwidth katakana U+FF66..U+FF9D to fullwidth; any other character unchanged.
  - `internal static int TextNormalizer.Cjk(ReadOnlySpan<char> text, Span<char> destination)`.
  - `Stage.Cjk`, `StreamKind.Cjk`, field `cjk`. Task 4 makes `Cjk` map Han through `Simplified`.

- [ ] **Step 1: Write the failing normalizer tests**

Add to `TextNormalizerTests.cs` (Han in these cases has no Simplified variant, so Task 4 does not change them):

```csharp
        [TestCase("ちんこ", "チンコ")]
        [TestCase("\uFF81\uFF9D\uFF7A", "チンコ")]
        [TestCase("ち ん こ", "チンコ")]
        [TestCase("ちーんこ", "チンコ")]
        [TestCase("チ・ン・コ", "チンコ")]
        [TestCase("ち\u200Bんこ", "チンコ")]
        [TestCase("か\u3099", "ガ")]
        [TestCase("か\u309B", "ガ")]
        [TestCase("\uFF76\uFF9E", "ガ")]
        [TestCase("\uFF8A\uFF9F", "パ")]
        [TestCase("う\u3099", "ヴ")]
        [TestCase("ア\u3099", "ア")]
        [TestCase("\u3099ア", "ア")]
        [TestCase("AV女子", "女子")]
        [TestCase("人々", "人々")]
        [TestCase("傻\uD840\uDC00逼", "傻逼")]
        [TestCase("hello", "")]
        [TestCase("시발", "")]
        public void CjkStreamFoldsKanaAndDropsSeparators(string text, string expected) =>
            Assert.That(RunCjk(text), Is.EqualTo(expected));

        [TestCase("hello", false)]
        [TestCase("시발", false)]
        [TestCase("хуй", false)]
        [TestCase("ちんこ", true)]
        [TestCase("\uFF81", true)]
        [TestCase("中", true)]
        public void DetectsKanaAndHan(string text, bool expected) =>
            Assert.That(TextNormalizer.HasCjk(text), Is.EqualTo(expected));

        static string RunCjk(string text)
        {
            var destination = new char[text.Length];
            return new string(destination, 0, TextNormalizer.Cjk(text, destination));
        }
```

Create `common/tests/Bun3.BadWords.Tests/UnicodeTableTests.cs` (dotnet-only: Unity's runtime is not trusted for `string.Normalize`, and the tables do not depend on the runtime):

```csharp
using System.Globalization;
using System.Text;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    // Checks the hand-written kana tables against .NET's Unicode data.
    public class UnicodeTableTests
    {
        [Test]
        public void HalfwidthKatakanaFoldsLikeCompatibilityDecomposition()
        {
            for (char c = '\uFF66'; c <= '\uFF9D'; c++)
                Assert.That(TextNormalizer.Kana(c), Is.EqualTo(c.ToString().Normalize(NormalizationForm.FormKC)[0]), ((int)c).ToString("X4", CultureInfo.InvariantCulture));
        }

        [Test]
        public void VoicedMarksComposeLikeCanonicalComposition()
        {
            foreach (char c in "カキクケコサシスセソタチツテトハヒフヘホウ")
                Assert.That(Cjk(c + "\u3099"), Is.EqualTo((c + "\u3099").Normalize(NormalizationForm.FormC)), c.ToString());
            foreach (char c in "ハヒフヘホ")
                Assert.That(Cjk(c + "\u309A"), Is.EqualTo((c + "\u309A").Normalize(NormalizationForm.FormC)), c.ToString());
        }

        static string Cjk(string text)
        {
            var destination = new char[text.Length];
            return new string(destination, 0, TextNormalizer.Cjk(text, destination));
        }
    }
}
```

- [ ] **Step 2: Write the failing filter tests**

In `BadWordFilterTests.cs`, extend the shared filter's entries with `"ちんこ", "フェラ", "!フェラーリ", "傻逼",`.

Add to `BlocksEvasionForms`:

```csharp
        [TestCase("ちんこ")]
        [TestCase("チンコ")]
        [TestCase("\uFF81\uFF9D\uFF7A")]
        [TestCase("ち ん こ")]
        [TestCase("ちーんこ")]
        [TestCase("お前のちんこ")]
        [TestCase("Player ちんこ")]
        [TestCase("フェラ")]
        [TestCase("傻逼")]
        [TestCase("你是傻逼吗")]
        [TestCase("傻\uD840\uDC00逼")]
```

Add to `PassesOrdinaryText`:

```csharp
        [TestCase("フェラーリ")]
        [TestCase("こんにちは")]
        [TestCase("中文")]
        [TestCase("\u3099\u309A")]
        [TestCase("\u30FC\u30FB")]
```

Add to `ExplainNamesTheStreamAndNormalizedSpan`:

```csharp
            Assert.That(filter.Explain("\uFF81\uFF9D\uFF7A"), Is.EqualTo("Cjk:チンコ"));
```

In `MalformedEntryThrows`, delete `[TestCase("\u4E2D\u56FD")]` (now a valid two-Han entry) and add:

```csharp
        [TestCase("\u6027")]
        [TestCase("\u3061")]
        [TestCase("\u30FC\u30FB")]
        [TestCase("\u5356B")]
        [TestCase("\u5356\u0445")]
        [TestCase("\u3061\u3093\u3053\uAC00")]
        [TestCase("|\u3061\u3093\u3053")]
        [TestCase("\u3061\u3093\u3053\uD840\uDC00")]
```

Add:

```csharp
        [Test]
        public void TwoHanEntryMatchesInsideText() =>
            Assert.That(new BadWordFilter(new[] { "\u4E2D\u56FD" }).ContainsBadWord("\u4E2D\u56FD\u4EBA"), Is.True);
```

In `BadWordFilterPerformanceTests.cs`: add `"ちんこ"` to the filter entries; change the two arrays to

```csharp
            string[] blocked = WithLongForms("시1발 f.u.c.k 시발점 tlqkf class", "x\u0443\u0439 привет", "\uFF81\uFF9D\uFF7A 你好");
            // Clean texts must not match so every scan, the gated ones included, runs; the asserts also warm up the code paths.
            string[] clean = WithLongForms("hello world class Player One 안녕하세요", "привет как дела хлеб", "こんにちは今日はいい天気 奶茶");
```

and append `"\uFF81\uFF9D\uFF7A", "こんにちは"` to `inputs` and `"ちんこ"` to `defaultInputs` in `ConcurrentCallsMatchSequentialResults`.

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAILS — `TextNormalizer.Cjk`, `HasCjk` and `Kana` do not exist.

- [ ] **Step 4: Add stream E to `TextNormalizer`**

Add the constants next to `keyboard`:

```csharp
        // U+FF66..U+FF9D halfwidth katakana to fullwidth, from their Unicode <narrow> decompositions.
        const string halfwidthKatakana =
            "\u30F2\u30A1\u30A3\u30A5\u30A7\u30A9\u30E3\u30E5\u30E7\u30C3\u30FC\u30A2\u30A4\u30A6\u30A8\u30AA" +
            "\u30AB\u30AD\u30AF\u30B1\u30B3\u30B5\u30B7\u30B9\u30BB\u30BD\u30BF\u30C1\u30C4\u30C6\u30C8\u30CA" +
            "\u30CB\u30CC\u30CD\u30CE\u30CF\u30D2\u30D5\u30D8\u30DB\u30DE\u30DF\u30E0\u30E1\u30E2\u30E4\u30E6" +
            "\u30E8\u30E9\u30EA\u30EB\u30EC\u30ED\u30EF\u30F3";

        // Katakana that a voiced mark turns into the next code point, and the subset a semi-voiced mark turns into the one after.
        const string voiceable = "\u30AB\u30AD\u30AF\u30B1\u30B3\u30B5\u30B7\u30B9\u30BB\u30BD\u30BF\u30C1\u30C4\u30C6\u30C8\u30CF\u30D2\u30D5\u30D8\u30DB";
        const string semiVoiceable = "\u30CF\u30D2\u30D5\u30D8\u30DB";
```

Add the stream:

```csharp
        // Neither Japanese nor Chinese separates words with spaces, so separators are dropped rather than kept as boundaries.
        internal static int Cjk(ReadOnlySpan<char> text, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Kana(raw);
                if (c == '\u3099' || c == '\u309B' || c == '\uFF9E')
                {
                    if (count > 0) destination[count - 1] = Voiced(destination[count - 1]);
                }
                else if (c == '\u309A' || c == '\u309C' || c == '\uFF9F')
                {
                    if (count > 0 && semiVoiceable.IndexOf(destination[count - 1]) >= 0) destination[count - 1] = (char)(destination[count - 1] + 2);
                }
                else if ((c >= '\u30A1' && c <= '\u30FA') || c == '\u3005' || IsHan(c))
                {
                    destination[count++] = c;
                }
            }
            return count;
        }

        internal static char Kana(char c)
        {
            if (c >= '\u3041' && c <= '\u3096') return (char)(c + 0x60);
            if (c >= '\uFF66' && c <= '\uFF9D') return halfwidthKatakana[c - '\uFF66'];
            return c;
        }

        internal static bool IsCjk(char c) =>
            (c >= '\u3041' && c <= '\u30FF') || (c >= '\uFF66' && c <= '\uFF9F') || c == '\u3005' || IsHan(c);

        internal static bool HasCjk(ReadOnlySpan<char> text)
        {
            foreach (char c in text)
                if (IsCjk(c)) return true;
            return false;
        }

        static bool IsHan(char c) =>
            (c >= '\u3400' && c <= '\u4DBF') || (c >= '\u4E00' && c <= '\u9FFF') || (c >= '\uF900' && c <= '\uFAFF');

        static char Voiced(char c)
        {
            if (c == '\u30A6') return '\u30F4';
            return voiceable.IndexOf(c) >= 0 ? (char)(c + 1) : c;
        }
```

A mark with nothing to combine with is dropped, as are `ー` (U+30FC), `・` (U+30FB), iteration marks, digits, Latin, Hangul and surrogates.

- [ ] **Step 5: Add the kana/Han stage to `BadWordFilter`**

1. `enum Stage` gains `Cjk`; `enum StreamKind` gains `Cjk` after `CyrillicCollapsed`.
2. Field `readonly AhoCorasick cjk;`; in the constructor `var cjkSet = new PatternSet();`, a `case Stage.Cjk: cjkSet.Add(key, flags); break;` and `cjk = cjkSet.Build();`.
3. In `Find`, before `match = default;`:

```csharp
            if (TextNormalizer.HasCjk(text))
            {
                length = TextNormalizer.Cjk(text, stream);
                if (cjk.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Cjk;
            }
```

4. In `Parse`, declare `cjk = false` with the other flags; in the loop, right after the Cyrillic check:

```csharp
                if (TextNormalizer.IsCjk(c))
                {
                    cjk = true;
                    continue;
                }
```

replace `if (hangul && cyrillic) throw …` with

```csharp
            if ((hangul ? 1 : 0) + (cyrillic ? 1 : 0) + (cjk ? 1 : 0) > 1) throw Malformed(entry, "mixes scripts");
```

and insert before `if (!latinLetter) …`:

```csharp
            if (cjk)
            {
                // Stream E drops every other character, which would silently shrink the entry.
                if (latinLetter || other) throw Malformed(entry, "mixes kana or Han with other characters");
                if ((flags & Boundaries) != 0) throw Malformed(entry, "uses a word boundary on a kana or Han entry");
                int length = TextNormalizer.Cjk(body, normalized);
                // A single kana or Han character sits inside too many ordinary words.
                if (length < 2) throw Malformed(entry, "has fewer than two kana or Han characters");
                key = new string(normalized, 0, length);
                return Stage.Cjk;
            }
```

5. Constructor docs: the boundary sentence becomes "(Latin and Cyrillic entries only)" (unchanged) and the stage sentence becomes "Entries containing Hangul are Korean, entries containing Cyrillic are Cyrillic, entries containing kana or Han are kana/Han, and the rest are Latin."; the exception text gains "or is a kana or Han entry shorter than two characters" and "puts a boundary on a Korean or kana/Han entry".

- [ ] **Step 6: Run all badwords tests**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` and `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj -c Release`
Expected: all PASS except `SweepCorpus` (Ignored); `0 Warning(s)`. If `UnicodeTableTests` fails, fix the table in `TextNormalizer.cs`, not the test.

- [ ] **Step 7: Commit**

```bash
git add common/src/com.bun3.badwords/Runtime common/src/com.bun3.badwords/Tests/Editor common/tests/Bun3.BadWords.Tests/UnicodeTableTests.cs
git commit -m "🌐 일본어 가나·한자 스트림 추가" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Traditional to Simplified table

**Files:**
- Create: `common/src/com.bun3.badwords/Data~/zh-simplified.txt`
- Create (generated): `common/src/com.bun3.badwords/Runtime/TextNormalizer.Simplified.cs`
- Modify: `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs` (`partial`, class docs, `Simplified`, `Cjk`)
- Modify: `common/tests/Bun3.BadWords.Tests/DefaultEntriesGenerator.cs` (render and check both generated files)
- Test: `TextNormalizerTests.cs`, `BadWordFilterTests.cs`

**Interfaces:**
- Consumes: Task 3's `Cjk`.
- Produces: `internal static char TextNormalizer.Simplified(char c)` (unchanged when there is no pair); `DefaultEntriesGenerator.ReadSimplifiedPairs()` and `RenderSimplified(...)`; `GeneratedDefaultIsUpToDate` now covers both generated files.

- [ ] **Step 1: Extract the pairs**

```bash
SRC="<scratchpad>/badwords-sources-020"; mkdir -p "$SRC"
curl -sL -o "$SRC/Unihan.zip" https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip
```

Write `<scratchpad>/badwords-scripts/extract_simplified.py`:

```python
"""Extract BMP Traditional->Simplified pairs from Unihan. Usage: python -I extract_simplified.py <Unihan.zip> <out>"""
import pathlib, sys, zipfile


def main(source, out):
    with zipfile.ZipFile(source) as archive:
        text = archive.read("Unihan_Variants.txt").decode("utf-8")
    pairs = {}
    for line in text.splitlines():
        fields = line.split("\t")
        if len(fields) != 3 or fields[1] != "kSimplifiedVariant":
            continue
        traditional = int(fields[0][2:], 16)
        simplified = int(fields[2].split()[0][2:], 16)
        if traditional <= 0xFFFF and simplified <= 0xFFFF and traditional != simplified:
            pairs[traditional] = simplified
    header = [
        "# Traditional to Simplified Chinese pairs for com.bun3.badwords stream E.",
        "# Source: Unicode 17.0.0 Unihan_Variants.txt kSimplifiedVariant (Unicode License V3) https://www.unicode.org/Public/17.0.0/ucd/Unihan.zip",
        "# Kept: BMP pairs, first listed target only, self-mappings dropped. Format: traditional<TAB>simplified.",
    ]
    lines = header + [f"{chr(t)}\t{chr(s)}" for t, s in sorted(pairs.items())]
    pathlib.Path(out).write_text("\n".join(lines) + "\n", encoding="utf-8")
    print(f"pairs={len(pairs)}")


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
```

Run: `PYTHONIOENCODING=utf-8 python -I "<scratchpad>/badwords-scripts/extract_simplified.py" "$SRC/Unihan.zip" common/src/com.bun3.badwords/Data~/zh-simplified.txt`
Expected: `pairs=` a few thousand. Spot-check that the file contains `媽	妈` and `雞	鸡`.

The generated file must exist before any test references `Simplified`, because the generator runs inside the same test project. Steps 2–4 keep that order.

- [ ] **Step 2: Render and check both generated files**

In `DefaultEntriesGenerator.cs` replace `GeneratedDefaultIsUpToDate` with:

```csharp
        [Test]
        public void GeneratedDefaultIsUpToDate()
        {
            List<string> entries = ReadDataEntries();
            var filter = new BadWordFilter(entries);
            foreach (string entry in entries)
                if (!entry.StartsWith("!", StringComparison.Ordinal))
                    Assert.That(filter.ContainsBadWord(entry.Trim('|')), Is.True, "Dead block entry: " + entry);
            string runtime = Path.Combine(PackageRoot(), "Runtime");
            var generated = new Dictionary<string, string>
            {
                [Path.Combine(runtime, "BadWordFilter.Default.cs")] = Render(entries),
                [Path.Combine(runtime, "TextNormalizer.Simplified.cs")] = RenderSimplified(ReadSimplifiedPairs()),
            };
            if (Environment.GetEnvironmentVariable(regenerateVariable) == "1")
            {
                foreach (var file in generated) File.WriteAllText(file.Key, file.Value, new UTF8Encoding(false));
                Assert.Pass("Regenerated " + entries.Count + " entries.");
            }
            foreach (var file in generated)
                Assert.That(File.ReadAllText(file.Key).Replace("\r\n", "\n"), Is.EqualTo(file.Value),
                    "Run with " + regenerateVariable + "=1 to regenerate " + Path.GetFileName(file.Key) + ".");
        }
```

and add:

```csharp
        const string simplifiedFile = "zh-simplified.txt";

        internal static List<KeyValuePair<char, char>> ReadSimplifiedPairs()
        {
            var pairs = new SortedDictionary<char, char>();
            foreach (string line in ReadEntries(Path.Combine(PackageRoot(), "Data~", simplifiedFile)))
            {
                string[] parts = line.Split('\t');
                Assert.That(parts.Length == 2 && parts[0].Length == 1 && parts[1].Length == 1, Is.True, "Malformed pair: " + line);
                pairs.Add(parts[0][0], parts[1][0]);
            }
            return pairs.ToList();
        }

        internal static string RenderSimplified(IReadOnlyList<KeyValuePair<char, char>> pairs)
        {
            var builder = new StringBuilder();
            builder.Append("// <auto-generated/>\n");
            builder.Append("// Traditional to Simplified Chinese pairs generated from Data~/" + simplifiedFile + ", sorted by the Traditional character. Regenerate: BUN3_BADWORDS_REGEN=1 dotnet test --filter GeneratedDefaultIsUpToDate\n");
            builder.Append("namespace Bun3.BadWords\n{\n    internal static partial class TextNormalizer\n    {\n");
            AppendCharConstant(builder, "traditional", pairs.Select(pair => pair.Key).ToList());
            AppendCharConstant(builder, "simplified", pairs.Select(pair => pair.Value).ToList());
            builder.Append("    }\n}\n");
            return builder.ToString();
        }

        static void AppendCharConstant(StringBuilder builder, string name, List<char> chars)
        {
            builder.Append("        const string ").Append(name).Append(" =");
            if (chars.Count == 0) builder.Append(" \"\"");
            for (int i = 0; i < chars.Count; i += 16)
            {
                builder.Append(i == 0 ? "\n            \"" : " +\n            \"");
                for (int k = i; k < Math.Min(i + 16, chars.Count); k++)
                    builder.Append("\\u").Append(((int)chars[k]).ToString("X4", CultureInfo.InvariantCulture));
                builder.Append('"');
            }
            builder.Append(";\n");
        }
```

`SortedDictionary.Add` throws on a duplicate Traditional character, and sorting keeps the binary search valid.

In `TextNormalizer.cs` declare `internal static partial class TextNormalizer` and add to the class docs `<remarks>Partial: <c>TextNormalizer.cs</c> (streams) and the generated <c>TextNormalizer.Simplified.cs</c> (Traditional to Simplified pairs).</remarks>`.

Run: `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`, then `dotnet test common/tests/Bun3.BadWords.Tests`.
Expected: the first run creates `Runtime/TextNormalizer.Simplified.cs`; the second passes everything except `SweepCorpus` (the constants are not used yet; private constants produce no warning).

- [ ] **Step 3: Write the failing tests**

Add to `TextNormalizerTests.cs`:

```csharp
        [TestCase('媽', '妈')]
        [TestCase('雞', '鸡')]
        [TestCase('歡', '欢')]
        [TestCase('中', '中')]
        [TestCase('A', 'A')]
        public void SimplifiedMapsTraditionalHan(char input, char expected) =>
            Assert.That(TextNormalizer.Simplified(input), Is.EqualTo(expected));
```

and to `CjkStreamFoldsKanaAndDropsSeparators`: `[TestCase("他媽的", "他妈的")]`, `[TestCase("操你媽", "操你妈")]`.

In `BadWordFilterTests.cs`, add `"鸡巴",` to the shared filter entries and `[TestCase("雞巴")]`, `[TestCase("鸡巴")]` to `BlocksEvasionForms`.

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAILS — `TextNormalizer.Simplified` does not exist.

- [ ] **Step 4: Add the lookup**

Add to `TextNormalizer.cs`:

```csharp
        internal static char Simplified(char c)
        {
            int low = 0, high = traditional.Length - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                char key = traditional[middle];
                if (key == c) return simplified[middle];
                if (key < c) low = middle + 1;
                else high = middle - 1;
            }
            return c;
        }
```

and in `Cjk` split the kept branch:

```csharp
                else if ((c >= '\u30A1' && c <= '\u30FA') || c == '\u3005')
                {
                    destination[count++] = c;
                }
                else if (IsHan(c))
                {
                    destination[count++] = Simplified(c);
                }
```

- [ ] **Step 5: Run everything**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` and `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj -c Release`.
Expected: all PASS except `SweepCorpus`; `0 Warning(s)`. If a `SimplifiedMapsTraditionalHan` case fails, check that pair in `zh-simplified.txt` against Unihan before touching the code. The new file has no `.meta` until Task 7.

- [ ] **Step 6: Commit**

```bash
git add common/src/com.bun3.badwords/Data~/zh-simplified.txt common/src/com.bun3.badwords/Runtime common/src/com.bun3.badwords/Tests/Editor common/tests/Bun3.BadWords.Tests/DefaultEntriesGenerator.cs
git commit -m "🌐 번체 한자를 간체로 맞추는 Unihan 표 생성" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

### Task 5: Latin-script language dictionaries

**Files:**
- Modify: `common/tests/Bun3.BadWords.Tests/DefaultEntriesGenerator.cs` (`ReadDataEntries`, `Render` header)
- Create: `common/src/com.bun3.badwords/Data~/es.txt`, `pt.txt`, `fr.txt`, `it.txt`, `de.txt`, `nl.txt`, `tl.txt`
- Modify (generated): `common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs`

**Interfaces:**
- Consumes: `Parse` stages, `SweepCorpus`, `Explain`.
- Produces: one committed data file per Latin-script language; `ReadDataEntries()` reads every `Data~/*.txt` except `zh-simplified.txt`, in ordinal file-name order.

- [ ] **Step 1: Read every language file**

In `DefaultEntriesGenerator.cs`:

```csharp
        internal static List<string> ReadDataEntries() =>
            Directory.GetFiles(Path.Combine(PackageRoot(), "Data~"), "*.txt")
                .Where(path => Path.GetFileName(path) != simplifiedFile)
                .OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal)
                .SelectMany(ReadEntries)
                .ToList();
```

and change the `Render` header line to `"// Default dictionary entries generated from the language files in Data~. Regenerate: BUN3_BADWORDS_REGEN=1 dotnet test --filter GeneratedDefaultIsUpToDate\n"`.

Run: regenerate, then `dotnet test common/tests/Bun3.BadWords.Tests`. Expected: all PASS (same entries; `en` now precedes `ko`).

Commit: `git commit -m "♻️ 기본 사전 생성기가 Data~의 언어 파일을 모두 읽도록 변경" …` (generator + regenerated file).

- [ ] **Step 2: Write the failing smoke tests**

Add to `DefaultDictionaryTests.cs`:

```csharp
        [TestCase("hijo de puta")]
        [TestCase("pendejo")]
        [TestCase("gilipollas")]
        [TestCase("chinga tu madre")]
        [TestCase("caralho")]
        [TestCase("filho da puta")]
        [TestCase("arrombado")]
        [TestCase("vai se foder")]
        [TestCase("salope")]
        [TestCase("connard")]
        [TestCase("enculé")]
        [TestCase("nique ta mère")]
        [TestCase("vaffanculo")]
        [TestCase("puttana")]
        [TestCase("coglione")]
        [TestCase("figlio di puttana")]
        [TestCase("Hurensohn")]
        [TestCase("Fotze")]
        [TestCase("Wichser")]
        [TestCase("fick dich")]
        [TestCase("klootzak")]
        [TestCase("kutwijf")]
        [TestCase("putang ina")]
        [TestCase("tangina")]
        [TestCase("kantot")]
        public void BlocksStrongWordsInLatinScriptLanguages(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.True, text);

        [TestCase("con")]
        [TestCase("del")]
        [TestCase("mama")]
        [TestCase("año")]
        [TestCase("negro")]
        [TestCase("comer")]
        [TestCase("bitte")]
        [TestCase("nicht")]
        [TestCase("Straße")]
        [TestCase("computadora")]
        [TestCase("disputa")]
        [TestCase("economía")]
        [TestCase("economy")]
        [TestCase("computation")]
        [TestCase("computer")]
        [TestCase("député")]
        [TestCase("réputation")]
        [TestCase("cuidado")]
        [TestCase("fica")]
        [TestCase("anno")]
        [TestCase("ano")]
        [TestCase("bobo")]
        [TestCase("tanga")]
        [TestCase("salamat")]
        [TestCase("bedankt")]
        [TestCase("lekker")]
        [TestCase("certificate")]
        [TestCase("difficulty")]
        [TestCase("brochure")]
        [TestCase("Monaco")]
        [TestCase("Moses")]
        [TestCase("alcohol")]
        [TestCase("connect")]
        public void PassesEverydayWordsInLatinScriptLanguages(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));

        // Owner ruling (2026-10-14): mild words pass in every language.
        [TestCase("mierda")]
        [TestCase("merda")]
        [TestCase("merde")]
        [TestCase("Scheiße")]
        [TestCase("scheisse")]
        [TestCase("kacke")]
        [TestCase("culo")]
        [TestCase("bastardo")]
        [TestCase("idiota")]
        public void PassesMildWords(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));
```

Run: `dotnet test common/tests/Bun3.BadWords.Tests --filter "FullyQualifiedName~DefaultDictionaryTests"`
Expected: `BlocksStrongWordsInLatinScriptLanguages` FAILS for every case; the pass tests PASS.

- [ ] **Step 3: Download sources and frequency corpora to scratch**

```bash
SRC="<scratchpad>/badwords-sources-020"; mkdir -p "$SRC"
L=https://raw.githubusercontent.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words/5faf2ba42d7b1c0977169ec3611df25a3c08eb13
C=https://raw.githubusercontent.com/wooorm/cuss/6bab3fef250481e34ba55bc400fac5c6d25f1429
F=https://raw.githubusercontent.com/hermitdave/FrequencyWords/525f9b560de45753a5ea01069454e72e9aa541c6/content/2018
for l in es pt fr it de nl fil ru zh ja; do curl -sL -o "$SRC/ldnoobw-$l.txt" "$L/$l"; done
for l in es pt pt-pt fr it; do curl -sL -o "$SRC/cuss-$l.js" "$C/$l.js"; done
for l in en ko es pt pt_br fr it de nl ru zh_cn zh_tw; do curl -sL "$F/$l/${l}_50k.txt" | head -n 20000 | cut -d' ' -f1 > "$SRC/freq-$l.txt"; done
for l in ja tl; do curl -sL "$F/$l/${l}_full.txt" | head -n 20000 | cut -d' ' -f1 > "$SRC/freq-$l.txt"; done
curl -sL -o "$SRC/en-common.txt" https://raw.githubusercontent.com/first20hours/google-10000-english/master/google-10000-english-usa-no-swears.txt
curl -sL https://raw.githubusercontent.com/e9t/nsmc/master/ratings.txt | cut -f2 | tail -n +2 > "$SRC/nsmc-lines.txt"
wc -l "$SRC"/*
```

Expected line counts: `ldnoobw-` es 68, pt 76, fr 91, it 168, de 66, nl 190, fil 14, ru 151, zh 319, ja 180; `cuss-` es 657, pt 180, pt-pt 51, fr 744, it 1185; each `freq-*.txt` 20,000; `nsmc-lines.txt` ≈ 200,000. FrequencyWords is CC-BY-SA: it stays in scratch.

- [ ] **Step 4: Extract candidates with a scratch-only script**

Write `<scratchpad>/badwords-scripts/extract_latin.py`:

```python
"""Extract com.bun3.badwords 0.2.0 Latin-stage candidates. Usage: python -I extract_latin.py <sources> <data> <review.tsv>"""
import pathlib, re, sys, unicodedata

LDNOOBW = "LDNOOBW (CC-BY-4.0, modified) https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words @ 5faf2ba42d7b1c0977169ec3611df25a3c08eb13"
CUSS = "cuss (MIT) https://github.com/wooorm/cuss @ 6bab3fef250481e34ba55bc400fac5c6d25f1429"
GRAMMAR = "# Grammar: one entry per line; '#' starts a comment; '!' allow; '|' word boundary (Latin and Cyrillic). See README.md."
CURATION = "# Curation: strong expressions only; mild words pass (owner ruling 2026-10-14). Record: docs/research/2026-10-13-bad-word-multilingual-sources.md."
LANGUAGES = {
    # code: (name, [(cuss file, keep phrases)], [LDNOOBW files])
    "es": ("Spanish", [("cuss-es.js", True)], ["ldnoobw-es.txt"]),
    "pt": ("Portuguese", [("cuss-pt.js", True), ("cuss-pt-pt.js", True)], ["ldnoobw-pt.txt"]),
    "fr": ("French", [("cuss-fr.js", False)], ["ldnoobw-fr.txt"]),
    "it": ("Italian", [("cuss-it.js", True)], ["ldnoobw-it.txt"]),
    "de": ("German", [], ["ldnoobw-de.txt"]),
    "nl": ("Dutch", [], ["ldnoobw-nl.txt"]),
    "tl": ("Tagalog", [], ["ldnoobw-fil.txt"]),
}
FREQUENCY = ["en", "es", "pt", "pt_br", "fr", "it", "de", "nl", "tl"]
CUSS_LINE = re.compile(r"""^\s*(?:'([^']*)'|"([^"]*)"|([^\s:'"]+)):\s*(\d)""")
LETTERS = re.compile(r"[a-z\u00df\u00e0-\u00f6\u00f8-\u00ff' -]+")
REWRITE = {"\u0153": "oe", "\u00e6": "ae"}
# Spec rule 3 (cross-language everyday words) and the owner ruling of 2026-10-14 (mild words pass), compared after folding.
DROP = {
    "con", "bitte", "nicht", "del", "mama", "negro", "negre", "comer", "fica", "ano", "peter", "hol", "pot", "balle", "mona",
    "regina", "pis", "nazi", "saco", "burro", "bobo", "tanga", "gol", "sex", "sexy", "sexo", "porno", "penis", "pene", "anal",
    "viagra", "cul", "cu", "pau", "peru", "macaco", "vaca", "galinha", "gat", "paal", "poot", "pik", "aso", "muts", "reet",
    "shit", "nackt", "desnudo", "nu", "nue", "orgasmo", "orgasmus", "vagina",
    "mierda", "merda", "merde", "scheise", "scheisse", "kacke", "kacken", "stront", "caca", "pipi", "culo", "bastardo",
    "bastarda", "batard", "idiota", "idiot", "imbecil", "estupido", "stupido", "tonto",
}


def fold(text):
    text = text.lower().replace("\u00df", "s")
    return "".join(c for c in unicodedata.normalize("NFKD", text) if not unicodedata.combining(c))


def clean(raw):
    entry = " ".join(raw.strip().lower().split())
    for source, target in REWRITE.items():
        entry = entry.replace(source, target)
    return entry if LETTERS.fullmatch(entry) else None


def cuss(path, phrases):
    for line in path.read_text(encoding="utf-8").splitlines():
        match = CUSS_LINE.match(line)
        if match and match.group(4) == "2":
            word = next(group for group in match.groups()[:3] if group is not None)
            if phrases or " " not in word.strip():
                yield word


def ranks(sources):
    table = {}
    for code in FREQUENCY:
        for rank, line in enumerate((sources / f"freq-{code}.txt").read_text(encoding="utf-8").splitlines(), 1):
            table.setdefault(fold(line.strip()), []).append(f"{code}:{rank}")
    return table


def main(sources, data, review):
    rank_table, rows, skipped = ranks(sources), [], []
    for code, (name, cuss_files, ldnoobw_files) in LANGUAGES.items():
        groups = [(f"cuss {file[5:-3]} (rating 2)", list(cuss(sources / file, phrases))) for file, phrases in cuss_files]
        groups += [(f"LDNOOBW {file[8:-4]}", (sources / file).read_text(encoding="utf-8").splitlines()) for file in ldnoobw_files]
        seen, lines = set(), [f"# com.bun3.badwords default {name} entries.", GRAMMAR,
                              "# Sources: " + "; ".join(([CUSS] if cuss_files else []) + [LDNOOBW]), CURATION]
        for title, words in groups:
            lines += ["", f"# == {title} =="]
            for raw in words:
                entry = clean(raw) if raw.strip() else None
                if entry is None or fold(entry) in DROP:
                    if raw.strip():
                        skipped.append(f"{code}\t{raw.strip()}")
                    continue
                if entry in seen:
                    continue
                seen.add(entry)
                lines.append(entry)
                rows.append(f"{code}\t{entry}\t{title}\t{' '.join(rank_table.get(fold(entry), []))}")
        (data / f"{code}.txt").write_text("\n".join(lines) + "\n", encoding="utf-8")
    review.write_text("language\tentry\tsource\tfrequency ranks\n" + "\n".join(rows) + "\n", encoding="utf-8")
    print(f"candidates={len(rows)} skipped={len(skipped)}")
    print("\n".join(skipped))


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]), pathlib.Path(sys.argv[3]))
```

The script takes cuss rating 2 only, for every language: the spec names the rating for es and it, and pt rating 1 is mostly ordinary words (`burro`, `pau`, `peru`, `vaca`). French cuss phrases are dropped (spec); other phrases stay until Step 5.

Run: `PYTHONIOENCODING=utf-8 python -I "<scratchpad>/badwords-scripts/extract_latin.py" "$SRC" common/src/com.bun3.badwords/Data~ "<scratchpad>/review-latin.tsv" > "<scratchpad>/extract-latin.log"`
Expected: first log line `candidates=<n> skipped=<k>` with n ≈ 2,000; the rest lists dropped and malformed lines.

- [ ] **Step 5: Curate the data files by hand**

Edit each new `Data~/<code>.txt`, deleting candidate lines, following the spec's curation rules:

1. **Keep** explicit sexual terms, slurs and harsh insults only.
2. **Drop** ordinary words and mild insults (most of cuss rating 2 is mild: `abanto`, `adefesio`, `alcornoque`, `boniato`, `brocha`), anatomy and clinical terms, drug, crime and political terms (`asesinato`, `drogas`, `heroína`, `racista`), Dutch idioms (`aardappels afgieten`, `buiten de pot piesen`, `gras maaien`), and phrases already covered by a kept root.
3. Use `review-latin.tsv`: an entry with a rank in a frequency list is an everyday word in that language unless it is itself profanity there (subtitles contain swearing). Never keep an everyday word of any target language.
4. **Short roots inside common words** get a leading boundary (`|puta`, `|puto`, `|pute`, `|cono`) or are dropped. Collapse inflection runs to one root (`arrapa`, `arrapamenti`, `arrapare`, … → one entry).
5. Smoke-list words of Step 2 missing from the sources go under `# == project additions (owner-reviewed smoke list, 2026-10-14) ==` at the end of their language file.

Expected scale after curation: tens of entries per language, not hundreds. Then regenerate: an `ArgumentException` names a malformed entry — fix or delete that line and rerun.

- [ ] **Step 6: Sweep the Latin corpora**

```bash
OUT="<scratchpad>/sweeps-020"; mkdir -p "$OUT"
for l in en es pt pt_br fr it de nl tl; do
  BUN3_BADWORDS_SWEEP="$SRC/freq-$l.txt" BUN3_BADWORDS_SWEEP_OUT="$OUT/sweep-$l.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
done
BUN3_BADWORDS_SWEEP="$SRC/en-common.txt" BUN3_BADWORDS_SWEEP_OUT="$OUT/sweep-en-common.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
```

Record the first run's hit counts per corpus (`wc -l` and the summed first column) as "before". `SweepCorpus` reads `Data~` directly, so iterations need no regeneration. Review every reason: it stays only when the matched word is a strong expression in some target language; otherwise resolve it by deleting the entry, adding a `|` boundary, or adding `!<word>` under `# == false-positive sweep allows (FrequencyWords <code>) ==`. Rerun until every remaining hit is a reviewed strong expression, and record the "after" counts and the kept hits.

- [ ] **Step 7: Regenerate and run everything**

Run: regenerate, then `dotnet test common/tests/Bun3.BadWords.Tests`.
Expected: all PASS except `SweepCorpus` (Ignored). A smoke case that still fails gets the shortest fix allowed by the rules (an entry for a block case; a boundary or `!` entry for a pass case), then regenerate and rerun.

- [ ] **Step 8: Commit**

```bash
git add common/src/com.bun3.badwords/Data~ common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs
git commit -m "🍱 스페인어·포르투갈어·프랑스어·이탈리아어·독일어·네덜란드어·타갈로그어 기본 사전 추가" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

The commit body records counts per file and the sweep before/after numbers; Task 7 copies them into the research record.

---

### Task 6: Russian, Japanese and Chinese dictionaries, full sweep, owner check

**Files:**
- Create: `common/src/com.bun3.badwords/Data~/ru.txt`, `ja.txt`, `zh.txt`
- Modify (generated): `common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs`

**Interfaces:**
- Consumes: Tasks 2–5 (stages D and E, Simplified table, generator, sources in `$SRC`).
- Produces: the complete 0.2.0 default dictionary.

- [ ] **Step 1: Write the failing smoke tests**

Add to `DefaultDictionaryTests.cs`:

```csharp
        [TestCase("хуй")]
        [TestCase("x\u0443\u0439")]
        [TestCase("х у й")]
        [TestCase("пизда")]
        [TestCase("блядь")]
        [TestCase("иди нахуй")]
        [TestCase("pizda")]
        [TestCase("ちんこ")]
        [TestCase("チンコ")]
        [TestCase("\uFF81\uFF9D\uFF7A")]
        [TestCase("ち ん こ")]
        [TestCase("まんこ")]
        [TestCase("操你妈")]
        [TestCase("操你媽")]
        [TestCase("傻逼")]
        [TestCase("他媽的")]
        public void BlocksStrongWordsInOtherScripts(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.True, text);

        [TestCase("тебе")]
        [TestCase("хлеб")]
        [TestCase("рубля")]
        [TestCase("где батарея")]
        [TestCase("застрахуйте")]
        [TestCase("употреблять")]
        [TestCase("оскорблять")]
        [TestCase("колебание")]
        [TestCase("сукно")]
        [TestCase("привет")]
        [TestCase("男性")]
        [TestCase("奶茶")]
        [TestCase("操作")]
        [TestCase("体操")]
        [TestCase("妈妈")]
        [TestCase("媽媽")]
        [TestCase("鸡肉")]
        [TestCase("日本")]
        [TestCase("フェラーリ")]
        [TestCase("カフェラテ")]
        [TestCase("ちんちん電車")]
        [TestCase("ガチンコ")]
        [TestCase("こんにちは")]
        public void PassesEverydayWordsInOtherScripts(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));
```

and add `[TestCase("クソ")]`, `[TestCase("バカ")]`, `[TestCase("アホ")]`, `[TestCase("笨蛋")]`, `[TestCase("白痴")]` to `PassesMildWords`.

Run the `DefaultDictionaryTests`. Expected: `BlocksStrongWordsInOtherScripts` FAILS for every case.

- [ ] **Step 2: Download the Japanese sources**

```bash
M=https://raw.githubusercontent.com/MosasoM/inappropriate-words-ja/e24de6e185135f92c3491ef61931c26905bd3b1f
curl -sL -o "$SRC/mosasom-sexual.txt" "$M/Sexual.txt"
curl -sL -o "$SRC/mosasom-offensive.txt" "$M/Offensive.txt"
curl -sL -o "$SRC/citrine-ngwords.txt" https://raw.githubusercontent.com/EbiseLutica/Citrine/e6a37316bc8f3009febe0e0db77d9e29872e2d4d/Resources/ngwords.txt
```

Expected: 281, 49 and 197 lines.

- [ ] **Step 3: Extract candidates with a scratch-only script**

Write `<scratchpad>/badwords-scripts/extract_other_scripts.py`:

```python
"""Extract com.bun3.badwords 0.2.0 Russian, Japanese and Chinese candidates. Usage: python -I extract_other_scripts.py <sources> <data>"""
import pathlib, re, sys

LDNOOBW = "LDNOOBW (CC-BY-4.0, modified) https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words @ 5faf2ba42d7b1c0977169ec3611df25a3c08eb13"
MOSASOM = "MosasoM/inappropriate-words-ja (MIT) https://github.com/MosasoM/inappropriate-words-ja @ e24de6e185135f92c3491ef61931c26905bd3b1f"
CITRINE = "Citrine (MIT) https://github.com/EbiseLutica/Citrine @ e6a37316bc8f3009febe0e0db77d9e29872e2d4d"
GRAMMAR = "# Grammar: one entry per line; '#' starts a comment; '!' allow; '|' word boundary (Latin and Cyrillic). See README.md."
CURATION = "# Curation: strong expressions only; mild words pass (owner ruling 2026-10-14). Record: docs/research/2026-10-13-bad-word-multilingual-sources.md."
CYRILLIC = re.compile(r"[\u0400-\u04ff]")
CYRILLIC_ENTRY = re.compile(r"[\u0400-\u04ffaceopxy ]+")
LATIN_ENTRY = re.compile(r"[a-z' ]+")
CJK_ENTRY = re.compile(r"[\u3041-\u30ff\uff66-\uff9f\u3005\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff]+")
SEPARATORS = re.compile(r"[\u30fb\u30fc\uff70\u3099-\u309c\uff9e\uff9f]")
# Spec rule 2: ordinary or mild words, drugs, crime and politics; owner ruling 2026-10-14: mild words pass.
DROP = {
    "ru": {"голый", "какая разница", "секс", "мент", "фига", "хрен", "хапать", "гол", "gol", "seks"},
    "ja": {"イク", "ナンパ", "カス", "クソ", "ハゲ", "バカ", "アホ", "デブ", "ブス", "ボケ", "エロ", "ハーレム", "愛人", "催眠", "体位", "座位",
           "ホ別", "リフレ", "ネトウヨ", "パヨク", "非国民"},
    "zh": {"成人", "激情", "私服", "迷药", "老二", "月经", "情色", "笨蛋", "白痴", "放荡", "色情"},
}


def katakana(text):
    return "".join(chr(ord(c) + 0x60) if "\u3041" <= c <= "\u3096" else c for c in text)


def lines(path):
    return [line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip() and not line.startswith("#")]


def cjk(entry):
    return CJK_ENTRY.fullmatch(entry) is not None and len(SEPARATORS.sub("", entry)) >= 2


def write(path, name, sources, sections):
    out = [f"# com.bun3.badwords default {name} entries.", GRAMMAR, "# Sources: " + "; ".join(sources), CURATION]
    seen = set()
    for title, entries in sections:
        out += ["", f"# == {title} =="]
        for entry in entries:
            if entry not in seen:
                seen.add(entry)
                out.append(entry)
    path.write_text("\n".join(out) + "\n", encoding="utf-8")
    return len(seen)


def main(sources, data):
    skipped = []
    cyrillic, latin = [], []
    for raw in lines(sources / "ldnoobw-ru.txt"):
        entry = " ".join(raw.lower().replace("-", " ").split())
        if entry in DROP["ru"]:
            skipped.append(f"ru\t{raw}")
        elif CYRILLIC.search(entry):
            if CYRILLIC_ENTRY.fullmatch(entry):
                cyrillic.append(entry)
            else:
                skipped.append(f"ru\t{raw}")
        elif LATIN_ENTRY.fullmatch(entry):
            latin.append(entry)
        else:
            skipped.append(f"ru\t{raw}")
    ru = write(data / "ru.txt", "Russian", [LDNOOBW], [("LDNOOBW ru (Cyrillic)", cyrillic), ("LDNOOBW ru (Latin transliterations)", latin)])

    ja_sections = []
    for title, file in [("MosasoM Sexual", "mosasom-sexual.txt"), ("MosasoM Offensive", "mosasom-offensive.txt")]:
        kept = []
        for raw in lines(sources / file):
            if not cjk(raw) or katakana(raw) in DROP["ja"] or raw in DROP["ja"]:
                skipped.append(f"ja\t{raw}")
            else:
                kept.append(raw)
        ja_sections.append((title, kept))
    allows = ["!" + raw[1:] for raw in lines(sources / "citrine-ngwords.txt") if raw.startswith("-") and cjk(raw[1:])]
    ja_sections.append(("Citrine exceptions", allows))
    ja = write(data / "ja.txt", "Japanese", [MOSASOM, CITRINE], ja_sections)

    zh_kept = []
    for raw in lines(sources / "ldnoobw-zh.txt"):
        if not cjk(raw) or raw in DROP["zh"]:
            skipped.append(f"zh\t{raw}")
        else:
            zh_kept.append(raw)
    zh = write(data / "zh.txt", "Chinese", [LDNOOBW], [("LDNOOBW zh", zh_kept)])
    print(f"ru={ru} ja={ja} zh={zh} skipped={len(skipped)}")
    print("\n".join(skipped))


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
```

Run: `PYTHONIOENCODING=utf-8 python -I "<scratchpad>/badwords-scripts/extract_other_scripts.py" "$SRC" common/src/com.bun3.badwords/Data~ > "<scratchpad>/extract-other.log"`
Expected: first log line `ru=<n> ja=<m> zh=<k> skipped=<s>`; the skipped list includes single-Han entries, Latin-only Japanese entries (`3P`, `SM`, `SOD`, `NTR`, `SEX`), mixed entries (`AV女優`, `卖B`, `13.`), and the drop lists.

- [ ] **Step 4: Curate and add the Russian roots**

Apply Task 5 Step 5's rules to the three files. In addition:

- **ru:** append the hand-written roots and their first allows (roots must be long enough to pass the sweep; never `еб`):

```
# == project additions (mat roots) ==
хуй
хуе
хуя
пизд
ебат
ебан
ебуч
ебло
бляд
|бля
мудак
мудил
залуп
пидор
пидар
шлюх
гандон
|сука
!колебан
!колебат
!страхуй
!страхуе
!страхуя
```

  Drop LDNOOBW Cyrillic entries now covered by a root.
- **ja:** drop political slurs and ordinary words still left (`女優` alone is an ordinary word); halfwidth entries such as `ﾁﾝﾁﾝ` stay (stream E folds them).
- **zh:** drop entries rejected by the engine (single Han, outside the BMP, mixed with Latin or digits) and weak or ordinary words; keep Cantonese entries as written. Rewrite a mixed entry into Han only when that form is a strong term not already present.

Regenerate; fix or delete any entry an `ArgumentException` names.

- [ ] **Step 5: Sweep every corpus**

```bash
for l in en ko es pt pt_br fr it de nl tl ru ja zh_cn zh_tw; do
  BUN3_BADWORDS_SWEEP="$SRC/freq-$l.txt" BUN3_BADWORDS_SWEEP_OUT="$OUT/sweep-$l.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
done
BUN3_BADWORDS_SWEEP="$SRC/en-common.txt" BUN3_BADWORDS_SWEEP_OUT="$OUT/sweep-en-common.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
BUN3_BADWORDS_SWEEP="$SRC/nsmc-lines.txt" BUN3_BADWORDS_SWEEP_OUT="$OUT/sweep-nsmc.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
```

Review exactly as in Task 5 Step 6 (allow entries for kana/Han false positives, as `!フェラーリ`). NSMC contains real Korean profanity: compare its reasons with the 0.1.0 report and review only new reasons. Record before/after counts for every corpus.

- [ ] **Step 6: Regenerate, run everything, commit**

Run: regenerate, `dotnet test common/tests/Bun3.BadWords.Tests`, the Release build.
Expected: all PASS except `SweepCorpus`; `0 Warning(s)`.

```bash
git add common/src/com.bun3.badwords/Data~ common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs
git commit -m "🍱 러시아어·일본어·중국어 기본 사전 추가" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

- [ ] **Step 7: Owner spot-check (human gate — stop and ask)**

Show the owner, per language, the entry count and about ten kept words that are arguable (borderline strength, words with a frequency rank, hand-written roots), each with a short Korean gloss. Apply their removals and additions, regenerate, rerun the tests, and commit separately (for example `🔥 소유자 검토에서 허용한 단어를 기본 사전에서 제거`). Do not continue to Task 7 before the owner answers.

---

### Task 7: Release 0.2.0 — docs, notices, costs, Unity

**Files:**
- Modify: `common/src/com.bun3.badwords/package.json`, `Bun3.BadWords.csproj` (version, description)
- Modify: `common/src/com.bun3.badwords/README.md`, `Third Party Notices.md`
- Modify: `common/src/com.bun3.badwords/Runtime/BadWordFilter.cs` (class summary, `DefaultEntries` doc)
- Modify: `docs/research/2026-10-13-bad-word-multilingual-sources.md` (append `## 0.2.0 큐레이션 기록`)
- Create (Unity-generated): `common/src/com.bun3.badwords/Runtime/TextNormalizer.Simplified.cs.meta`

**Interfaces:**
- Consumes: everything above; the baseline in `<scratchpad>/scan-cost-baseline.txt`.
- Produces: a releasable 0.2.0 package.

- [ ] **Step 1: Version and metadata**

`package.json`: `"version": "0.2.0"`, `"description": "Allocation-free multilingual bad word detection (Korean, English, Spanish, Portuguese, French, Italian, German, Dutch, Tagalog, Russian, Japanese, Chinese) with a bundled default dictionary. netstandard2.1; no UnityEngine dependency."`. `Bun3.BadWords.csproj`: `<Version>0.2.0</Version>`.

`BadWordFilter.cs`: class summary "Detects bad words in short user text such as nicknames and chat lines, in every bundled language at once."; `DefaultEntries` summary "The bundled entries for every supported language, for composing consumer-specific filters.".

- [ ] **Step 2: README**

Rewrite these sections of `README.md` (English, no non-Latin characters):

- Opening: the twelve languages; "Every language applies to every input, whatever the UI language."
- **Entry grammar:** the stage table — Hangul → Korean (Hangul and spaces, no boundaries); Cyrillic → Cyrillic (Cyrillic letters, Latin `a c e o p x y` in any case and `B H K M T`, spaces; boundaries allowed); kana or Han → kana/Han (kana, Han, the iteration mark, the prolonged sound mark, the middle dot, spaces; at least two characters after normalization; no boundaries); anything else → Latin. Rejected: mixed stages, letters of any other script (Greek, Arabic; Cyrillic look-alikes inside a Latin word), characters outside the Basic Multilingual Plane.
- **What is normalized:** add sharp s to `s`; the Cyrillic stream (lowercase, yo to ye, short i to i, Latin look-alikes folded into Cyrillic, separators as boundaries, single letters joined, stretched letters collapsed; runs only when the text has Cyrillic); the kana/Han stream (hiragana and halfwidth katakana folded to katakana with voiced marks combined, Traditional Han mapped to Simplified, every other character dropped; runs only when the text has kana or Han).
- **Limits:** add Latin Extended-A letters, romanized Japanese and Chinese, leet digits in Cyrillic, Han outside the BMP, Russian inflections outside the bundled roots, no Cebuano- or Irish-specific words.
- **Default dictionary:** one file per language plus `zh-simplified.txt`; both `Runtime/BadWordFilter.Default.cs` and `Runtime/TextNormalizer.Simplified.cs` are generated by the same command.
- **License note:** part of the dictionary is derived from LDNOOBW under CC-BY-4.0 — consumers must reproduce its notice from `Third Party Notices.md` (for example on a credits or licenses screen) and must not apply DRM to builds that include it.

- [ ] **Step 3: Third Party Notices**

Add one section per new source, each with name, URL@commit, what is used and the **full license text copied verbatim** from:

```bash
curl -sL https://raw.githubusercontent.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words/5faf2ba42d7b1c0977169ec3611df25a3c08eb13/LICENSE
curl -sL https://raw.githubusercontent.com/wooorm/cuss/6bab3fef250481e34ba55bc400fac5c6d25f1429/license
curl -sL https://raw.githubusercontent.com/MosasoM/inappropriate-words-ja/e24de6e185135f92c3491ef61931c26905bd3b1f/LICENSE
curl -sL https://raw.githubusercontent.com/EbiseLutica/Citrine/e6a37316bc8f3009febe0e0db77d9e29872e2d4d/LICENSE
```

The LDNOOBW section also states: title "List of Dirty, Naughty, Obscene, and Otherwise Bad Words", creator "Shutterstock and LDNOOBW contributors", license link https://creativecommons.org/licenses/by/4.0/, "Modified: entries were selected, rewritten and normalized, and combined with other sources into the compiled dictionary.", and that the material is provided as-is. Rename the Unicode section's "Used" line to cover both look-alike mappings and the Unihan `kSimplifiedVariant` pairs (Unicode 17.0.0).

- [ ] **Step 4: Costs**

Run three times: `dotnet test common/tests/Bun3.BadWords.Tests -c Release --filter ReportsScanCost --logger "console;verbosity=detailed"`.
Expected: Korean, English and other Latin inputs within 1.2× of the Task 1 baseline medians; the Cyrillic and kana/Han inputs within 5,000 ns. Otherwise profile before continuing.

Run once in a fresh process: `dotnet test common/tests/Bun3.BadWords.Tests -c Release --filter ReportsDefaultBuildCost --logger "console;verbosity=detailed"`.
Expected: `buildMs` ≤ 50. If higher, investigate the cause before merge (spec).

- [ ] **Step 5: Research record**

Append `## 0.2.0 큐레이션 기록` (Korean) to `docs/research/2026-10-13-bad-word-multilingual-sources.md`: entry counts per data file (block/allow); extraction skip counts with examples; what the drop rules and the owner ruling removed; boundaries and allows added per sweep; hit counts per corpus before → after; hits kept on purpose and why; the owner spot-check decisions; Unihan pair count and version; the FrequencyWords commit `525f9b560de45753a5ea01069454e72e9aa541c6`; scan cost before → after per input; build cost.

- [ ] **Step 6: Unity EditMode run (generates the new .meta)**

Make sure no Unity Editor has `E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity` open, then run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -nographics \
  -projectPath "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity" \
  -runTests -testPlatform EditMode -assemblyNames Bun3.BadWords.Tests \
  -testResults "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity/Logs/badwords-editmode.xml" \
  -logFile "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity/Logs/badwords-editmode.log"
```

Expected: exit code 0; the XML root `test-run` has `result="Passed"` and `failed="0"`; the count equals the dotnet run minus the dotnet-only tests (`DefaultEntriesGenerator` ×2, `UnicodeTableTests` ×2); no `warning CS` lines for `Bun3.BadWords*` in the log. Then `git status --short`: a new `Runtime/TextNormalizer.Simplified.cs.meta`, no `.meta` under `Data~`; revert `unity/Packages/packages-lock.json` if Unity touched it.

- [ ] **Step 7: Final checks and commit**

Run: `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj -c Release` and `dotnet test common/tests/Bun3.BadWords.Tests`.
Expected: `0 Warning(s)`; all PASS except `SweepCorpus`.

```bash
git add common/src/com.bun3.badwords docs/research/2026-10-13-bad-word-multilingual-sources.md
git commit -m "🔖 다국어 비속어 필터 0.2.0 문서·라이선스 고지·Unity meta 정리" -m "<body>" -m "Co-Authored-By: Claude Opus 5.5 (1M context) <noreply@anthropic.com>"
```

---

## After the last task

Use `superpowers:finishing-a-development-branch`. Merge `Bun3/bad-word-filter` into `main` locally and push only after the owner approves at that time. Then, in jurassic-paradise (separate work, separate approval):

- update `unity/Packages/packages-lock.json` to the new bun3-kit commit;
- update `docs/third-party/bun3-badwords.md` with the LDNOOBW attribution and the no-DRM condition, and carry the notice into the credits/licenses screen plan;
- its tests use fake dictionaries and need no change.
