using System;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VivatPopulationMod;

internal static class CollisionPartDamage
{
    [ThreadStatic]
    private static Collision activeCollision;

    private static ManualLogSource log;
    private static float nextFilteredLog;
    private static float nextJointLog;

    internal static void Install(Harmony harmony)
    {
        log ??= BepInEx.Logging.Logger.CreateLogSource("Vivat Part Damage");
        nextFilteredLog = 0f;
        nextJointLog = 0f;

        PatchScope(harmony, typeof(CarController), "OnCollision", typeof(Collision), typeof(bool));
        PatchScope(harmony, typeof(MeshDeform), "OnCollisionEnter", typeof(Collision));
        harmony.Patch(RequireMethod(typeof(DetachablePart), "OnHit", typeof(float)),
            prefix: Callback(nameof(FilterPartHit)));
        harmony.Patch(RequireMethod(typeof(DetachablePart), "OnJointBreak", typeof(float)),
            prefix: Callback(nameof(TraceJointBreak)));
        log.LogInfo("Filtr poskozeni dilu zapnuty.");
    }

    private static void PatchScope(Harmony harmony, Type type, string name, params Type[] signature)
    {
        harmony.Patch(RequireMethod(type, name, signature),
            prefix: Callback(nameof(EnterCollision)), finalizer: Callback(nameof(LeaveCollision)));
    }

    private static System.Reflection.MethodInfo RequireMethod(Type type, string name, params Type[] signature) =>
        AccessTools.DeclaredMethod(type, name, signature) ?? throw new MissingMethodException(type.FullName, name);

    private static HarmonyMethod Callback(string name) =>
        new(AccessTools.DeclaredMethod(typeof(CollisionPartDamage), name));

    private static void EnterCollision(Collision __0, out Collision __state)
    {
        __state = activeCollision;
        activeCollision = __0;
    }

    private static void LeaveCollision(Collision __state) => activeCollision = __state;

    private static bool FilterPartHit(DetachablePart __instance, float __0)
    {
        var collision = activeCollision;
        if (collision == null)
            return true;

        float impulseSquared = collision.impulse.sqrMagnitude;
        float speedSquared = collision.relativeVelocity.sqrMagnitude;
        bool negligibleImpulse = impulseSquared <= 1f;
        bool slowDeepOverlap = speedSquared <= 0.75f * 0.75f && HasDeepOverlap(collision);
        if (!negligibleImpulse && !slowDeepOverlap)
            return true;

        float now = Time.realtimeSinceStartup;
        if (now >= nextFilteredLog)
        {
            nextFilteredLog = now + 0.5f;
            log.LogInfo($"Preskoceno poskozeni dilu {__instance.name}: " +
                        $"v={MathF.Sqrt(speedSquared):F2} m/s, J={MathF.Sqrt(impulseSquared):F1} N*s, " +
                        $"OnHit={__0:F1}, duvod={(negligibleImpulse ? "zanedbatelny impuls" : "pomaly hluboky prunik")}.");
        }
        return false;
    }

    private static bool HasDeepOverlap(Collision collision)
    {
        int count = collision.contactCount;
        for (int i = 0; i < count; i++)
        {
            if (collision.GetContact(i).separation <= -0.05f)
                return true;
        }
        return false;
    }

    private static void TraceJointBreak(DetachablePart __instance, float __0)
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextJointLog)
            return;

        nextJointLog = now + 0.5f;
        var body = __instance.parentBody;
        log.LogInfo($"Praskl kloub dilu {__instance.name}, auto={(body == null ? "-" : body.name)}, sila={__0:F1}.");
    }
}
