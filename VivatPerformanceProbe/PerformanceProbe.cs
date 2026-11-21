using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Vivat.Systems.BurstOcclusion;

namespace VivatPopulationMod;

[BepInPlugin("local.vivat.performanceprobe", "Vivat Performance Probe", "0.1.11")]
public sealed class PerformanceProbePlugin : BasePlugin
{
    private Harmony harmony;
    private PerformanceProbe probe;

    public override void Load()
    {
        harmony = new Harmony("local.vivat.performanceprobe");
        Watch(typeof(OcclusionManager), "Update");
        Watch(typeof(ChunkActivator), "Update");
        Watch(typeof(DriverAI), "Sensors");
        probe = AddComponent<PerformanceProbe>();
        probe.Initialize(Log, Config.Bind("Road", "SharperMarkings", true,
            "Ostřejší vzorkování ověřených barevných textur EasyRoads. Změna po restartu.").Value);
        Log.LogInfo("F9 spustí dvacetisekundové měření výkonu.");
    }

    private void Watch(Type type, string method, params Type[] parameters)
    {
        var target = AccessTools.DeclaredMethod(type, method, parameters);
        if (target == null)
        {
            Log.LogWarning($"Měřená metoda chybí: {type.Name}.{method}");
            return;
        }

        try
        {
            harmony.Patch(target,
                prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PerformanceProbe), nameof(PerformanceProbe.Begin))),
                postfix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(PerformanceProbe), nameof(PerformanceProbe.End))));
            PerformanceProbe.Register(target);
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Měření {type.Name}.{method} se nepřipojilo: {exception.Message}");
        }
    }

    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        if (probe != null)
            UnityEngine.Object.Destroy(probe);
        return true;
    }
}

public sealed class PerformanceProbe : MonoBehaviour
{
    private sealed class Sample
    {
        internal int Calls;
        internal double TotalMs;
        internal double MaxMs;
    }

    private static readonly Dictionary<MethodBase, Sample> Methods = new();
    private static bool recording;
    private readonly List<float> frames = new(2500);
    private ManualLogSource log;
    private Il2CppStructArray<FrameTiming> timings;
    private float stopAt;
    private ulong lastTimestamp;
    private int timingSamples;
    private int gpuSamples;
    private double gpuMs;
    private double cpuMainMs;
    private double cpuPresentWaitMs;
    private static readonly HashSet<string> RoadTextureNames = new(StringComparer.Ordinal)
    {
        "EasyRoads_Cesta_2Lane_albedo",
        "EasyRoads_Cesta_2Lane_albedo-alpha",
        "EasyRoads_Cesta_2Lane_Parking_albedo",
        "EasyRoads_Cesta_2Lane_Tram_albedo",
        "EasyRoads_Cesta_2Lane_Tram_albedo-alpha",
        "EasyRoads_Cesta_2Lane_Tram_Crossing_albedo-alpha",
        "EasyRoads_Cesta_2Lane_Tram_CrossingLines_albedo",
        "EasyRoads_Cesta_2Lane_Tram_CrossingLines_albedo-alpha",
        "EasyRoads_Cesta_4Lane_base",
        "EasyRoads_Cesta_4Lane_base-alpha",
        "EasyRoads_Cesta_4Lane_Crossing_base",
        "EasyRoads_Cesta_4Lane_Crossing_base-alpha",
        "EasyRoads_Cesta_clean_albedo",
        "EasyRoads_Cesta_clean_albedo-alpha",
        "Hlavna_4pruhy_rozdelena_base",
        "Hlavna_4pruhy_rozdelena_base-alpha",
        "Hlavna_4pruhy_rozdelena_krizovatka_base"
    };
    private readonly List<(Texture2D Texture, int Aniso, float Bias, FilterMode Filter)> sharpenedRoad = new();
    private readonly HashSet<string> sharpenedRoadNames = new(StringComparer.Ordinal);
    private const float RoadMipBias = -1.5f;
    private bool sharpenMarkings;
    private float nextRoadCheck;
    private double cpuRenderMs;

    public PerformanceProbe(IntPtr pointer) : base(pointer) { }

    internal void Initialize(ManualLogSource source, bool sharperMarkings)
    {
        log = source;
        sharpenMarkings = sharperMarkings;
    }

    internal static void Register(MethodBase method) => Methods[method] = new Sample();

    internal static void Begin(out long __state) => __state = recording ? Stopwatch.GetTimestamp() : 0;

    internal static void End(MethodBase __originalMethod, long __state)
    {
        if (__state == 0 || !Methods.TryGetValue(__originalMethod, out var sample))
            return;

        double ms = (Stopwatch.GetTimestamp() - __state) * (1000d / Stopwatch.Frequency);
        sample.Calls++;
        sample.TotalMs += ms;
        sample.MaxMs = Math.Max(sample.MaxMs, ms);
    }

    private void Update()
    {
        if (sharpenMarkings && !recording &&
            Time.unscaledTime >= nextRoadCheck)
        {
            nextRoadCheck = Time.unscaledTime + 15f;
            ApplyRoadSampling();
        }

        var keyboard = Keyboard.current;
        if (!recording)
        {
            if (keyboard != null && keyboard.f9Key.wasPressedThisFrame)
                StartCapture();
            return;
        }

        frames.Add(Time.unscaledDeltaTime * 1000f);
        if (timings != null)
            CaptureTiming();
        if (Time.realtimeSinceStartup >= stopAt)
            FinishCapture();
    }

