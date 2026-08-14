# GameplayTag Sources and Catalog Distribution Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 게임·패키지 JSON과 C# Native 태그를 Source별로 작성하면서 하나의 결정론적 `B3DK` Runtime Catalog로 병합하고, Unity Editor·로컬 서버·배포 서버가 같은 Catalog를 안전하게 소비하게 한다.

**Architecture:** `Bun3.Gameplay`은 immutable Runtime Catalog와 binary reader만 소유하고, 새 `Bun3.Gameplay.Catalog` assembly가 Source parsing, merge, provenance와 binary writer를 소유한다. Unity Editor는 이 compiler 위에 Source별 Workspace와 merged Picker를 투영하며, `bun3-tags` CLI와 `Bun3.Server.GameplayTags`는 동일 binary contract를 각각 개발 cache와 Generic Host 경계에 연결한다.

**Tech Stack:** C# 9, netstandard2.1, Newtonsoft.Json 13.0.2, Roslyn analyzer 4.8.0, .NET 10 CLI/Generic Host, Unity 2022.3+ IMGUI TreeView, NUnit 4.1, SHA-256.

## Global Constraints

- 기반 명세는 `docs/superpowers/specs/2026-08-14-gameplay-tag-sources-design.md`이며 충돌 시 명세가 우선한다.
- `Bun3.Gameplay`과 `Bun3.Gameplay.Catalog`은 netstandard2.1, C# 9, nullable enable을 유지한다.
- Runtime assembly는 UnityEngine, UnityEditor, Microsoft.Extensions.Hosting과 package registry를 참조하지 않는다.
- 모든 public member에 한국어 XML 문서를 작성하고 모든 Release build를 warning 0으로 유지한다.
- 태그 이름은 읽기·작성·조회에서 `TagName.TryFold`/`ToLowerInvariant()`로 canonical 소문자화한다.
- `0`은 `GameplayTag.None`, 유효 태그 index는 결정론적 preorder의 `1..65,535`다.
- Runtime artifact는 `B3DK` magic을 가진 단일 `GameplayTags.catalog`이며 JSON sidecar나 checksum sidecar를 배포하지 않는다.
- Source, comment, read-only 상태는 Semantic Fingerprint와 runtime payload에 포함하지 않는다.
- Development Version은 정확히 `0.0.0-dev`; 게시 Version은 저장 시 자동 증가시키지 않는다.
- 파일 교체는 같은 디렉터리의 임시 파일, flush, atomic replace 순서를 사용한다.
- runtime/editor failure는 이전 Catalog로 조용히 fallback하지 않고 popup 또는 전용 예외로 차단한다.
- `Bun3.Gameplay` NuGet/UPM version은 구현 완료 시 `0.8.0`에서 `0.9.0`으로 한 번만 올린다.
- 기존 public `TagCatalog.Load(Stream)` JSON API는 한 compatibility release 동안 `[Obsolete]`로 유지한다.
- 사용자 소유 `.superpowers/`와 `unity/GameplayTags.json`은 절대 stage, 삭제, 이동 또는 수정하지 않는다.
- Unity test runner가 만든 기존 사용자 Unity process를 종료하지 않는다.
- 각 commit은 gitmoji 제목과 `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` trailer를 사용한다.

---

## File and Assembly Map

### Runtime: `Bun3.Gameplay`

- `Runtime/Tags/TagCatalog.cs`: immutable name/index/hierarchy/redirect storage and compatibility metadata.
- `Runtime/Tags/TagCatalogBinary.cs`: strict `B3DK` reader only.
- `Runtime/Tags/TagCatalogExpectations.cs`: Development/Published compatibility policy.
- `Runtime/Tags/TagCatalogFormatException.cs`: structural/corruption failure.
- `Runtime/Tags/TagCatalogCompatibilityException.cs`: ID/version/fingerprint mismatch.
- `Runtime/Tags/GameplayTagSourceAttribute.cs`, `NativeGameplayTagAttribute.cs`: compile-time Native declaration surface.

### Authoring tooling: `Bun3.Gameplay.Catalog`

- `Catalog/Source/*.cs`: Source descriptor/document/tag/redirect and strict JSON codecs.
- `Catalog/Compiler/*.cs`: deterministic merge, diagnostics, provenance and compilation result.
- `Catalog/Binary/TagCatalogBinaryWriter.cs`: deterministic `B3DK` writer.
- `Runtime/Tags/TagCatalogDevelopmentPath.cs`: shared path calculation only; adapters still own file IO.

The UPM package contains both asmdefs. The `Bun3.Gameplay` NuGet package includes both DLLs under
`lib/netstandard2.1`; the Catalog assembly remains a separate dependency boundary inside the same package.

### Native declaration tooling

- `common/src/Bun3.Gameplay.TagSourceAnalyzer`: Roslyn diagnostic analyzer.
- `common/src/Bun3.Gameplay.Tags.Cli`: `compile`, `extract-native`, `inspect` commands.
- `common/src/Bun3.Gameplay.TagSource.Tasks`: build-only Roslyn extractor and MSBuild task; it has no Runtime project reference.
- `common/src/com.bun3.gameplay/buildTransitive/Bun3.Gameplay.NativeTags.targets`: package-build hook that invokes the packaged MSBuild task and adds generated Source Metadata to package output.

### Unity Editor

- Existing `Editor/Tags` session/controller/window/tree files are adapted, not wholesale replaced.
- New Workspace, build-context provider, fixed-path, picker, play gate and build processor files each hold one responsibility.
- All new Unity `.cs` and `.asmdef` files include their generated `.meta` files in the same task.

### Native .NET server

- New `server/src/Bun3.Server.GameplayTags` package owns options, path resolution, DI registration and startup loading.
- It depends on `Bun3.Gameplay` and Microsoft Generic Host only; it does not depend on Catalog authoring, RPC, transport or game code.

---

### Task 1: Establish the Catalog tooling assembly and strict Source documents

**Files:**
- Create: `common/src/com.bun3.gameplay/Catalog/Bun3.Gameplay.Catalog.csproj`
- Create: `common/src/com.bun3.gameplay/Catalog/Bun3.Gameplay.Catalog.asmdef`
- Create: `common/src/com.bun3.gameplay/Catalog/AssemblyInfo.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceKind.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceDescriptor.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceTag.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceRedirect.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceDocument.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Source/TagSourceJson.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagSourceJsonTests.cs`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Bun3.Gameplay.Editor.asmdef`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/Bun3.Gameplay.Tests.asmdef`
- Modify: `common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
- Modify: `Bun3.sln`
- Create: corresponding Unity `.meta` files under `Catalog/`

**Interfaces:**
- Consumes: existing `Bun3.Gameplay.Tags.TagName` through `InternalsVisibleTo("Bun3.Gameplay.Catalog")`.
- Produces:
  - `TagSourceDescriptor(string sourceId, string displayName, TagSourceKind kind, bool isReadOnly)`
  - `TagSourceDocument(TagSourceDescriptor descriptor, string origin, IReadOnlyList<TagSourceTag> tags, IReadOnlyList<TagSourceRedirect> redirects)`
  - `TagSourceJson.LoadGame(Stream json, string origin) : TagSourceDocument`
  - `TagSourceJson.LoadMetadata(Stream json, string origin) : TagSourceDocument`
  - `TagSourceJson.WriteGame(Stream destination, TagSourceDocument document) : void`
  - `TagSourceJson.WriteMetadata(Stream destination, TagSourceDocument document) : void`

- [ ] **Step 1: Add failing Source codec tests**

```csharp
[Test]
public void Game_source_is_fixed_editable_and_normalizes_names_on_write()
{
    using var input = Utf8("{\"schemaVersion\":1,\"tags\":[{\"name\":\"Ability.Jump\",\"comment\":\"점프\"}],\"redirects\":[]}");
    var source = TagSourceJson.LoadGame(input, "ProjectSettings/GameplayTags.json");

    Assert.That(source.Descriptor.SourceId, Is.EqualTo("game"));
    Assert.That(source.Descriptor.IsReadOnly, Is.False);
    using var output = new MemoryStream();
    TagSourceJson.WriteGame(output, source);
    Assert.That(Utf8Text(output), Does.Contain("ability.jump"));
    Assert.That(Utf8Text(output), Does.Not.Contain("Ability.Jump"));
}

