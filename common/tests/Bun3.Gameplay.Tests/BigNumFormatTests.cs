using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Bun3.Gameplay.Numerics;
using NUnit.Framework;

namespace Bun3.Gameplay.Tests;

[TestFixture]
public class BigNumFormatTests
{
    private static string Format(BigNum value, BigNumFormat? format = null)
    {
        Span<char> buffer = stackalloc char[64];
        Assert.That(value.TryFormat(buffer, out var written, format), Is.True);
        return new string(buffer[..written]);
    }

    [Test]
    public void Small_integers_render_plain()
    {
        Assert.That(Format(0), Is.EqualTo("0"));
        Assert.That(Format(42), Is.EqualTo("42"));
        Assert.That(Format(-42), Is.EqualTo("-42"));
        Assert.That(Format(999), Is.EqualTo("999"));
    }

    [Test]
    public void Small_fractions_render_with_decimal_point()
    {
        Assert.That(Format(BigNum.FromParts(125, -2)), Is.EqualTo("1.25"));
        Assert.That(Format(BigNum.FromParts(-5, -1)), Is.EqualTo("-0.5"));
    }

    [Test]
    public void Alpha_units_group_by_thousands()
    {
        Assert.That(Format((BigNum)1_500), Is.EqualTo("1.5K"));
        Assert.That(Format((BigNum)2_000_000), Is.EqualTo("2M"));
        Assert.That(Format((BigNum)3_450_000_000L), Is.EqualTo("3.45B"));
        Assert.That(Format((BigNum)7_000_000_000_000L), Is.EqualTo("7T"));
        Assert.That(Format((BigNum)(-1_500)), Is.EqualTo("-1.5K"));
    }

    [Test]
    public void Korean_units_group_by_ten_thousands()
    {
        var fmt = BigNumFormat.Korean;
        Assert.That(Format((BigNum)15_000, fmt), Is.EqualTo("1.5만"));
        Assert.That(Format((BigNum)200_000_000, fmt), Is.EqualTo("2억"));
        Assert.That(Format((BigNum)3_000_000_000_000L, fmt), Is.EqualTo("3조"));
        Assert.That(Format(BigNum.FromParts(92, 17), fmt), Is.EqualTo("920경"));
    }

    [Test]
    public void Fraction_truncates_to_two_digits_and_strips_zeros()
    {
        Assert.That(Format((BigNum)1_234), Is.EqualTo("1.23K"));     // 1.234 truncated to 1.23
        Assert.That(Format((BigNum)1_204), Is.EqualTo("1.2K"));      // trailing zero stripped
        Assert.That(Format((BigNum)1_004), Is.EqualTo("1K"));        // .00 stripped
    }

    [Test]
    public void Default_overflow_keeps_top_unit_and_grows_integer()
    {
        // Default style (TopUnit): keep the top unit and grow the integer part
        Assert.That(Format(BigNum.FromParts(123, 43)),
            Is.EqualTo("1230000000000000000000000000Qi"));   // 1.23e45, Qi = 10^18
        Assert.That(Format(BigNum.FromParts(3, 20)), Is.EqualTo("300Qi"));
    }

    [Test]
    public void Scientific_overflow_style_falls_back_to_exponent()
    {
        var sci = new BigNumFormat(3, new[] { "", "K", "M", "B", "T", "Qa", "Qi" },
            overflowStyle: BigNumOverflowStyle.Scientific);
        Assert.That(Format(BigNum.FromParts(123, 43), sci), Is.EqualTo("1.23e45"));
        Assert.That(Format(BigNum.FromParts(-123, 43), sci), Is.EqualTo("-1.23e45"));
        Assert.That(Format(BigNum.FromParts(1, 100), sci), Is.EqualTo("1e100"));
    }

    [Test]
    public void Custom_unit_override_is_respected()
    {
        var custom = new BigNumFormat(3, new[] { "", "k", "m" });
        Assert.That(Format((BigNum)1_500, custom), Is.EqualTo("1.5k"));
        Assert.That(Format((BigNum)2_000_000, custom), Is.EqualTo("2m"));
        Assert.That(Format(BigNum.FromParts(3, 9), custom), Is.EqualTo("3000m"));   // above top unit, defaults to TopUnit
    }

    [Test]
    public void MaxUnits_caps_unitization_with_top_unit_growth()
    {
        // Keep top unit, grow integer part, 3-digit group separator
        var fmt = new BigNumFormat(3, new[] { "", "K", "M" }, maxUnits: 2,
            integerGroupSeparator: ',', overflowStyle: BigNumOverflowStyle.TopUnit);
        Assert.That(Format((BigNum)1_500, fmt), Is.EqualTo("1.5K"));
        Assert.That(Format(BigNum.FromParts(1, 10), fmt), Is.EqualTo("10,000M"));      // 1e10
        Assert.That(Format(BigNum.FromParts(123, 7), fmt), Is.EqualTo("1,230M"));      // 1.23e9
        Assert.That(Format((BigNum)123_456_789, fmt), Is.EqualTo("123.45M"));
    }

    [Test]
    public void MaxUnits_lower_than_table_respects_cap()
    {
        // Even with M in the table, maxUnits=1 unitizes only up to K
        var fmt = new BigNumFormat(3, new[] { "", "K", "M", "B" }, maxUnits: 1,
            integerGroupSeparator: ',', overflowStyle: BigNumOverflowStyle.TopUnit);
        Assert.That(Format((BigNum)2_000_000, fmt), Is.EqualTo("2,000K"));
    }

