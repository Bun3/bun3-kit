using System;
using System.Collections.Generic;
using Bun3.Gameplay.Numerics;

namespace Bun3.Server.Items
{
    // 지급·소모·트랜잭션 — 모든 변경 API가 단일 커밋 코어(CommitOps)를 지난다.
    public sealed partial class ItemInventory<TState>
    {
        private static readonly BigNum MaxInstancesPerOperationQuantity = MaxInstancesPerOperation;

        internal enum TxOpKind : byte
        {
            Add,
            RemoveByItem,
            RemoveInstanceAll,
            RemoveInstanceAmount,
        }

        internal readonly struct TxOp
        {
            internal TxOp(TxOpKind kind, ItemId item, long instanceId, BigNum amount)
            {
                Kind = kind;
                Item = item;
                InstanceId = instanceId;
                Amount = amount;
            }

            internal TxOpKind Kind { get; }
            internal ItemId Item { get; }
            internal long InstanceId { get; }
            internal BigNum Amount { get; }
        }

        /// <summary>
        /// 트랜잭션 빌더를 시작한다 — 정의 단위와 인스턴스 지정 연산을 섞은 원자 배치.
        /// 동시 배치는 1개: 새 Begin은 이전 미커밋 배치를 버리고, 낡은 빌더 사용은 던진다.
        /// </summary>
        public InventoryTransaction<TState> BeginTransaction()
        {
            _txOps.Clear();
            _txToken++;
            return new InventoryTransaction<TState>(this, _txToken);
        }

        internal void TxRecord(int token, TxOp op)
        {
            if (token != _txToken)
            {
                throw new InvalidOperationException("낡은 트랜잭션 빌더입니다 — BeginTransaction 이후의 빌더만 유효합니다.");
            }

            _txOps.Add(op);
        }

        internal InventoryError TxCommit(int token, out int failedIndex, List<ItemInstance<TState>>? created)
        {
            if (token != _txToken)
            {
                throw new InvalidOperationException("낡은 트랜잭션 빌더입니다 — BeginTransaction 이후의 빌더만 유효합니다.");
            }

            _txToken++;   // 커밋 후 빌더 재사용 차단
            var error = CommitOps(_txOps, out failedIndex, created);
            _txOps.Clear();
            return error;
        }

