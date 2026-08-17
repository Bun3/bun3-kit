using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;
using Bun3.Gameplay.Tags.Catalog;

namespace Bun3.Gameplay.Tests;

/// <summary>
/// EffectCatalogBuilder·EffectPipeline 계열 테스트가 공유하는 태그 카탈로그·속성 레지스트리·
/// 최소 스펙 헬퍼입니다. 정적 헬퍼(카탈로그·스펙 뼈대)는 그대로 두고, 파이프라인 조립이 필요한
/// 테스트를 위해 인스턴스 쪽(Create/Attacker/Defender/AddSpec/BuildPipeline)을 추가로 제공한다.
/// </summary>
internal sealed class EffectTestKit
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

    // ---- 인스턴스 쪽: EffectPipeline 조립이 필요한 테스트용 ----

    private readonly TagCatalog _tagCatalog;
    private readonly AttributeRegistry _attributeRegistry;
    private readonly SeamRegistryBuilder _seamRegistryBuilder = new SeamRegistryBuilder();
    private readonly EffectCatalogBuilder _effectCatalogBuilder = new EffectCatalogBuilder();
    private readonly Dictionary<ulong, EffectTarget> _targetsById = new Dictionary<ulong, EffectTarget>();
    private readonly List<TargetId> _targetIds = new List<TargetId>();
    private EffectCatalog? _catalog;

    private EffectTestKit()
    {
        _tagCatalog = LoadCatalog();
        _attributeRegistry = BuildAttributeRegistry();
        Attacker = CreateTarget(1);
        Defender = CreateTarget(2);
    }

    /// <summary>공격자 대상입니다.</summary>
    public EffectTarget Attacker { get; }

    /// <summary>방어자 대상입니다.</summary>
    public EffectTarget Defender { get; }

    /// <summary>등록된 모든 대상입니다(등록 순서 — Attacker, Defender).</summary>
    public IReadOnlyList<EffectTarget> AllTargets => _targetIds.ConvertAll(id => _targetsById[id.Value]);

    /// <summary>카탈로그·표준 속성 레지스트리·공격자/방어자 두 대상을 조립한 키트를 만듭니다.</summary>
    public static EffectTestKit Create() => new EffectTestKit();

    private EffectTarget CreateTarget(ulong id)
    {
        Span<ushort> ids = stackalloc ushort[] { Hp, MaxHp, Mp, Attack };
        var target = new EffectTarget(new TargetId(id), _attributeRegistry, ids, _tagCatalog);
        _targetsById.Add(id, target);
        _targetIds.Add(target.Id);
        return target;
    }

    /// <summary>효과 스펙을 카탈로그 빌더에 등록합니다. BuildPipeline 전에 호출해야 합니다.</summary>
    public void AddSpec(EffectSpec spec) => _effectCatalogBuilder.Add(spec);

    /// <summary>효과 실행 계산을 태그에 등록합니다. BuildPipeline 전에 호출해야 합니다.</summary>
    public void RegisterExecutionCalc(string calcTag, IExecutionCalc calc) =>
        _seamRegistryBuilder.RegisterExecutionCalc(_tagCatalog.GetRequired(calcTag), calc);

    /// <summary>대상 선택 계약을 태그에 등록합니다. <see cref="AllTargetsSelector"/>는 등록 시점의
    /// 대상 목록에 바인딩됩니다. BuildPipeline 전에 호출해야 합니다.</summary>
    public void RegisterSelector(string selectorTag, ITargetSelector selector)
    {
        if (selector is AllTargetsSelector allTargets) allTargets.Bind(_targetIds);
        _seamRegistryBuilder.RegisterTargetSelector(_tagCatalog.GetRequired(selectorTag), selector);
    }

    /// <summary>공유 카탈로그에서 경로로 태그를 찾습니다.</summary>
    public GameplayTag Tag(string path) => _tagCatalog.GetRequired(path);

    /// <summary>등록된 효과 이름의 카탈로그 id를 가져옵니다. BuildPipeline 이후에 호출해야 합니다.</summary>
    public int SpecId(string name) => BuiltCatalog.GetRequiredId(name);

    private EffectCatalog BuiltCatalog =>
        _catalog ??= _effectCatalogBuilder.Build(_tagCatalog, _seamRegistryBuilder.Build(_tagCatalog), _attributeRegistry);

    /// <summary>지금까지 등록된 스펙·계산으로 카탈로그를 굳히고 파이프라인을 만듭니다.</summary>
    public EffectPipeline BuildPipeline(int applyBudgetPerTick = 64) =>
        new EffectPipeline(
            BuiltCatalog, new DictionaryResolver(_targetsById, _targetIds), new XorShiftRng(12345), applyBudgetPerTick);

    private sealed class DictionaryResolver : IEffectTargetResolver
    {
        private readonly Dictionary<ulong, EffectTarget> _targets;
        private readonly List<TargetId> _ids;

        internal DictionaryResolver(Dictionary<ulong, EffectTarget> targets, List<TargetId> ids)
        {
            _targets = targets;
            _ids = ids;
        }

        public bool TryResolve(TargetId id, out EffectTarget? target) => _targets.TryGetValue(id.Value, out target);

        public IReadOnlyList<TargetId> TargetIds => _ids;
    }

    /// <summary>Input(0)만큼 대상의 Hp를 깎는 테스트용 실행 계산입니다.</summary>
    public sealed class SubtractHpCalc : IExecutionCalc
    {
        public void Execute(ref ExecutionContext ctx) => ctx.WriteTarget(Hp, ctx.TargetAttr(Hp) - ctx.Input(0));
    }

    /// <summary>키트에 등록된 모든 대상을 반환하는 테스트용 선택자입니다. <see cref="RegisterSelector"/>가
    /// 등록 시점의 대상 목록에 바인딩합니다.</summary>
    public sealed class AllTargetsSelector : ITargetSelector
    {
        private List<TargetId>? _allTargetIds;

        internal void Bind(List<TargetId> targetIds) => _allTargetIds = targetIds;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var ids = _allTargetIds ?? throw new InvalidOperationException("RegisterSelector로 먼저 바인딩해야 합니다.");
            var count = Math.Min(ids.Count, results.Length);
            for (var i = 0; i < count; i++) results[i] = ids[i];
            return count;
        }
    }
}
