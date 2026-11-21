using System;
using System.Collections.Generic;
using System.Diagnostics;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using FMODUnity;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using Vivat.Characters;
using Vivat.Systems.WantedSystem;
using Vivat.UI.Map;
using Object = UnityEngine.Object;

namespace VivatPopulationMod;

[BepInPlugin("local.vivat.combat", "Vivat Combat", "0.1.25")]
public sealed class CombatPlugin : BasePlugin
{
    private Harmony markers;
    private Harmony diagnostics;
    private Harmony damageDiagnostics;
    private Harmony cameraRecoil;
    private Harmony policeTransport;
    private Harmony policeBalance;
    private Harmony policeNavigation;
    private PoliceDispatch policeDispatch;
    private Harmony npcAudio;
    private Harmony npcHitReactions;
    private Harmony policeVoices;
    private PoliceVoicePlayback policePlayback;
    private static CombatMarkers markerComponent;
    private static ManualLogSource log;
    private static float nextAudioReport;
    private static float nextPlayerAudioReport;
    private static float nextTimingReport;
    private static float npcShotGain;
    private static bool reportedNpcGain;
    private static float npcHitReactionStrength;
    private static bool reportedNpcHitReaction;
    private static bool damageTraceEnabled;
    private static bool combatTraceEnabled;
    private static float damageCurveStrength;
    private static float cameraRecoilScale;
    private static float skorpionCameraRecoilScale;
    private static float activeCameraRecoilScale = 1f;
    private static bool policeLoadouts;
    private static bool policeTransportEnabled;
    private static bool policeBalanceEnabled;
    private static int policeWaveLimit;
    private static int policeFootFallbackLimit;
    private static float policeDropOffDistance;
    private static float policeWantedSince;
    private static int debugInitialPoliceCars;
    private static bool debugInitialDispatchSent;
    private static float nextPoliceDispatchDebug;
    private static readonly HashSet<int> DebugActivatedPatrols = new();
    private static readonly Dictionary<int, PoliceCarProgress> PoliceProgress = new();
    private static readonly Vector3[] RoadRecoveryOffsets =
    {
        new(0f, 0f, 10f), new(0f, 0f, -10f), new(8f, 0f, 0f),
        new(-8f, 0f, 0f), new(0f, 0f, 18f), new(0f, 0f, -18f)
    };

    private sealed class PoliceCarProgress
    {
        internal Vector3 Position;
        internal float StationarySince;
        internal bool RepathRequested;
    }
    private static Spawner reservedPoliceSpawner;
    private static int originalPoliceCarCapacity;
    private static int reservedPoliceCarCapacity;
    private static bool reportedMissingPoliceSpawner;
    private static float nextPoliceNavigationReport;
    private static float nextPoliceFootNavigationReport;
    private static readonly HashSet<int> AssembledPoliceCars = new();
    private static readonly Dictionary<int, PoliceTransport> PoliceTransports = new();
    private static readonly CarSeatType[] PolicePassengerSeats =
        { CarSeatType.FrontRight, CarSeatType.BackLeft, CarSeatType.BackRight };

    private sealed class PoliceTransport
    {
        internal readonly CarController Vehicle;
        internal readonly CarInteract Car;
        internal readonly List<CharacterControllerBase> Officers = new(3);
        internal bool BrakingForDropOff;

        internal PoliceTransport(CarController vehicle, CarInteract car)
        {
            Vehicle = vehicle;
            Car = car;
        }
    }
    [ThreadStatic]
    private static CharacterControllerBase weaponHitNpc;
    [ThreadStatic]
    private static Weapon activeShotWeapon;
    [ThreadStatic]
    private static Weapon simulatedDamageWeapon;

