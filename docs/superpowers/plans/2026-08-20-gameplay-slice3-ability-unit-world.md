# 게임플레이 슬라이스 3 (Ability · Unit · World) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Bun3.Gameplay`에 Unit/World 레지스트리, 데이터 전용 Ability(게이트·부여 소스), Effect 체인의 `OnEvent` 트리거 기반 반응형 콘텐츠, TargetSelector 후보 배선을 추가하고 카탈로그 빌드 비용 백로그 3종을 해소한다.

**Architecture:** Unit은 기존 `EffectTarget`을 **합성**으로 품고(상속 아님), World가 `IEffectTargetResolver`를 구현해 `EffectPipeline`에 자신을 주입한다. 반응형은 새 큐 없이 `ApplyInstant`/`FirePeriodic` 안에서 이벤트를 발행하고 그 자리에서 `OnEvent` 체인을 매칭해 **기존 적용 큐**로 넣는다 — 재진입이 없고 순환은 기존 틱당 예산이 유계화한다. Ability는 로직이 없는 순수 데이터이며 쿨다운은 Effect로, 코스트는 즉시 차감으로 표현한다.

**Tech Stack:** C# 9 / netstandard2.1 (블록 네임스페이스, `#nullable enable`), NUnit 3, `dotnet test`. 테스트만 file-scoped 네임스페이스를 쓴다(기존 관례).

**Spec:** [`docs/superpowers/specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md`](../specs/2026-08-20-gameplay-slice3-ability-unit-world-design.md)

## Global Constraints

- **패키지 코드는 netstandard2.1 + C#9, 블록 네임스페이스.** 파일 첫 줄은 `#nullable enable`.
- **모든 public 멤버에 영어 XML 문서.** 빌드 경고 0.
- **코드·주석·문자열 리터럴은 영어 전용.** 한글 금지(`docs/` 제외). 레거시(idlez, growninja, growdaemon) 언급 금지, 타 패키지 참고 언급 금지 — 출처·근거는 스펙 문서에만.
- **최소한의 필요한 주석만.** 코드가 못 보여주는 제약·계약만. 구현 경위·정당화·다음 줄 설명 주석 금지.
- **폴더 = 네임스페이스.** `Runtime/` → `Bun3.Gameplay`, `Runtime/Effects/` → `Bun3.Gameplay.Effects`, `Runtime/Abilities/` → `Bun3.Gameplay.Abilities`, `Runtime/Attributes/` → `Bun3.Gameplay.Attributes`, `Runtime/Numerics/` → `Bun3.Gameplay.Numerics`, `Runtime/Seams/` → `Bun3.Gameplay.Seams`, `Runtime/Tags/` → `Bun3.Gameplay.Tags`, `Catalog/` → `Bun3.Gameplay.Catalog`.
- **틱 핫패스 무할당 규율**: 클로저·LINQ·스냅샷 복사 금지. 카탈로그 Build 경로는 할당 허용(기동 1회).
- **결정론**: 순회 순서는 항상 canonical — 인스턴스 id 오름차순, 그 안에서 선언 행 순. `TargetId` 목록은 오름차순.
- **`EffectTarget`은 sealed 유지·무수정.** `EffectTarget.CreateSnapshot`/`RestoreSnapshot` 시그니처 변경 금지.
- **커밋**: gitmoji + `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>` 트레일러. 서브에이전트는 `git commit -m "<제목>" -m "<트레일러>"` 이중 플래그.
- **퍼블리시·main 머지 금지.** 브랜치 `Bun3/gameplay-slice3-ability`에서 완결한다.
- 테스트 실행: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
- 빌드: `dotnet build Bun3.sln`

---

## File Structure

**신규 파일**

