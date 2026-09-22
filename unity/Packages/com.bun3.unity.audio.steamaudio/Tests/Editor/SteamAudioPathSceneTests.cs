using System;
using System.Runtime.InteropServices;
using NUnit.Framework;
using SteamAudio;

namespace Bun3.Unity.Audio.SteamAudio.Tests
{
    public class SteamAudioPathSceneTests
    {
        // Steam Audio 4.8.1 path_data.cpp invokes this callback without a null check.
        private static readonly ProgressCallback BakeProgress = (_, __) => { };

        [DllImport("phonon", CallingConvention = CallingConvention.Winapi)]
        private static extern void iplSourceGetOutputs(IntPtr source, SimulationFlags flags, IntPtr outputs);

        [Test]
        public void BakedPathAroundWallRespondsToDoorClosingAndReopening()
        {
            Context context = null;
            Scene scene = null;
            StaticMesh wall = null;
            StaticMesh door = null;
            ProbeBatch probes = null;
            Simulator simulator = null;
            Source source = null;
            IntPtr outputMemory = IntPtr.Zero;
            try
            {
                context = new Context();
                scene = new Scene(context, SceneType.Default, null, null, null, null);
                wall = Panel(context, scene, -10, 2);
                wall.AddToScene(scene);
                scene.Commit();
                probes = new ProbeBatch(context);
                foreach (var point in new[] { V(-2, 0, 0), V(-2, 0, 3), V(0, 0, 3), V(2, 0, 3), V(2, 0, 0) })
                    probes.AddProbe(new Sphere { center = point, radius = 0.6f });
                probes.Commit();
                var bake = new PathBakeParams
                {
                    scene = scene.Get(), probeBatch = probes.Get(),
                    identifier = new BakedDataIdentifier { type = BakedDataType.Pathing, variation = BakedDataVariation.Dynamic },
                    numSamples = 1, radius = 0.05f, threshold = 0.99f, visRange = 4.5f, pathRange = 20, numThreads = 1
                };
                API.iplPathBakerBake(context.Get(), ref bake, BakeProgress, IntPtr.Zero);
                var flags = SimulationFlags.Direct | SimulationFlags.Pathing;
                var settings = new SimulationSettings
                {
                    flags = flags, sceneType = SceneType.Default, maxNumOcclusionSamples = 1,
                    maxNumSources = 1, numThreads = 1, numVisSamples = 1,
                    maxOrder = 1, samplingRate = 48000, frameSize = 512
                };
                simulator = new Simulator(context, settings);
                simulator.SetScene(scene);
                simulator.AddProbeBatch(probes);
                source = new Source(simulator, settings);
                source.AddToSimulator(simulator);
                simulator.Commit();
                simulator.SetSharedInputs(flags, new SimulationSharedInputs { listener = Space(2, 0, 0) });
                source.SetInputs(flags, new SimulationInputs
                {
                    flags = flags, directFlags = DirectSimulationFlags.Occlusion,
                    source = Space(-2, 0, 0), occlusionType = OcclusionType.Raycast,
                    numOcclusionSamples = 1, pathingProbes = probes.Get(),
                    visRadius = 0.05f, visThreshold = 0.99f, visRange = 4.5f,
                    pathingOrder = 1, enableValidation = Bool.True, findAlternatePaths = Bool.True
                });
                // The 4.8.1 native path struct has a trailing normalizeEQ field absent from the Unity prefix.
                outputMemory = Marshal.AllocHGlobal(Marshal.SizeOf<SimulationOutputs>() + 16);
                var open = Run(simulator, source, flags, outputMemory);
                Assert.That(open.direct.occlusion, Is.LessThan(0.01f), "The direct ray must be blocked by the wall.");
                float openEnergy = DirectionEnergy(open.pathing);
                Assert.That(openEnergy, Is.GreaterThan(0.000001f), "An indirect route must exist around the wall endpoint.");
                double openPcm = RenderEnergy(context, open.pathing);
                Assert.That(openPcm, Is.GreaterThan(1e-8), "The simulated route must produce actual binaural PCM.");

                door = Panel(context, scene, 2, 10);
                door.AddToScene(scene);
                scene.Commit();
                simulator.Commit();
                var closed = Run(simulator, source, flags, outputMemory);
                Assert.That(DirectionEnergy(closed.pathing), Is.LessThan(openEnergy * 0.01f), "Closing the opening must invalidate the baked route.");
                Assert.That(RenderEnergy(context, closed.pathing), Is.LessThan(openPcm * 0.0001), "A closed route must suppress the rendered signal.");

                door.RemoveFromScene(scene);
                scene.Commit();
                simulator.Commit();
                var reopened = Run(simulator, source, flags, outputMemory);
                Assert.That(DirectionEnergy(reopened.pathing), Is.GreaterThan(openEnergy * 0.5f));
                Assert.That(RenderEnergy(context, reopened.pathing), Is.GreaterThan(openPcm * 0.5));
            }
            finally
            {
                if (outputMemory != IntPtr.Zero) Marshal.FreeHGlobal(outputMemory);
                source?.Release();
                simulator?.Release();
                probes?.Release();
                door?.Release();
                wall?.Release();
                scene?.Release();
                context?.Release();
            }
        }