    public override void Load()
    {
        log = Log;
        bool customPoliceEnabled = Config.Bind("Police", "CustomLogicEnabled", true,
            "Zapne vlastní logiku policejních posil, dopravy, výzbroje a navigace. Po změně restartuj hru.").Value;
        bool transportReinforcements = Config.Bind("Police", "TransportReinforcements", true,
            "Při pátrání obsadí volná místa v přijíždějící policejní hlídce pěšími posilami.").Value;
        bool separateLoadouts = Config.Bind("Police", "SeparateLoadouts", true,
            "Pěší posily dostanou pistoli, posádka z auta může mít útočnou pušku.").Value;
        if (!customPoliceEnabled)
            Log.LogInfo("Policejní posily: CustomLogicEnabled = false, používá se původní logika hry.");
        debugInitialPoliceCars = Config.Bind("Police", "DebugInitialCars", 0, new ConfigDescription(
            "Počet aut vyžádaných ihned na začátku wanted. 0 = běžný spawn bez tohoto debugu.",
            new AcceptableValueRange<int>(0, 6))).Value;
        policeTransportEnabled = customPoliceEnabled && transportReinforcements;
        policeLoadouts = customPoliceEnabled && separateLoadouts;
        policeWaveLimit = Config.Bind("Police", "WaveLimit", 5, new ConfigDescription(
            "Maximální cíl pěší vlny, pokud je doprava posil autem vypnutá.",
            new AcceptableValueRange<int>(3, 12))).Value;
        policeFootFallbackLimit = Config.Bind("Police", "FootFallbackLimit", 2, new ConfigDescription(
            "Limit pěších posil, pokud dispečink během 15 sekund nesežene hlídku. 0 = bez pěší zálohy.",
            new AcceptableValueRange<int>(0, 5))).Value;
        policeDropOffDistance = Config.Bind("Police", "DropOffDistance", 48f, new ConfigDescription(
            "Vzdálenost od hráče v metrech, na které hlídka začne brzdit kvůli výstupu.",
            new AcceptableValueRange<float>(30f, 70f))).Value;
        bool balanceWaves = Config.Bind("Police", "BalanceWaves", true,
            "S dopravou zapnutou mají přednost posily autem. Jinak počet pěších posil roste pozvolna.").Value;
        policeBalanceEnabled = customPoliceEnabled && balanceWaves;
        if (policeBalanceEnabled)
        {
            policeBalance = Install("police-balance", harmony =>
            {
                var activate = AccessTools.DeclaredMethod(typeof(WaveSpawner), "Activate", Type.EmptyTypes)
                    ?? throw new MissingMethodException(nameof(WaveSpawner), "Activate");
                harmony.Patch(activate, prefix: Callback(nameof(BalancePoliceWave)));
                Log.LogInfo("Harmony: WaveSpawner.Activate");
            });
        }

        if (policeTransportEnabled || policeLoadouts)
        {
            policeTransport = Install("police-transport", harmony =>
            {
                Patch(harmony, typeof(WaveSpawner), "RegisterCharacter", new[] { typeof(CharacterControllerBase) },
                    nameof(PreparePoliceReinforcement));
                if (!policeTransportEnabled)
                    return;
                Patch(harmony, typeof(PoliceCar), "Update", Type.EmptyTypes, nameof(CheckPoliceArrival));
                Patch(harmony, typeof(PoliceCar), "OnWantedLevelDeactivated", Type.EmptyTypes, nameof(EndPolicePursuit));
                Patch(harmony, typeof(PoliceCar), "OnDisable", Type.EmptyTypes, nameof(CancelPoliceDropOff));
            });
        }
        if (policeTransportEnabled)
        {
            policeNavigation = Install("police-navigation", harmony =>
            {
                var calculatePath = AccessTools.DeclaredMethod(typeof(NavMeshAgent), "CalculatePath",
                        new[] { typeof(Vector3), typeof(NavMeshPath) })
                    ?? throw new MissingMethodException(nameof(NavMeshAgent), "CalculatePath");
                harmony.Patch(calculatePath, prefix: Callback(nameof(CalculatePolicePath)));
                var repath = AccessTools.DeclaredMethod(typeof(CharacterAgentMovement), "RepathInternal", Type.EmptyTypes)
                    ?? throw new MissingMethodException(nameof(CharacterAgentMovement), "RepathInternal");
                harmony.Patch(repath, prefix: Callback(nameof(PreparePoliceFootPath)));
                Log.LogInfo("Harmony: NavMeshAgent.CalculatePath, CharacterAgentMovement.RepathInternal");
            });
            policeDispatch = AddComponent<PoliceDispatch>();
        }
        damageTraceEnabled = Config.Bind("Diagnostics", "NpcDamageTrace", true,
            "Zapisuje zásahy hráče do NPC a změny zdraví do logu BepInEx.").Value;
        damageCurveStrength = Config.Bind("Weapons", "PlayerDamageCurveStrength", 1f, new ConfigDescription(
            "Síla nové křivky poškození střel hráče do NPC. 0 = původní, 1 = plná úprava.",
            new AcceptableValueRange<float>(0f, 1f))).Value;
        cameraRecoilScale = Config.Bind("Weapons", "CameraRecoilScale", 0.90f, new ConfigDescription(
            "Násobek kopnutí kamery u zbraní hráče. 1 = původní.",
            new AcceptableValueRange<float>(0f, 1f))).Value;
        skorpionCameraRecoilScale = Config.Bind("Weapons", "SkorpionCameraRecoilScale", 0.65f, new ConfigDescription(
            "Samostatný násobek kopnutí kamery u Škorpionu. 1 = původní.",
            new AcceptableValueRange<float>(0f, 1f))).Value;
        activeCameraRecoilScale = 1f;

        if (cameraRecoilScale < 1f || skorpionCameraRecoilScale < 1f)
        {
            cameraRecoil = Install("camera-recoil", harmony =>
            {
                var generate = AccessTools.DeclaredMethod(typeof(Recoil), "GenerateCameraRecoil",
                        new[] { typeof(CharacterControllerBase) })
                    ?? throw new MissingMethodException(nameof(Recoil), "GenerateCameraRecoil");
                var process = AccessTools.DeclaredMethod(typeof(CharacterCameraLook), "ProcessRecoil",
                        new[] { typeof(Vector2) })
                    ?? throw new MissingMethodException(nameof(CharacterCameraLook), "ProcessRecoil");
                harmony.Patch(generate, prefix: Callback(nameof(SelectCameraRecoil)));
                harmony.Patch(process, prefix: Callback(nameof(ScaleCameraRecoil)));
                Log.LogInfo("Harmony: Recoil.GenerateCameraRecoil, CharacterCameraLook.ProcessRecoil");
            });
        }
        npcHitReactionStrength = Config.Bind("HitReaction", "NpcStrength", 2f, new ConfigDescription(
            "Síla animované reakce NPC na střelu. 1 = původní síla.",
            new AcceptableValueRange<float>(1f, 4f))).Value;
        reportedNpcHitReaction = false;
        if (npcHitReactionStrength > 1f || damageTraceEnabled)
        {
            npcHitReactions = Install("npc-hit-reaction", harmony =>
            {
                harmony.Patch(AccessTools.DeclaredMethod(typeof(CharacterHealth), "OnChildRecievedDamage"),
                    prefix: Callback(nameof(BeginWeaponHit)), postfix: Callback(nameof(EndWeaponHit)));
                if (npcHitReactionStrength <= 1f)
                    return;
                var reactionType = AccessTools.DeclaredProperty(typeof(CharacterRagdoll), "hitReaction")?.PropertyType
                    ?? throw new MissingMemberException(nameof(CharacterRagdoll), "hitReaction");
                var reaction = AccessTools.DeclaredMethod(reactionType, "Hit",
                        new[] { typeof(Collider), typeof(Vector3), typeof(Vector3) })
                    ?? throw new MissingMethodException(reactionType.FullName, "Hit");
                harmony.Patch(reaction, prefix: Callback(nameof(ScaleNpcHitReaction)));
                Log.LogInfo("Harmony: CharacterHealth.OnChildRecievedDamage, FinalIK.HitReaction.Hit");
            });
        }
        if (Config.Bind("PoliceVoices", "Enabled", true, "Přidá prostorové slovenské pokřiky pěších policistů.").Value)
        {
            policeVoices = Install("police-voices", harmony =>
            {
                Patch(harmony, typeof(Vivat.Systems.WantedSystem.Policeman), "OnWantedLevelActivated",
                    Type.EmptyTypes, nameof(PoliceVoicePlayback.Register));
                Patch(harmony, typeof(Vivat.Systems.WantedSystem.Policeman), "OnDisable",
                    Type.EmptyTypes, nameof(PoliceVoicePlayback.Unregister));
                policePlayback = AddComponent<PoliceVoicePlayback>();
                policePlayback.Initialize(Log);
            });
        }
        if (Config.Bind("Minimap", "EnemyDots", true, "Zobrazí červené tečky nepřátel útočících na hráče.").Value)
        {
            markers = Install("markers", harmony =>
            {
                Patch(harmony, typeof(CharacterAIBrain), "Enable", Type.EmptyTypes, nameof(RegisterBrain));
                Patch(harmony, typeof(CharacterAIBrain), "Disable", Type.EmptyTypes, nameof(UnregisterBrain));
                Patch(harmony, typeof(MiniMapView), "Update", Type.EmptyTypes, nameof(UpdateMinimapOverlay));
                markerComponent = AddComponent<CombatMarkers>();
                markerComponent.Initialize(Log);
            });
        }

        combatTraceEnabled = Config.Bind("Diagnostics", "CombatTrace", true,
            "Měří pomalé průchody simulace střel a zaznamená stav zvuku FMOD.").Value;
        if (combatTraceEnabled || damageTraceEnabled || damageCurveStrength > 0f)
        {
            diagnostics = Install("trace", harmony =>
            {
                Patch(harmony, typeof(Weapon), "SimulateBullets", new[] { typeof(float) },
                    nameof(EndBulletTiming), nameof(StartBulletTiming));
                if (combatTraceEnabled)
                    Patch(harmony, typeof(Weapon), "PlayShotSound", Type.EmptyTypes, nameof(InspectShot));
            });
        }

        if (damageTraceEnabled || damageCurveStrength > 0f)
        {
            damageDiagnostics = Install("npc-damage-trace", harmony =>
            {
                Patch(harmony, typeof(CharacterHealth), "TakeDamage",
                    new[] { typeof(float), typeof(bool), typeof(RaycastHit), typeof(Vector3), typeof(float),
                        typeof(bool), typeof(int) },
                    nameof(LogNpcDamage), nameof(CaptureNpcDamage));
            });
        }

        npcShotGain = Config.Bind("Audio", "NpcGunshotGain", 1.5f, new ConfigDescription(
            "Násobek hlasitosti výstřelů NPC z dálky. Plný účinek od 60 m. 1 = původní.",
            new AcceptableValueRange<float>(1f, 2f))).Value;
        reportedNpcGain = false;
        if (npcShotGain > 1f)
        {
            npcAudio = Install("npc-audio", harmony =>
            {
                PatchShotScope(harmony, "PlayShotSound", Type.EmptyTypes);
                PatchShotScope(harmony, "Update", Type.EmptyTypes);
                PatchShotScope(harmony, "ShootAI", new[] { typeof(Transform), typeof(float), typeof(int), typeof(int) });
                Patch(harmony, typeof(FMOD.Studio.EventDescription), "createInstance",
                    new[] { typeof(FMOD.Studio.EventInstance).MakeByRefType() }, nameof(HandleShotInstance));
            });
        }
    }

