# Bad Word Filter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship `com.bun3.badwords` 0.1.0 — an allocation-free, thread-safe Korean + English bad word filter with a curated default dictionary compiled into the assembly.

**Architecture:** `TextNormalizer` turns input into three streams (Hangul, 2-beolsik keyboard, Latin); `HangulComposer` recombines loose jamo; `AhoCorasick` (CSR edges, failure links, output links) scans each stream with allow-span suppression; `BadWordFilter` wires it together and exposes `Default` built from a generated `BadWordFilter.Default.cs`.

**Tech Stack:** netstandard2.1, C# 9, NUnit (dotnet: NUnit 4.1 via `common/tests`; Unity: Test Framework NUnit 3), Unity 6000.3.14f1 (bun3-kit `unity/` project), Python 3 (scratch-only curation script).

**Spec:** `docs/superpowers/specs/2026-10-12-bad-word-filter-design.md` (research: `docs/research/2026-10-10-bad-word-filter-research.md`)

## Global Constraints

- Package path `common/src/com.bun3.badwords`; UPM name `com.bun3.badwords`; NuGet/assembly `Bun3.BadWords`; version `0.1.0` in both `package.json` and `.csproj`.
- netstandard2.1, `LangVersion` 9.0, block namespaces, **no dependencies**, `noEngineReferences: true`.
- Folder = namespace: `Runtime/` → `Bun3.BadWords` (flat); `Tests/Editor/` → `Bun3.BadWords.Tests`.
- English only in code, comments, README, package metadata. **No Hangul characters in `Runtime/`** — write `\uXXXX` escapes. Hangul is allowed only as test-input data literals in tests and as data in `Data~/*.txt`.
- Every public member has an English XML doc comment. `dotnet build` must report `0 Warning(s)`.
- `ContainsBadWord` allocates nothing: no strings, closures, LINQ, boxing, iterators on the call path.
- Shared tests (`Tests/Editor`) compile against both NUnit 3 (Unity) and NUnit 4.1 (dotnet): use only `Assert.That(..., Is.*)`, `Assert.Throws<T>`, `Assert.Pass`, `Assert.Ignore`, `[Test]`, `[TestCase]`. No `Assert.AreEqual`/`ClassicAssert`.
- Comments only for constraints/contracts the code cannot show; no narration comments.
- Commits: gitmoji title + Korean Why/Changes/Impact/Results body (use the `writing-gitmoji-commits` skill), trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` (bun3-kit `CLAUDE.md`). Commit with repeated `-m` flags or `-F <file>`.

## Review Focus

1. **Input with more than 32 matches in one scan** (chat paste such as `시발점` × 40) — spec says a 33rd block span returns `true` conservatively; pinned in Task 4.
2. **Emoji (surrogate pairs) between letters** (`시😀발`) — surrogates are "other", dropped from stream A, so it must block; pinned in Task 4.
3. **Text made only of removed characters** (`\u200B\u200B`, `\u3164`) — must return `false` without touching empty spans incorrectly; pinned in Task 4.
4. **A `null` element inside `entries`** — must throw `ArgumentException`, not `NullReferenceException`; pinned in Task 4.
5. **Digits between loose jamo** (`ㅅ1ㅂ`) — stream A drops the digit before composing, so it must block like `ㅅㅂ`; pinned in Task 4.

---

### Task 1: Package scaffold and Hangul composer

**Files:**
- Create: `common/src/com.bun3.badwords/package.json`
- Create: `common/src/com.bun3.badwords/Bun3.BadWords.asmdef`
- Create: `common/src/com.bun3.badwords/Bun3.BadWords.csproj`
- Create: `common/src/com.bun3.badwords/Runtime/AssemblyInfo.cs`
- Create: `common/src/com.bun3.badwords/Runtime/HangulComposer.cs`
- Create: `common/src/com.bun3.badwords/Tests/Editor/Bun3.BadWords.Tests.asmdef`
- Create: `common/src/com.bun3.badwords/Tests/Editor/HangulComposerTests.cs`
- Create: `common/tests/Bun3.BadWords.Tests/Bun3.BadWords.Tests.csproj`
- Modify: `Bun3.sln` (via `dotnet sln add`)

**Interfaces:**
- Produces: `internal static class HangulComposer` with `internal static int Compose(ReadOnlySpan<char> source, Span<char> destination)` (returns written length, never more than `source.Length`) and constants `FirstSyllable` (`\uAC00`), `LastSyllable` (`\uD7A3`), `FirstConsonant` (`\u3131`), `FirstVowel` (`\u314F`), `LastVowel` (`\u3163`).

- [ ] **Step 1: Create package metadata, assembly definitions and projects**

`common/src/com.bun3.badwords/package.json`:

```json
{
  "name": "com.bun3.badwords",
  "displayName": "Bun3 Bad Words",
  "version": "0.1.0",
  "unity": "2022.3",
  "description": "Allocation-free Korean and English bad word detection with a bundled default dictionary. netstandard2.1; no UnityEngine dependency.",
  "dependencies": {},
  "author": {
    "name": "Bun3",
    "url": "https://github.com/Bun3",
    "email": "bun3.dev@gmail.com"
  }
}
```

`common/src/com.bun3.badwords/Bun3.BadWords.asmdef`:

```json
{
    "name": "Bun3.BadWords",
    "rootNamespace": "Bun3.BadWords",
    "references": [],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": true
}
```

`common/src/com.bun3.badwords/Bun3.BadWords.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>9.0</LangVersion>
    <RootNamespace>Bun3.BadWords</RootNamespace>
    <AssemblyName>Bun3.BadWords</AssemblyName>
    <PackageId>Bun3.BadWords</PackageId>
    <Version>0.1.0</Version>
    <Authors>Bun3</Authors>
    <RepositoryUrl>https://github.com/Bun3/bun3-kit</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove="Tests/**/*.cs" />
  </ItemGroup>

</Project>
```

`common/src/com.bun3.badwords/Runtime/AssemblyInfo.cs` (source attribute so Unity's asmdef compile sees it too):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Bun3.BadWords.Tests")]
```

`common/src/com.bun3.badwords/Tests/Editor/Bun3.BadWords.Tests.asmdef`:

```json
{
    "name": "Bun3.BadWords.Tests",
    "rootNamespace": "Bun3.BadWords.Tests",
    "references": [
        "Bun3.BadWords",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`common/tests/Bun3.BadWords.Tests/Bun3.BadWords.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <RootNamespace>Bun3.BadWords.Tests</RootNamespace>
    <AssemblyName>Bun3.BadWords.Tests</AssemblyName>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.10.0" />
    <PackageReference Include="NUnit" Version="4.1.0" />
    <PackageReference Include="NUnit3TestAdapter" Version="4.5.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\com.bun3.badwords\Bun3.BadWords.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Compile Include="..\..\src\com.bun3.badwords\Tests\Editor\**\*.cs" LinkBase="Shared" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln Bun3.sln add common/src/com.bun3.badwords/Bun3.BadWords.csproj common/tests/Bun3.BadWords.Tests/Bun3.BadWords.Tests.csproj`
Expected: two "Project ... added to the solution." lines.

- [ ] **Step 2: Write the failing composer test**

`common/src/com.bun3.badwords/Tests/Editor/HangulComposerTests.cs`:

```csharp
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class HangulComposerTests
    {
        [TestCase("ㅅㅣㅂㅏㄹ", "시발")]
        [TestCase("ㅅㅂ", "ㅅㅂ")]
        [TestCase("ㄱㅗㅏ", "과")]
        [TestCase("ㄷㅏㄹㄱ", "닭")]
        [TestCase("ㄷㅏㄹㄱㅏ", "달가")]
        [TestCase("ㅇㅣㅆㅇㅓ", "있어")]
        [TestCase("ㄸㅏㄸ", "따ㄸ")]
        [TestCase("시ㅂㅏㄹ", "시발")]
        [TestCase("ㅗㅏ", "ㅘ")]
        [TestCase("ㄳ", "ㄳ")]
        [TestCase("ㅂㅕㅇㅅㅣㄴ", "병신")]
        [TestCase("가나", "가나")]
        [TestCase("ㅏㄱ", "ㅏㄱ")]
        [TestCase("", "")]
        public void ComposesLikeTwoBeolsikIme(string jamo, string expected)
        {
            var destination = new char[jamo.Length];
            int length = HangulComposer.Compose(jamo, destination);
            Assert.That(new string(destination, 0, length), Is.EqualTo(expected));
        }
    }
}
```

- [ ] **Step 3: Run it to verify it fails**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAIL — `error CS0103: The name 'HangulComposer' does not exist in the current context`.

- [ ] **Step 4: Implement the composer**

`common/src/com.bun3.badwords/Runtime/HangulComposer.cs`:

