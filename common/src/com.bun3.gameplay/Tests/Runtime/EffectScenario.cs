#nullable enable
using System;
using System.Collections.Generic;
using Bun3.Gameplay.Attributes;
using Bun3.Gameplay.Effects;
using Bun3.Gameplay.Numerics;
using Bun3.Gameplay.Seams;
using Bun3.Gameplay.Tags;

namespace Bun3.Gameplay.Tests
{
    /// <summary>
    /// 결정론 오라클 — 태그 없는 미니 우주(속성·스택·주기·체인·조건만)를 코드로 조립해 고정 시드로
    /// 적용/디스펠/틱을 뒤섞어 돌린 뒤 최종 상태를 FNV-1a 64비트 해시로 접는 순수 함수입니다.
    /// Unity Player 어셈블리(Bun3.Gameplay.Runtime.Tests)와 .NET 테스트 어셈블리 양쪽에 링크되므로
    /// 저작 전용 로더(TagCatalogJson 등)나 NUnit·EffectTestKit을 참조하지 않고 프레임워크
    /// 런타임 타입만 사용합니다.
    /// </summary>
    internal static class EffectScenario
    {
        private const ushort Hp = 1;
        private const ushort MaxHp = 2;
        private const ushort Attack = 3;

        private const int TargetCount = 3;

        /// <summary>고정 시드로 미니 우주를 조립해 ticks회 실행한 뒤 최종 상태의 FNV-1a 64비트 해시를 반환합니다.</summary>
        /// <param name="seed">파이프라인 난수·시나리오 드라이버 난수의 시드입니다.</param>
        /// <param name="ticks">진행할 파이프라인 틱 수입니다.</param>
        internal static ulong Run(int seed, int ticks)
        {
            var world = BuildWorld(seed);
            RunTicks(world, ticks);
            return HashState(world.Targets);
        }

        /// <summary>월드를 조립만 하고 진행하지 않습니다 — 스냅샷/복원 왕복 테스트가 중간에 개입할 수 있도록 노출합니다.</summary>
        internal static World BuildWorld(int seed)
        {
            var tagCatalog = TagCatalog.Create(new List<string>(), new List<TagCatalog.RedirectDefinition>());

            var registryBuilder = new AttributeRegistryBuilder();
            registryBuilder.Register(MaxHp, min: Operand.Constant(BigNum.FromParts(1, 0)));
            registryBuilder.Register(Hp, min: Operand.Constant(BigNum.Zero), max: Operand.Attribute(MaxHp));
            registryBuilder.Register(Attack, min: Operand.Constant(BigNum.Zero));
            var registry = registryBuilder.Build();

            var catalogBuilder = new EffectCatalogBuilder();
            AddSpecs(catalogBuilder);
            var seams = new SeamRegistryBuilder().Build(tagCatalog);
            var catalog = catalogBuilder.Build(tagCatalog, seams, registry);

            var targets = new EffectTarget[TargetCount];
            var targetIds = new List<TargetId>(TargetCount);
            Span<ushort> attributeIds = stackalloc ushort[] { Hp, MaxHp, Attack };
            for (var i = 0; i < TargetCount; i++)
            {
                var target = new EffectTarget(new TargetId((ulong)(i + 1)), registry, attributeIds, tagCatalog);
                target.Attributes.SetBase(MaxHp, BigNum.FromParts(1000, 0));
                target.Attributes.SetBase(Hp, BigNum.FromParts(1000, 0));
                target.Attributes.SetBase(Attack, BigNum.FromParts(50, 0));
                targets[i] = target;
                targetIds.Add(target.Id);
            }

            var resolver = new DictionaryResolver(targets, targetIds);
            var pipelineRngSeed = NonZero(unchecked((ulong)seed) + 1);
            var pipeline = new EffectPipeline(catalog, resolver, new XorShiftRng(pipelineRngSeed));
            var driverRng = new XorShiftRng(NonZero(unchecked((ulong)seed)));

            return new World(catalog, resolver, targets, pipeline, driverRng);
        }