    public override bool Unload()
    {
        markers?.UnpatchSelf();
        diagnostics?.UnpatchSelf();
        damageDiagnostics?.UnpatchSelf();
        cameraRecoil?.UnpatchSelf();
        policeTransport?.UnpatchSelf();
        policeBalance?.UnpatchSelf();
        policeNavigation?.UnpatchSelf();
        if (policeDispatch != null)
            Object.Destroy(policeDispatch);
        AssembledPoliceCars.Clear();
        RestorePoliceCarCapacity();
        foreach (var crew in PoliceTransports.Values)
            ReleaseDropOffBrake(crew);
        PoliceTransports.Clear();
        npcAudio?.UnpatchSelf();
        npcHitReactions?.UnpatchSelf();
        policeVoices?.UnpatchSelf();
        if (policePlayback != null)
            Object.Destroy(policePlayback);
        if (markerComponent != null)
            Object.Destroy(markerComponent);
        return true;
    }

    private Harmony Install(string feature, Action<Harmony> install)
    {
        var harmony = new Harmony($"local.vivat.combat.{feature}");
        try
        {
            install(harmony);
            return harmony;
        }
        catch (Exception exception)
        {
            harmony.UnpatchSelf();
            Log.LogError($"{feature}: {exception}");
            return null;
        }
    }

    private void Patch(Harmony harmony, Type type, string name, Type[] signature, string postfix,
        string prefix = null)
    {
        var target = AccessTools.DeclaredMethod(type, name, signature) ??
            throw new MissingMethodException(type.FullName, name);
        harmony.Patch(target, prefix: prefix == null ? null : Callback(prefix), postfix: Callback(postfix));
        Log.LogInfo($"Harmony: {type.Name}.{name}");
    }

    private static HarmonyMethod Callback(string method) =>
        new(AccessTools.DeclaredMethod(typeof(CombatPlugin), method) ??
            AccessTools.DeclaredMethod(typeof(PoliceVoicePlayback), method));

    private static void PatchShotScope(Harmony harmony, string name, Type[] signature)
    {
        var target = AccessTools.DeclaredMethod(typeof(Weapon), name, signature) ??
            throw new MissingMethodException("Weapon", name);
        harmony.Patch(target, prefix: Callback(nameof(BeginShotScope)), finalizer: Callback(nameof(EndShotScope)));
    }

    private static void UpdateMinimapOverlay(MiniMapView __instance)
    {
        if (markerComponent != null)
            markerComponent.RenderOverlay(__instance);
    }

    private static void RegisterBrain(CharacterAIBrain __instance) =>
        CombatMarkers.Brains[__instance.GetInstanceID()] = __instance;

    private static void BalancePoliceWave(WaveSpawner __instance)
    {
        var wanted = WantedSystem.instance;
        if (wanted == null || wanted.waveSpawner != __instance)
            return;

        if (policeTransportEnabled)
        {
            SetPoliceFootFallback(__instance, 0);
            return;
        }

        int middle = Math.Max(3, policeWaveLimit - 1);
        __instance.spawnInterval = 3f;
        __instance.maxSpawned = policeWaveLimit;
        __instance.useSpawnCurve = true;
        __instance.spawnCurve = new AnimationCurve(
            new Keyframe(0f, 3f), new Keyframe(30f, 3f),
            new Keyframe(90f, middle), new Keyframe(150f, policeWaveLimit));
        log.LogInfo($"Policejní vlna: 3 až {policeWaveLimit} posil, interval 3 s.");
    }

    private static void SetPoliceFootFallback(WaveSpawner wave, int limit)
    {
        if (wave == null)
            return;

        wave.useSpawnCurve = false;
        wave.spawnInterval = 5f;
        if (wave.maxSpawned == limit)
            return;

        wave.maxSpawned = limit;
        log.LogInfo($"Policejní posily: limit pěší zálohy {limit}.");
    }

    private static void PreparePoliceReinforcement(WaveSpawner __instance, CharacterControllerBase __0)
    {
        var wanted = WantedSystem.instance;
        if (wanted == null || WantedSystem.WantedLevel != WantedLevel.Active || wanted.waveSpawner != __instance ||
            __0 == null || __0.IsDead || __0.Faction != Faction.Police)
            return;

        if (policeLoadouts && !__0.IsInCar)
            ApplyPoliceLoadout(__0, false);
    }