[Test]
public void Metadata_requires_source_identity_and_preserves_per_source_comment()
{
    using var input = Utf8("{\"schemaVersion\":1,\"source\":{\"id\":\"bun3.gameplay\",\"displayName\":\"Bun3.Gameplay\",\"kind\":\"packageJson\"},\"tags\":[{\"name\":\"ability.jump\",\"comment\":\"framework\"}],\"redirects\":[]}");
    var source = TagSourceJson.LoadMetadata(input, "Packages/com.bun3.gameplay/Bun3/GameplayTags/TagSource.json");
    Assert.That(source.Tags.Single().Comment, Is.EqualTo("framework"));
    Assert.That(source.Descriptor.IsReadOnly, Is.True);
}
```

Also assert rejection of unsupported `schemaVersion`, unknown or duplicate JSON properties, a missing `tags` array,
duplicate canonical tag rows, duplicate redirect sources, invalid names, an editable metadata descriptor, invalid UTF-8
and trailing JSON tokens. For legacy authoring input only, an omitted `redirects` property reads as an empty array; every
new write emits it. Empty `tags` and `redirects` arrays express an intentionally empty Game Source.

- [ ] **Step 2: Run the focused tests and capture RED**

Run:

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagSourceJsonTests'
```

Expected: compile failure because `Bun3.Gameplay.Catalog` and Source types do not exist.

- [ ] **Step 3: Add the assembly boundary and exact public contracts**

Use namespace `Bun3.Gameplay.Tags.Catalog`. `TagSourceKind` has exactly `GameJson`, `PackageJson`, `Native`.
Validate Source ID with `^[a-z0-9]+(?:[.-][a-z0-9]+)*$`; reject empty display names; copy all incoming lists to
arrays and expose them as `IReadOnlyList<T>`. `GameJson` requires Source ID `game` and `IsReadOnly == false`; `game` is
reserved from the other kinds, and `PackageJson` and `Native` require `IsReadOnly == true`.

The metadata JSON schema is exactly:

```json
{
  "schemaVersion": 1,
  "source": {
    "id": "bun3.gameplay",
    "displayName": "Bun3.Gameplay",
    "kind": "packageJson"
  },
  "tags": [{ "name": "ability.jump", "comment": "framework" }],
  "redirects": [{ "from": "ability.oldjump", "to": "ability.jump" }]
}
```

Tag names still use the existing alphanumeric segment grammar. Game JSON omits `source`; `LoadGame` injects descriptor
`("game", "Game", GameJson, false)`. `origin` is a diagnostic path/declaration label, never identity or fingerprint.
All writers emit UTF-8 without BOM, two-space indentation, final newline,
ordinal tag/redirect order and canonical lowercase names.

- [ ] **Step 4: Wire project references without leaking Catalog into Runtime**

`Bun3.Gameplay.csproj` removes `Catalog/**/*.cs` from compile. It must not ProjectReference Catalog because Catalog
references Runtime; instead its pack-only target invokes the already-restored Catalog project after Runtime Build and
adds the resulting DLL/PDB/XML to the same NuGet `lib/netstandard2.1` folder. `Bun3.Gameplay.Catalog.csproj` references
`../Bun3.Gameplay.csproj`, pins `Newtonsoft.Json [13.0.2]` and sets `IsPackable=false`; only the root package owns the
artifact. `Runtime/AssemblyInfo.cs` grants
`InternalsVisibleTo("Bun3.Gameplay.Catalog")`. The Catalog asmdef has `noEngineReferences: true` and references
`Bun3.Gameplay`; it is restricted to `includePlatforms: ["Editor"]` so authoring compiler/writer code is absent from
Unity players. Editor/test asmdefs additionally reference `Bun3.Gameplay.Catalog`.

- [ ] **Step 5: Run focused tests and both assembly builds**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagSourceJsonTests'
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --no-restore --warnaserror
dotnet build common/src/com.bun3.gameplay/Catalog/Bun3.Gameplay.Catalog.csproj -c Release --no-restore --warnaserror
```

Expected: all tests pass; both builds report 0 warnings and 0 errors.

- [ ] **Step 6: Commit the Source document boundary**

```powershell
git add -- Bun3.sln common/src/com.bun3.gameplay common/tests/Bun3.Gameplay.Tests
git commit -m "✨ GameplayTag Source 문서 모델 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Compile multiple Sources into one runtime Catalog and provenance index

**Files:**
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagCatalogDiagnosticSeverity.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagCatalogDiagnostic.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagSourceContribution.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagCatalogProvenance.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagCatalogCompilation.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/TagCatalogCompiler.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/CatalogBuildMode.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Compiler/GameCatalogBuildContext.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogIdentity.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogCompiledRedirect.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogCompilerTests.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/AssemblyInfo.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Build.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Fingerprint.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCatalogFingerprintTests.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 1 `TagSourceDocument` and existing `TagCatalog` query surface.
- Produces:
  - `TagCatalogIdentity(string catalogId, string catalogVersion)`
  - `TagCatalogCompiler.Compile(IReadOnlyList<TagSourceDocument> sources, TagCatalogIdentity identity) : TagCatalogCompilation`
  - `GameCatalogBuildContext(TagCatalogIdentity identity, CatalogBuildMode mode, IReadOnlyList<TagSourceDocument> sources)`
  - `TagCatalogCompilation.Succeeded`, `.Catalog`, `.Provenance`, `.Diagnostics`
  - `TagCatalogProvenance.GetContributions(string canonicalName) : IReadOnlyList<TagSourceContribution>`
  - `TagSourceContribution.SourceId`, `.DisplayName`, `.Origin`, `.Comment`, `.IsExplicit`, `.IsReadOnly`
  - `TagCatalogDiagnostic` fields `Code`, `Severity`, `SourceId`, `Origin`, `CanonicalPath`, `Message`
  - internal `TagCatalog.CreateCompiled(TagCatalogIdentity identity, string[] canonicalNames, ushort[] parents, ushort[] subtreeEnds, IReadOnlyList<CompiledRedirect> redirects) : TagCatalog`

- [ ] **Step 1: Write compiler RED tests for union, implicit parents and comment provenance**

```csharp
[Test]
public void Same_tag_from_two_sources_has_one_runtime_identity_and_two_comments()
{
    var result = TagCatalogCompiler.Compile(new[] {
        Source("game", false, Tag("Ability.Jump", "game")),
        Source("bun3.gameplay", true, Tag("ability.jump", "framework"))
    }, new TagCatalogIdentity("test-game", "0.0.0-dev"));

    Assert.That(result.Succeeded, Is.True);
    Assert.That(result.Catalog!.Count, Is.EqualTo(2));
    Assert.That(result.Catalog.GetDisplayName(result.Catalog.GetRequired("ABILITY.JUMP")),
        Is.EqualTo("ability.jump"));
    Assert.That(result.Provenance!.GetContributions("ability.jump").Select(x => x.Comment),
        Is.EqualTo(new[] { "framework", "game" }));
}

[Test]
public void Removing_one_source_keeps_a_tag_contributed_by_another_source()
{
    var both = Compile(Source("a", true, Tag("state.dead")), Source("b", true, Tag("state.dead")));
    var one = Compile(Source("b", true, Tag("state.dead")));
    Assert.That(both.Catalog!.TryGet("state.dead", out _), Is.True);
    Assert.That(one.Catalog!.TryGet("state.dead", out _), Is.True);
}
```