```csharp
using System;

namespace Bun3.BadWords
{
    /// <summary>
    /// Composes Hangul compatibility jamo into syllables with 2-beolsik IME rules.
    /// Precomposed syllables and other characters pass through unchanged and flush pending jamo.
    /// </summary>
    internal static class HangulComposer
    {
        internal const char FirstSyllable = '\uAC00';
        internal const char LastSyllable = '\uD7A3';
        internal const char FirstConsonant = '\u3131';
        internal const char FirstVowel = '\u314F';
        internal const char LastVowel = '\u3163';

        // Indexed by (consonant - U+3131): initial index, or -1 when the consonant cannot start a syllable.
        static readonly sbyte[] initialOf = { 0, 1, -1, 2, -1, -1, 3, 4, 5, -1, -1, -1, -1, -1, -1, -1, 6, 7, 8, -1, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18 };

        // Indexed by (consonant - U+3131): final index, or 0 when the consonant cannot end a syllable.
        static readonly byte[] finalOf = { 1, 2, 3, 4, 5, 6, 7, 0, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 0, 18, 19, 20, 21, 22, 0, 23, 24, 25, 26, 27 };

        // Indexed by final index: the initial index a simple final becomes before a vowel.
        static readonly sbyte[] finalToInitial = { -1, 0, 1, -1, 2, -1, -1, 3, 5, -1, -1, -1, -1, -1, -1, -1, 6, 7, -1, 9, 10, 11, 12, 14, 15, 16, 17, 18 };

        // Indexed by initial index.
        const string initialJamo = "\u3131\u3132\u3134\u3137\u3138\u3139\u3141\u3142\u3143\u3145\u3146\u3147\u3148\u3149\u314A\u314B\u314C\u314D\u314E";

        /// <summary>Writes the composed text and returns its length (never more than <paramref name="source"/>.Length).</summary>
        internal static int Compose(ReadOnlySpan<char> source, Span<char> destination)
        {
            int written = 0, initial = -1, vowel = -1, final = 0;
            foreach (char c in source)
            {
                if (c >= FirstSyllable && c <= LastSyllable)
                {
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    destination[written++] = c;
                }
                else if (c >= FirstConsonant && c < FirstVowel)
                {
                    int consonant = c - FirstConsonant;
                    if (initial >= 0 && vowel >= 0)
                    {
                        int next = final == 0 ? finalOf[consonant] : CombineFinal(final, finalOf[consonant]);
                        if (next > 0)
                        {
                            final = next;
                            continue;
                        }
                    }
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    if (initialOf[consonant] >= 0) initial = initialOf[consonant];
                    else destination[written++] = c;
                }
                else if (c >= FirstVowel && c <= LastVowel)
                {
                    int v = c - FirstVowel;
                    if (initial >= 0 && vowel < 0)
                    {
                        vowel = v;
                        continue;
                    }
                    if (vowel >= 0 && final == 0)
                    {
                        int combined = CombineVowel(vowel, v);
                        if (combined >= 0)
                        {
                            vowel = combined;
                            continue;
                        }
                    }
                    if (initial >= 0 && vowel >= 0 && final > 0)
                    {
                        int nextInitial = SplitFinal(final, out final);
                        Flush(destination, ref written, ref initial, ref vowel, ref final);
                        initial = nextInitial;
                        vowel = v;
                        continue;
                    }
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    vowel = v;
                }
                else
                {
                    Flush(destination, ref written, ref initial, ref vowel, ref final);
                    destination[written++] = c;
                }
            }
            Flush(destination, ref written, ref initial, ref vowel, ref final);
            return written;
        }

        static void Flush(Span<char> destination, ref int written, ref int initial, ref int vowel, ref int final)
        {
            if (initial >= 0 && vowel >= 0) destination[written++] = (char)(FirstSyllable + (initial * 21 + vowel) * 28 + final);
            else if (initial >= 0) destination[written++] = initialJamo[initial];
            else if (vowel >= 0) destination[written++] = (char)(FirstVowel + vowel);
            initial = -1;
            vowel = -1;
            final = 0;
        }

        static int CombineVowel(int first, int second)
        {
            switch (first)
            {
                case 8: return second == 0 ? 9 : second == 1 ? 10 : second == 20 ? 11 : -1;
                case 13: return second == 4 ? 14 : second == 5 ? 15 : second == 20 ? 16 : -1;
                case 18: return second == 20 ? 19 : -1;
                default: return -1;
            }
        }

        static int CombineFinal(int first, int second)
        {
            switch (first)
            {
                case 1: return second == 19 ? 3 : 0;
                case 4: return second == 22 ? 5 : second == 27 ? 6 : 0;
                case 8:
                    switch (second)
                    {
                        case 1: return 9;
                        case 16: return 10;
                        case 17: return 11;
                        case 19: return 12;
                        case 25: return 13;
                        case 26: return 14;
                        case 27: return 15;
                        default: return 0;
                    }
                case 17: return second == 19 ? 18 : 0;
                default: return 0;
            }
        }

        // Returns the initial that moves to the next syllable; remaining is the final that stays.
        static int SplitFinal(int final, out int remaining)
        {
            switch (final)
            {
                case 3: remaining = 1; return 9;
                case 5: remaining = 4; return 12;
                case 6: remaining = 4; return 18;
                case 9: remaining = 8; return 0;
                case 10: remaining = 8; return 6;
                case 11: remaining = 8; return 7;
                case 12: remaining = 8; return 9;
                case 13: remaining = 8; return 16;
                case 14: remaining = 8; return 17;
                case 15: remaining = 8; return 18;
                case 18: remaining = 17; return 9;
                default: remaining = 0; return finalToInitial[final];
            }
        }
    }
}
```

- [ ] **Step 5: Run tests and build**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` then `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj`
Expected: `Passed! - Failed: 0, Passed: 14`; build `0 Warning(s)`.

- [ ] **Step 6: Commit**

```bash
git add Bun3.sln common/src/com.bun3.badwords common/tests/Bun3.BadWords.Tests
git commit -m "✨ 비속어 필터 패키지 골격과 한글 자모 조합기 추가" -m "<Why/Changes/Impact/Results body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Aho-Corasick automaton

**Files:**
- Create: `common/src/com.bun3.badwords/Runtime/AhoCorasick.cs`
- Create: `common/src/com.bun3.badwords/Runtime/PatternSet.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/AhoCorasickTests.cs`

**Interfaces:**
- Produces:
  - `[Flags] internal enum PatternFlags : byte { None = 0, Allow = 1, BoundaryStart = 2, BoundaryEnd = 4, Short = 8 }`
  - `internal readonly struct Match { public readonly int Start, End, Pattern; }` (`End` exclusive)
  - `internal sealed class AhoCorasick(IReadOnlyList<string> patterns, IReadOnlyList<PatternFlags> flags)` — throws `ArgumentException` on empty or duplicate patterns.
  - `AhoCorasick.MatchEnumerator Matches(ReadOnlySpan<char> text)` (ref struct, `foreach`-able, every occurrence of every pattern).
  - `bool ContainsBlocked(ReadOnlySpan<char> text, bool shortNeedsWholeText)` and `bool TryFindBlocked(ReadOnlySpan<char> text, bool shortNeedsWholeText, out Match blocked)`; `const int SpanCapacity = 32`.
  - `internal sealed class PatternSet { void Add(string key, PatternFlags flags); AhoCorasick Build(); }` — duplicate keys merge: `Allow`/`Short` OR, boundary flags AND.

- [ ] **Step 1: Write the failing tests**