    internal static void DispatchPoliceCar()
    {
        var wanted = WantedSystem.instance;
        if (wanted == null || WantedSystem.WantedLevel != WantedLevel.Active)
        {
            policeWantedSince = 0f;
            reportedMissingPoliceSpawner = false;
            debugInitialDispatchSent = false;
            nextPoliceDispatchDebug = 0f;
            DebugActivatedPatrols.Clear();
            PoliceProgress.Clear();
            AssembledPoliceCars.Clear();
            RestorePoliceCarCapacity();
            return;
        }

        if (policeWantedSince == 0f)
            policeWantedSince = Time.unscaledTime;
        var game = GameManager.instance;
        var player = game != null ? game.PlayerController : null;
        var world = WorldManager.instance;
        if (player == null || world == null)
            return;

        var playerPosition = player.transform.position;
        float wantedDuration = Time.unscaledTime - policeWantedSince;
        const int maximumPatrols = 6;
        int targetPatrols = wantedDuration < 30f ? 1 : wantedDuration < 120f
            ? 2
            : Math.Min(maximumPatrols, 3 + (int)((wantedDuration - 120f) / 60f));

        targetPatrols = Math.Max(targetPatrols, debugInitialPoliceCars);
        var spawner = WorldManager.CarPoliceSpawner;
        if (spawner != null && debugInitialPoliceCars > 0 && !debugInitialDispatchSent)
        {
            debugInitialDispatchSent = true;
            ReservePoliceCarCapacity(spawner, targetPatrols);
            int before = spawner.Spawned;
            int requested = Math.Max(0, debugInitialPoliceCars - before);
            for (int i = 0; i < requested && spawner.Spawned < debugInitialPoliceCars; i++)
                spawner.Spawn();
            log.LogInfo($"Police debug: start wanted, cíl {debugInitialPoliceCars}, požadavky {requested}, " +
                        $"spawnuto {before}->{spawner.Spawned}, volné body {spawner.availableSpawnpoints}.");
        }

        bool reportDebug = debugInitialPoliceCars > 0 && Time.unscaledTime >= nextPoliceDispatchDebug;
        if (reportDebug)
            nextPoliceDispatchDebug = Time.unscaledTime + 5f;

        int nearbyPatrols = 0;
        foreach (var vehicle in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy)
                continue;

            float distance = (vehicle.transform.position - playerPosition).sqrMagnitude;
            var patrol = vehicle.PoliceCar;
            bool nativePoliceVehicle = spawner != null && vehicle.Spawner == spawner;
            if (patrol == null && !nativePoliceVehicle && !vehicle.isPolice)
                continue;

            if (patrol == null && nativePoliceVehicle)
                patrol = vehicle.GetComponentInChildren<PoliceCar>();

            if (reportDebug)
                ReportPoliceVehicle(vehicle, patrol, Mathf.Sqrt(distance));

            if (patrol != null && distance <= 230f * 230f)
                RecoverStoppedPoliceCar(vehicle, patrol, spawner, playerPosition);

            if (distance > 130f * 130f)
                continue;

            if (debugInitialPoliceCars > 0 && wantedDuration >= 2f && patrol != null &&
                !patrol.IsActive && DebugActivatedPatrols.Add(patrol.GetInstanceID()))
            {
                log.LogInfo($"Police debug: auto #{vehicle.GetInstanceID()}, požadavek na původní aktivaci pronásledování.");
                patrol.OnWantedLevelActivated();
            }

            var car = vehicle.CarInteract;
            var driver = car != null ? car.Driver : null;
            if (driver == player || (driver != null && driver.IsDead) ||
                (driver == null && (vehicle.DriverAI == null || (!nativePoliceVehicle && !vehicle.DriverAI.isPoliceActive))))
                continue;

            nearbyPatrols++;
            if (distance >= 32f * 32f && patrol != null && PoliceTransports.Count < targetPatrols)
                AssemblePoliceCrew(wanted.waveSpawner, patrol);
        }

        if (reportDebug)
        {
            string population = spawner != null ? $"{spawner.Spawned}/{spawner.GetMaxSpawned()}" : "nedostupný";
            log.LogInfo($"Police debug: {wantedDuration:0} s wanted, poblíž {nearbyPatrols}/{targetPatrols}, " +
                        $"policejní spawner {population}.");
        }

        if (policeBalanceEnabled)
        {
            bool needsFootFallback = nearbyPatrols == 0 && wantedDuration >= 15f;
            SetPoliceFootFallback(wanted.waveSpawner, needsFootFallback ? policeFootFallbackLimit : 0);
        }

