#nullable enable
namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// 결정론 BigNum 수식 평가기입니다. 변수 <c>x</c> 하나, 사칙연산(<c>+ - * /</c>), 단항 <c>-</c>,
    /// 괄호, 정수 거듭제곱 <c>^</c>(지수는 0..64 정수 리터럴만 허용 — <c>x^2</c>는 되지만 <c>2^x</c>·
    /// <c>x^x</c>·비정수 지수는 거부)을 지원합니다. 우선순위는 <c>^</c> &gt; 단항 <c>-</c> &gt;
    /// <c>* /</c> &gt; <c>+ -</c>. 저작·기동(카탈로그 Build) 전용이라 할당을 아끼지 않는
    /// 재귀 하강 파서로 구현합니다 — 틱 핫패스에서는 미리 컴파일된 레벨 배열만 읽습니다.
    /// float/double은 전혀 쓰지 않고 전부 BigNum 정수 산술입니다.
    /// </summary>
    public static class BigNumFormula
    {
        /// <summary>수식 문법만 검사합니다(0 나눗셈 여부는 검사하지 않습니다 — 그건 평가 시점의 일입니다).</summary>
        /// <param name="formula">검사할 수식 문자열입니다.</param>
        /// <param name="error">실패 시 오류 메시지이고, 성공 시 <see langword="null"/>입니다.</param>
        /// <returns>문법이 유효하면 <see langword="true"/>입니다.</returns>
        public static bool TryValidate(string formula, out string? error)
        {
            if (string.IsNullOrEmpty(formula) || !new Parser(formula).TryParse(evaluate: false, BigNum.Zero, out _))
            {
                error = $"유효하지 않은 수식입니다: {formula}";
                return false;
            }

            error = null;
            return true;
        }

        /// <summary>수식을 <paramref name="x"/> 값으로 평가합니다. 0 나눗셈·파스 실패는 모두 false입니다.</summary>
        /// <param name="formula">평가할 수식 문자열입니다.</param>
        /// <param name="x">변수 <c>x</c>에 대입할 값입니다.</param>
        /// <param name="result">성공 시 평가 결과이고, 실패 시 <see cref="BigNum.Zero"/>입니다.</param>
        /// <returns>평가에 성공했으면 <see langword="true"/>입니다.</returns>
        public static bool TryEvaluate(string formula, BigNum x, out BigNum result)
        {
            if (string.IsNullOrEmpty(formula))
            {
                result = BigNum.Zero;
                return false;
            }

            return new Parser(formula).TryParse(evaluate: true, x, out result);
        }

        // 재귀 하강 파서. evaluate=false면 문법만 훑고(0 나눗셈은 검사하지 않음) 실제 산술은 하지 않는다.
        // 문법: add := mul (('+'|'-') mul)* / mul := unary (('*'|'/') unary)* /
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
                            if (rhs.IsZero) return false;   // 0 나눗셈 — 평가 실패(문법 오류 아님)
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
                if (_pos == digitsStart) return false;   // 지수는 정수 리터럴 필수(변수·소수·부호 불가)

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

            // BigNum.TryParse와 같은 리터럴 문법(부호 제외 — 단항 -가 별도 처리): \d+(\.\d+)?([eE][+-]?\d+)?
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
