#nullable enable
using System;
using System.Diagnostics;

namespace Bun3.Gameplay.Tags
{
    /// <summary>
    /// public const string 필드를 Native GameplayTag 선언으로 표시합니다.
    /// </summary>
    [Conditional("BUN3_GAMEPLAY_TAGS_AUTHORING")]
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = false, Inherited = false)]
    public sealed class NativeGameplayTagAttribute : Attribute
    {
        /// <summary>
        /// Native GameplayTag 특성을 초기화합니다.
        /// </summary>
        /// <param name="comment">작성 도구에 표시할 Source별 설명입니다.</param>
        public NativeGameplayTagAttribute(string comment = "")
        {
            Comment = comment;
        }

        /// <summary>
        /// 작성 도구에 표시할 Source별 설명을 가져옵니다.
        /// </summary>
        public string Comment { get; }
    }
}