        /// <summary>수량을 지급한다. amount는 양수여야 한다. 비스택형은 amount개(정수·상한
        /// <see cref="MaxInstancesPerOperation"/>) 인스턴스가 생성되며 <paramref name="created"/>에
        /// 담긴다(스택 싱글턴 신규 생성 포함).</summary>
        public InventoryError TryAdd(ItemId item, BigNum amount, List<ItemInstance<TState>>? created = null)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, amount));
            return CommitOps(_applyOps, out _, created);
        }

        /// <summary>
        /// 가능한 만큼만 지급한다(클램프) — 상한 도달은 실패가 아니라 부분 지급이라는
        /// 레거시 의미론(idlez 초과분 버림·growninja 우편 폴백). <paramref name="granted"/>가
        /// 실제 지급량(0 포함 — 0이면 변경·통지 없음)이며, 잔여분 처리(우편 등)는 게임 몫.
        /// 오류는 UnknownItem·InvalidAmount(0 이하·비스택형 비정수)뿐이다.
        /// 스택형 BigNum 손실 의미론상 보고 지급량과 잔량 변화가 다를 수 있다(거대 스택에 미소량 흡수).
        /// </summary>
        public InventoryError TryAddUpTo(
            ItemId item,
            BigNum amount,
            out BigNum granted,
            List<ItemInstance<TState>>? created = null)
        {
            granted = BigNum.Zero;
            if (!_catalog.Contains(item))
            {
                return InventoryError.UnknownItem;
            }

            if (amount.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            var clamp = amount;
            if (_catalog.IsUnstackable(item))
            {
                if (amount.Exponent < 0)
                {
                    return InventoryError.InvalidAmount;
                }

                if (clamp.CompareTo(MaxInstancesPerOperationQuantity) > 0)
                {
                    clamp = MaxInstancesPerOperationQuantity;
                }
            }

            var maxStack = _catalog.GetMaxStack(item);
            if (maxStack != long.MaxValue && _catalog.GetRegenPeriodTicks(item) == 0)
            {
                var room = (BigNum)maxStack - GetQuantity(item);
                if (room.Sign <= 0)
                {
                    return InventoryError.None;   // 가득 — 0 지급 성공
                }

                if (clamp.CompareTo(room) > 0)
                {
                    clamp = room;
                }
            }

            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.Add, item, 0, clamp));
            var error = CommitOps(_applyOps, out _, created);
            if (error == InventoryError.None)
            {
                granted = clamp;
            }

            return error;
        }

        /// <summary>수량을 소모한다. amount는 양수여야 한다. 비스택형은 잠금 아닌 인스턴스
        /// amount개가 제거된다(순서 미보장 — 특정 인스턴스는 <see cref="TryRemoveByInstance"/>).</summary>
        public InventoryError TryRemove(ItemId item, BigNum amount)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.RemoveByItem, item, 0, amount));
            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>정의의 가용(잠금 제외) 수량 — 소모 가능량 표시·우선순위 소모 계획용.</summary>
        public BigNum GetRemovableQuantity(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                return (singleton.Flags & _removeBlockingFlags) != 0 ? BigNum.Zero : singleton.Quantity;
            }

            long count = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item && (entry.Value.Flags & _removeBlockingFlags) == 0)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>여러 정의의 총 보유 수량 합 — 풀 분리(리젠/보상, 무상/유상) 표시용.</summary>
        public BigNum GetQuantityAcross(ReadOnlySpan<ItemId> items)
        {
            var total = BigNum.Zero;
            for (var i = 0; i < items.Length; i++)
            {
                total += GetQuantity(items[i]);
            }

            return total;
        }

        /// <summary>
        /// 여러 정의에서 우선순위 순서로 나눠 소모한다 — 전부-아니면-전무. 풀 분리 패턴의
        /// 소모 경로(보상 티켓 먼저·무상 재화 먼저 등 — 순서는 호출측이 정한다).
        /// 가용(잠금 제외) 합이 부족하면 <see cref="InventoryError.Insufficient"/>에 무변경.
        /// 비스택형 소스는 정수 amount에서만 정확히 동작한다.
        /// </summary>
        /// <param name="sources">소모 우선순위 순서의 정의들(중복 없이).</param>
        /// <param name="amount">총 소모량(양수).</param>
        public InventoryError TryRemoveAcross(ReadOnlySpan<ItemId> sources, BigNum amount)
        {
            if (amount.Sign <= 0)
            {
                return InventoryError.InvalidAmount;
            }

            _applyOps.Clear();
            var remaining = amount;
            for (var i = 0; i < sources.Length && remaining.Sign > 0; i++)
            {
                if (!_catalog.Contains(sources[i]))
                {
                    return InventoryError.UnknownItem;
                }

                var available = GetRemovableQuantity(sources[i]);
                if (available.Sign <= 0)
                {
                    continue;
                }

                var take = available.CompareTo(remaining) < 0 ? available : remaining;
                _applyOps.Add(new TxOp(TxOpKind.RemoveByItem, sources[i], 0, take));
                remaining -= take;
            }

            if (remaining.Sign > 0)
            {
                return InventoryError.Insufficient;
            }

            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>특정 인스턴스에서 수량을 소모한다. 잠금 인스턴스는 <see cref="InventoryError.Locked"/>.
        /// 비스택형은 수량이 1이므로 amount 1로 인스턴스 자체가 제거된다.</summary>
        public InventoryError TryRemoveByInstance(long instanceId, BigNum amount)
        {
            _applyOps.Clear();
            _applyOps.Add(new TxOp(TxOpKind.RemoveInstanceAmount, ItemId.None, instanceId, amount));
            return CommitOps(_applyOps, out _, null);
        }

        /// <summary>
        /// 복수 델타를 전부-아니면-전무로 적용한다. 스택형·비스택형 혼합 가능 — 판정은
        /// 내부에서 1회. 순차 판정(같은 정의의 앞선 델타 누적 반영)이며 실패 시 완전
        /// 무변경, <paramref name="failedIndex"/>가 원인 델타를 가리킨다.
        /// </summary>
        public InventoryError TryApply(
            ReadOnlySpan<ItemDelta> deltas,
            out int failedIndex,
            List<ItemInstance<TState>>? created = null)
        {
            _applyOps.Clear();
            for (var i = 0; i < deltas.Length; i++)
            {
                var delta = deltas[i];
                _applyOps.Add(delta.Amount.Sign >= 0
                    ? new TxOp(TxOpKind.Add, delta.Item, 0, delta.Amount)
                    : new TxOp(TxOpKind.RemoveByItem, delta.Item, 0, -delta.Amount));
            }

            return CommitOps(_applyOps, out failedIndex, created);
        }

        /// <summary>
        /// 커밋 코어 — 검증 패스(수량 시뮬레이션)에서 전 연산이 통과해야만 적용 패스로
        /// 넘어간다. 배치 내 인스턴스 지정 소모가 가리키는 비스택형 인스턴스는 정의 단위
        /// 소모의 후보 풀에서 배치 전체에 걸쳐 제외된다(순서 무관). id 발급자·상태 팩토리는
        /// 검증 통과 후에만 호출되고, 성공 시 onChanged는 배치당 1회다.
        /// </summary>
        private InventoryError CommitOps(List<TxOp> ops, out int failedIndex, List<ItemInstance<TState>>? created)
        {
            failedIndex = -1;
            if (ops.Count == 0)
            {
                return InventoryError.None;
            }

            // 지정 대상 수집 — 비스택형 지정 인스턴스는 정의 단위 소모 풀에서 전역 제외.
            _txUnstackableTargets.Clear();
            for (var i = 0; i < ops.Count; i++)
            {
                var op = ops[i];
                if (op.Kind != TxOpKind.RemoveInstanceAll && op.Kind != TxOpKind.RemoveInstanceAmount)
                {
                    continue;
                }

                if (_instances.TryGetValue(op.InstanceId, out var target) && _catalog.IsUnstackable(target.Item))
                {
                    _txUnstackableTargets.Add(op.InstanceId);
                }
            }

            // 검증 패스 — 정의별 (총량, 가용 풀) 시뮬레이션 + 인스턴스 지정 소모량 확정.
            _txConsumedTargets.Clear();
            _txResolved.Clear();
            _txNetIds.Clear();
            _txNetTotal.Clear();
            _txNetPool.Clear();
            for (var i = 0; i < ops.Count; i++)
            {
                var error = ValidateOp(ops[i], out var resolved);
                _txResolved.Add(resolved);
                if (error != InventoryError.None)
                {
                    failedIndex = i;
                    return error;
                }
            }

            // 적용 패스 — 실패 불가능 상태에서 순서대로 반영.
            _appliedCount = 0;
            for (var i = 0; i < ops.Count; i++)
            {
                ApplyOp(ops[i], _txResolved[i], created);
            }

            _hasChanges = true;
            _onChanged?.Invoke();
            if (_onApplied != null && _appliedCount > 0)
            {
                _onApplied(new ReadOnlySpan<ItemDelta>(_applied, 0, _appliedCount));
            }

            return InventoryError.None;
        }

        private InventoryError ValidateOp(TxOp op, out BigNum resolved)
        {
            resolved = BigNum.Zero;
            switch (op.Kind)
            {
                case TxOpKind.Add:
                case TxOpKind.RemoveByItem:
                {
                    if (!_catalog.Contains(op.Item))
                    {
                        return InventoryError.UnknownItem;
                    }

                    if (op.Amount.Sign <= 0)
                    {
                        return InventoryError.InvalidAmount;
                    }

                    if (_catalog.IsUnstackable(op.Item))
                    {
                        if (!TryToInstanceCount(op.Amount, out _))
                        {
                            return InventoryError.InvalidAmount;
                        }
                    }
                    else if (_catalog.GetRegenPeriodTicks(op.Item) > 0 && op.Amount.Exponent < 0)
                    {
                        return InventoryError.InvalidAmount;   // 리젠 정의는 정수 수량만 — 정산 산술의 전제
                    }

                    var net = NetIndexOf(op.Item);
                    if (op.Kind == TxOpKind.Add)
                    {
                        if (!TryAddQuantity(_txNetTotal[net], op.Amount, out var total))
                        {
                            return InventoryError.ExceedsMaxStack;
                        }

                        // 리젠 정의의 maxStack은 하드 상한이 아니라 리젠 목표선 —
                        // 명시적 지급(보상 티켓 등)은 목표선을 넘어 쌓인다.
                        var maxStack = _catalog.GetMaxStack(op.Item);
                        if (maxStack != long.MaxValue
                            && _catalog.GetRegenPeriodTicks(op.Item) == 0
                            && total.CompareTo(maxStack) > 0)
                        {
                            return InventoryError.ExceedsMaxStack;
                        }

                        _txNetTotal[net] = total;
                        _txNetPool[net] += op.Amount;   // 신규 지급분은 잠금 없음 — 가용
                        return InventoryError.None;
                    }

                    if (_txNetPool[net].CompareTo(op.Amount) < 0)
                    {
                        return InventoryError.Insufficient;
                    }

                    _txNetPool[net] -= op.Amount;
                    _txNetTotal[net] -= op.Amount;
                    return InventoryError.None;
                }

                default:
                {
                    if (!_instances.TryGetValue(op.InstanceId, out var target))
                    {
                        return InventoryError.UnknownInstance;
                    }

                    if (op.Kind == TxOpKind.RemoveInstanceAmount && op.Amount.Sign <= 0)
                    {
                        return InventoryError.InvalidAmount;
                    }

                    if ((target.Flags & _removeBlockingFlags) != 0)
                    {
                        return InventoryError.Locked;
                    }

                    if (_catalog.IsUnstackable(target.Item))
                    {
                        if (op.Kind == TxOpKind.RemoveInstanceAmount)
                        {
                            var comparison = op.Amount.CompareTo(BigNum.One);
                            if (comparison > 0)
                            {
                                return InventoryError.Insufficient;
                            }

                            if (comparison < 0)
                            {
                                return InventoryError.InvalidAmount;
                            }
                        }

                        for (var i = 0; i < _txConsumedTargets.Count; i++)
                        {
                            if (_txConsumedTargets[i] == op.InstanceId)
                            {
                                return InventoryError.Insufficient;   // 같은 인스턴스 중복 지정
                            }
                        }

                        _txConsumedTargets.Add(op.InstanceId);
                        var netUnstackable = NetIndexOf(target.Item);
                        _txNetTotal[netUnstackable] -= BigNum.One;   // 풀은 이미 전역 제외됨
                        resolved = BigNum.One;
                        return InventoryError.None;
                    }

                    // 스택 싱글턴 지정 — 정의 단위 소모와 같은 풀로 정산한다.
                    var netSingleton = NetIndexOf(target.Item);
                    var amount = op.Kind == TxOpKind.RemoveInstanceAll ? _txNetPool[netSingleton] : op.Amount;
                    if (amount.Sign <= 0 || _txNetPool[netSingleton].CompareTo(amount) < 0)
                    {
                        return InventoryError.Insufficient;
                    }

                    _txNetPool[netSingleton] -= amount;
                    _txNetTotal[netSingleton] -= amount;
                    resolved = amount;
                    return InventoryError.None;
                }
            }
        }

        private void ApplyOp(TxOp op, BigNum resolved, List<ItemInstance<TState>>? created)
        {
            switch (op.Kind)
            {
                case TxOpKind.Add:
                    ApplyGrant(op.Item, op.Amount, created);
                    RecordApplied(op.Item, op.Amount);
                    return;

                case TxOpKind.RemoveByItem:
                    ApplyConsume(op.Item, op.Amount);
                    RecordApplied(op.Item, -op.Amount);
                    return;

                default:
                {
                    var target = _instances[op.InstanceId];
                    var item = target.Item;
                    if (target.Quantity.CompareTo(resolved) == 0)
                    {
                        RemoveInstance(target);
                    }
                    else
                    {
                        target.Quantity -= resolved;
                        MarkChangedNoNotify(target);
                    }

                    RecordApplied(item, -resolved);
                    return;
                }
            }
        }

        private void RecordApplied(ItemId item, BigNum delta)
        {
            if (_onApplied == null)
            {
                return;
            }

            if (_appliedCount == _applied.Length)
            {
                System.Array.Resize(ref _applied, _applied.Length == 0 ? 8 : _applied.Length * 2);
            }

            _applied[_appliedCount++] = new ItemDelta(item, delta);
        }

        private void ApplyGrant(ItemId item, BigNum amount, List<ItemInstance<TState>>? created)
        {
            if (_catalog.IsUnstackable(item))
            {
                TryToInstanceCount(amount, out var count);   // 검증 통과분 — 실패 불가
                for (var i = 0; i < count; i++)
                {
                    var instance = CreateInstance(item, BigNum.One);
                    created?.Add(instance);
                }

                return;
            }

            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                singleton.Quantity += amount;
                MarkChangedNoNotify(singleton);
            }
            else
            {
                var instance = CreateInstance(item, amount);
                _stackSingletons.Add(item, instance.InstanceId);
                created?.Add(instance);
            }
        }

        private void ApplyConsume(ItemId item, BigNum amount)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                if (singleton.Quantity.CompareTo(amount) == 0)
                {
                    RemoveInstance(singleton);
                }
                else
                {
                    singleton.Quantity -= amount;
                    MarkChangedNoNotify(singleton);
                }

                return;
            }

            // 비스택형 — 잠금·배치 지정 대상이 아닌 인스턴스를 count개 수집 후 제거.
            TryToInstanceCount(amount, out var count);   // 검증 통과분 — 실패 불가
            _removeScratch.Clear();
            foreach (var entry in _instances)
            {
                if (_removeScratch.Count >= count)
                {
                    break;
                }

                if (entry.Value.Item == item
                    && (entry.Value.Flags & _removeBlockingFlags) == 0
                    && !IsUnstackableTarget(entry.Key))
                {
                    _removeScratch.Add(entry.Value);
                }
            }

            for (var i = 0; i < _removeScratch.Count; i++)
            {
                RemoveInstance(_removeScratch[i]);
            }

            _removeScratch.Clear();
        }

        /// <summary>정의별 시뮬레이션 엔트리를 찾거나 만든다 — (총량, 가용 풀) 시작값은
        /// 현재 보유량과 잠금·지정 대상 제외 가용량.</summary>
        private int NetIndexOf(ItemId item)
        {
            for (var i = 0; i < _txNetIds.Count; i++)
            {
                if (_txNetIds[i] == item)
                {
                    return i;
                }
            }

            _txNetIds.Add(item);
            _txNetTotal.Add(GetQuantity(item));
            _txNetPool.Add(GetRemovablePoolBase(item));
            return _txNetIds.Count - 1;
        }

        private BigNum GetRemovablePoolBase(ItemId item)
        {
            if (_stackSingletons.TryGetValue(item, out var singletonId))
            {
                var singleton = _instances[singletonId];
                return (singleton.Flags & _removeBlockingFlags) != 0 ? BigNum.Zero : singleton.Quantity;
            }

            long count = 0;
            foreach (var entry in _instances)
            {
                if (entry.Value.Item == item
                    && (entry.Value.Flags & _removeBlockingFlags) == 0
                    && !IsUnstackableTarget(entry.Key))
                {
                    count++;
                }
            }

            return count;
        }

        private bool IsUnstackableTarget(long instanceId)
        {
            for (var i = 0; i < _txUnstackableTargets.Count; i++)
            {
                if (_txUnstackableTargets[i] == instanceId)
                {
                    return true;
                }
            }

            return false;
        }

        private ItemInstance<TState> CreateInstance(ItemId item, BigNum quantity)
        {
            var instance = new ItemInstance<TState>(
                this, _instanceIdIssuer(), item, quantity, 0, 0, _stateFactory(item))
            {
                IsNew = true,
            };
            _instances.Add(instance.InstanceId, instance);
            return instance;
        }

        private void RemoveInstance(ItemInstance<TState> instance)
        {
            _instances.Remove(instance.InstanceId);
            if (_stackSingletons.TryGetValue(instance.Item, out var singletonId) && singletonId == instance.InstanceId)
            {
                _stackSingletons.Remove(instance.Item);
            }

            if (!instance.IsNew)
            {
                _removed.Add(instance.InstanceId);
            }

            instance._owner = null;
        }

        private static void MarkChangedNoNotify(ItemInstance<TState> instance) => instance.Changed = true;

        /// <summary>BigNum 덧셈 — 지수 한계 초과는 false(전부-아니면-전무 판정용).</summary>
        private static bool TryAddQuantity(BigNum a, BigNum b, out BigNum result)
        {
            try
            {
                result = a + b;
                return true;
            }
            catch (BigNumOverflowException)
            {
                result = BigNum.Zero;
                return false;
            }
        }

        /// <summary>비스택형 수량 변환 — 양수·정수·<see cref="MaxInstancesPerOperation"/> 이하만 허용.</summary>
        private static bool TryToInstanceCount(BigNum amount, out int count)
        {
            count = 0;
            if (amount.Exponent < 0 || amount.CompareTo(MaxInstancesPerOperationQuantity) > 0)
            {
                return false;
            }

            long value = amount.Mantissa;
            for (var i = 0; i < amount.Exponent; i++)
            {
                value *= 10;
            }

            count = (int)value;
            return true;
        }
    }
}
