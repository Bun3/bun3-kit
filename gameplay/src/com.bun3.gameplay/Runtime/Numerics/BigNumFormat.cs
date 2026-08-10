using System;

namespace Bun3.Gameplay.Numerics
{
    /// <summary>
    /// BigNum 표시 포맷 설정 — 단위 그룹 자릿수와 단위 문자 테이블. 게임은 자체 테이블로
    /// 인스턴스를 만들어 오버라이드한다(스펙 §6). 테이블을 넘는 값은 지수 표기로 폴백.
    /// </summary>
    public sealed class BigNumFormat
    {
        /// <summary>단위 하나가 감당하는 십진 자릿수(한국식 4, 알파벳식 3).</summary>
        public int GroupDigits { get; }

        /// <summary>단위 문자 테이블. [0]은 단위 없음("")이어야 한다.</summary>
        public string[] Units { get; }

        /// <summary>알파벳 축약(1K = 10^3): "", K, M, B, T, Qa, Qi.</summary>
        public static readonly BigNumFormat Alpha =
            new BigNumFormat(3, new[] { "", "K", "M", "B", "T", "Qa", "Qi" });

        /// <summary>한국식(1만 = 10^4): "", 만, 억, 조, 경, 해, 자, 양, 구, 간, 정, 재, 극.</summary>
        public static readonly BigNumFormat Korean =
            new BigNumFormat(4, new[] { "", "만", "억", "조", "경", "해", "자", "양", "구", "간", "정", "재", "극" });

        /// <summary>그룹 자릿수(1~9)와 단위 테이블로 포맷을 만든다.</summary>
        public BigNumFormat(int groupDigits, string[] units)
        {
            if (groupDigits < 1 || groupDigits > 9)
            {
                throw new ArgumentOutOfRangeException(nameof(groupDigits));
            }

            if (units == null || units.Length == 0 || units[0].Length != 0)
            {
                throw new ArgumentException("Units[0]은 빈 문자열이어야 한다.", nameof(units));
            }

            GroupDigits = groupDigits;
            Units = units;
        }
    }
}
