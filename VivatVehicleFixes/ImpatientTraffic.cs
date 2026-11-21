using System;
using System.Collections.Generic;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace VivatPopulationMod;

internal static class ImpatientTraffic
{
    private const float WaitSeconds = 5f;
    private const float CrawlSpeed = 2.5f;
    private const float SampleInterval = 0.2f;

    private sealed class Stop
    {
        public float Since;
        public float NextSample;
        public Vector3 Start;
        public Vector3[] Route;
        public int Leg;
        public float Deadline;
        public Vector2 Input;
    }

    private static readonly Dictionary<int, Stop> Stops = new();
    private static ManualLogSource log;
    private static bool failed;
    private static float nextDecisionLog;

    public static void Install(Harmony harmony, ManualLogSource logger)
    {
        log = logger;
        failed = false;
        nextDecisionLog = 0f;
        harmony.Patch(AccessTools.DeclaredMethod(typeof(DriverAI), "FixedTick"),
            postfix: new HarmonyMethod(typeof(ImpatientTraffic), nameof(AfterDrive)));
        harmony.Patch(AccessTools.DeclaredMethod(typeof(DriverAI), "OnDisable"),
            postfix: new HarmonyMethod(typeof(ImpatientTraffic), nameof(Forget)));
    }

    public static void Clear() => Stops.Clear();

    private static void Forget(DriverAI __instance) => Stops.Remove(__instance.GetInstanceID());

    private static void AfterDrive(DriverAI __instance)
    {
        if (failed)
            return;

        try
        {
            Drive(__instance);
        }
        catch (Exception exception)
        {
            failed = true;
            Clear();
            log?.LogError($"Impatient traffic disabled for this session: {exception}");
        }
    }

    private static void Drive(DriverAI __instance)
    {
        var driver = __instance;
        var car = driver.carController;
        if (car == null || car.IsPlayer || car.IsParked || car.IsDestroyed ||
            car.IsMovementDisabled || !car.IsWheelPhysicsEnabled ||
            !driver.haveTarget || driver.destinationReached || driver.isPaused ||
            driver.isPoliceActive || driver.ignoreTrafficSigns || driver.hardLockOnTarget ||
            driver.isBranching || driver.targetType != DriverAI.TargetType.WorldPoint)
        {
            Forget(driver);
            return;
        }

        int id = driver.GetInstanceID();
        float now = Time.time;
        float speed = car.velocity.magnitude;
        if (!Stops.TryGetValue(id, out var stop))
        {
            if (speed < 0.35f)
                Stops[id] = new Stop { Since = now, Start = driver.position };
            return;
        }

        if (stop.Route == null && (speed >= 0.35f ||
            (driver.position - stop.Start).sqrMagnitude > 1f))
        {
            Stops.Remove(id);
            return;
        }

        if (now - stop.Since < WaitSeconds)
            return;

        if (now >= stop.NextSample)
        {
            stop.NextSample = now + SampleInterval;
            if (!UpdateManeuver(driver, car, stop, now))
            {
                Stops.Remove(id);
                return;
            }
        }

        if (stop.Route != null)
            car.input = stop.Input;
    }

    private static bool UpdateManeuver(DriverAI driver, CarController car, Stop stop, float now)
    {
        if (TrafficControlAhead(driver, car))
            return Skip(car, "traffic control ahead");

        float radius = Mathf.Max(0.8f, driver.boundExtents.x) + 0.25f;
        if (stop.Route == null)
        {
            stop.Route = FindRoute(driver, car, radius, out string reason);
            if (stop.Route == null)
                return Skip(car, reason);

            stop.Deadline = now + 12f;
            log?.LogInfo($"Impatient traffic: car {car.ID} attempting {(stop.Route.Length == 1 ? "restart" : "bypass")}.");
        }

        if (now >= stop.Deadline)
            return false;

        Vector3 delta = stop.Route[stop.Leg] - driver.position;
        delta.y = 0f;
        if (delta.sqrMagnitude < 1.44f)
        {
            if (++stop.Leg == stop.Route.Length)
                return false;
            delta = stop.Route[stop.Leg] - driver.position;
            delta.y = 0f;
        }

        if (Vector3.Dot(driver.forward, delta.normalized) < 0.25f)
            return Skip(car, "steering angle too sharp");
        if (!ClearSegment(car, driver.position, driver.position + Vector3.ClampMagnitude(delta, 4f), radius))
            return Skip(car, "route no longer clear");

        float steer = Vector3.SignedAngle(driver.forward, delta, Vector3.up) / 30f;
        float throttle = car.velocity.magnitude < CrawlSpeed ? 0.28f : 0f;
        stop.Input = new Vector2(Mathf.Clamp(steer, -0.65f, 0.65f), throttle);
        return true;
    }