- [ ] **Step 2: Write redirect RED tests**

Cover identical mapping union, divergent target error, chain flattening, cycle, missing target and an active old name.
The active old-name case must succeed with diagnostic code `B3TAG2001` and severity `Warning`; lookup must return the
active tag until that declaration is removed.

Use these stable compiler diagnostics:

```text
B3TAG1001 Duplicate Source ID
B3TAG1002 Runtime tag capacity exceeds 65,535
B3TAG2001 Redirect is shadowed by an active old name (Warning)
B3TAG2002 Conflicting targets for one redirect source
B3TAG2003 Redirect self-reference or cycle
B3TAG2004 Redirect target is not active
```

Add a capacity test whose implicit parents push the union over 65,535 and assert `B3TAG1002` without allocating a
runtime Catalog.

- [ ] **Step 3: Run focused tests to verify RED**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagCatalogCompilerTests'
```

Expected: compile failure for the missing compiler/result/provenance types.

- [ ] **Step 4: Implement deterministic compile phases**

Implement these phases in this exact order:

```text
Validate unique SourceId
Canonicalize every tag and redirect endpoint
Build per-source explicit/implicit contribution sets
Union active canonical paths
Sort children with StringComparer.Ordinal
Assign preorder ushort indices
Merge and recursively flatten redirects
Emit B3TAG2001 for redirect sources shadowed by active tags
Compute schema-2 semantic fingerprint
Create immutable TagCatalog and provenance arrays
```

Compilation never throws for authoring errors. It returns `Catalog = null`, `Provenance = null`, stable diagnostics
sorted by SourceId, canonical path and code. API misuse (`sources == null`, null source, invalid identity) throws normal
argument exceptions. Comments never participate in merge conflict or fingerprint calculation.

`CatalogBuildMode` has exactly `Development` and `Published`. `GameCatalogBuildContext` rejects an identity whose Version
does not equal `0.0.0-dev` in Development mode; Published mode requires a non-empty Version other than
`0.0.0-dev`. Version changes remain an explicit release/build-context choice, never an editor-save side effect.

- [ ] **Step 5: Make runtime display names canonical and fingerprint hierarchy-explicit**

`TagCatalog.GetDisplayName` remains source-compatible but now returns the canonical lowercase name. Fingerprint schema 2
writes, for every index, canonical name, parent index and subtree end, then flattened redirects sorted by old name.
Update the existing golden hash only after asserting a second compile with reversed Source input produces the same hash.

- [ ] **Step 6: Run compiler, legacy and conformance tests**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagCatalogCompilerTests|FullyQualifiedName~TagCatalogFingerprintTests|FullyQualifiedName~TagCatalogConformanceTests|FullyQualifiedName~TagCatalogRedirectTests'
```

Expected: all selected tests pass and existing container semantics remain unchanged.

- [ ] **Step 7: Commit the compiler**

```powershell
git add -- common/src/com.bun3.gameplay common/tests/Bun3.Gameplay.Tests
git commit -m "✨ GameplayTag Source 병합 컴파일러 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Add the deterministic B3DK binary writer and strict runtime reader

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogBinary.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogExpectations.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogFormatException.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogCompatibilityException.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/Binary/TagCatalogBinaryWriter.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogBinaryTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tests/TagCatalogBinaryCorruptionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalog.Json.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogFileAdapter.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayUnitySmokeTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/TagCatalogConformanceTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Runtime/TagPerformanceFixture.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCatalogAllocationTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCatalogLoadingTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCatalogTestData.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 2 successful `TagCatalogCompilation.Catalog`.
- Produces:
  - `TagCatalogBinaryWriter.Write(Stream output, TagCatalog catalog) : void`
  - `TagCatalogBinary.Load(Stream input, TagCatalogExpectations expectations) : TagCatalog`
  - `TagCatalogExpectations.ForDevelopment(string catalogId)`
  - `TagCatalogExpectations.ForPublished(string catalogId, string catalogVersion, ReadOnlySpan<byte> expectedFingerprint)`
  - `TagCatalog.CatalogId`, `TagCatalog.CatalogVersion`

- [ ] **Step 1: Write byte determinism and round-trip RED tests**

```csharp
[Test]
public void Same_semantic_input_writes_identical_b3dk_bytes()
{
    var first = WriteBinary(Compile(SourcesInOrderA()).Catalog!);
    var second = WriteBinary(Compile(SourcesInOrderB()).Catalog!);
    Assert.That(first.Take(4).ToArray(), Is.EqualTo(Encoding.ASCII.GetBytes("B3DK")));
    Assert.That(second, Is.EqualTo(first));
}

[Test]
public void Published_round_trip_requires_external_expected_fingerprint()
{
    var original = CompileGame("game-a", "1.4.0").Catalog!;
    using var bytes = new MemoryStream(WriteBinary(original));
    var loaded = TagCatalogBinary.Load(bytes,
        TagCatalogExpectations.ForPublished("game-a", "1.4.0", original.Fingerprint));
    Assert.That(loaded.GetRequired("ability.jump").Index,
        Is.EqualTo(original.GetRequired("ability.jump").Index));
}
```

- [ ] **Step 2: Write one corruption test per validation stage**

Tests mutate magic, schema, each length, UTF-8, checksum, duplicate/out-of-order tag index, duplicate/non-canonical tag
name, parent, subtree end, duplicate/out-of-order redirect source and redirect target. Structural cases expect
`TagCatalogFormatException`; ID/version/fingerprint mismatch expects `TagCatalogCompatibilityException`. Also verify a
non-seekable readable stream succeeds and a trailing byte fails.

- [ ] **Step 3: Run binary tests to verify RED**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagCatalogBinaryTests|FullyQualifiedName~TagCatalogBinaryCorruptionTests'
```

Expected: compile failure for the missing writer/reader/exception types.

- [ ] **Step 4: Implement the exact schema-1 file layout**

Use the offsets from the approved spec. All integers are little-endian; names are strict UTF-8 with `UInt16` byte
length; tag and redirect counts are `UInt32`; entries use `UInt16` indices. Buffer one file in memory, leave bytes
`46..77` zero while hashing the whole file, then write the SHA-256 checksum into that field. The reader repeats that
procedure and uses fixed upper bounds derived from file length before allocating arrays.

- [ ] **Step 5: Implement explicit expectation modes**

Development requires exact Catalog ID and exact Version `0.0.0-dev`, but no predetermined fingerprint. Published
requires exact ID, Version and 32-byte expected fingerprint. The expected fingerprint is copied in the factory; no
public mutable array is exposed.

- [ ] **Step 6: Deprecate, but do not repurpose, JSON Load**

Add:

```csharp
[Obsolete("JSON 로딩은 작성 도구 호환용입니다. 런타임에서는 TagCatalogBinary.Load를 사용하세요.", false)]
public static TagCatalog Load(Stream utf8Json)
```

It must continue parsing JSON. Passing JSON to `TagCatalogBinary.Load` fails the magic check; passing binary to
`TagCatalog.Load` fails JSON parsing. Add `#pragma warning disable CS0618` only around tests and legacy editor adapter
calls that intentionally exercise this path. A legacy JSON-loaded Catalog has empty Catalog ID and Version; add a test
that `TagCatalogBinaryWriter.Write` rejects it with `InvalidOperationException`, so distributable binaries can only be
created from the compiler path with an explicit identity.