        if (spawner != null)
        {
            ReservePoliceCarCapacity(spawner, targetPatrols);
        }
        else if (!reportedMissingPoliceSpawner)
        {
            reportedMissingPoliceSpawner = true;
            log.LogWarning("Policejní posily: původní spawner policejních aut není v této scéně dostupný.");
        }
    }

    private static void RecoverStoppedPoliceCar(CarController vehicle, PoliceCar patrol, Spawner spawner,
        Vector3 playerPosition)
    {
        int id = vehicle.GetInstanceID();
        var driver = vehicle.DriverAI;
        var interact = vehicle.CarInteract;
        var officer = interact != null ? interact.Driver : null;
        float distanceSquared = (vehicle.transform.position - playerPosition).sqrMagnitude;
        if (!patrol.IsActive || driver == null || officer == null || officer.IsDead || officer.IsPlayer ||
            distanceSquared < 55f * 55f || Mathf.Abs(vehicle.SpeedPerSecond) >= 1f ||
            (PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var crew) && crew.BrakingForDropOff))
        {
            PoliceProgress.Remove(id);
            return;
        }

        var position = vehicle.transform.position;
        float now = Time.unscaledTime;
        if (!PoliceProgress.TryGetValue(id, out var progress))
        {
            PoliceProgress[id] = new PoliceCarProgress { Position = position, StationarySince = now };
            return;
        }

        if ((position - progress.Position).sqrMagnitude > 4f)
        {
            progress.Position = position;
            progress.StationarySince = now;
            progress.RepathRequested = false;
            return;
        }

        float stationaryFor = now - progress.StationarySince;
        if (stationaryFor >= 7f && !progress.RepathRequested)
        {
            progress.RepathRequested = true;
            patrol.Repath();
            log.LogInfo($"Policejní posily: auto #{id} stojí, žádám přepočet trasy.");
        }

        if (stationaryFor < 14f)
            return;

        var camera = Camera.main;
        var body = vehicle.Rigidbody;
        var roads = spawner != null ? spawner.worldPointsData : null;
        if (camera == null || body == null || body.isKinematic || roads == null ||
            IsInsideCamera(camera, position))
            return;

        foreach (var offset in RoadRecoveryOffsets)
        {
            var point = roads.FindNearestWorldPoint(position + vehicle.transform.rotation * offset);
            if (point == null || point.NextWorldPoint == null)
                continue;

            var destination = point.Position;
            float moveSquared = (destination - position).sqrMagnitude;
            if (moveSquared < 16f || moveSquared > 22f * 22f ||
                Mathf.Abs(destination.y - position.y) > 2f ||
                (destination - playerPosition).sqrMagnitude < 45f * 45f ||
                IsInsideCamera(camera, destination) || !IsFreePoliceRoadPoint(vehicle, point))
                continue;

            body.position = destination + Vector3.up * 0.25f;
            body.rotation = point.Rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            Physics.SyncTransforms();
            driver.SetWorldPoint(point.NextWorldPoint);
            patrol.Repath();
            PoliceProgress.Remove(id);
            log.LogInfo($"Policejní posily: auto #{id} vráceno na volný silniční bod, posun {Mathf.Sqrt(moveSquared):0} m.");
            return;
        }

        progress.StationarySince = now - 7f;
    }

    private static bool IsInsideCamera(Camera camera, Vector3 position)
    {
        var viewport = camera.WorldToViewportPoint(position);
        return viewport.z > 0f && viewport.x >= -0.1f && viewport.x <= 1.1f &&
            viewport.y >= -0.1f && viewport.y <= 1.1f;
    }

    private static bool IsFreePoliceRoadPoint(CarController vehicle, WorldPoint point)
    {
        var hits = Physics.OverlapBox(point.Position + Vector3.up * 1.05f,
            new Vector3(1.15f, 0.65f, 2.65f), point.Rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (var hit in hits)
            if (hit != null && hit.GetComponentInParent<CarController>() != vehicle)
                return false;

        return true;
    }

    private static void ReportPoliceVehicle(CarController vehicle, PoliceCar patrol, float distance)
    {
        var driver = vehicle.DriverAI;
        var target = driver != null ? driver.target : null;
        var agent = patrol != null ? patrol.agent : null;
        string navigation = agent == null ? "chybí" :
            !agent.isActiveAndEnabled ? "neaktivní" : agent.isOnNavMesh ? "na síti" : "mimo síť";
        var obstacle = driver != null ? driver.colliderHit : null;
        var roadPoint = driver != null ? driver.worldPointTarget : null;
        string roadDistance = roadPoint != null
            ? $"{Vector3.Distance(vehicle.transform.position, roadPoint.Position):0} m"
            : "chybí";
        var path = patrol != null ? patrol.path : null;
        string pathStatus = path != null ? path.status.ToString() : "chybí";
        var interact = vehicle.CarInteract;
        var character = interact != null ? interact.Driver : null;
        log.LogInfo($"Police debug: auto #{vehicle.GetInstanceID()} {vehicle.name}, " +
                    $"{distance:0} m, {Mathf.Abs(vehicle.SpeedPerSecond) * 3.6f:0.0} km/h, " +
                    $"řidič={(character != null ? character.name : "bez NPC")}, " +
                    $"hlídka={(patrol != null ? patrol.IsActive.ToString() : "chybí")}, " +
                    $"AI={(driver != null ? driver.targetType.ToString() : "chybí")}, " +
                    $"cíl={(target != null ? target.name : "chybí")}, silniční bod={roadDistance}, " +
                    $"překážka={(obstacle != null ? obstacle.name : "nezjištěna")}, agent={navigation}, trasa={pathStatus}.");
    }

    private static void ReservePoliceCarCapacity(Spawner spawner, int targetCapacity)
    {
        if (reservedPoliceSpawner != spawner)
            RestorePoliceCarCapacity();

        int currentCapacity = spawner.GetMaxSpawned();
        if (currentCapacity >= targetCapacity)
            return;

        if (reservedPoliceSpawner == null || currentCapacity != reservedPoliceCarCapacity)
            originalPoliceCarCapacity = currentCapacity;

        spawner.SetMaxSpawned(targetCapacity);
        reservedPoliceSpawner = spawner;
        reservedPoliceCarCapacity = targetCapacity;
        log.LogInfo($"Policejní posily: kapacita původního spawneru aut {currentCapacity} -> {targetCapacity}.");
    }

    private static void RestorePoliceCarCapacity()
    {
        if (reservedPoliceSpawner != null && reservedPoliceSpawner.GetMaxSpawned() == reservedPoliceCarCapacity)
            reservedPoliceSpawner.SetMaxSpawned(originalPoliceCarCapacity);
        reservedPoliceSpawner = null;
    }

    private static bool CalculatePolicePath(NavMeshAgent __instance, Vector3 __0, NavMeshPath __1, ref bool __result)
    {
        if (__instance.isActiveAndEnabled && __instance.isOnNavMesh)
            return true;

        var patrol = __instance.GetComponentInParent<PoliceCar>();
        if (patrol == null)
        {
            var car = __instance.GetComponentInParent<CarController>();
            patrol = car != null ? car.PoliceCar : null;
        }

        if (patrol == null || patrol.agent != __instance)
            return true;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = __instance.agentTypeID,
            areaMask = __instance.areaMask
        };
        __result = false;
        if (__1 != null)
        {
            __1.ClearCorners();
            if (NavMesh.SamplePosition(__instance.transform.position, out NavMeshHit start, 6f, filter))
                __result = NavMesh.CalculatePath(start.position, __0, filter, __1);
        }

        float now = Time.unscaledTime;
        if (now >= nextPoliceNavigationReport)
        {
            nextPoliceNavigationReport = now + 5f;
            log.LogInfo(__result
                ? "Policejní posily: trasa auta spočtena z nejbližšího bodu na NavMeshi."
                : "Policejní posily: pro auto mimo NavMesh se nepodařilo spočítat trasu.");
        }

        return false;
    }

    private static bool PreparePoliceFootPath(CharacterAgentMovement __instance)
    {
        var officer = __instance.controller;
        if (officer == null || officer.IsPlayer || officer.Faction != Faction.Police)
            return true;

        var agent = __instance.agent;
        if (officer.IsDead || officer.IsInCar || agent == null || !agent.isActiveAndEnabled)
            return false;

        if (agent.isOnNavMesh)
            return true;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        var position = agent.transform.position;
        bool recovered = NavMesh.SamplePosition(position, out NavMeshHit hit, 2f, filter) &&
            Mathf.Abs(hit.position.y - position.y) <= 0.8f &&
            agent.Warp(hit.position) && agent.isOnNavMesh;

        float now = Time.unscaledTime;
        if (now >= nextPoliceFootNavigationReport)
        {
            nextPoliceFootNavigationReport = now + 5f;
            log.LogInfo(recovered
                ? "Policejní posily: pěší agent připojen k NavMeshi."
                : "Policejní posily: výpočet pěší trasy odložen, agent je mimo NavMesh.");
        }

        return recovered;
    }

    private static void AssemblePoliceCrew(WaveSpawner wave, PoliceCar patrol)
    {
        var car = patrol.carController.CarInteract;
        var driver = car != null ? car.Driver : null;
        var prefabs = wave != null ? wave.characterPrefabs : null;
        if (driver == null || driver.IsDead || driver.Faction != Faction.Police ||
            prefabs == null || prefabs.Length == 0 || wave.characters == null)
            return;

        int id = patrol.GetInstanceID();
        if (!AssembledPoliceCars.Add(id))
            return;

        bool later = Time.unscaledTime - policeWantedSince >= 60f;
        int count = later ? UnityEngine.Random.Range(2, 5) : UnityEngine.Random.Range(1, 3);
        var crew = new PoliceTransport(patrol.carController, car);
        foreach (CarSeatType seatType in PolicePassengerSeats)
        {
            if (crew.Officers.Count >= count - 1)
                break;

            var seat = car.GetCarSeat(seatType);
            if (seat == null || seat.Character != null)
                continue;

            var prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Length)];
            if (prefab == null)
                continue;

            var officer = Object.Instantiate(prefab, car.transform.position, car.transform.rotation);
            car.EnterVehicleInstant(officer, seatType);
            if (seat.Character == officer)
            {
                crew.Officers.Add(officer);
                wave.RegisterCharacter(officer);
                if (policeLoadouts)
                    ApplyPoliceLoadout(officer, true);
            }
            else
            {
                Object.Destroy(officer.gameObject);
            }
        }

        if (crew.Officers.Count > 0)
            PoliceTransports.Add(id, crew);
        log.LogInfo($"Policejní posily: auto #{id}, posádka {1 + crew.Officers.Count}/{count} včetně řidiče.");
    }

    private static readonly WeaponType[] PoliceLongWeapons =
    {
        WeaponType.SubmachineGun, WeaponType.Shotgun, WeaponType.AssaultRifle, WeaponType.Rifle, WeaponType.Heavy
    };

    private static void ApplyPoliceLoadout(CharacterControllerBase officer, bool vehicleCrew)
    {
        var brain = officer.AIBrain;
        var inventory = brain != null ? brain.inventory : null;
        if (inventory == null)
            return;

        if (vehicleCrew)
        {
            if (!inventory.HasWeapon(WeaponType.AssaultRifle))
                inventory.AddWeapon(WeaponType.AssaultRifle, 90, false);
            return;
        }

        if (!inventory.HasWeapon(WeaponType.Pistol))
            inventory.AddWeapon(WeaponType.Pistol, 48, false);
        var pistol = inventory.SelectWeapon(WeaponType.Pistol);
        if (pistol == null)
        {
            log.LogWarning($"Policejní výzbroj: pistole není dostupná pro {officer.name}.");
            return;
        }

        inventory.EquipInstant(pistol);
        foreach (var type in PoliceLongWeapons)
            if (inventory.HasWeapon(type))
                inventory.RemoveWeapon(type);
        log.LogInfo($"Policejní výzbroj: pěší posila {officer.name} má pistoli.");
    }

    private static void CheckPoliceArrival(PoliceCar __instance)
    {
        if (PoliceTransports.Count == 0 ||
            !PoliceTransports.TryGetValue(__instance.GetInstanceID(), out var crew))
            return;

        var game = GameManager.instance;
        var player = game != null ? game.PlayerController : null;
        var car = crew.Vehicle;
        if (player == null || car == null)
        {
            ReleaseDropOffBrake(crew);
            return;
        }

        float distance = Vector3.Distance(car.transform.position, player.transform.position);
        float range = policeDropOffDistance + (crew.BrakingForDropOff ? 8f : 0f);
        if (__instance.IsActive && distance > range)
        {
            ReleaseDropOffBrake(crew);
            return;
        }

        // Keep the existing driver in control after the passengers have left.
        if (__instance.IsActive && !crew.BrakingForDropOff && car.DriverAI != null && !car.isBraking)
        {
            car.ToggleDriverAIIsBraking(true);
            crew.BrakingForDropOff = true;
        }

        if (Mathf.Abs(car.SpeedPerSecond) > 1.25f)
            return;

        PoliceTransports.Remove(__instance.GetInstanceID());
        try
        {
            DisembarkPoliceCrew(crew, distance);
        }
        finally
        {
            ReleaseDropOffBrake(crew);
        }
    }

    private static void CancelPoliceDropOff(PoliceCar __instance)
    {
        if (PoliceTransports.Remove(__instance.GetInstanceID(), out var crew))
            ReleaseDropOffBrake(crew);
    }

    private static void EndPolicePursuit(PoliceCar __instance)
    {
        if (PoliceTransports.TryGetValue(__instance.GetInstanceID(), out var crew))
            ReleaseDropOffBrake(crew);
    }

    private static void ReleaseDropOffBrake(PoliceTransport crew)
    {
        if (!crew.BrakingForDropOff)
            return;

        crew.BrakingForDropOff = false;
        if (crew.Vehicle != null && crew.Vehicle.DriverAI != null)
            crew.Vehicle.ToggleDriverAIIsBraking(false);
    }

    private static void DisembarkPoliceCrew(PoliceTransport crew, float distance)
    {
        if (crew.Car == null)
            return;

        int exited = 0;
        foreach (var officer in crew.Officers)
        {
            if (officer == null || officer.IsDead || !crew.Car.Passengers.Contains(officer))
                continue;

            crew.Car.ExitCarInstant(officer);
            if (policeLoadouts)
            {
                var brain = officer.AIBrain;
                var inventory = brain != null ? brain.inventory : null;
                var rifle = inventory != null ? inventory.SelectWeapon(WeaponType.AssaultRifle) : null;
                if (rifle != null)
                    inventory.EquipInstant(rifle);
            }
            exited++;
        }

        if (exited > 0)
            log.LogInfo($"Policejní posily: vystoupilo {exited} policistů, vzdálenost {distance:0} m.");
    }

    private static void BeginWeaponHit(CharacterHealth __instance, RaycastHit __0, bool __3, int __5,
        out (CharacterControllerBase PreviousNpc, int? HealthBefore) __state)
    {
        __state = (weaponHitNpc, damageTraceEnabled && __3 ? __instance.Health : null);
        weaponHitNpc = null;
        var collider = __0.collider;
        if (npcHitReactionStrength <= 1f || __5 <= 0 || collider == null)
            return;

        var target = collider.GetComponentInParent<CharacterControllerBase>();
        if (target != null && !target.IsPlayer && !target.IsDead)
            weaponHitNpc = target;
    }

    private static void EndWeaponHit(CharacterHealth __instance, RaycastHit __0, int __5,
        (CharacterControllerBase PreviousNpc, int? HealthBefore) __state)
    {
        weaponHitNpc = __state.PreviousNpc;
        if (__state.HealthBefore.HasValue)
            LogHealthChange("zásah", __instance, __0, __5, __state.HealthBefore.Value);
    }

    private static void CaptureNpcDamage(CharacterHealth __instance, ref float __0, RaycastHit __2,
        bool __5, out (int? HealthBefore, float OriginalDamage) __state)
    {
        __state = (damageTraceEnabled && __5 ? __instance.Health : null, __0);
        if (!__5 || damageCurveStrength <= 0f || __0 <= 0f || simulatedDamageWeapon == null)
            return;

        var collider = __2.collider;
        var target = collider != null ? collider.GetComponentInParent<CharacterControllerBase>() : null;
        if (target == null || target.IsPlayer || target.IsDead)
            return;

        const float damageBreakpoint = 20f;
        float bonus = Mathf.Min(__0, damageBreakpoint) + Mathf.Max(0f, __0 - damageBreakpoint) * 0.25f;
        __0 += bonus * damageCurveStrength;
    }

    private static void LogNpcDamage(CharacterHealth __instance, float __0, RaycastHit __2,
        (int? HealthBefore, float OriginalDamage) __state)
    {
        if (__state.HealthBefore.HasValue)
            LogHealthChange("TakeDamage", __instance, __2, __0, __state.HealthBefore.Value, __state.OriginalDamage);
    }

    private static void LogHealthChange(string phase, CharacterHealth health, RaycastHit hit, float damage, int before,
        float? originalDamage = null)
    {
        var collider = hit.collider;
        var target = collider != null ? collider.GetComponentInParent<CharacterControllerBase>() : null;
        target ??= health.GetComponentInParent<CharacterControllerBase>();
        if (target == null || target.IsPlayer)
            return;

        var weapon = simulatedDamageWeapon;
        int after = health.Health;
        string damageText = originalDamage.HasValue && originalDamage.Value != damage
            ? $"{originalDamage.Value:0.##}->{damage:0.##}"
            : $"{damage:0.##}";
        log.LogInfo($"NPC damage [{phase}]: zbraň={(weapon != null ? weapon.name : "neznámá")}, " +
                    $"NPC={target.name}#{target.GetInstanceID()}, část={(collider != null ? collider.name : "neznámá")}, " +
                    $"vstup={damageText}, HP={before}->{after}, úbytek={before - after}.");
    }

    private static void SelectCameraRecoil(Recoil __instance, CharacterControllerBase __0)
    {
        if (__0 == null || !__0.IsPlayer)
            return;

        var weapon = __instance.weapon;
        activeCameraRecoilScale = weapon != null &&
            weapon.name.Contains("skorpion", StringComparison.OrdinalIgnoreCase)
            ? skorpionCameraRecoilScale
            : cameraRecoilScale;
    }

    private static void ScaleCameraRecoil(ref Vector2 __0) => __0 *= activeCameraRecoilScale;

    private static void ScaleNpcHitReaction(Collider __0, ref Vector3 __1)
    {
        var target = weaponHitNpc;
        if (target == null || target.IsDead || __0 == null ||
            __0.GetComponentInParent<CharacterControllerBase>() != target)
            return;

        __1 *= npcHitReactionStrength;
        if (!reportedNpcHitReaction)
        {
            reportedNpcHitReaction = true;
            log.LogInfo($"Reakce NPC na střelu: násobek síly {npcHitReactionStrength:F1}.");
        }
    }

    private static void UnregisterBrain(CharacterAIBrain __instance) =>
        CombatMarkers.Brains.Remove(__instance.GetInstanceID());

    private static void StartBulletTiming(Weapon __instance, out (long Timestamp, Weapon PreviousWeapon) __state)
    {
        __state = (combatTraceEnabled ? Stopwatch.GetTimestamp() : 0, simulatedDamageWeapon);
        if (damageTraceEnabled || damageCurveStrength > 0f)
            simulatedDamageWeapon = __instance;
    }

    private static void EndBulletTiming((long Timestamp, Weapon PreviousWeapon) __state)
    {
        simulatedDamageWeapon = __state.PreviousWeapon;
        if (__state.Timestamp == 0)
            return;

        double ms = (Stopwatch.GetTimestamp() - __state.Timestamp) * (1000d / Stopwatch.Frequency);
        if (ms < 5 || Time.realtimeSinceStartup < nextTimingReport)
            return;

        nextTimingReport = Time.realtimeSinceStartup + 1f;
        log.LogInfo($"Simulace střel: {ms:F2} ms v jednom volání.");
    }

    private static void BeginShotScope(Weapon __instance, out Weapon __state)
    {
        __state = activeShotWeapon;
        activeShotWeapon = __instance;
    }

    private static void EndShotScope(Weapon __state) => activeShotWeapon = __state;

    private static void HandleShotInstance(FMOD.Studio.EventDescription __instance, ref FMOD.Studio.EventInstance __0,
        FMOD.RESULT __result)
    {
        var weapon = activeShotWeapon;
        if (ReferenceEquals(weapon, null) || __result != FMOD.RESULT.OK ||
            __instance.getID(out var id) != FMOD.RESULT.OK || !id.Equals(weapon.shotSound.Guid))
            return;

        var owner = weapon.owner;
        if (owner == null)
            return;

        if (owner.IsPlayer ||
            __0.getVolume(out float volume) != FMOD.RESULT.OK || !float.IsFinite(volume))
            return;

        var cameraManager = CameraManager.instance;
        var camera = cameraManager == null ? Camera.main : cameraManager.Camera;
        if (camera == null)
            return;

        float distance = Vector3.Distance(weapon.transform.position, camera.transform.position);
        if (distance <= 15f)
            return;

        float gain = Mathf.Lerp(1f, npcShotGain, Mathf.InverseLerp(15f, 60f, distance));
        if (__0.setVolume(volume * gain) == FMOD.RESULT.OK && !reportedNpcGain)
        {
            reportedNpcGain = true;
            log.LogInfo($"Zesílení vzdáleného výstřelu NPC: ×{gain:F2} při {distance:F1} m.");
        }
    }

    private static void InspectShot(Weapon __instance)
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextAudioReport && now < nextPlayerAudioReport)
            return;

        var owner = __instance.owner;
        bool player = owner != null && owner.IsPlayer;
        ref float nextReport = ref (player ? ref nextPlayerAudioReport : ref nextAudioReport);
        if (now < nextReport)
            return;

        nextReport = now + 3f;
        try
        {
            var shot = RuntimeManager.GetEventDescription(__instance.shotSound);
            var result = shot.is3D(out bool spatial);
            var instances = shot.getInstanceCount(out int count);
            string shooter = owner == null ? "neznámý" : player ? "hráč" : $"NPC {owner.name}";
            log.LogInfo($"Výstřel ({shooter}): zdroj={__instance.transform.position}, " +
                        $"FMOD posluchači={StudioListener.ListenerCount}, 3D={spatial} ({result}), instance={count} ({instances}).");
        }
        catch (Exception exception)
        {
            log.LogWarning($"Kontrola zvuku výstřelu: {exception.Message}");
        }
    }
}

