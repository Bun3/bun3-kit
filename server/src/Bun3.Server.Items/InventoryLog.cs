using System;

namespace Bun3.Server.Items
{
    /// <summary>로그 항목의 종류.</summary>
    public enum InventoryLogEntryKind : byte
    {
        /// <summary>스코프 시작 — <see cref="InventoryLogEntry.Text"/>가 스코프 이름(행동·문맥).</summary>
        ScopeStart,

        /// <summary>게임이 남긴 자유 노트(추첨 결과·천장 카운터 등).</summary>
        Note,

        /// <summary>인벤토리 변경 — <see cref="InventoryLogEntry.Change"/>에 델타·잔량.</summary>
        Change,
    }

    /// <summary>CS 감사 원장의 한 항목 — Depth로 들여쓰면 "행동 트레이스"가 된다.</summary>
    public readonly struct InventoryLogEntry
    {
        internal InventoryLogEntry(InventoryLogEntryKind kind, int depth, string? text, InventoryChange change)
        {
            Kind = kind;
            Depth = depth;
            Text = text;
            Change = change;
        }

        /// <summary>항목 종류.</summary>
        public InventoryLogEntryKind Kind { get; }

        /// <summary>중첩 깊이(루트 스코프 = 0).</summary>
        public int Depth { get; }

        /// <summary>스코프 이름 또는 노트 내용. Change 항목은 null.</summary>
        public string? Text { get; }

        /// <summary>변경 내용(델타·변경 후 잔량). Change 항목에만 유효.</summary>
        public InventoryChange Change { get; }
    }

    /// <summary>
    /// 감사 원장 싱크 — 루트 스코프가 닫힐 때 완성된 항목 묶음을 받는다(스코프 밖 변경은
    /// 즉시 단건 묶음으로 — 원장에 구멍이 없도록). 게임은 이 어댑터 하나로
    /// (플레이어·시각·행동 트리·델타·잔량)를 영속화하면 라이브 CS 추적이 완성된다.
    /// span은 호출 동안만 유효 — 보관은 복사/직렬화로.
    /// </summary>
    /// <param name="entries">완성된 로그 항목들(발생 순서, Depth 들여쓰기).</param>
    public delegate void InventoryLogHandler(ReadOnlySpan<InventoryLogEntry> entries);

    /// <summary>로그 스코프 핸들 — using으로 닫는다. 여는 순서의 역순으로만 닫을 수 있다.</summary>
    public readonly struct InventoryLogScope<TState> : IDisposable
    {
        private readonly ItemInventory<TState>? _inventory;
        private readonly int _token;

        internal InventoryLogScope(ItemInventory<TState>? inventory, int token)
        {
            _inventory = inventory;
            _token = token;
        }

        /// <summary>스코프를 닫는다 — 루트가 닫히면 원장 싱크로 묶음이 전달된다.</summary>
        public void Dispose() => _inventory?.EndLogScope(_token);
    }
}
