namespace Bun3.Common.UnifiedToggle
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