public sealed class PoliceDispatch : MonoBehaviour
{
    private float nextCheck;

    public PoliceDispatch(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck)
            return;

        nextCheck = Time.unscaledTime + 1f;
        CombatPlugin.DispatchPoliceCar();
    }
}

public sealed class CombatMarkers : MonoBehaviour
{
    internal static readonly Dictionary<int, CharacterAIBrain> Brains = new();
    private readonly Dictionary<int, Image> icons = new();
    private readonly Dictionary<int, Transform> markerTargets = new();
    private readonly HashSet<int> policeCarTargets = new();
    private readonly List<CarController> policeCars = new();
    private readonly List<int> expired = new();
    private VisualElement overlay;
    private VisualElement minimapElement;
    private Texture2D dot;
    private CharacterControllerBase player;
    private float nextRefresh;
    private float nextPoliceCarScan;
    private ManualLogSource log;
    private bool reportedPoliceCarMarker;

    public CombatMarkers(IntPtr pointer) : base(pointer) { }

    public void Initialize(ManualLogSource source) => log = source;

    internal void RenderOverlay(MiniMapView view)
    {
        var element = view.ShaderElement;
        if (element == null || element.panel == null)
            return;

        if (minimapElement == null || minimapElement.Pointer != element.Pointer)
        {
            ClearOverlay();
            minimapElement = element;
            overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0f;
            overlay.style.right = 0f;
            overlay.style.top = 0f;
            overlay.style.bottom = 0f;
            element.Add(overlay);
            nextRefresh = 0f;
        }

        if (Time.unscaledTime >= nextRefresh)
        {
            nextRefresh = Time.unscaledTime + 0.33f;
            RefreshOverlayTargets();
        }

        if (player == null || player.IsDead)
            return;

        var center = view.ConvertWorldToViewSpace(player.transform.position, false, false);
        foreach (var pair in icons)
        {
            var target = markerTargets[pair.Key];
            var marker = pair.Value;
            if (target == null)
            {
                marker.style.display = DisplayStyle.None;
                continue;
            }

            var offset = view.ConvertWorldToViewSpace(target.position, false, false) - center;
            if (offset.sqrMagnitude > 0.47f * 0.47f)
            {
                marker.style.display = DisplayStyle.None;
                continue;
            }

            marker.style.display = DisplayStyle.Flex;
            marker.style.left = Length.Percent((0.5f + offset.x) * 100f);
            marker.style.top = Length.Percent((0.5f - offset.y) * 100f);
            if (!reportedPoliceCarMarker && policeCarTargets.Contains(pair.Key))
            {
                reportedPoliceCarMarker = true;
                log?.LogInfo("Na minimapě vykreslena vlastní tečka policejního auta.");
            }
        }
    }

