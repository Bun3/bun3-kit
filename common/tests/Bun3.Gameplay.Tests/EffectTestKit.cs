using System.IO;
using System.Text;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Tests;

/// <summary>
/// EffectCatalogBuilder 계열 테스트가 공유하는 태그 카탈로그·속성 레지스트리·최소 스펙 헬퍼입니다.
/// 이후 태스크(파이프라인·타겟 등)가 같은 헬퍼를 재사용할 수 있도록 정적으로 유지한다.
/// </summary>
internal static class EffectTestKit
{
    // 테스트에서 쓰는 속성 id들.
    public const ushort Hp = 1;
    public const ushort MaxHp = 2;
    public const ushort Mp = 3;
    public const ushort Attack = 5;

    private const string CatalogJson = """
    {
      "schemaVersion": 1,
      "tags": [
        { "name": "calc.magnitude.x" },
        { "name": "calc.execution.dmg" },
        { "name": "selector.team" },
        { "name": "selector.everyone" },
        { "name": "state.dead" },
        { "name": "state.hasted" },
        { "name": "state.frozen" },
        { "name": "state.chilled" },
        { "name": "state.lowhealth" },
        { "name": "effect.fire.bolt" },
        { "name": "effect.frost" },
        { "name": "effect.magic.curse" }
      ]
    }
    """;

    /// <summary>공유 태그 카탈로그를 새로 로드합니다.</summary>
    public static TagCatalog LoadCatalog()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson));
        return TagCatalogJson.Load(stream);
    }

    /// <summary>표준 AttributeRegistry — MaxHp(min 1), Hp(0..MaxHp), Attack(min 0), Mp(무제한)를 만듭니다.</summary>
    public static AttributeRegistry BuildAttributeRegistry()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, min: Operand.Constant(1));
        builder.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(Attack, min: Operand.Constant(0));
        builder.Register(Mp);
        return builder.Build();
    }

    /// <summary>모든 필드가 기본값인 최소 Instant 스펙을 만듭니다.</summary>
    public static EffectSpec MinimalInstant(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Instant,
    };

    /// <summary>지정한 틱 수로 지속되는 최소 Duration 스펙을 만듭니다.</summary>
    public static EffectSpec MinimalDuration(string name, int ticks) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Duration,
        DurationTicks = ticks,
    };

    /// <summary>모든 필드가 기본값인 최소 Infinite 스펙을 만듭니다.</summary>
    public static EffectSpec MinimalInfinite(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Infinite,
    };

    /// <summary>대상 효과 이름만 채운 체인 엣지를 만듭니다.</summary>
    public static ChainEdgeDef Edge(ChainTrigger trigger, string effectName) => new ChainEdgeDef
    {
        Trigger = trigger,
        EffectName = effectName,
    };

    /// <summary>공유 카탈로그 + 빈 SeamRegistry + 표준 AttributeRegistry로 빌드합니다.</summary>
    public static EffectCatalog BuildCatalog(EffectCatalogBuilder builder)
    {
        var catalog = LoadCatalog();
        var seams = new SeamRegistryBuilder().Build(catalog);
        var attributes = BuildAttributeRegistry();
        return builder.Build(catalog, seams, attributes);
    }
}
