using System;

namespace PackageConsumer
{
    internal static class Program
    {
        private static void Main()
        {
            var state = new EquipmentState();
            state.Level = 5;
            Console.WriteLine($"Level={state.Level}");
        }
    }
}