- [ ] **Step 7: Run full runtime tests and warning-zero builds**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --no-restore --warnaserror
dotnet build common/src/com.bun3.gameplay/Catalog/Bun3.Gameplay.Catalog.csproj -c Release --no-restore --warnaserror
```

- [ ] **Step 8: Commit the binary contract**

```powershell
git add -- common/src/com.bun3.gameplay common/tests/Bun3.Gameplay.Tests
git commit -m "✨ B3DK GameplayTag Catalog 포맷 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Validate C# Native tag declarations and extract read-only metadata

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/GameplayTagSourceAttribute.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/NativeGameplayTagAttribute.cs`
- Create: `common/src/Bun3.Gameplay.TagSourceAnalyzer/Bun3.Gameplay.TagSourceAnalyzer.csproj`
- Create: `common/src/Bun3.Gameplay.TagSourceAnalyzer/NativeGameplayTagAnalyzer.cs`
- Create: `common/src/Bun3.Gameplay.TagSourceAnalyzer/NativeGameplayTagDiagnostics.cs`
- Create: `common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests/Bun3.Gameplay.TagSourceAnalyzer.Tests.csproj`
- Create: `common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests/NativeGameplayTagAnalyzerTests.cs`
- Modify: `Bun3.sln`
- Create: corresponding Unity `.meta` files for the two Runtime attribute files

**Interfaces:**
- Consumes: existing tag grammar and Task 1 Native metadata schema.
- Produces:
  - `[assembly: GameplayTagSource(string sourceId, string displayName)]`
  - `[NativeGameplayTag(string comment = "")]` on `public const string` fields
  - diagnostics `B3TAG0001` through `B3TAG0005`

- [ ] **Step 1: Write analyzer RED tests with an in-memory CSharpCompilation**

Test valid declaration, missing assembly Source, non-public/non-const/non-string field, invalid path, duplicate canonical
path and multiple assembly Source declarations. Exact diagnostics are:

```text
B3TAG0001 Native tag declaration requires exactly one assembly GameplayTagSource attribute
B3TAG0002 NativeGameplayTag may annotate only const string fields
B3TAG0003 Native gameplay tag name is invalid
B3TAG0004 Native gameplay tag is duplicated after canonicalization
B3TAG0005 GameplayTag Source ID or display name is invalid
```

- [ ] **Step 2: Run analyzer tests and capture RED**

```powershell
dotnet test common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests/Bun3.Gameplay.TagSourceAnalyzer.Tests.csproj -c Release
```

Expected: compile failure because the analyzer and attributes do not exist.

- [ ] **Step 3: Implement conditional authoring attributes**

Apply `[Conditional("BUN3_GAMEPLAY_TAGS_AUTHORING")]` to both attribute classes so usages are available to source
analysis but omitted from normal runtime assembly metadata unless the authoring symbol is explicitly enabled. The const
string remains the framework runtime lookup key.

- [ ] **Step 4: Implement analyzer validation without file IO**

Target netstandard2.0, pin `Microsoft.CodeAnalysis.CSharp` to `[4.8.0]` with `PrivateAssets=all`, register one
`CompilationStartAction`, collect annotated `IFieldSymbol`s, and report duplicates in `CompilationEndAction` using
`StringComparer.Ordinal` after canonical lowercase conversion. Analyzer execution must be deterministic and side-effect
free. Set the Analyzer project `IsPackable=false`; the root Gameplay package explicitly places its DLL under
`analyzers/dotnet/cs`.

- [ ] **Step 5: Run analyzer tests and warning-zero build**

```powershell
dotnet test common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests/Bun3.Gameplay.TagSourceAnalyzer.Tests.csproj -c Release
dotnet build common/src/Bun3.Gameplay.TagSourceAnalyzer/Bun3.Gameplay.TagSourceAnalyzer.csproj `
  -c Release --no-restore --warnaserror
```

- [ ] **Step 6: Commit Native declarations and analyzer**

```powershell
git add -- Bun3.sln common/src/Bun3.Gameplay.TagSourceAnalyzer `
  common/src/com.bun3.gameplay/Runtime/Tags common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests
git commit -m "✨ Native GameplayTag 선언 검증 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Add the bun3-tags CLI, Native extraction and local development cache

**Files:**
- Create: `common/src/Bun3.Gameplay.Tags.Cli/Bun3.Gameplay.Tags.Cli.csproj`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/AssemblyInfo.cs`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/Program.cs`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/CliArguments.cs`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/CompileCommand.cs`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/ExtractNativeCommand.cs`
- Create: `common/src/Bun3.Gameplay.Tags.Cli/InspectCommand.cs`
- Create: `common/src/Bun3.Gameplay.TagSource.Tasks/Bun3.Gameplay.TagSource.Tasks.csproj`
- Create: `common/src/Bun3.Gameplay.TagSource.Tasks/NativeTagMetadataExtractor.cs`
- Create: `common/src/Bun3.Gameplay.TagSource.Tasks/ExtractNativeGameplayTagsTask.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogDevelopmentPath.cs`
- Create: `common/src/com.bun3.gameplay/Catalog/IO/AtomicFileWriter.cs`
- Create: `common/tests/Bun3.Gameplay.Tags.Cli.Tests/Bun3.Gameplay.Tags.Cli.Tests.csproj`
- Create: `common/tests/Bun3.Gameplay.Tags.Cli.Tests/GameplayTagCliTests.cs`
- Create: `common/tests/Bun3.Gameplay.Tags.Cli.Tests/Fixtures/NativePackage/NativePackage.csproj`
- Create: `common/tests/Bun3.Gameplay.Tags.Cli.Tests/Fixtures/NativePackage/NativeTags.cs`
- Create: `common/src/com.bun3.gameplay/buildTransitive/Bun3.Gameplay.NativeTags.targets`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `Bun3.sln`
- Create: corresponding Unity `.meta` files for `TagCatalogDevelopmentPath.cs`, `AtomicFileWriter.cs` and the packaged `.targets` file

**Interfaces:**
- Consumes: Task 1 metadata codec, Task 2 compiler, Task 3 writer, Task 4 attributes.
- Produces:
  - `bun3-tags compile --development --catalog-id <id> --project-root <dir> [--source <metadata>]...`
  - `bun3-tags compile --published --catalog-id <id> --catalog-version <version> --project-root <dir> --output <file> [--source <metadata>]...`
  - `bun3-tags extract-native --output <metadata.json> <source.cs> [<source.cs>]...`
  - `bun3-tags inspect <GameplayTags.catalog>`
  - `TagCatalogDevelopmentPath.Get(string catalogId, string? localApplicationDataOverride = null) : string`

- [ ] **Step 1: Write CLI RED integration tests**

Invoke `Program.Run(string[] args, TextWriter stdout, TextWriter stderr)` directly. Verify development compile reads
`<project-root>/ProjectSettings/GameplayTags.json`, merges two `--source` files, writes the exact OS cache path, and
does not replace a previous good file when compile fails. Verify `inspect` prints Catalog ID, Version, lowercase
fingerprint and counts. Keep `Program.Run` internal and grant `InternalsVisibleTo("Bun3.Gameplay.Tags.Cli.Tests")` from
the CLI `AssemblyInfo.cs`. Published compile also reads the fixed `<project-root>/ProjectSettings/GameplayTags.json`;
it fails when that file is absent and never accepts an arbitrary Game Source path.

- [ ] **Step 2: Write Native extraction RED tests**

Provide two C# files containing the assembly attribute and annotated constants. `extract-native` must output one strict
Task 1 metadata JSON with `kind: "native"`, canonical lowercase paths and source-order-independent rows. Invalid or
non-constant declarations return exit code `2`, print diagnostics and leave the destination untouched.

- [ ] **Step 3: Run CLI tests and capture RED**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tags.Cli.Tests/Bun3.Gameplay.Tags.Cli.Tests.csproj -c Release
```

- [ ] **Step 4: Implement commands with no command-line framework dependency**