`common/src/com.bun3.badwords/Tests/Editor/AhoCorasickTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class AhoCorasickTests
    {
        static readonly string[] alphabets = { "abc", "\uAC00\uB098\uB2E4" };

        [Test]
        public void MatchesEqualNaiveSearch()
        {
            var random = new Random(20261012);
            for (int round = 0; round < 500; round++)
            {
                string alphabet = alphabets[round % alphabets.Length];
                var patterns = new List<string>();
                var seen = new HashSet<string>();
                int patternCount = random.Next(1, 12);
                while (patterns.Count < patternCount)
                {
                    string pattern = RandomText(random, alphabet, random.Next(1, 5));
                    if (seen.Add(pattern)) patterns.Add(pattern);
                }
                var automaton = new AhoCorasick(patterns, new PatternFlags[patterns.Count]);
                string text = RandomText(random, alphabet, random.Next(0, 40));

                var actual = new List<(int, int, int)>();
                foreach (Match match in automaton.Matches(text)) actual.Add((match.Start, match.End, match.Pattern));
                var expected = new List<(int, int, int)>();
                for (int p = 0; p < patterns.Count; p++)
                    for (int i = 0; i + patterns[p].Length <= text.Length; i++)
                        if (string.CompareOrdinal(text, i, patterns[p], 0, patterns[p].Length) == 0)
                            expected.Add((i, i + patterns[p].Length, p));

                Assert.That(actual, Is.EquivalentTo(expected), "round " + round);
            }
        }

        [Test]
        public void AllowSpanSuppressesOnlyFullyCoveredBlocks()
        {
            var automaton = new AhoCorasick(new[] { "ab", "abc" }, new[] { PatternFlags.None, PatternFlags.Allow });
            Assert.That(automaton.ContainsBlocked("abc", false), Is.False);
            Assert.That(automaton.ContainsBlocked("ababc", false), Is.True);
        }

        [Test]
        public void BoundaryAndShortFlagsRestrictValidMatches()
        {
            var automaton = new AhoCorasick(new[] { "ass", "tq" }, new[] { PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd, PatternFlags.Short });
            Assert.That(automaton.ContainsBlocked("my ass", false), Is.True);
            Assert.That(automaton.ContainsBlocked("class", false), Is.False);
            Assert.That(automaton.ContainsBlocked("tq", true), Is.True);
            Assert.That(automaton.ContainsBlocked("tqx", true), Is.False);
            Assert.That(automaton.ContainsBlocked("tqx", false), Is.True);
        }

        [Test]
        public void MergedDuplicatesKeepTheLeastRestrictiveBoundary()
        {
            var set = new PatternSet();
            set.Add("ab", PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd);
            set.Add("ab", PatternFlags.BoundaryStart);
            var automaton = set.Build();
            Assert.That(automaton.ContainsBlocked("ab x", false), Is.True);
            Assert.That(automaton.ContainsBlocked("abx", false), Is.True);
            Assert.That(automaton.ContainsBlocked("xab", false), Is.False);
        }

        [Test]
        public void RejectsEmptyAndDuplicatePatterns()
        {
            Assert.Throws<ArgumentException>(() => new AhoCorasick(new[] { "" }, new PatternFlags[1]));
            Assert.Throws<ArgumentException>(() => new AhoCorasick(new[] { "a", "a" }, new PatternFlags[2]));
        }

        static string RandomText(Random random, string alphabet, int length)
        {
            var chars = new char[length];
            for (int i = 0; i < length; i++) chars[i] = alphabet[random.Next(alphabet.Length)];
            return new string(chars);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAIL — `CS0246: The type or namespace name 'AhoCorasick' could not be found`.

- [ ] **Step 3: Implement the automaton**

`common/src/com.bun3.badwords/Runtime/AhoCorasick.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    [Flags]
    internal enum PatternFlags : byte
    {
        None = 0,
        Allow = 1,
        BoundaryStart = 2,
        BoundaryEnd = 4,
        Short = 8,
    }

    internal readonly struct Match
    {
        public readonly int Start;
        public readonly int End;
        public readonly int Pattern;

        public Match(int start, int end, int pattern)
        {
            Start = start;
            End = end;
            Pattern = pattern;
        }
    }

    /// <summary>
    /// Immutable Aho-Corasick automaton over distinct, non-empty patterns.
    /// Edges are sorted per state in flattened arrays; lookups use binary search. Safe to share across threads.
    /// </summary>
    internal sealed class AhoCorasick
    {
        internal const int SpanCapacity = 32;

        readonly int[] firstEdge;
        readonly char[] edgeChar;
        readonly int[] edgeTarget;
        readonly int[] fail;
        readonly int[] statePattern;
        readonly int[] firstOutput;
        readonly int[] nextOutput;
        readonly int[] patternLength;
        readonly PatternFlags[] patternFlags;

        internal AhoCorasick(IReadOnlyList<string> patterns, IReadOnlyList<PatternFlags> flags)
        {
            var children = new List<Dictionary<char, int>> { new Dictionary<char, int>() };
            var terminal = new List<int> { -1 };
            patternLength = new int[patterns.Count];
            patternFlags = new PatternFlags[patterns.Count];
            for (int p = 0; p < patterns.Count; p++)
            {
                string pattern = patterns[p];
                if (string.IsNullOrEmpty(pattern)) throw new ArgumentException("Patterns must be non-empty.", nameof(patterns));
                int state = 0;
                foreach (char c in pattern)
                {
                    if (!children[state].TryGetValue(c, out int next))
                    {
                        next = children.Count;
                        children.Add(new Dictionary<char, int>());
                        terminal.Add(-1);
                        children[state].Add(c, next);
                    }
                    state = next;
                }
                if (terminal[state] >= 0) throw new ArgumentException("Patterns must be distinct.", nameof(patterns));
                terminal[state] = p;
                patternLength[p] = pattern.Length;
                patternFlags[p] = flags[p];
            }

            int count = children.Count;
            statePattern = terminal.ToArray();
            firstEdge = new int[count + 1];
            for (int s = 0; s < count; s++) firstEdge[s + 1] = firstEdge[s] + children[s].Count;
            edgeChar = new char[firstEdge[count]];
            edgeTarget = new int[firstEdge[count]];
            for (int s = 0; s < count; s++)
            {
                int e = firstEdge[s];
                foreach (var pair in children[s])
                {
                    edgeChar[e] = pair.Key;
                    edgeTarget[e] = pair.Value;
                    e++;
                }
                Array.Sort(edgeChar, edgeTarget, firstEdge[s], children[s].Count);
            }

            fail = new int[count];
            firstOutput = new int[count];
            nextOutput = new int[count];
            firstOutput[0] = -1;
            nextOutput[0] = -1;
            var queue = new Queue<int>();
            for (int e = firstEdge[0]; e < firstEdge[1]; e++) queue.Enqueue(edgeTarget[e]);
            while (queue.Count > 0)
            {
                int s = queue.Dequeue();
                nextOutput[s] = firstOutput[fail[s]];
                firstOutput[s] = statePattern[s] >= 0 ? s : nextOutput[s];
                for (int e = firstEdge[s]; e < firstEdge[s + 1]; e++)
                {
                    int target = edgeTarget[e];
                    fail[target] = Next(fail[s], edgeChar[e]);
                    queue.Enqueue(target);
                }
            }
        }

        internal MatchEnumerator Matches(ReadOnlySpan<char> text) => new MatchEnumerator(this, text);

        internal bool ContainsBlocked(ReadOnlySpan<char> text, bool shortNeedsWholeText) => TryFindBlocked(text, shortNeedsWholeText, out _);

        // A block span is suppressed when an allow span of the same scan covers it; more than SpanCapacity blocks is a hit.
        internal bool TryFindBlocked(ReadOnlySpan<char> text, bool shortNeedsWholeText, out Match blocked)
        {
            Span<Match> blocks = stackalloc Match[SpanCapacity];
            Span<Match> allows = stackalloc Match[SpanCapacity];
            int blockCount = 0, allowCount = 0;
            foreach (Match match in Matches(text))
            {
                PatternFlags flags = patternFlags[match.Pattern];
                if (!IsValid(flags, text, match, shortNeedsWholeText)) continue;
                if ((flags & PatternFlags.Allow) != 0)
                {
                    if (allowCount < SpanCapacity) allows[allowCount++] = match;
                }
                else if (blockCount == SpanCapacity)
                {
                    blocked = match;
                    return true;
                }
                else
                {
                    blocks[blockCount++] = match;
                }
            }
            for (int b = 0; b < blockCount; b++)
            {
                bool covered = false;
                for (int a = 0; a < allowCount && !covered; a++)
                    covered = allows[a].Start <= blocks[b].Start && blocks[b].End <= allows[a].End;
                if (!covered)
                {
                    blocked = blocks[b];
                    return true;
                }
            }
            blocked = default;
            return false;
        }

        static bool IsValid(PatternFlags flags, ReadOnlySpan<char> text, Match match, bool shortNeedsWholeText)
        {
            if ((flags & PatternFlags.BoundaryStart) != 0 && match.Start > 0 && text[match.Start - 1] != ' ') return false;
            if ((flags & PatternFlags.BoundaryEnd) != 0 && match.End < text.Length && text[match.End] != ' ') return false;
            return !shortNeedsWholeText || (flags & PatternFlags.Short) == 0 || (match.Start == 0 && match.End == text.Length);
        }

        int Next(int state, char c)
        {
            while (true)
            {
                int target = Edge(state, c);
                if (target >= 0) return target;
                if (state == 0) return 0;
                state = fail[state];
            }
        }

        int Edge(int state, char c)
        {
            int low = firstEdge[state], high = firstEdge[state + 1] - 1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                char key = edgeChar[middle];
                if (key == c) return edgeTarget[middle];
                if (key < c) low = middle + 1;
                else high = middle - 1;
            }
            return -1;
        }

        internal ref struct MatchEnumerator
        {
            readonly AhoCorasick automaton;
            readonly ReadOnlySpan<char> text;
            int position;
            int state;
            int output;

            internal MatchEnumerator(AhoCorasick automaton, ReadOnlySpan<char> text)
            {
                this.automaton = automaton;
                this.text = text;
                position = 0;
                state = 0;
                output = -1;
                Current = default;
            }

            public Match Current { get; private set; }

            public MatchEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                while (output < 0)
                {
                    if (position == text.Length) return false;
                    state = automaton.Next(state, text[position++]);
                    output = automaton.firstOutput[state];
                }
                int pattern = automaton.statePattern[output];
                Current = new Match(position - automaton.patternLength[pattern], position, pattern);
                output = automaton.nextOutput[output];
                return true;
            }
        }
    }
}
```

`common/src/com.bun3.badwords/Runtime/PatternSet.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>Collects normalized patterns, merging the flags of duplicate keys, and builds an automaton.</summary>
    internal sealed class PatternSet
    {
        const PatternFlags boundaries = PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd;

        readonly Dictionary<string, PatternFlags> patterns = new Dictionary<string, PatternFlags>(StringComparer.Ordinal);

        internal void Add(string key, PatternFlags flags)
        {
            if (!patterns.TryGetValue(key, out PatternFlags existing))
            {
                patterns.Add(key, flags);
                return;
            }
            patterns[key] = ((existing | flags) & ~boundaries) | (existing & flags & boundaries);
        }

        internal AhoCorasick Build()
        {
            var keys = new List<string>(patterns.Count);
            var flags = new List<PatternFlags>(patterns.Count);
            foreach (var pair in patterns)
            {
                keys.Add(pair.Key);
                flags.Add(pair.Value);
            }
            return new AhoCorasick(keys, flags);
        }
    }
}
```

- [ ] **Step 4: Run tests and build**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` then `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj`
Expected: all tests PASS (19); `0 Warning(s)`.

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.badwords
git commit -m "✨ 허용 구간 억제를 지원하는 Aho-Corasick 매처 추가" -m "<body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Text normalizer (step 0 and three streams)

**Files:**
- Create: `common/src/com.bun3.badwords/Runtime/TextNormalizer.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/TextNormalizerTests.cs`

**Interfaces:**
- Consumes: `HangulComposer.Compose`, its constants.
- Produces (`internal static class TextNormalizer`):
  - `const char Removed = '\0'`, `const char Other = '\uFFFF'`; `char Clean(char c)`.
  - `int Hangul(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)`
  - `int Keyboard(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)` (returns 0 when no ASCII letter)
  - `int Latin(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)` (lowercase letters, single `' '` boundaries, no leading/trailing space, single-letter runs ≥ 3 joined)
  - `int Collapse(ReadOnlySpan<char> text, Span<char> destination)`
  - `bool IsHangul(char c)`, `bool IsLatinLetter(char c)`, `bool ContainsJamo(string text)`, `bool HasDoubledLetter(string text)`
  - All outputs are never longer than the input; `work` and `destination` must each hold `text.Length` chars and must not overlap.

- [ ] **Step 1: Write the failing tests**

