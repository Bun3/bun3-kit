namespace Bun3.State
{
    /// <summary>Restores a generated scalar field without invoking its public setter.</summary>
    /// <typeparam name="T">The scalar field type.</typeparam>
    public interface IStateValueTarget<T>
    {
        /// <summary>Restores the specified field to a recorded value.</summary>
        /// <param name="slot">The generated field slot.</param>
        /// <param name="value">The value to restore.</param>
        void RestoreValue(int slot, T value);
    }
}