        /// <summary>월드를 ticks회 더 진행합니다. 매 틱 랜덤 적용/디스펠 액션 하나를 섞은 뒤 Tick()을 부르고,
        /// 이벤트/변경 버퍼를 비웁니다.</summary>
        internal static void RunTicks(World world, int ticks)
        {
            for (var t = 0; t < ticks; t++)
            {
                DriveOneTick(world);
                world.Pipeline.Tick();

                for (var i = 0; i < world.Targets.Length; i++)
                {
                    world.Targets[i].Attributes.ClearChanges();
                    world.Targets[i].ClearEffectEvents();
                }
            }
        }

        /// <summary>대상들의 최종 상태(속성 Base/Current, 활성 인스턴스 필드)를 FNV-1a 64비트로 접습니다.</summary>
        internal static ulong HashState(EffectTarget[] targets)
        {
            var hash = FnvOffset;
            for (var i = 0; i < targets.Length; i++)
            {
                var target = targets[i];
                hash = FoldAttribute(hash, target, Hp);
                hash = FoldAttribute(hash, target, MaxHp);
                hash = FoldAttribute(hash, target, Attack);

                var active = target.ActiveEffects;
                for (var k = 0; k < active.Count; k++)
                {
                    var instance = active[k];
                    hash = Fold(hash, instance.SpecId);
                    hash = Fold(hash, instance.RemainingTicks);
                    hash = Fold(hash, instance.PeriodCountdown);
                    hash = Fold(hash, instance.Stack);
                    hash = Fold(hash, instance.Level);
                    hash = Fold(hash, instance.Enabled ? 1 : 0);
                }
            }

            return hash;
        }

        private static void DriveOneTick(World world)
        {
            var roll = world.DriverRng.NextUInt32() % 4;
            if (roll == 0)
            {
                var specId = world.DriverRng.NextUInt32() % (uint)world.Catalog.Count;
                var source = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var target = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var level = 1 + (int)(world.DriverRng.NextUInt32() % 3);
                world.Pipeline.EnqueueApply((int)specId, source.Id, target.Id, level);
            }
            else if (roll == 1)
            {
                var target = world.Targets[world.DriverRng.NextUInt32() % TargetCount];
                var active = target.ActiveEffects;
                if (active.Count > 0)
                {
                    var index = (int)(world.DriverRng.NextUInt32() % (uint)active.Count);
                    world.Pipeline.RemoveById(target.Id, active[index].Id);
                }
            }
            // roll 2·3: 이번 틱은 적용/디스펠 없이 그대로 진행 — 정착 구간을 섞어 넣는다.
        }

        private static ulong FoldAttribute(ulong hash, EffectTarget target, ushort attributeId)
        {
            var b = target.Attributes.GetBase(attributeId);
            var c = target.Attributes.GetCurrent(attributeId);
            hash = Fold(hash, b.Mantissa);
            hash = Fold(hash, b.Exponent);
            hash = Fold(hash, c.Mantissa);
            hash = Fold(hash, c.Exponent);
            return hash;
        }

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static ulong Fold(ulong hash, long value)
        {
            var v = unchecked((ulong)value);
            for (var i = 0; i < 8; i++)
            {
                hash ^= v & 0xFF;
                hash *= FnvPrime;
                v >>= 8;
            }

            return hash;
        }

        private static ulong NonZero(ulong value) => value == 0 ? 1UL : value;