`common/src/com.bun3.badwords/Tests/Editor/TextNormalizerTests.cs`:

```csharp
using System;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class TextNormalizerTests
    {
        delegate int Builder(ReadOnlySpan<char> text, Span<char> work, Span<char> destination);

        [TestCase('\u200B', '\0')]
        [TestCase('\u3164', '\0')]
        [TestCase('\u0301', '\0')]
        [TestCase('\uFF21', 'A')]
        [TestCase('\u00E9', 'e')]
        [TestCase('\u00C6', '\uFFFF')]
        [TestCase('\u0430', 'a')]
        [TestCase('\uD83D', '\uFFFF')]
        [TestCase('\uAC00', '\uAC00')]
        public void CleansCharacters(char input, char expected) =>
            Assert.That(TextNormalizer.Clean(input), Is.EqualTo(expected));

        [TestCase("시1발", "시발")]
        [TestCase("시 발", "시발")]
        [TestCase("시.발", "시발")]
        [TestCase("ㅅㅣㅂㅏㄹ", "시발")]
        [TestCase("시\u200B발", "시발")]
        [TestCase("시\u3164발", "시발")]
        [TestCase("ㅅ ㅂ", "ㅅㅂ")]
        [TestCase("ㅅ1ㅂ", "ㅅㅂ")]
        [TestCase("abc시발", "시발")]
        [TestCase("abc", "")]
        public void HangulStreamKeepsComposedHangulOnly(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Hangul, text), Is.EqualTo(expected));

        [TestCase("tlqkf", "시발")]
        [TestCase("t l q k f", "시발")]
        [TestCase("qudtls", "병신")]
        [TestCase("TLQKF", "씨빨")]
        [TestCase("tq", "ㅅㅂ")]
        [TestCase("123 시발", "")]
        public void KeyboardStreamFollowsTwoBeolsikLayout(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Keyboard, text), Is.EqualTo(expected));

        [TestCase("Sh1t", "shit")]
        [TestCase("f.u.c.k", "fuck")]
        [TestCase("f u c k", "fuck")]
        [TestCase("a b xyz", "a b xyz")]
        [TestCase("i am a b c", "i am abc")]
        [TestCase("  hello   world  ", "hello world")]
        [TestCase("fu\u200Bck", "fuck")]
        [TestCase("\uFF46\uFF55\uFF43\uFF4B", "fuck")]
        [TestCase("f\u00FCck", "fuck")]
        [TestCase("fu\u0441k", "fuck")]
        [TestCase("시발", "")]
        public void LatinStreamNormalizesLettersAndBoundaries(string text, string expected) =>
            Assert.That(Run(TextNormalizer.Latin, text), Is.EqualTo(expected));

        [TestCase("fuuuck", "fuck")]
        [TestCase("aa bb", "a b")]
        public void CollapseKeepsOneLetterPerRun(string text, string expected)
        {
            var destination = new char[text.Length];
            int length = TextNormalizer.Collapse(text, destination);
            Assert.That(new string(destination, 0, length), Is.EqualTo(expected));
        }

        static string Run(Builder builder, string text)
        {
            var work = new char[text.Length];
            var destination = new char[text.Length];
            int length = builder(text, work, destination);
            return new string(destination, 0, length);
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAIL — `CS0103: The name 'TextNormalizer' does not exist`.

- [ ] **Step 3: Implement the normalizer**

`common/src/com.bun3.badwords/Runtime/TextNormalizer.cs`:

```csharp
using System;
using System.Globalization;

namespace Bun3.BadWords
{
    /// <summary>
    /// Builds the normalized streams scanned by <see cref="BadWordFilter"/>.
    /// Every output is no longer than its input; work and destination buffers must not overlap.
    /// </summary>
    internal static class TextNormalizer
    {
        internal const char Removed = '\0';
        internal const char Other = '\uFFFF';

        // U+00C0..U+00FF folded to lowercase ASCII; '?' marks letters without an ASCII base.
        const string latin1 = "aaaaaa?ceeeeiiii?nooooo?ouuuuy??aaaaaa?ceeeeiiii?nooooo?ouuuuy?y";

        // KS X 5002 2-beolsik layout indexed by (lowercase letter - 'a').
        const string keyboard = "\u3141\u3160\u314A\u3147\u3137\u3139\u314E\u3157\u3151\u3153\u314F\u3163\u3161\u315C\u3150\u3154\u3142\u3131\u3134\u3145\u3155\u314D\u3148\u314C\u315B\u314B";

        internal static char Clean(char c)
        {
            if (c < '\u0080') return c;
            if (c >= '\u00C0' && c <= '\u00FF')
            {
                char folded = latin1[c - '\u00C0'];
                return folded == '?' ? Other : folded;
            }
            if (c >= '\uFF01' && c <= '\uFF5E') return (char)(c - 0xFEE0);
            if (c == '\u115F' || c == '\u1160' || c == '\u3164' || c == '\uFFA0') return Removed;
            char lookalike = Lookalike(c);
            if (lookalike != Removed) return lookalike;
            switch (char.GetUnicodeCategory(c))
            {
                case UnicodeCategory.Format:
                case UnicodeCategory.NonSpacingMark:
                case UnicodeCategory.EnclosingMark:
                    return Removed;
                case UnicodeCategory.Surrogate:
                    return Other;
                default:
                    return c;
            }
        }

        internal static int Hangul(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (IsHangul(c)) work[count++] = c;
            }
            return HangulComposer.Compose(work.Slice(0, count), destination);
        }

        internal static int Keyboard(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (c >= 'a' && c <= 'z') work[count++] = keyboard[c - 'a'];
                else if (c >= 'A' && c <= 'Z') work[count++] = Shifted(c);
            }
            return count == 0 ? 0 : HangulComposer.Compose(work.Slice(0, count), destination);
        }

        internal static int Latin(ReadOnlySpan<char> text, Span<char> work, Span<char> destination)
        {
            int count = 0;
            foreach (char raw in text)
            {
                char c = Clean(raw);
                if (c == Removed) continue;
                char letter = LatinLetter(c);
                if (letter != ' ') work[count++] = letter;
                else if (count > 0 && work[count - 1] != ' ') work[count++] = ' ';
            }
            if (count > 0 && work[count - 1] == ' ') count--;
            return JoinSingleLetters(work.Slice(0, count), destination);
        }

        internal static int Collapse(ReadOnlySpan<char> text, Span<char> destination)
        {
            int count = 0;
            foreach (char c in text)
                if (count == 0 || destination[count - 1] != c) destination[count++] = c;
            return count;
        }

        internal static bool IsHangul(char c) =>
            (c >= HangulComposer.FirstSyllable && c <= HangulComposer.LastSyllable) ||
            (c >= HangulComposer.FirstConsonant && c <= HangulComposer.LastVowel);

        internal static bool IsLatinLetter(char c) => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');

        internal static bool ContainsJamo(string text)
        {
            foreach (char c in text)
                if (c >= HangulComposer.FirstConsonant && c <= HangulComposer.LastVowel) return true;
            return false;
        }

        internal static bool HasDoubledLetter(string text)
        {
            for (int i = 1; i < text.Length; i++)
                if (text[i] != ' ' && text[i] == text[i - 1]) return true;
            return false;
        }

        // Input holds tokens separated by single spaces with no leading or trailing space.
        static int JoinSingleLetters(ReadOnlySpan<char> text, Span<char> destination)
        {
            int written = 0, i = 0;
            while (i < text.Length)
            {
                if (written > 0) destination[written++] = ' ';
                int run = 0;
                while (i + 2 * run < text.Length && (i + 2 * run + 1 == text.Length || text[i + 2 * run + 1] == ' ')) run++;
                if (run >= 3)
                {
                    for (int k = 0; k < run; k++) destination[written++] = text[i + 2 * k];
                    i += 2 * run;
                }
                else
                {
                    int end = i;
                    while (end < text.Length && text[end] != ' ') end++;
                    for (int k = i; k < end; k++) destination[written++] = text[k];
                    i = end + 1;
                }
            }
            return written;
        }

        static char LatinLetter(char c)
        {
            if (c >= 'a' && c <= 'z') return c;
            if (c >= 'A' && c <= 'Z') return (char)(c + 32);
            switch (c)
            {
                case '4':
                case '@':
                    return 'a';
                case '3':
                    return 'e';
                case '1':
                    return 'i';
                case '0':
                    return 'o';
                case '5':
                case '$':
                    return 's';
                case '7':
                    return 't';
                default:
                    return ' ';
            }
        }

        static char Shifted(char c) => c switch
        {
            'Q' => '\u3143',
            'W' => '\u3149',
            'E' => '\u3138',
            'R' => '\u3132',
            'T' => '\u3146',
            'O' => '\u3152',
            'P' => '\u3156',
            _ => keyboard[c - 'A'],
        };

        // Subset of Unicode UTS #39 confusables: Cyrillic and Greek letters drawn like Latin letters.
        static char Lookalike(char c) => c switch
        {
            '\u0430' or '\u0410' or '\u03B1' or '\u0391' => 'a',
            '\u0412' or '\u0392' => 'b',
            '\u0441' or '\u0421' => 'c',
            '\u0501' => 'd',
            '\u0435' or '\u0415' or '\u0395' => 'e',
            '\u04BB' or '\u041D' or '\u0397' => 'h',
            '\u0456' or '\u03B9' or '\u0399' => 'i',
            '\u0458' => 'j',
            '\u041A' or '\u03BA' or '\u039A' => 'k',
            '\u04CF' => 'l',
            '\u041C' or '\u039C' => 'm',
            '\u039D' => 'n',
            '\u043E' or '\u041E' or '\u03BF' or '\u039F' => 'o',
            '\u0440' or '\u0420' or '\u03C1' or '\u03A1' => 'p',
            '\u0455' => 's',
            '\u0422' or '\u03C4' or '\u03A4' => 't',
            '\u03BD' => 'v',
            '\u0445' or '\u0425' or '\u03A7' => 'x',
            '\u0443' or '\u0423' or '\u03A5' => 'y',
            '\u0396' => 'z',
            _ => Removed,
        };
    }
}
```

- [ ] **Step 4: Run tests and build**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` then `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj`
Expected: all tests PASS (57); `0 Warning(s)`.

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.badwords
git commit -m "✨ 한글·두벌식·라틴 정규화 스트림 추가" -m "<body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: BadWordFilter public API

