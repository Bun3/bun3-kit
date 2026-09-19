using System;
using System.Runtime.InteropServices;
using SA = global::SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio
{
    /// <summary>Whether a copied simulation result is current, awaiting computation, or failed.</summary>
    public enum SteamAudioPathSimulationStatus
    {
        /// <summary>Inputs changed or no simulation has completed.</summary>
        Pending,
        /// <summary>The native run completed and returned finite data; probe coverage is not implied.</summary>
        Valid,
        /// <summary>A run failed or returned invalid data; this is not a blocked-route result.</summary>
        Failed
    }

    /// <summary>A source registration tied to one scope, slot and generation.</summary>
    public readonly struct SteamAudioSimulationSourceHandle
    {
        internal readonly SteamAudioPathSimulationScope Owner;
        internal readonly int Index;
        internal readonly uint Generation;
        internal SteamAudioSimulationSourceHandle(SteamAudioPathSimulationScope owner, int index, uint generation)
        { Owner = owner; Index = index; Generation = generation; }
        /// <summary>Whether this registration remains active. Query on the scope's creating thread.</summary>
        public bool IsValid => Owner != null && Owner.IsValid(this);
    }

    /// <summary>Copied native scalars, containing no borrowed pointers or inferred path distance.</summary>
    public readonly struct SteamAudioPathSimulationResult
    {
        /// <summary>Gets computation state, independently of physical reachability.</summary>
        public SteamAudioPathSimulationStatus Status { get; }
        /// <summary>Gets the caller's geometry revision represented by this result or pending request.</summary>
        public ulong GeometryRevision { get; }
        /// <summary>Gets the run's monotonic time; undefined while pending.</summary>
        public double ComputedAt { get; }
        /// <summary>Gets whether the copied native SH and EQ contain nonzero signal, not proof of geometric reachability.</summary>
        public bool HasPathSignal { get; }
        /// <summary>Gets the requested direct computations. Transmission and directivity are not computed.</summary>
        public SA.DirectSimulationFlags ComputedDirectEffects => SA.DirectSimulationFlags.DistanceAttenuation |
            SA.DirectSimulationFlags.AirAbsorption | SA.DirectSimulationFlags.Occlusion;
        /// <summary>Gets the native direct-path distance gain; this is not an indirect path length.</summary>
        public float DirectDistanceAttenuation { get; }
        /// <summary>Gets the native low-frequency direct air-absorption gain.</summary>
        public float DirectAirAbsorptionLow { get; }
        /// <summary>Gets the native middle-frequency direct air-absorption gain.</summary>
        public float DirectAirAbsorptionMid { get; }
        /// <summary>Gets the native high-frequency direct air-absorption gain.</summary>
        public float DirectAirAbsorptionHigh { get; }
        /// <summary>Gets the unoccluded direct fraction, including fractional volumetric results.</summary>
        public float DirectOcclusion { get; }
        /// <summary>Gets the low-frequency path EQ coefficient.</summary>
        public float EqLow { get; }
        /// <summary>Gets the middle-frequency path EQ coefficient.</summary>
        public float EqMid { get; }
        /// <summary>Gets the high-frequency path EQ coefficient.</summary>
        public float EqHigh { get; }
        /// <summary>Gets the native path EQ normalization request.</summary>
        public bool NormalizeEq { get; }

        internal SteamAudioPathSimulationResult(SteamAudioPathSimulationStatus status, ulong revision, double time,
            NativePathSimulation.Outputs output = default, bool signal = false)
        {
            Status = status; GeometryRevision = revision; ComputedAt = time; HasPathSignal = signal;
            DirectDistanceAttenuation = output.Direct.DistanceAttenuation;
            DirectAirAbsorptionLow = output.Direct.AirLow;
            DirectAirAbsorptionMid = output.Direct.AirMid;
            DirectAirAbsorptionHigh = output.Direct.AirHigh;
            DirectOcclusion = output.Direct.Occlusion;
            EqLow = output.Pathing.EqLow; EqMid = output.Pathing.EqMid; EqHigh = output.Pathing.EqHigh;
            NormalizeEq = output.Pathing.NormalizeEq != 0;
        }
    }

    /// <summary>
    /// Owns a bounded native direct/pathing simulator on one creating control thread. Native references to
    /// caller-authored default-scene geometry and baked probes are retained. The caller commits geometry
    /// before invalidating its revision and must prevent concurrent native scene mutation. Probe coverage
    /// remains a caller responsibility. This scope never runs on an audio callback or infers sealed regions.
    /// Debug contains validation rays; Diagnostics contains optional final-path capture.
    /// </summary>
    public sealed partial class SteamAudioPathSimulationScope : IDisposable
    {
        const SA.SimulationFlags Flags = SA.SimulationFlags.Direct | SA.SimulationFlags.Pathing;
        const SA.DirectSimulationFlags DirectFlags = SA.DirectSimulationFlags.DistanceAttenuation |
            SA.DirectSimulationFlags.AirAbsorption | SA.DirectSimulationFlags.Occlusion;
        sealed class DistanceState
        {
            internal float Minimum = 1, Maximum = float.PositiveInfinity, FadeFraction = .2f;
            internal bool CapturePathDistances, AttenuationEnabled = true;
            internal int PathCount, NonzeroPathCount;
            internal float Shortest, Longest;
            internal DiagnosticCapture Diagnostics;
            internal DistanceAttenuationCurve Curve;
        }
        static readonly SA.DistanceAttenuationCallback DistanceCallback = EvaluateDistance;
        static readonly IntPtr DistanceCallbackPointer = Marshal.GetFunctionPointerForDelegate(DistanceCallback);

        [AOT.MonoPInvokeCallback(typeof(SA.DistanceAttenuationCallback))]
        static float EvaluateDistance(float distance, IntPtr userData)
        {
            var state = (DistanceState)GCHandle.FromIntPtr(userData).Target;
            float gain = EvaluateDistanceGain(distance, state);
            if (state.CapturePathDistances && Finite(distance) && distance >= 0)
            {
                state.PathCount++;
                if (gain > 0) state.NonzeroPathCount++;
                state.Shortest = Math.Min(state.Shortest, distance);
                state.Longest = Math.Max(state.Longest, distance);
            }
            return gain;
        }

        static float EvaluateDistanceGain(float distance, DistanceState state)
        {
            if (!state.AttenuationEnabled) return Finite(distance) && distance >= 0 ? 1 : 0;
            if (state.Curve != null) return state.Curve.Evaluate(distance);
            return DistanceAttenuationProfile.Evaluate(distance, state.Minimum, state.Maximum, state.FadeFraction);
        }

        struct Slot
        {
            internal IntPtr Source;
            internal DistanceState Distance;
            internal GCHandle DistanceRoot;
            internal uint Generation;
            internal bool Active;
            internal NativePathSimulation.Inputs Inputs;
            internal SteamAudioPathSimulationResult Result;
        }
        readonly int ownerThread;
        readonly Slot[] slots;
        readonly float[] coefficients;
        readonly int order;
        readonly float visibilityRadius, visibilityThreshold, visibilityRange;
        IntPtr context, scene, probes, simulator;
        NativePathSimulation.SharedInputs shared;
        bool hasListener, needsCommit = true, disposed;
        ulong revision;
        double time;

        /// <summary>
        /// Creates and preallocates a 64-bit Steam Audio 4.8.1 simulator. The supplied scene must use
        /// SceneType.Default and probes must already contain a compatible pathing bake. The caller owns
        /// geometry and bake mutation; this scope retains native references and owns all simulation sources.
        /// Orders zero through three are supported. Warm updates, simulation and result copying allocate no managed memory.
        /// </summary>
        public SteamAudioPathSimulationScope(SA.Context context, SA.Scene scene, SA.ProbeBatch probes,
            int maxSources, int sampleRate, int frameSize, int order = 1, float visRadius = 0.05f,
            float visThreshold = 0.99f, float visRange = 4.5f, int maxOcclusionSamples = 32)
        {
            if (context == null || context.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(context));
            if (scene == null || scene.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(scene));
            if (probes == null || probes.Get() == IntPtr.Zero) throw new ArgumentNullException(nameof(probes));
            if (maxSources <= 0) throw new ArgumentOutOfRangeException(nameof(maxSources));
            if (sampleRate <= 0) throw new ArgumentOutOfRangeException(nameof(sampleRate));
            if (frameSize <= 0) throw new ArgumentOutOfRangeException(nameof(frameSize));
            if (order < 0 || order > 3) throw new ArgumentOutOfRangeException(nameof(order));
            if (!Finite(visRadius) || visRadius < 0) throw new ArgumentOutOfRangeException(nameof(visRadius));
            if (!Finite(visThreshold) || visThreshold < 0 || visThreshold > 1) throw new ArgumentOutOfRangeException(nameof(visThreshold));
            if (!Finite(visRange) || visRange <= 0) throw new ArgumentOutOfRangeException(nameof(visRange));
            if (maxOcclusionSamples <= 0) throw new ArgumentOutOfRangeException(nameof(maxOcclusionSamples));
            NativePathSimulation.ValidateLayout();
            ownerThread = Environment.CurrentManagedThreadId;
            SourceCapacity = maxSources; MaxOcclusionSamples = maxOcclusionSamples;
            CoefficientCount = (order + 1) * (order + 1);
            this.order = order;
            visibilityRadius = visRadius; visibilityThreshold = visThreshold; visibilityRange = visRange;
            slots = new Slot[maxSources];
            coefficients = new float[checked(maxSources * CoefficientCount)];
            try
            {
                this.context = SA.API.iplContextRetain(context.Get());
                this.scene = SA.API.iplSceneRetain(scene.Get());
                this.probes = SA.API.iplProbeBatchRetain(probes.Get());
                var settings = new SA.SimulationSettings
                {
                    flags = Flags, sceneType = SA.SceneType.Default, maxNumOcclusionSamples = maxOcclusionSamples,
                    maxNumSources = maxSources, numThreads = 1, numVisSamples = 1, maxOrder = order,
                    samplingRate = sampleRate, frameSize = frameSize
                };
                Check(SA.API.iplSimulatorCreate(this.context, ref settings, out simulator));
                SA.API.iplSimulatorSetScene(simulator, this.scene);
                SA.API.iplSimulatorAddProbeBatch(simulator, this.probes);
                var sourceSettings = new SA.SourceSettings { flags = Flags };
                for (int i = 0; i < slots.Length; i++)
                {
                    slots[i].Distance = new DistanceState();
                    slots[i].DistanceRoot = GCHandle.Alloc(slots[i].Distance);
                    Check(SA.API.iplSourceCreate(simulator, ref sourceSettings, out slots[i].Source));
                }
            }
            catch { ReleaseNative(); disposed = true; throw; }
        }

        /// <summary>Gets the fixed number of available source slots.</summary>
        public int SourceCapacity { get; }
        /// <summary>Gets the number of active registrations.</summary>
        public int SourceCount { get; private set; }
        /// <summary>Gets the exact length required for copied SH coefficient arrays.</summary>
        public int CoefficientCount { get; }
        /// <summary>Gets the maximum volumetric sample count prepared at construction.</summary>
        public int MaxOcclusionSamples { get; }
        /// <summary>Gets whether native resources have been released.</summary>
        public bool IsDisposed => disposed;

        /// <summary>Registers a source with raycast occlusion defaults, or returns false if all slots are occupied.</summary>
        public bool TryRegisterSource(in SA.CoordinateSpace3 source, out SteamAudioSimulationSourceHandle handle)
        {
            EnsureUsable(); ValidateSpace(source);
            handle = default;
            for (int i = 0; i < slots.Length; i++)
            {
                ref var slot = ref slots[i];
                if (slot.Active || slot.Generation == uint.MaxValue) continue;
                slot.Generation++;
                slot.Distance.Curve = null;
                slot.Distance.AttenuationEnabled = true; slot.Distance.Minimum = 1; slot.Distance.Maximum = float.PositiveInfinity; slot.Distance.FadeFraction = .2f;
                slot.Inputs = new NativePathSimulation.Inputs
                {
                    Flags = Flags, DirectFlags = DirectFlags, Source = source, Probes = probes,
                    Distance = new NativePathSimulation.DistanceModel
                    {
                        Type = (int)SA.DistanceAttenuationModelType.Callback, MinDistance = 1,
                        Callback = DistanceCallbackPointer, UserData = GCHandle.ToIntPtr(slot.DistanceRoot), Dirty = 1
                    },
                    OcclusionType = SA.OcclusionType.Raycast, OcclusionSamples = 1,
                    VisibilityRadius = visibilityRadius, VisibilityThreshold = visibilityThreshold, VisibilityRange = visibilityRange,
                    Order = order, EnableValidation = 1, FindAlternatePaths = 1
                };
                SA.API.iplSourceAdd(slot.Source, simulator);
                slot.Active = true;
                SourceCount++; needsCommit = true;
                Invalidate(i);
                handle = new SteamAudioSimulationSourceHandle(this, i, slot.Generation);
                return true;
            }
            return false;
        }

        /// <summary>Updates a registered source and invalidates its result. Returns false for stale or foreign handles.</summary>
        public bool SetSource(SteamAudioSimulationSourceHandle handle, in SA.CoordinateSpace3 source)
        {
            EnsureUsable(); ValidateSpace(source);
            if (!IsValid(handle)) return false;
            slots[handle.Index].Inputs.Source = source;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>
        /// Sets normalized inverse attenuation: unity gain at or within the minimum, then minimum/distance.
        /// Native direct and indirect path computations apply this model before producing SH; callers must
        /// not apply it again. Finite negatives clamp to zero, which is silent at positive distances.
        /// NaN/infinity throw. Unchanged values preserve results; stale or foreign registrations return false.
        /// Each new registration defaults to one, including a recycled slot. Creating thread only.
        /// </summary>
        public bool SetSourceMinimumDistance(SteamAudioSimulationSourceHandle handle, float minimumDistance)
        {
            EnsureUsable();
            if (!Finite(minimumDistance)) throw new ArgumentOutOfRangeException(nameof(minimumDistance));
            if (!IsValid(handle)) return false;
            minimumDistance = Math.Max(0, minimumDistance);
            ref var slot = ref slots[handle.Index];
            if (slot.Distance.Minimum == minimumDistance) return true;
            slot.Distance.Minimum = minimumDistance;
            slot.Inputs.Distance.MinDistance = minimumDistance;
            slot.Inputs.Distance.Dirty = 1;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>
        /// Applies an edge fade within the native distance callback, before each path is summed into SH.
        /// At or beyond maximumDistance the path contributes zero. Positive infinity disables the edge fade.
        /// The fade fraction must be finite in (0,1]. Defaults reset on source reuse. Creating thread only.
        /// </summary>
        public bool SetSourceDistanceRange(SteamAudioSimulationSourceHandle handle, float maximumDistance, float fadeFraction)
        {
            EnsureUsable();
            if (float.IsNaN(maximumDistance) || maximumDistance <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDistance));
            if (!Finite(fadeFraction) || fadeFraction <= 0 || fadeFraction > 1) throw new ArgumentOutOfRangeException(nameof(fadeFraction));
            if (!IsValid(handle)) return false;
            ref var slot = ref slots[handle.Index];
            if (slot.Distance.Maximum == maximumDistance && slot.Distance.FadeFraction == fadeFraction) return true;
            slot.Distance.Maximum = maximumDistance; slot.Distance.FadeFraction = fadeFraction;
            slot.Inputs.Distance.Dirty = 1;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>Sets an immutable authored curve. Null restores scalar distance settings. Unchanged snapshots preserve results.</summary>
        public bool SetSourceDistanceCurve(SteamAudioSimulationSourceHandle handle, DistanceAttenuationCurve curve)
        {
            EnsureUsable();
            if (!IsValid(handle)) return false;
            ref var slot = ref slots[handle.Index];
            if (ReferenceEquals(slot.Distance.Curve, curve)) return true;
            slot.Distance.Curve = curve;
            slot.Inputs.Distance.Dirty = 1;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>Enables distance gain independently of pathing, occlusion and spatial direction.</summary>
        public bool SetSourceDistanceAttenuationEnabled(SteamAudioSimulationSourceHandle handle, bool enabled)
        {
            EnsureUsable();
            if (!IsValid(handle)) return false;
            ref var slot = ref slots[handle.Index];
            if (slot.Distance.AttenuationEnabled == enabled) return true;
            slot.Distance.AttenuationEnabled = enabled;
            slot.Inputs.Distance.Dirty = 1;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>
        /// Reports distances actually evaluated during the last valid native pathing run, excluding direct simulation.
        /// These are native virtual-source distances, not an endpoint-inclusive grid route or a single chosen path.
        /// Counts include callback evaluations with zero interpolation weight; nonzero counts describe distance gain only.
        /// </summary>
        public bool TryGetPathDistanceRange(SteamAudioSimulationSourceHandle handle, out float shortest, out float longest,
            out int evaluatedCount, out int nonzeroGainCount)
        {
            EnsureUsable(); shortest = longest = 0; evaluatedCount = nonzeroGainCount = 0;
            if (!IsValid(handle) || slots[handle.Index].Result.Status != SteamAudioPathSimulationStatus.Valid) return false;
            var state = slots[handle.Index].Distance;
            if (state.PathCount == 0) return false;
            shortest = state.Shortest; longest = state.Longest;
            evaluatedCount = state.PathCount; nonzeroGainCount = state.NonzeroPathCount;
            return true;
        }

        /// <summary>Configures raycast or volumetric direct occlusion. The sample count cannot exceed prepared capacity.</summary>
        public bool SetOcclusion(SteamAudioSimulationSourceHandle handle, SA.OcclusionType type, float radius, int samples)
        {
            EnsureUsable();
            if (type != SA.OcclusionType.Raycast && type != SA.OcclusionType.Volumetric) throw new ArgumentOutOfRangeException(nameof(type));
            if (!Finite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (samples <= 0 || samples > MaxOcclusionSamples) throw new ArgumentOutOfRangeException(nameof(samples));
            if (!IsValid(handle)) return false;
            ref var input = ref slots[handle.Index].Inputs;
            input.OcclusionType = type; input.OcclusionRadius = radius; input.OcclusionSamples = samples;
            Invalidate(handle.Index);
            return true;
        }

        /// <summary>Removes a registration without releasing its precreated slot. Returns false for stale or foreign handles.</summary>
        public bool RemoveSource(SteamAudioSimulationSourceHandle handle)
        {
            EnsureUsable();
            if (!IsValid(handle)) return false;
            ref var slot = ref slots[handle.Index];
            SA.API.iplSourceRemove(slot.Source, simulator);
            slot.Active = false;
            Invalidate(handle.Index);
            SourceCount--; needsCommit = true;
            return true;
        }

        /// <summary>Updates the listener and invalidates every registered source result.</summary>
        public void SetListener(in SA.CoordinateSpace3 listener)
        {
            EnsureUsable(); ValidateSpace(listener);
            shared.Listener = listener;
            hasListener = true;
            InvalidateAll();
        }

        /// <summary>Invalidates results after the caller commits changed native geometry. Revisions must not rewind or wrap.</summary>
        public void InvalidateGeometry(ulong geometryRevision)
        {
            EnsureUsable();
            if (geometryRevision < revision) throw new ArgumentOutOfRangeException(nameof(geometryRevision));
            if (geometryRevision == revision) return;
            revision = geometryRevision;
            needsCommit = true;
            InvalidateAll();
        }

        /// <summary>
        /// Runs direct distance attenuation, air absorption and occlusion plus validated pathing, then copies
        /// results before returning. Time must be finite, nonnegative and monotonic. A finite native result
        /// does not establish probe influence coverage or physical reachability.
        /// </summary>
        public void Simulate(double simulationTime)
        {
            EnsureUsable();
            if (double.IsNaN(simulationTime) || double.IsInfinity(simulationTime) || simulationTime < time)
                throw new ArgumentOutOfRangeException(nameof(simulationTime));
            if (!hasListener) throw new InvalidOperationException("A listener must be configured before simulation.");
            time = simulationTime;
            PrepareSimulationInputs();
            SA.API.iplSimulatorRunDirect(simulator);
            BeginPathDistanceCapture();
            RunPathingWithDiagnostics();
            MarkDistanceModelsConsumed();
            CopySimulationResults();
        }

        void PrepareSimulationInputs()
        {
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active) ClearResult(i, SteamAudioPathSimulationStatus.Failed);
            if (needsCommit)
            {
                SA.API.iplSimulatorCommit(simulator);
                needsCommit = false;
            }
            NativePathSimulation.SetSharedInputs(simulator, Flags, ref shared);
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active) NativePathSimulation.SetInputs(slots[i].Source, Flags, ref slots[i].Inputs);
        }

        void BeginPathDistanceCapture()
        {
            debugCapture?.Clear();
            for (int i = 0; i < slots.Length; i++)
            {
                var state = slots[i].Distance;
                state.PathCount = state.NonzeroPathCount = 0;
                state.Diagnostics?.Clear();
                state.Shortest = float.PositiveInfinity;
                state.Longest = 0;
                state.CapturePathDistances = slots[i].Active;
            }
        }

        void RunPathingWithDiagnostics()
        {
            try
            {
                if (diagnosticsEnabled) bun3SteamAudioSetPathDiagnosticCallbackV1(DiagnosticPointer);
                SA.API.iplSimulatorRunPathing(simulator);
            }
            finally
            {
                if (diagnosticsEnabled) bun3SteamAudioSetPathDiagnosticCallbackV1(IntPtr.Zero);
                for (int i = 0; i < slots.Length; i++) slots[i].Distance.CapturePathDistances = false;
            }
        }

        void MarkDistanceModelsConsumed()
        {
            // Both synchronous runs have consumed the callback curve change.
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Active) slots[i].Inputs.Distance.Dirty = 0;
        }

        void CopySimulationResults()
        {
            for (int i = 0; i < slots.Length; i++)
            {
                if (!slots[i].Active) continue;
                NativePathSimulation.GetOutputs(slots[i].Source, Flags, out var output);
                if (!HasReadableOutput(output)) continue;
                int offset = i * CoefficientCount;
                Marshal.Copy(output.Pathing.Coefficients, coefficients, offset, CoefficientCount);
                bool valid = true, signal = false;
                for (int j = 0; j < CoefficientCount; j++)
                {
                    float value = coefficients[offset + j];
                    valid &= Finite(value);
                    signal |= value != 0;
                }
                if (!valid)
                {
                    ClearResult(i, SteamAudioPathSimulationStatus.Failed);
                    continue;
                }
                signal &= output.Pathing.EqLow != 0 || output.Pathing.EqMid != 0 || output.Pathing.EqHigh != 0;
                slots[i].Result = new SteamAudioPathSimulationResult(SteamAudioPathSimulationStatus.Valid, revision, time, output, signal);
            }
        }

        static bool HasReadableOutput(in NativePathSimulation.Outputs output)
        {
            return Finite(output.Direct.DistanceAttenuation) && Finite(output.Direct.Occlusion) &&
                Finite(output.Direct.AirLow) && Finite(output.Direct.AirMid) && Finite(output.Direct.AirHigh) &&
                Finite(output.Pathing.EqLow) && Finite(output.Pathing.EqMid) && Finite(output.Pathing.EqHigh) &&
                output.Pathing.Coefficients != IntPtr.Zero;
        }

        /// <summary>
        /// Copies into an exact-size caller-owned SH array and returns scalars. Pending or failed results
        /// copy silence, without interpreting that silence as a geometric block. Returns false for invalid handles.
        /// </summary>
        public bool TryCopyResult(SteamAudioSimulationSourceHandle handle, float[] destination, out SteamAudioPathSimulationResult result)
        {
            EnsureUsable();
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (destination.Length != CoefficientCount) throw new ArgumentException("Coefficient array length must match the scope.", nameof(destination));
            result = default;
            if (!IsValid(handle)) { Array.Clear(destination, 0, destination.Length); return false; }
            Array.Copy(coefficients, handle.Index * CoefficientCount, destination, 0, CoefficientCount);
            result = slots[handle.Index].Result;
            return true;
        }

        internal bool IsValid(SteamAudioSimulationSourceHandle handle)
        {
            EnsureThread();
            return !disposed && ReferenceEquals(handle.Owner, this) && handle.Index >= 0 && handle.Index < slots.Length &&
                slots[handle.Index].Active && slots[handle.Index].Generation == handle.Generation;
        }
        void InvalidateAll() { for (int i = 0; i < slots.Length; i++) if (slots[i].Active) Invalidate(i); }
        void Invalidate(int index) => ClearResult(index, SteamAudioPathSimulationStatus.Pending);
        void ClearResult(int index, SteamAudioPathSimulationStatus status)
        {
            slots[index].Result = new SteamAudioPathSimulationResult(status, revision, time);
            Array.Clear(coefficients, index * CoefficientCount, CoefficientCount);
        }
        void EnsureThread()
        {
            if (Environment.CurrentManagedThreadId != ownerThread)
                throw new InvalidOperationException("Simulation access must remain on its creating control thread.");
        }
        void EnsureUsable()
        {
            EnsureThread();
            if (disposed) throw new ObjectDisposedException(nameof(SteamAudioPathSimulationScope));
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static bool Finite(SA.Vector3 value) => Finite(value.x) && Finite(value.y) && Finite(value.z);
        static double Dot(SA.Vector3 a, SA.Vector3 b) => (double)a.x * b.x + (double)a.y * b.y + (double)a.z * b.z;
        static void ValidateSpace(SA.CoordinateSpace3 value)
        {
            if (!Finite(value.origin) || !Finite(value.right) || !Finite(value.up) || !Finite(value.ahead) ||
                Math.Abs(Dot(value.right, value.right) - 1) > 0.001 || Math.Abs(Dot(value.up, value.up) - 1) > 0.001 ||
                Math.Abs(Dot(value.ahead, value.ahead) - 1) > 0.001 || Math.Abs(Dot(value.right, value.up)) > 0.001 ||
                Math.Abs(Dot(value.right, value.ahead)) > 0.001 || Math.Abs(Dot(value.up, value.ahead)) > 0.001)
                throw new ArgumentException("Coordinates must be finite with orthonormal orientation vectors.", nameof(value));
        }
        static void Check(SA.Error error)
        {
            if (error != SA.Error.Success) throw new InvalidOperationException("Steam Audio could not allocate simulation resources.");
        }

        /// <summary>Releases every owned source and retained native reference. Repeated disposal on the creating thread is harmless.</summary>
        public void Dispose()
        {
            EnsureThread();
            if (disposed) return;
            disposed = true;
            ReleaseNative();
            SourceCount = 0;
        }
        void ReleaseNative()
        {
            if (simulator != IntPtr.Zero)
            {
                for (int i = 0; i < slots.Length; i++)
                    if (slots[i].Active) { SA.API.iplSourceRemove(slots[i].Source, simulator); slots[i].Active = false; }
                SA.API.iplSimulatorCommit(simulator);
            }
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].Source != IntPtr.Zero) SA.API.iplSourceRelease(ref slots[i].Source);
            if (simulator != IntPtr.Zero) SA.API.iplSimulatorRelease(ref simulator);
            if (debugRoot.IsAllocated) debugRoot.Free();
            // Native sources/simulator can no longer invoke their rooted per-slot callbacks.
            for (int i = 0; i < slots.Length; i++)
                if (slots[i].DistanceRoot.IsAllocated) slots[i].DistanceRoot.Free();
            if (probes != IntPtr.Zero) SA.API.iplProbeBatchRelease(ref probes);
            if (scene != IntPtr.Zero) SA.API.iplSceneRelease(ref scene);
            if (context != IntPtr.Zero) SA.API.iplContextRelease(ref context);
        }
    }
}