        // 6종(체인은 트리거+추종 두 스펙) — Instant 데미지, Duration 버프×스택, 독(Duration+Period),
        // 만감 체인(적용 시 추종 효과 발동), Ongoing 조건(저체력일 때만 공격력 보너스), 체인 폭탄
        // (지속시간 만료 시 폭발 데미지). 전부 태그 없음 — GrantedTags/AssetTags/ImmunityTags 빈 리스트.
        private static void AddSpecs(EffectCatalogBuilder builder)
        {
            builder.Add(new EffectSpec
            {
                Name = "InstantDamage",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-30)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Buff",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 20,
                Stack = new StackPolicy
                {
                    MaxStack = 5,
                    OnReapply = StackReapply.AddStack,
                    AddStackCount = 1,
                    RefreshDurationOnReapply = true,
                    OnExpiration = StackExpiration.ClearAll,
                    OnOverflow = StackOverflow.Deny,
                },
                Modifiers = { AddModifier(Attack, Constant(5)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Poison",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 30,
                PeriodTicks = 5,
                Modifiers = { AddModifier(Hp, Constant(-3)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "ChainTrigger",
                DurationType = EffectDurationType.Instant,
                Chains =
                {
                    new ChainEdgeDef
                    {
                        Trigger = ChainTrigger.OnApplication,
                        EffectName = "ChainFollow",
                        LevelRule = ChainLevelRule.Inherit,
                    },
                },
            });

            builder.Add(new EffectSpec
            {
                Name = "ChainFollow",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-5)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "OngoingBuff",
                DurationType = EffectDurationType.Infinite,
                OngoingConditions =
                {
                    new ConditionDef
                    {
                        Left = Operand.Attribute(Hp),
                        Op = ComparisonOp.Less,
                        Right = Operand.Attribute(MaxHp, BigNum.FromParts(5, -1)),
                    },
                },
                Modifiers = { AddModifier(Attack, Constant(10)) },
            });

            builder.Add(new EffectSpec
            {
                Name = "Bomb",
                DurationType = EffectDurationType.Duration,
                DurationTicks = 10,
                Chains =
                {
                    new ChainEdgeDef
                    {
                        Trigger = ChainTrigger.OnCompleteNormal,
                        EffectName = "BombBlast",
                        LevelRule = ChainLevelRule.Inherit,
                    },
                },
            });

            builder.Add(new EffectSpec
            {
                Name = "BombBlast",
                DurationType = EffectDurationType.Instant,
                Modifiers = { AddModifier(Hp, Constant(-20)) },
            });
        }

        private static Operand Constant(long value) => Operand.Constant(BigNum.FromParts(value, 0));

        private static ModifierDef AddModifier(ushort attributeId, Operand baseValue) => new ModifierDef
        {
            AttributeId = attributeId,
            Op = AttributeModifierOp.Add,
            Magnitude = new MagnitudeDef { Base = baseValue },
        };

        private sealed class DictionaryResolver : IEffectTargetResolver
        {
            private readonly EffectTarget[] _targets;
            private readonly List<TargetId> _ids;

            internal DictionaryResolver(EffectTarget[] targets, List<TargetId> ids)
            {
                _targets = targets;
                _ids = ids;
            }

            public bool TryResolve(TargetId id, out EffectTarget? target)
            {
                for (var i = 0; i < _targets.Length; i++)
                {
                    if (_targets[i].Id == id)
                    {
                        target = _targets[i];
                        return true;
                    }
                }

                target = null;
                return false;
            }

            public IReadOnlyList<TargetId> TargetIds => _ids;
        }

        /// <summary>조립된 미니 우주 — 스냅샷/복원 테스트가 중간 지점에 개입할 수 있도록 값들을 노출합니다.
        /// <see cref="DriverRng"/>는 XorShiftRng(클래스, 참조 공유)를 필드로 노출합니다 — 특정 시점의
        /// 스트림 상태를 독립적으로 보존하려면 호출자가 <see cref="XorShiftRng.Clone"/>을 명시적으로 써야 합니다.
        /// <see cref="Pipeline"/>은 스냅샷 복원 재생 테스트가 큐 잔여물 없는 새 파이프라인으로
        /// 교체할 수 있도록 세터를 엽니다(대기 큐는 스냅샷 범위 밖이라 새 인스턴스로 비운다).</summary>
        internal sealed class World
        {
            internal World(
                EffectCatalog catalog, IEffectTargetResolver resolver, EffectTarget[] targets,
                EffectPipeline pipeline, XorShiftRng driverRng)
            {
                Catalog = catalog;
                Resolver = resolver;
                Targets = targets;
                Pipeline = pipeline;
                DriverRng = driverRng;
            }

            internal EffectCatalog Catalog { get; }
            internal IEffectTargetResolver Resolver { get; }
            internal EffectTarget[] Targets { get; }
            internal EffectPipeline Pipeline { get; set; }
            internal XorShiftRng DriverRng;
        }
    }
}
