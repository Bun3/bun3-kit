namespace Bun3.Unity.Core.UnifiedToggle
{
    public interface IUnifiedOption
    {
        void SetOptionValues(string[] values);
    }

    public interface IUnifiedOption<in TComponent> : IUnifiedOption
    {
        void SetValue(TComponent component, string value);
    }
}
