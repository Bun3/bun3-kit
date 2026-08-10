using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>단위 테이블 상한(MaxUnits)을 넘어선 값의 표기 방식.</summary>
    public enum BigNumOverflowStyle
    {
        /// <summary>지수 표기로 폴백한다 ("1.23e45"). 폭이 제한된 HUD 표시에 적합.</summary>
        Scientific,

        /// <summary>최상위 허용 단위를 유지하고 정수부가 자란다 ("12,345M")</summary>
        TopUnit,
    }

    /// <summary>
    /// BigNum 표시 포맷 설정 — 몇 자리마다(<see cref="GroupDigits"/>), 무엇으로
    /// (<see cref="Units"/>), 최대 몇 번(<see cref="MaxUnits"/>) 유닛화할지를 게임이
    /// 정한다(idlez ToUnitString의 일반화). 소수 자릿수, 정수부 구분자, 상한 초과
    /// 표기 방식도 설정 가능.
    /// </summary>
    public sealed class BigNumFormat
    {
        /// <summary>단위 하나가 감당하는 십진 자릿수(한국식 4, 알파벳식 3).</summary>
        public int GroupDigits { get; }

        /// <summary>단위 문자 테이블. [0]은 단위 없음("")이어야 한다.</summary>
        public string[] Units { get; }

        /// <summary>유닛화 최대 횟수(사용할 최상위 단위의 인덱스). Units.Length - 1 이하.</summary>
        public int MaxUnits { get; }

        /// <summary>소수부 최대 자릿수(0~9). 절사 후 트레일링 0은 항상 제거된다("1.5K", "2M").</summary>
        public int MaxFractionDigits { get; }

        /// <summary>정수부 3자리 구분자(예: ','). null이면 구분 없음.</summary>
        public char? IntegerGroupSeparator { get; }

        /// <summary>단위 상한을 넘어선 값의 표기 방식.</summary>
        public BigNumOverflowStyle OverflowStyle { get; }

        /// <summary>알파벳 축약(1K = 10^3): "", K, M, B, T, Qa, Qi. 상한 초과는 지수 표기.</summary>
        public static readonly BigNumFormat Base =
            new BigNumFormat(3, new[] { "", "K", "M", "B", "T", "Qa", "Qi" });

        /// <summary>한국식(1만 = 10^4): "", 만, 억, 조, 경, 해, 자, 양, 구, 간, 정, 재, 극. 상한 초과는 지수 표기.</summary>
        public static readonly BigNumFormat Korean =
            new BigNumFormat(4, new[] { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" });

        /// <summary>포맷을 만든다. maxUnits가 음수면 단위 테이블 전체(Units.Length - 1)를 쓴다.</summary>
        public BigNumFormat(
            int groupDigits,
            string[] units,
            int maxUnits = -1,
            int maxFractionDigits = 2,
            char? integerGroupSeparator = null,
            BigNumOverflowStyle overflowStyle = BigNumOverflowStyle.TopUnit)
        {
            if (groupDigits < 1 || groupDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(groupDigits));
            }

            if (units == null || units.Length == 0 || units[0].Length != 0)
            {
                throw new ArgumentException("Units[0]은 빈 문자열이어야 한다.", nameof(units));
            }

            for (var i = 1; i < units.Length; i++)
            {
                if (units[i] == null)
                {
                    throw new ArgumentException("Units에 null 원소가 있다.", nameof(units));
                }
            }

            if (maxUnits >= units.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(maxUnits), "MaxUnits는 Units.Length - 1 이하여야 한다.");
            }

            if (maxFractionDigits < 0 || maxFractionDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(maxFractionDigits));
            }

            GroupDigits = groupDigits;
            Units = (string[])units.Clone();   // 외부 변이 차단
            MaxUnits = maxUnits < 0 ? units.Length - 1 : maxUnits;
            MaxFractionDigits = maxFractionDigits;
            IntegerGroupSeparator = integerGroupSeparator;
            OverflowStyle = overflowStyle;
        }
    }
}
