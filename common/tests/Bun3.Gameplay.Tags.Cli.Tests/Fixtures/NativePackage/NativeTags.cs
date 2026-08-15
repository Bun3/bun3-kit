using Bun3.Gameplay.Tags;

[assembly: GameplayTagSource("fixture.native", "Fixture Native")]

namespace Bun3.Gameplay.Tags.Cli.Tests.Fixtures.NativePackage;

public static class NativeTags
{
    [NativeGameplayTag("Ready state")]
    public const string Ready = "State.Ready";

    [NativeGameplayTag]
    public const string Jump = "Ability.Jump";
}