**Files:**
- Create: `common/src/com.bun3.badwords/Runtime/BadWordFilter.cs`
- Create: `common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs` (empty generated list for now)
- Test: `common/src/com.bun3.badwords/Tests/Editor/BadWordFilterTests.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/BadWordFilterPerformanceTests.cs`

**Interfaces:**
- Consumes: `TextNormalizer.*`, `AhoCorasick.TryFindBlocked`, `PatternSet`, `PatternFlags`, `Match`.
- Produces:
  - `public sealed partial class BadWordFilter` with `public static BadWordFilter Default { get; }`, `public static IReadOnlyList<string> DefaultEntries { get; }`, `public BadWordFilter(IEnumerable<string> entries)`, `public bool ContainsBadWord(ReadOnlySpan<char> text)`.
  - `internal string Explain(ReadOnlySpan<char> text)` → `null` or `"<Stream>:<normalized span>"` where `<Stream>` is `Hangul`, `Keyboard`, `Latin` or `Collapsed` (diagnostics; allocates).
  - Generated partial method `static string[] CreateDefaultEntries()` in `BadWordFilter.Default.cs`.

- [ ] **Step 1: Write the failing tests**

`common/src/com.bun3.badwords/Tests/Editor/BadWordFilterTests.cs`:

```csharp
using System;
using System.Text;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class BadWordFilterTests
    {
        static readonly BadWordFilter filter = new BadWordFilter(new[]
        {
            "시발", "!시발점", "!다시발", "ㅅㅂ", "병신", "좆",
            "fuck", "|ass|", "butt", "cum", "!accumulate", "cunt", "!scunthorpe",
        });

        [TestCase("시발")]
        [TestCase("시1발")]
        [TestCase("시 발")]
        [TestCase("시.발")]
        [TestCase("ㅅㅣㅂㅏㄹ")]
        [TestCase("시ㅂㅏㄹ")]
        [TestCase("시\u200B발")]
        [TestCase("시\u3164발")]
        [TestCase("시\uD83D\uDE00발")]
        [TestCase("ㅅㅂ")]
        [TestCase("ㅅ ㅂ")]
        [TestCase("ㅅ1ㅂ")]
        [TestCase("ㅅㅂ야")]
        [TestCase("닉네임시발")]
        [TestCase("시발시발점")]
        [TestCase("tlqkf")]
        [TestCase("t l q k f")]
        [TestCase("qudtls")]
        [TestCase("tq")]
        [TestCase("whw")]
        [TestCase("fuck")]
        [TestCase("FUCK")]
        [TestCase("f u c k")]
        [TestCase("f.u.c.k")]
        [TestCase("fuuuck")]
        [TestCase("\uFF46\uFF55\uFF43\uFF4B")]
        [TestCase("f\u00FCck")]
        [TestCase("fu\u0441k")]
        [TestCase("my ass")]
        [TestCase("you fuck off")]
        public void BlocksEvasionForms(string text) =>
            Assert.That(filter.ContainsBadWord(text), Is.True, text);

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\u200B\u200B")]
        [TestCase("\u3164")]
        [TestCase("시발점")]
        [TestCase("다시 발견")]
        [TestCase("수박")]
        [TestCase("class")]
        [TestCase("assassin")]
        [TestCase("but")]
        [TestCase("accumulate")]
        [TestCase("Scunthorpe")]
        [TestCase("tqx")]
        [TestCase("Player One")]
        [TestCase("가")]
        public void PassesOrdinaryText(string text) =>
            Assert.That(filter.ContainsBadWord(text), Is.False, filter.Explain(text));

        [Test]
        public void LongInputUsesThePooledPath()
        {
            Assert.That(filter.ContainsBadWord(new string('\uAC00', 300) + "시발"), Is.True);
            Assert.That(filter.ContainsBadWord(new string('\uAC00', 300)), Is.False);
        }

        [Test]
        public void MoreBlockSpansThanCapacityCountAsBadWord()
        {
            var text = new StringBuilder();
            for (int i = 0; i < AhoCorasick.SpanCapacity + 8; i++) text.Append("시발점");
            Assert.That(filter.ContainsBadWord(text.ToString()), Is.True);
        }

        [Test]
        public void ExplainNamesTheStreamAndNormalizedSpan()
        {
            Assert.That(filter.Explain("시1발"), Is.EqualTo("Hangul:시발"));
            Assert.That(filter.Explain("tlqkf"), Is.EqualTo("Keyboard:시발"));
            Assert.That(filter.Explain("f.u.c.k"), Is.EqualTo("Latin:fuck"));
            Assert.That(filter.Explain("fuuuck"), Is.EqualTo("Collapsed:fuck"));
            Assert.That(filter.Explain("시발점"), Is.Null);
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("!")]
        [TestCase("|")]
        [TestCase("!|")]
        [TestCase("123")]
        [TestCase("시발bal")]
        [TestCase("18놈")]
        [TestCase("|시발")]
        [TestCase("\u4E2D\u56FD")]
        public void MalformedEntryThrows(string entry) =>
            Assert.Throws<ArgumentException>(() => new BadWordFilter(new[] { entry }));

        [Test]
        public void NullEntryThrowsArgumentException() =>
            Assert.Throws<ArgumentException>(() => new BadWordFilter(new string[] { null }));

        [Test]
        public void NullEntriesThrow() =>
            Assert.Throws<ArgumentNullException>(() => new BadWordFilter(null));

        [Test]
        public void DuplicateEntriesKeepTheLeastRestrictiveBoundary()
        {
            var merged = new BadWordFilter(new[] { "|fuck|", "fuck" });
            Assert.That(merged.ContainsBadWord("motherfucker"), Is.True);
        }
    }
}
```

`common/src/com.bun3.badwords/Tests/Editor/BadWordFilterPerformanceTests.cs`:

```csharp
using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class BadWordFilterPerformanceTests
    {
        static readonly BadWordFilter filter = new BadWordFilter(new[] { "시발", "!시발점", "ㅅㅂ", "fuck", "|ass|" });

        [Test]
        public void ContainsBadWordDoesNotAllocate()
        {
            string shortText = "시1발 f.u.c.k 시발점 tlqkf class";
            string longText = new string('a', 300) + " fuck";
            filter.ContainsBadWord(shortText);
            filter.ContainsBadWord(longText);
#if UNITY_5_3_OR_NEWER
            Assert.That(() =>
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
            }, UnityEngine.TestTools.Constraints.Is.Not.AllocatingGCMemory());
#else
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++)
            {
                filter.ContainsBadWord(shortText);
                filter.ContainsBadWord(longText);
            }
            Assert.That(GC.GetAllocatedBytesForCurrentThread() - before, Is.EqualTo(0));
#endif
        }

        [Test]
        public void ConcurrentCallsMatchSequentialResults()
        {
            string[] inputs = { "시발", "시발점", "fuck you", "class", "tq", "ㅅㅂ", "hello", new string('x', 400) + "시발" };
            var expected = new bool[inputs.Length];
            for (int i = 0; i < inputs.Length; i++) expected[i] = filter.ContainsBadWord(inputs[i]);
            var actual = new bool[4000];
            Parallel.For(0, actual.Length, i => actual[i] = filter.ContainsBadWord(inputs[i % inputs.Length]));
            for (int i = 0; i < actual.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i % inputs.Length]));
        }
    }
}
```

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: build FAIL — `CS0246: The type or namespace name 'BadWordFilter' could not be found`.

- [ ] **Step 3: Implement the filter**

