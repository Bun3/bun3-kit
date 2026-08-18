using System;
using System.Collections.Generic;

namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 정의 베이스. 프레임워크가 아는 것은 이 필드들뿐이며, 이름·보상·조건값 등
    /// 정의 콘텐츠는 게임이 파생 클래스에 얹는다. 인스턴스는 카탈로그 생성 후 불변으로
    /// 취급할 것 — 검증은 <see cref="AchievementCatalog{TDef}"/> 생성자가 일괄 수행한다.
    /// </summary>
    public class AchievementDefinition
    {
        /// <summary>업적 식별자. 카탈로그 기동 시 int 인덱스로 인터닝되며, 런타임
        /// 핫패스에는 이 문자열이 등장하지 않는다. 비어 있지 않아야 하고 카탈로그 안에서
        /// 유일해야 한다(ordinal 비교).</summary>
        public string Id { get; }

        /// <summary>달성 목표치 (&gt; 0). 반복 업적은 목표치를 넘길 때마다 달성이 쌓인다.</summary>
        public long Target { get; }

        /// <summary>true면 목표치에 도달할 때마다 다시 달성되는 반복 업적(진행도는 누적,
        /// 수령 시 목표치만큼 차감), false면 1회 달성 후 진행도가 목표치에 클램프된다.</summary>
        public bool Repeatable { get; }

        /// <summary>라우팅·그룹 태그. 카탈로그 기동 시 태그 인덱스로 인터닝되어
        /// <see cref="AchievementManager{TDef}.IncreaseByTag(int, long)"/>와 그룹 순회에
        /// 쓰인다. 게임이 enum으로 관리하고 싶으면 nameof로 채우면 된다(프레임워크는 모름).</summary>
        public IReadOnlyList<string> Tags { get; }

        /// <summary>초기 가용성 — Locked/Ready/Active만 유효(카탈로그가 검증). 기본 Active.
        /// 체인 티어는 Locked로 두고 이전 티어의 완료 훅에서 <see cref="AchievementManager{TDef}.Activate"/>.</summary>
        public AchievementStatus InitialAvailability { get; }

        /// <summary>정의를 생성한다. 인자 검증은 카탈로그 생성 시점에 일괄 수행된다.</summary>
        public AchievementDefinition(
            string id,
            long target,
            bool repeatable = false,
            AchievementStatus initialAvailability = AchievementStatus.Active,
            IReadOnlyList<string>? tags = null)
        {
            Id = id;
            Target = target;
            Repeatable = repeatable;
            InitialAvailability = initialAvailability;
            Tags = tags ?? Array.Empty<string>();
        }
    }
}