Parse the four exact commands/options above, reject unknown/duplicate singleton options, and return exit codes `0`
success, `1` usage, `2` validation/compile, `3` IO. Development output defaults to:

```text
Environment.SpecialFolder.LocalApplicationData/Bun3/GameplayTags/<catalog-id>/dev/GameplayTags.catalog
```

`--source` means a resolved metadata file, not an arbitrary tag JSON. The existing game resource/dependency system is
responsible for supplying server-only package metadata paths; the CLI does not invent a second committed manifest.
The CLI targets `net10.0`, sets `PackAsTool=true`, `ToolCommandName=bun3-tags` and Version `0.1.0`, and references Catalog
and Tasks. Tasks targets `netstandard2.0` and pins private build dependencies `Microsoft.CodeAnalysis.CSharp [4.8.0]`,
`Microsoft.Build.Framework [17.8.3]`, `Microsoft.Build.Utilities.Core [17.8.3]` and `Newtonsoft.Json [13.0.2]`.
Tasks sets `IsPackable=false`; the root Gameplay package owns its build-task distribution.

- [ ] **Step 5: Implement atomic output and package build hook**

`AtomicFileWriter` writes beside the destination, flushes, verifies the newly written binary by reading it back, and
only then replaces the destination. `NativeTagMetadataExtractor` lives in the build-only Tasks assembly and recognizes
the authoring attributes by fully qualified metadata name, so that project must not reference `Bun3.Gameplay` and cannot
form a project cycle. The CLI `extract-native` command and MSBuild task both call this same extractor.

`Bun3.Gameplay.NativeTags.targets` runs `ExtractNativeGameplayTagsTask` before pack when a project sets
`<Bun3GameplayTagSource>true</Bun3GameplayTagSource>` and adds the resulting metadata under
`contentFiles/any/any/Bun3/GameplayTags/TagSource.json`. The task receives the evaluated `@(Compile)`, `@(ReferencePath)`
and `$(TargetPath)` items directly from MSBuild, so paths containing spaces never pass through a shell. The root
`Bun3.Gameplay.csproj` references Analyzer and Tasks with `ReferenceOutputAssembly="false"` and packs their DLLs plus
the target under `analyzers/dotnet/cs` and `buildTransitive`; neither project references Runtime.

For Published output, read an existing destination before replacement. If it has the same Catalog ID and Version, an
identical checksum is an idempotent success and any different checksum or fingerprint is exit code `2`; never overwrite
that immutable identity. Artifact-registry immutability remains the publishing adapter's matching external gate.

- [ ] **Step 6: Run CLI tests and a real pack fixture**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tags.Cli.Tests/Bun3.Gameplay.Tags.Cli.Tests.csproj -c Release
dotnet pack common/tests/Bun3.Gameplay.Tags.Cli.Tests/Fixtures/NativePackage/NativePackage.csproj `
  -c Release -o artifacts/tag-source-fixture
```

Read the fixture nupkg as ZIP and assert exactly one `TagSource.json` with canonical tags. Do not publish it.

- [ ] **Step 7: Commit the CLI and build hook**

```powershell
git add -- Bun3.sln common/src/Bun3.Gameplay.Tags.Cli common/src/Bun3.Gameplay.TagSource.Tasks `
  common/src/com.bun3.gameplay/Catalog common/src/com.bun3.gameplay/buildTransitive `
  common/src/com.bun3.gameplay/Bun3.Gameplay.csproj common/tests/Bun3.Gameplay.Tags.Cli.Tests
git commit -m "✨ GameplayTag Catalog 개발 CLI 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 6: Add Native .NET server loading before hosted gameplay starts

**Files:**
- Create: `server/src/Bun3.Server.GameplayTags/Bun3.Server.GameplayTags.csproj`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagCatalogMode.cs`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagCatalogOptions.cs`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagCatalogPathResolver.cs`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagCatalogLoader.cs`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagCatalogStartupService.cs`
- Create: `server/src/Bun3.Server.GameplayTags/GameplayTagServiceCollectionExtensions.cs`
- Create: `server/tests/Bun3.Server.Tests/GameplayTagHostingTests.cs`
- Modify: `server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj`
- Modify: `Bun3.sln`

**Interfaces:**
- Consumes: Task 3 runtime reader and Task 5 development path contract.
- Produces:
  - `GameplayTagCatalogOptions.SectionName = "Bun3:GameplayTags"`
  - `IServiceCollection.AddGameplayTagCatalog(Action<GameplayTagCatalogOptions>? configure = null)`
  - singleton `TagCatalog` resolved before any gameplay `IHostedService.StartAsync`

The new server package targets `net10.0`, starts at Version `0.1.0`, references Runtime through the repository
ProjectReference, and pins `Microsoft.Extensions.Hosting [10.0.0]` like the existing server hosting package.

- [ ] **Step 1: Write hosting RED tests**

```csharp
[Test]
public async Task Development_host_loads_shared_cache_before_gameplay_service_starts()
{
    using var fixture = CatalogFixture.Development("server-game");
    var starts = 0;
    using var host = BuildHost(fixture.LocalAppData, options => {
        options.Mode = GameplayTagCatalogMode.LocalDevelopment;
        options.CatalogId = "server-game";
    }, () => starts++);

    await host.StartAsync();
    Assert.That(host.Services.GetRequiredService<TagCatalog>().CatalogId, Is.EqualTo("server-game"));
    Assert.That(starts, Is.EqualTo(1));
}
```

Also test missing, corrupt and mismatched Catalog: `StartAsync` throws the exact format/compatibility exception and a
later gameplay hosted service observes `starts == 0`. Test `BUN3_GAMEPLAY_TAG_CATALOG_PATH` only in
LocalDevelopment mode; Packaged mode ignores it and requires exact ID, Version and fingerprint configuration.
The server test project adds a test-only ProjectReference to `Bun3.Gameplay.Catalog.csproj`, allowing `CatalogFixture`
to compile Source documents and write real B3DK fixtures without adding Catalog authoring code to the server package.

- [ ] **Step 2: Run server tests and capture RED**

```powershell
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~GameplayTagHostingTests'
```

- [ ] **Step 3: Implement options and loader**

Use these required properties:

```csharp
public GameplayTagCatalogMode Mode { get; set; }
public string CatalogId { get; set; } = string.Empty;
public string CatalogVersion { get; set; } = string.Empty;
public string ExpectedFingerprint { get; set; } = string.Empty;
public string PackagedPath { get; set; } = "Content/GameplayTags.catalog";
internal string? LocalApplicationDataOverride { get; set; }
```

Parse `ExpectedFingerprint` as exactly 64 lowercase or uppercase hex digits. Register options binding, one singleton
loader result and the startup service before returning from `AddGameplayTagCatalog`. The startup service constructor
requires the singleton `TagCatalog`; its `StartAsync` is a no-op, making DI construction/validation happen before hosted
gameplay starts. On success, log the resolved Catalog ID, Version and lowercase fingerprint once; never log tag contents
or silently reload the file during the process lifetime.

- [ ] **Step 4: Run focused and full server tests**

```powershell
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~GameplayTagHostingTests'
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release
dotnet build server/src/Bun3.Server.GameplayTags/Bun3.Server.GameplayTags.csproj `
  -c Release --no-restore --warnaserror
