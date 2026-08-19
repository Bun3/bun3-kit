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
