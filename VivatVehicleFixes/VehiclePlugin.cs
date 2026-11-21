using System;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using Vivat.Systems.WantedSystem;

namespace VivatPopulationMod;

[BepInPlugin(PluginId, "Vivat Vehicle Fixes", "0.1.14")]
public sealed class VehiclePlugin : BasePlugin
{
    private const string PluginId = "local.vivat.vehiclefixes";
    private static ManualLogSource patchLog;
    private static bool verboseLogging;
    private static int detachedReported;
    private static int sensorReported;
    private static float sensorMultiplier;
    private Harmony debrisPatch;
    private Harmony sensorPatch;
    private Harmony tracePatch;
    private Harmony partDamagePatch;
    private Harmony streamingPatch;
    private Harmony detailPatch;
    private Harmony decalPatch;
    private Harmony decalMipPatch;
    private Harmony vehicleDetailPatch;
    private static float vehicleDetailMultiplier;
    private static int vehicleDetailReported;
    private static float decalMipMultiplier;
    private static int decalMipReported;
    private static int decalDistance;
    private static int decalDistanceReported;
    private Harmony populationRangePatch;
    private Harmony trafficSpeedPatch;
    private Harmony impatientTrafficPatch;
    private Harmony policeCrashPatch;
    private static float cleanupIntervalMultiplier;
    private static float detailMultiplier;
    private static float populationRangeMultiplier;
    private static float trafficSpeedMultiplier;
    private static bool trafficSpeedReported;
    private static bool carRangeReported;
    private static bool pedestrianRangeReported;
    private static float nextCollisionTrace;

