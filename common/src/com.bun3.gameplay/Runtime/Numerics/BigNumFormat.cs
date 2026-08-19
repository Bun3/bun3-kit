using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>How to render values beyond the unit-table cap (MaxUnits).</summary>
    public enum BigNumOverflowStyle
    {
        /// <summary>Fall back to scientific notation ("1.23e45"). Suits width-constrained HUDs.</summary>
        Scientific,

        /// <summary>Keep the highest allowed unit and let the integer part grow ("12,345M")</summary>
        TopUnit,
    }

    /// <summary>
    /// BigNum display format settings — the game decides how many digits per group
    /// (<see cref="GroupDigits"/>), with which unit strings (<see cref="Units"/>), and up to how
    /// many units (<see cref="MaxUnits"/>). Fraction digits, fixed fraction (keep trailing zeros),
    /// integer group separator, and cap-overflow style are also configurable.
    /// </summary>
    public sealed class BigNumFormat
    {
        private readonly string[] _units;

        /// <summary>Decimal digits one unit covers (Korean style 4, alphabetic style 3).</summary>
        public int GroupDigits { get; }

        /// <summary>Read-only view of the unit string table. [0] is the empty (no-unit) string.</summary>
        public IReadOnlyList<string> Units { get; }

        internal string GetUnit(int index) => _units[index];

        /// <summary>Maximum unit applications (index of the highest unit used). At most Units.Length - 1.</summary>
        public int MaxUnits { get; }

        /// <summary>Maximum fraction digits (0-9).</summary>
        public int MaxFractionDigits { get; }

        /// <summary>True trims trailing fraction zeros ("1.5K", "2M"); false keeps a fixed
        /// fraction ("1.50K", "2.00M" — display width stays stable as values change).</summary>
        public bool TrimFractionZeros { get; }

        /// <summary>Thousands separator for the integer part (e.g. ','). Null for none.</summary>
        public char? IntegerGroupSeparator { get; }

        /// <summary>How values beyond the unit cap are rendered.</summary>
        public BigNumOverflowStyle OverflowStyle { get; }

        /// <summary>Alphabetic abbreviations (1K = 10^3): "", K, M, B, T, Qa, Qi. Beyond the cap, defaults to the top unit.</summary>
        public static readonly BigNumFormat Base =
            new BigNumFormat(3, new[] { "", "K", "M", "B", "T", "Qa", "Qi" });

        /// <summary>Korean-style units (one unit per 10^4), 12 units up to 10^48. Beyond the cap, defaults to the top unit.</summary>
        public static readonly BigNumFormat Korean =
            new BigNumFormat(4, new[] { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" });

        /// <summary>Creates a format. A negative maxUnits uses the whole unit table (Units.Length - 1).</summary>
        public BigNumFormat(
            int groupDigits,
            string[] units,
            int maxUnits = -1,
            int maxFractionDigits = 2,
            bool trimFractionZeros = true,
            char? integerGroupSeparator = null,
            BigNumOverflowStyle overflowStyle = BigNumOverflowStyle.TopUnit)
        {
            if (groupDigits < 1 || groupDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(groupDigits));
            }

            if (units == null)
            {
                throw new ArgumentNullException(nameof(units));
            }

            if (units.Length == 0)
            {
                throw new ArgumentException("Units must contain at least one element.", nameof(units));
            }

            for (var i = 0; i < units.Length; i++)
            {
                if (units[i] == null)
                {
                    throw new ArgumentException("Units must not contain null elements.", nameof(units));
                }
            }

            if (units[0].Length != 0)
            {
                throw new ArgumentException("Units[0] must be the empty string.", nameof(units));
            }

            if (maxUnits >= units.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(maxUnits), "MaxUnits must be at most Units.Length - 1.");
            }

            if (maxFractionDigits < 0 || maxFractionDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFractionDigits));
            }

            GroupDigits = groupDigits;
            _units = (string[])units.Clone();
            Units = new ReadOnlyCollection<string>(_units);
            MaxUnits = maxUnits < 0 ? units.Length - 1 : maxUnits;
            MaxFractionDigits = maxFractionDigits;
            TrimFractionZeros = trimFractionZeros;
            IntegerGroupSeparator = integerGroupSeparator;
            OverflowStyle = overflowStyle;
        }
    }
}
