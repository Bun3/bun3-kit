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