    [Test]
    public void MaxFractionDigits_caps_fraction()
    {
        // Fraction digit cap (trailing zeros always stripped)
        var fmt = new BigNumFormat(3, new[] { "", "K", "M" }, maxFractionDigits: 1);
        Assert.That(Format((BigNum)2_000_000, fmt), Is.EqualTo("2M"));
        Assert.That(Format((BigNum)1_234, fmt), Is.EqualTo("1.2K"));
        Assert.That(Format((BigNum)999, fmt), Is.EqualTo("999"));
        Assert.That(Format(BigNum.FromParts(125, -2), fmt), Is.EqualTo("1.2"));

        var noFraction = new BigNumFormat(3, new[] { "", "K", "M" }, maxFractionDigits: 0);
        Assert.That(Format((BigNum)1_500, noFraction), Is.EqualTo("1K"));
    }

    [Test]
    public void Fixed_fraction_mode_pads_zeros()
    {
        // Fixed fraction digits, trailing zeros kept (stable display width)
        var fmt = new BigNumFormat(3, new[] { "", "K", "M" },
            maxFractionDigits: 1, trimFractionZeros: false);
        Assert.That(Format((BigNum)2_000_000, fmt), Is.EqualTo("2.0M"));
        Assert.That(Format((BigNum)1_234, fmt), Is.EqualTo("1.2K"));
        Assert.That(Format((BigNum)999, fmt), Is.EqualTo("999"));   // plain integers get no fraction
        Assert.That(Format(BigNum.FromParts(125, -2), fmt), Is.EqualTo("1.2"));
    }

    [Test]
    public void Invalid_format_options_throw()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new BigNumFormat(3, new[] { "", "K" }, maxUnits: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            _ = new BigNumFormat(3, new[] { "", "K" }, maxFractionDigits: 10));
    }

    [Test]
    public void Unit_table_cannot_change_after_construction()
    {
        var source = new[] { "", "K" };
        var format = new BigNumFormat(3, source);

        source[1] = "X";
        Assert.That(format.Units[1], Is.EqualTo("K"));
        Assert.That(format.Units, Is.InstanceOf<ReadOnlyCollection<string>>());
        Assert.That(() => ((IList<string>)format.Units)[1] = "X",
            Throws.TypeOf<NotSupportedException>());
        Assert.That(Format((BigNum)1_500, format), Is.EqualTo("1.5K"));
    }

    [Test]
    public void Null_unit_entries_report_argument_errors()
    {
        Assert.That(() => _ = new BigNumFormat(3, null!),
            Throws.TypeOf<ArgumentNullException>());
        Assert.That(() => _ = new BigNumFormat(3, new string[] { null!, "K" }),
            Throws.TypeOf<ArgumentException>());
        Assert.That(() => _ = new BigNumFormat(3, new string[] { "", null! }),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void ToDisplayString_matches_TryFormat()
    {
        // Convenience for low-frequency paths; same output as TryFormat
        Assert.That(((BigNum)3_450_000_000L).ToDisplayString(), Is.EqualTo("3.45B"));
        Assert.That(((BigNum)15_000).ToDisplayString(BigNumFormat.Korean), Is.EqualTo("1.5만"));

        // Over 128 chars (large TopUnit integer part) still uses the growth path
        var big = BigNum.FromParts(1, 200).ToDisplayString();
        Assert.That(big.Length, Is.EqualTo(183 + 2));   // 183 integer digits + "Qi"
        Assert.That(big.EndsWith("Qi"), Is.True);
    }

    [Test]
    public void ToDisplayString_default_budget_rejects_oversized_top_unit_output()
    {
        Assert.That(
            () => BigNum.MaxValue.ToDisplayString(),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void ToDisplayString_explicit_budget_allows_larger_output()
    {
        var value = BigNum.FromParts(1, 300);
        var text = value.ToDisplayString(BigNumFormat.Base, 512);
        Assert.That(text.Length, Is.GreaterThan(256));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ToDisplayString_rejects_nonpositive_budget(int maxLength)
    {
        Assert.That(
            () => BigNum.One.ToDisplayString(BigNumFormat.Base, maxLength),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void ToDisplayString_scientific_formats_extrema_within_default_budget()
    {
        var scientific = new BigNumFormat(
            3,
            new[] { "", "K", "M", "B", "T" },
            overflowStyle: BigNumOverflowStyle.Scientific);
        Assert.That(BigNum.MaxValue.ToDisplayString(scientific).Length, Is.LessThanOrEqualTo(256));
    }

    [Test]
    public void Insufficient_destination_returns_false()
    {
        Span<char> tiny = stackalloc char[2];
        Assert.That(((BigNum)12_345).TryFormat(tiny, out _, null), Is.False);
    }

    [Test]
    public void Plain_fraction_truncates_to_two_digits()
    {
        Assert.That(Format(BigNum.FromParts(523, -4)), Is.EqualTo("0.05"));    // 0.0523 truncated
        Assert.That(Format(BigNum.FromParts(10001, -4)), Is.EqualTo("1"));     // 1.0001, .00 stripped
    }

    [Test]
    public void Tiny_values_fall_back_to_scientific_with_negative_exponent()
    {
        Assert.That(Format(BigNum.FromParts(1, -50)), Is.EqualTo("1e-50"));
        Assert.That(Format(BigNum.FromParts(123, -45)), Is.EqualTo("1.23e-43"));
        Assert.That(Format(BigNum.FromParts(-1, -50)), Is.EqualTo("-1e-50"));
    }
}