    private void ApplyRoadSampling()
    {
        foreach (var (texture, _, _, _) in sharpenedRoad)
        {
            if (texture == null || (texture.anisoLevel == 16 && texture.mipMapBias <= RoadMipBias && texture.filterMode == FilterMode.Trilinear))
                continue;

            texture.anisoLevel = 16;
            texture.filterMode = FilterMode.Trilinear;
            texture.mipMapBias = Mathf.Min(texture.mipMapBias, RoadMipBias);
            log.LogInfo($"Ostrost vozovky obnovena: {texture.name}.");
        }

        if (sharpenedRoadNames.Count == RoadTextureNames.Count)
            return;

        foreach (var texture in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (!RoadTextureNames.Contains(texture.name) || sharpenedRoad.Any(item => item.Texture == texture))
                continue;

            sharpenedRoad.Add((texture, texture.anisoLevel, texture.mipMapBias, texture.filterMode));
            texture.anisoLevel = 16;
            texture.filterMode = FilterMode.Trilinear;
            texture.mipMapBias = Mathf.Min(texture.mipMapBias, RoadMipBias);
            sharpenedRoadNames.Add(texture.name);
            log.LogInfo($"Ostrost vozovky: {texture.name}, aniso={texture.anisoLevel}, bias={texture.mipMapBias:F2}, filtr={texture.filterMode}.");
        }
    }

    private void StartCapture()
    {
        frames.Clear();
        timings = null;
        lastTimestamp = 0;
        timingSamples = 0;
        gpuSamples = 0;
        gpuMs = cpuMainMs = cpuPresentWaitMs = cpuRenderMs = 0;
        foreach (var sample in Methods.Values)
        {
            sample.Calls = 0;
            sample.TotalMs = 0;
            sample.MaxMs = 0;
        }

        log.LogInfo($"Profil výkonu začal. VSync={QualitySettings.vSyncCount}, limit={Application.targetFrameRate}.");
        try
        {
            log.LogInfo($"GPU Resident Drawer={GPUResidentDrawer.IsEnabled()}, " +
                        $"GPU occlusion={GPUResidentDrawer.IsInstanceOcclusionCullingEnabled()}.");
        }
        catch (Exception exception)
        {
            log.LogWarning($"Stav GPU occlusion nelze přečíst: {exception.Message}");
        }

        try
        {
            if (FrameTimingManager.IsFeatureEnabled())
                timings = new Il2CppStructArray<FrameTiming>(1);
        }
        catch (Exception exception)
        {
            log.LogWarning($"Unity FrameTiming není dostupné: {exception.Message}");
        }
        stopAt = Time.realtimeSinceStartup + 20f;
        recording = true;
    }

    private void CaptureTiming()
    {
        try
        {
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, timings) == 0)
                return;

            var frame = timings[0];
            if (frame.frameStartTimestamp == lastTimestamp)
                return;

            lastTimestamp = frame.frameStartTimestamp;
            timingSamples++;
            cpuMainMs += frame.cpuMainThreadFrameTime;
            cpuPresentWaitMs += frame.cpuMainThreadPresentWaitTime;
            cpuRenderMs += frame.cpuRenderThreadFrameTime;
            if (double.IsFinite(frame.gpuFrameTime) && frame.gpuFrameTime > 0)
            {
                gpuMs += frame.gpuFrameTime;
                gpuSamples++;
            }
        }
        catch (Exception exception)
        {
            timings = null;
            log.LogWarning($"Unity FrameTiming přerušeno: {exception.Message}");
        }
    }

    private void FinishCapture()
    {
        recording = false;
        frames.Sort();
        int count = frames.Count;
        log.LogInfo($"Profil výkonu: {count} snímků, P95={frames[(int)((count - 1) * 0.95f)]:F1} ms, " +
                    $"maximum={frames[count - 1]:F1} ms.");
        if (timingSamples > 0)
            log.LogInfo($"Unity FrameTiming ({timingSamples} vzorků): hlavní vlákno {cpuMainMs / timingSamples:F2} ms, " +
                        $"čekání na present {cpuPresentWaitMs / timingSamples:F2} ms, render vlákno {cpuRenderMs / timingSamples:F2} ms.");
        if (gpuSamples > 0)
            log.LogInfo($"GPU čas snímku: {gpuMs / gpuSamples:F2} ms ({gpuSamples} vzorků).");
        else
            log.LogInfo("Unity neposkytlo platné GPU časy.");

        foreach (var pair in Methods)
        {
            var sample = pair.Value;
            log.LogInfo($"{pair.Key.DeclaringType?.Name}.{pair.Key.Name}: {sample.Calls} volání, " +
                        $"{sample.TotalMs / count:F3} ms na snímek, maximum volání {sample.MaxMs:F2} ms.");
        }
    }

    private void OnDestroy()
    {
        foreach (var (texture, aniso, bias, filter) in sharpenedRoad)
        {
            if (texture == null)
                continue;
            texture.anisoLevel = aniso;
            texture.mipMapBias = bias;
            texture.filterMode = filter;
        }
        recording = false;
        Methods.Clear();
    }
}