    private static bool Skip(CarController car, string reason)
    {
        if (Time.time >= nextDecisionLog)
        {
            nextDecisionLog = Time.time + 5f;
            log?.LogDebug($"Impatient traffic: car {car.ID} waiting ({reason}).");
        }
        return false;
    }

    private static Vector3[] FindRoute(DriverAI driver, CarController car, float radius, out string reason)
    {
        reason = "no straight road segment";
        var point = driver.worldPointTarget;
        var next = driver.worldPointTargetNext;
        if (point == null || next == null || !point.HasData || !next.HasData ||
            !point.IsWaypoint || !next.IsWaypoint ||
            (point.Branches != null && point.Branches.Count > 0) ||
            (next.Branches != null && next.Branches.Count > 0))
            return null;

        Vector3 origin = driver.position;
        Vector3 forward = driver.forward;
        if (Vector3.Dot(forward, point.Forward) < 0.9f ||
            Vector3.Dot(forward, next.Forward) < 0.9f ||
            Vector3.Dot(next.Position - origin, forward) < 15f)
            return null;

        Vector3 straight = origin + forward * 5f;
        if (ClearSegment(car, origin, straight, radius))
            return new[] { straight };

        var blocker = driver.colliderHit;
        var blockedCar = blocker == null ? null : blocker.GetComponentInParent<CarController>();
        reason = "no stationary vehicle in the driver sensor";
        if (blockedCar == null || blockedCar.velocity.sqrMagnitude > 0.12f)
            return null;

        var blockedDriver = blockedCar.DriverAI;
        reason = "vehicle ahead is waiting in traffic";
        if (blockedDriver != null && (blockedDriver.colliderHit != null || blockedDriver.hasPriorityCollider))
            return null;

        float sideOffset = radius + blocker.bounds.extents.x + 0.5f;
        float availableWidth = Mathf.Min(point.Width, next.Width) * 0.5f;
        reason = $"road too narrow ({availableWidth:F1} m available, {sideOffset + radius:F1} m needed)";
        if (availableWidth < sideOffset + radius)
            return null;

        // Keep the detour on the right side of the available road.
        Vector3 side = driver.right * sideOffset;
        float obstacleDistance = Vector3.Dot(blocker.bounds.center - origin, forward);
        float clearance = blocker.bounds.extents.magnitude + driver.boundExtents.z + 1f;
        reason = $"insufficient maneuver distance (vehicle {obstacleDistance:F1} m ahead)";
        if (obstacleDistance < 5f || obstacleDistance + clearance > 11f)
            return null;

        Vector3 pullOut = origin + forward * 5f + side;
        Vector3 pass = origin + forward * (obstacleDistance + clearance) + side;
        Vector3 rejoin = origin + forward * 15f;
        reason = "bypass corridor is occupied or lacks ground";
        return ClearSegment(car, origin, pullOut, radius) &&
            ClearSegment(car, pullOut, pass, radius) &&
            ClearSegment(car, pass, rejoin, radius)
            ? new[] { pullOut, pass, rejoin }
            : null;
    }

    private static bool TrafficControlAhead(DriverAI driver, CarController car)
    {
        if (driver.hasPriorityCollider || IsTrafficControl(driver.colliderHit))
            return true;

        Vector3 center = driver.position + driver.forward * 8f + Vector3.up;
        foreach (var collider in Physics.OverlapBox(center, new Vector3(5f, 2f, 8f),
                     car.transform.rotation, ~0, QueryTriggerInteraction.Collide))
        {
            if (IsTrafficControl(collider))
                return true;
        }
        return false;
    }

    private static bool IsTrafficControl(Collider collider) =>
        collider != null && collider.GetComponentInParent<TrafficObject>() != null;

    private static bool ClearSegment(CarController car, Vector3 from, Vector3 to, float radius)
    {
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < 0.01f)
            return true;

        Vector3 height = Vector3.up * 1.25f;
        int groundSamples = Mathf.CeilToInt(delta.magnitude / 2f);
        for (int i = 1; i <= groundSamples; i++)
        {
            Vector3 sample = from + delta * (i / (float)groundSamples);
            if (!Physics.Raycast(sample + Vector3.up * 1.5f, Vector3.down, 2.2f,
                    car.groundMask, QueryTriggerInteraction.Ignore))
                return false;
        }

        Quaternion orientation = Quaternion.LookRotation(delta, Vector3.up);
        Vector3 center = (from + to) * 0.5f + height;
        Vector3 extents = new Vector3(radius, 0.9f, delta.magnitude * 0.5f + radius);
        foreach (var collider in Physics.OverlapBox(center, extents, orientation, ~0,
                     QueryTriggerInteraction.Ignore))
        {
            if (collider != null && !collider.transform.IsChildOf(car.transform))
                return false;
        }
        return true;
    }
}