`common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs` (exact bytes; Task 5's generator must reproduce this for an empty list):

```csharp
// <auto-generated/>
// Default dictionary entries generated from Data~/ko.txt and Data~/en.txt. Regenerate: BUN3_BADWORDS_REGEN=1 dotnet test --filter GeneratedDefaultIsUpToDate
namespace Bun3.BadWords
{
    public sealed partial class BadWordFilter
    {
        static string[] CreateDefaultEntries() => new string[]
        {
        };
    }
}
```

`common/src/com.bun3.badwords/Runtime/BadWordFilter.cs`:

```csharp
using System;
using System.Buffers;
using System.Collections.Generic;

namespace Bun3.BadWords
{
    /// <summary>
    /// Detects Korean and English bad words in short user text such as nicknames and chat lines.
    /// Immutable after construction and safe to share across threads.
    /// </summary>
    /// <remarks>
    /// Partial: <c>BadWordFilter.cs</c> (API, entry parsing, scanning) and the generated <c>BadWordFilter.Default.cs</c> (default entries).
    /// </remarks>
    public sealed partial class BadWordFilter
    {
        const int StackLimit = 256;

        readonly AhoCorasick korean;
        readonly AhoCorasick english;
        readonly AhoCorasick englishCollapsed;

        /// <summary>The filter built from <see cref="DefaultEntries"/> on first access. Touch it during boot to prewarm.</summary>
        public static BadWordFilter Default => DefaultFilter.Instance;

        /// <summary>The bundled Korean and English entries, for composing consumer-specific filters.</summary>
        public static IReadOnlyList<string> DefaultEntries => DefaultEntryList.Instance;

        /// <summary>Builds a filter from entries in the form <c>[!][|]word[|]</c>.</summary>
        /// <param name="entries">
        /// Words to block. A leading <c>!</c> marks an allow entry that suppresses block matches it fully covers.
        /// A leading or trailing <c>|</c> requires a word boundary there (English entries only).
        /// Entries containing Hangul are Korean; others are English.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="entries"/> is null.</exception>
        /// <exception cref="ArgumentException">An entry is null, has no letters, mixes Hangul with other characters, or puts a boundary on a Korean word.</exception>
        public BadWordFilter(IEnumerable<string> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var koreanSet = new PatternSet();
            var englishSet = new PatternSet();
            var collapsedSet = new PatternSet();
            foreach (string entry in entries) Add(entry, koreanSet, englishSet, collapsedSet);
            korean = koreanSet.Build();
            english = englishSet.Build();
            englishCollapsed = collapsedSet.Build();
        }

        enum StreamKind
        {
            None,
            Hangul,
            Keyboard,
            Latin,
            Collapsed,
        }

        /// <summary>Returns true when <paramref name="text"/> contains a blocked word that no allow entry covers.</summary>
        /// <remarks>Allocates no managed memory; inputs over 256 characters rent scratch space from <see cref="ArrayPool{T}.Shared"/>.</remarks>
        public bool ContainsBadWord(ReadOnlySpan<char> text)
        {
            if (text.IsEmpty) return false;
            char[] rented = null;
            Span<char> buffer = text.Length <= StackLimit
                ? stackalloc char[StackLimit * 2]
                : (rented = ArrayPool<char>.Shared.Rent(text.Length * 2));
            try
            {
                return Find(text, buffer.Slice(0, text.Length), buffer.Slice(text.Length, text.Length), out _) != StreamKind.None;
            }
            finally
            {
                if (rented != null) ArrayPool<char>.Shared.Return(rented);
            }
        }

        // Diagnostics for dictionary curation; allocates.
        internal string Explain(ReadOnlySpan<char> text)
        {
            var work = new char[text.Length];
            var stream = new char[text.Length];
            StreamKind kind = Find(text, work, stream, out Match match);
            if (kind == StreamKind.None) return null;
            char[] source = kind == StreamKind.Collapsed ? work : stream;
            return kind + ":" + new string(source, match.Start, match.End - match.Start);
        }

        StreamKind Find(ReadOnlySpan<char> text, Span<char> work, Span<char> stream, out Match match)
        {
            int length = TextNormalizer.Hangul(text, work, stream);
            if (korean.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Hangul;
            length = TextNormalizer.Keyboard(text, work, stream);
            if (length > 0 && korean.TryFindBlocked(stream.Slice(0, length), true, out match)) return StreamKind.Keyboard;
            length = TextNormalizer.Latin(text, work, stream);
            if (length == 0) return StreamKind.None;
            if (english.TryFindBlocked(stream.Slice(0, length), false, out match)) return StreamKind.Latin;
            int collapsed = TextNormalizer.Collapse(stream.Slice(0, length), work);
            return englishCollapsed.TryFindBlocked(work.Slice(0, collapsed), false, out match) ? StreamKind.Collapsed : StreamKind.None;
        }

        static void Add(string entry, PatternSet koreanSet, PatternSet englishSet, PatternSet collapsedSet)
        {
            if (entry == null) throw new ArgumentException("Entries must not contain null.", "entries");
            string body = entry.Trim();
            var flags = PatternFlags.None;
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

            bool hangul = false, latin = false, other = false;
            foreach (char c in body)
            {
                char clean = TextNormalizer.Clean(c);
                if (TextNormalizer.IsHangul(clean)) hangul = true;
                else if (TextNormalizer.IsLatinLetter(clean)) latin = true;
                else if (clean != TextNormalizer.Removed && !char.IsWhiteSpace(clean)) other = true;
            }
            if (!hangul && !latin) throw Malformed(entry, "has no Hangul or Latin letters");
            // Stream A drops non-Hangul characters, so a Korean entry with digits or symbols would silently shrink.
            if (hangul && (latin || other)) throw Malformed(entry, "mixes Hangul with other characters");
            if (hangul && (flags & (PatternFlags.BoundaryStart | PatternFlags.BoundaryEnd)) != 0) throw Malformed(entry, "uses a word boundary on a Korean entry");

            var work = new char[body.Length];
            var normalized = new char[body.Length];
            if (hangul)
            {
                int length = TextNormalizer.Hangul(body, work, normalized);
                string key = new string(normalized, 0, length);
                if (length < 2 || TextNormalizer.ContainsJamo(key)) flags |= PatternFlags.Short;
                koreanSet.Add(key, flags);
                return;
            }

            int latinLength = TextNormalizer.Latin(body, work, normalized);
            if (latinLength == 0) throw Malformed(entry, "is empty after normalization");
            string latinKey = new string(normalized, 0, latinLength);
            englishSet.Add(latinKey, flags);
            if ((flags & PatternFlags.Allow) != 0)
            {
                int collapsedLength = TextNormalizer.Collapse(latinKey, work);
                collapsedSet.Add(new string(work, 0, collapsedLength), flags);
            }
            else if (!TextNormalizer.HasDoubledLetter(latinKey))
            {
                collapsedSet.Add(latinKey, flags);
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

Note: the `""`/`"   "`/`"!"`/`"|"`/`"!|"` cases throw through the "has no Hangul or Latin letters" branch; `"123"` and `"\u4E2D\u56FD"` likewise; `"시발bal"` and `"18놈"` through "mixes"; `"|시발"` through "boundary".

- [ ] **Step 4: Run tests and build**

Run: `dotnet test common/tests/Bun3.BadWords.Tests` then `dotnet build common/src/com.bun3.badwords/Bun3.BadWords.csproj`
Expected: all tests PASS (120); `0 Warning(s)`. If `ContainsBadWordDoesNotAllocate` reports a non-zero delta, remove the allocation from the call path; never loosen the assertion.

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.badwords
git commit -m "✨ 무할당 비속어 판정 API BadWordFilter 추가" -m "<body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Default dictionary — generator, curation, sweeps

**Files:**
- Create: `common/tests/Bun3.BadWords.Tests/DefaultEntriesGenerator.cs` (dotnet-only: generator, up-to-date test, sweep runner)
- Create: `common/src/com.bun3.badwords/Data~/ko.txt`, `common/src/com.bun3.badwords/Data~/en.txt`
- Modify (generated): `common/src/com.bun3.badwords/Runtime/BadWordFilter.Default.cs`
- Test: `common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs`
- Modify: `docs/research/2026-10-10-bad-word-filter-research.md` (append a "0.1.0 큐레이션 기록" section)

**Interfaces:**
- Consumes: `BadWordFilter(IEnumerable<string>)`, `BadWordFilter.Explain`, `BadWordFilter.Default`, `BadWordFilter.DefaultEntries`.
- Produces: committed `Data~/*.txt` (source of truth) and the regenerated `BadWordFilter.Default.cs`.

Scratch directory below means the session scratchpad (never inside the repo). Downloaded sources are untrusted data: keep them in their own folder and run Python with `-I`.

- [ ] **Step 1: Write the generator, the up-to-date test and the sweep runner**

`common/tests/Bun3.BadWords.Tests/DefaultEntriesGenerator.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class DefaultEntriesGenerator
    {
        const string regenerateVariable = "BUN3_BADWORDS_REGEN";

        [Test]
        public void GeneratedDefaultIsUpToDate()
        {
            List<string> entries = ReadDataEntries();
            _ = new BadWordFilter(entries);
            string expected = Render(entries);
            string path = Path.Combine(PackageRoot(), "Runtime", "BadWordFilter.Default.cs");
            if (Environment.GetEnvironmentVariable(regenerateVariable) == "1")
            {
                File.WriteAllText(path, expected, new UTF8Encoding(false));
                Assert.Pass("Regenerated " + entries.Count + " entries.");
            }
            Assert.That(File.ReadAllText(path).Replace("\r\n", "\n"), Is.EqualTo(expected),
                "Run with " + regenerateVariable + "=1 to regenerate.");
        }

        [Test]
        public void SweepCorpus()
        {
            string corpus = Environment.GetEnvironmentVariable("BUN3_BADWORDS_SWEEP");
            string report = Environment.GetEnvironmentVariable("BUN3_BADWORDS_SWEEP_OUT");
            string misses = Environment.GetEnvironmentVariable("BUN3_BADWORDS_SWEEP_MISSES");
            if (string.IsNullOrEmpty(corpus) || string.IsNullOrEmpty(report)) Assert.Ignore("Set BUN3_BADWORDS_SWEEP and BUN3_BADWORDS_SWEEP_OUT to run.");

            var filter = new BadWordFilter(ReadDataEntries());
            var hits = new Dictionary<string, (int Count, List<string> Samples)>(StringComparer.Ordinal);
            var unblocked = new List<string>();
            foreach (string line in File.ReadLines(corpus, Encoding.UTF8))
            {
                string reason = filter.Explain(line);
                if (reason == null)
                {
                    unblocked.Add(line);
                    continue;
                }
                hits.TryGetValue(reason, out var hit);
                hit.Samples ??= new List<string>();
                if (hit.Samples.Count < 5) hit.Samples.Add(line);
                hits[reason] = (hit.Count + 1, hit.Samples);
            }
            File.WriteAllLines(report, hits.OrderByDescending(pair => pair.Value.Count)
                .Select(pair => pair.Value.Count + "\t" + pair.Key + "\t" + string.Join(" | ", pair.Value.Samples)), Encoding.UTF8);
            if (!string.IsNullOrEmpty(misses)) File.WriteAllLines(misses, unblocked, Encoding.UTF8);
            Assert.Pass(hits.Count + " distinct reasons, " + unblocked.Count + " unblocked lines.");
        }

        internal static List<string> ReadDataEntries()
        {
            string data = Path.Combine(PackageRoot(), "Data~");
            return ReadEntries(Path.Combine(data, "ko.txt")).Concat(ReadEntries(Path.Combine(data, "en.txt"))).ToList();
        }

        static IEnumerable<string> ReadEntries(string path) =>
            File.ReadAllLines(path, Encoding.UTF8)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal));

        internal static string Render(IReadOnlyList<string> entries)
        {
            var builder = new StringBuilder();
            builder.Append("// <auto-generated/>\n");
            builder.Append("// Default dictionary entries generated from Data~/ko.txt and Data~/en.txt. Regenerate: BUN3_BADWORDS_REGEN=1 dotnet test --filter GeneratedDefaultIsUpToDate\n");
            builder.Append("namespace Bun3.BadWords\n{\n    public sealed partial class BadWordFilter\n    {\n");
            builder.Append("        static string[] CreateDefaultEntries() => new string[]\n        {\n");
            foreach (string entry in entries)
            {
                builder.Append("            \"");
                foreach (char c in entry)
                {
                    if (c >= ' ' && c <= '~' && c != '"' && c != '\\') builder.Append(c);
                    else builder.Append("\\u").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }
                builder.Append("\",\n");
            }
            builder.Append("        };\n    }\n}\n");
            return builder.ToString();
        }

        static string PackageRoot()
        {
            for (var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); directory != null; directory = directory.Parent)
            {
                string candidate = Path.Combine(directory.FullName, "src", "com.bun3.badwords");
                if (Directory.Exists(candidate)) return candidate;
                candidate = Path.Combine(directory.FullName, "common", "src", "com.bun3.badwords");
                if (Directory.Exists(candidate)) return candidate;
            }
            throw new DirectoryNotFoundException("com.bun3.badwords was not found above the test directory.");
        }
    }
}
```

Create `Data~/ko.txt` and `Data~/en.txt` each containing only `# placeholder until Step 3` so the test can run.

Run: `dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`
Expected: PASS — the empty list renders exactly the Task 4 file. If it fails, fix `Render` (not the Task 4 file).

- [ ] **Step 2: Download the pinned sources to scratch**

```bash
SRC="<scratchpad>/badwords-sources"; mkdir -p "$SRC"
curl -sL -o "$SRC/korcen.py" https://raw.githubusercontent.com/Tanat05/korcen/eecd9763dbdccce3dc96ddb578ef0b6396058fa9/korcen/korcen.py
curl -sL -o "$SRC/en.json"   https://raw.githubusercontent.com/dsojevic/profanity-list/c27924319aa9bd6f917e3782b4f4b6604a50b652/en.json
curl -sL -o "$SRC/kbw.md"    https://raw.githubusercontent.com/doublems/korean-bad-words/96d6ce489b64fcf258cd4c25b9a1b3a62b0f5558/korean-bad-words.md
curl -sL -o "$SRC/nsmc.txt"  https://raw.githubusercontent.com/e9t/nsmc/master/ratings.txt
curl -sL -o "$SRC/en-common.txt" https://raw.githubusercontent.com/first20hours/google-10000-english/master/google-10000-english-usa-no-swears.txt
cut -f2 "$SRC/nsmc.txt" | tail -n +2 > "$SRC/nsmc-lines.txt"
```

Expected: `korcen.py` ≈ 65 KB, `en.json` ≈ 63 KB, `kbw.md` ≈ 35 KB, `nsmc-lines.txt` ≈ 200,000 lines.

- [ ] **Step 3: Extract entries with a scratch-only script**

Write `<scratchpad>/badwords-scripts/extract_entries.py` (outside the sources folder):

```python
"""Extract com.bun3.badwords 0.1.0 entries from pinned sources. Usage: python -I extract_entries.py <sources> <data>"""
import ast, json, pathlib, sys

KORCEN = "korcen (MIT) https://github.com/Tanat05/korcen @ eecd9763dbdccce3dc96ddb578ef0b6396058fa9"
DSOJEVIC = "dsojevic/profanity-list (MIT) https://github.com/dsojevic/profanity-list @ c27924319aa9bd6f917e3782b4f4b6604a50b652"
CATEGORIES = ["GENERAL", "SEXUAL", "BELITTLE", "RACE", "PARENT"]


def is_hangul(text):
    return text != "" and all(0xAC00 <= ord(c) <= 0xD7A3 or 0x3131 <= ord(c) <= 0x3163 for c in text)


def is_latin(text):
    return text.strip() != "" and all("a" <= c <= "z" or c == " " for c in text)


def korcen_lists(path):
    lists = {}
    for node in ast.parse(path.read_text(encoding="utf-8")).body:
        if isinstance(node, ast.Assign) and len(node.targets) == 1 and isinstance(node.targets[0], ast.Name):
            name = node.targets[0].id
            if name.endswith("_PROFANITY_PATTERNS") or name.startswith("FALSE_POSITIVE_PATTERNS_"):
                lists[name] = ast.literal_eval(node.value)
    return lists


def write(path, header, sections):
    seen, lines = set(), list(header)
    for title, entries in sections.items():
        lines.append("")
        lines.append(f"# == {title} ==")
        for entry in entries:
            if entry not in seen:
                seen.add(entry)
                lines.append(entry)
    path.write_text("\n".join(lines) + "\n", encoding="utf-8")
    return len(seen)


def main(sources, data):
    lists = korcen_lists(sources / "korcen.py")
    ko, en, skipped = {}, {}, []
    for category in CATEGORIES:
        block = ko.setdefault(f"korcen {category.lower()}", [])
        allow = ko.setdefault(f"korcen {category.lower()} false positives", [])
        latin = en.setdefault("korcen latin forms (romanized and 2-beolsik)", [])
        for raw in lists[f"{category}_PROFANITY_PATTERNS"]:
            text = raw.strip().lower()
            compact = text.replace(" ", "")
            if is_hangul(compact):
                block.append(compact)
            elif is_latin(text):
                latin.append(f"|{text}|" if len(text) <= 3 else text)
            else:
                skipped.append(raw)
        for raw in lists[f"FALSE_POSITIVE_PATTERNS_{category}"]:
            compact = raw.strip().replace(" ", "")
            if is_hangul(compact):
                allow.append("!" + compact)
            else:
                skipped.append(raw)
    blocked = en.setdefault("dsojevic severity 3-4", [])
    exceptions = en.setdefault("dsojevic exceptions", [])
    for item in json.loads((sources / "en.json").read_text(encoding="utf-8")):
        if item["severity"] < 3:
            continue
        for variant in (v.replace("*", "") for v in item["match"].split("|")):
            if is_latin(variant):
                blocked.append(variant)
            else:
                skipped.append(variant)
            for exception in item.get("exceptions", []):
                allowed = exception.replace("*", variant)
                if is_latin(allowed):
                    exceptions.append("!" + allowed)
    grammar = "# Grammar: one entry per line; '#' starts a comment; '!' allow; '|' word boundary (English). See README.md."
    ko_count = write(data / "ko.txt", ["# com.bun3.badwords default Korean entries.", grammar, f"# Source: {KORCEN}"], ko)
    en_count = write(data / "en.txt", ["# com.bun3.badwords default English entries.", grammar, f"# Sources: {DSOJEVIC}; {KORCEN}"], en)
    print(f"ko={ko_count} en={en_count} skipped={len(skipped)}")
    print("\n".join(skipped))


if __name__ == "__main__":
    main(pathlib.Path(sys.argv[1]), pathlib.Path(sys.argv[2]))
```

Run: `PYTHONIOENCODING=utf-8 python -I "<scratchpad>/badwords-scripts/extract_entries.py" "$SRC" common/src/com.bun3.badwords/Data~ > "<scratchpad>/badwords-extract.log"`
Expected: first log line `ko=<n> en=<m> skipped=<k>` with n ≈ 1,000, m ≈ 700; the rest lists skipped mixed-script/digit patterns (e.g. `18놈`, `t발`).

Then run: `dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`
Expected: FAIL with "Run with BUN3_BADWORDS_REGEN=1" (data changed) — and **no** `ArgumentException`. An `ArgumentException` names the malformed entry: delete that line from the data file and rerun.

- [ ] **Step 4: Add the project allow additions and the default smoke tests**

Append to `Data~/ko.txt`:

```
# == project additions ==
!다시발
```

`common/src/com.bun3.badwords/Tests/Editor/DefaultDictionaryTests.cs`:

```csharp
using System;
using System.Diagnostics;
using NUnit.Framework;

namespace Bun3.BadWords.Tests
{
    public class DefaultDictionaryTests
    {
        [TestCase("시발")]
        [TestCase("ㅅㅂ")]
        [TestCase("tlqkf")]
        [TestCase("병신")]
        [TestCase("fuck")]
        [TestCase("b1tch")]
        [TestCase("f u c k")]
        [TestCase("fuuuck")]
        public void BlocksStrongWords(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.True, text);

        [TestCase("시발점")]
        [TestCase("다시 발견")]
        [TestCase("수박")]
        [TestCase("class")]
        [TestCase("assassin")]
        [TestCase("Scunthorpe")]
        [TestCase("but")]
        [TestCase("document")]
        [TestCase("accumulate")]
        [TestCase("탈출친구")]
        [TestCase("방장")]
        [TestCase("먼저 온 친구")]
        [TestCase("공룡조련사")]
        [TestCase("Player")]
        [TestCase("dinosaur")]
        [TestCase("Jurassic")]
        [TestCase("hello world")]
        [TestCase("가")]
        public void PassesOrdinaryText(string text) =>
            Assert.That(BadWordFilter.Default.ContainsBadWord(text), Is.False, BadWordFilter.Default.Explain(text));

        [Test]
        public void ReportsDefaultBuildCost()
        {
            long before = GC.GetTotalMemory(true);
            var watch = Stopwatch.StartNew();
            var filter = new BadWordFilter(BadWordFilter.DefaultEntries);
            watch.Stop();
            long retained = GC.GetTotalMemory(true) - before;
            GC.KeepAlive(filter);
            Assert.Pass("entries=" + BadWordFilter.DefaultEntries.Count + " buildMs=" + watch.Elapsed.TotalMilliseconds.ToString("F1") + " retainedBytes=" + retained);
        }
    }
}
```

Run: `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate` then `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: regeneration passes; in the full run `GeneratedDefaultIsUpToDate` passes and some `PassesOrdinaryText` cases may FAIL (e.g. `document` via `Latin:cum`) — those are resolved by the sweeps next.

- [ ] **Step 5: English false-positive sweep**

Run: `BUN3_BADWORDS_SWEEP="$SRC/en-common.txt" BUN3_BADWORDS_SWEEP_OUT="<scratchpad>/sweep-en.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus`

Every reported line is a false positive (the corpus excludes swears). For each reason in `sweep-en.tsv`:
- If one pattern produces more than 10 distinct false-positive words, change that pattern in `en.txt` to its boundary form (`|cum`, `|cum|`) instead of adding allows.
- Otherwise add `!<word>` lines for each sample word under a new section `# == false-positive sweep allows (google-10000-english no-swears) ==`.

Rerun the sweep until the report is empty or only lists words you consciously keep blocked (record those in Step 8).

- [ ] **Step 6: Korean false-positive sweep**

Run: `BUN3_BADWORDS_SWEEP="$SRC/nsmc-lines.txt" BUN3_BADWORDS_SWEEP_OUT="<scratchpad>/sweep-ko.tsv" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus`

The corpus contains real profanity, so judge each of the top 100 reasons by count: when the samples show the matched span inside an ordinary word or across a word gap (`다시 발견`, `하시 발라`), add the shortest covering allow (`!다시발`, `!하시발`) under `# == false-positive sweep allows (NSMC) ==` in `ko.txt`. Leave true profanity alone. Rerun once to confirm the reviewed reasons disappeared.

- [ ] **Step 7: doublems additions**

```bash
PYTHONIOENCODING=utf-8 python -I -c "import sys,pathlib; [print(l.strip()) for l in pathlib.Path(sys.argv[1]).read_text(encoding='utf-8').splitlines() if len(l.strip())>=2 and all(0xAC00<=ord(c)<=0xD7A3 for c in l.strip())]" "$SRC/kbw.md" > "$SRC/kbw-lines.txt"
BUN3_BADWORDS_SWEEP="$SRC/kbw-lines.txt" BUN3_BADWORDS_SWEEP_OUT="<scratchpad>/sweep-kbw.tsv" BUN3_BADWORDS_SWEEP_MISSES="<scratchpad>/kbw-misses.txt" dotnet test common/tests/Bun3.BadWords.Tests --filter SweepCorpus
```

From `kbw-misses.txt`, add only **strong** expressions (general profanity, sexual, parent insults, slurs — same bar as the spec's Decision 5; skip mild words and politics) to `ko.txt` under `# == doublems/korean-bad-words (MIT) @ 96d6ce489b64fcf258cd4c25b9a1b3a62b0f5558 ==`. Rerun Step 6's sweep afterwards to catch new false positives.

- [ ] **Step 8: Regenerate, run everything, record curation**

Run: `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate` then `dotnet test common/tests/Bun3.BadWords.Tests`
Expected: all PASS except `SweepCorpus` (Ignored). Read the `ReportsDefaultBuildCost` message. A `PassesOrdinaryText` case that still fails after the sweeps gets the shortest covering `!` entry (or a `|` boundary on the offending English pattern), then regenerate and rerun.

Append to `docs/research/2026-10-10-bad-word-filter-research.md` a section `## 0.1.0 큐레이션 기록` (Korean) with: final entry counts per file, skipped-pattern count and examples from the extract log, boundary conversions and allow counts from each sweep, doublems additions count, words deliberately kept blocked, and the build cost line (entries, ms, bytes).

- [ ] **Step 9: Owner spot check (human gate)**

Show the user the counts and `git diff --stat`, and ask them to skim `Data~/ko.txt` and `Data~/en.txt`. Apply their removals/additions, regenerate (Step 8 commands), and only then commit.

- [ ] **Step 10: Commit**

```bash
git add common/src/com.bun3.badwords common/tests/Bun3.BadWords.Tests docs/research/2026-10-10-bad-word-filter-research.md
git commit -m "🍱 비속어 필터 기본 한국어·영어 사전과 생성기 추가" -m "<body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Unity package integration, notices and README

**Files:**
- Create: `common/src/com.bun3.badwords/README.md`
- Create: `common/src/com.bun3.badwords/Third Party Notices.md`
- Modify: `unity/Packages/manifest.json` (dependency + `testables`)
- Modify: `unity/Packages/packages-lock.json` (Unity updates it)
- Create (Unity-generated): `.meta` files under `common/src/com.bun3.badwords` (not under `Data~`)

**Interfaces:**
- Consumes: the finished package from Tasks 1–5.
- Produces: a package Unity resolves from `file:../../common/src/com.bun3.badwords`, with green EditMode tests in Unity 6000.3.14f1.

- [ ] **Step 1: Write README and notices**

`common/src/com.bun3.badwords/README.md`:

````markdown
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
- Korean: digits, spaces and symbols between Hangul are dropped, and loose compatibility jamo are composed into syllables. Latin letters are also read as 2-beolsik keyboard input (`tlqkf`).
- English: leetspeak digits, separators as word boundaries, spaced single letters joined (`f u c k`), stretched letters (`fuuuck`).

## Limits

Not handled: initial-consonant projection of syllables, symbols imitating jamo, digit puns mixed into Korean words, two-letter splits (`fu ck`), self-censoring (`f*ck`), mathematical alphanumerics, conjoining jamo (pass NFC text). Korean entries may contain only Hangul and spaces.

## Default dictionary

Sources live in `Data~/` (not imported by Unity). `Runtime/BadWordFilter.Default.cs` is generated; regenerate with `BUN3_BADWORDS_REGEN=1 dotnet test common/tests/Bun3.BadWords.Tests --filter GeneratedDefaultIsUpToDate`. Bump the package version for any data change. Licenses: `Third Party Notices.md`.
````

`common/src/com.bun3.badwords/Third Party Notices.md`: one section per source — korcen (MIT, commit `eecd9763dbdccce3dc96ddb578ef0b6396058fa9`), doublems/korean-bad-words (MIT, `96d6ce489b64fcf258cd4c25b9a1b3a62b0f5558`), dsojevic/profanity-list (MIT, `c27924319aa9bd6f917e3782b4f4b6604a50b652`), obscenity (MIT, leet substitution table; pin the commit from `gh api repos/jo3-l/obscenity/commits/main --jq .sha`), Unicode confusables (Unicode License V3). Each section: name, URL@commit, what is used, and the **full license text copied verbatim** from:

```bash
curl -sL https://raw.githubusercontent.com/Tanat05/korcen/eecd9763dbdccce3dc96ddb578ef0b6396058fa9/LICENSE
curl -sL https://raw.githubusercontent.com/doublems/korean-bad-words/96d6ce489b64fcf258cd4c25b9a1b3a62b0f5558/LICENSE
curl -sL https://raw.githubusercontent.com/dsojevic/profanity-list/c27924319aa9bd6f917e3782b4f4b6604a50b652/LICENSE
curl -sL https://raw.githubusercontent.com/jo3-l/obscenity/<sha>/LICENSE
curl -sL https://www.unicode.org/license.txt
```

- [ ] **Step 2: Register the package in the bun3-kit Unity project**

In `unity/Packages/manifest.json` add `"com.bun3.badwords": "file:../../common/src/com.bun3.badwords",` as the first `com.bun3.*` dependency, and add `"com.bun3.badwords"` to `testables`.

- [ ] **Step 3: Run the EditMode tests in Unity (generates .meta files)**

Make sure no Unity Editor has `E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity` open, then run:

```bash
"E:/Unitys/6000.3.14f1/Editor/Unity.exe" -batchmode -nographics \
  -projectPath "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity" \
  -runTests -testPlatform EditMode -assemblyNames Bun3.BadWords.Tests \
  -testResults "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity/Logs/badwords-editmode.xml" \
  -logFile "E:/Projects/orca/workspace/bun3-kit/bad-word-filter/unity/Logs/badwords-editmode.log"
```

Expected: exit code 0; the XML's root `test-run` has `result="Passed"` and `failed="0"`; the test count equals the dotnet run minus the two dotnet-only tests in `DefaultEntriesGenerator`. Check the log for `warning CS` lines in `Bun3.BadWords*` assemblies — there must be none. First import of a fresh worktree can take several minutes.

- [ ] **Step 4: Verify generated files**

Run: `git status --short` and `git check-ignore -v unity/Logs/badwords-editmode.xml`
Expected: new `.meta` files for the package root items, `Runtime/`, `Tests/`, `Tests/Editor/` and each file (none for `Data~`); `manifest.json` and `packages-lock.json` modified; the Logs path is ignored.

- [ ] **Step 5: Commit**

```bash
git add common/src/com.bun3.badwords unity/Packages/manifest.json unity/Packages/packages-lock.json
git commit -m "📦 비속어 필터 패키지 Unity 등록과 라이선스 고지 추가" -m "<body>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## After the last task

Use `superpowers:finishing-a-development-branch`. Merging `Bun3/bad-word-filter` into `main` and pushing need the user's explicit approval — the jurassic-paradise integration plan depends on the package being reachable from `https://github.com/Bun3/bun3-kit.git` `main`.
