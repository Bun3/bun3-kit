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
/// Shared tag catalog, attribute registry, and minimal-spec helpers for EffectCatalogBuilder /
/// EffectPipeline tests. Static helpers cover catalog and spec skeletons; the instance side
/// (Create/Attacker/Defender/AddSpec/BuildPipeline) serves tests that need pipeline assembly.
/// </summary>
internal sealed class EffectTestKit
{
    // Attribute ids used by the tests.
    public const ushort Hp = 1;
    public const ushort MaxHp = 2;
    public const ushort Mp = 3;
    public const ushort Attack = 5;
    public const ushort Resistance = 6;

    private const string CatalogJson = """
    {
      "schemaVersion": 1,
      "tags": [
        { "name": "calc.magnitude.x" },
        { "name": "calc.magnitude.y" },
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

    /// <summary>Loads a fresh copy of the shared tag catalog.</summary>
    public static TagCatalog LoadCatalog()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(CatalogJson));
        return TagCatalogJson.Load(stream);
    }

    /// <summary>Builds the standard AttributeRegistry — MaxHp (min 1), Hp (0..MaxHp), Attack (min 0), Mp (unbounded).</summary>
    public static AttributeRegistry BuildAttributeRegistry()
    {
        var builder = new AttributeRegistryBuilder();
        builder.Register(MaxHp, min: Operand.Constant(1));
        builder.Register(Hp, min: Operand.Constant(0), max: Operand.Attribute(MaxHp));
        builder.Register(Attack, min: Operand.Constant(0));
        builder.Register(Mp);
        builder.Register(Resistance);
        return builder.Build();
    }

    /// <summary>Creates a minimal Instant spec with all fields at defaults.</summary>
    public static EffectSpec MinimalInstant(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Instant,
    };

    /// <summary>Creates a minimal Duration spec lasting the given number of ticks.</summary>
    public static EffectSpec MinimalDuration(string name, int ticks) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Duration,
        DurationTicks = ticks,
    };

    /// <summary>Creates a minimal Infinite spec with all fields at defaults.</summary>
    public static EffectSpec MinimalInfinite(string name) => new EffectSpec
    {
        Name = name,
        DurationType = EffectDurationType.Infinite,
    };

    /// <summary>Creates a chain edge with only the target effect name set.</summary>
    public static ChainEdgeDef Edge(ChainTrigger trigger, string effectName) => new ChainEdgeDef
    {
        Trigger = trigger,
        EffectName = effectName,
    };

    /// <summary>Builds with the shared catalog, an empty SeamRegistry, and the standard AttributeRegistry.</summary>
    public static EffectCatalog BuildCatalog(EffectCatalogBuilder builder)
    {
        var catalog = LoadCatalog();
        var seams = new SeamRegistryBuilder().Build(catalog);
        var attributes = BuildAttributeRegistry();
        return builder.Build(catalog, seams, attributes);
    }

    // ---- Instance side: for tests that assemble an EffectPipeline ----

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

    /// <summary>The attacker target.</summary>
    public EffectTarget Attacker { get; }

    /// <summary>The defender target.</summary>
    public EffectTarget Defender { get; }

    /// <summary>All registered targets, in registration order — Attacker, Defender.</summary>
    public IReadOnlyList<EffectTarget> AllTargets => _targetIds.ConvertAll(id => _targetsById[id.Value]);

    /// <summary>Creates a kit wired with the catalog, the standard attribute registry, and the attacker/defender targets.</summary>
    public static EffectTestKit Create() => new EffectTestKit();

    private EffectTarget CreateTarget(ulong id)
    {
        Span<ushort> ids = stackalloc ushort[] { Hp, MaxHp, Mp, Attack, Resistance };
        var target = new EffectTarget(new TargetId(id), _attributeRegistry, ids, _tagCatalog);
        _targetsById.Add(id, target);
        _targetIds.Add(target.Id);
        return target;
    }

    /// <summary>Registers an effect spec with the catalog builder. Call before BuildPipeline.</summary>
    public void AddSpec(EffectSpec spec) => _effectCatalogBuilder.Add(spec);

    /// <summary>Registers an execution calc under a tag. Call before BuildPipeline.</summary>
    public void RegisterExecutionCalc(string calcTag, IExecutionCalc calc) =>
        _seamRegistryBuilder.RegisterExecutionCalc(_tagCatalog.GetRequired(calcTag), calc);

    /// <summary>Registers a magnitude calc under a tag. Call before BuildPipeline.</summary>
    public void RegisterMagnitudeCalc(string calcTag, IMagnitudeCalc calc) =>
        _seamRegistryBuilder.RegisterMagnitudeCalc(_tagCatalog.GetRequired(calcTag), calc);

    /// <summary>Registers a target selector under a tag. <see cref="AllTargetsSelector"/> binds to the
    /// target list as of registration time. Call before BuildPipeline.</summary>
    public void RegisterSelector(string selectorTag, ITargetSelector selector)
    {
        if (selector is AllTargetsSelector allTargets) allTargets.Bind(_targetIds);
        _seamRegistryBuilder.RegisterTargetSelector(_tagCatalog.GetRequired(selectorTag), selector);
    }

    /// <summary>Looks up a tag by path in the shared catalog.</summary>
    public GameplayTag Tag(string path) => _tagCatalog.GetRequired(path);

    /// <summary>The shared tag catalog.</summary>
    public TagCatalog TagCatalog => _tagCatalog;

    /// <summary>Gets the catalog id of a registered effect name. Call after BuildPipeline.</summary>
    public int SpecId(string name) => BuiltCatalog.GetRequiredId(name);

    /// <summary>The built effect catalog (for calls needing it, e.g. RestoreSnapshot). Call after BuildPipeline.</summary>
    public EffectCatalog Catalog => BuiltCatalog;

    private EffectCatalog BuiltCatalog =>
        _catalog ??= _effectCatalogBuilder.Build(_tagCatalog, _seamRegistryBuilder.Build(_tagCatalog), _attributeRegistry);

    /// <summary>Freezes the catalog with everything registered so far and creates a pipeline.</summary>
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

    /// <summary>Test execution calc that subtracts Input(0) from the target's Hp.</summary>
    public sealed class SubtractHpCalc : IExecutionCalc
    {
        public void Execute(ref ExecutionContext ctx) => ctx.WriteTarget(Hp, ctx.TargetAttr(Hp) - ctx.Input(0));
    }

    /// <summary>Test selector returning every target registered with the kit. <see cref="RegisterSelector"/>
    /// binds it to the target list as of registration time.</summary>
    public sealed class AllTargetsSelector : ITargetSelector
    {
        private List<TargetId>? _allTargetIds;

        internal void Bind(List<TargetId> targetIds) => _allTargetIds = targetIds;

        public int Select(in SelectorContext ctx, Span<TargetId> results)
        {
            var ids = _allTargetIds ?? throw new InvalidOperationException("Bind via RegisterSelector first.");
            var count = Math.Min(ids.Count, results.Length);
            for (var i = 0; i < count; i++) results[i] = ids[i];
            return count;
        }
    }
}