    public override void Load()
    {
        patchLog = Log;
        verboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false,
            "Podrobne informacni vypisy. Chyby a varovani zustavaji zapnute. Restart hry.").Value;
        bool logPartDamage = Config.Bind("Diagnostics", "LogPartDamage", false,
            "Loguje preskocene poskozeni a prasknuti kloubu dilu. Filtr poskozeni zustava aktivni. Restart hry.").Value;
        detachedReported = 0;
        sensorReported = 0;
        nextCollisionTrace = 0f;
        carRangeReported = false;
        pedestrianRangeReported = false;
        trafficSpeedReported = false;
        decalDistanceReported = 0;
        decalMipReported = 0;
        vehicleDetailReported = 0;
        vehicleDetailMultiplier = ReadVisibilityMultiplier("VehicleDetailMultiplier", 1.25f,
            "Nasobek dosahu modelu aut a svetel, ktera jsou soucasti jejich LOD.");
        if (vehicleDetailMultiplier > 1f)
        {
            vehicleDetailPatch = Install("vehicle-detail", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(CarController), "Start", Type.EmptyTypes),
                nameof(AdjustVehicleDetail), prefix: false));
        }
        decalMipMultiplier = Config.Bind("Visibility", "DecalMipDistanceMultiplier", 1.5f,
            new ConfigDescription("Nasobek prahu detailu textur decalu. 1 = puvodni. Zmena po restartu.",
                new AcceptableValueRange<float>(1f, 2f))).Value;
        if (decalMipMultiplier > 1f)
        {
            decalMipPatch = Install("decal-mips", harmony =>
            {
                var target = AccessTools.DeclaredMethod(typeof(DecalStreamingManager),
                    "SetMipLevelEdgesInShader", new[] { typeof(ComputeShader) });
                if (target == null)
                    throw new MissingMethodException("DecalStreamingManager.SetMipLevelEdgesInShader");

                harmony.Patch(target,
                    prefix: new HarmonyMethod(typeof(VehiclePlugin), nameof(ScaleDecalMipEdges)),
                    finalizer: new HarmonyMethod(typeof(VehiclePlugin), nameof(RestoreDecalMipEdges)));
            });
        }

        decalDistance = Config.Bind("Visibility", "DecalDrawDistance", 150,
            new ConfigDescription("Minimalni dosah HDRP decalů v metrech. 0 vypne upravu. Zmena po restartu.",
                new AcceptableValueRange<int>(0, 200))).Value;
        if (decalDistance > 0)
        {
            decalPatch = Install("decal-distance", harmony =>
            {
                var decalSystem = Type.GetType("UnityEngine.Rendering.HighDefinition.DecalSystem, Unity.RenderPipelines.HighDefinition.Runtime");
                if (decalSystem == null)
                    throw new TypeLoadException("HDRP DecalSystem");

                Patch(harmony, AccessTools.DeclaredPropertyGetter(decalSystem, "DrawDistance"),
                    nameof(AdjustDecalDistance), prefix: false);
            });
        }

        if (Config.Bind("Collision", "FilterSpuriousPartDamage", true,
                "Omezi poskozeni dilu po kontaktech bez impulsu a po pomalem hlubokem pruniku.").Value)
        {
            partDamagePatch = Install("part-damage", harmony => CollisionPartDamage.Install(harmony, logPartDamage));
        }
        if (Config.Bind("Collision", "IgnoreDetachedPartReports", true,
                "Oddeleny dil nehlasi vlastni naraz puvodnimu autu.").Value)
        {
            debrisPatch = Install("debris", harmony =>
            {
                Patch(harmony, AccessTools.DeclaredMethod(typeof(NotifyCarParentOnCollision),
                    "OnCollisionEnter", new[] { typeof(Collision) }), nameof(ReportAttachedPartOnly), prefix: true);
                Patch(harmony, AccessTools.DeclaredMethod(typeof(CarController),
                    "OnChildCollisionEnter", new[] { typeof(Collision), typeof(Transform) }),
                    nameof(ProcessAttachedChildOnly), prefix: true);
            });
        }

        sensorMultiplier = Config.Bind("Traffic", "SensorIntervalMultiplier", 1.25f, new ConfigDescription(
            "Interval senzoru aut. 1 = puvodni. Vyssi hodnota muze zpomalit reakce aut. Zmena po restartu.",
            new AcceptableValueRange<float>(1f, 1.5f))).Value;
        if (sensorMultiplier > 1f)
        {
            sensorPatch = Install("sensors", harmony => Patch(harmony,
                AccessTools.DeclaredPropertyGetter(typeof(WorldManager), "CarSensorInterval"),
                nameof(AdjustSensorInterval), prefix: false));
        }

        bool tracePlayerContacts = Config.Bind("Collision", "TracePlayerContacts", false,
            "Loguje kontakty hracova auta jen pri zapnutem Diagnostics.VerboseLogging.").Value;
        if (verboseLogging && tracePlayerContacts)
        {
            tracePatch = Install("trace", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(CarController), "OnCollision",
                    new[] { typeof(Collision), typeof(bool) }),
                nameof(TracePlayerContact), prefix: true));
        }

        cleanupIntervalMultiplier = Config.Bind("Streaming", "AssetCleanupIntervalMultiplier", 2f,
            new ConfigDescription(
                "Interval periodickeho uklidu assetu. 1 = puvodni. Vyssi hodnota muze zvysit spotrebu pameti.",
                new AcceptableValueRange<float>(1f, 2f))).Value;
        if (cleanupIntervalMultiplier > 1f)
        {
            streamingPatch = Install("streaming", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(SceneLoader), "Awake", Type.EmptyTypes),
                nameof(AdjustAssetCleanupInterval), prefix: false));
        }

        detailMultiplier = ReadVisibilityMultiplier("DetailMultiplier", 1.15f,
            "Nasobek vzdalenosti pro detailnejsi Unity LOD modely.");
        if (detailMultiplier > 1f)
        {
            detailPatch = Install("detail", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(GraphicsOptionsManager), "SetLodBias", new[] { typeof(float) }),
                nameof(AdjustDetail), prefix: false));
        }

        populationRangeMultiplier = ReadVisibilityMultiplier("PopulationRangeMultiplier", 1.10f,
            "Nasobek maximalni vzdalenosti spawneru beznych aut a chodcu.");
        if (populationRangeMultiplier > 1f)
        {
            populationRangePatch = Install("population-range", harmony =>
            {
                Patch(harmony, AccessTools.DeclaredPropertyGetter(typeof(WorldManager), "CarSpawnerMaxDistance"),
                    nameof(AdjustCarRange), prefix: false);
                Patch(harmony, AccessTools.DeclaredPropertyGetter(typeof(WorldManager), "PadestrianSpawnerMaxDistance"),
                    nameof(AdjustPedestrianRange), prefix: false);
            });
        }

        trafficSpeedMultiplier = Config.Bind("Traffic", "CitySpeedMultiplier", 0.75f,
            new ConfigDescription(
                "Cilova rychlost bezneho provozu na silnicni siti. 1 = puvodni. Zmena po restartu.",
                new AcceptableValueRange<float>(0.5f, 1f))).Value;
        if (trafficSpeedMultiplier < 1f)
        {
            trafficSpeedPatch = Install("traffic-speed", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(DriverAI), "GetDesiredSpeed", Type.EmptyTypes),
                nameof(AdjustTrafficSpeed), prefix: false));
        }

        if (Config.Bind("Traffic", "ImpatientDrivers", true,
                "Po 5 sekundach stani zkusi NPC popojet nebo pomalu objet nehybne auto, pokud ma volno. Zmena po restartu.").Value)
        {
            impatientTrafficPatch = Install("impatient-traffic", harmony => ImpatientTraffic.Install(harmony, Log, verboseLogging));
        }

        if (Config.Bind("Collision", "WantedOnHardPoliceCrash", true,
                "Spusti hledanost po silnem narazu hracova auta do policejniho vozu.").Value)
        {
            policeCrashPatch = Install("police-crash", harmony => Patch(harmony,
                AccessTools.DeclaredMethod(typeof(CarController), "OnCollision",
                    new[] { typeof(Collision), typeof(bool) }),
                nameof(ReportHardPoliceCrash), prefix: false));
        }
    }

    public override bool Unload()
    {
        debrisPatch?.UnpatchSelf();
        sensorPatch?.UnpatchSelf();
        tracePatch?.UnpatchSelf();
        partDamagePatch?.UnpatchSelf();
        streamingPatch?.UnpatchSelf();
        detailPatch?.UnpatchSelf();
        decalPatch?.UnpatchSelf();
        decalMipPatch?.UnpatchSelf();
        vehicleDetailPatch?.UnpatchSelf();
        populationRangePatch?.UnpatchSelf();
        trafficSpeedPatch?.UnpatchSelf();
        impatientTrafficPatch?.UnpatchSelf();
        ImpatientTraffic.Clear();
        policeCrashPatch?.UnpatchSelf();
        return true;
    }

    private static void AdjustVehicleDetail(CarController __instance)
    {
        try
        {
            foreach (var group in __instance.GetComponentsInChildren<LODGroup>(true))
            {
                var lods = group.GetLODs();
                if (lods.Length == 0)
                    continue;

                foreach (var lod in lods)
                    lod.screenRelativeTransitionHeight /= vehicleDetailMultiplier;

                group.SetLODs(lods);
                if (Interlocked.Exchange(ref vehicleDetailReported, 1) == 0)
                    if (verboseLogging)
                        patchLog?.LogInfo($"Dosah LOD aut ×{vehicleDetailMultiplier:F2}: {group.name}.");
            }
        }
        catch (Exception exception)
        {
            patchLog?.LogWarning($"Dosah LOD auta: {exception.Message}");
        }
    }

    private static void ScaleDecalMipEdges(DecalStreamingManager __instance,
        out Il2CppStructArray<float> __state)
    {
        __state = null;
        try
        {
            var edges = __instance.mipLevelEdges;
            if (edges == null || edges.Length < 2 || edges.Length > 32)
                return;

            var scaled = new Il2CppStructArray<float>(edges.Length);
            for (int i = 0; i < edges.Length; i++)
            {
                if (!float.IsFinite(edges[i]))
                    return;

                scaled[i] = edges[i] > 0f ? edges[i] * decalMipMultiplier : edges[i];
            }

            __state = edges;
            __instance.mipLevelEdges = scaled;
            if (Interlocked.Exchange(ref decalMipReported, 1) == 0)
                if (verboseLogging)
                    patchLog?.LogInfo($"HDRP decal mip: prahy ×{decalMipMultiplier:F2}, {edges.Length} hodnot.");
        }
        catch (Exception exception)
        {
            if (Interlocked.Exchange(ref decalMipReported, 1) == 0)
                patchLog?.LogWarning($"HDRP decal mip: {exception.Message}");
        }
    }

    private static void RestoreDecalMipEdges(DecalStreamingManager __instance,
        Il2CppStructArray<float> __state)
    {
        if (__state != null)
            __instance.mipLevelEdges = __state;
    }

    private static void AdjustDecalDistance(ref int __result)
    {
        if (__result <= 0 || __result >= decalDistance)
            return;

        int original = __result;
        __result = decalDistance;
        if (Interlocked.Exchange(ref decalDistanceReported, 1) == 0)
            if (verboseLogging)
                patchLog?.LogInfo($"HDRP dosah decalů při vykreslení: {original} -> {__result} m.");
    }

    private float ReadVisibilityMultiplier(string name, float defaultValue, string description) =>
        Config.Bind("Visibility", name, defaultValue, new ConfigDescription(
            description + " 1 = puvodni. Zmena po restartu.",
            new AcceptableValueRange<float>(1f, 1.25f))).Value;

    private Harmony Install(string suffix, Action<Harmony> install)
    {
        var harmony = new Harmony($"{PluginId}.{suffix}");
        try
        {
            install(harmony);
            return harmony;
        }
        catch (Exception exception)
        {
            harmony.UnpatchSelf();
            Log.LogError($"{suffix}: {exception}");
            return null;
        }
    }

    private void Patch(Harmony harmony, MethodInfo target, string callback, bool prefix)
    {
        if (target == null)
            throw new MissingMethodException($"Cil pro {callback} chybi.");

        var patch = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VehiclePlugin), callback));
        if (prefix)
            harmony.Patch(target, prefix: patch);
        else
            harmony.Patch(target, postfix: patch);
        if (verboseLogging)
            Log.LogInfo($"Harmony: {target.DeclaringType?.Name}.{target.Name}");
    }

    private static bool ReportAttachedPartOnly(NotifyCarParentOnCollision __instance) =>
        !IsDetached(__instance);

    private static bool ProcessAttachedChildOnly(Transform __1) =>
        !IsDetached(__1);

    private static bool IsDetached(Component source)
    {
        if (source == null)
            return false;

        var part = source.GetComponentInParent<DetachablePart>();
        if (part == null || !part.IsDetached)
            return false;

        if (Interlocked.Exchange(ref detachedReported, 1) == 0)
            if (verboseLogging)
                patchLog?.LogInfo("Kolize oddeleneho dilu se nepredala jeho puvodnimu autu.");
        return true;
    }

    private static void AdjustSensorInterval(ref float __result)
    {
        if (__result <= 0f || !float.IsFinite(__result))
            return;

        float original = __result;
        __result *= sensorMultiplier;
        if (Interlocked.Exchange(ref sensorReported, 1) == 0)
            if (verboseLogging)
                patchLog?.LogInfo($"Interval senzoru aut: {original:F3} -> {__result:F3}");
    }

    private static void AdjustAssetCleanupInterval(SceneLoader __instance)
    {
        float current = __instance.unloadUnusedAssetsInterval;
        if (!float.IsFinite(current) || current <= 0f)
            return;

        float adjusted = current * cleanupIntervalMultiplier;
        if (!float.IsFinite(adjusted))
            return;

        __instance.unloadUnusedAssetsInterval = adjusted;
        if (verboseLogging)
            patchLog?.LogInfo($"Interval uklidu nepouzitych assetu: {current:F1} -> {adjusted:F1} s.");
    }

    private static void AdjustDetail(float __0)
    {
        if (!float.IsFinite(__0) || __0 <= 0f)
            return;

        float adjusted = __0 * detailMultiplier;
        if (!float.IsFinite(adjusted))
            return;

        QualitySettings.lodBias = adjusted;
        if (verboseLogging)
            patchLog?.LogInfo($"Unity LOD bias: {__0:F2} -> {adjusted:F2}.");
    }

    private static void AdjustCarRange(ref float __result) =>
        AdjustPopulationRange(ref __result, ref carRangeReported, "Auta");

    private static void AdjustPedestrianRange(ref float __result) =>
        AdjustPopulationRange(ref __result, ref pedestrianRangeReported, "Chodci");

    private static void AdjustPopulationRange(ref float distance, ref bool reported, string name)
    {
        if (!float.IsFinite(distance) || distance <= 0f)
            return;

        float adjusted = distance * populationRangeMultiplier;
        if (!float.IsFinite(adjusted))
            return;

        if (!reported)
        {
            reported = true;
            if (verboseLogging)
                patchLog?.LogInfo($"{name}: dosah spawneru {distance:F1} -> {adjusted:F1} m.");
        }
        distance = adjusted;
    }

    private static void AdjustTrafficSpeed(DriverAI __instance, ref float __result)
    {
        if (!float.IsFinite(__result) || __result <= 0f ||
            __instance.targetType != DriverAI.TargetType.WorldPoint ||
            __instance.isPoliceActive || __instance.ignoreTrafficSigns || __instance.hardLockOnTarget)
            return;

        var car = __instance.carController;
        if (car == null || car.IsPlayer || car.isPolice || car.PoliceCar != null)
            return;

        float original = __result;
        __result *= trafficSpeedMultiplier;
        if (!trafficSpeedReported)
        {
            trafficSpeedReported = true;
            if (verboseLogging)
                patchLog?.LogInfo($"Cilova rychlost bezne dopravy: {original:F2} -> {__result:F2}.");
        }
    }

    private static void ReportHardPoliceCrash(CarController __instance, Collision __0, bool __1)
    {
        if (__1 || __0 == null || !__instance.IsPlayer ||
            !(__0.relativeVelocity.sqrMagnitude >= 25f && __0.impulse.sqrMagnitude >= 2_250_000f))
            return;

        var collider = __0.collider;
        if (collider == null || IsDetached(collider))
            return;

        var policeCar = collider.GetComponentInParent<CarController>();
        if (policeCar == null || (!policeCar.isPolice && policeCar.PoliceCar == null))
            return;

        var wanted = WantedSystem.instance;
        if (wanted == null || wanted.isDisabled || WantedSystem.WantedLevel != WantedLevel.Inactive)
            return;

        WantedSystem.AddWantedLevel();
        if (verboseLogging)
            patchLog?.LogInfo("Silný náraz hráče do policejního vozu předán systému hledanosti.");
    }

    private static void TracePlayerContact(CarController __instance, Collision __0, bool __1)
    {
        try
        {
            if (__0 == null || !__instance.IsPlayer || Time.realtimeSinceStartup < nextCollisionTrace)
                return;

            nextCollisionTrace = Time.realtimeSinceStartup + 0.25f;
            int count = __0.contactCount;
            string contactInfo = "bez kontaktnich bodu";
            if (count > 0)
            {
                var contact = __0.GetContact(0);
                for (int index = 1; index < count; index++)
                {
                    var candidate = __0.GetContact(index);
                    if (candidate.separation < contact.separation)
                        contact = candidate;
                }

                contactInfo = $"vlastni={Describe(contact.thisCollider)}, cizi={Describe(contact.otherCollider)}, " +
                              $"separation={contact.separation:F3} m, bod={contact.point}";
            }

            patchLog?.LogInfo($"Kontakt auta: {contactInfo}, collider={Describe(__0.collider)}, " +
                              $"rychlost={__0.relativeVelocity.magnitude:F2} m/s, " +
                              $"impuls={__0.impulse.magnitude:F2} N*s, body={count}, priznak={__1}");
        }
        catch (Exception exception)
        {
            nextCollisionTrace = float.PositiveInfinity;
            patchLog?.LogWarning($"Diagnostika kolizi zastavena: {exception.Message}");
        }
    }

    private static string Describe(Collider collider)
    {
        return collider == null ? "-" :
            $"{collider.transform.root.name}/{collider.name}#{collider.GetInstanceID()} bounds={collider.bounds.size}";
    }
}