```

- [ ] **Step 5: Commit server integration**

```powershell
git add -- Bun3.sln server/src/Bun3.Server.GameplayTags server/tests/Bun3.Server.Tests
git commit -m "✨ 서버 GameplayTag Catalog 시작 경계 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 7: Replace arbitrary Unity file selection with the fixed Game Source and Workspace context

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagGameSourcePath.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/IGameplayTagBuildContextProvider.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPublishedCatalogContext.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextResolution.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildContextResolver.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditorWorkspace.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagWorkspaceSnapshot.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagEditorWorkspaceTests.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogFileAdapter.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogFileAdapterTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 1 Source documents and Task 2 compilation/provenance.
- Produces:
  - `GameplayTagGameSourcePath.Get(string dataPath) : string`
  - public Editor interface properties `CatalogId`, `ExternalSourceMetadataPaths`, and method `GetPublishedCatalog()`
  - `GameplayTagPublishedCatalogContext(string artifactPath, string catalogId, string catalogVersion, ReadOnlySpan<byte> expectedFingerprint)`
  - `GameplayTagBuildContextResolution.Context`, `.Diagnostics`, `.HasCompleteContext`
  - `GameplayTagBuildContextResolver.ResolveDevelopment(string gameSourcePath) : GameplayTagBuildContextResolution`
  - `GameplayTagEditorWorkspace.Open(GameplayTagBuildContextResolution resolution, string gameSourcePath)`
  - nullable `GameplayTagEditorWorkspace.Snapshot`, plus `.GameSession`, `.Diagnostics`, `.CanEditGameSource`, `.CanBuildCatalog`
  - `GameplayTagWorkspaceSnapshot.Catalog`, `.Provenance`, `.Sources`

- [ ] **Step 1: Write fixed-path and provider-resolution RED tests**

Verify `Application.dataPath` maps only to `<project>/ProjectSettings/GameplayTags.json`; no Assets path is accepted.
Use injected fake provider lists to verify exactly one provider is required, zero/multiple providers produce a stable
configuration diagnostic, and a context has Catalog ID plus resolved read-only Source documents. With zero providers,
a valid Game Source still yields `CanEditGameSource == true` through Game-only validation while
`CanBuildCatalog == false` and `Snapshot == null`; malformed resolved external metadata makes both flags false.

- [ ] **Step 2: Write missing/create/import RED tests**

Verify missing Game Source yields `CanCreateGameSource = true` and invalid Workspace; creating writes the exact empty
JSON. Import validates and lowercases into the fixed path without deleting the old file; invalid import leaves both
paths untouched.

- [ ] **Step 3: Run Unity focused tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogFileAdapterTests'
```

- [ ] **Step 4: Implement Editor-only build context resolution**

The public Editor interface is exact:

```csharp
public interface IGameplayTagBuildContextProvider
{
    string CatalogId { get; }
    IReadOnlyList<string> ExternalSourceMetadataPaths { get; }
    GameplayTagPublishedCatalogContext GetPublishedCatalog();
}
```

Use `TypeCache.GetTypesDerivedFrom<IGameplayTagBuildContextProvider>()`, reject abstract/generic/non-parameterless
providers, instantiate exactly one, then combine its external metadata documents with installed package metadata at
fixed relative path `Bun3/GameplayTags/TagSource.json`. `GameplayTagPublishedCatalogContext` contains exact artifact
path, Catalog ID, Version and a copied 32-byte expected fingerprint. This reflection is Editor authoring discovery, not
runtime Source discovery.

- [ ] **Step 5: Remove New/Open and add Create/Import Existing**

The toolbar shows the fixed path and buttons `Create Game Source`, `Import Existing…`, `Reload`, `Save`. `New`, `Open`
and file-path state are removed from the controller. `Import Existing…` uses a file picker only for the one-time source
copy; normal editing never follows that selected path.

- [ ] **Step 6: Run focused Unity tests**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagEditorWorkspaceTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogFileAdapterTests'
```

- [ ] **Step 7: Commit fixed Source and Workspace loading**

```powershell
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ GameplayTag Game Source 경로 고정" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 8: Make Editor mutations source-scoped, lowercase and atomic

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogEditSession.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRenameResult.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindowController.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagEditDialog.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogEditSessionTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Create: corresponding Unity `.meta` file for `GameplayTagRenameResult.cs`

**Interfaces:**
- Consumes: Task 7 Workspace and Task 2 whole-Workspace compiler.
- Produces:
  - `GameplayTagCatalogEditSession.Open(TagSourceDocument gameSource, Func<TagSourceDocument, TagCatalogCompilation> compileCandidate)`
  - `Add`, `SetComment`, `RenameSubtree`, `DeleteExact`, `RemoveRedirects` validate a candidate Source against all Sources.
  - `GameplayTagRenameResult.NewPath` and `.ShadowedOldPaths` for popup policy.

- [ ] **Step 1: Replace casing expectations with canonical lowercase RED tests**

Update/add tests so `Add("Ability.Jump")`, `SetComment("ABILITY", ...)`, rename segment `Sprint` and imported JSON all
serialize only `ability`, `ability.jump`, `sprint`. Delete the obsolete case-only display rename test; replacing one
casing with another is a semantic no-op and creates no redirect.

- [ ] **Step 2: Add source-scoped rename/delete RED tests**

Test these exact scenarios:

```text
Game ability.jump -> ability.leap; Package ability.jump remains active.
Game gets ability.jump -> ability.leap redirect and GameplayTagRenameResult shadows ability.jump.
Rename into a path already active in Game rejects byte-for-byte.
Rename into a path active only in another Source succeeds and merges at runtime.
Renaming implicit Game parent ability -> skill rewrites ability.jump and creates direct redirects for both active paths.
Setting a comment on an implicit Game parent promotes only that parent to an explicit Game row.
DeleteExact removes one Game explicit row only; it never deletes descendants or another Source.
DeleteExact rejects an implicit-only node.
```

- [ ] **Step 3: Run focused tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

- [ ] **Step 4: Refactor Apply into Workspace candidate validation**

Clone only the Game Source document, perform the mutation, call `TagCatalogCompiler.Compile` with the unchanged
read-only Sources, and replace session state only when `Succeeded`. Convert compiler errors to one editor exception with
all diagnostics; return warning diagnostics separately so the window can popup without rolling back a successful rename.
The Workspace supplies the `compileCandidate` delegate, so the edit session never discovers packages or build context
itself. The controller snapshots the `TagSourceDocument`, selected `(SourceId, CanonicalPath)` and dirty state for
`TryExecute` rollback instead of reconstructing a session through the obsolete JSON Runtime loader.

When build-context discovery alone is missing, the delegate compiles the Game Source by itself so authors can continue
editing as required by the spec, but it never exposes that partial result as a merged Picker Snapshot or dev Catalog.
When a resolved external Source is malformed or the complete compiler reports an error, mutations stay disabled until
that external/configuration error is fixed; do not validate against an incomplete dependency set and call it complete.

- [ ] **Step 5: Preserve Save versus dev-compile semantics**

Saving a valid Game Source makes source dirty false after atomic JSON replacement. A later dev Catalog compile failure
does not mark the already-saved source dirty again and does not replace the previous dev artifact. The window must popup
the compile failure and retain invalid Workspace diagnostics.

- [ ] **Step 6: Run focused tests and commit**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagCatalogEditSessionTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ GameplayTag Source 단위 편집 트랜잭션 적용" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 9: Render the Source-grouped Tag Editor tree and permissions

**Files:**
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeModel.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagReferenceSearch.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagRedirectMaintenance.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagTreeModelTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagRedirectMaintenanceTests.cs`

**Interfaces:**
- Consumes: Task 7 Workspace Snapshot and Task 8 mutation results.
- Produces: Source-root row model with stable Editor row IDs, Source ID, path, comment, explicit/read-only flags and action policy.

- [ ] **Step 1: Write Source tree RED tests**

Assert deterministic preorder:

```text
Bun3.Gameplay [source root, read only]
  ability [implicit]
    jump [explicit, framework comment]
Game [source root, editable]
  ability [implicit]
    jump [explicit, game comment]
```

