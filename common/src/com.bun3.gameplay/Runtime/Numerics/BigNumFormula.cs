#nullable enable
namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// Deterministic BigNum formula evaluator. Supports a single variable <c>x</c>, the four
    /// arithmetic operators (<c>+ - * /</c>), unary <c>-</c>, parentheses, and integer power
    /// <c>^</c> (exponent must be an integer literal 0..64 — <c>x^2</c> is allowed; <c>2^x</c>,
    /// <c>x^x</c>, and non-integer exponents are rejected). Precedence: <c>^</c> &gt; unary
    /// <c>-</c> &gt; <c>* /</c> &gt; <c>+ -</c>. Authoring/startup (catalog build) only, so it is
    /// an allocation-tolerant recursive-descent parser — tick hot paths read only precompiled
    /// level arrays. No float/double anywhere; all BigNum integer arithmetic.
    /// </summary>
    public static class BigNumFormula
    {
        /// <summary>Checks formula grammar only (division by zero is not checked — that is an evaluation-time concern).</summary>
        /// <param name="formula">Formula text to check.</param>
        /// <param name="error">Error message on failure; <see langword="null"/> on success.</param>
        /// <returns><see langword="true"/> when the grammar is valid.</returns>
        public static bool TryValidate(string formula, out string? error)
        {
            if (string.IsNullOrEmpty(formula) || !new Parser(formula).TryParse(evaluate: false, BigNum.Zero, out _))
            {
                error = $"Invalid formula: {formula}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>Evaluates the formula with the given <paramref name="x"/>. Division by zero and parse failures both return false.</summary>
        /// <param name="formula">Formula text to evaluate.</param>
        /// <param name="x">Value substituted for the variable <c>x</c>.</param>
        /// <param name="result">Evaluation result on success; <see cref="BigNum.Zero"/> on failure.</param>
        /// <returns><see langword="true"/> when evaluation succeeds.</returns>
        public static bool TryEvaluate(string formula, BigNum x, out BigNum result)
        {
            if (string.IsNullOrEmpty(formula))
            {
                result = BigNum.Zero;
                return false;
            }

            return new Parser(formula).TryParse(evaluate: true, x, out result);
        }

        // Recursive-descent parser. With evaluate=false it only checks grammar (no division-by-zero
        // check) and performs no arithmetic.
        // Grammar: add := mul (('+'|'-') mul)* / mul := unary (('*'|'/') unary)* /
        // unary := '-' unary | pow / pow := primary ('^' digits)? / primary := '(' add ')' | 'x' | literal
        private struct Parser
        {
            private readonly string _text;
            private int _pos;

            internal Parser(string text)
            {
                _text = text;
                _pos = 0;
            }

            internal bool TryParse(bool evaluate, BigNum x, out BigNum value)
            {
                SkipWhitespace();
                if (!TryParseAdd(evaluate, x, out value)) return false;
                SkipWhitespace();
                return _pos == _text.Length;
            }

            private bool TryParseAdd(bool evaluate, BigNum x, out BigNum value)
            {
                if (!TryParseMul(evaluate, x, out value)) return false;

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || (_text[_pos] != '+' && _text[_pos] != '-')) return true;

                    var op = _text[_pos];
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseMul(evaluate, x, out var rhs)) return false;
                    if (evaluate) value = op == '+' ? value + rhs : value - rhs;
                }
            }

            private bool TryParseMul(bool evaluate, BigNum x, out BigNum value)
            {
                if (!TryParseUnary(evaluate, x, out value)) return false;

                while (true)
                {
                    SkipWhitespace();
                    if (_pos >= _text.Length || (_text[_pos] != '*' && _text[_pos] != '/')) return true;

                    var op = _text[_pos];
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseUnary(evaluate, x, out var rhs)) return false;

                    if (evaluate)
                    {
                        if (op == '*')
                        {
                            value *= rhs;
                        }
                        else
                        {
                            if (rhs.IsZero) return false;   // division by zero — evaluation failure (not a grammar error)
                            value /= rhs;
                        }
                    }
                }
            }

            private bool TryParseUnary(bool evaluate, BigNum x, out BigNum value)
            {
                SkipWhitespace();
                if (_pos < _text.Length && _text[_pos] == '-')
                {
                    _pos++;
                    SkipWhitespace();
                    if (!TryParseUnary(evaluate, x, out var inner))
                    {
                        value = BigNum.Zero;
                        return false;
                    }

                    value = evaluate ? -inner : BigNum.Zero;
                    return true;
                }

                return TryParsePow(evaluate, x, out value);
            }

            private bool TryParsePow(bool evaluate, BigNum x, out BigNum value)
            {
                if (!TryParsePrimary(evaluate, x, out value)) return false;

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

                if (evaluate)
                {
                    var baseValue = value;
                    value = BigNum.One;
                    for (var i = 0; i < exponent; i++) value *= baseValue;
                }

                return true;
            }

            private bool TryParsePrimary(bool evaluate, BigNum x, out BigNum value)
            {
                value = BigNum.Zero;
                SkipWhitespace();
                if (_pos >= _text.Length) return false;

                if (_text[_pos] == '(')
                {
                    _pos++;
                    if (!TryParseAdd(evaluate, x, out value)) return false;
                    SkipWhitespace();
                    if (_pos >= _text.Length || _text[_pos] != ')') return false;
                    _pos++;
                    return true;
                }

                if (_text[_pos] == 'x')
                {
                    _pos++;
                    value = evaluate ? x : BigNum.Zero;
                    return true;
                }

                return TryParseLiteral(evaluate, out value);
            }

            // Same literal grammar as BigNum.TryParse minus the sign (unary - handled separately): \d+(\.\d+)?([eE][+-]?\d+)?
            private bool TryParseLiteral(bool evaluate, out BigNum value)
            {
                value = BigNum.Zero;
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
                value = evaluate ? parsed : BigNum.Zero;
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
