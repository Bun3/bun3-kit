using System;

namespace Bun3.Server.Items
{
    /// <summary>로그 항목의 종류.</summary>
    public enum ActionLogEntryKind : byte
    {
        /// <summary>스코프 시작 — <see cref="ActionLogEntry.Text"/>가 행동 이름·문맥.</summary>
        ScopeStart,

        /// <summary>시스템이 남긴 자유 노트(추첨 결과·업적 클리어·천장 카운터 등).</summary>
        Note,

        /// <summary>인벤토리 변경 — <see cref="ActionLogEntry.Change"/>에 델타·잔량,
        /// <see cref="ActionLogEntry.Label"/>에 인벤토리 라벨(가방/창고 구분).</summary>
        Change,
    }

    /// <summary>CS 감사 원장의 한 항목 — Depth로 들여쓰면 "행동 트레이스"가 된다.</summary>
    public readonly struct ActionLogEntry
    {
        internal ActionLogEntry(ActionLogEntryKind kind, int depth, string? text, string? label, InventoryChange change)
        {
            Kind = kind;
            Depth = depth;
            Text = text;
            Label = label;
            Change = change;
        }

        /// <summary>항목 종류.</summary>
        public ActionLogEntryKind Kind { get; }

        /// <summary>중첩 깊이(루트 스코프 = 0).</summary>
        public int Depth { get; }

        /// <summary>스코프 이름 또는 노트 내용. Change 항목은 null.</summary>
        public string? Text { get; }

        /// <summary>변경을 기록한 인벤토리의 라벨(복수 인벤토리 구분용). 미지정이면 null.</summary>
        public string? Label { get; }

        /// <summary>변경 내용(델타·변경 후 잔량). Change 항목에만 유효.</summary>
        public InventoryChange Change { get; }
    }

    /// <summary>원장 싱크 — 루트 스코프가 닫힐 때(스코프 밖 항목은 즉시) 완성된 묶음을
    /// 받는다. span은 호출 동안만 유효 — 보관은 복사/직렬화로.</summary>
    /// <param name="entries">완성된 로그 항목들(발생 순서, Depth 들여쓰기).</param>
    public delegate void ActionLogHandler(ReadOnlySpan<ActionLogEntry> entries);

    /// <summary>로그 스코프 핸들 — using으로 닫는다. 여는 순서의 역순으로만 닫을 수 있다.</summary>
    public readonly struct ActionLogScope : IDisposable
    {
        private readonly ActionLog? _log;
        private readonly int _token;

        internal ActionLogScope(ActionLog? log, int token)
        {
            _log = log;
            _token = token;
        }

        /// <summary>스코프를 닫는다 — 루트가 닫히면 원장 싱크로 묶음이 전달된다.</summary>
        public void Dispose() => _log?.EndScope(_token);
    }

    /// <summary>
    /// 행동 로그(CS 감사 원장) — 인벤토리 내부 기능이 아니라 세션/플레이어당 1개를 두는
    /// 범용 문맥이다. 핸들러(HandleBuyItem 등)가 루트 스코프를 열면 그 아래에서 일어나는
    /// 모든 일 — 인벤토리 변경(참조를 받은 인벤토리들이 자동 첨부), 이벤트로 파생된 업적
    /// 클리어(<see cref="Log"/> 노트), 그로 인한 보상 지급(다시 자동 첨부) — 이 하나의
    /// 트리에 순서대로 남는다. 하위 로직은 "뭘로 시작됐는지" 몰라도 되고, 스코프는 자유
    /// 중첩, 루트가 닫힐 때 완성 묶음이 싱크로 전달된다. 스코프 밖 항목은 즉시 단건
    /// 전달돼 원장에 구멍이 없다. 단일 세션 액터 안에서만 쓴다(락 없음).
    /// </summary>
    public sealed class ActionLog
    {
        private readonly ActionLogHandler _sink;
        private ActionLogEntry[] _entries = Array.Empty<ActionLogEntry>();
        private int _count;
        private int _depth;

        /// <summary>원장 싱크로 로그를 만든다 — 게임은 싱크 어댑터 하나로
        /// (플레이어·시각·행동 트리·델타·잔량)를 영속화하면 라이브 CS 추적이 완성된다.</summary>
        public ActionLog(ActionLogHandler sink)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        }

        /// <summary>스코프를 연다 — 이름에 행동과 문맥을 담는다(예: "BuyItem product=1021 x3").</summary>
        public ActionLogScope BeginScope(string name)
        {
            Append(ActionLogEntryKind.ScopeStart, _depth, name, null, default);
            _depth++;
            return new ActionLogScope(this, _depth);
        }

        /// <summary>현재 스코프에 자유 노트를 남긴다 — 어떤 시스템이든(업적 클리어, 추첨
        /// 결과 등). 스코프 밖이면 즉시 단건 묶음으로 전달된다.</summary>
        public void Log(string note)
        {
            Append(ActionLogEntryKind.Note, _depth, note, null, default);
            FlushIfRoot();
        }

        internal void EndScope(int token)
        {
            if (token != _depth)
            {
                throw new InvalidOperationException("로그 스코프는 여는 순서의 역순으로 닫아야 합니다.");
            }

            _depth--;
            FlushIfRoot();
        }

        internal void AppendChanges(string? label, ReadOnlySpan<InventoryChange> changes)
        {
            for (var i = 0; i < changes.Length; i++)
            {
                Append(ActionLogEntryKind.Change, _depth, null, label, changes[i]);
            }

            FlushIfRoot();
        }

        private void Append(ActionLogEntryKind kind, int depth, string? text, string? label, InventoryChange change)
        {
            if (_count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length == 0 ? 16 : _entries.Length * 2);
            }

            _entries[_count++] = new ActionLogEntry(kind, depth, text, label, change);
        }

        private void FlushIfRoot()
        {
            if (_depth != 0 || _count == 0)
            {
                return;
            }

            var count = _count;
            _count = 0;
            _sink(new ReadOnlySpan<ActionLogEntry>(_entries, 0, count));
        }
    }
}
