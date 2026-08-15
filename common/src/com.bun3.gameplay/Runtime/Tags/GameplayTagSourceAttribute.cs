#nullable enable
using System;
using System.Diagnostics;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// Native GameplayTag 선언을 제공하는 어셈블리의 안정적인 Source 정보를 지정합니다.
    /// </summary>
    [Conditional("BUN3_GAMEPLAY_TAGS_AUTHORING")]
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class GameplayTagSourceAttribute : Attribute
    {
        /// <summary>
        /// Native GameplayTag Source 특성을 초기화합니다.
        /// </summary>
        /// <param name="sourceId">Source의 안정적인 소문자 식별자입니다.</param>
        /// <param name="displayName">사용자에게 표시할 Source 이름입니다.</param>
        public GameplayTagSourceAttribute(string sourceId, string displayName)
        {
            SourceId = sourceId;
            DisplayName = displayName;
        }

        /// <summary>
        /// Source의 안정적인 소문자 식별자를 가져옵니다.
        /// </summary>
        public string SourceId { get; }

        /// <summary>
        /// 사용자에게 표시할 Source 이름을 가져옵니다.
        /// </summary>
        public string DisplayName { get; }
    }
}
