using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace VivatPopulationMod;

[BepInPlugin(PluginId, "Vivat Population Performance", "0.1.0")]
public sealed class PopulationPlugin : BasePlugin
{
    private const string PluginId = "local.vivat.populationperformance";
    private static int carPercent;
    private static int distantCarPercent;
    private static int pedestrianPercent;
    private static int reportedLimits;
    private static ManualLogSource patchLog;
    private static bool verboseLogging;
    private Harmony harmony;

    public override void Load()
    {
        carPercent = ReadPercent("Cars");
        distantCarPercent = ReadPercent("DistantCars");
        pedestrianPercent = ReadPercent("Pedestrians");
        patchLog = Log;
        verboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false,
            "Podrobne informacni vypisy limitu populace. Restart hry.").Value;
        reportedLimits = 0;
        harmony = new Harmony(PluginId);
        try
        {
            var assembly = Assembly.Load("Assembly-CSharp");
            var manager = assembly.GetTypes().Single(type => type.Name == "WorldManager" &&
                type.GetProperty("MaxSpawnedCars", AccessTools.allDeclared) != null &&
                type.GetProperty("MaxSpawnedPadestrians", AccessTools.allDeclared) != null);
            PatchLimit(manager, "MaxSpawnedCars", nameof(CarLimit), carPercent);
            PatchLimit(manager, "MaxSpawnedDistantCars", nameof(DistantCarLimit), distantCarPercent);
            PatchLimit(manager, "MaxSpawnedPadestrians", nameof(PedestrianLimit), pedestrianPercent);
        }
        catch (Exception exception)
        {
            harmony.UnpatchSelf();
            Log.LogError($"Limity populace nebyly aktivovany: {exception}");
        }
    }

    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        return true;
    }

    private int ReadPercent(string name)
    {
        return Config.Bind("Population", name, 75, new BepInEx.Configuration.ConfigDescription(
            "Procento puvodniho limitu (100 = beze zmeny). Zmena vyzaduje restart hry.",
            new BepInEx.Configuration.AcceptableValueRange<int>(25, 100))).Value;
    }

    private void PatchLimit(Type manager, string property, string callback, int percent)
    {
        if (percent == 100)
            return;

        var getter = AccessTools.DeclaredPropertyGetter(manager, property);
        if (getter == null || getter.ReturnType != typeof(int) || getter.GetParameters().Length != 0)
            throw new MissingMethodException(manager.FullName, $"get_{property}()");

        var postfix = AccessTools.DeclaredMethod(typeof(PopulationPlugin), callback);
        harmony!.Patch(getter, postfix: new HarmonyMethod(postfix));
        if (verboseLogging)
            Log.LogInfo($"Harmony: {manager.FullName}.{getter.Name} ({percent} %)");
    }

    private static void CarLimit(ref int __result) =>
        ApplyLimit(ref __result, carPercent, 1, "Cars");

    private static void DistantCarLimit(ref int __result) =>
        ApplyLimit(ref __result, distantCarPercent, 2, "DistantCars");

    private static void PedestrianLimit(ref int __result) =>
        ApplyLimit(ref __result, pedestrianPercent, 4, "Pedestrians");

    private static void ApplyLimit(ref int result, int percent, int reportFlag, string name)
    {
        if (result <= 1)
            return;

        int original = result;
        result = Math.Max(1, (int)((long)original * percent / 100));
        if (verboseLogging && (Volatile.Read(ref reportedLimits) & reportFlag) == 0 &&
            (Interlocked.Or(ref reportedLimits, reportFlag) & reportFlag) == 0)
            patchLog?.LogInfo($"{name}: herni limit {original} -> {result}");
    }
}