        private static SimulationOutputs Run(Simulator simulator, Source source, SimulationFlags flags, IntPtr memory)
        {
            simulator.RunDirect();
            simulator.RunPathing();
            iplSourceGetOutputs(source.Get(), flags, memory);
            return Marshal.PtrToStructure<SimulationOutputs>(memory);
        }

        private static float DirectionEnergy(PathEffectParams path)
        {
            if (path.shCoeffs == IntPtr.Zero) return 0;
            var coefficients = new float[4];
            Marshal.Copy(path.shCoeffs, coefficients, 0, coefficients.Length);
            float energy = 0;
            foreach (float coefficient in coefficients) energy += coefficient * coefficient;
            return energy;
        }

        private static double RenderEnergy(Context context, PathEffectParams path)
        {
            var hrtf = new HRTF(context, new AudioSettings { samplingRate = 48000, frameSize = 512 }, null, null, 0, HRTFNormType.None);
            try
            {
                using var renderer = new SteamAudioPathRenderer(context, hrtf, 48000, 512);
                var mono = new float[512];
                var stereo = new float[1024];
                var coefficients = new float[4];
                if (path.shCoeffs != IntPtr.Zero) Marshal.Copy(path.shCoeffs, coefficients, 0, coefficients.Length);
                for (int i = 0; i < mono.Length; i++) mono[i] = (float)Math.Sin(i * 0.17) * 0.1f;
                for (int i = 0; i < 8; i++)
                    renderer.Render(mono, stereo, coefficients, path.eqCoeffsLow, path.eqCoeffsMid, path.eqCoeffsHigh, Space(2, 0, 0));
                double energy = 0;
                foreach (float sample in stereo)
                {
                    Assert.That(float.IsNaN(sample) || float.IsInfinity(sample), Is.False);
                    energy += (double)sample * sample;
                }
                return energy;
            }
            finally { hrtf.Release(); }
        }

        private static StaticMesh Panel(Context context, Scene scene, float start, float end)
        {
            return new StaticMesh(context, scene,
                new[] { V(0, -3, start), V(0, 3, start), V(0, 3, end), V(0, -3, end) },
                new[] { new Triangle { index0 = 0, index1 = 1, index2 = 2 }, new Triangle { index0 = 0, index1 = 2, index2 = 3 } },
                new[] { 0, 0 }, new[] { new Material { absorptionLow = 1, absorptionMid = 1, absorptionHigh = 1 } });
        }

        private static Vector3 V(float x, float y, float z) => new Vector3 { x = x, y = y, z = z };
        private static CoordinateSpace3 Space(float x, float y, float z) => new CoordinateSpace3
        {
            origin = V(x, y, z), right = V(1, 0, 0), up = V(0, 1, 0), ahead = V(0, 0, -1)
        };
    }
}