Source roots have empty tag path and are not selectable as GameplayTags. Duplicate runtime index values across Sources
must not collide because TreeView row IDs are sequential editor IDs, not `ushort` tag indices.

- [ ] **Step 2: Write context menu permission RED tests**

Writable explicit rows expose Rename, Edit Comment, Add Sub-Tag, Copy, Find References and Delete. Writable implicit
rows expose Rename, Edit Comment, Add Sub-Tag, Copy and Find References but not Delete. Read-only rows expose only Copy
and Find References. Source roots expose no tag actions. Add Sub-Tag still only fills `canonicalPath + "."` and focuses
input. Delete first runs `GameplayTagReferenceSearch` for the exact canonical path; any live match blocks `DeleteExact`
and opens the reference result view instead of mutating the Game Source.

- [ ] **Step 3: Run focused Unity tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

- [ ] **Step 4: Implement Source projection and stable selection keys**

Store selection as `(SourceId, CanonicalPath)`, not path alone. Filtering searches canonical full paths, includes Source
root and ancestors, expands matches, and restores previous expand state after the filter clears. Continue using both
horizontal and vertical TreeView scrolling and `GetContentIndent` so fold arrows never overlap labels.

- [ ] **Step 5: Group Redirect rows by owning Source**

Show read-only state and Source ownership/path context. Preserve Find References/Remove Obsolete behavior; disable
remove for read-only Sources. A shadowed redirect gets a warning icon and tooltip explaining active-name lookup priority.

- [ ] **Step 6: Run focused tests and commit**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests;Bun3.Gameplay.Unity.Tests.GameplayTagRedirectMaintenanceTests'
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ GameplayTag Source 트리 편집기 적용" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 10: Add the reusable merged GameplayTag Picker projection

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerModel.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerRow.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPickerWindow.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagPickerTests.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagTreeView.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 7 `GameplayTagWorkspaceSnapshot` runtime Catalog and provenance.
- Produces:
  - `GameplayTagPickerModel.Filter(string search) : IReadOnlyList<GameplayTagPickerRow>`
  - `GameplayTagPickerRow.CanonicalPath`, `.DisplaySegment`, `.SourceCount`, `.SourceDetails`, `.IsDirectMatch`
  - `GameplayTagPickerWindow.Show(GameplayTagWorkspaceSnapshot snapshot, string selectedPath, Action<string> onSelected)`

- [ ] **Step 1: Write merged projection RED tests**

Two Source declarations of `ability.jump` produce one row with `SourceCount == 2`. Tooltip lists Sources in Source ID
order and preserves each Source comment. Selecting the row returns only `ability.jump`; Source root/ID never enters the
callback or serialized value.

- [ ] **Step 2: Write filter/expand/scroll RED tests**

Search `JUMP` matches lowercase full name, includes ancestors, and marks only the leaf direct. Entering filter snapshots
expand IDs, expands all result ancestors, and clearing restores the snapshot. Constructing the view sets both-axis
scrolling; long paths remain available through horizontal scroll and tooltip.

- [ ] **Step 3: Run Picker tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests'
```

- [ ] **Step 4: Implement selection-only Picker using the shared row renderer**

Do not add a `PropertyDrawer` or serialized wrapper in this task. The utility window is the reusable boundary a future
Inspector drawer calls. Disable selection when the Workspace has errors and show the same persistent diagnostics banner.

- [ ] **Step 5: Run Picker and tree tests, then commit**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagPickerTests;Bun3.Gameplay.Unity.Tests.GameplayTagTreeModelTests'
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ 병합 GameplayTag Picker 트리 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 11: Compile the local Catalog on save/Play and gate invalid Editor sessions

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagDevelopmentCatalogBuilder.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPlayModeGate.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPlaySessionCatalog.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagDiagnosticsPanel.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagDevelopmentCatalogTests.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagPlayModeGateTests.cs`
- Modify: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagCatalogWindow.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagCatalogWindowTests.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 3 writer, Task 5 dev path, Task 7 Workspace.
- Produces:
  - `GameplayTagDevelopmentCatalogBuilder.Build(GameplayTagEditorWorkspace workspace) : TagCatalog`
  - `Gameplay/Build Local Tag Catalog` menu
  - `GameplayTagPlayModeGate.TryPrepare(GameplayTagEditorWorkspace workspace, out TagCatalog catalog, out string diagnostic) : bool`
  - `GameplayTagPlaySessionCatalog.Current` exposes only the immutable binary-reloaded Catalog prepared for the active Play transition.

- [ ] **Step 1: Write save/build RED tests**

Saving valid JSON writes and binary-round-trips the dev Catalog at the exact OS cache path. Compiler or binary readback
failure preserves the prior cache bytes and shows a warning popup. The focused window `Ctrl/Cmd+S` consumes the event,
saves the fixed Game Source and attempts dev compile; Unity general Save is not invoked.

- [ ] **Step 2: Write Play gate RED tests**

Missing Game Source, missing provider context or error diagnostics make `TryPrepare` false and do not enter Play.
Successful prepare compiles, atomically writes, reads via `TagCatalogBinary.Load(ForDevelopment(id))`, returns that
reloaded Catalog, and freezes the same instance in `GameplayTagPlaySessionCatalog.Current`. The holder is empty during
ordinary Edit Mode and is cleared on return to `EnteredEditMode`; a game Editor-only composition adapter consumes it
before creating its first gameplay world instead of receiving the Preview Snapshot.

- [ ] **Step 3: Run focused tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagDevelopmentCatalogTests;Bun3.Gameplay.Unity.Tests.GameplayTagPlayModeGateTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
```

- [ ] **Step 4: Implement playModeStateChanged gate**

Register once with `[InitializeOnLoadMethod]`. On `ExitingEditMode`, build from freshly reloaded Sources; when it fails,
set `EditorApplication.isPlaying = false` and show one popup with Source/path diagnostics. Do not terminate any Unity
process and do not mutate ProjectSettings besides the user-authored fixed JSON.

- [ ] **Step 5: Add persistent invalid-Workspace diagnostics**

Tag Editor remains open and existing serialized tag text remains visible. Picker selection and all build actions are
disabled until `CanBuildCatalog` is true. Add/Rename/Delete follow `CanEditGameSource`: they remain available for a valid
Game Source when only provider discovery is missing, but are disabled for malformed Game or resolved external Sources.
The panel provides `Open Source` only when a local path is known and always provides `Copy Details`.

- [ ] **Step 6: Run focused and full EditMode tests, then commit**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagDevelopmentCatalogTests;Bun3.Gameplay.Unity.Tests.GameplayTagPlayModeGateTests;Bun3.Gameplay.Unity.Tests.GameplayTagCatalogWindowTests'
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ GameplayTag 로컬 Catalog 실행 게이트 추가" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 12: Validate and include the pinned Published Catalog in Unity builds

**Files:**
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagPublishedCatalogValidator.cs`
- Create: `common/src/com.bun3.gameplay/Editor/Tags/GameplayTagBuildPlayerProcessor.cs`
- Create: `common/src/com.bun3.gameplay/Tests/Editor/GameplayTagBuildPlayerProcessorTests.cs`
- Create: corresponding `.meta` files

**Interfaces:**
- Consumes: Task 3 reader and the Task 7 build-context provider's Published identity/fingerprint.
- Produces: Unity `BuildPlayerProcessor.PrepareForBuild(BuildPlayerContext)` validation and inclusion hook.

- [ ] **Step 1: Write build processor RED tests around an injectable context adapter**

Verify a valid pinned file is added once at `Bun3/GameplayTags/GameplayTags.catalog`; missing, corrupt, wrong ID,
Version or fingerprint throws `BuildFailedException` before player build. Verify the processor never compiles working
tree JSON in Published mode and never creates `Assets/StreamingAssets` files.

