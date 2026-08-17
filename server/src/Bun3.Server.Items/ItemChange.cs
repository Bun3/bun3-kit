namespace Bun3.Server.Items
{
    /// <summary>마지막 드레인 이후 인스턴스에 일어난 변경의 종류.</summary>
    public enum ItemChangeKind
    {
        /// <summary>새로 생성됨 — DB INSERT 대상.</summary>
        Created,

        /// <summary>수량·플래그·상태가 변경됨 — DB UPDATE 대상.</summary>
        Updated,

        /// <summary>제거됨 — DB DELETE 대상. <see cref="ItemChange{TState}.Instance"/>는 null.</summary>
        Removed,
    }

    /// <summary><see cref="ItemInventory{TState}.DrainChanges"/>가 내놓는 변경 1건.
    /// 생성 후 드레인 전에 제거된 인스턴스는 상쇄되어 나오지 않는다(DB 왕복 불필요).</summary>
    /// <typeparam name="TState">게임이 정의하는 인스턴스 상태 타입.</typeparam>
    public readonly struct ItemChange<TState>
    {
        internal ItemChange(ItemChangeKind kind, long instanceId, ItemInstance<TState>? instance)
        {
            Kind = kind;
            InstanceId = instanceId;
            Instance = instance;
        }

        /// <summary>변경 종류.</summary>
        public ItemChangeKind Kind { get; }

        /// <summary>대상 인스턴스 id.</summary>
        public long InstanceId { get; }

        /// <summary>대상 인스턴스 — <see cref="ItemChangeKind.Removed"/>면 null.</summary>
        public ItemInstance<TState>? Instance { get; }
    }
}