| 경로 | 책임 |
|---|---|
| `common/src/com.bun3.gameplay/Runtime/Numerics/CompiledFormula.cs` | 수식 RPN 컴파일 결과 + 평가 |
| `common/src/com.bun3.gameplay/Runtime/Unit.cs` | `Unit` — EffectTarget 합성 + AbilitySet + OwnerId |
| `common/src/com.bun3.gameplay/Runtime/UnitSnapshot.cs` | `UnitSnapshot`, `WorldSnapshot` |
| `common/src/com.bun3.gameplay/Runtime/World.cs` | `World` — 레지스트리·resolver·스폰/디스폰 큐·Tick |
| `common/src/com.bun3.gameplay/Runtime/World.Ability.cs` | `World.TryActivateAbility` partial |
| `common/src/com.bun3.gameplay/Runtime/Effects/GameplayEvent.cs` | 이벤트 4+1 튜플 구조체 |
| `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.Events.cs` | 이벤트 발행·`OnEvent` 매칭 partial |
| `common/src/com.bun3.gameplay/Runtime/Effects/BuiltInSelectors.cs` | `EventSource`/`EventTarget` 내장 셀렉터 |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityDef.cs` | 저작 스펙 + `CostDef` + `AbilityEffectDef` |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityActivationResult.cs` | 발동 결과 enum |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalog.cs` | 컴파일된 카탈로그 + `CompiledAbility` |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalogBuilder.cs` | Build 검증·컴파일 |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySet.cs` | 부여·회수·참조 카운트·레벨 |
| `common/src/com.bun3.gameplay/Runtime/Abilities/GrantSource.cs` | 부여 소스 4종 |
| `common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySetSnapshot.cs` | 부여 상태 스냅샷 |
| `common/src/com.bun3.gameplay/Catalog/AbilityDefJson.cs` | Ability JSON 로더 |
| `common/src/com.bun3.gameplay/Catalog/EffectCatalogBinary.cs` | Effect 카탈로그 바이너리 Writer/Reader |
| `common/src/com.bun3.gameplay/Catalog/AbilityCatalogBinary.cs` | Ability 카탈로그 바이너리 Writer/Reader |

**수정 파일**

| 경로 | 변경 |
|---|---|
| `Runtime/Numerics/BigNumFormula.cs` | Parser를 RPN emit으로 전환, `TryCompile` 추가 |
| `Runtime/Attributes/AttributeEnums.cs` | `OperandKind.EventMagnitude = 3` |
| `Runtime/Attributes/Operand.cs` | `EventMagnitude` 팩토리 |
| `Runtime/Seams/Seams.cs` | `SelectorContext` 확장, `ITargetSelector.Select` 반환 의미 |
| `Runtime/Effects/EffectSpec.cs` | `RaiseEvents`, `GrantedAbilities`, `ChainTrigger.OnEvent`, `ChainEdgeDef.EventTag`/`Listen` |
| `Runtime/Effects/EffectCatalog.cs` | `CompiledRaiseEvent`, `CompiledChain` 확장, `CompiledEffectSpec` 확장 |
| `Runtime/Effects/EffectCatalogBuilder.cs` | 신규 어휘 컴파일·검증, `CompileFormula` 1회화 |
| `Runtime/Effects/EffectPipeline.cs` | `PendingApply.EventMagnitude`, 평가 문맥 전파, 절단 카운터 |
| `Runtime/Effects/EffectTarget.cs` | `OnEvent` 리스너 카운터 |
| `Catalog/EffectSpecJson.cs` | 신규 필드 로드 |
| `common/src/com.bun3.gameplay/package.json` | 0.13.0 → 0.14.0 |
| `common/src/com.bun3.gameplay/*/AssemblyInfo.cs` 또는 csproj | 버전 범프 |

---

## Task 1: 수식 파싱 1회화 (백로그 ①)

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Numerics/CompiledFormula.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Numerics/BigNumFormula.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalogBuilder.cs:475-497` (`CompileFormula`)
- Test: `common/tests/Bun3.Gameplay.Tests/BigNumFormulaTests.cs`

**Interfaces:**
- Consumes: 없음 (선행 작업 없음)
- Produces:
  - `public sealed class CompiledFormula` — `public bool TryEvaluate(BigNum x, out BigNum result)`
  - `public static bool BigNumFormula.TryCompile(string formula, out CompiledFormula? compiled, out string? error)`
  - 기존 `TryValidate(string, out string?)` / `TryEvaluate(string, BigNum, out BigNum)` 시그니처 불변

- [ ] **Step 1: 회귀 테스트를 먼저 쓴다 — 새 경로가 기존과 비트 동일한지**

`common/tests/Bun3.Gameplay.Tests/BigNumFormulaTests.cs` 끝에 추가:

```csharp
    [Test]
    public void Compiled_path_matches_interpreted_path_bit_for_bit()
    {
        string[] formulas =
        {
            "1", "x", "-x", "x+1", "1-x", "x*x", "x/2", "2/x", "x^3", "-x^2",
            "(x+1)*(x-1)", "x*x*x - 3*x + 2", "1.5*x + 0.25", "1e3*x", "x^0", "((x))",
        };

        foreach (var formula in formulas)
        {
            Assert.That(BigNumFormula.TryCompile(formula, out var compiled, out var error), Is.True, $"compile failed: {formula} ({error})");
            for (var level = 1; level <= 20; level++)
            {
                var interpretedOk = BigNumFormula.TryEvaluate(formula, level, out var interpreted);
                var compiledOk = compiled!.TryEvaluate(level, out var fromCompiled);
                Assert.That(compiledOk, Is.EqualTo(interpretedOk), $"{formula} @ {level}");
                if (interpretedOk)
                {
                    Assert.That(fromCompiled.Mantissa, Is.EqualTo(interpreted.Mantissa), $"{formula} @ {level}");
                    Assert.That(fromCompiled.Exponent, Is.EqualTo(interpreted.Exponent), $"{formula} @ {level}");
                }
            }
        }
    }

    [Test]
    public void Compile_rejects_invalid_grammar()
    {
        Assert.That(BigNumFormula.TryCompile("x +", out var compiled, out var error), Is.False);
        Assert.That(compiled, Is.Null);
        Assert.That(error, Is.Not.Null);
    }

    [Test]
    public void Compiled_division_by_zero_fails_at_evaluation_not_compilation()
    {
        Assert.That(BigNumFormula.TryCompile("1/(x-5)", out var compiled, out _), Is.True);
        Assert.That(compiled!.TryEvaluate(4, out _), Is.True);
        Assert.That(compiled.TryEvaluate(5, out _), Is.False);
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~BigNumFormulaTests"`
Expected: FAIL — `BigNumFormula.TryCompile` 이 존재하지 않아 컴파일 오류.

- [ ] **Step 3: `CompiledFormula` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Numerics/CompiledFormula.cs`:

```csharp
#nullable enable
namespace Bun3.Gameplay.Numerics
{
    /// <summary>Operation of one compiled formula token, in reverse-polish order.</summary>
    internal enum FormulaOp : byte
    {
        /// <summary>Pushes a literal value.</summary>
        Literal = 0,
        /// <summary>Pushes the variable x.</summary>
        Variable = 1,
        /// <summary>Pops two operands and pushes their sum.</summary>
        Add = 2,
        /// <summary>Pops two operands and pushes their difference.</summary>
        Subtract = 3,
        /// <summary>Pops two operands and pushes their product.</summary>
        Multiply = 4,
        /// <summary>Pops two operands and pushes their quotient. Fails when the divisor is zero.</summary>
        Divide = 5,
        /// <summary>Pops one operand and pushes its negation.</summary>
        Negate = 6,
        /// <summary>Pops one operand and pushes it raised to an integer exponent.</summary>
        Power = 7,
    }

    /// <summary>One token of a compiled formula.</summary>
    internal readonly struct FormulaToken
    {
        internal FormulaToken(FormulaOp op, BigNum literal, int exponent)
        {
            Op = op;
            Literal = literal;
            Exponent = exponent;
        }

        internal FormulaOp Op { get; }

        internal BigNum Literal { get; }

        internal int Exponent { get; }
    }

    /// <summary>
    /// Formula parsed once into reverse-polish tokens. Evaluation replays the exact operation
    /// order of the recursive-descent parser, so results are bit-identical to single-shot
    /// evaluation. Produced by <see cref="BigNumFormula.TryCompile"/>.
    /// </summary>
    public sealed class CompiledFormula
    {
        private readonly FormulaToken[] _tokens;
        private readonly int _stackDepth;

        internal CompiledFormula(FormulaToken[] tokens, int stackDepth)
        {
            _tokens = tokens;
            _stackDepth = stackDepth;
        }

        /// <summary>Evaluates the compiled formula with the given variable value.</summary>
        /// <param name="x">Value substituted for the variable x.</param>
        /// <param name="result">Evaluation result on success; <see cref="BigNum.Zero"/> on failure.</param>
        /// <returns><see langword="true"/> when evaluation succeeds. Division by zero returns false.</returns>
        public bool TryEvaluate(BigNum x, out BigNum result)
        {
            var stack = new BigNum[_stackDepth];
            var top = 0;
            for (var i = 0; i < _tokens.Length; i++)
            {
                var token = _tokens[i];
                switch (token.Op)
                {
                    case FormulaOp.Literal:
                        stack[top++] = token.Literal;
                        break;
                    case FormulaOp.Variable:
                        stack[top++] = x;
                        break;
                    case FormulaOp.Add:
                        stack[top - 2] += stack[top - 1];
                        top--;
                        break;
                    case FormulaOp.Subtract:
                        stack[top - 2] -= stack[top - 1];
                        top--;
                        break;
                    case FormulaOp.Multiply:
                        stack[top - 2] *= stack[top - 1];
                        top--;
                        break;
                    case FormulaOp.Divide:
                        if (stack[top - 1].IsZero)
                        {
                            result = BigNum.Zero;
                            return false;
                        }

                        stack[top - 2] /= stack[top - 1];
                        top--;
                        break;
                    case FormulaOp.Negate:
                        stack[top - 1] = -stack[top - 1];
                        break;
                    default:
                        var baseValue = stack[top - 1];
                        var powered = BigNum.One;
                        for (var p = 0; p < token.Exponent; p++) powered *= baseValue;
                        stack[top - 1] = powered;
                        break;
                }
            }

            result = stack[0];
            return true;
        }
    }
}
```

- [ ] **Step 4: `BigNumFormula` 의 Parser를 RPN emit으로 전환한다**

`BigNumFormula.cs` 전체를 아래로 교체한다. Parser의 `evaluate`/`x` 인자가 사라지고 토큰을 `List<FormulaToken>`에 쌓는다. 문법·우선순위·리터럴 규칙은 그대로다.

```csharp
#nullable enable
using System.Collections.Generic;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// Deterministic BigNum formula compiler. Supports a single variable <c>x</c>, the four
    /// arithmetic operators (<c>+ - * /</c>), unary <c>-</c>, parentheses, and integer power
    /// <c>^</c> (exponent must be an integer literal 0..64 — <c>x^2</c> is allowed; <c>2^x</c>,
    /// <c>x^x</c>, and non-integer exponents are rejected). Precedence: <c>^</c> &gt; unary
    /// <c>-</c> &gt; <c>* /</c> &gt; <c>+ -</c>. Authoring/startup (catalog build) only, so it is
    /// an allocation-tolerant recursive-descent parser — tick hot paths read only precompiled
    /// level arrays. No float/double anywhere; all BigNum integer arithmetic.
    /// </summary>
    public static class BigNumFormula
    {
        /// <summary>Compiles the formula into reusable reverse-polish tokens.</summary>
        /// <param name="formula">Formula text to compile.</param>
        /// <param name="compiled">Compiled formula on success; <see langword="null"/> on failure.</param>
        /// <param name="error">Error message on failure; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the grammar is valid.</returns>
        public static bool TryCompile(string formula, out CompiledFormula? compiled, out string? error)
        {
            compiled = null;
            if (string.IsNullOrEmpty(formula))
            {
                error = $"Invalid formula: {formula}";
                return false;
            }

            var tokens = new List<FormulaToken>();
            var parser = new Parser(formula, tokens);
            if (!parser.TryParse(out var stackDepth))
            {
                error = $"Invalid formula: {formula}";
                return false;
            }

            compiled = new CompiledFormula(tokens.ToArray(), stackDepth);
            error = null;
            return true;
        }

        /// <summary>Checks formula grammar only (division by zero is not checked — that is an evaluation-time concern).</summary>
        /// <param name="formula">Formula text to check.</param>
        /// <param name="error">Error message on failure; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the grammar is valid.</returns>
        public static bool TryValidate(string formula, out string? error) =>
            TryCompile(formula, out _, out error);

        /// <summary>Compiles and evaluates in one shot. Prefer <see cref="TryCompile"/> when evaluating
        /// the same formula more than once.</summary>
        /// <param name="formula">Formula text to evaluate.</param>
        /// <param name="x">Value substituted for the variable <c>x</c>.</param>
        /// <param name="result">Evaluation result on success; <see cref="BigNum.Zero"/> on failure.</param>
        /// <returns><see langword="true"/> when evaluation succeeds.</returns>
        public static bool TryEvaluate(string formula, BigNum x, out BigNum result)
        {
            if (!TryCompile(formula, out var compiled, out _))
            {
                result = BigNum.Zero;
                return false;
            }

            return compiled!.TryEvaluate(x, out result);
        }

        // Recursive-descent parser emitting reverse-polish tokens. Emission order matches the
        // evaluation order of the grammar, which is what keeps results bit-identical.
        // Grammar: add := mul (('+'|'-') mul)* / mul := unary (('*'|'/') unary)* /
        // unary := '-' unary | pow / pow := primary ('^' digits)? / primary := '(' add ')' | 'x' | literal
        private struct Parser
        {
            private readonly string _text;
            private readonly List<FormulaToken> _tokens;
            private int _pos;
            private int _depth;
            private int _maxDepth;

            internal Parser(string text, List<FormulaToken> tokens)
            {
                _text = text;
                _tokens = tokens;
                _pos = 0;
                _depth = 0;
                _maxDepth = 0;
            }

            internal bool TryParse(out int stackDepth)
            {
                stackDepth = 0;
                SkipWhitespace();
                if (!TryParseAdd()) return false;
                SkipWhitespace();
                if (_pos != _text.Length) return false;
                stackDepth = _maxDepth;
                return true;
            }

            private void Push(FormulaOp op, BigNum literal, int exponent)
            {
                _tokens.Add(new FormulaToken(op, literal, exponent));
                if (op == FormulaOp.Literal || op == FormulaOp.Variable)
                {
                    _depth++;
                    if (_depth > _maxDepth) _maxDepth = _depth;
                }
                else if (op != FormulaOp.Negate && op != FormulaOp.Power)
                {
                    _depth--;
                }
            }

            private bool TryParseAdd()
            {
                if (!TryParseMul()) return false;

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || (_text[_pos] != '+' && _text[_pos] != '-')) return true;

                    var op = _text[_pos];
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseMul()) return false;
                    Push(op == '+' ? FormulaOp.Add : FormulaOp.Subtract, BigNum.Zero, 0);
                }
            }

            private bool TryParseMul()
            {
                if (!TryParseUnary()) return false;

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || (_text[_pos] != '*' && _text[_pos] != '/')) return true;

                    var op = _text[_pos];
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseUnary()) return false;
                    Push(op == '*' ? FormulaOp.Multiply : FormulaOp.Divide, BigNum.Zero, 0);
                }
            }

            private bool TryParseUnary()
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '-')
                {
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseUnary()) return false;
                    Push(FormulaOp.Negate, BigNum.Zero, 0);
                    return true;
                }

                return TryParsePow();
            }

            private bool TryParsePow()
            {
                if (!TryParsePrimary()) return false;

                SkipWhitespace();
                if (_pos >= _text.Length || _text[_pos] != '^') return true;

                _pos++;
                SkipWhitespace();
                var digitsStart = _pos;
                while (_pos < _text.Length && IsAsciiDigit(_text[_pos])) _pos++;
                if (_pos == digitsStart) return false;   // exponent must be an integer literal (no variables, fractions, signs)

                if (!int.TryParse(_text.Substring(digitsStart, _pos - digitsStart), out var exponent)
                    || exponent < 0 || exponent > 64)
                {
                    return false;
                }

                Push(FormulaOp.Power, BigNum.Zero, exponent);
                return true;
            }

            private bool TryParsePrimary()
            {
                SkipWhitespace();
                if (_pos >= _text.Length) return false;

                if (_text[_pos] == '(')
                {
                    _pos++;
                    if (!TryParseAdd()) return false;
                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != ')') return false;
                    _pos++;
                    return true;
                }

                if (_text[_pos] == 'x')
                {
                    _pos++;
                    Push(FormulaOp.Variable, BigNum.Zero, 0);
                    return true;
                }

                return TryParseLiteral();
            }

            // Same literal grammar as BigNum.TryParse minus the sign (unary - handled separately): \d+(\.\d+)?([eE][+-]?\d+)?
            private bool TryParseLiteral()
            {
                var start = _pos;
                if (_pos >= _text.Length || !IsAsciiDigit(_text[_pos])) return false;

                while (_pos < _text.Length && IsAsciiDigit(_text[_pos])) _pos++;

                if (_pos < _text.Length && _text[_pos] == '.'
                    && _pos + 1 < _text.Length && IsAsciiDigit(_text[_pos + 1]))
                {
                    _pos++;
                    while (_pos < _text.Length && IsAsciiDigit(_text[_pos])) _pos++;
                }

                if (_pos < _text.Length && (_text[_pos] == 'e' || _text[_pos] == 'E'))
                {
                    var expScan = _pos + 1;
                    if (expScan < _text.Length && (_text[expScan] == '+' || _text[expScan] == '-')) expScan++;
                    var expDigitsStart = expScan;
                    while (expScan < _text.Length && IsAsciiDigit(_text[expScan])) expScan++;
                    if (expScan > expDigitsStart) _pos = expScan;
                }

                if (!BigNum.TryParse(_text.Substring(start, _pos - start), out var parsed)) return false;
                Push(FormulaOp.Literal, parsed, 0);
                return true;
            }

            private void SkipWhitespace()
            {
                while (_pos < _text.Length && _text[_pos] == ' ') _pos++;
            }

            private static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
        }
    }
}
```

- [ ] **Step 5: 테스트 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~BigNumFormulaTests"`
Expected: PASS (기존 테스트 포함 전부).

- [ ] **Step 6: `EffectCatalogBuilder.CompileFormula` 가 1회만 컴파일하게 고친다**

`EffectCatalogBuilder.cs`의 `CompileFormula`(약 475행)를 교체:

```csharp
        private static BigNum[] CompileFormula(string formula, EffectSpec spec)
        {
            if (!BigNumFormula.TryCompile(formula, out var compiled, out var validationError))
            {
                throw new InvalidOperationException($"Effect '{spec.Name}': Formula is invalid — {validationError}");
            }

            var byLevel = new BigNum[spec.MaxLevel];
            for (var level = 1; level <= spec.MaxLevel; level++)
            {
                if (!compiled!.TryEvaluate(level, out byLevel[level - 1]))
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': Formula evaluation failed (level {level}): {formula}");
                }
            }

            return byLevel;
        }
```

- [ ] **Step 7: 전체 테스트 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
Expected: PASS. 기존 `EffectLevelTableTests`의 수식 표기(③) 케이스가 값 변화 없이 통과해야 한다.

- [ ] **Step 8: 빌드 경고 0 확인**

Run: `dotnet build Bun3.sln`
Expected: `0 Warning(s)`.

- [ ] **Step 9: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Numerics/ common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalogBuilder.cs common/tests/Bun3.Gameplay.Tests/BigNumFormulaTests.cs
git commit -m "⚡ 수식을 1회 컴파일해 레벨 테이블 생성: BigNumFormula.TryCompile + CompiledFormula" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 2: 셀렉터 절단 카운터 (백로그 ③)

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs` (`ITargetSelector.Select` XML 문서)
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs:543-577` (`FireChain`)
- Test: `common/tests/Bun3.Gameplay.Tests/SeamRegistryTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `internal long EffectPipeline.SelectorTruncationCount { get; private set; }`
- 계약 변경: `ITargetSelector.Select`는 **선택한 전체 개수**를 반환하고 `results.Length`까지만 기록한다. 반환값 > `results.Length` 이면 절단이다.

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/SeamRegistryTests.cs` 끝에 추가:

```csharp
    private sealed class OverflowSelector : ITargetSelector
    {
        private readonly int _desired;

        internal OverflowSelector(int desired) => _desired = desired;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var written = _desired < results.Length ? _desired : results.Length;
            for (var i = 0; i < written; i++) results[i] = new TargetId((ulong)(i + 1));
            return _desired;
        }
    }

    [Test]
    public void Selector_returning_more_than_the_buffer_increments_the_truncation_counter()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterSelector("selector.everyone", new OverflowSelector(desired: 40));

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(pipeline.SelectorTruncationCount, Is.EqualTo(1));
    }

    [Test]
    public void Selector_within_the_buffer_does_not_increment_the_truncation_counter()
    {
        var kit = EffectTestKit.Create();
        kit.RegisterSelector("selector.everyone", new OverflowSelector(desired: 2));

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(pipeline.SelectorTruncationCount, Is.Zero);
    }
```

파일 상단 `using`에 `System`, `Bun3.Gameplay.Effects`, `Bun3.Gameplay.Seams` 가 없으면 추가한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~SeamRegistryTests"`
Expected: FAIL — `SelectorTruncationCount` 없음.

- [ ] **Step 3: `ITargetSelector.Select` 의 계약 문서를 고친다**

`Runtime/Seams/Seams.cs`의 `ITargetSelector`:

```csharp
    /// <summary>Contract for selecting targets.</summary>
    public interface ITargetSelector
    {
        /// <summary>Selects targets for the given context.</summary>
        /// <param name="ctx">Context to select with.</param>
        /// <param name="results">Span receiving the selected target ids. At most
        /// <c>results.Length</c> entries may be written.</param>
        /// <returns>Total number of targets selected. A value greater than <c>results.Length</c>
        /// reports truncation — the caller uses only the written entries and counts the event.</returns>
        int Select(in SelectorContext ctx, System.Span<TargetId> results);
    }
```

- [ ] **Step 4: `EffectPipeline` 에 카운터를 더하고 `FireChain` 에서 절단을 감지한다**

`EffectPipeline.cs`의 드롭 카운터 무리(약 53행 `DrImmuneDropCount` 뒤)에 추가:

```csharp
        /// <summary>Number of selector calls whose result exceeded the fixed selection buffer.</summary>
        internal long SelectorTruncationCount { get; private set; }
```

`FireChain`의 셀렉터 호출부를 교체:

```csharp
                // ponytail: target counts are authoring-spec scale, so a 32-slot stack buffer suffices — on overflow only the first 32 are applied.
                Span<TargetId> targets = stackalloc TargetId[32];
                var selectorCtx = new SelectorContext(source, chain.SelectorParams, _rng);
                var selected = chain.Selector.Select(in selectorCtx, targets);
                var count = selected;
                if (count > targets.Length)
                {
                    SelectorTruncationCount++;
                    count = targets.Length;
                }

                for (var k = 0; k < count; k++)
                {
                    EnqueueApply(chain.EffectId, source, targets[k], chainLevel);
                }
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~SeamRegistryTests"`
Expected: PASS.

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs common/tests/Bun3.Gameplay.Tests/SeamRegistryTests.cs
git commit -m "📊 셀렉터 절단 카운터 추가: Select 반환값이 버퍼를 넘으면 계수" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 3: AbilitySet — 부여 소스와 참조 카운트

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/GrantSource.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySet.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySetSnapshot.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AbilitySetTests.cs`

**Interfaces:**
- Consumes: 없음
- Produces:
  - `public readonly struct GrantSource` — `GrantSourceKind Kind`, `ulong Key`; 팩토리 `Archetype()`, `Effect(ulong instanceId)`, `Item(ulong itemId)`, `Code(ulong key = 0)`
  - `public enum GrantSourceKind : byte { Archetype = 0, Effect = 1, Item = 2, Code = 3 }`
  - `public sealed class AbilitySet` — `void Grant(int abilityId, GrantSource source, int level)`, `int Revoke(GrantSource source)`, `bool IsGranted(int abilityId)`, `int GetLevel(int abilityId)`, `int GrantCount`, `AbilitySetSnapshot CreateSnapshot()`, `void RestoreSnapshot(AbilitySetSnapshot)`
  - `public sealed class AbilitySetSnapshot` (opaque, 멤버 internal)

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/AbilitySetTests.cs`:

```csharp
#nullable enable
using Bun3.Gameplay.Abilities;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilitySetTests
{
    [Test]
    public void Grant_then_revoke_by_the_same_source_removes_the_ability()
    {
        var set = new AbilitySet();
        set.Grant(abilityId: 7, GrantSource.Archetype(), level: 1);

        Assert.That(set.IsGranted(7), Is.True);
        Assert.That(set.Revoke(GrantSource.Archetype()), Is.EqualTo(1));
        Assert.That(set.IsGranted(7), Is.False);
    }

    [Test]
    public void Revoking_one_of_two_sources_keeps_the_ability()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Effect(instanceId: 42), level: 1);

        set.Revoke(GrantSource.Effect(instanceId: 42));

        Assert.That(set.IsGranted(7), Is.True);
        Assert.That(set.GrantCount, Is.EqualTo(1));
    }

    [Test]
    public void Level_is_the_maximum_across_granting_sources()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Item(itemId: 9), level: 5);
        set.Grant(7, GrantSource.Code(), level: 3);

        Assert.That(set.GetLevel(7), Is.EqualTo(5));

        set.Revoke(GrantSource.Item(itemId: 9));

        Assert.That(set.GetLevel(7), Is.EqualTo(3));
    }

    [Test]
    public void Regranting_the_same_source_updates_the_level_without_duplicating()
    {
        var set = new AbilitySet();
        set.Grant(7, GrantSource.Archetype(), level: 1);
        set.Grant(7, GrantSource.Archetype(), level: 4);

        Assert.That(set.GrantCount, Is.EqualTo(1));
        Assert.That(set.GetLevel(7), Is.EqualTo(4));
    }

    [Test]
    public void Ungranted_ability_reports_level_zero()
    {
        var set = new AbilitySet();

        Assert.That(set.IsGranted(7), Is.False);
        Assert.That(set.GetLevel(7), Is.Zero);
    }

    [Test]
    public void Grants_are_kept_in_ascending_ability_then_source_order()
    {
        var set = new AbilitySet();
        set.Grant(9, GrantSource.Code(key: 2), level: 1);
        set.Grant(3, GrantSource.Item(itemId: 5), level: 1);
        set.Grant(9, GrantSource.Archetype(), level: 1);

        var snapshot = set.CreateSnapshot();
        var restored = new AbilitySet();
        restored.RestoreSnapshot(snapshot);

        Assert.That(restored.GrantCount, Is.EqualTo(3));
        Assert.That(restored.CreateSnapshot().DebugOrder(), Is.EqualTo(snapshot.DebugOrder()));
        Assert.That(snapshot.DebugOrder(), Is.EqualTo("3/Item:5 9/Archetype:0 9/Code:2"));
    }

    [Test]
    public void Restore_replaces_all_previous_grants()
    {
        var set = new AbilitySet();
        set.Grant(1, GrantSource.Archetype(), level: 1);
        var empty = new AbilitySet().CreateSnapshot();

        set.RestoreSnapshot(empty);

        Assert.That(set.GrantCount, Is.Zero);
        Assert.That(set.IsGranted(1), Is.False);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilitySetTests"`
Expected: FAIL — `Bun3.Gameplay.Abilities` 네임스페이스 없음.

- [ ] **Step 3: `GrantSource` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/GrantSource.cs`:

```csharp
#nullable enable
using System;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Origin that granted an ability. Revocation removes only the grants of one origin.</summary>
    public enum GrantSourceKind : byte
    {
        /// <summary>Granted by the unit archetype at creation.</summary>
        Archetype = 0,
        /// <summary>Granted by an active effect instance; reclaimed when that instance ends.</summary>
        Effect = 1,
        /// <summary>Granted by an item; reclaimed when the item is unequipped or lost.</summary>
        Item = 2,
        /// <summary>Granted directly by game code.</summary>
        Code = 3,
    }

    /// <summary>One grant origin — a kind plus the id identifying the individual origin within that kind.</summary>
    public readonly struct GrantSource : IEquatable<GrantSource>, IComparable<GrantSource>
    {
        private GrantSource(GrantSourceKind kind, ulong key)
        {
            Kind = kind;
            Key = key;
        }

        /// <summary>Kind of the origin.</summary>
        public GrantSourceKind Kind { get; }

        /// <summary>Id identifying the individual origin within its kind; 0 when the kind has a single origin.</summary>
        public ulong Key { get; }

        /// <summary>Creates an archetype origin.</summary>
        public static GrantSource Archetype() => new GrantSource(GrantSourceKind.Archetype, 0);

        /// <summary>Creates an effect-instance origin.</summary>
        /// <param name="instanceId">Effect instance id that grants the ability.</param>
        public static GrantSource Effect(ulong instanceId) => new GrantSource(GrantSourceKind.Effect, instanceId);

        /// <summary>Creates an item origin.</summary>
        /// <param name="itemId">Item id that grants the ability.</param>
        public static GrantSource Item(ulong itemId) => new GrantSource(GrantSourceKind.Item, itemId);

        /// <summary>Creates a game-code origin.</summary>
        /// <param name="key">Optional id distinguishing several code origins.</param>
        public static GrantSource Code(ulong key = 0) => new GrantSource(GrantSourceKind.Code, key);

        /// <summary>Compares with another origin for equality.</summary>
        /// <param name="other">Origin to compare.</param>
        /// <returns>True when kind and key are equal.</returns>
        public bool Equals(GrantSource other) => Kind == other.Kind && Key == other.Key;

        /// <inheritdoc />
        public override bool Equals(object? obj) => obj is GrantSource other && Equals(other);

        /// <inheritdoc />
        public override int GetHashCode() => unchecked(((int)Kind * 397) ^ Key.GetHashCode());

        /// <summary>Orders by kind then key — the canonical grant order.</summary>
        /// <param name="other">Origin to compare.</param>
        /// <returns>Negative, zero, or positive ordering result.</returns>
        public int CompareTo(GrantSource other)
        {
            var byKind = ((byte)Kind).CompareTo((byte)other.Kind);
            return byKind != 0 ? byKind : Key.CompareTo(other.Key);
        }

        /// <summary>Returns whether two origins are equal.</summary>
        public static bool operator ==(GrantSource left, GrantSource right) => left.Equals(right);

        /// <summary>Returns whether two origins differ.</summary>
        public static bool operator !=(GrantSource left, GrantSource right) => !left.Equals(right);
    }
}
```

- [ ] **Step 4: `AbilitySetSnapshot` 을 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySetSnapshot.cs`:

```csharp
#nullable enable
using System.Text;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>
    /// Immutable deep copy of one ability set's grants, created by <see cref="AbilitySet.CreateSnapshot"/>.
    /// Rows are kept in canonical order (ability id ascending, then grant origin ascending) so
    /// restore is deterministic. Opaque token — callers hold and pass instances without inspecting them.
    /// </summary>
    public sealed class AbilitySetSnapshot
    {
        internal AbilitySetSnapshot(GrantRow[] grants) => Grants = grants;

        /// <summary>Grant rows in canonical order.</summary>
        internal GrantRow[] Grants { get; }

        /// <summary>One granted (ability, origin) pair with its level.</summary>
        internal readonly struct GrantRow
        {
            internal GrantRow(int abilityId, GrantSource source, int level)
            {
                AbilityId = abilityId;
                Source = source;
                Level = level;
            }

            internal int AbilityId { get; }

            internal GrantSource Source { get; }

            internal int Level { get; }
        }

        /// <summary>Renders the canonical grant order as text. Diagnostics and tests only.</summary>
        /// <returns>Space-separated <c>abilityId/Kind:Key</c> entries in stored order.</returns>
        public string DebugOrder()
        {
            var builder = new StringBuilder();
            for (var i = 0; i < Grants.Length; i++)
            {
                if (i > 0) builder.Append(' ');
                builder.Append(Grants[i].AbilityId).Append('/')
                    .Append(Grants[i].Source.Kind).Append(':').Append(Grants[i].Source.Key);
            }

            return builder.ToString();
        }
    }
}
```

- [ ] **Step 5: `AbilitySet` 을 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilitySet.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>
    /// Abilities granted to one unit, with the origin that granted each. The same ability may be
    /// granted by several origins; it stays available until the last origin is revoked, and its
    /// level is the maximum across the granting origins. Grants are held in canonical order
    /// (ability id ascending, then origin ascending) — snapshot and level resolution both depend
    /// on that order being deterministic.
    /// </summary>
    public sealed class AbilitySet
    {
        private struct Grant
        {
            internal int AbilityId;
            internal GrantSource Source;
            internal int Level;
        }

        private readonly List<Grant> _grants = new List<Grant>();

        /// <summary>Number of (ability, origin) grants held.</summary>
        public int GrantCount => _grants.Count;

        /// <summary>Grants an ability from one origin. Regranting the same pair updates the level only.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <param name="source">Origin granting the ability.</param>
        /// <param name="level">Ability level this origin grants; must be at least 1.</param>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when the level is below 1.</exception>
        public void Grant(int abilityId, GrantSource source, int level)
        {
            if (level < 1) throw new ArgumentOutOfRangeException(nameof(level), "Ability level must be at least 1.");

            var position = FindInsertPosition(abilityId, source, out var exists);
            if (exists)
            {
                var existing = _grants[position];
                existing.Level = level;
                _grants[position] = existing;
                return;
            }

            _grants.Insert(position, new Grant { AbilityId = abilityId, Source = source, Level = level });
        }

        /// <summary>Revokes every grant made by one origin.</summary>
        /// <param name="source">Origin whose grants are reclaimed.</param>
        /// <returns>Number of grants removed.</returns>
        public int Revoke(GrantSource source)
        {
            var removed = 0;
            for (var i = _grants.Count - 1; i >= 0; i--)
            {
                if (_grants[i].Source != source) continue;
                _grants.RemoveAt(i);
                removed++;
            }

            return removed;
        }

        /// <summary>Returns whether any origin currently grants the ability.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>True when at least one grant exists.</returns>
        public bool IsGranted(int abilityId)
        {
            for (var i = 0; i < _grants.Count; i++)
            {
                if (_grants[i].AbilityId == abilityId) return true;
            }

            return false;
        }

        /// <summary>Returns the effective level — the maximum across granting origins, or 0 when ungranted.</summary>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns>Effective ability level, or 0.</returns>
        public int GetLevel(int abilityId)
        {
            var level = 0;
            for (var i = 0; i < _grants.Count; i++)
            {
                if (_grants[i].AbilityId == abilityId && _grants[i].Level > level) level = _grants[i].Level;
            }

            return level;
        }

        /// <summary>Creates a deep copy of the current grants.</summary>
        /// <returns>Snapshot in canonical order.</returns>
        public AbilitySetSnapshot CreateSnapshot()
        {
            var rows = new AbilitySetSnapshot.GrantRow[_grants.Count];
            for (var i = 0; i < _grants.Count; i++)
            {
                rows[i] = new AbilitySetSnapshot.GrantRow(_grants[i].AbilityId, _grants[i].Source, _grants[i].Level);
            }

            return new AbilitySetSnapshot(rows);
        }

        /// <summary>Replaces all grants with the snapshot's.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        public void RestoreSnapshot(AbilitySetSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            _grants.Clear();
            var rows = snapshot.Grants;
            for (var i = 0; i < rows.Length; i++)
            {
                _grants.Add(new Grant { AbilityId = rows[i].AbilityId, Source = rows[i].Source, Level = rows[i].Level });
            }
        }

        // Linear scan is intentional — grant counts are archetype-scale (single digits to low tens)
        // and the list must stay in canonical order for snapshot determinism.
        private int FindInsertPosition(int abilityId, GrantSource source, out bool exists)
        {
            for (var i = 0; i < _grants.Count; i++)
            {
                var current = _grants[i];
                if (current.AbilityId < abilityId) continue;
                if (current.AbilityId > abilityId)
                {
                    exists = false;
                    return i;
                }

                var order = current.Source.CompareTo(source);
                if (order < 0) continue;
                exists = order == 0;
                return i;
            }

            exists = false;
            return _grants.Count;
        }
    }
}
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilitySetTests"`
Expected: PASS (7건).

- [ ] **Step 7: 빌드 경고 0 확인**

Run: `dotnet build Bun3.sln`
Expected: `0 Warning(s)`.

- [ ] **Step 8: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Abilities/ common/tests/Bun3.Gameplay.Tests/AbilitySetTests.cs
git commit -m "✨ AbilitySet 추가: 부여 소스 4종·참조 카운트·최대 레벨·스냅샷" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 4: Unit — EffectTarget 합성

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Unit.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/UnitSnapshot.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/UnitTests.cs`

**Interfaces:**
- Consumes: Task 3의 `AbilitySet`, `AbilitySetSnapshot`
- Produces:
  - `public sealed class Unit` — 생성자 `Unit(TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds, TagCatalog tagCatalog, ulong ownerId = 0)`; 프로퍼티 `TargetId Id`, `EffectTarget Effects`, `AbilitySet Abilities`, `ulong OwnerId { get; set; }`, `AttributeSet Attributes`, `TagCountContainer Tags`; 메서드 `UnitSnapshot CreateSnapshot()`, `void RestoreSnapshot(UnitSnapshot snapshot, EffectCatalog catalog)`
  - `public sealed class UnitSnapshot` (opaque, 멤버 internal)

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/UnitTests.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class UnitTests
{
    private static Unit CreateUnit(EffectTestKit kit, ulong id, ulong ownerId = 0)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog, ownerId);
    }

    [Test]
    public void Forwarding_properties_expose_the_same_instances_as_the_effect_target()
    {
        var kit = EffectTestKit.Create();
        var unit = CreateUnit(kit, 1);

        Assert.That(unit.Attributes, Is.SameAs(unit.Effects.Attributes));
        Assert.That(unit.Tags, Is.SameAs(unit.Effects.Tags));
        Assert.That(unit.Effects.Id, Is.EqualTo(unit.Id));
    }

    [Test]
    public void Owner_defaults_to_zero_meaning_unowned()
    {
        var kit = EffectTestKit.Create();

        Assert.That(CreateUnit(kit, 1).OwnerId, Is.Zero);
        Assert.That(CreateUnit(kit, 2, ownerId: 99).OwnerId, Is.EqualTo(99UL));
    }

    [Test]
    public void Snapshot_round_trip_restores_attributes_abilities_and_owner()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var catalog = kit.Catalog;

        var unit = CreateUnit(kit, 1, ownerId: 7);
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 200);
        unit.Attributes.SetBase(EffectTestKit.Hp, 150);
        unit.Abilities.Grant(abilityId: 3, GrantSource.Archetype(), level: 2);

        var snapshot = unit.CreateSnapshot();

        unit.Attributes.SetBase(EffectTestKit.Hp, 10);
        unit.Abilities.Revoke(GrantSource.Archetype());
        unit.OwnerId = 0;

        unit.RestoreSnapshot(snapshot, catalog);

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)150));
        Assert.That(unit.Abilities.GetLevel(3), Is.EqualTo(2));
        Assert.That(unit.OwnerId, Is.EqualTo(7UL));
    }

    [Test]
    public void Restoring_a_snapshot_from_another_unit_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var catalog = kit.Catalog;

        var snapshot = CreateUnit(kit, 1).CreateSnapshot();
        var other = CreateUnit(kit, 2);

        Assert.That(() => other.RestoreSnapshot(snapshot, catalog), Throws.ArgumentException);
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~UnitTests"`
Expected: FAIL — `Unit` 타입 없음.

- [ ] **Step 3: `UnitSnapshot` 을 만든다**

`common/src/com.bun3.gameplay/Runtime/UnitSnapshot.cs`:

```csharp
#nullable enable
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    /// <summary>
    /// Immutable deep copy of one unit's deterministic state, created by <see cref="Unit.CreateSnapshot"/>.
    /// Composes the effect-target snapshot with the ability grants and the owner id — the same
    /// composition the replication schema uses. Opaque token: callers hold and pass instances
    /// without inspecting them.
    /// </summary>
    public sealed class UnitSnapshot
    {
        internal UnitSnapshot(TargetId unitId, EffectTargetSnapshot effects, AbilitySetSnapshot abilities, ulong ownerId)
        {
            UnitId = unitId;
            Effects = effects;
            Abilities = abilities;
            OwnerId = ownerId;
        }

        /// <summary>Unit this snapshot belongs to. Used to reject restoring onto another unit.</summary>
        internal TargetId UnitId { get; }

        /// <summary>Attribute and active-effect state.</summary>
        internal EffectTargetSnapshot Effects { get; }

        /// <summary>Ability grants.</summary>
        internal AbilitySetSnapshot Abilities { get; }

        /// <summary>Owner identity, 0 when unowned.</summary>
        internal ulong OwnerId { get; }
    }
}
```

- [ ] **Step 4: `Unit` 을 만든다**

`common/src/com.bun3.gameplay/Runtime/Unit.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay
{
    /// <summary>
    /// One acting subject in a world — an effect target plus the abilities granted to it and its
    /// owner identity. The effect target is composed rather than inherited so that the replication
    /// schema, dirty tracking, and snapshots all keep the same boundaries, and so that a partial
    /// snapshot cannot be expressed.
    /// </summary>
    public sealed class Unit
    {
        /// <summary>Creates a unit from an id, attribute registry, archetype-declared attributes, and tag catalog.</summary>
        /// <param name="id">Unit id, also its target id.</param>
        /// <param name="registry">Registry holding the attribute definitions.</param>
        /// <param name="attributeIds">Attribute ids this unit declares.</param>
        /// <param name="tagCatalog">Tag catalog used for owned-tag counting.</param>
        /// <param name="ownerId">Owner identity; 0 means unowned.</param>
        public Unit(
            TargetId id, AttributeRegistry registry, ReadOnlySpan<ushort> attributeIds,
            TagCatalog tagCatalog, ulong ownerId = 0)
        {
            Id = id;
            Effects = new EffectTarget(id, registry, attributeIds, tagCatalog);
            Abilities = new AbilitySet();
            OwnerId = ownerId;
        }

        /// <summary>Unit id.</summary>
        public TargetId Id { get; }

        /// <summary>Attribute set, owned tags, and active effects of this unit.</summary>
        public EffectTarget Effects { get; }

        /// <summary>Abilities granted to this unit.</summary>
        public AbilitySet Abilities { get; }

        /// <summary>Owner identity; 0 means unowned. The simulation assigns no meaning beyond identity.</summary>
        public ulong OwnerId { get; set; }

        /// <summary>Attribute set of this unit.</summary>
        public AttributeSet Attributes => Effects.Attributes;

        /// <summary>Tags this unit owns, with their counts.</summary>
        public TagCountContainer Tags => Effects.Tags;

        /// <summary>Creates a deep-copy snapshot of this unit's deterministic state.</summary>
        /// <returns>Snapshot of effects, ability grants, and owner identity.</returns>
        public UnitSnapshot CreateSnapshot() =>
            new UnitSnapshot(Id, Effects.CreateSnapshot(), Abilities.CreateSnapshot(), OwnerId);

        /// <summary>Restores this unit from a snapshot.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <param name="catalog">Effect catalog used to reclaim and regrant tags; must be the catalog used at snapshot time.</param>
        /// <exception cref="ArgumentException">Thrown when the snapshot belongs to another unit.</exception>
        public void RestoreSnapshot(UnitSnapshot snapshot, EffectCatalog catalog)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));
            if (snapshot.UnitId != Id)
                throw new ArgumentException("Cannot restore a snapshot taken from a different unit.", nameof(snapshot));

            Effects.RestoreSnapshot(snapshot.Effects, catalog);
            Abilities.RestoreSnapshot(snapshot.Abilities);
            OwnerId = snapshot.OwnerId;
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~UnitTests"`
Expected: PASS (4건).

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Unit.cs common/src/com.bun3.gameplay/Runtime/UnitSnapshot.cs common/tests/Bun3.Gameplay.Tests/UnitTests.cs
git commit -m "✨ Unit 추가: EffectTarget 합성 + AbilitySet + OwnerId, 스냅샷 왕복" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 5: MagnitudeCompiler 추출 (순수 리팩터)

Ability의 코스트도 `MagnitudeDef`를 쓰므로 크기 컴파일 로직을 두 빌더가 공유해야 한다. 동작은 한 줄도 바뀌지 않는다 — 기존 테스트 전부가 게이트다.

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/MagnitudeCompiler.cs`
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalogBuilder.cs` (해당 메서드 제거 + 호출부 변경)
- Test: 기존 테스트 그대로 (신규 테스트 없음 — 동작 불변 리팩터)

**Interfaces:**
- Consumes: Task 1의 `BigNumFormula.TryCompile` / `CompiledFormula`
- Produces (전부 `internal static`, 네임스페이스 `Bun3.Gameplay.Effects`):
  - `MagnitudeCompiler.Resolve(MagnitudeDef def, string ownerName, int maxLevel, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)` → `(Operand? Base, Operand? PerLevel, IMagnitudeCalc? Calc, BigNum[]? ByLevel, LevelTail Tail, BigNum Increment)`
  - `MagnitudeCompiler.ResolveMagnitudeCalc(string calcTag, string ownerName, TagCatalog tags, SeamRegistry seams)` → `IMagnitudeCalc`
  - `MagnitudeCompiler.ResolveExecutionCalc(string calcTag, string ownerName, TagCatalog tags, SeamRegistry seams)` → `IExecutionCalc`
  - `MagnitudeCompiler.ResolveTargetSelector(string selectorTag, string ownerName, TagCatalog tags, SeamRegistry seams)` → `ITargetSelector`
  - `MagnitudeCompiler.ResolveTags(List<string> paths, string ownerName, TagCatalog tags, string what)` → `GameplayTag[]`
  - `MagnitudeCompiler.ValidateOperandAttribute(Operand operand, string ownerName, AttributeRegistry attributes)` → `void`

- [ ] **Step 1: 이동 전 기준선을 기록한다**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
Expected: PASS. 통과 개수를 적어둔다 — Step 5에서 같은 수가 나와야 한다.

- [ ] **Step 2: `MagnitudeCompiler.cs` 를 만들고 메서드를 옮긴다**

`EffectCatalogBuilder.cs`에서 아래 `private static` 메서드를 **본문 그대로** 새 파일
`Runtime/Effects/MagnitudeCompiler.cs`의 `internal static class MagnitudeCompiler` 안으로 옮긴다:

| 원래 이름 | 새 이름 | 시그니처 변경 |
|---|---|---|
| `ResolveMagnitude` | `Resolve` | `EffectSpec spec` → `string ownerName, int maxLevel` |
| `CompilePerLevelValues` | `CompilePerLevelValues` (private) | `EffectSpec spec` → `string ownerName, int maxLevel` |
| `CompileFormula` | `CompileFormula` (private) | `EffectSpec spec` → `string ownerName, int maxLevel` |
| `CompileCurveKeys` | `CompileCurveKeys` (private) | `EffectSpec spec` → `string ownerName, int maxLevel` |
| `ResolveMagnitudeCalc` | `ResolveMagnitudeCalc` | `string specName` → `string ownerName` |
| `ResolveExecutionCalc` | `ResolveExecutionCalc` | `string specName` → `string ownerName` |
| `ResolveTargetSelector` | `ResolveTargetSelector` | `string specName` → `string ownerName` |
| `ResolveTags` | `ResolveTags` | `string specName` → `string ownerName` |
| `ValidateOperandAttribute` | `ValidateOperandAttribute` | `string specName` → `string ownerName` |

본문에서 `spec.Name` → `ownerName`, `spec.MaxLevel` → `maxLevel`, `specName` → `ownerName` 로 치환한다.
**예외 메시지의 `$"Effect '{...}'"` 접두사는 `$"'{ownerName}'"` 로 바꾸지 않는다** — 기존 테스트가
메시지를 검사할 수 있으므로 `Effect '` 접두사를 그대로 유지하고, Ability 쪽 호출자는
`ownerName` 에 `"Ability XYZ"` 형태로 이름을 넘겨 문맥을 구분한다.

파일 헤더:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// Compiles authoring-time magnitude, seam-reference, and tag-reference declarations into their
    /// runtime forms. Shared by the effect and ability catalog builders — both use the same
    /// <see cref="MagnitudeDef"/> vocabulary, so the notation rules live in one place.
    /// </summary>
    internal static class MagnitudeCompiler
    {
        // ... moved methods ...
    }
}
```

- [ ] **Step 3: `EffectCatalogBuilder` 의 호출부를 고친다**

옮긴 메서드의 호출을 `MagnitudeCompiler.X(...)` 로 바꾸고, `spec` 을 넘기던 자리에
`spec.Name, spec.MaxLevel` 을 넘긴다. 예:

```csharp
            var (magBase, magPerLevel, magCalc, magByLevel, magTail, magIncrement) =
                MagnitudeCompiler.Resolve(def.Magnitude, spec.Name, spec.MaxLevel, tags, seams, attributes);
```

```csharp
            var grantedTags = MagnitudeCompiler.ResolveTags(spec.GrantedTags, spec.Name, tags, "GrantedTags");
```

- [ ] **Step 4: 빌드 확인**

Run: `dotnet build Bun3.sln`
Expected: `0 Warning(s)`, `0 Error(s)`.

- [ ] **Step 5: 전체 테스트가 기준선과 동일한지 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
Expected: PASS, 통과 개수가 Step 1과 동일. **하나라도 줄거나 실패하면 이동 중 본문이 바뀐 것이다.**

- [ ] **Step 6: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Effects/MagnitudeCompiler.cs common/src/com.bun3.gameplay/Runtime/Effects/EffectCatalogBuilder.cs
git commit -m "♻️ 크기·확장점·태그 컴파일을 MagnitudeCompiler로 추출: Ability 빌더와 공유" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 6: AbilityDef · AbilityCatalog · AbilityCatalogBuilder

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityDef.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityActivationResult.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalog.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalogBuilder.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/AbilityCatalogBuilderTests.cs`

**Interfaces:**
- Consumes: Task 5의 `MagnitudeCompiler`; 기존 `EffectCatalog.GetRequiredId(string)`, `ConditionDef`, `MagnitudeDef`, `ChainLevelRule`
- Produces:
  - `public sealed class AbilityDef` (아래 필드), `public sealed class CostDef`, `public sealed class AbilityEffectDef`
  - `public enum AbilityActivationResult : byte`
  - `public sealed class AbilityCatalog` — `int Count`, `int GetRequiredId(string name)`, `bool TryGetId(string, out int)`, `internal CompiledAbility GetAbility(int id)`
  - `public sealed class AbilityCatalogBuilder` — `void Add(AbilityDef def)`, `AbilityCatalog Build(TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects)`
  - `internal sealed class CompiledAbility`, `internal readonly struct CompiledCost`, `internal sealed class CompiledAbilityEffect`

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/AbilityCatalogBuilderTests.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityCatalogBuilderTests
{
    private static AbilityDef Minimal(string name) => new AbilityDef { Name = name };

    [Test]
    public void Build_assigns_dense_ids_in_insertion_order()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(Minimal("alpha"));
        builder.Add(Minimal("beta"));

        var catalog = builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(catalog.Count, Is.EqualTo(2));
        Assert.That(catalog.GetRequiredId("alpha"), Is.EqualTo(0));
        Assert.That(catalog.GetRequiredId("beta"), Is.EqualTo(1));
    }

    [Test]
    public void Duplicate_ability_names_are_rejected()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(Minimal("dup"));
        builder.Add(Minimal("dup"));

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("dup"));
    }

    [Test]
    public void Empty_ability_name_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var builder = new AbilityCatalogBuilder();
        builder.Add(new AbilityDef());

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("name"));
    }

    [Test]
    public void Unknown_referenced_effect_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef { EffectName = "missing" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("missing"));
    }

    [Test]
    public void Unknown_cooldown_effect_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.CooldownEffect = "nope";
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("nope"));
    }

    [Test]
    public void Cost_on_an_unregistered_attribute_is_rejected()
    {
        var kit = EffectTestKit.Create();
        var ability = Minimal("caster");
        ability.Costs.Add(new CostDef
        {
            AttributeId = 250,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(1) },
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException);
    }

    [Test]
    public void Fixed_level_rule_beyond_max_level_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.MaxLevel = 3;
        ability.Effects.Add(new AbilityEffectDef
        {
            EffectName = "bolt", LevelRule = ChainLevelRule.Fixed, FixedLevel = 9,
        });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InvalidOperationException.With.Message.Contains("FixedLevel"));
    }

    [Test]
    public void Unknown_selector_tag_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        var ability = Minimal("caster");
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", SelectorTag = "selector.nope" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);

        Assert.That(
            () => builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog),
            Throws.InstanceOf<Exception>());
    }
}
```

`EffectTestKit`에 접근자 3개를 추가한다 (`common/tests/Bun3.Gameplay.Tests/EffectTestKit.cs`, `public TagCatalog TagCatalog` 근처):

```csharp
    /// <summary>Seam registry backing this kit.</summary>
    public SeamRegistry Seams => BuiltSeams;

    /// <summary>Attribute registry backing this kit.</summary>
    public AttributeRegistry Attributes => BuiltAttributes;
```

`EffectTestKit` 내부에서 `Build`가 만드는 `SeamRegistry`/`AttributeRegistry`를 필드로 보관해
`BuiltSeams`/`BuiltAttributes` 로 노출한다 (`BuiltCatalog`가 이미 쓰는 지연 빌드 패턴과 동일하게).

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityCatalogBuilderTests"`
Expected: FAIL — `AbilityDef` 없음.

- [ ] **Step 3: `AbilityActivationResult` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilityActivationResult.cs`:

```csharp
#nullable enable
namespace Bun3.Gameplay.Abilities
{
    /// <summary>Outcome of an ability activation attempt. Gate failures are results, not exceptions.</summary>
    public enum AbilityActivationResult : byte
    {
        /// <summary>Gates passed; costs were paid and the ability's effects were enqueued.</summary>
        Success = 0,
        /// <summary>The unit id did not resolve.</summary>
        UnknownUnit = 1,
        /// <summary>The ability id is outside the catalog.</summary>
        UnknownAbility = 2,
        /// <summary>No origin currently grants this ability to the unit.</summary>
        NotGranted = 3,
        /// <summary>The unit lacks a tag the ability requires.</summary>
        MissingTag = 4,
        /// <summary>The unit owns a tag the ability blocks on — cooldown is reported here.</summary>
        BlockedByTag = 5,
        /// <summary>An activation condition did not hold.</summary>
        ConditionFailed = 6,
        /// <summary>At least one cost exceeded the unit's current value.</summary>
        CostInsufficient = 7,
    }
}
```

- [ ] **Step 4: `AbilityDef` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilityDef.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Resource an activation consumes, deducted immediately when every gate passes.</summary>
    public sealed class CostDef
    {
        /// <summary>Id of the attribute to consume.</summary>
        public ushort AttributeId { get; set; }

        /// <summary>Amount consumed. Evaluated against the activating unit at activation time.</summary>
        public MagnitudeDef Magnitude { get; set; } = new MagnitudeDef();
    }

    /// <summary>One effect an activation applies, with the rule choosing its targets and level.</summary>
    public sealed class AbilityEffectDef
    {
        /// <summary>Name of the effect to apply.</summary>
        public string EffectName { get; set; } = string.Empty;

        /// <summary>Target-selector tag registered in SeamRegistry, or null (use the activating unit).</summary>
        public string? SelectorTag { get; set; }

        /// <summary>Parameters passed to target selection.</summary>
        public List<BigNum> SelectorParams { get; set; } = new List<BigNum>();

        /// <summary>Rule deciding the applied effect's level.</summary>
        public ChainLevelRule LevelRule { get; set; }

        /// <summary>Level to use when <see cref="LevelRule"/> is Fixed.</summary>
        public int FixedLevel { get; set; }
    }

    /// <summary>
    /// Authoring spec for one ability. A property bag filled by a loader, then validated and
    /// compiled by <see cref="AbilityCatalogBuilder"/>. Abilities carry no logic — activation
    /// applies effects, and anything beyond that belongs in an effect execution seam.
    /// </summary>
    public sealed class AbilityDef
    {
        /// <summary>Ability name; must be unique within the catalog.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Maximum level. 0 means undeclared, which forbids level-table cost notations.</summary>
        public int MaxLevel { get; set; }

        /// <summary>Tags classifying the ability itself.</summary>
        public List<string> AssetTags { get; set; } = new List<string>();

        /// <summary>Tags the owner must have for activation to pass the gate.</summary>
        public List<string> RequiredTags { get; set; } = new List<string>();

        /// <summary>Tags that block activation while the owner has them (self or any descendant).</summary>
        public List<string> BlockedTags { get; set; } = new List<string>();

        /// <summary>Activation conditions; all must hold to activate.</summary>
        public List<ConditionDef> ActivationConditions { get; set; } = new List<ConditionDef>();

        /// <summary>Resources consumed on a successful activation.</summary>
        public List<CostDef> Costs { get; set; } = new List<CostDef>();

        /// <summary>Name of the effect applied to the activating unit on success, or null for no cooldown.
        /// Its GrantedTags paired with <see cref="BlockedTags"/> are what make the cooldown a gate.</summary>
        public string? CooldownEffect { get; set; }

        /// <summary>Effects applied on a successful activation.</summary>
        public List<AbilityEffectDef> Effects { get; set; } = new List<AbilityEffectDef>();
    }
}
```

- [ ] **Step 5: `AbilityCatalog` 을 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalog.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>One compiled cost row.</summary>
    internal readonly struct CompiledCost
    {
        internal CompiledCost(ushort attributeId, CompiledMagnitude magnitude)
        {
            AttributeId = attributeId;
            Magnitude = magnitude;
        }

        internal ushort AttributeId { get; }

        internal CompiledMagnitude Magnitude { get; }
    }

    /// <summary>One compiled effect application of an ability.</summary>
    internal sealed class CompiledAbilityEffect
    {
        internal CompiledAbilityEffect(
            int effectId, ITargetSelector? selector, BigNum[] selectorParams,
            ChainLevelRule levelRule, int fixedLevel)
        {
            EffectId = effectId;
            Selector = selector;
            SelectorParams = selectorParams;
            LevelRule = levelRule;
            FixedLevel = fixedLevel;
        }

        internal int EffectId { get; }

        internal ITargetSelector? Selector { get; }

        internal BigNum[] SelectorParams { get; }

        internal ChainLevelRule LevelRule { get; }

        internal int FixedLevel { get; }
    }

    /// <summary>One compiled ability.</summary>
    internal sealed class CompiledAbility
    {
        internal CompiledAbility(
            string name, int maxLevel, GameplayTag[] assetTags, GameplayTag[] requiredTags,
            GameplayTag[] blockedTags, CompiledCondition[] activationConditions, CompiledCost[] costs,
            int cooldownEffectId, CompiledAbilityEffect[] effects)
        {
            Name = name;
            MaxLevel = maxLevel;
            AssetTags = assetTags;
            RequiredTags = requiredTags;
            BlockedTags = blockedTags;
            ActivationConditions = activationConditions;
            Costs = costs;
            CooldownEffectId = cooldownEffectId;
            Effects = effects;
        }

        internal string Name { get; }

        internal int MaxLevel { get; }

        internal GameplayTag[] AssetTags { get; }

        internal GameplayTag[] RequiredTags { get; }

        internal GameplayTag[] BlockedTags { get; }

        internal CompiledCondition[] ActivationConditions { get; }

        internal CompiledCost[] Costs { get; }

        /// <summary>Compiled cooldown effect id, or -1 when the ability has no cooldown.</summary>
        internal int CooldownEffectId { get; }

        internal CompiledAbilityEffect[] Effects { get; }
    }

    /// <summary>Immutable compiled ability catalog produced by <see cref="AbilityCatalogBuilder"/>.</summary>
    public sealed class AbilityCatalog
    {
        private readonly CompiledAbility[] _abilities;
        private readonly Dictionary<string, int> _nameToId;

        internal AbilityCatalog(CompiledAbility[] abilities, Dictionary<string, int> nameToId)
        {
            _abilities = abilities;
            _nameToId = nameToId;
        }

        /// <summary>Number of compiled abilities.</summary>
        public int Count => _abilities.Length;

        /// <summary>Resolves an ability name to its dense id.</summary>
        /// <param name="name">Ability name.</param>
        /// <returns>Dense ability id.</returns>
        /// <exception cref="KeyNotFoundException">Thrown when the name is not in the catalog.</exception>
        public int GetRequiredId(string name)
        {
            if (_nameToId.TryGetValue(name, out var id)) return id;
            throw new KeyNotFoundException($"Ability '{name}' is not in the catalog.");
        }

        /// <summary>Tries to resolve an ability name to its dense id.</summary>
        /// <param name="name">Ability name.</param>
        /// <param name="id">Resolved dense id.</param>
        /// <returns>True when the name is in the catalog.</returns>
        public bool TryGetId(string name, out int id) => _nameToId.TryGetValue(name, out id);

        /// <summary>Returns whether the id addresses a compiled ability.</summary>
        /// <param name="id">Dense ability id.</param>
        /// <returns>True when the id is in range.</returns>
        public bool Contains(int id) => id >= 0 && id < _abilities.Length;

        internal CompiledAbility GetAbility(int id) => _abilities[id];
    }
}
```

- [ ] **Step 6: `AbilityCatalogBuilder` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Abilities/AbilityCatalogBuilder.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Abilities
{
    /// <summary>Collects <see cref="AbilityDef"/> declarations and compiles them into an
    /// <see cref="AbilityCatalog"/>. Runs after the effect catalog — abilities reference effects by name.</summary>
    public sealed class AbilityCatalogBuilder
    {
        private readonly List<AbilityDef> _defs = new List<AbilityDef>();

        /// <summary>Adds an ability declaration.</summary>
        /// <param name="def">Declaration to add.</param>
        public void Add(AbilityDef def)
        {
            if (def is null) throw new ArgumentNullException(nameof(def));
            _defs.Add(def);
        }

        /// <summary>Validates and compiles every added declaration.</summary>
        /// <param name="tags">Tag catalog resolving tag paths.</param>
        /// <param name="seams">Seam registry resolving selector tags.</param>
        /// <param name="attributes">Attribute registry validating cost and operand references.</param>
        /// <param name="effects">Effect catalog resolving referenced effect names.</param>
        /// <returns>Compiled catalog.</returns>
        /// <exception cref="InvalidOperationException">Thrown on any validation failure.</exception>
        public AbilityCatalog Build(
            TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects)
        {
            if (tags is null) throw new ArgumentNullException(nameof(tags));
            if (seams is null) throw new ArgumentNullException(nameof(seams));
            if (attributes is null) throw new ArgumentNullException(nameof(attributes));
            if (effects is null) throw new ArgumentNullException(nameof(effects));

            var nameToId = new Dictionary<string, int>(_defs.Count, StringComparer.Ordinal);
            for (var i = 0; i < _defs.Count; i++)
            {
                var name = _defs[i].Name;
                if (string.IsNullOrEmpty(name))
                    throw new InvalidOperationException($"Ability at index {i} has an empty name.");
                if (nameToId.ContainsKey(name))
                    throw new InvalidOperationException($"Ability '{name}' is declared more than once.");
                nameToId.Add(name, i);
            }

            var compiled = new CompiledAbility[_defs.Count];
            for (var i = 0; i < _defs.Count; i++)
            {
                compiled[i] = Compile(_defs[i], tags, seams, attributes, effects);
            }

            return new AbilityCatalog(compiled, nameToId);
        }

        private static CompiledAbility Compile(
            AbilityDef def, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes, EffectCatalog effects)
        {
            var owner = $"Ability '{def.Name}'";

            var costs = new CompiledCost[def.Costs.Count];
            for (var i = 0; i < def.Costs.Count; i++)
            {
                var cost = def.Costs[i];
                if (!attributes.Contains(cost.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: cost references unregistered attribute {cost.AttributeId}.");
                }

                var (magBase, magPerLevel, magCalc, magByLevel, magTail, magIncrement) =
                    MagnitudeCompiler.Resolve(cost.Magnitude, owner, def.MaxLevel, tags, seams, attributes);
                costs[i] = new CompiledCost(
                    cost.AttributeId,
                    new CompiledMagnitude(magBase, magPerLevel, magCalc, magByLevel, magTail, magIncrement));
            }

            var abilityEffects = new CompiledAbilityEffect[def.Effects.Count];
            for (var i = 0; i < def.Effects.Count; i++)
            {
                var entry = def.Effects[i];
                if (!effects.TryGetId(entry.EffectName, out var effectId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: references unknown effect '{entry.EffectName}'.");
                }

                if (entry.LevelRule == ChainLevelRule.Fixed
                    && (entry.FixedLevel < 1 || (def.MaxLevel > 0 && entry.FixedLevel > def.MaxLevel)))
                {
                    throw new InvalidOperationException(
                        $"{owner}: FixedLevel {entry.FixedLevel} is outside 1..{def.MaxLevel}.");
                }

                var selector = string.IsNullOrEmpty(entry.SelectorTag)
                    ? null
                    : MagnitudeCompiler.ResolveTargetSelector(entry.SelectorTag!, owner, tags, seams);

                abilityEffects[i] = new CompiledAbilityEffect(
                    effectId, selector, entry.SelectorParams.ToArray(), entry.LevelRule, entry.FixedLevel);
            }

            var cooldownEffectId = -1;
            if (!string.IsNullOrEmpty(def.CooldownEffect))
            {
                if (!effects.TryGetId(def.CooldownEffect!, out cooldownEffectId))
                {
                    throw new InvalidOperationException(
                        $"{owner}: references unknown cooldown effect '{def.CooldownEffect}'.");
                }
            }

            var conditions = new CompiledCondition[def.ActivationConditions.Count];
            for (var i = 0; i < def.ActivationConditions.Count; i++)
            {
                var condition = def.ActivationConditions[i];
                MagnitudeCompiler.ValidateOperandAttribute(condition.Left, owner, attributes);
                MagnitudeCompiler.ValidateOperandAttribute(condition.Right, owner, attributes);
                conditions[i] = new CompiledCondition(condition.Left, condition.Op, condition.Right);
            }

            return new CompiledAbility(
                def.Name,
                def.MaxLevel,
                MagnitudeCompiler.ResolveTags(def.AssetTags, owner, tags, "AssetTags"),
                MagnitudeCompiler.ResolveTags(def.RequiredTags, owner, tags, "RequiredTags"),
                MagnitudeCompiler.ResolveTags(def.BlockedTags, owner, tags, "BlockedTags"),
                conditions,
                costs,
                cooldownEffectId,
                abilityEffects);
        }
    }
}
```

`AttributeRegistry`에 `Contains(ushort)` 가 없으면 추가한다 (`Runtime/Attributes/AttributeRegistry.cs`):

```csharp
        /// <summary>Returns whether the attribute id is registered.</summary>
        /// <param name="attributeId">Attribute id to check.</param>
        /// <returns>True when the id is registered.</returns>
        public bool Contains(ushort attributeId) => TryGetIndex(attributeId, out _);
```

기존 내부 조회 메서드 이름이 다르면 그 메서드를 감싸는 형태로 맞춘다.

- [ ] **Step 7: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityCatalogBuilderTests"`
Expected: PASS (8건).

- [ ] **Step 8: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 9: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Abilities/ common/src/com.bun3.gameplay/Runtime/Attributes/AttributeRegistry.cs common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ AbilityDef·AbilityCatalog·Builder 추가: 게이트·코스트·쿨다운 참조 검증" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 7: World — 레지스트리 · resolver · 스폰/디스폰 큐

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/World.cs`
- Test: `common/tests/Bun3.Gameplay.Tests/WorldTests.cs`

**Interfaces:**
- Consumes: Task 4의 `Unit`, Task 6의 `AbilityCatalog`; 기존 `EffectCatalog`, `EffectPipeline`, `IEffectTargetResolver`, `IRng`
- Produces:
  - `public sealed partial class World : IEffectTargetResolver` — 생성자 `World(EffectCatalog effects, AbilityCatalog abilities, IRng rng, int applyBudgetPerTick = 64)`
  - `EffectPipeline Pipeline`, `long CurrentTick`, `int UnitCount`
  - `bool TryResolve(TargetId, out EffectTarget?)`, `IReadOnlyList<TargetId> TargetIds`
  - `void Spawn(Unit unit)`, `bool Despawn(TargetId id)`, `bool TryGetUnit(TargetId id, out Unit? unit)`
  - `void Tick()`
  - `partial` 선언 — Task 12가 `World.Ability.cs` 를 붙인다

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/WorldTests.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class WorldTests
{
    private static Unit MakeUnit(EffectTestKit kit, ulong id)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog);
    }

    private static World MakeWorld(EffectTestKit kit)
    {
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        return new World(kit.Catalog, abilities, new XorShiftRng(12345));
    }

    [Test]
    public void Spawn_is_visible_only_after_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 1));

        Assert.That(world.UnitCount, Is.Zero, "spawn must not mutate the registry mid-frame");
        world.Tick();
        Assert.That(world.UnitCount, Is.EqualTo(1));
        Assert.That(world.TryGetUnit(new TargetId(1), out _), Is.True);
    }

    [Test]
    public void Registry_and_target_ids_stay_in_ascending_order()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        world.Spawn(MakeUnit(kit, 30));
        world.Spawn(MakeUnit(kit, 10));
        world.Spawn(MakeUnit(kit, 20));
        world.Tick();

        Assert.That(world.TargetIds.Count, Is.EqualTo(3));
        for (var i = 1; i < world.TargetIds.Count; i++)
        {
            Assert.That(world.TargetIds[i], Is.GreaterThan(world.TargetIds[i - 1]));
        }
    }

    [Test]
    public void Despawn_is_visible_only_after_the_next_tick()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Despawn(new TargetId(1));

        Assert.That(world.UnitCount, Is.EqualTo(1));
        world.Tick();
        Assert.That(world.UnitCount, Is.Zero);
        Assert.That(world.TryResolve(new TargetId(1), out _), Is.False);
    }

    [Test]
    public void Resolver_returns_the_units_effect_target()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();

        Assert.That(world.TryResolve(new TargetId(1), out var resolved), Is.True);
        Assert.That(resolved, Is.SameAs(unit.Effects));
    }

    [Test]
    public void Spawning_a_duplicate_id_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Spawn(MakeUnit(kit, 1));

        Assert.That(() => world.Tick(), Throws.InvalidOperationException);
    }

    [Test]
    public void World_tick_advances_the_pipeline_tick_counter_once()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);

        Assert.That(world.CurrentTick, Is.Zero);
        world.Tick();
        world.Tick();
        Assert.That(world.CurrentTick, Is.EqualTo(2));
        Assert.That(world.CurrentTick, Is.EqualTo(world.Pipeline.CurrentTick));
    }

    [Test]
    public void Effects_applied_to_a_spawned_unit_run_through_the_worlds_pipeline()
    {
        var kit = EffectTestKit.Create();
        var damage = EffectTestKit.MinimalInstant("hit");
        damage.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = Bun3.Gameplay.Attributes.AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Bun3.Gameplay.Attributes.Operand.Constant(-30) },
        });
        kit.AddSpec(damage);
        var world = MakeWorld(kit);

        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();

        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 100);
        world.Pipeline.EnqueueApply(kit.SpecId("hit"), new TargetId(0), unit.Id);
        world.Tick();

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)70));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~WorldTests"`
Expected: FAIL — `World` 타입 없음.

- [ ] **Step 3: `World` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/World.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;

namespace Bun3.Gameplay
{
    /// <summary>
    /// Unit registry and tick driver for one simulation. Implements
    /// <see cref="IEffectTargetResolver"/> for the pipeline it owns, so effects address units by
    /// <see cref="TargetId"/> without knowing about units at all. Structural changes (spawn,
    /// despawn) are queued and applied only at a phase boundary — the registry must never mutate
    /// while a tick iterates it. Composed of this file plus <c>World.Ability.cs</c>.
    /// </summary>
    public sealed partial class World : IEffectTargetResolver
    {
        private readonly List<Unit> _units = new List<Unit>();
        private readonly List<TargetId> _unitIds = new List<TargetId>();
        private readonly List<Unit> _spawnQueue = new List<Unit>();
        private readonly List<TargetId> _despawnQueue = new List<TargetId>();
        private readonly EffectCatalog _effects;
        private readonly AbilityCatalog _abilities;
        private readonly IRng _rng;

        /// <summary>Creates a world over the given catalogs and random source.</summary>
        /// <param name="effects">Compiled effect catalog.</param>
        /// <param name="abilities">Compiled ability catalog.</param>
        /// <param name="rng">Seeded random source; shared with the pipeline.</param>
        /// <param name="applyBudgetPerTick">Maximum effect applications drained per tick.</param>
        public World(EffectCatalog effects, AbilityCatalog abilities, IRng rng, int applyBudgetPerTick = 64)
        {
            _effects = effects ?? throw new ArgumentNullException(nameof(effects));
            _abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
            _rng = rng ?? throw new ArgumentNullException(nameof(rng));
            Pipeline = new EffectPipeline(effects, this, rng, applyBudgetPerTick);
        }

        /// <summary>Effect pipeline this world drives.</summary>
        public EffectPipeline Pipeline { get; }

        /// <summary>Ticks processed so far. The pipeline owns the counter; this world keeps none of its own.</summary>
        public long CurrentTick => Pipeline.CurrentTick;

        /// <summary>Number of registered units.</summary>
        public int UnitCount => _units.Count;

        /// <summary>Registered unit ids, ascending.</summary>
        public IReadOnlyList<TargetId> TargetIds => _unitIds;

        /// <summary>Resolves a unit's effect target by id.</summary>
        /// <param name="id">Target id to resolve.</param>
        /// <param name="target">Resolved effect target.</param>
        /// <returns>True when a unit with that id is registered.</returns>
        public bool TryResolve(TargetId id, out EffectTarget? target)
        {
            if (TryGetUnit(id, out var unit))
            {
                target = unit!.Effects;
                return true;
            }

            target = null;
            return false;
        }

        /// <summary>Looks up a registered unit by id.</summary>
        /// <param name="id">Unit id.</param>
        /// <param name="unit">Resolved unit.</param>
        /// <returns>True when the unit is registered.</returns>
        public bool TryGetUnit(TargetId id, out Unit? unit)
        {
            var index = IndexOf(id);
            unit = index >= 0 ? _units[index] : null;
            return index >= 0;
        }

        /// <summary>Queues a unit for registration. It becomes visible at the start of the next
        /// <see cref="Tick"/>.</summary>
        /// <param name="unit">Unit to register.</param>
        public void Spawn(Unit unit)
        {
            if (unit is null) throw new ArgumentNullException(nameof(unit));
            _spawnQueue.Add(unit);
        }

        /// <summary>Queues a unit for removal. It disappears at the start of the next <see cref="Tick"/>.</summary>
        /// <param name="id">Unit id to remove.</param>
        /// <returns>True when a matching unit is currently registered.</returns>
        public bool Despawn(TargetId id)
        {
            _despawnQueue.Add(id);
            return IndexOf(id) >= 0;
        }

        /// <summary>Applies queued structural changes, then advances the pipeline by one tick.</summary>
        public void Tick()
        {
            FlushStructuralChanges();
            Pipeline.Tick();
        }

        private void FlushStructuralChanges()
        {
            for (var i = 0; i < _despawnQueue.Count; i++)
            {
                var index = IndexOf(_despawnQueue[i]);
                if (index < 0) continue;
                _units.RemoveAt(index);
                _unitIds.RemoveAt(index);
            }

            _despawnQueue.Clear();

            for (var i = 0; i < _spawnQueue.Count; i++)
            {
                var unit = _spawnQueue[i];
                var index = IndexOf(unit.Id);
                if (index >= 0)
                {
                    _spawnQueue.Clear();
                    throw new InvalidOperationException($"Unit id {unit.Id.Value} is already registered.");
                }

                var position = InsertPosition(unit.Id);
                _units.Insert(position, unit);
                _unitIds.Insert(position, unit.Id);
            }

            _spawnQueue.Clear();
        }

        private int IndexOf(TargetId id)
        {
            var low = 0;
            var high = _unitIds.Count - 1;
            while (low <= high)
            {
                var mid = low + ((high - low) >> 1);
                var order = _unitIds[mid].CompareTo(id);
                if (order == 0) return mid;
                if (order < 0) low = mid + 1;
                else high = mid - 1;
            }

            return -1;
        }

        private int InsertPosition(TargetId id)
        {
            var low = 0;
            var high = _unitIds.Count;
            while (low < high)
            {
                var mid = low + ((high - low) >> 1);
                if (_unitIds[mid].CompareTo(id) < 0) low = mid + 1;
                else high = mid;
            }

            return low;
        }
    }
}
```

- [ ] **Step 4: 기존 테스트 스캐폴드의 이름 충돌을 없앤다**

`common/src/com.bun3.gameplay/Tests/Runtime/EffectScenario.cs` 에 이미
`internal sealed class World` 라는 **중첩 스캐폴드 타입**이 있다(316행 부근). 새
`Bun3.Gameplay.World` 와 단순 이름이 겹쳐, `EffectScenario` 안에서 `World` 가 스캐폴드를
가리키게 된다 — Task 16이 이 파일에 진짜 World를 쓰는 헬퍼를 더하므로 지금 없앤다.

`EffectScenario.World` → `EffectScenario.ScenarioWorld` 로 이름을 바꾸고,
`BuildWorld` 의 반환 타입과 `EffectDeterminismTests.cs` 의 사용처를 함께 고친다
(지역 변수 이름 `world` 는 그대로 둬도 된다).

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectDeterminismTests"`
Expected: PASS — 이름만 바뀌었으므로 동작 불변.

- [ ] **Step 5: `WorldSnapshot` 을 더한다**

스펙 §4.1이 요구하는 월드 수준 스냅샷이다. `UnitSnapshot.cs` 에 추가한다:

```csharp
    /// <summary>
    /// Immutable deep copy of an entire world's deterministic state — every unit plus the counters
    /// that live outside a unit (pipeline tick, next issued instance id). The pending apply queue is
    /// deliberately excluded: a restore starts from a drained queue, which is why
    /// <see cref="World.CreateSnapshot"/> requires one.
    /// </summary>
    public sealed class WorldSnapshot
    {
        internal WorldSnapshot(UnitSnapshot[] units, long currentTick, ulong nextInstanceId)
        {
            Units = units;
            CurrentTick = currentTick;
            NextInstanceId = nextInstanceId;
        }

        internal UnitSnapshot[] Units { get; }

        internal long CurrentTick { get; }

        internal ulong NextInstanceId { get; }
    }
```

`World.cs` 에 두 메서드를 더한다:

```csharp
        /// <summary>Creates a deep-copy snapshot of every registered unit plus the world counters.</summary>
        /// <returns>Snapshot of the whole world.</returns>
        /// <exception cref="InvalidOperationException">Thrown when applications are still pending —
        /// the queue is outside snapshot scope, so snapshotting mid-drain would silently lose work.</exception>
        public WorldSnapshot CreateSnapshot()
        {
            if (Pipeline.PendingApplyCount > 0)
                throw new InvalidOperationException("Cannot snapshot a world with pending applications.");

            var units = new UnitSnapshot[_units.Count];
            for (var i = 0; i < _units.Count; i++) units[i] = _units[i].CreateSnapshot();
            return new WorldSnapshot(units, Pipeline.CurrentTick, Pipeline.NextInstanceId);
        }

        /// <summary>Restores every unit and the world counters from a snapshot.</summary>
        /// <param name="snapshot">Snapshot to restore.</param>
        /// <exception cref="ArgumentException">Thrown when the snapshot's unit set does not match the
        /// registry — spawn and despawn are not part of a snapshot.</exception>
        public void RestoreSnapshot(WorldSnapshot snapshot)
        {
            if (snapshot is null) throw new ArgumentNullException(nameof(snapshot));

            FlushStructuralChanges();
            if (snapshot.Units.Length != _units.Count)
                throw new ArgumentException("Snapshot unit count does not match the registry.", nameof(snapshot));

            for (var i = 0; i < _units.Count; i++)
            {
                _units[i].RestoreSnapshot(snapshot.Units[i], _effects);
            }

            Pipeline.CurrentTick = snapshot.CurrentTick;
            Pipeline.NextInstanceId = snapshot.NextInstanceId;
        }
```

`Unit.RestoreSnapshot` 이 unit id 불일치를 이미 거부하므로 순서가 어긋나면 거기서 잡힌다.

`WorldTests.cs` 에 테스트를 더한다:

```csharp
    [Test]
    public void World_snapshot_round_trip_restores_units_and_counters()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInfinite("aura"));
        var world = MakeWorld(kit);
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 80);

        world.Pipeline.EnqueueApply(kit.SpecId("aura"), unit.Id, unit.Id);
        world.Tick();
        var snapshot = world.CreateSnapshot();
        var tickAtSnapshot = world.CurrentTick;

        unit.Attributes.SetBase(EffectTestKit.Hp, 5);
        world.Tick();
        world.Tick();

        world.RestoreSnapshot(snapshot);

        Assert.That(unit.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((Bun3.Gameplay.Numerics.BigNum)80));
        Assert.That(world.CurrentTick, Is.EqualTo(tickAtSnapshot));
        Assert.That(unit.Effects.ActiveEffectCount, Is.EqualTo(1));
    }

    [Test]
    public void Snapshotting_with_pending_applications_is_rejected()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("noop"));
        var world = MakeWorld(kit);
        world.Spawn(MakeUnit(kit, 1));
        world.Tick();

        world.Pipeline.EnqueueApply(kit.SpecId("noop"), new TargetId(1), new TargetId(1));

        Assert.That(() => world.CreateSnapshot(), Throws.InvalidOperationException);
    }
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~WorldTests"`
Expected: PASS (9건).

- [ ] **Step 7: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 8: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/ common/src/com.bun3.gameplay/Tests/Runtime/EffectScenario.cs common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ World 추가: 유닛 레지스트리·resolver 구현·스폰/디스폰 큐·틱 구동·월드 스냅샷" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 8: SelectorContext 후보 배선

**Files:**
- Modify: `common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs` (`SelectorContext`)
- Modify: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs` (`FireChain`의 컨텍스트 생성)
- Test: `common/tests/Bun3.Gameplay.Tests/TargetSelectorTests.cs`

**Interfaces:**
- Consumes: 기존 `IEffectTargetResolver.TargetIds`
- Produces: `SelectorContext` 에 아래가 추가된다
  - `TargetId Origin` — 선택의 원 대상
  - `int CandidateCount`, `TargetId CandidateAt(int index)` — 오름차순 후보
  - `bool TryGetTags(TargetId id, out TagCountContainer? tags)`
  - `bool TryGetAttribute(TargetId id, ushort attributeId, out BigNum value)`
  - `TargetId EventSource`, `TargetId EventTarget` — Task 10이 채운다. 이 태스크에서는 항상 `default`.
  - 내부 생성자 `SelectorContext(TargetId source, TargetId origin, ReadOnlySpan<BigNum> parameters, IRng rng, IEffectTargetResolver resolver, TargetId eventSource, TargetId eventTarget)`

`ref struct` 이므로 `ReadOnlySpan<TargetId>` 프로퍼티를 직접 노출해도 되지만, `IReadOnlyList<TargetId>`
를 스팬으로 바꾸는 무할당 경로가 없으므로 **resolver 참조를 들고 인덱서로 노출**한다.

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/TargetSelectorTests.cs`:

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class TargetSelectorTests
{
    private sealed class RecordingSelector : ITargetSelector
    {
        internal readonly List<TargetId> SeenCandidates = new List<TargetId>();
        internal TargetId SeenOrigin;
        internal BigNum SeenHp;
        internal bool SawTags;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            SeenOrigin = ctx.Origin;
            for (var i = 0; i < ctx.CandidateCount; i++) SeenCandidates.Add(ctx.CandidateAt(i));
            ctx.TryGetAttribute(ctx.Origin, EffectTestKit.Hp, out SeenHp);
            SawTags = ctx.TryGetTags(ctx.Origin, out _);
            return 0;
        }
    }

    [Test]
    public void Selector_sees_all_candidates_in_ascending_order_plus_origin_and_attributes()
    {
        var kit = EffectTestKit.Create();
        var selector = new RecordingSelector();
        kit.RegisterSelector("selector.everyone", selector);

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 55);
        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(selector.SeenOrigin, Is.EqualTo(kit.Defender.Id));
        Assert.That(selector.SeenCandidates.Count, Is.EqualTo(kit.AllTargets.Count));
        for (var i = 1; i < selector.SeenCandidates.Count; i++)
        {
            Assert.That(selector.SeenCandidates[i], Is.GreaterThan(selector.SeenCandidates[i - 1]));
        }

        Assert.That(selector.SeenHp, Is.EqualTo((BigNum)55));
        Assert.That(selector.SawTags, Is.True);
    }

    [Test]
    public void Unknown_candidate_lookups_report_failure_without_throwing()
    {
        var kit = EffectTestKit.Create();
        var probe = new UnknownProbeSelector();
        kit.RegisterSelector("selector.everyone", probe);

        var source = EffectTestKit.MinimalInstant("source");
        var edge = EffectTestKit.Edge(ChainTrigger.OnApplication, "chained");
        edge.SelectorTag = "selector.everyone";
        source.Chains.Add(edge);
        kit.AddSpec(source);
        kit.AddSpec(EffectTestKit.MinimalInstant("chained"));
        var pipeline = kit.BuildPipeline();

        pipeline.EnqueueApply(kit.SpecId("source"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(probe.TagsFound, Is.False);
        Assert.That(probe.AttributeFound, Is.False);
        Assert.That(probe.AttributeValue, Is.EqualTo(BigNum.Zero));
    }

    private sealed class UnknownProbeSelector : ITargetSelector
    {
        internal bool TagsFound;
        internal bool AttributeFound;
        internal BigNum AttributeValue;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var missing = new TargetId(ulong.MaxValue);
            TagsFound = ctx.TryGetTags(missing, out _);
            AttributeFound = ctx.TryGetAttribute(missing, EffectTestKit.Hp, out AttributeValue);
            return 0;
        }
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~TargetSelectorTests"`
Expected: FAIL — `ctx.Origin` 등이 없어 컴파일 오류.

- [ ] **Step 3: `SelectorContext` 를 확장한다**

`Runtime/Seams/Seams.cs`의 `SelectorContext` 를 아래로 교체한다:

```csharp
    /// <summary>Context for target selection. Candidates are the resolver's target ids in ascending
    /// order — that order is the determinism basis, so selectors must not reorder their scan.</summary>
    public readonly ref struct SelectorContext
    {
        private readonly ReadOnlySpan<BigNum> _params;
        private readonly IEffectTargetResolver _resolver;

        internal SelectorContext(
            TargetId source, TargetId origin, ReadOnlySpan<BigNum> parameters, IRng rng,
            IEffectTargetResolver resolver, TargetId eventSource, TargetId eventTarget)
        {
            Source = source;
            Origin = origin;
            _params = parameters;
            Rng = rng;
            _resolver = resolver;
            EventSource = eventSource;
            EventTarget = eventTarget;
        }

        /// <summary>Source that triggered the selection.</summary>
        public TargetId Source { get; }

        /// <summary>Target the selection originates from — the target the firing effect is applied to.</summary>
        public TargetId Origin { get; }

        /// <summary>Source of the gameplay event that triggered this selection. Default outside an event path.</summary>
        public TargetId EventSource { get; }

        /// <summary>Target of the gameplay event that triggered this selection. Default outside an event path.</summary>
        public TargetId EventTarget { get; }

        /// <summary>Random number generator usable in this selection.</summary>
        public IRng Rng { get; }

        /// <summary>Number of parameters passed to the selection.</summary>
        public int ParamCount => _params.Length;

        /// <summary>Gets a parameter value passed to the selection.</summary>
        /// <param name="index">Index into the parameter list.</param>
        public BigNum Param(int index) => _params[index];

        /// <summary>Number of candidate targets.</summary>
        public int CandidateCount => _resolver.TargetIds.Count;

        /// <summary>Gets a candidate target id. Candidates are in ascending id order.</summary>
        /// <param name="index">Index into the candidate list.</param>
        public TargetId CandidateAt(int index) => _resolver.TargetIds[index];

        /// <summary>Reads a candidate's owned tags.</summary>
        /// <param name="id">Candidate target id.</param>
        /// <param name="tags">Owned-tag container of that candidate.</param>
        /// <returns>True when the candidate resolves.</returns>
        public bool TryGetTags(TargetId id, out TagCountContainer? tags)
        {
            if (_resolver.TryResolve(id, out var target) && target is not null)
            {
                tags = target.Tags;
                return true;
            }

            tags = null;
            return false;
        }

        /// <summary>Reads a candidate attribute's Current value.</summary>
        /// <param name="id">Candidate target id.</param>
        /// <param name="attributeId">Attribute id to read.</param>
        /// <param name="value">Current value; <see cref="BigNum.Zero"/> when unavailable.</param>
        /// <returns>True when the candidate resolves and declares that attribute.</returns>
        public bool TryGetAttribute(TargetId id, ushort attributeId, out BigNum value)
        {
            if (_resolver.TryResolve(id, out var target) && target is not null
                && target.Attributes.Has(attributeId))
            {
                value = target.Attributes.GetCurrent(attributeId);
                return true;
            }

            value = BigNum.Zero;
            return false;
        }
    }
```

파일 상단 `using`에 `Bun3.Gameplay.Tags` 가 이미 있는지 확인한다(`TagCountContainer` 용).

- [ ] **Step 4: `FireChain` 의 컨텍스트 생성을 고친다**

`EffectPipeline.FireChain` 안에서:

```csharp
                var selectorCtx = new SelectorContext(
                    source, originTargetId, chain.SelectorParams, _rng, _resolver,
                    eventSource: default, eventTarget: default);
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~TargetSelectorTests"`
Expected: PASS (2건).

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs common/tests/Bun3.Gameplay.Tests/TargetSelectorTests.cs
git commit -m "✨ SelectorContext에 후보 열거·태그·속성 접근자 배선" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 9: 이벤트 발행 + OnEvent 체인 라우팅

발행과 수신은 관찰 가능한 결과가 서로뿐이라 한 태스크다. `EventMagnitude` 는 Task 10 — 여기서는 고정 크기 반격까지 확인한다.

**스펙 §6.6에서 벗어나는 점(의도적)**: 리스너 카운터를 두지 않는다. 이벤트는 `RaiseEvents` 를 선언한 스펙에서만 발생하므로 바깥 게이트가 이미 선택적이고, 카운터를 인스턴스 삽입·제거·`Enabled` 토글·스냅샷 복원 네 경로에서 정확히 유지하는 비용이 이득보다 크다. 대신 스펙별 `HasEventChains` 플래그로 안쪽 루프를 건너뛴다. 실측으로 병목이 확인되면 그때 카운터를 넣는다.

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/GameplayEvent.cs`
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.Events.cs`
- Modify: `Runtime/Effects/EffectSpec.cs` (`RaiseEventDef`, `EffectSpec.RaiseEvents`, `ChainTrigger.OnEvent`, `ChainListen`, `ChainEdgeDef.EventTag`/`Listen`)
- Modify: `Runtime/Effects/EffectCatalog.cs` (`CompiledRaiseEvent`, `CompiledChain` 확장, `CompiledEffectSpec.RaiseEvents`/`HasEventChains`)
- Modify: `Runtime/Effects/EffectCatalogBuilder.cs` (컴파일 + 검증)
- Modify: `Runtime/Effects/EffectPipeline.cs` (`ApplyInstant`/`FirePeriodic` 에서 발행)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectEventChainTests.cs`

**Interfaces:**
- Consumes: Task 8의 `SelectorContext` 생성자
- Produces:
  - `public enum ChainListen : byte { AsTarget = 0, AsSource = 1 }`
  - `public sealed class RaiseEventDef { string Tag; ushort AttributeId; }`
  - `EffectSpec.RaiseEvents: List<RaiseEventDef>`
  - `ChainTrigger.OnEvent = 4`, `ChainEdgeDef.EventTag: string?`, `ChainEdgeDef.Listen: ChainListen`
  - `public readonly struct GameplayEvent` — `GameplayTag Tag`, `TargetId Source`, `TargetId Target`, `BigNum Magnitude`, `BigNum NominalMagnitude`
  - `internal readonly struct CompiledRaiseEvent`, `CompiledEffectSpec.RaiseEvents`, `CompiledEffectSpec.HasEventChains`
  - `CompiledChain.EventTag: GameplayTag`, `CompiledChain.Listen: ChainListen`

- [ ] **Step 1: 실패 테스트를 쓴다**

먼저 `EffectTestKit.CatalogJson` 의 `tags` 배열에 이벤트 태그를 추가한다:

```json
        { "name": "event.damage.taken" },
        { "name": "event.damage" },
        { "name": "event.heal.taken" }
```

`common/tests/Bun3.Gameplay.Tests/EffectEventChainTests.cs`:

```csharp
#nullable enable
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class EffectEventChainTests
{
    private static EffectSpec Damage(string name, int amount, string? eventTag)
    {
        var spec = EffectTestKit.MinimalInstant(name);
        spec.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-amount) },
        });
        if (eventTag != null)
        {
            spec.RaiseEvents.Add(new RaiseEventDef { Tag = eventTag, AttributeId = EffectTestKit.Hp });
        }

        return spec;
    }

    private static ChainEdgeDef EventEdge(string eventTag, string effectName, ChainListen listen)
    {
        var edge = EffectTestKit.Edge(ChainTrigger.OnEvent, effectName);
        edge.EventTag = eventTag;
        edge.Listen = listen;
        return edge;
    }

    private static EffectTestKit BuildReflectKit(ChainListen listen, string chainEventTag = "event.damage.taken")
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge(chainEventTag, "retaliate", listen);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        return kit;
    }

    private sealed class AttackerSelector : Bun3.Gameplay.Seams.ITargetSelector
    {
        public int Select(in Bun3.Gameplay.Seams.SelectorContext ctx, System.Span<TargetId> results)
        {
            results[0] = ctx.Source;
            return 1;
        }
    }

    [Test]
    public void AsTarget_listener_retaliates_against_the_event_source()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)60));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void AsSource_listener_does_not_fire_for_a_target_side_event()
    {
        var kit = BuildReflectKit(ChainListen.AsSource);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Chain_event_tag_matches_descendant_event_tags()
    {
        var kit = BuildReflectKit(ChainListen.AsTarget, chainEventTag: "event.damage");
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void Disabled_listener_does_not_receive_events()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        thorns.OngoingConditions.Add(new ConditionDef
        {
            Left = Operand.Attribute(EffectTestKit.Mp),
            Op = ComparisonOp.Greater,
            Right = Operand.Constant(0),
        });
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        kit.Defender.Attributes.SetBase(EffectTestKit.Mp, 0);   // ongoing condition breaks → disabled
        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Event_magnitude_is_the_clamped_delta_not_the_nominal_amount()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("overkill", 500, "event.damage.taken"));
        var probe = new MagnitudeProbe();
        kit.RegisterSelector("selector.everyone", probe);

        var watcher = EffectTestKit.MinimalInfinite("watcher");
        var edge = EventEdge("event.damage.taken", "watcher", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        watcher.Chains.Add(edge);
        kit.AddSpec(watcher);
        var pipeline = kit.BuildPipeline();

        kit.Defender.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        kit.Defender.Attributes.SetBase(EffectTestKit.Hp, 30);
        pipeline.EnqueueApply(kit.SpecId("watcher"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("overkill"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(probe.LastMagnitude, Is.EqualTo((BigNum)(-30)));
        Assert.That(probe.LastNominal, Is.EqualTo((BigNum)(-500)));
    }

    private sealed class MagnitudeProbe : Bun3.Gameplay.Seams.ITargetSelector
    {
        internal BigNum LastMagnitude;
        internal BigNum LastNominal;

        public int Select(in Bun3.Gameplay.Seams.SelectorContext ctx, System.Span<TargetId> results)
        {
            LastMagnitude = ctx.EventMagnitude;
            LastNominal = ctx.EventNominalMagnitude;
            return 0;
        }
    }

    [Test]
    public void Mutual_reflection_is_bounded_by_the_apply_budget()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 1, "event.damage.taken"));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "bolt", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline(applyBudgetPerTick: 8);

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();

        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        Assert.That(() => pipeline.Tick(), Throws.Nothing);   // budget must stop the loop, not a stack overflow
        Assert.That(pipeline.PendingApplyCount, Is.GreaterThan(0));
    }
}
```

`SelectorContext.EventMagnitude`/`EventNominalMagnitude` 는 이 태스크에서 함께 추가한다 (Task 8이 이미 `EventSource`/`EventTarget` 자리를 만들어 뒀다).

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectEventChainTests"`
Expected: FAIL — `RaiseEventDef`, `ChainTrigger.OnEvent` 등이 없어 컴파일 오류.

- [ ] **Step 3: 저작 어휘를 더한다**

`Runtime/Effects/EffectSpec.cs` 의 `ChainTrigger` 에 멤버를 추가하고 두 타입을 더한다:

```csharp
        /// <summary>When a matching gameplay event reaches the owner of this effect.</summary>
        OnEvent = 4,
```

```csharp
    /// <summary>Which side of a gameplay event an OnEvent chain listens on.</summary>
    public enum ChainListen : byte
    {
        /// <summary>Fires on the effect owner that the event was applied to — retaliation and on-hit reactions.</summary>
        AsTarget = 0,
        /// <summary>Fires on the effect owner that caused the event — leech and on-deal reactions.</summary>
        AsSource = 1,
    }

    /// <summary>Gameplay event this effect raises, and the attribute whose change carries its magnitude.</summary>
    public sealed class RaiseEventDef
    {
        /// <summary>Tag path of the event to raise.</summary>
        public string Tag { get; set; } = string.Empty;

        /// <summary>Attribute whose Base change becomes the event magnitude. Must be modified by this effect.</summary>
        public ushort AttributeId { get; set; }
    }
```

`ChainEdgeDef` 에 두 프로퍼티를 더한다:

```csharp
        /// <summary>Event tag this edge listens for. Required when <see cref="Trigger"/> is
        /// <see cref="ChainTrigger.OnEvent"/> and rejected otherwise. Matches an event whose tag is
        /// this tag or one of its descendants.</summary>
        public string? EventTag { get; set; }

        /// <summary>Which side of the event this edge listens on. Only meaningful for
        /// <see cref="ChainTrigger.OnEvent"/>.</summary>
        public ChainListen Listen { get; set; }
```

`EffectSpec` 에 발행 목록을 더한다:

```csharp
        /// <summary>Gameplay events raised when this effect applies instantly or executes periodically.</summary>
        public List<RaiseEventDef> RaiseEvents { get; set; } = new List<RaiseEventDef>();
```

- [ ] **Step 4: `GameplayEvent` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/Effects/GameplayEvent.cs`:

```csharp
#nullable enable
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Effects
{
    /// <summary>
    /// One gameplay event raised by an effect application. <see cref="Magnitude"/> is the signed
    /// Base change that actually landed after clamping; <see cref="NominalMagnitude"/> is the
    /// amount before clamping. Damage reads as a negative magnitude, healing as positive.
    /// </summary>
    public readonly struct GameplayEvent
    {
        /// <summary>Creates a gameplay event.</summary>
        /// <param name="tag">Event tag.</param>
        /// <param name="source">Caster of the effect that raised the event.</param>
        /// <param name="target">Target the effect was applied to.</param>
        /// <param name="magnitude">Signed Base change after clamping.</param>
        /// <param name="nominalMagnitude">Signed Base change before clamping.</param>
        public GameplayEvent(
            GameplayTag tag, TargetId source, TargetId target, BigNum magnitude, BigNum nominalMagnitude)
        {
            Tag = tag;
            Source = source;
            Target = target;
            Magnitude = magnitude;
            NominalMagnitude = nominalMagnitude;
        }

        /// <summary>Event tag.</summary>
        public GameplayTag Tag { get; }

        /// <summary>Caster of the effect that raised the event.</summary>
        public TargetId Source { get; }

        /// <summary>Target the effect was applied to.</summary>
        public TargetId Target { get; }

        /// <summary>Signed Base change after clamping.</summary>
        public BigNum Magnitude { get; }

        /// <summary>Signed Base change before clamping.</summary>
        public BigNum NominalMagnitude { get; }
    }
}
```

- [ ] **Step 5: 컴파일 형태를 더한다**

`Runtime/Effects/EffectCatalog.cs`:

```csharp
    /// <summary>One compiled event declaration.</summary>
    internal readonly struct CompiledRaiseEvent
    {
        internal CompiledRaiseEvent(GameplayTag tag, ushort attributeId)
        {
            Tag = tag;
            AttributeId = attributeId;
        }

        internal GameplayTag Tag { get; }

        internal ushort AttributeId { get; }
    }
```

`CompiledChain` 생성자와 프로퍼티에 `GameplayTag EventTag`, `ChainListen Listen` 을 더한다.
`CompiledEffectSpec` 생성자와 프로퍼티에 `CompiledRaiseEvent[] RaiseEvents` 와
`bool HasEventChains` 를 더한다. `HasEventChains` 는 생성자에서 계산한다:

```csharp
            HasEventChains = false;
            for (var i = 0; i < chains.Length; i++)
            {
                if (chains[i].Trigger != ChainTrigger.OnEvent) continue;
                HasEventChains = true;
                break;
            }
```

- [ ] **Step 6: 빌더에서 컴파일·검증한다**

`EffectCatalogBuilder.ResolveChain` 에 검증을 더한다:

```csharp
            var eventTag = default(GameplayTag);
            if (chain.Trigger == ChainTrigger.OnEvent)
            {
                if (string.IsNullOrEmpty(chain.EventTag))
                {
                    throw new InvalidOperationException(
                        $"Effect '{specName}': an OnEvent chain requires EventTag.");
                }

                eventTag = tags.GetRequired(chain.EventTag!);
            }
            else if (!string.IsNullOrEmpty(chain.EventTag))
            {
                throw new InvalidOperationException(
                    $"Effect '{specName}': EventTag is only valid on an OnEvent chain.");
            }
```

`CompileSpec` 에 발행 목록 컴파일을 더한다:

```csharp
            var raiseEvents = new CompiledRaiseEvent[spec.RaiseEvents.Count];
            for (var i = 0; i < spec.RaiseEvents.Count; i++)
            {
                var raise = spec.RaiseEvents[i];
                if (string.IsNullOrEmpty(raise.Tag))
                    throw new InvalidOperationException($"Effect '{spec.Name}': RaiseEvents entry has an empty Tag.");
                if (!attributes.Contains(raise.AttributeId))
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': RaiseEvents references unregistered attribute {raise.AttributeId}.");
                }

                var modified = false;
                for (var m = 0; m < spec.Modifiers.Count; m++)
                {
                    if (spec.Modifiers[m].AttributeId != raise.AttributeId) continue;
                    modified = true;
                    break;
                }

                if (!modified)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': RaiseEvents attribute {raise.AttributeId} is not modified by this effect.");
                }

                raiseEvents[i] = new CompiledRaiseEvent(tags.GetRequired(raise.Tag), raise.AttributeId);
            }
```

- [ ] **Step 7: `SelectorContext` 에 이벤트 크기를 더한다**

`Runtime/Seams/Seams.cs` 의 `SelectorContext` 생성자 끝에 `BigNum eventMagnitude, BigNum eventNominalMagnitude` 를 더하고 프로퍼티 둘을 노출한다:

```csharp
        /// <summary>Clamped magnitude of the gameplay event that triggered this selection. Zero outside an event path.</summary>
        public BigNum EventMagnitude { get; }

        /// <summary>Pre-clamp magnitude of the gameplay event that triggered this selection. Zero outside an event path.</summary>
        public BigNum EventNominalMagnitude { get; }
```

Task 8이 만든 `FireChain` 호출부에 `eventMagnitude: BigNum.Zero, eventNominalMagnitude: BigNum.Zero` 를 더한다.

- [ ] **Step 8: 발행과 라우팅을 구현한다**

`common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.Events.cs` — 이벤트 발행과 OnEvent 라우팅 담당 partial:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    // Gameplay event emission and OnEvent chain routing. Events are never queued: a raise walks the
    // listeners immediately and enqueues applications, so cycles are bounded by the same per-tick
    // apply budget as any other chain and no re-entry is possible.
    public sealed partial class EffectPipeline
    {
        /// <summary>Number of OnEvent chain edges that fired.</summary>
        internal long EventChainFireCount { get; private set; }

        private void RaiseGameplayEvent(in GameplayEvent evt)
        {
            DispatchEvent(evt.Target, ChainListen.AsTarget, in evt);
            DispatchEvent(evt.Source, ChainListen.AsSource, in evt);
        }

        private void DispatchEvent(TargetId listenerId, ChainListen listen, in GameplayEvent evt)
        {
            if (!_resolver.TryResolve(listenerId, out var listener) || listener is null) return;

            var tagCatalog = listener.Tags.Catalog;
            var active = listener.ActiveEffects;
            for (var i = 0; i < active.Count; i++)
            {
                var instance = active[i];
                if (!instance.Enabled) continue;

                var spec = _catalog.GetSpec(instance.SpecId);
                if (!spec.HasEventChains) continue;

                var chains = spec.Chains;
                for (var c = 0; c < chains.Length; c++)
                {
                    var chain = chains[c];
                    if (chain.Trigger != ChainTrigger.OnEvent || chain.Listen != listen) continue;
                    if (!tagCatalog.IsAncestorOrSelf(chain.EventTag, evt.Tag)) continue;

                    FireEventChain(chain, instance, listenerId, listener, in evt);
                }
            }
        }

        private void FireEventChain(
            CompiledChain chain, EffectInstance instance, TargetId listenerId, EffectTarget listener,
            in GameplayEvent evt)
        {
            var source = instance.Source;
            var hasSource = _resolver.TryResolve(source, out var sourceTarget) && sourceTarget is not null;
            if (!EvaluateConditions(chain.Conditions, listener, sourceTarget, hasSource)) return;

            var chainLevel = chain.LevelRule == ChainLevelRule.Inherit ? instance.Level : chain.FixedLevel;
            EventChainFireCount++;

            if (chain.Selector is null)
            {
                EnqueueApplyWithEvent(chain.EffectId, source, listenerId, chainLevel, in evt);
                return;
            }

            Span<TargetId> targets = stackalloc TargetId[32];
            var selectorCtx = new SelectorContext(
                source, listenerId, chain.SelectorParams, _rng, _resolver,
                evt.Source, evt.Target, evt.Magnitude, evt.NominalMagnitude);
            var selected = chain.Selector.Select(in selectorCtx, targets);
            var count = selected;
            if (count > targets.Length)
            {
                SelectorTruncationCount++;
                count = targets.Length;
            }

            for (var k = 0; k < count; k++)
            {
                EnqueueApplyWithEvent(chain.EffectId, source, targets[k], chainLevel, in evt);
            }
        }
    }
}
```

`EffectPipeline` 클래스 선언을 `public sealed partial class EffectPipeline` 로 바꾸고,
`_resolver`/`_catalog`/`_rng` 는 이미 private 필드이므로 그대로 쓴다.

`EnqueueApplyWithEvent` 와 `PendingApply` 확장은 Task 10에서 크기를 실어 나르기 시작한다.
이 태스크에서는 아래 임시 구현으로 둔다 (Task 10이 본체를 채운다):

```csharp
        private void EnqueueApplyWithEvent(
            int specId, TargetId source, TargetId target, int level, in GameplayEvent evt) =>
            EnqueueApply(specId, source, target, level);
```

`ApplyInstant` 에서 발행한다 — modifier 루프를 감싸도록 고친다:

```csharp
        private void ApplyInstant(
            CompiledEffectSpec spec, EffectTarget target, EffectTarget? source, bool hasSource, in PendingApply pending)
        {
            var raises = spec.RaiseEvents;
            Span<BigNum> before = stackalloc BigNum[raises.Length];
            Span<BigNum> nominal = stackalloc BigNum[raises.Length];
            for (var r = 0; r < raises.Length; r++)
            {
                before[r] = target.Attributes.GetBase(raises[r].AttributeId);
                nominal[r] = BigNum.Zero;
            }

            var modifiers = spec.Modifiers;
            for (var i = 0; i < modifiers.Length; i++)
            {
                var modifier = modifiers[i];
                var magnitude = EvaluateMagnitude(modifier, target, source, hasSource, pending.Level, stack: 1);
                for (var r = 0; r < raises.Length; r++)
                {
                    if (raises[r].AttributeId == modifier.AttributeId) nominal[r] += magnitude;
                }

                ApplyModifierToBase(target, modifier, magnitude);
            }

            var executions = spec.Executions;
            for (var i = 0; i < executions.Length; i++)
            {
                RunExecution(executions[i], target, source, hasSource, pending.Source, pending.Target, pending.Level, stack: 1);
            }

            for (var r = 0; r < raises.Length; r++)
            {
                var delta = target.Attributes.GetBase(raises[r].AttributeId) - before[r];
                var evt = new GameplayEvent(raises[r].Tag, pending.Source, pending.Target, delta, nominal[r]);
                RaiseGameplayEvent(in evt);
            }

            FireChain(spec.Chains, ChainTrigger.OnApplication, pending.Source, pending.Level, pending.Target, target);
        }
```

`FirePeriodic` 도 같은 방식으로 감싼다 — 주기 실행이 `ApplyInstant` 를 재사용한다면 추가 작업이 없다.
재사용하지 않는다면 위 before/nominal/발행 3단계를 같은 형태로 복제한다.

`FireChain` 은 `chain.Trigger != trigger` 로 걸러지므로 `OnEvent` 체인이 다른 트리거에서 발화하지 않는다 — 별도 방어가 필요 없다.

- [ ] **Step 9: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectEventChainTests"`
Expected: PASS (6건).

- [ ] **Step 10: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 11: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Effects/ common/src/com.bun3.gameplay/Runtime/Seams/Seams.cs common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ 이벤트 발행과 OnEvent 체인 라우팅: RaiseEvents·Listen·실적용 델타" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 10: Operand.EventMagnitude

**전파 범위 결정(스펙 §6.5 구체화)**: 이벤트 크기는 **한 홉**만 간다 — `OnEvent` 체인이 직접 거는 Effect까지다. 그 Effect의 `OnApplication` 체인이 또 다른 Effect를 걸면 거기서는 0이다. 무한 전파는 "언제까지 이 값이 유효한가"를 저작자가 추적할 수 없게 만들고, Build 도달성 검사도 그래프 전체로 번진다.

**Files:**
- Modify: `Runtime/Attributes/AttributeEnums.cs` (`OperandKind.EventMagnitude = 3`)
- Modify: `Runtime/Attributes/Operand.cs` (팩토리)
- Modify: `Runtime/Effects/EffectPipeline.cs` (`PendingApply.EventMagnitude`, 평가 문맥 전파)
- Modify: `Runtime/Effects/EffectPipeline.Events.cs` (`EnqueueApplyWithEvent` 본체)
- Modify: `Runtime/Effects/EffectCatalogBuilder.cs` (도달성·자리 검증)
- Modify: `Runtime/Attributes/AttributeRegistryBuilder.cs` (클램프 경계에서 거부)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectEventChainTests.cs`, `common/tests/Bun3.Gameplay.Tests/OperandTests.cs`

**Interfaces:**
- Consumes: Task 9의 `GameplayEvent`, `EnqueueApplyWithEvent`
- Produces:
  - `OperandKind.EventMagnitude = 3`
  - `public static Operand Operand.EventMagnitude(BigNum coefficient)` / `Operand.EventMagnitude()` (계수 1)
  - `internal void EffectPipeline.EnqueueApply(int specId, TargetId source, TargetId target, int level, BigNum eventMagnitude)`

- [ ] **Step 1: 실패 테스트를 쓴다**

`OperandTests.cs` 에 추가:

```csharp
    [Test]
    public void Event_magnitude_operand_carries_its_coefficient()
    {
        var operand = Operand.EventMagnitude((BigNum)(-0.3m));

        Assert.That(operand.Kind, Is.EqualTo(OperandKind.EventMagnitude));
        Assert.That(operand.Value, Is.EqualTo((BigNum)(-0.3m)));
        Assert.That(operand.AttributeId, Is.Zero);
        Assert.That(Operand.EventMagnitude().Value, Is.EqualTo(BigNum.One));
    }
```

`EffectEventChainTests.cs` 에 추가:

```csharp
    [Test]
    public void Proportional_reflect_uses_the_clamped_event_magnitude()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 100, "event.damage.taken"));

        var retaliate = EffectTestKit.MinimalInstant("retaliate");
        retaliate.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)0.3m) },
        });
        kit.AddSpec(retaliate);

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.everyone";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        kit.RegisterSelector("selector.everyone", new AttackerSelector());
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)900));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)970));
    }

    [Test]
    public void Lifesteal_flips_sign_through_a_negative_coefficient()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 100, "event.damage.taken"));

        var leech = EffectTestKit.MinimalInstant("leech");
        leech.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)(-0.1m)) },
        });
        kit.AddSpec(leech);

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        vampiric.Chains.Add(EventEdge("event.damage.taken", "leech", ChainListen.AsSource));
        kit.AddSpec(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 500);
        }

        pipeline.EnqueueApply(kit.SpecId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)510));
    }

    [Test]
    public void Event_magnitude_does_not_propagate_past_the_first_hop()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 100, "event.damage.taken"));

        var second = EffectTestKit.MinimalInstant("second");
        second.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)1) },
        });
        kit.AddSpec(second);

        var first = EffectTestKit.MinimalInstant("first");
        first.Chains.Add(EffectTestKit.Edge(ChainTrigger.OnApplication, "second"));
        kit.AddSpec(first);

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        thorns.Chains.Add(EventEdge("event.damage.taken", "first", ChainListen.AsTarget));
        kit.AddSpec(thorns);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 1000);
            target.Attributes.SetBase(EffectTestKit.Hp, 1000);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.Tick();

        // bolt −100 only; "second" resolves EventMagnitude to zero because it is two hops away.
        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)900));
    }

    [Test]
    public void Event_magnitude_in_an_unreachable_spec_is_a_build_error()
    {
        var kit = EffectTestKit.Create();
        var orphan = EffectTestKit.MinimalInstant("orphan");
        orphan.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.EventMagnitude((BigNum)1) },
        });
        kit.AddSpec(orphan);

        Assert.That(
            () => kit.BuildPipeline(),
            Throws.InvalidOperationException.With.Message.Contains("EventMagnitude"));
    }

    [Test]
    public void Event_magnitude_in_a_clamp_bound_is_rejected()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(EffectTestKit.Hp, min: Operand.EventMagnitude((BigNum)1));

        Assert.That(() => builder.Build(), Throws.InvalidOperationException);
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectEventChainTests|FullyQualifiedName~OperandTests"`
Expected: FAIL — `Operand.EventMagnitude` 없음.

- [ ] **Step 3: `OperandKind` 와 `Operand` 를 확장한다**

`AttributeEnums.cs`:

```csharp
        /// <summary>Magnitude of the gameplay event that triggered this application × constant coefficient.
        /// Valid only inside an effect applied directly by an OnEvent chain; zero elsewhere.</summary>
        EventMagnitude = 3,
```

`Operand.cs`:

```csharp
        /// <summary>Creates a gameplay-event magnitude operand with coefficient 1.</summary>
        public static Operand EventMagnitude() => EventMagnitude(BigNum.One);

        /// <summary>Creates a gameplay-event magnitude × coefficient operand.</summary>
        /// <param name="coefficient">Coefficient multiplied into the event magnitude.</param>
        public static Operand EventMagnitude(BigNum coefficient) =>
            new Operand(OperandKind.EventMagnitude, coefficient, 0);
```

- [ ] **Step 4: 파이프라인에 크기를 실어 나른다**

`EffectPipeline.cs`의 `PendingApply` 에 필드를 더한다:

```csharp
        private struct PendingApply
        {
            internal int SpecId;
            internal TargetId Source;
            internal TargetId Target;
            internal int Level;
            internal BigNum EventMagnitude;
        }
```

기존 `EnqueueApply` 는 0을 싣고, 오버로드를 더한다:

```csharp
        /// <summary>Enqueues an effect application for the next drain phase.</summary>
        /// <param name="specId">Effect spec id to apply.</param>
        /// <param name="source">Caster.</param>
        /// <param name="target">Target.</param>
        /// <param name="level">Effect level.</param>
        public void EnqueueApply(int specId, TargetId source, TargetId target, int level = 1) =>
            EnqueueApply(specId, source, target, level, BigNum.Zero);

        internal void EnqueueApply(int specId, TargetId source, TargetId target, int level, BigNum eventMagnitude)
        {
            _queue.Enqueue(new PendingApply
            {
                SpecId = specId, Source = source, Target = target, Level = level, EventMagnitude = eventMagnitude,
            });
        }
```

`EffectPipeline.Events.cs`의 임시 구현을 본체로 바꾼다:

```csharp
        private void EnqueueApplyWithEvent(
            int specId, TargetId source, TargetId target, int level, in GameplayEvent evt) =>
            EnqueueApply(specId, source, target, level, evt.Magnitude);
```

- [ ] **Step 5: 평가 문맥에 크기를 전파한다**

`EvaluateOperand` 시그니처에 `BigNum eventMagnitude` 를 더하고 분기를 추가한다:

```csharp
        private static BigNum EvaluateOperand(
            Operand operand, EffectTarget target, EffectTarget? source, bool hasSource, BigNum eventMagnitude)
        {
            switch (operand.Kind)
            {
                case OperandKind.EventMagnitude:
                    return eventMagnitude * operand.Value;
                // ... existing cases unchanged ...
            }
        }
```

`EvaluateConditions`, `EvaluateCondition`, `EvaluateMagnitude`, `EvaluateMagnitudeDef`, `RunExecution`,
`ApplyInstant`, `CreateInstance`, `MergeReapply`, `SyncLevelFromStack`, `RollChance`,
`ComputeMergedDurationTicks`, `FirePeriodic` 에 `BigNum eventMagnitude` 를 인자로 이어 붙이고,
`ProcessPendingApply` 가 `pending.EventMagnitude` 를 시작점으로 넘긴다.
`FireChain` 이 거는 체인은 `BigNum.Zero` 를 넘긴다 — 한 홉 규칙.

`OngoingConditions` 평가(`EvaluateOngoing`)와 클램프 경계는 이벤트 문맥이 없으므로 항상
`BigNum.Zero` 를 넘긴다. Build가 이 자리의 `EventMagnitude` 를 이미 거부하므로 도달할 수 없다.

- [ ] **Step 6: Build 검증을 더한다**

`EffectCatalogBuilder.Build` 에서, 컴파일이 끝난 뒤 도달성 검사를 한다:

```csharp
        // EventMagnitude is only meaningful for an effect an OnEvent chain applies directly (one hop).
        private static void ValidateEventMagnitudeReachability(EffectSpec[] specs, Dictionary<string, int> nameToId)
        {
            var reachable = new bool[specs.Length];
            for (var i = 0; i < specs.Length; i++)
            {
                var chains = specs[i].Chains;
                for (var c = 0; c < chains.Count; c++)
                {
                    if (chains[c].Trigger != ChainTrigger.OnEvent) continue;
                    if (nameToId.TryGetValue(chains[c].EffectName, out var id)) reachable[id] = true;
                }
            }

            for (var i = 0; i < specs.Length; i++)
            {
                if (reachable[i] || !UsesEventMagnitude(specs[i])) continue;
                throw new InvalidOperationException(
                    $"Effect '{specs[i].Name}': uses EventMagnitude but no OnEvent chain applies it directly.");
            }
        }

        private static bool UsesEventMagnitude(EffectSpec spec)
        {
            for (var i = 0; i < spec.Modifiers.Count; i++)
            {
                if (MagnitudeUsesEventMagnitude(spec.Modifiers[i].Magnitude)) return true;
            }

            for (var i = 0; i < spec.Executions.Count; i++)
            {
                var inputs = spec.Executions[i].Inputs;
                for (var k = 0; k < inputs.Count; k++)
                {
                    if (inputs[k].Kind == OperandKind.EventMagnitude) return true;
                }
            }

            for (var i = 0; i < spec.ApplicationConditions.Count; i++)
            {
                if (spec.ApplicationConditions[i].Left.Kind == OperandKind.EventMagnitude
                    || spec.ApplicationConditions[i].Right.Kind == OperandKind.EventMagnitude)
                {
                    return true;
                }
            }

            return spec.ChanceToApply != null && MagnitudeUsesEventMagnitude(spec.ChanceToApply);
        }

        private static bool MagnitudeUsesEventMagnitude(MagnitudeDef def) =>
            (def.Base.HasValue && def.Base.Value.Kind == OperandKind.EventMagnitude)
            || (def.PerLevel.HasValue && def.PerLevel.Value.Kind == OperandKind.EventMagnitude);
```

`OngoingConditions` 에 `EventMagnitude` 가 있으면 별도 오류를 낸다 (수명 중 상시 평가라 문맥이 없다):

```csharp
            for (var i = 0; i < spec.OngoingConditions.Count; i++)
            {
                if (spec.OngoingConditions[i].Left.Kind == OperandKind.EventMagnitude
                    || spec.OngoingConditions[i].Right.Kind == OperandKind.EventMagnitude)
                {
                    throw new InvalidOperationException(
                        $"Effect '{spec.Name}': EventMagnitude cannot be used in OngoingConditions.");
                }
            }
```

`AttributeRegistryBuilder.Build` 의 경계 검증에 추가한다:

```csharp
                if (min.HasValue && min.Value.Kind == OperandKind.EventMagnitude)
                    throw new InvalidOperationException($"Attribute {attributeId}: EventMagnitude cannot be a clamp bound.");
                if (max.HasValue && max.Value.Kind == OperandKind.EventMagnitude)
                    throw new InvalidOperationException($"Attribute {attributeId}: EventMagnitude cannot be a clamp bound.");
```

기존에 `SourceAttribute` 를 경계에서 거부하는 코드가 있으면 같은 자리에 나란히 둔다.

- [ ] **Step 7: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectEventChainTests|FullyQualifiedName~OperandTests"`
Expected: PASS.

- [ ] **Step 8: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 9: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/ common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ Operand.EventMagnitude 추가: 비례 반사·흡혈이 순수 데이터, 한 홉 전파" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 11: 내장 셀렉터 EventSource · EventTarget

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/Effects/BuiltInSelectors.cs`
- Modify: `Runtime/Seams/SeamRegistryBuilder.cs` (기본 등록)
- Test: `common/tests/Bun3.Gameplay.Tests/EffectEventChainTests.cs`

**Interfaces:**
- Consumes: Task 8·9의 `SelectorContext.EventSource`/`EventTarget`
- Produces: 예약 태그 `selector.event.source` / `selector.event.target` 이 `SeamRegistryBuilder.Build()` 이후 항상 등록되어 있다.

- [ ] **Step 1: 실패 테스트를 쓴다**

`EffectTestKit.CatalogJson` 의 `tags` 에 추가:

```json
        { "name": "selector.event.source" },
        { "name": "selector.event.target" }
```

`EffectEventChainTests.cs` 에 추가:

```csharp
    [Test]
    public void Built_in_event_source_selector_targets_the_attacker()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("retaliate", 7, eventTag: null));

        var thorns = EffectTestKit.MinimalInfinite("thorns");
        var edge = EventEdge("event.damage.taken", "retaliate", ChainListen.AsTarget);
        edge.SelectorTag = "selector.event.source";
        thorns.Chains.Add(edge);
        kit.AddSpec(thorns);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("thorns"), kit.Defender.Id, kit.Defender.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)93));
    }

    [Test]
    public void Built_in_event_target_selector_targets_the_victim()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(Damage("bolt", 40, "event.damage.taken"));
        kit.AddSpec(Damage("followup", 5, eventTag: null));

        var vampiric = EffectTestKit.MinimalInfinite("vampiric");
        var edge = EventEdge("event.damage.taken", "followup", ChainListen.AsSource);
        edge.SelectorTag = "selector.event.target";
        vampiric.Chains.Add(edge);
        kit.AddSpec(vampiric);
        var pipeline = kit.BuildPipeline();

        foreach (var target in kit.AllTargets)
        {
            target.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            target.Attributes.SetBase(EffectTestKit.Hp, 100);
        }

        pipeline.EnqueueApply(kit.SpecId("vampiric"), kit.Attacker.Id, kit.Attacker.Id);
        pipeline.Tick();
        pipeline.EnqueueApply(kit.SpecId("bolt"), kit.Attacker.Id, kit.Defender.Id);
        pipeline.Tick();

        Assert.That(kit.Defender.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)55));
        Assert.That(kit.Attacker.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~Built_in_event"`
Expected: FAIL — 셀렉터 태그 미등록 예외.

- [ ] **Step 3: 내장 셀렉터를 만든다**

`common/src/com.bun3.gameplay/Runtime/Effects/BuiltInSelectors.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay.Effects
{
    /// <summary>Selector tags the framework registers on every seam registry.</summary>
    public static class BuiltInSelectors
    {
        /// <summary>Tag path of the selector picking the source of the triggering gameplay event.</summary>
        public const string EventSourceTag = "selector.event.source";

        /// <summary>Tag path of the selector picking the target of the triggering gameplay event.</summary>
        public const string EventTargetTag = "selector.event.target";

        internal static readonly ITargetSelector EventSource = new EventSourceSelector();

        internal static readonly ITargetSelector EventTarget = new EventTargetSelector();

        private sealed class EventSourceSelector : ITargetSelector
        {
            public int Select(in SelectorContext ctx, Span<TargetId> results)
            {
                results[0] = ctx.EventSource;
                return 1;
            }
        }

        private sealed class EventTargetSelector : ITargetSelector
        {
            public int Select(in SelectorContext ctx, Span<TargetId> results)
            {
                results[0] = ctx.EventTarget;
                return 1;
            }
        }
    }
}
```

- [ ] **Step 4: `SeamRegistryBuilder.Build` 에서 기본 등록한다**

`Runtime/Seams/SeamRegistryBuilder.cs` 의 `Build(TagCatalog tags)` 안에서, 사용자 등록을 채운 뒤
아래를 더한다. 게임이 같은 태그를 직접 등록했으면 게임 것이 이긴다.

```csharp
            RegisterBuiltInSelector(targetSelectors, tags, BuiltInSelectors.EventSourceTag, BuiltInSelectors.EventSource);
            RegisterBuiltInSelector(targetSelectors, tags, BuiltInSelectors.EventTargetTag, BuiltInSelectors.EventTarget);
```

```csharp
        // Built-ins are registered only when the tag exists in the catalog and the game did not
        // claim it — a game that never uses event chains need not declare the tags.
        private static void RegisterBuiltInSelector(
            Dictionary<ushort, ITargetSelector> selectors, TagCatalog tags, string path, ITargetSelector selector)
        {
            if (!tags.TryGet(path, out var tag)) return;
            if (selectors.ContainsKey(tag.Index)) return;
            selectors.Add(tag.Index, selector);
        }
```

`TagCatalog` 에 `TryGet(string, out GameplayTag)` 가 없으면 기존 조회 메서드(`GetRequired` 의
비예외 버전)를 찾아 그 이름으로 맞춘다.

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~EffectEventChainTests"`
Expected: PASS.

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/Effects/BuiltInSelectors.cs common/src/com.bun3.gameplay/Runtime/Seams/SeamRegistryBuilder.cs common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ 내장 셀렉터 EventSource·EventTarget 등록: 반사·흡혈이 저작 코드 0줄" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 12: World.TryActivateAbility — 게이트 · 코스트 · 쿨다운

**Files:**
- Create: `common/src/com.bun3.gameplay/Runtime/World.Ability.cs`
- Modify: `Runtime/Effects/EffectPipeline.cs` (내부 평가·즉시 적용 접근자)
- Test: `common/tests/Bun3.Gameplay.Tests/AbilityActivationTests.cs`

**Interfaces:**
- Consumes: Task 6의 `CompiledAbility`, Task 7의 `World`, Task 3의 `AbilitySet`
- Produces:
  - `public AbilityActivationResult World.TryActivateAbility(TargetId unitId, int abilityId)`
  - `internal bool EffectPipeline.CheckConditions(CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource)`
  - `internal BigNum EffectPipeline.ComputeMagnitude(CompiledMagnitude magnitude, EffectTarget target, EffectTarget? source, bool hasSource, int level)`
  - `internal void EffectPipeline.ApplyImmediate(int specId, TargetId source, TargetId target, int level)`
  - `internal void EffectPipeline.ReportSelectorTruncation()`
  - `internal IRng EffectPipeline.Rng`

- [ ] **Step 1: 실패 테스트를 쓴다**

`EffectTestKit.CatalogJson` 의 `tags` 에 추가:

```json
        { "name": "cooldown.fireball" },
        { "name": "state.silenced" },
        { "name": "state.armed" }
```

`common/tests/Bun3.Gameplay.Tests/AbilityActivationTests.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityActivationTests
{
    private sealed class Fixture
    {
        internal EffectTestKit Kit = null!;
        internal World World = null!;
        internal Unit Caster = null!;
        internal Unit Victim = null!;
        internal AbilityCatalog Abilities = null!;
    }

    private static Unit MakeUnit(EffectTestKit kit, ulong id)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog);
    }

    private static Fixture Build(Action<EffectTestKit, AbilityCatalogBuilder> configure)
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = Operand.Constant(-25) },
        });
        kit.AddSpec(bolt);

        var cooldown = EffectTestKit.MinimalDuration("cooldown.fireball", ticks: 5);
        cooldown.GrantedTags.Add("cooldown.fireball");
        kit.AddSpec(cooldown);

        var abilityBuilder = new AbilityCatalogBuilder();
        configure(kit, abilityBuilder);

        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);
        var world = new World(kit.Catalog, abilities, new XorShiftRng(999));
        var caster = MakeUnit(kit, 1);
        var victim = MakeUnit(kit, 2);
        world.Spawn(caster);
        world.Spawn(victim);
        world.Tick();

        foreach (var unit in new[] { caster, victim })
        {
            unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
            unit.Attributes.SetBase(EffectTestKit.Hp, 100);
            unit.Attributes.SetBase(EffectTestKit.Mp, 50);
        }

        return new Fixture { Kit = kit, World = world, Caster = caster, Victim = victim, Abilities = abilities };
    }

    private static AbilityDef Fireball()
    {
        var ability = new AbilityDef { Name = "fireball", MaxLevel = 3 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", LevelRule = ChainLevelRule.Inherit });
        return ability;
    }

    [Test]
    public void Unknown_unit_and_ability_are_reported_as_such()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");

        Assert.That(f.World.TryActivateAbility(new TargetId(99), id), Is.EqualTo(AbilityActivationResult.UnknownUnit));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, 12345), Is.EqualTo(AbilityActivationResult.UnknownAbility));
    }

    [Test]
    public void Ungranted_ability_cannot_be_activated()
    {
        var f = Build((_, b) => b.Add(Fireball()));

        Assert.That(
            f.World.TryActivateAbility(f.Caster.Id, f.Abilities.GetRequiredId("fireball")),
            Is.EqualTo(AbilityActivationResult.NotGranted));
    }

    [Test]
    public void Missing_required_tag_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.RequiredTags.Add("state.armed");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.MissingTag));

        f.Caster.Tags.Add(f.Kit.Tag("state.armed"));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
    }

    [Test]
    public void Blocked_tag_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.BlockedTags.Add("state.silenced");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);
        f.Caster.Tags.Add(f.Kit.Tag("state.silenced"));

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.BlockedByTag));
    }

    [Test]
    public void Failing_activation_condition_blocks_activation()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.ActivationConditions.Add(new ConditionDef
            {
                Left = Operand.Attribute(EffectTestKit.Hp),
                Op = ComparisonOp.Greater,
                Right = Operand.Constant(500),
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.ConditionFailed));
    }

    [Test]
    public void Insufficient_cost_blocks_activation_and_spends_nothing()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(80) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)50));
    }

    [Test]
    public void Cost_is_deducted_immediately_so_repeat_activation_in_one_tick_fails()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.Costs.Add(new CostDef
            {
                AttributeId = EffectTestKit.Mp,
                Magnitude = new MagnitudeDef { Base = Operand.Constant(30) },
            });
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)20));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.CostInsufficient));
        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Mp), Is.EqualTo((BigNum)20));
    }

    [Test]
    public void Cooldown_effect_blocks_the_next_activation_in_the_same_tick()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.BlockedTags.Add("cooldown.fireball");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.BlockedByTag));
    }

    [Test]
    public void Cooldown_expires_and_the_ability_becomes_available_again()
    {
        var f = Build((_, b) =>
        {
            var ability = Fireball();
            ability.CooldownEffect = "cooldown.fireball";
            ability.BlockedTags.Add("cooldown.fireball");
            b.Add(ability);
        });
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        f.World.TryActivateAbility(f.Caster.Id, id);
        for (var i = 0; i < 8; i++) f.World.Tick();

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
    }

    [Test]
    public void Activation_enqueues_the_ability_effects_on_the_caster_by_default()
    {
        var f = Build((_, b) => b.Add(Fireball()));
        var id = f.Abilities.GetRequiredId("fireball");
        f.Caster.Abilities.Grant(id, GrantSource.Archetype(), level: 1);

        Assert.That(f.World.TryActivateAbility(f.Caster.Id, id), Is.EqualTo(AbilityActivationResult.Success));
        f.World.Tick();

        Assert.That(f.Caster.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)75));
        Assert.That(f.Victim.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)100));
    }

    [Test]
    public void Granted_level_drives_the_applied_effect_level()
    {
        var kit = EffectTestKit.Create();
        var bolt = EffectTestKit.MinimalInstant("bolt");
        bolt.MaxLevel = 3;
        bolt.Modifiers.Add(new ModifierDef
        {
            AttributeId = EffectTestKit.Hp,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef
            {
                PerLevelValues = new System.Collections.Generic.List<BigNum> { -10, -20, -30 },
            },
        });
        kit.AddSpec(bolt);

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 3 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt", LevelRule = ChainLevelRule.Inherit });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(ability);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var world = new World(kit.Catalog, abilities, new XorShiftRng(7));
        var caster = MakeUnit(kit, 1);
        world.Spawn(caster);
        world.Tick();
        caster.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        caster.Attributes.SetBase(EffectTestKit.Hp, 100);

        var id = abilities.GetRequiredId("fireball");
        caster.Abilities.Grant(id, GrantSource.Archetype(), level: 3);
        world.TryActivateAbility(caster.Id, id);
        world.Tick();

        Assert.That(caster.Attributes.GetBase(EffectTestKit.Hp), Is.EqualTo((BigNum)70));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityActivationTests"`
Expected: FAIL — `TryActivateAbility` 없음.

- [ ] **Step 3: `EffectPipeline` 에 내부 접근자를 더한다**

`EffectPipeline.cs` 에 추가한다. 재진입 금지 계약을 주석으로 남긴다:

```csharp
        /// <summary>Random source shared with this pipeline.</summary>
        internal IRng Rng => _rng;

        internal bool CheckConditions(
            CompiledCondition[] conditions, EffectTarget target, EffectTarget? source, bool hasSource) =>
            EvaluateConditions(conditions, target, source, hasSource, BigNum.Zero);

        internal BigNum ComputeMagnitude(
            CompiledMagnitude magnitude, EffectTarget target, EffectTarget? source, bool hasSource, int level) =>
            EvaluateMagnitudeDef(magnitude, target, source, hasSource, level, stack: 1, BigNum.Zero);

        // Applies one effect outside the drain loop. Must not be called from inside Tick — the drain
        // loop owns the queue while it runs, and re-entering it would break the per-tick budget.
        internal void ApplyImmediate(int specId, TargetId source, TargetId target, int level) =>
            ProcessPendingApply(new PendingApply
            {
                SpecId = specId, Source = source, Target = target, Level = level, EventMagnitude = BigNum.Zero,
            });

        internal void ReportSelectorTruncation() => SelectorTruncationCount++;
```

`EvaluateMagnitudeDef` 의 실제 이름이 다르면(예: `EvaluateMagnitudeFromDef`) 그 이름에 맞춘다 —
`CompiledMagnitude` 를 받아 `BigNum` 을 내는 기존 private 메서드를 감싸는 것이 목적이다.

- [ ] **Step 4: `World.Ability.cs` 를 만든다**

`common/src/com.bun3.gameplay/Runtime/World.Ability.cs` — 어빌리티 발동 담당 partial:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;

namespace Bun3.Gameplay
{
    // Ability activation. Gate checks and cost payment are synchronous so that repeated activation
    // within one tick sees the spent resources; the ability's effects go through the normal apply
    // queue, and the cooldown effect is applied immediately so its granted tags gate the next call.
    public sealed partial class World
    {
        /// <summary>Attempts to activate a granted ability on a unit.</summary>
        /// <param name="unitId">Unit activating the ability.</param>
        /// <param name="abilityId">Compiled ability id.</param>
        /// <returns><see cref="AbilityActivationResult.Success"/> when every gate passed, otherwise the
        /// reason the activation was refused. Gate failures are results, not exceptions.</returns>
        public AbilityActivationResult TryActivateAbility(TargetId unitId, int abilityId)
        {
            if (!TryGetUnit(unitId, out var unit) || unit is null) return AbilityActivationResult.UnknownUnit;
            if (!_abilities.Contains(abilityId)) return AbilityActivationResult.UnknownAbility;

            var level = unit.Abilities.GetLevel(abilityId);
            if (level == 0) return AbilityActivationResult.NotGranted;

            var ability = _abilities.GetAbility(abilityId);

            var required = ability.RequiredTags;
            for (var i = 0; i < required.Length; i++)
            {
                if (!unit.Tags.Has(required[i])) return AbilityActivationResult.MissingTag;
            }

            var blocked = ability.BlockedTags;
            for (var i = 0; i < blocked.Length; i++)
            {
                if (unit.Tags.Has(blocked[i])) return AbilityActivationResult.BlockedByTag;
            }

            if (!Pipeline.CheckConditions(ability.ActivationConditions, unit.Effects, unit.Effects, hasSource: true))
            {
                return AbilityActivationResult.ConditionFailed;
            }

            var costs = ability.Costs;
            Span<BigNum> amounts = stackalloc BigNum[costs.Length];
            for (var i = 0; i < costs.Length; i++)
            {
                amounts[i] = Pipeline.ComputeMagnitude(
                    costs[i].Magnitude, unit.Effects, unit.Effects, hasSource: true, level);
                if (unit.Attributes.GetCurrent(costs[i].AttributeId) < amounts[i])
                {
                    return AbilityActivationResult.CostInsufficient;
                }
            }

            for (var i = 0; i < costs.Length; i++)
            {
                unit.Attributes.AddBase(costs[i].AttributeId, -amounts[i]);
            }

            if (ability.CooldownEffectId >= 0)
            {
                Pipeline.ApplyImmediate(ability.CooldownEffectId, unitId, unitId, level);
            }

            ApplyAbilityEffects(ability, unitId, level);
            return AbilityActivationResult.Success;
        }

        private void ApplyAbilityEffects(CompiledAbility ability, TargetId unitId, int level)
        {
            var effects = ability.Effects;
            for (var i = 0; i < effects.Length; i++)
            {
                var entry = effects[i];
                var effectLevel = entry.LevelRule == ChainLevelRule.Inherit ? level : entry.FixedLevel;

                if (entry.Selector is null)
                {
                    Pipeline.EnqueueApply(entry.EffectId, unitId, unitId, effectLevel);
                    continue;
                }

                Span<TargetId> targets = stackalloc TargetId[32];
                var ctx = new SelectorContext(
                    unitId, unitId, entry.SelectorParams, Pipeline.Rng, this,
                    eventSource: default, eventTarget: default,
                    eventMagnitude: BigNum.Zero, eventNominalMagnitude: BigNum.Zero);
                var selected = entry.Selector.Select(in ctx, targets);
                var count = selected;
                if (count > targets.Length)
                {
                    Pipeline.ReportSelectorTruncation();
                    count = targets.Length;
                }

                for (var k = 0; k < count; k++)
                {
                    Pipeline.EnqueueApply(entry.EffectId, unitId, targets[k], effectLevel);
                }
            }
        }
    }
}
```

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityActivationTests"`
Expected: PASS (11건).

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/World.Ability.cs common/src/com.bun3.gameplay/Runtime/Effects/EffectPipeline.cs common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ World.TryActivateAbility: 게이트 7종·코스트 즉시 차감·쿨다운 Effect" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 13: EffectSpec.GrantedAbilities — Effect가 부여 소스가 된다

**Files:**
- Modify: `Runtime/Effects/EffectSpec.cs` (`GrantedAbilities`)
- Modify: `Runtime/Effects/EffectCatalog.cs` (`CompiledEffectSpec.GrantedAbilities`)
- Modify: `Runtime/Effects/EffectCatalogBuilder.cs` (이름 → id 해석은 지연)
- Modify: `Runtime/World.cs` (인스턴스 생성·제거·토글 시 부여/회수 훅)
- Test: `common/tests/Bun3.Gameplay.Tests/AbilityGrantSourceTests.cs`

**해석 순서 문제와 그 해법**: `EffectCatalog` 는 `AbilityCatalog` 보다 먼저 빌드된다(어빌리티가 Effect를 참조하므로). 따라서 Effect의 `GrantedAbilities` 는 Build 시점에 id로 해석할 수 없다. **이름을 그대로 컴파일해 두고 `World` 생성 시 `AbilityCatalog` 로 한 번 해석해 캐시**한다. 미해석 이름은 World 생성 시 예외다.

**Interfaces:**
- Consumes: Task 12의 `World`, Task 3의 `AbilitySet`
- Produces:
  - `EffectSpec.GrantedAbilities: List<string>`
  - `CompiledEffectSpec.GrantedAbilityNames: string[]`
  - `World` 가 인스턴스 수명에 맞춰 `GrantSource.Effect(instanceId)` 로 부여·회수한다

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/AbilityGrantSourceTests.cs`:

```csharp
#nullable enable
using System;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityGrantSourceTests
{
    private static Unit MakeUnit(EffectTestKit kit, ulong id)
    {
        ReadOnlySpan<ushort> attributes = stackalloc ushort[]
        {
            EffectTestKit.Hp, EffectTestKit.MaxHp, EffectTestKit.Mp,
            EffectTestKit.Attack, EffectTestKit.Resistance,
        };
        return new Unit(new TargetId(id), EffectTestKit.BuildAttributeRegistry(), attributes, kit.TagCatalog);
    }

    private static (World World, Unit Unit, int AbilityId, EffectTestKit Kit) Build(
        Action<EffectSpec> configureGranter)
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));

        var granter = EffectTestKit.MinimalInfinite("granter");
        granter.GrantedAbilities.Add("fireball");
        configureGranter(granter);
        kit.AddSpec(granter);

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 1 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var abilityBuilder = new AbilityCatalogBuilder();
        abilityBuilder.Add(ability);
        var abilities = abilityBuilder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        var world = new World(kit.Catalog, abilities, new XorShiftRng(5));
        var unit = MakeUnit(kit, 1);
        world.Spawn(unit);
        world.Tick();
        unit.Attributes.SetBase(EffectTestKit.MaxHp, 100);
        unit.Attributes.SetBase(EffectTestKit.Hp, 100);
        unit.Attributes.SetBase(EffectTestKit.Mp, 50);

        return (world, unit, abilities.GetRequiredId("fireball"), kit);
    }

    [Test]
    public void Applying_the_granting_effect_grants_the_ability()
    {
        var (world, unit, abilityId, kit) = Build(_ => { });

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);
        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);
    }

    [Test]
    public void Removing_the_granting_effect_reclaims_the_ability()
    {
        var (world, unit, abilityId, kit) = Build(_ => { });
        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();

        world.Pipeline.RemoveById(unit.Id, unit.Effects.ActiveEffects[0].Id);
        world.Tick();

        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);
    }

    [Test]
    public void Disabling_the_granting_effect_toggles_the_grant_off_and_on()
    {
        var (world, unit, abilityId, kit) = Build(spec =>
            spec.OngoingConditions.Add(new ConditionDef
            {
                Left = Operand.Attribute(EffectTestKit.Mp),
                Op = ComparisonOp.Greater,
                Right = Operand.Constant(0),
            }));

        world.Pipeline.EnqueueApply(kit.SpecId("granter"), unit.Id, unit.Id);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);

        unit.Attributes.SetBase(EffectTestKit.Mp, 0);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.False);

        unit.Attributes.SetBase(EffectTestKit.Mp, 10);
        world.Tick();
        Assert.That(unit.Abilities.IsGranted(abilityId), Is.True);
    }

    [Test]
    public void Unknown_granted_ability_name_fails_when_the_world_is_created()
    {
        var kit = EffectTestKit.Create();
        var granter = EffectTestKit.MinimalInfinite("granter");
        granter.GrantedAbilities.Add("nope");
        kit.AddSpec(granter);
        var abilities = new AbilityCatalogBuilder().Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog);

        Assert.That(
            () => new World(kit.Catalog, abilities, new XorShiftRng(1)),
            Throws.InvalidOperationException.With.Message.Contains("nope"));
    }
}
```

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityGrantSourceTests"`
Expected: FAIL — `GrantedAbilities` 없음.

- [ ] **Step 3: 저작·컴파일 어휘를 더한다**

`EffectSpec.cs`:

```csharp
        /// <summary>Abilities granted to the target while this effect is applied and enabled.</summary>
        public List<string> GrantedAbilities { get; set; } = new List<string>();
```

`EffectCatalog.cs` 의 `CompiledEffectSpec` 에 `internal string[] GrantedAbilityNames { get; }` 를 더한다.
Effect 카탈로그는 어빌리티 카탈로그보다 먼저 빌드되므로 **이름을 그대로 보관**한다.

`EffectCatalogBuilder.CompileSpec` 에서 `spec.GrantedAbilities.ToArray()` 를 넘기고, 빈 이름은 거부한다:

```csharp
            for (var i = 0; i < spec.GrantedAbilities.Count; i++)
            {
                if (string.IsNullOrEmpty(spec.GrantedAbilities[i]))
                    throw new InvalidOperationException($"Effect '{spec.Name}': GrantedAbilities entry is empty.");
            }
```

- [ ] **Step 4: `World` 에서 이름을 한 번 해석하고 수명에 맞춰 부여·회수한다**

`World.cs` 생성자 끝에 해석 캐시를 만든다:

```csharp
            _grantedAbilityIds = new int[effects.Count][];
            for (var specId = 0; specId < effects.Count; specId++)
            {
                var names = effects.GetGrantedAbilityNames(specId);
                var ids = new int[names.Length];
                for (var i = 0; i < names.Length; i++)
                {
                    if (!abilities.TryGetId(names[i], out ids[i]))
                    {
                        throw new InvalidOperationException(
                            $"Effect spec {specId} grants unknown ability '{names[i]}'.");
                    }
                }

                _grantedAbilityIds[specId] = ids;
            }
```

`EffectCatalog` 에 접근자를 더한다:

```csharp
        /// <summary>Names of the abilities a compiled spec grants.</summary>
        /// <param name="id">Compiled spec id.</param>
        /// <returns>Ability names, empty when the spec grants none.</returns>
        public IReadOnlyList<string> GetGrantedAbilityNames(int id) => _specs[id].GrantedAbilityNames;
```

`EffectPipeline` 에 부여/회수 훅을 뚫는다 — 태그 부여·회수와 **정확히 같은 자리**에 붙인다:

```csharp
        /// <summary>Called when an instance starts granting (creation, or re-enable) and stops granting
        /// (removal, or disable). Set by the owning world; null in pipeline-only tests.</summary>
        internal Action<EffectTarget, EffectInstance, bool>? AbilityGrantHook { get; set; }
```

`CreateInstance` 의 GrantedTags 부여 직후, `RemoveInstanceCompletely` 의 GrantedTags 회수 직후,
`EvaluateOngoing` 의 enable/disable 토글 직후에 각각 호출한다:

```csharp
            AbilityGrantHook?.Invoke(target, instance, granting);
```

`World` 생성자에서 훅을 건다:

```csharp
            Pipeline.AbilityGrantHook = OnEffectAbilityGrant;
```

```csharp
        private void OnEffectAbilityGrant(EffectTarget target, EffectInstance instance, bool granting)
        {
            if (!TryGetUnit(target.Id, out var unit) || unit is null) return;

            var source = GrantSource.Effect(instance.Id);
            if (!granting)
            {
                unit.Abilities.Revoke(source);
                return;
            }

            var ids = _grantedAbilityIds[instance.SpecId];
            for (var i = 0; i < ids.Length; i++)
            {
                unit.Abilities.Grant(ids[i], source, instance.Level);
            }
        }
```

`_grantedAbilityIds` 필드 선언을 `World.cs` 에 더한다: `private readonly int[][] _grantedAbilityIds;`

- [ ] **Step 5: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityGrantSourceTests"`
Expected: PASS (4건).

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Runtime/ common/tests/Bun3.Gameplay.Tests/AbilityGrantSourceTests.cs
git commit -m "✨ EffectSpec.GrantedAbilities: Effect가 어빌리티 부여 소스, 토글·제거 시 회수" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 14: Ability JSON 로더 + Effect JSON 신규 필드

**Files:**
- Create: `common/src/com.bun3.gameplay/Catalog/AbilityDefJson.cs`
- Modify: `common/src/com.bun3.gameplay/Catalog/EffectSpecJson.cs` (`raiseEvents`, `grantedAbilities`, 체인의 `eventTag`/`listen`)
- Test: `common/tests/Bun3.Gameplay.Tests/AbilityDefJsonTests.cs`, `common/tests/Bun3.Gameplay.Tests/EffectSpecJsonTests.cs`

`EffectSpecJson` 의 관례를 그대로 따른다: `Newtonsoft.Json` `JsonTextReader`, **strict** (알 수 없는 키는 오류), 오류 메시지에 줄·열 포함, `Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)` 시그니처.

**Interfaces:**
- Consumes: Task 6의 `AbilityDef`/`CostDef`/`AbilityEffectDef`, Task 9의 `RaiseEventDef`/`ChainListen`
- Produces: `public static List<AbilityDef> AbilityDefJson.Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)`

**JSON 스키마**

```json
{
  "schemaVersion": 1,
  "abilities": [
    {
      "name": "fireball",
      "maxLevel": 3,
      "assetTags": ["ability.fire"],
      "requiredTags": ["state.armed"],
      "blockedTags": ["cooldown.fireball", "state.silenced"],
      "activationConditions": [
        { "left": { "attribute": "Mp" }, "op": "GreaterOrEqual", "right": { "constant": "10" } }
      ],
      "costs": [ { "attribute": "Mp", "magnitude": { "base": { "constant": "10" } } } ],
      "cooldownEffect": "Cooldown.Fireball",
      "effects": [
        { "effect": "FireBolt", "selector": "selector.event.target",
          "selectorParams": ["1"], "levelRule": "Inherit", "fixedLevel": 0 }
      ]
    }
  ]
}
```

`left`/`right`/`base`/`magnitude` 의 내부 표기는 `EffectSpecJson` 의 Operand·MagnitudeDef 파서를
**그대로 재사용**한다 — 해당 private 파서를 `internal static` 으로 올려 `AbilityDefJson` 에서 부른다.

**Effect JSON 신규 키**

```json
{
  "raiseEvents": [ { "tag": "event.damage.taken", "attribute": "Hp" } ],
  "grantedAbilities": ["fireball"],
  "chains": [
    { "trigger": "OnEvent", "eventTag": "event.damage.taken", "listen": "AsTarget",
      "effect": "ThornsRetaliate", "selector": "selector.event.source", "levelRule": "Inherit" }
  ]
}
```

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/AbilityDefJsonTests.cs`:

```csharp
#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Catalog;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class AbilityDefJsonTests
{
    private static readonly Dictionary<string, ushort> AttributeNames = new Dictionary<string, ushort>
    {
        ["Hp"] = EffectTestKit.Hp,
        ["MaxHp"] = EffectTestKit.MaxHp,
        ["Mp"] = EffectTestKit.Mp,
        ["Attack"] = EffectTestKit.Attack,
    };

    private static List<AbilityDef> Load(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        return AbilityDefJson.Load(stream, AttributeNames);
    }

    [Test]
    public void Full_ability_round_trips_every_field()
    {
        var abilities = Load("""
        {
          "schemaVersion": 1,
          "abilities": [
            {
              "name": "fireball",
              "maxLevel": 3,
              "assetTags": ["effect.fire.bolt"],
              "requiredTags": ["state.armed"],
              "blockedTags": ["cooldown.fireball"],
              "activationConditions": [
                { "left": { "attribute": "Mp" }, "op": "GreaterOrEqual", "right": { "constant": "10" } }
              ],
              "costs": [ { "attribute": "Mp", "magnitude": { "base": { "constant": "10" } } } ],
              "cooldownEffect": "Cooldown.Fireball",
              "effects": [
                { "effect": "FireBolt", "selector": "selector.event.source",
                  "selectorParams": ["2"], "levelRule": "Fixed", "fixedLevel": 2 }
              ]
            }
          ]
        }
        """);

        Assert.That(abilities.Count, Is.EqualTo(1));
        var ability = abilities[0];
        Assert.That(ability.Name, Is.EqualTo("fireball"));
        Assert.That(ability.MaxLevel, Is.EqualTo(3));
        Assert.That(ability.AssetTags, Is.EqualTo(new[] { "effect.fire.bolt" }));
        Assert.That(ability.RequiredTags, Is.EqualTo(new[] { "state.armed" }));
        Assert.That(ability.BlockedTags, Is.EqualTo(new[] { "cooldown.fireball" }));
        Assert.That(ability.ActivationConditions.Count, Is.EqualTo(1));
        Assert.That(ability.ActivationConditions[0].Op, Is.EqualTo(ComparisonOp.GreaterOrEqual));
        Assert.That(ability.Costs.Count, Is.EqualTo(1));
        Assert.That(ability.Costs[0].AttributeId, Is.EqualTo(EffectTestKit.Mp));
        Assert.That(ability.Costs[0].Magnitude.Base!.Value.Value, Is.EqualTo((BigNum)10));
        Assert.That(ability.CooldownEffect, Is.EqualTo("Cooldown.Fireball"));
        Assert.That(ability.Effects.Count, Is.EqualTo(1));
        Assert.That(ability.Effects[0].EffectName, Is.EqualTo("FireBolt"));
        Assert.That(ability.Effects[0].SelectorTag, Is.EqualTo("selector.event.source"));
        Assert.That(ability.Effects[0].SelectorParams, Is.EqualTo(new[] { (BigNum)2 }));
        Assert.That(ability.Effects[0].LevelRule, Is.EqualTo(ChainLevelRule.Fixed));
        Assert.That(ability.Effects[0].FixedLevel, Is.EqualTo(2));
    }

    [Test]
    public void Minimal_ability_uses_defaults()
    {
        var abilities = Load("""{ "schemaVersion": 1, "abilities": [ { "name": "a" } ] }""");

        Assert.That(abilities[0].MaxLevel, Is.Zero);
        Assert.That(abilities[0].Costs, Is.Empty);
        Assert.That(abilities[0].CooldownEffect, Is.Null);
        Assert.That(abilities[0].Effects, Is.Empty);
    }

    [Test]
    public void Unknown_key_is_rejected_with_line_and_position()
    {
        Assert.That(
            () => Load("""{ "schemaVersion": 1, "abilities": [ { "name": "a", "bogus": 1 } ] }"""),
            Throws.InvalidOperationException.With.Message.Contains("bogus"));
    }

    [Test]
    public void Unknown_attribute_name_in_a_cost_is_rejected()
    {
        Assert.That(
            () => Load("""
            { "schemaVersion": 1, "abilities": [ { "name": "a",
              "costs": [ { "attribute": "Nope", "magnitude": { "base": { "constant": "1" } } } ] } ] }
            """),
            Throws.InvalidOperationException.With.Message.Contains("Nope"));
    }

    [Test]
    public void Unsupported_schema_version_is_rejected()
    {
        Assert.That(
            () => Load("""{ "schemaVersion": 99, "abilities": [] }"""),
            Throws.InvalidOperationException.With.Message.Contains("schemaVersion"));
    }
}
```

`EffectSpecJsonTests.cs` 에 신규 필드 테스트를 더한다:

```csharp
    [Test]
    public void Raise_events_granted_abilities_and_event_chains_load()
    {
        var specs = LoadSpecs("""
        {
          "schemaVersion": 1,
          "effects": [
            {
              "name": "bolt", "durationType": "Instant",
              "modifiers": [ { "attribute": "Hp", "op": "Add", "magnitude": { "base": { "constant": "-10" } } } ],
              "raiseEvents": [ { "tag": "event.damage.taken", "attribute": "Hp" } ]
            },
            {
              "name": "thorns", "durationType": "Infinite",
              "grantedAbilities": ["fireball"],
              "chains": [
                { "trigger": "OnEvent", "eventTag": "event.damage.taken", "listen": "AsTarget",
                  "effect": "bolt", "selector": "selector.event.source", "levelRule": "Inherit" }
              ]
            }
          ]
        }
        """);

        Assert.That(specs[0].RaiseEvents.Count, Is.EqualTo(1));
        Assert.That(specs[0].RaiseEvents[0].Tag, Is.EqualTo("event.damage.taken"));
        Assert.That(specs[0].RaiseEvents[0].AttributeId, Is.EqualTo(EffectTestKit.Hp));
        Assert.That(specs[1].GrantedAbilities, Is.EqualTo(new[] { "fireball" }));
        Assert.That(specs[1].Chains[0].Trigger, Is.EqualTo(ChainTrigger.OnEvent));
        Assert.That(specs[1].Chains[0].EventTag, Is.EqualTo("event.damage.taken"));
        Assert.That(specs[1].Chains[0].Listen, Is.EqualTo(ChainListen.AsTarget));
    }
```

`LoadSpecs` 헬퍼가 없으면 그 파일의 기존 로드 헬퍼 이름에 맞춘다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityDefJsonTests|FullyQualifiedName~EffectSpecJsonTests"`
Expected: FAIL — `AbilityDefJson` 없음.

- [ ] **Step 3: `EffectSpecJson` 의 재사용 파서를 `internal` 로 올린다**

`EffectSpecJson.cs` 에서 아래 private 헬퍼의 접근자를 `internal static` 으로 바꾼다
(이름이 다르면 같은 역할의 메서드를 찾는다):

- Operand 파서 (`ReadOperand` 계열)
- `MagnitudeDef` 파서 (`ReadMagnitude` 계열)
- `ConditionDef` 파서 (`ReadCondition` 계열)
- 태그 문자열 배열 파서 (`ReadStringArray` 계열)
- `BigNum` 배열 파서 (`ReadBigNumArray` 계열)
- 오류 생성기 (`Fail(JsonReader, string)` 계열 — 줄·열을 메시지에 넣는 것)
- enum 파서 (`ReadEnum<T>` 계열)

- [ ] **Step 4: `EffectSpecJson` 에 신규 키를 더한다**

효과 객체 파서의 키 스위치에 추가:

```csharp
                    case "raiseEvents":
                        spec.RaiseEvents = ReadRaiseEvents(reader, attributeNames);
                        break;
                    case "grantedAbilities":
                        spec.GrantedAbilities = ReadStringArray(reader);
                        break;
```

체인 객체 파서의 키 스위치에 추가:

```csharp
                    case "eventTag":
                        edge.EventTag = ReadString(reader);
                        break;
                    case "listen":
                        edge.Listen = ReadEnum<ChainListen>(reader);
                        break;
```

`ReadRaiseEvents` 를 더한다:

```csharp
        private static List<RaiseEventDef> ReadRaiseEvents(
            JsonTextReader reader, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            var list = new List<RaiseEventDef>();
            ExpectStartArray(reader);
            while (reader.Read() && reader.TokenType != JsonToken.EndArray)
            {
                var entry = new RaiseEventDef();
                ExpectStartObject(reader);
                while (reader.Read() && reader.TokenType != JsonToken.EndObject)
                {
                    var key = (string)reader.Value!;
                    switch (key)
                    {
                        case "tag":
                            entry.Tag = ReadString(reader);
                            break;
                        case "attribute":
                            entry.AttributeId = ReadAttributeId(reader, attributeNames);
                            break;
                        default:
                            throw Fail(reader, $"Unknown raiseEvents key '{key}'.");
                    }
                }

                list.Add(entry);
            }

            return list;
        }
```

`ExpectStartArray`/`ExpectStartObject`/`ReadString`/`ReadAttributeId` 는 기존 파일의 동등한
헬퍼 이름에 맞춘다.

- [ ] **Step 5: `AbilityDefJson` 을 만든다**

`common/src/com.bun3.gameplay/Catalog/AbilityDefJson.cs` — 위 스키마를 `EffectSpecJson` 과 동일한
strict 규율(알 수 없는 키 = 오류, 줄·열 포함)로 파싱한다. `schemaVersion` 은 1만 허용한다.
Operand·MagnitudeDef·ConditionDef 는 Step 3에서 올린 `EffectSpecJson` 의 `internal` 파서를 부른다.

```csharp
#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Newtonsoft.Json;

namespace Bun3.Gameplay.Catalog
{
    /// <summary>
    /// Strict JSON loader for ability authoring documents. Unknown keys are errors and messages
    /// carry line and position, matching the effect loader — authoring data is the source of truth,
    /// so silent drift must be impossible.
    /// </summary>
    public static class AbilityDefJson
    {
        private const int SupportedSchemaVersion = 1;

        /// <summary>Loads ability declarations from a UTF-8 JSON stream.</summary>
        /// <param name="utf8Json">Stream holding the document.</param>
        /// <param name="attributeNames">Map from authoring attribute name to attribute id.</param>
        /// <returns>Parsed declarations in document order.</returns>
        /// <exception cref="InvalidOperationException">Thrown on any schema violation.</exception>
        public static List<AbilityDef> Load(Stream utf8Json, IReadOnlyDictionary<string, ushort> attributeNames)
        {
            if (utf8Json is null) throw new ArgumentNullException(nameof(utf8Json));
            if (attributeNames is null) throw new ArgumentNullException(nameof(attributeNames));

            using var streamReader = new StreamReader(utf8Json, new UTF8Encoding(false, true), false, 1024, true);
            var text = streamReader.ReadToEnd();
            using var stringReader = new StringReader(text);
            using var reader = new JsonTextReader(stringReader) { DateParseHandling = DateParseHandling.None };

            var result = new List<AbilityDef>();
            var sawSchemaVersion = false;
            ExpectStartObject(reader);
            while (reader.Read() && reader.TokenType != JsonToken.EndObject)
            {
                var key = (string)reader.Value!;
                switch (key)
                {
                    case "schemaVersion":
                        var version = ReadInt(reader);
                        if (version != SupportedSchemaVersion)
                            throw Fail(reader, $"Unsupported schemaVersion {version}; expected {SupportedSchemaVersion}.");
                        sawSchemaVersion = true;
                        break;
                    case "abilities":
                        result = ReadAbilities(reader, attributeNames);
                        break;
                    default:
                        throw Fail(reader, $"Unknown document key '{key}'.");
                }
            }

            if (!sawSchemaVersion) throw Fail(reader, "Missing schemaVersion.");
            return result;
        }
    }
}
```

**`ReadAbilities` 구현 지침**: `EffectSpecJson` 의 효과 배열 파서와 같은 모양이다 — 배열을 열고,
각 원소마다 객체를 열어 아래 키 스위치를 돌리고, 알 수 없는 키는 `Fail` 로 던진다.
`ExpectStartObject`·`ReadInt`·`Fail` 은 Step 3에서 `internal` 로 올린 `EffectSpecJson` 헬퍼를 쓴다.

```csharp
                    case "name":            def.Name = ReadString(reader); break;
                    case "maxLevel":        def.MaxLevel = ReadInt(reader); break;
                    case "assetTags":       def.AssetTags = EffectSpecJson.ReadStringArray(reader); break;
                    case "requiredTags":    def.RequiredTags = EffectSpecJson.ReadStringArray(reader); break;
                    case "blockedTags":     def.BlockedTags = EffectSpecJson.ReadStringArray(reader); break;
                    case "activationConditions":
                        def.ActivationConditions = EffectSpecJson.ReadConditions(reader, attributeNames); break;
                    case "costs":           def.Costs = ReadCosts(reader, attributeNames); break;
                    case "cooldownEffect":  def.CooldownEffect = ReadString(reader); break;
                    case "effects":         def.Effects = ReadAbilityEffects(reader); break;
                    default: throw Fail(reader, $"Unknown ability key '{key}'.");
```

```csharp
                    // ReadCosts entry keys
                    case "attribute": cost.AttributeId = EffectSpecJson.ReadAttributeId(reader, attributeNames); break;
                    case "magnitude": cost.Magnitude = EffectSpecJson.ReadMagnitude(reader, attributeNames); break;

                    // ReadAbilityEffects entry keys
                    case "effect":         entry.EffectName = ReadString(reader); break;
                    case "selector":       entry.SelectorTag = ReadString(reader); break;
                    case "selectorParams": entry.SelectorParams = EffectSpecJson.ReadBigNumArray(reader); break;
                    case "levelRule":      entry.LevelRule = EffectSpecJson.ReadEnum<ChainLevelRule>(reader); break;
                    case "fixedLevel":     entry.FixedLevel = ReadInt(reader); break;
```

- [ ] **Step 6: 통과 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~AbilityDefJsonTests|FullyQualifiedName~EffectSpecJsonTests"`
Expected: PASS.

- [ ] **Step 7: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 8: 커밋**

```bash
git add common/src/com.bun3.gameplay/Catalog/ common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ Ability JSON 로더 + Effect JSON 신규 필드(raiseEvents·grantedAbilities·OnEvent 체인)" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 15: 출하 컴파일 아티팩트 (백로그 ②)

**Files:**
- Create: `common/src/com.bun3.gameplay/Catalog/GameplayCatalogBinary.cs` (Effect + Ability 한 파일)
- Test: `common/tests/Bun3.Gameplay.Tests/GameplayCatalogBinaryTests.cs`

**설계 결정(스펙 §10 구체화)**: Effect와 Ability를 **한 아티팩트**에 담는다. 둘은 항상 같이 빌드되고
어빌리티가 Effect id를 참조하므로 따로 두면 두 파일의 버전 정합을 별도로 지켜야 한다.

`TagCatalogBinary`/`TagCatalogBinaryWriter` 의 구조를 따른다 — 매직 + 스키마 버전 + fingerprint +
체크섬 헤더, 그 뒤 페이로드, 읽기는 `PayloadReader` 스타일 경계 검사.

**Interfaces:**
- Consumes: Task 6의 `AbilityCatalog`, 기존 `EffectCatalog`, `TagCatalog.Fingerprint`
- Produces:
  - `public static void GameplayCatalogBinary.Write(Stream output, EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags, AttributeRegistry attributes)`
  - `public static (EffectCatalog Effects, AbilityCatalog Abilities) GameplayCatalogBinary.Load(Stream input, TagCatalog tags, SeamRegistry seams, AttributeRegistry attributes)`
  - `public static uint AttributeRegistry.Signature { get; }` — 등록된 (id, 정책, 경계) 를 순서대로 FNV-1a 해싱한 값

- [ ] **Step 1: 실패 테스트를 쓴다**

`common/tests/Bun3.Gameplay.Tests/GameplayCatalogBinaryTests.cs`:

```csharp
#nullable enable
using System;
using System.IO;
using Bun3.Gameplay.Abilities;
using Bun3.Gameplay.Catalog;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Tags;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public sealed class GameplayCatalogBinaryTests
{
    private static (EffectTestKit Kit, AbilityCatalog Abilities) BuildSource()
    {
        var kit = EffectTestKit.Create();
        kit.AddSpec(EffectTestKit.MinimalInstant("bolt"));
        kit.AddSpec(EffectTestKit.MinimalDuration("burn", ticks: 4));

        var ability = new AbilityDef { Name = "fireball", MaxLevel = 2 };
        ability.Effects.Add(new AbilityEffectDef { EffectName = "bolt" });
        var builder = new AbilityCatalogBuilder();
        builder.Add(ability);
        return (kit, builder.Build(kit.TagCatalog, kit.Seams, kit.Attributes, kit.Catalog));
    }

    [Test]
    public void Round_trip_preserves_names_and_ids()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var (loadedEffects, loadedAbilities) =
            GameplayCatalogBinary.Load(stream, kit.TagCatalog, kit.Seams, kit.Attributes);

        Assert.That(loadedEffects.Count, Is.EqualTo(kit.Catalog.Count));
        Assert.That(loadedEffects.GetRequiredId("bolt"), Is.EqualTo(kit.Catalog.GetRequiredId("bolt")));
        Assert.That(loadedEffects.GetRequiredId("burn"), Is.EqualTo(kit.Catalog.GetRequiredId("burn")));
        Assert.That(loadedAbilities.Count, Is.EqualTo(1));
        Assert.That(loadedAbilities.GetRequiredId("fireball"), Is.EqualTo(abilities.GetRequiredId("fireball")));
    }

    [Test]
    public void Tag_catalog_fingerprint_mismatch_is_rejected()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;

        var otherKit = EffectTestKit.CreateWithExtraTag("state.extra");

        Assert.That(
            () => GameplayCatalogBinary.Load(stream, otherKit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogCompatibilityException>());
    }

    [Test]
    public void Truncated_payload_is_rejected()
    {
        var (kit, abilities) = BuildSource();
        using var full = new MemoryStream();
        GameplayCatalogBinary.Write(full, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        var bytes = full.ToArray();

        using var truncated = new MemoryStream(bytes, 0, bytes.Length - 4);

        Assert.That(
            () => GameplayCatalogBinary.Load(truncated, kit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogFormatException>());
    }

    [Test]
    public void Corrupted_byte_fails_the_checksum()
    {
        var (kit, abilities) = BuildSource();
        using var full = new MemoryStream();
        GameplayCatalogBinary.Write(full, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        var bytes = full.ToArray();
        bytes[bytes.Length - 1] ^= 0xFF;

        using var corrupted = new MemoryStream(bytes);

        Assert.That(
            () => GameplayCatalogBinary.Load(corrupted, kit.TagCatalog, kit.Seams, kit.Attributes),
            Throws.InstanceOf<TagCatalogFormatException>());
    }

    [Test]
    public void Binary_and_json_paths_produce_the_same_scenario_hash()
    {
        var (kit, abilities) = BuildSource();
        using var stream = new MemoryStream();
        GameplayCatalogBinary.Write(stream, kit.Catalog, abilities, kit.TagCatalog, kit.Attributes);
        stream.Position = 0;
        var (loadedEffects, loadedAbilities) =
            GameplayCatalogBinary.Load(stream, kit.TagCatalog, kit.Seams, kit.Attributes);

        var fromBuilder = EffectScenario.RunHash(kit.Catalog, abilities, kit.TagCatalog, seed: 4242);
        var fromBinary = EffectScenario.RunHash(loadedEffects, loadedAbilities, kit.TagCatalog, seed: 4242);

        Assert.That(fromBinary, Is.EqualTo(fromBuilder));
    }
}
```

`EffectTestKit.CreateWithExtraTag(string extraTagPath)` 는 **이 태스크에서** 만든다 —
`CatalogJson` 에 태그 하나를 더한 카탈로그로 킷을 만들어 다른 fingerprint를 얻는 용도다:

```csharp
    /// <summary>Creates a kit whose tag catalog has one extra tag, so its fingerprint differs.</summary>
    /// <param name="extraTagPath">Canonical path of the tag to append.</param>
    public static EffectTestKit CreateWithExtraTag(string extraTagPath)
    {
        var json = CatalogJson.Replace("\"tags\": [", $"\"tags\": [\n        {{ \"name\": \"{extraTagPath}\" }},");
        return new EffectTestKit(json);
    }
```

`EffectTestKit` 에 `private EffectTestKit(string catalogJson)` 생성자를 더하고 기존
매개변수 없는 생성자가 `CatalogJson` 을 넘기게 바꾼다.

`EffectScenario.RunHash(EffectCatalog, AbilityCatalog, TagCatalog, ulong seed)` 만 Task 16에서
만든다. 따라서 마지막 테스트 `Binary_and_json_paths_produce_the_same_scenario_hash` 에만
`[Ignore("Enabled in Task 16 once EffectScenario.RunHash exists.")]` 를 붙였다가 Task 16에서 해제한다.

- [ ] **Step 2: 실패 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~GameplayCatalogBinaryTests"`
Expected: FAIL — `GameplayCatalogBinary` 없음.

- [ ] **Step 3: `AttributeRegistry.Signature` 를 더한다**

`Runtime/Attributes/AttributeRegistry.cs`:

```csharp
        /// <summary>
        /// Stable hash of the registered attribute shape — ids, clamp bounds, and boundary policies in
        /// evaluation order. A compiled catalog artifact is only valid against the same shape, because
        /// its magnitudes and clamps were resolved against it.
        /// </summary>
        public uint Signature { get; }
```

`AttributeRegistryBuilder.Build()` 에서 계산해 넘긴다 (FNV-1a 32비트, 평가 순서대로
`attributeId`, `min.Kind`, `min.AttributeId`, `min.Value` 의 Mantissa·Exponent, `max` 동일,
`onMaxIncrease`, `onMaxDecrease` 를 순서대로 먹인다).

- [ ] **Step 4: `GameplayCatalogBinary` 를 만든다**

`common/src/com.bun3.gameplay/Catalog/GameplayCatalogBinary.cs`. 형식:

```
헤더 (고정 길이)
  magic       "BUN3GPCAT"  (9 bytes)
  schema      ushort       (현재 1)
  tagFingerprint           TagCatalog.Fingerprint 와 같은 길이·형식
  attributeSignature uint
  payloadChecksum    32-byte hash (TagCatalogBinary 와 같은 해시 함수)
페이로드
  effectCount  uint
  effect ×N    (이름, DurationType, DurationTicks, PeriodTicks, StackPolicy, Modifiers,
                Executions(확장점 태그 인덱스), 조건들, GrantedTags/AssetTags/ImmunityTags 인덱스,
                Chains(트리거·EventTag·Listen·EffectId·셀렉터 태그 인덱스·파라미터·조건·레벨규칙),
                RemoveOnApplyTags, ChanceToApply, DurationPerLevel, DurationScale, Dr*,
                RaiseEvents, GrantedAbilityNames)
  abilityCount uint
  ability ×N   (이름, MaxLevel, AssetTags/RequiredTags/BlockedTags 인덱스,
                ActivationConditions, Costs, CooldownEffectId, Effects)
```

- 확장점(`IMagnitudeCalc`/`IExecutionCalc`/`ITargetSelector`)은 **직렬화하지 않는다** — 태그
  인덱스만 싣고 `Load` 가 넘겨받은 `SeamRegistry` 로 다시 해석한다. 미등록 태그는 예외다.
- `GameplayTag` 는 `ushort` 인덱스로만 싣는다. fingerprint 검사가 인덱스 의미를 보장한다.
- `BigNum` 은 `long` 가수 + `int` 지수로 싣는다.
- 손상·절단 검출은 `TagCatalogBinary` 의 `PayloadReader` 패턴(남은 바이트 검사 + 필드명 포함
  예외)을 그대로 쓰고, 예외 타입도 `TagCatalogFormatException` 을 재사용한다.
- fingerprint·서명 불일치는 `TagCatalogCompatibilityException` 을 던진다.
- 최대 크기 가드는 `TagCatalogBinary.MaximumCatalogBytes` 와 같은 64MB.

- [ ] **Step 5: 통과 확인 (마지막 테스트 제외)**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj --filter "FullyQualifiedName~GameplayCatalogBinaryTests"`
Expected: PASS (4건 + Ignore 1건).

- [ ] **Step 6: 전체 테스트 + 빌드**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj && dotnet build Bun3.sln`
Expected: PASS, `0 Warning(s)`.

- [ ] **Step 7: 커밋**

```bash
git add common/src/com.bun3.gameplay/Catalog/GameplayCatalogBinary.cs common/src/com.bun3.gameplay/Runtime/Attributes/ common/tests/Bun3.Gameplay.Tests/
git commit -m "✨ 출하 컴파일 아티팩트: Effect·Ability 카탈로그 바이너리 + fingerprint 검증" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

## Task 16: 결정론 오라클 · 무할당 스모크 · 버전 범프

**Files:**
- Modify: `common/src/com.bun3.gameplay/Tests/Runtime/EffectScenario.cs` (World·Ability 시나리오 확장)
- Modify: `common/tests/Bun3.Gameplay.Tests/EffectDeterminismTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/AllocationSmokeTests.cs`
- Modify: `common/tests/Bun3.Gameplay.Tests/GameplayCatalogBinaryTests.cs` (`[Ignore]` 해제)
- Modify: `common/src/com.bun3.gameplay/package.json` (0.13.0 → 0.14.0)
- Modify: `Bun3.Gameplay` 의 nuspec/csproj 버전
- Test: 위 파일들

**Interfaces:**
- Consumes: Task 7·12·15
- Produces: `EffectScenario.RunHash(EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags, ulong seed)` → `ulong`

- [ ] **Step 1: 시나리오 해시 헬퍼를 확장한다**

`EffectScenario.cs` 에 World 기반 오버로드를 더한다. 고정 시드로 유닛 3기를 스폰하고 200틱 동안
매 틱 결정론적으로 (어빌리티 발동 | 효과 적용 | 디스펠) 을 골라 실행한 뒤, 전 유닛의
속성 Base·활성 인스턴스(id·스펙·레벨·스택·남은틱)·부여 목록을 순서대로 FNV-1a 로 접어 `ulong` 을 낸다.

```csharp
    /// <summary>Runs the fixed scenario and folds the resulting world state into one hash.</summary>
    /// <param name="effects">Effect catalog to run against.</param>
    /// <param name="abilities">Ability catalog to run against.</param>
    /// <param name="tags">Tag catalog the units count owned tags with.</param>
    /// <param name="seed">Rng seed; the same seed must always produce the same hash.</param>
    /// <returns>State hash after the scenario.</returns>
    public static ulong RunHash(EffectCatalog effects, AbilityCatalog abilities, TagCatalog tags, ulong seed)
```

- [ ] **Step 2: 결정론 오라클 3종에 시나리오를 더한다**

`EffectDeterminismTests.cs` 에 추가:

```csharp
    [Test]
    public void Same_seed_produces_the_same_world_state_hash()
    {
        var kit = ScenarioKit();
        var first = EffectScenario.RunHash(kit.Effects, kit.Abilities, kit.Tags, seed: 20260820);
        var second = EffectScenario.RunHash(kit.Effects, kit.Abilities, kit.Tags, seed: 20260820);

        Assert.That(second, Is.EqualTo(first));
    }

    [Test]
    public void Reflection_round_trip_leaves_no_residue()
    {
        // A reflects onto B and B reflects onto A; after the exchange settles, removing both
        // reflect effects must return every attribute Base to its starting value.
        var kit = ScenarioKit();
        var before = EffectScenario.CaptureBases(kit);
        EffectScenario.RunMutualReflection(kit, ticks: 30);
        EffectScenario.RemoveAllEffects(kit);
        for (var i = 0; i < 3; i++) kit.World.Tick();

        Assert.That(EffectScenario.CaptureBases(kit), Is.EqualTo(before));
    }

    [Test]
    public void Grant_and_revoke_round_trip_restores_the_ability_set()
    {
        var kit = ScenarioKit();
        var before = EffectScenario.CaptureGrants(kit);
        EffectScenario.ApplyGrantingEffectsAndRemoveThem(kit);

        Assert.That(EffectScenario.CaptureGrants(kit), Is.EqualTo(before));
    }
```

`ScenarioKit`, `CaptureBases`, `CaptureGrants`, `RunMutualReflection`, `RemoveAllEffects`,
`ApplyGrantingEffectsAndRemoveThem` 를 `EffectScenario` 에 함께 만든다. 각각은
Task 9~13에서 만든 공개 API만 쓴다.

- [ ] **Step 3: 무할당 스모크를 확장한다**

`AllocationSmokeTests.cs` 에 추가:

```csharp
    [Test]
    public void Settled_world_tick_with_event_listeners_allocates_nothing()
    {
        var kit = AllocationKit.WithEventListeners();
        for (var i = 0; i < 10; i++) kit.World.Tick();   // settle

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++) kit.World.Tick();
        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.That(after - before, Is.Zero);
    }

    [Test]
    public void Raising_events_on_a_settled_world_allocates_nothing()
    {
        var kit = AllocationKit.WithEventListeners();
        for (var i = 0; i < 10; i++) kit.World.Tick();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 100; i++)
        {
            kit.World.Pipeline.EnqueueApply(kit.DamageSpecId, kit.AttackerId, kit.DefenderId);
            kit.World.Tick();
        }

        var after = GC.GetAllocatedBytesForCurrentThread();
        Assert.That(after - before, Is.Zero);
    }
```

**주의**: 기존 무할당 테스트의 관례를 따라 카운터 읽기를 NUnit 제약 할당 밖에 둔다 —
`Assert.That(after - before, Is.Zero)` 처럼 **먼저 지역 변수로 읽고** 나서 단언한다
(브랜치 최신 커밋 `eeb79b5` 가 고친 오탐 원인).

측정에서 할당이 잡히면 원인을 찾아 고친다. 흔한 원인 셋: `SelectorContext` 에 넘긴 람다,
`IReadOnlyList<TargetId>` 열거자 박싱, `stackalloc` 이 아닌 배열 생성.

- [ ] **Step 4: Task 15의 `[Ignore]` 를 해제한다**

`GameplayCatalogBinaryTests.Binary_and_json_paths_produce_the_same_scenario_hash` 의
`[Ignore]` 속성을 제거한다.

- [ ] **Step 5: 전체 테스트 확인**

Run: `dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
Expected: PASS 전부.

- [ ] **Step 6: 버전을 올린다**

`common/src/com.bun3.gameplay/package.json`:

```json
  "version": "0.14.0",
```

`Bun3.Gameplay` 의 NuGet 버전이 선언된 곳(csproj `<Version>` 또는 nuspec)도 `0.14.0` 으로 맞춘다.
`grep -rn "0\.13\.0" --include=*.csproj --include=*.nuspec --include=*.json common/` 으로 누락을 확인한다.

- [ ] **Step 7: 빌드 + 전체 테스트 최종 확인**

Run: `dotnet build Bun3.sln && dotnet test common/tests/Bun3.Gameplay.Tests/Bun3.Gameplay.Tests.csproj`
Expected: `0 Warning(s)`, PASS 전부.

- [ ] **Step 8: 커밋**

```bash
git add common/
git commit -m "✅ 슬라이스 3 결정론 오라클·무할당 스모크 추가 + Bun3.Gameplay 0.14.0" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 9: 퍼블리시·머지 금지 확인**

Run: `git log --oneline main..HEAD && git status`
Expected: 커밋이 브랜치 `Bun3/gameplay-slice3-ability` 에만 있고, 작업 트리가 깨끗하며,
**`dotnet nuget push` 나 `git merge` 를 실행하지 않았다.** 여기서 멈추고 사용자 확인을 기다린다.