- [ ] **Step 2: Run focused tests and capture RED**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
```

- [ ] **Step 3: Implement BuildPlayerProcessor integration**

The provider supplies the resolved published artifact path and exact ID/Version/fingerprint from the existing game
resource build context. Validate via
`TagCatalogBinary.Load(stream, TagCatalogExpectations.ForPublished(context.CatalogId, context.CatalogVersion, context.ExpectedFingerprint))`,
then call:

```csharp
buildPlayerContext.AddAdditionalPathToStreamingAssets(
    publishedCatalogPath,
    "Bun3/GameplayTags/GameplayTags.catalog");
```

This Unity 2022.3 API includes the external file without copying it into the project. Runtime game composition roots open
that path and call the common binary reader before creating gameplay worlds; asynchronous platform-specific
StreamingAssets transport remains the game bootstrap's IO adapter, not the Catalog's responsibility.

- [ ] **Step 4: Run build processor tests and Unity import compile**

```powershell
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode `
  -TestFilter 'Bun3.Gameplay.Unity.Tests.GameplayTagBuildPlayerProcessorTests'
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode Import
```

- [ ] **Step 5: Commit Unity Published build integration**

```powershell
git add -- common/src/com.bun3.gameplay/Editor common/src/com.bun3.gameplay/Tests/Editor
git commit -m "✨ Unity GameplayTag Catalog 빌드 포함 지원" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 13: Complete compatibility gates, package metadata and release validation

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogCompatibility.cs`
- Modify: `common/src/com.bun3.gameplay/Bun3.Gameplay.csproj`
- Modify: `common/src/com.bun3.gameplay/package.json`
- Modify: `common/tests/Bun3.Gameplay.Tests/TagCatalogConformanceTests.cs`
- Modify: `common/src/com.bun3.gameplay/Tests/Editor/TagCatalogConformanceTests.cs`
- Modify: `server/src/Bun3.Server.GameplayTags/Bun3.Server.GameplayTags.csproj`
- Modify: `Bun3.sln`
- Create: corresponding `.meta` file

**Interfaces:**
- Consumes: all previous tasks.
- Produces: versioned packages and one verified client/server binary compatibility contract.

- [ ] **Step 1: Add end-to-end conformance tests**

Compile one Source set once, write one binary, load it through direct Runtime and server adapter paths, and compare every
index/name/parent/subtree/redirect/fingerprint. Add exact helper
`TagCatalogCompatibility.RequirePeerFingerprint(TagCatalog local, ReadOnlySpan<byte> peerFingerprint)` so a mismatch
throws `TagCatalogCompatibilityException`. The conformance test calls this helper immediately before its simulation
callback and asserts a mismatch leaves callback count `0`; matching fingerprint reaches the callback exactly once.

- [ ] **Step 2: Run conformance tests and capture RED for any missing compatibility surface**

```powershell
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~TagCatalogConformanceTests'
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~GameplayTagHostingTests'
```

- [ ] **Step 3: Set final package versions and dependency metadata**

Set both `Bun3.Gameplay.csproj` and `package.json` to `0.9.0`. Keep NuGet Newtonsoft exactly `[13.0.2]`, UPM Unity
exactly `2022.3` and UPM Newtonsoft exactly `3.2.2`. Set new `Bun3.Server.GameplayTags` package version to `0.1.0`
and retain its repository ProjectReference; standard NuGet pack output must record `Bun3.Gameplay` minimum Version
`0.9.0`. Runtime ID/Version/fingerprint checks, rather than an artificial exact NuGet range, enforce Catalog compatibility.

- [ ] **Step 4: Run all Release and Unity gates**

```powershell
git diff --check
dotnet restore Bun3.sln
dotnet build common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release --no-restore --warnaserror
dotnet build common/src/com.bun3.gameplay/Catalog/Bun3.Gameplay.Catalog.csproj -c Release --no-restore --warnaserror
dotnet build common/src/Bun3.Gameplay.TagSourceAnalyzer/Bun3.Gameplay.TagSourceAnalyzer.csproj -c Release --no-restore --warnaserror
dotnet build common/src/Bun3.Gameplay.TagSource.Tasks/Bun3.Gameplay.TagSource.Tasks.csproj -c Release --no-restore --warnaserror
dotnet build common/src/Bun3.Gameplay.Tags.Cli/Bun3.Gameplay.Tags.Cli.csproj -c Release --no-restore --warnaserror
dotnet build server/src/Bun3.Server.GameplayTags/Bun3.Server.GameplayTags.csproj -c Release --no-restore --warnaserror
dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj -c Release
dotnet test common/tests/Bun3.Gameplay.TagSourceAnalyzer.Tests/Bun3.Gameplay.TagSourceAnalyzer.Tests.csproj -c Release
dotnet test common/tests/Bun3.Gameplay.Tags.Cli.Tests/Bun3.Gameplay.Tags.Cli.Tests.csproj -c Release
dotnet test server/tests/Bun3.Server.Tests/Bun3.Server.Tests.csproj -c Release
dotnet test Bun3.sln -c Release --no-restore
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
```

The planned changes only alter Catalog construction/loading/metadata, so Player tests are not required. If implementation
expands into existing `TagContainer`, `TagCountContainer`, lookup or matching hot-path methods, run both Mono and IL2CPP
with the existing script; in either case never terminate the Unity process.

- [ ] **Step 5: Pack to an isolated artifact directory and read back contents**

```powershell
dotnet pack common/src/com.bun3.gameplay/Bun3.Gameplay.csproj -c Release `
  -o artifacts/gameplay-tag-sources-pack
dotnet pack common/src/Bun3.Gameplay.Tags.Cli/Bun3.Gameplay.Tags.Cli.csproj -c Release `
  -o artifacts/gameplay-tag-sources-pack
dotnet pack server/src/Bun3.Server.GameplayTags/Bun3.Server.GameplayTags.csproj -c Release `
  -o artifacts/gameplay-tag-sources-pack
```

Open nupkgs as ZIP and assert:

```text
Bun3.Gameplay 0.9.0
lib/netstandard2.1/Bun3.Gameplay.dll
lib/netstandard2.1/Bun3.Gameplay.Catalog.dll
Newtonsoft.Json [13.0.2]
Native analyzer, build-task DLL and buildTransitive target present once
bun3-tags 0.1.0 / DotnetTool / ToolCommandName bun3-tags
Bun3.Server.GameplayTags 0.1.0
Bun3.Gameplay minimum 0.9.0
UPM 0.9.0 / Unity 2022.3 / com.unity.nuget.newtonsoft-json 3.2.2
```

- [ ] **Step 6: Verify repository hygiene and commit metadata only**

`git status --short --untracked-files=all` may still show user-owned `.superpowers/` and `unity/GameplayTags.json`;
do not stage them. Ensure Unity did not leave an unintended `ProjectSettings.asset` diff.

```powershell
git add -- Bun3.sln common/src/com.bun3.gameplay/Bun3.Gameplay.csproj `
  common/src/com.bun3.gameplay/package.json common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogCompatibility.cs `
  common/src/com.bun3.gameplay/Runtime/Tags/TagCatalogCompatibility.cs.meta common/tests/Bun3.Gameplay.Tests `
  common/src/com.bun3.gameplay/Tests/Editor server/src/Bun3.Server.GameplayTags
git commit -m "🔖 GameplayTag Source Catalog 릴리스 준비" `
  -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Re-run final tests from committed HEAD**

```powershell
git diff --check
dotnet test Bun3.sln -c Release --no-restore
& common/tests/Bun3.Gameplay.Tests/Invoke-GameplayUnityTests.ps1 -Mode EditMode
git status --short --untracked-files=all
```

Expected: all gates pass, no staged changes remain, and only pre-existing user-owned untracked files remain. Do not
publish, push or open a PR without a separate user request.
