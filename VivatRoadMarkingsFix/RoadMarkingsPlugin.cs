using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace VivatRoadMarkingsFix;

[BepInPlugin("local.vivat.roadmarkingsfix", "Vivat Road Markings Fix", "1.1.0")]
public sealed class RoadMarkingsPlugin : BasePlugin
{
    private RoadMarkingsController controller;

    public override void Load()
    {
        bool verboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false,
            "Podrobne vypisy o uprave textur. Chyby a varovani zustavaji zapnute. Restart hry.").Value;
        bool enabled = Config.Bind("RoadMarkings", "Enabled", true,
            "Doostri vybrane textury silnicniho znaceni. Zmena vyzaduje restart hry.").Value;
        if (!enabled)
        {
            if (verboseLogging)
                Log.LogInfo("Road markings fix je vypnuty konfiguraci.");
            return;
        }

        int anisotropy = Config.Bind("RoadMarkings", "Anisotropy", 16,
            new ConfigDescription("Anizotropni filtrovani vybranych textur (1-16).",
                new AcceptableValueRange<int>(1, 16))).Value;
        float mipBias = Config.Bind("RoadMarkings", "MipBias", -1.5f,
            new ConfigDescription("Posun mipmap pro ostrejsi znacky. Nizsi hodnota = ostrejsi, ale mozne trpytani.",
                new AcceptableValueRange<float>(-3f, 0f))).Value;
        // New config key: old ScanIntervalSeconds=30 from existing cfg files must not
        // silently keep a 30-second delay for newly streamed road textures.
        float refreshInterval = Config.Bind("RoadMarkings", "RefreshIntervalSeconds", 2f,
            new ConfigDescription("Kontrola nove nactenych textur (sekundy). Prvni kontrola probiha okamzite. Mensi interval muze zvysit CPU zatez.",
                new AcceptableValueRange<float>(1f, 10f))).Value;

        controller = AddComponent<RoadMarkingsController>();
        controller.Initialize(Log, anisotropy, mipBias, refreshInterval, verboseLogging);
        if (verboseLogging)
            Log.LogInfo($"Road markings fix zapnuty (aniso={anisotropy}, mipBias={mipBias:F2}).");
    }

    public override bool Unload()
    {
        if (controller != null)
        {
            controller.RestoreOriginalSettings();
            UnityEngine.Object.Destroy(controller);
            controller = null;
        }
        return true;
    }
}

public sealed class RoadMarkingsController : MonoBehaviour
{
    // Explicit whitelist: never alter unrelated textures or global quality settings.
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

    private sealed class OriginalSettings
    {
        internal Texture2D Texture;
        internal int Anisotropy;
        internal float MipBias;
        internal FilterMode Filter;
    }

    private readonly Dictionary<int, OriginalSettings> originals = new();
    private ManualLogSource log;
    private bool verboseLogging;
    private int targetAnisotropy;
    private float targetMipBias;
    private float scanInterval;
    private float nextScan;

    public RoadMarkingsController(IntPtr pointer) : base(pointer) { }

    internal void Initialize(ManualLogSource logger, int anisotropy, float mipBias, float interval, bool enableVerboseLogging)
    {
        log = logger;
        verboseLogging = enableVerboseLogging;
        targetAnisotropy = anisotropy;
        targetMipBias = mipBias;
        scanInterval = interval;
        // Apply immediately, then repeat on the first Unity update to catch textures
        // loaded after the plugin initializes. Subsequent checks use the short interval.
        nextScan = 0f;
        ScanTextures();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextScan)
            return;

        nextScan = Time.unscaledTime + scanInterval;
        ScanTextures();
    }

    private void ScanTextures()
    {
        try
        {
            ApplyToLoadedTextures();
        }
        catch (Exception exception)
        {
            log?.LogWarning($"Road markings: kontrola textur selhala: {exception.Message}");
        }
    }

    private void ApplyToLoadedTextures()
    {
        // Streamed-out Unity textures are destroyed; discard stale snapshots.
        var expired = new List<int>();
        foreach (var entry in originals)
        {
            if (entry.Value.Texture == null)
                expired.Add(entry.Key);
        }
        foreach (int id in expired)
            originals.Remove(id);

        int newlyAdjusted = 0;
        foreach (Texture2D texture in Resources.FindObjectsOfTypeAll<Texture2D>())
        {
            if (texture == null || !RoadTextureNames.Contains(texture.name) || texture.mipmapCount <= 1)
                continue;

            int id = texture.GetInstanceID();
            if (!originals.TryGetValue(id, out var original))
            {
                original = new OriginalSettings
                {
                    Texture = texture,
                    Anisotropy = texture.anisoLevel,
                    MipBias = texture.mipMapBias,
                    Filter = texture.filterMode
                };
                originals.Add(id, original);
                newlyAdjusted++;
            }

            // Restore requested settings if the game changes them after streaming.
            if (texture.anisoLevel < targetAnisotropy)
                texture.anisoLevel = targetAnisotropy;
            if (texture.filterMode != FilterMode.Trilinear)
                texture.filterMode = FilterMode.Trilinear;
            if (texture.mipMapBias > targetMipBias)
                texture.mipMapBias = targetMipBias;
        }

        if (verboseLogging && newlyAdjusted > 0)
            log?.LogDebug($"Road markings: upraveno {newlyAdjusted} novych textur, sledovano {originals.Count}.");
    }

    internal void RestoreOriginalSettings()
    {
        foreach (OriginalSettings original in originals.Values)
        {
            if (original.Texture == null)
                continue;

            try
            {
                original.Texture.anisoLevel = original.Anisotropy;
                original.Texture.mipMapBias = original.MipBias;
                original.Texture.filterMode = original.Filter;
            }
            catch (Exception exception)
            {
                log?.LogWarning($"Road markings: obnova textury selhala: {exception.Message}");
            }
        }
        originals.Clear();
    }

    private void OnDestroy() => RestoreOriginalSettings();
}
