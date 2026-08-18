using System;

namespace Bun3.Server.Items
{
    // CS 감사 원장 — 스코프 트리 방식. 핸들러(HandleBuyItem 등)가 루트 스코프를 열면
    // 하위 로직은 "뭘로 시작됐는지" 몰라도 된다: 인벤토리 변경은 현재 스코프에 자동
    // 첨부되고, 부가 정보는 Log()로 남기며, 스코프는 자유롭게 중첩된다.
    // 루트가 닫힐 때 완성된 트리가 onLog 싱크로 전달된다(들여쓰면 행동 트레이스).
    public sealed partial class ItemInventory<TState>
    {
        /// <summary>원장 싱크가 연결돼 있는지 — 비싼 로그 문자열 구성 전 가드용.</summary>
        public bool IsLogging => _onLog != null;

        /// <summary>
        /// 로그 스코프를 연다 — 이름에 행동과 문맥을 담는다(예: "BuyItem product=1021 x3").
        /// 스코프 안의 모든 인벤토리 변경(델타·잔량)은 자동 첨부되고, 중첩 가능하며,
        /// 루트 스코프가 닫힐 때 전체 묶음이 원장 싱크로 전달된다. 싱크 미지정 시 no-op.
        /// </summary>
        public InventoryLogScope<TState> BeginLogScope(string name)
        {
            if (_onLog == null)
            {
                return new InventoryLogScope<TState>(null, 0);
            }

            AppendLog(InventoryLogEntryKind.ScopeStart, _logDepth, name, default);
            _logDepth++;
            return new InventoryLogScope<TState>(this, _logDepth);
        }

        /// <summary>현재 스코프에 자유 노트를 남긴다(추첨 결과·천장 카운터 등).
        /// 스코프 밖이면 단건 묶음으로 즉시 전달된다. 싱크 미지정 시 no-op.</summary>
        public void Log(string note)
        {
            if (_onLog == null)
            {
                return;
            }

            AppendLog(InventoryLogEntryKind.Note, _logDepth, note, default);
            if (_logDepth == 0)
            {
                FlushLog();
            }
        }

        internal void EndLogScope(int token)
        {
            if (token != _logDepth)
            {
                throw new InvalidOperationException("로그 스코프는 여는 순서의 역순으로 닫아야 합니다.");
            }

            _logDepth--;
            if (_logDepth == 0)
            {
                FlushLog();
            }
        }

        /// <summary>커밋 성공 직후 — 적용 버퍼의 변경들을 현재 스코프에 첨부한다.
        /// 스코프 밖 변경은 즉시 단건 묶음으로 전달돼 원장에 구멍이 없다.</summary>
        private void LogCommittedChanges()
        {
            if (_onLog == null || _appliedCount == 0)
            {
                return;
            }

            for (var i = 0; i < _appliedCount; i++)
            {
                AppendLog(InventoryLogEntryKind.Change, _logDepth, null, _applied[i]);
            }

            if (_logDepth == 0)
            {
                FlushLog();
            }
        }

        private void AppendLog(InventoryLogEntryKind kind, int depth, string? text, InventoryChange change)
        {
            if (_logCount == _logEntries.Length)
            {
                Array.Resize(ref _logEntries, _logEntries.Length == 0 ? 16 : _logEntries.Length * 2);
            }

            _logEntries[_logCount++] = new InventoryLogEntry(kind, depth, text, change);
        }

        private void FlushLog()
        {
            if (_logCount == 0)
            {
                return;
            }

            var count = _logCount;
            _logCount = 0;
            _onLog!(new ReadOnlySpan<InventoryLogEntry>(_logEntries, 0, count));
        }
    }
}