    private void RefreshOverlayTargets()
    {
        var game = GameManager.instance;
        player = game != null ? game.PlayerController : null;
        markerTargets.Clear();
        policeCarTargets.Clear();
        if (player != null && !player.IsDead)
        {
            var position = player.transform.position;
            AddPoliceCarTargets(player, position);
            foreach (var pair in Brains)
                if (IsAttacker(pair.Value, player, position))
                    markerTargets[pair.Key] = pair.Value.Controller.transform;
        }

        expired.Clear();
        foreach (var pair in icons)
            if (!markerTargets.ContainsKey(pair.Key))
                expired.Add(pair.Key);

        foreach (int id in expired)
        {
            icons[id].RemoveFromHierarchy();
            icons.Remove(id);
        }

        foreach (var pair in markerTargets)
        {
            if (icons.Count >= 12)
                break;
            if (icons.ContainsKey(pair.Key))
                continue;

            dot ??= CreateDot(16, 16, false);
            var marker = new Image { image = dot, pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = 10f;
            marker.style.height = 10f;
            marker.style.marginLeft = -5f;
            marker.style.marginTop = -5f;
            overlay.Add(marker);
            icons.Add(pair.Key, marker);
        }
    }

    private void ClearOverlay()
    {
        if (overlay != null)
            overlay.RemoveFromHierarchy();
        overlay = null;
        icons.Clear();
        markerTargets.Clear();
    }

    private void AddPoliceCarTargets(CharacterControllerBase player, Vector3 origin)
    {
        policeCarTargets.Clear();
        if (WantedSystem.WantedLevel != WantedLevel.Active)
        {
            policeCars.Clear();
            nextPoliceCarScan = 0f;
            return;
        }

        var spawner = WorldManager.CarPoliceSpawner;
        if (Time.unscaledTime >= nextPoliceCarScan)
        {
            nextPoliceCarScan = Time.unscaledTime + 1f;
            policeCars.Clear();
            foreach (var vehicle in Object.FindObjectsByType<CarController>(FindObjectsSortMode.None))
                if (vehicle != null && (vehicle.isPolice || vehicle.PoliceCar != null ||
                                        (spawner != null && vehicle.Spawner == spawner)))
                    policeCars.Add(vehicle);
        }

        foreach (var vehicle in policeCars)
        {
            if (vehicle == null || !vehicle.gameObject.activeInHierarchy ||
                (vehicle.transform.position - origin).sqrMagnitude > 130f * 130f)
                continue;

            var car = vehicle.CarInteract;
            if (car != null && (car.Driver == player ||
                (car.Passengers != null && car.Passengers.Contains(player))))
                continue;

            var driver = car != null ? car.Driver : null;
            bool hasPoliceDriver = driver != null && !driver.IsDead && driver.Faction == Faction.Police;
            bool hasTrafficDriver = driver == null && vehicle.DriverAI != null &&
                (vehicle.DriverAI.isPoliceActive || (spawner != null && vehicle.Spawner == spawner));
            if (hasPoliceDriver || hasTrafficDriver || HasPolicePassenger(car))
            {
                int id = vehicle.GetInstanceID();
                markerTargets[id] = vehicle.transform;
                policeCarTargets.Add(id);
            }
        }
    }

    private static bool HasPolicePassenger(CarInteract car)
    {
        if (car == null || car.Passengers == null)
            return false;

        foreach (var occupant in car.Passengers)
            if (occupant != null && !occupant.IsDead && occupant.Faction == Faction.Police)
                return true;

        return false;
    }

    private static bool IsAttacker(CharacterAIBrain brain, CharacterControllerBase player, Vector3 origin)
    {
        if (brain == null || !brain.gameObject.activeInHierarchy)
            return false;

        var npc = brain.Controller;
        if (npc == null || npc == player || npc.IsDead ||
            (npc.transform.position - origin).sqrMagnitude > 14_400f)
            return false;

        if (npc.IsInCar && npc.Faction == Faction.Police && WantedSystem.WantedLevel == WantedLevel.Active)
            return false;

        var attack = brain.ActiveBehaviour?.TryCast<AttackBehaviour>();
        return attack != null && attack.isActive && attack.target == player;
    }

    private static Texture2D CreateDot(int width, int height, bool mipmaps)
    {
        var image = new Texture2D(width, height, TextureFormat.RGBA32, mipmaps);
        var red = new Color(1f, 0.18f, 0.18f, 1f);
        float radius = Mathf.Min(width, height) * 0.3125f;
        var pixels = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<Color>(width * height);
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float dx = x + 0.5f - width * 0.5f;
            float dy = y + 0.5f - height * 0.5f;
            pixels[y * width + x] = dx * dx + dy * dy <= radius * radius ? red : Color.clear;
        }
        image.SetPixels(pixels);
        image.Apply(mipmaps, false);
        return image;
    }

    private void OnDestroy()
    {
        ClearOverlay();
        Brains.Clear();
        if (dot != null)
            Object.Destroy(dot);
    }
}
