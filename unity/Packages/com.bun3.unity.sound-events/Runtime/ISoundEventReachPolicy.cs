using UnityEngine;

namespace Bun3.Unity.SoundEvents
{
    /// <summary>Replaces the world's spatial reach test without interpreting event kinds.</summary>
    public interface ISoundEventReachPolicy
    {
        /// <summary>Tests reach. World mutation is forbidden while this method executes.</summary>
        bool CanReach(in SoundEventSnapshot snapshot, Vector3 listenerPosition, float listenerRadius);
    }

    /// <summary>Detects inclusive sphere contact using double-precision distance arithmetic.</summary>
    public sealed class SphereSoundEventReachPolicy : ISoundEventReachPolicy
    {
        /// <inheritdoc />
        public bool CanReach(in SoundEventSnapshot snapshot, Vector3 listenerPosition, float listenerRadius)
        {
            var source = snapshot.Data.Position;
            double x = (double)source.x - listenerPosition.x;
            double y = (double)source.y - listenerPosition.y;
            double z = (double)source.z - listenerPosition.z;
            double radius = (double)snapshot.Data.Radius + listenerRadius;
            return x * x + y * y + z * z <= radius * radius;
        }
    }
}
