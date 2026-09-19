using System;
using System.Collections.Generic;
using UnityEngine;
namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Registry of reusable output bindings, shared across acoustic world replacements.</summary>
    public sealed class PlanarAcousticOutputs
    {
        private readonly List<IPlanarAcousticOutput> outputs = new();
        /// <summary>Registered outputs in processing order.</summary>
        public IReadOnlyList<IPlanarAcousticOutput> Items => outputs;
        /// <summary>Adds an output and keeps it silent until the next valid simulation.</summary>
        public void Register(IPlanarAcousticOutput output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            if (outputs.Contains(output)) return;
            output.BlockAndDetach();
            outputs.Add(output);
        }
        /// <summary>Detaches and removes an output.</summary>
        public void Unregister(IPlanarAcousticOutput output)
        { if (outputs.Remove(output)) output.BlockAndDetach(); }
        /// <summary>Silences every output before geometry changes.</summary>
        public void Block() { for (int i = 0; i < outputs.Count; i++) outputs[i].Block(); }
        /// <summary>Silences and detaches all outputs while retaining registrations.</summary>
        public void Detach() { for (int i = 0; i < outputs.Count; i++) outputs[i].BlockAndDetach(); }
        /// <summary>Clears registrations after detachment.</summary>
        public void Clear() { Detach(); outputs.Clear(); }
    }

    /// <summary>Owns world/device lifetime and listener-driven, allocation-free steady-state output scheduling.</summary>
    public sealed class PlanarAcousticSessionScope : IDisposable
    {
        private readonly PlanarAcousticOutputs outputs;
        private readonly Func<PlanarAcousticWorld> createWorld;
        private readonly Func<bool> updateGeometry;
        private readonly Action releaseGeometry;
        private AudioListener listener;
        private float nextTick, nextListenerSearch;
        private bool disposed;
        /// <summary>The current world; null after disposal or failed device reconstruction.</summary>
        public PlanarAcousticWorld World { get; private set; }
        /// <summary>Last world initialization failure; null after successful reconstruction.</summary>
        public Exception LastInitializationError { get; private set; }
        /// <summary>Creates a world and subscribes to device changes. Geometry hooks must not mutate output registration.</summary>
        public PlanarAcousticSessionScope(PlanarAcousticOutputs outputs, Func<PlanarAcousticWorld> createWorld,
            Func<bool> updateGeometry, Action releaseGeometry)
        {
            this.outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
            this.createWorld = createWorld ?? throw new ArgumentNullException(nameof(createWorld));
            this.updateGeometry = updateGeometry;
            this.releaseGeometry = releaseGeometry;
            AudioSettings.OnAudioConfigurationChanged += OnDeviceChanged;
            TryReinitialize();
        }
        /// <summary>Finds the active listener and publishes current paths at the requested interval, immediately after geometry or source changes.</summary>
        public void Tick(float now, double simulationTime, float interval)
        {
            if (disposed || World == null) return;
            try
            {
                if (float.IsNaN(interval) || float.IsInfinity(interval) || interval <= 0)
                    throw new ArgumentOutOfRangeException(nameof(interval));
                if (listener != null && !listener.isActiveAndEnabled) { listener = null; nextListenerSearch = 0; }
                if (listener == null && now >= nextListenerSearch)
                {
                    nextListenerSearch = now + 1f;
                    var candidates = UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None);
                    for (int i = 0; i < candidates.Length; i++)
                        if (candidates[i].isActiveAndEnabled) { listener = candidates[i]; break; }
                }
                if (listener == null) { outputs.Detach(); return; }
                bool changed = updateGeometry != null && updateGeometry();
                bool initial = false;
                var items = outputs.Items;
                for (int i = 0; i < items.Count; i++)
                { items[i].Gate(World, listener.transform.position); initial |= items[i].RequiresSimulation; }
                if (now < nextTick && !changed && !initial) return;
                nextTick = now + interval;
                for (int i = 0; i < items.Count; i++) items[i].Prepare(World);
                World.Tick(listener.transform.position, simulationTime);
                for (int i = 0; i < items.Count; i++) items[i].Publish(World);
            }
            catch { ReleaseWorld(); throw; }
        }
        /// <summary>Releases the current world and retries construction while retaining the enabled session's device subscription.</summary>
        public bool TryReinitialize()
        {
            if (disposed) return false;
            ReleaseWorld();
            try
            {
                World = createWorld() ?? throw new InvalidOperationException("The world factory returned null.");
                LastInitializationError = null;
                return true;
            }
            catch (Exception error) { LastInitializationError = error; return false; }
        }
        private void OnDeviceChanged(bool changed)
        {
            if (!disposed && !TryReinitialize()) Debug.LogException(LastInitializationError);
        }
        private void ReleaseWorld()
        {
            outputs.Detach();
            World?.Dispose(); World = null;
            releaseGeometry?.Invoke();
            listener = null; nextTick = nextListenerSearch = 0;
        }
        /// <summary>Detaches all outputs and releases native resources and the device subscription.</summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            AudioSettings.OnAudioConfigurationChanged -= OnDeviceChanged;
            ReleaseWorld();
        }
    }
}
