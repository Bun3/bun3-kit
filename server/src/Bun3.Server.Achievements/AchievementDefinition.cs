namespace Bun3.Server.Achievements
{
    /// <summary>
    /// 업적 정의 베이스. 프레임워크가 아는 것은 이 세 필드뿐이며, 이름·보상·조건값 등
    /// 정의 콘텐츠는 게임이 파생 클래스에 얹는다. 인스턴스는 카탈로그 생성 후 불변으로
    /// 취급할 것 — 검증은 <see cref="AchievementCatalog{TDef}"/> 생성자가 일괄 수행한다.
    /// </summary>
    public class AchievementDefinition
    {
        /// <summary>업적 식별자. 카탈로그 기동 시 int 인덱스로 인터닝되며, 런타임
        /// 핫패스에는 이 문자열이 등장하지 않는다. 비어 있지 않아야 하고 카탈로그 안에서
        /// 유일해야 한다(ordinal 비교).</summary>
        public string Id { get; }

        /// <summary>달성 목표치 (&gt; 0). 반복 업적은 진행도/목표치 몫이 달성 횟수가 된다.</summary>
        public long Target { get; }

        /// <summary>true면 목표치에 도달할 때마다 다시 달성되는 반복 업적(진행도는 무한
        /// 누적), false면 1회 달성 후 진행도가 목표치에 클램프된다.</summary>
        public bool Repeatable { get; }

        /// <summary>정의를 생성한다. 인자 검증은 카탈로그 생성 시점에 일괄 수행된다.</summary>
        public AchievementDefinition(string id, long target, bool repeatable = false)
        {
            Id = id;
            Target = target;
            Repeatable = repeatable;
        }
    }
}
