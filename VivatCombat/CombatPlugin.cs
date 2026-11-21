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

[BepInPlugin("local.vivat.combat", "Vivat Combat", "0.1.34")]
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
    private static readonly Dictionary<int, PoliceCarProgress> PoliceProgress = new();
    private static readonly Dictionary<int, PatrolApproach> PatrolApproaches = new();
    private static Spawner replacementSpawner;
    private static float nextLowStrengthDispatch;
    private static readonly HashSet<int> ReplacedEmptyCars = new();
    private static readonly Dictionary<int, float> EmptyCarRetryAt = new();
    private static readonly HashSet<int> EmptyCarsThisScan = new();
    private static int countBeforeReplacement;
    private static float replacementTime;

    private sealed class PatrolApproach
    {
        internal Vector3 Anchor;
        internal float Since;
        internal bool Retried;
    }
    private static readonly HashSet<int> DismountedPatrols = new();
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
        internal float ReverseUntil;
        internal Vector3 ReverseStart;
        internal Vector3 ReverseDirection;
    }
    private static Spawner reservedPoliceSpawner;
    private static int originalPoliceCarCapacity;
    private static int reservedPoliceCarCapacity;
    private static bool reportedMissingPoliceSpawner;
    private static float nextPoliceNavigationReport;
    private static float nextPoliceFootNavigationReport;
    private static readonly HashSet<int> AssembledPoliceCars = new();
    private static readonly Dictionary<int, float> PoliceCrewRetryAt = new();
    private static readonly Dictionary<int, PoliceTransport> PoliceTransports = new();
    private static readonly CarSeatType[] PolicePassengerSeats =
        { CarSeatType.FrontRight, CarSeatType.BackLeft, CarSeatType.BackRight };

    private sealed class PoliceTransport
    {
        internal readonly CarController Vehicle;
        internal readonly CarInteract Car;
        internal readonly List<CharacterControllerBase> Officers = new(3);
        internal readonly List<CharacterControllerBase> CreatedOfficers = new(3);
        internal bool BrakingForDropOff;
        internal bool ExitInProgress;
        internal float RetryExitAt;

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
        Log.LogInfo("Police dispatch build: 0.1.34 (native agent rebinding, civilian path guard, crew spawn verification).");
        bool customPoliceEnabled = Config.Bind("Police", "CustomLogicEnabled", true,
            "Zapne vlastní logiku policejních posil, dopravy, výzbroje a navigace. Po změně restartuj hru.").Value;
        bool transportReinforcements = Config.Bind("Police", "TransportReinforcements", true,
            "Při pátrání obsadí volná místa v přijíždějící policejní hlídce pěšími posilami.").Value;
        bool separateLoadouts = Config.Bind("Police", "SeparateLoadouts", true,
            "Pěší posily dostanou pistoli, posádka z auta může mít útočnou pušku.").Value;
        if (!customPoliceEnabled)
            Log.LogInfo("Policejní posily: CustomLogicEnabled = false, používá se původní logika hry.");
        debugInitialPoliceCars = Config.Bind("Police", "DebugInitialCars", 0, new ConfigDescription(
            "Počet aut vyžádaných při startu pátrání pro testování spawneru. Nemění posádky ani aktivaci AI.",
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
                Patch(harmony, typeof(DriverAI), "FixedTick", Type.EmptyTypes, nameof(ReverseStalledPatrol));
                Patch(harmony, typeof(PoliceCar), "Update", Type.EmptyTypes, nameof(CheckPoliceArrival));
                var deactivation = AccessTools.DeclaredMethod(typeof(PoliceCar), "OnWantedLevelDeactivated",
                    Type.EmptyTypes) ?? throw new MissingMethodException(nameof(PoliceCar), "OnWantedLevelDeactivated");
                harmony.Patch(deactivation, prefix: Callback(nameof(EndPolicePursuit)));
                Log.LogInfo("Harmony: PoliceCar.OnWantedLevelDeactivated");
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
            ReplacedEmptyCars.Clear();
            EmptyCarRetryAt.Clear();
            nextLowStrengthDispatch = 0f;
            nextPoliceDispatchDebug = 0f;
            foreach (var crew in PoliceTransports.Values)
                ReleaseDropOffBrake(crew);
            // End every outstanding transition before a new wanted episode.
            // Unseated prefab instances do not belong to the game spawner yet.
            foreach (var entry in PendingSeats)
            {
                if (entry.Officer != null && !entry.Officer.IsInCar)
                    Object.Destroy(entry.Officer.gameObject);
            }
            PendingSeats.Clear();
            PendingExits.Clear();
            PoliceTransports.Clear();
            PoliceCrewRetryAt.Clear();
            PoliceProgress.Clear();
            PatrolApproaches.Clear();
            replacementSpawner = null;
            replacementTime = 0f;
            DismountedPatrols.Clear();
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
        int activeTransports = 0;
        foreach (var existing in PoliceTransports.Values)
        {
            var transportCar = existing.Vehicle;
            if (transportCar != null && transportCar.gameObject.activeInHierarchy &&
                transportCar.PoliceCar != null && transportCar.PoliceCar.IsActive &&
                (transportCar.transform.position - playerPosition).sqrMagnitude <= 130f * 130f)
                activeTransports++;
        }
        EmptyCarsThisScan.Clear();
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

            // A police-looking traffic car without the PoliceCar component is not
            // a dispatchable patrol. In particular, wrecks that have already gone
            // through DriverDeath/Despawn may remain visible for some time.
            // Do not include these objects in crew or empty-patrol replacement.
            if (patrol == null)
                continue;

            if (nativePoliceVehicle &&
                ReplaceStalledPatrol(vehicle, patrol, spawner, wanted.waveSpawner, player))
                continue;

            if (patrol != null && distance <= 230f * 230f)
                RecoverStoppedPoliceCar(vehicle, patrol, spawner, playerPosition);

            if (distance > 130f * 130f)
                continue;

            var car = vehicle.CarInteract;
            var driver = car != null ? car.Driver : null;
            if (driver != null && !driver.IsDead)
                ReplacedEmptyCars.Remove(vehicle.GetInstanceID());
            if (car == null || vehicle.IsPlayer || driver == player)
                continue;
            bool livePassenger = false;
            if (car.Passengers != null)
                foreach (var passenger in car.Passengers)
                    if (passenger != null && !passenger.IsDead)
                    {
                        livePassenger = true;
                        break;
                    }
            if ((driver == null || driver.IsDead) && !livePassenger)
            {
                // Only replace actual native police spawner patrols. Other police
                // cars can be wrecks, mission vehicles or ordinary traffic.
                if (nativePoliceVehicle)
                    EmptyCarsThisScan.Add(vehicle.GetInstanceID());
                continue;
            }
            if (driver == null || driver.IsDead || driver.Faction != Faction.Police)
                continue;

            // Stuck vehicles must not suppress pedestrian backup or count as responding patrols.
            bool stalled = PoliceProgress.TryGetValue(vehicle.GetInstanceID(), out var progress) &&
                Time.unscaledTime - progress.StationarySince >= 9f &&
                Mathf.Abs(vehicle.SpeedPerSecond) < 1f;
            bool stoppingForCrew = patrol != null &&
                PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var transport) &&
                (transport.BrakingForDropOff || transport.ExitInProgress);
            if (patrol != null && patrol.IsActive && (!stalled || stoppingForCrew))
                nearbyPatrols++;

            if (patrol != null && patrol.IsActive && distance >= 32f * 32f)
            {
                bool tracked = PoliceTransports.ContainsKey(patrol.GetInstanceID());
                if (tracked || activeTransports < targetPatrols)
                {
                    AssemblePoliceCrew(wanted.waveSpawner, patrol);
                    if (!tracked && PoliceTransports.ContainsKey(patrol.GetInstanceID()))
                        activeTransports++;
                }
            }
        }

        if (reportDebug)
        {
            string population = spawner != null ? $"{spawner.Spawned}/{spawner.GetMaxSpawned()}" : "nedostupný";
            log.LogInfo($"Police debug: {wantedDuration:0} s wanted, poblíž {nearbyPatrols}/{targetPatrols}, " +
                        $"policejní spawner {population}.");
        }

        if (policeBalanceEnabled)
        {
            // Keep the wave cap in place while cars are unavailable. Lowering it
            // after the first foot officers arrive can interfere with their lifecycle.
            bool needsFootFallback = nearbyPatrols == 0 && wantedDuration >= 15f;
            SetPoliceFootFallback(wanted.waveSpawner, needsFootFallback ? policeFootFallbackLimit : 0);
        }

        if (spawner != null)
        {
            ReservePoliceCarCapacity(spawner, targetPatrols);
            bool replacingStalled = replacementSpawner != null;
            RequestReplacementIfSlotIsFree(spawner, nearbyPatrols, targetPatrols);
            if (replacingStalled)
                nextLowStrengthDispatch = Time.unscaledTime + 8f;
            else
                ReinforceEmptyPatrol(spawner, playerPosition, nearbyPatrols, targetPatrols);
        }
        else if (!reportedMissingPoliceSpawner)
        {
            reportedMissingPoliceSpawner = true;
            log.LogWarning("Policejní posily: původní spawner policejních aut není v této scéně dostupný.");
        }
    }

    private static void ReinforceEmptyPatrol(Spawner spawner, Vector3 playerPosition, int nearbyPatrols, int targetPatrols)
    {
        if (nearbyPatrols >= targetPatrols || Time.unscaledTime < nextLowStrengthDispatch)
            return;

        foreach (int carId in EmptyCarsThisScan)
        {
            if (ReplacedEmptyCars.Contains(carId) ||
                (EmptyCarRetryAt.TryGetValue(carId, out var nextAttempt) && Time.unscaledTime < nextAttempt))
                continue;

            int liveOfficers = 0;
            foreach (var officer in Object.FindObjectsByType<CharacterControllerBase>(FindObjectsSortMode.None))
            {
                if (officer != null && !officer.IsPlayer && !officer.IsDead && officer.Faction == Faction.Police &&
                    (officer.transform.position - playerPosition).sqrMagnitude <= 130f * 130f)
                    liveOfficers++;
            }
            if (liveOfficers >= 2 * targetPatrols)
                return;

            int before = spawner.Spawned;
            nextLowStrengthDispatch = Time.unscaledTime + 8f;
            EmptyCarRetryAt[carId] = Time.unscaledTime + 25f;
            if (spawner.availableSpawnpoints <= 0)
                return;
            ReservePoliceCarCapacity(spawner, Math.Max(targetPatrols, before + 1));
            spawner.Spawn();
            if (spawner.Spawned > before)
            {
                ReplacedEmptyCars.Add(carId);
                log.LogInfo($"Policejní posily: náhrada za prázdné auto #{carId}, živí policisté {liveOfficers}/{2 * targetPatrols}, auta spawneru {before}->{spawner.Spawned}.");
            }
            else
            {
                log.LogInfo($"Policejní posily: náhradní vůz za #{carId} se nespawnul (stav {spawner.Spawned}/{spawner.GetMaxSpawned()}); opakování nejdříve za 25 s.");
            }
            return;
        }
    }

    private static bool ReplaceStalledPatrol(CarController vehicle, PoliceCar patrol, Spawner spawner,
        WaveSpawner wave, CharacterControllerBase player)
    {
        int id = vehicle.GetInstanceID();
        var position = vehicle.transform.position;
        var playerPosition = player.transform.position;
        float distance = Vector3.Distance(position, playerPosition);
        var car = vehicle.CarInteract;
        var driver = car != null ? car.Driver : null;
        if (distance <= 100f || !patrol.IsActive || vehicle.IsPlayer || driver == null || driver.IsDead ||
            driver == player || (car.Passengers != null && car.Passengers.Contains(player)))
        {
            PatrolApproaches.Remove(id);
            return false;
        }

        float now = Time.unscaledTime;
        if (!PatrolApproaches.TryGetValue(id, out var approach))
        {
            PatrolApproaches.Add(id, new PatrolApproach { Anchor = position, Since = now });
            return false;
        }

        // A road route can temporarily lead away from a moving player.
        // Treat real vehicle movement as progress, not only shrinking straight-line distance.
        if (Vector3.Distance(approach.Anchor, position) >= 15f)
        {
            approach.Anchor = position;
            approach.Since = now;
            approach.Retried = false;
            return false;
        }

        if (now - approach.Since < (approach.Retried ? 7f : 30f) || replacementSpawner != null ||
            now - replacementTime < 5f || Mathf.Abs(vehicle.SpeedPerSecond) >= 2f)
            return false;

        if (!approach.Retried)
        {
            approach.Retried = true;
            approach.Since = now;
            patrol.Repath();
            log.LogInfo($"Policejní posily: auto #{id} se nepřibližuje, poslední pokus o obnovu trasy.");
            return false;
        }

        var camera = Camera.main;
        if (camera == null || IsInsideCamera(camera, position) || spawner.Spawned <= 0)
            return false;

        if (PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var crew))
        {
            foreach (var officer in crew.CreatedOfficers)
            {
                if (officer == null || car.Passengers == null || !car.Passengers.Contains(officer))
                    continue;

                car.ExitCarInstant(officer);
                if (car.Passengers.Contains(officer))
                    continue;

                if (wave != null)
                    wave.UnregisterCharacter(officer);
                Object.Destroy(officer.gameObject);
            }
        }

        replacementSpawner = spawner;
        countBeforeReplacement = spawner.Spawned;
        replacementTime = now;
        PatrolApproaches.Remove(id);
        PoliceProgress.Remove(id);
        spawner.Despawn(vehicle.gameObject);
        log.LogInfo($"Policejní posily: vzdálené zaseknuté auto #{id} předáno ke stažení, {distance:0} m.");
        return true;
    }

    private static void RequestReplacementIfSlotIsFree(Spawner spawner, int nearbyPatrols, int targetPatrols)
    {
        if (replacementSpawner == null)
            return;

        float elapsed = Time.unscaledTime - replacementTime;
        if (replacementSpawner != spawner || elapsed >= 10f || nearbyPatrols >= targetPatrols)
        {
            replacementSpawner = null;
            return;
        }

        if (elapsed < 2f || spawner.Spawned >= countBeforeReplacement ||
            spawner.Spawned >= spawner.GetMaxSpawned())
            return;

        replacementSpawner = null;
        spawner.Spawn();
        log.LogInfo("Policejní posily: vyžádáno náhradní auto po uvolnění místa.");
    }

    private static bool RearIsClear(CarController car)
    {
        var box = car.collider;
        if (box == null)
            return false;
        var t = box.transform;
        var scale = t.lossyScale;
        var half = Vector3.Scale(box.size, scale) * 0.5f;
        var rear = t.TransformPoint(box.center) - t.forward * (Mathf.Abs(half.z) + 1.5f);
        if (!Physics.Raycast(rear + Vector3.up * 1.5f, Vector3.down, 4f, car.groundMask, QueryTriggerInteraction.Ignore))
            return false;
        foreach (var obstacle in Physics.OverlapBox(rear, new Vector3(Mathf.Abs(half.x) + 0.2f,
                     Mathf.Max(0.25f, Mathf.Abs(half.y) * 0.7f), 1.55f), t.rotation, ~0, QueryTriggerInteraction.Ignore))
            if (obstacle != null && !obstacle.transform.IsChildOf(car.transform))
                return false;
        return true;
    }

    private static void ReverseStalledPatrol(DriverAI __instance)
    {
        var car = __instance.carController;
        if (car == null || !PoliceProgress.TryGetValue(car.GetInstanceID(), out var progress) ||
            progress.ReverseUntil <= 0f)
            return;
        var patrol = car.PoliceCar;
        float meters = Vector3.Dot(car.transform.position - progress.ReverseStart, progress.ReverseDirection);
        if (!car.IsPlayer && patrol != null && patrol.IsActive && car.CarInteract?.Driver != null &&
            !(PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var crew) && crew.BrakingForDropOff) &&
            Time.time < progress.ReverseUntil && meters < 2.5f && RearIsClear(car))
        {
            car.input = new Vector2(0f, Mathf.Abs(car.SpeedPerSecond) < 1.8f ? -0.55f : 0f);
            return;
        }
        progress.ReverseUntil = 0f;
        if (!car.IsPlayer && patrol != null && patrol.IsActive)
            patrol.Repath();
        log.LogInfo($"Policejní posily: konec couvání #{car.GetInstanceID()}, posun dozadu {meters:F1} m.");
    }

    private static void RecoverStoppedPoliceCar(CarController vehicle, PoliceCar patrol, Spawner spawner,
        Vector3 playerPosition)
    {
        int id = vehicle.GetInstanceID();
        var driver = vehicle.DriverAI;
        var interact = vehicle.CarInteract;
        var officer = interact != null ? interact.Driver : null;
        float distanceSquared = (vehicle.transform.position - playerPosition).sqrMagnitude;
        if (PoliceProgress.TryGetValue(id, out var reversing) && reversing.ReverseUntil > 0f)
            return;
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
            if (vehicle.IsWheelPhysicsEnabled && RearIsClear(vehicle))
            {
                progress.ReverseStart = position;
                progress.ReverseDirection = -vehicle.transform.forward;
                progress.ReverseUntil = Time.time + 2.5f;
                log.LogInfo($"Policejní posily: zahájeno couvání auta #{id}.");
                return;
            }
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
            if (PatrolApproaches.TryGetValue(id, out var approach))
                approach.Anchor = destination;
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

    // An invalid NavMeshAgent.CalculatePath call can come from the game's
    // civilian AI as well as from a police prefab. Guard ALL invalid agents;
    // keep native CalculatePath untouched when the agent is usable.
    private static bool CalculatePolicePath(NavMeshAgent __instance, Vector3 __0, NavMeshPath __1, ref bool __result)
    {
        if (__instance.isActiveAndEnabled && __instance.isOnNavMesh)
            return true;

        var officer = __instance.GetComponentInParent<CharacterControllerBase>();
        if (officer != null && !officer.IsPlayer && !officer.IsDead && !officer.IsInCar &&
            TryRecoverPedestrianAgent(__instance, officer))
            return true;

        var patrol = __instance.GetComponentInParent<PoliceCar>();
        if (patrol == null)
        {
            var car = __instance.GetComponentInParent<CarController>();
            patrol = car != null ? car.PoliceCar : null;
        }

        __result = false;
        __1?.ClearCorners();
        if (patrol != null && patrol.agent == __instance && __1 != null)
        {
            // A police car may still use the static NavMesh query when its
            // own agent has not been attached to the NavMesh yet.
            var filter = new NavMeshQueryFilter
            {
                agentTypeID = __instance.agentTypeID,
                areaMask = __instance.areaMask
            };
            if (NavMesh.SamplePosition(__instance.transform.position, out NavMeshHit start, 6f, filter))
                __result = NavMesh.CalculatePath(start.position, __0, filter, __1);
        }

        float now = Time.unscaledTime;
        if (now >= nextPoliceNavigationReport)
        {
            nextPoliceNavigationReport = now + 5f;
            log.LogWarning($"NavMesh: neplatná žádost o trasu, agent={__instance.name}, " +
                $"NPC={(officer != null ? officer.name : "nezjištěno")}, " +
                $"policejní auto={(patrol != null)}, aktivní={__instance.isActiveAndEnabled}, " +
                $"na síti={__instance.isOnNavMesh}, statická trasa={__result}.");
        }
        return false;
    }

    private static bool TryRecoverPedestrianAgent(NavMeshAgent agent, CharacterControllerBase officer)
    {
        if (agent == null || !agent.isActiveAndEnabled || agent.isOnNavMesh)
            return agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh;
        if (officer == null || officer.IsDead || officer.IsInCar ||
            officer.StateMachine?.ActiveStateType == CharacterStateType.ExitCar)
            return false;

        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        Vector3 position = agent.transform.position;
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 2.5f, filter) ||
            Mathf.Abs(hit.position.y - position.y) > 0.8f)
            return false;

        // Warp may fail after Unity's native NavMeshAgent creation failed. Rebind
        // the component at the sampled position instead of repeatedly calling
        // CalculatePath on an agent with no native NavMesh registration.
        if (agent.Warp(hit.position) && agent.isOnNavMesh)
            return true;

        Vector3 delta = hit.position - position;
        agent.enabled = false;
        officer.transform.position += delta;
        agent.enabled = true;
        bool recovered = agent.isOnNavMesh;
        if (recovered && Time.unscaledTime >= nextPoliceFootNavigationReport)
        {
            nextPoliceFootNavigationReport = Time.unscaledTime + 5f;
            log.LogInfo($"NavMesh: obnovené připojení NPC {officer.name}, oprava {delta.magnitude:0.0} m.");
        }
        return recovered;
    }

    private static bool PreparePoliceFootPath(CharacterAgentMovement __instance)
    {
        var officer = __instance.controller;
        if (officer == null || officer.IsPlayer)
            return true;

        var agent = __instance.agent;
        if (officer.IsDead || officer.IsInCar || agent == null)
            return false;
        if (TryRecoverPedestrianAgent(agent, officer))
            return true;

        float now = Time.unscaledTime;
        if (now >= nextPoliceFootNavigationReport)
        {
            nextPoliceFootNavigationReport = now + 5f;
            log.LogWarning($"NavMesh: trasa NPC {officer.name} odložena, " +
                $"policie={officer.Faction == Faction.Police}, agent aktivní={agent.isActiveAndEnabled}, " +
                $"na síti={agent.isOnNavMesh}, typ={agent.agentTypeID}.");
        }
        return false;
    }

    private static bool TryFindOfficerSpawnPosition(CharacterControllerBase prefab, Vector3 origin,
        Quaternion rotation, out Vector3 spawn)
    {
        spawn = origin;
        var agent = prefab.GetComponentInChildren<NavMeshAgent>(true);
        if (agent == null)
            return true;

        // The NavMeshAgent is not always located at the prefab root. Sampling
        // the root at a walkable position can leave a CHILD agent off-mesh.
        Vector3 childOffset = prefab.transform.InverseTransformPoint(agent.transform.position);
        Vector3 rotatedOffset = rotation * Vector3.Scale(childOffset, prefab.transform.lossyScale);
        Vector3 expectedAgentPosition = origin + rotatedOffset;
        var filter = new NavMeshQueryFilter
        {
            agentTypeID = agent.agentTypeID,
            areaMask = agent.areaMask
        };
        if (!NavMesh.SamplePosition(expectedAgentPosition, out NavMeshHit hit, 8f, filter) ||
            Mathf.Abs(expectedAgentPosition.y - hit.position.y) > 2f)
            return false;

        spawn = hit.position - rotatedOffset;
        return true;
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
        if (PoliceCrewRetryAt.TryGetValue(id, out var retryAt) && Time.unscaledTime < retryAt)
            return;
        bool alreadyTracked = PoliceTransports.TryGetValue(id, out var crew);
        if (AssembledPoliceCars.Contains(id) ||
            (alreadyTracked && (crew.BrakingForDropOff || crew.ExitInProgress)))
            return;

        bool later = Time.unscaledTime - policeWantedSince >= 60f;
        int count = later ? UnityEngine.Random.Range(2, 5) : 2;
        crew ??= new PoliceTransport(patrol.carController, car);
        foreach (CarSeatType seatType in PolicePassengerSeats)
        {
            if (crew.Officers.Count >= count - 1)
                break;

            var seat = car.GetCarSeat(seatType);
            if (seat == null)
                continue;

            var occupant = seat.Character;
            if (occupant != null)
            {
                if (!occupant.IsDead && occupant.Faction == Faction.Police &&
                    !crew.Officers.Contains(occupant))
                    crew.Officers.Add(occupant);
                continue;
            }

            var prefab = prefabs[UnityEngine.Random.Range(0, prefabs.Length)];
            if (prefab == null)
                continue;

            if (!TryFindOfficerSpawnPosition(prefab, car.transform.position, car.transform.rotation, out Vector3 spawnPosition))
            {
                PoliceCrewRetryAt[id] = Time.unscaledTime + 10f;
                log.LogWarning($"Policejní posily: posádka #{id} čeká na pěší NavMesh v okolí auta; další pokus za 10 s.");
                break;
            }

            var officer = Object.Instantiate(prefab, spawnPosition, car.transform.rotation);
            if (officer == null)
                continue;

            var spawnedAgent = officer.GetComponentInChildren<NavMeshAgent>(true);
            if (spawnedAgent != null && spawnedAgent.isActiveAndEnabled && !spawnedAgent.isOnNavMesh &&
                !TryRecoverPedestrianAgent(spawnedAgent, officer))
            {
                // Never register a broken agent with the wave spawner or place
                // it into a police car: its first attack path would be invalid.
                log.LogWarning($"Policejní posily: prefab {officer.name} se nepřipojil k NavMeshi " +
                    $"u hlídky #{id}; posádka zkusí vytvoření později.");
                Object.Destroy(officer.gameObject);
                PoliceCrewRetryAt[id] = Time.unscaledTime + 10f;
                break;
            }
            PendingSeats.Add((patrol, car, seatType, officer, wave, crew, Time.frameCount));
            crew.Officers.Add(officer);
        }

        if (!alreadyTracked && crew.Officers.Count > 0)
            PoliceTransports.Add(id, crew);

        if (crew.Officers.Count > 0)
        {
            AssembledPoliceCars.Add(id);
            log.LogInfo($"Policejní posily: auto #{id}, plánovaná posádka {1 + crew.Officers.Count}/{count} včetně řidiče.");
        }
    }

    private static readonly List<(PoliceCar Patrol, CarInteract Car, CarSeatType Seat, CharacterControllerBase Officer,
        WaveSpawner Wave, PoliceTransport Crew, int Frame)> PendingSeats = new();
    private static readonly List<(PoliceTransport Crew, CharacterControllerBase Officer, float Deadline, bool Pursuing)> PendingExits = new();

    internal static void UpdateCrewTransitions()
    {
        for (int i = PendingSeats.Count - 1; i >= 0; i--)
        {
            var entry = PendingSeats[i];
            if (Time.frameCount <= entry.Frame + 1)
                continue;
            PendingSeats.RemoveAt(i);
            var officer = entry.Officer;
            if (officer == null)
                continue;
            try
            {
                if (entry.Patrol != null && entry.Patrol.IsActive && entry.Car != null && entry.Wave != null &&
                    PoliceTransports.TryGetValue(entry.Patrol.GetInstanceID(), out var current) &&
                    current == entry.Crew && entry.Car.GetCarSeat(entry.Seat)?.Character == null)
                {
                    entry.Car.EnterVehicleInstant(officer, entry.Seat);
                    if (entry.Car.GetCarSeat(entry.Seat)?.Character == officer)
                    {
                        entry.Crew.CreatedOfficers.Add(officer);
                        entry.Wave.RegisterCharacter(officer);
                        if (policeLoadouts)
                            ApplyPoliceLoadout(officer, true);
                        log.LogInfo($"Policejní posily: obsazené sedadlo {entry.Seat}.");
                        continue;
                    }
                }
            }
            catch (Exception error)
            {
                log.LogWarning($"Policejní posily: usazení {entry.Seat}: {error.Message}");
            }
            entry.Crew.Officers.Remove(officer);
            if (entry.Patrol != null)
            {
                int patrolId = entry.Patrol.GetInstanceID();
                AssembledPoliceCars.Remove(patrolId);
                PoliceCrewRetryAt[patrolId] = Time.unscaledTime + 5f;
            }
            if (officer.IsInCar && entry.Car != null)
                entry.Car.ExitCarInstant(officer);
            Object.Destroy(officer.gameObject);
        }

        for (int i = PendingExits.Count - 1; i >= 0; i--)
        {
            var entry = PendingExits[i];
            bool seated = entry.Crew.Car != null && entry.Officer != null &&
                (entry.Crew.Car.Driver == entry.Officer || entry.Crew.Car.Passengers.Contains(entry.Officer));
            if (entry.Officer != null && !entry.Officer.IsDead && (entry.Officer.IsInCar || seated ||
                entry.Officer.StateMachine?.ActiveStateType == CharacterStateType.ExitCar) &&
                Time.unscaledTime < entry.Deadline)
                continue;
            PendingExits.RemoveAt(i);
            if (entry.Officer == null || entry.Officer.IsDead)
                continue;
            bool finished = !entry.Officer.IsInCar && !seated &&
                entry.Officer.StateMachine?.ActiveStateType != CharacterStateType.ExitCar;
            log.LogInfo($"Policejní výstup #{entry.Officer.GetInstanceID()}: dokončen={finished}, sedí={seated}, stav={entry.Officer.StateMachine?.ActiveStateType}.");
            if (finished)
                ActivateDismountedOfficer(entry.Officer, entry.Pursuing && WantedSystem.WantedLevel == WantedLevel.Active);
        }
    }

    private static bool BeginExit(PoliceTransport crew, CharacterControllerBase officer, bool pursuing)
    {
        if (officer == null || officer.IsDead || !officer.IsInCar ||
            PendingExits.Exists(e => e.Officer == officer))
            return false;

        var brain = officer.AIBrain;
        if (brain == null || !officer.CanExitCar)
            return false;
        brain.Enable();
        var states = officer.StateMachine;
        if (states == null)
            return false;
        if (states.ActiveStateType != CharacterStateType.ExitCar && !states.TrySetState(CharacterStateType.ExitCar))
        {
            brain.ExitCar();
            log.LogDebug($"Policejní výstup #{officer.GetInstanceID()}: AI převzala žádost ze stavu {states.ActiveStateType}.");
        }
        PendingExits.Add((crew, officer, Time.unscaledTime + 8f, pursuing));
        return true;
    }

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

        // Never remove or swap an in-use weapon while AttackBehaviour is active:
        // vanilla AimStop/CancelSecondaryAction may still reference its action state.
        var attack = brain.ActiveBehaviour?.TryCast<AttackBehaviour>();
        if (attack != null && attack.isActive)
            return;
        inventory.EquipInstant(pistol);
        log.LogInfo($"Policejní výzbroj: pěší posila {officer.name} má vybavenou pistoli.");
    }

    private static void CheckPoliceArrival(PoliceCar __instance)
    {
        int id = __instance.GetInstanceID();
        if (DismountedPatrols.Contains(id))
            return;

        var game = GameManager.instance;
        var player = game != null ? game.PlayerController : null;
        var car = __instance.carController;
        var interact = car != null ? car.CarInteract : null;
        PoliceTransports.TryGetValue(id, out var crew);
        if (player == null || car == null || interact == null)
        {
            if (crew != null)
                ReleaseDropOffBrake(crew);
            return;
        }

        if (crew != null && crew.ExitInProgress)
        {
            if (PendingExits.Exists(e => e.Crew == crew))
                return;
            crew.ExitInProgress = false;
            bool passengerSeated = crew.Officers.Exists(o => o != null && !o.IsDead && interact.Passengers.Contains(o));
            if (!passengerSeated && (interact.Driver == null || interact.Driver.IsDead))
            {
                DismountedPatrols.Add(id);
                PoliceTransports.Remove(id);
                ReleaseDropOffBrake(crew);
                return;
            }
            crew.RetryExitAt = Time.time + 1f;
        }
        if (crew != null && Time.time < crew.RetryExitAt)
            return;

        bool pursuing = __instance.IsActive && WantedSystem.WantedLevel == WantedLevel.Active;
        // Never unload police outside an active on-foot pursuit.
        if (!pursuing || player.IsDead || player.IsInCar)
        {
            if (crew != null)
                ReleaseDropOffBrake(crew);
            return;
        }

        var driver = interact.Driver;
        bool exitDriver = driver != null && !driver.IsDead && driver.IsInCar &&
            driver.Faction == Faction.Police;
        bool exitPassengers = crew != null && crew.Officers.Exists(o => o != null && !o.IsDead &&
            o.IsInCar && interact.Passengers != null && interact.Passengers.Contains(o));
        if (!exitDriver && !exitPassengers)
        {
            if (crew != null)
            {
                DismountedPatrols.Add(id);
                PoliceTransports.Remove(id);
                ReleaseDropOffBrake(crew);
            }
            return;
        }

        float distance = Vector3.Distance(car.transform.position, player.transform.position);
        bool blockedNearby =
            PoliceProgress.TryGetValue(car.GetInstanceID(), out var progress) && progress.RepathRequested;
        float range = blockedNearby ? Mathf.Max(80f, policeDropOffDistance) : policeDropOffDistance;
        if (crew != null && crew.BrakingForDropOff)
            range += 8f;

        if (distance > range)
        {
            if (crew != null)
                ReleaseDropOffBrake(crew);
            return;
        }

        if (crew == null)
        {
            crew = new PoliceTransport(car, interact);
            PoliceTransports.Add(id, crew);
        }

        if (!crew.BrakingForDropOff && car.DriverAI != null)
        {
            car.ToggleDriverAIIsBraking(true);
            crew.BrakingForDropOff = true;
        }

        if (Mathf.Abs(car.SpeedPerSecond) > 1.25f)
            return;

        if (PendingSeats.Exists(e => e.Crew == crew))
            return;
        DisembarkPoliceCrew(crew, exitDriver ? driver : null, distance, pursuing);
        crew.ExitInProgress = PendingExits.Exists(e => e.Crew == crew);
        crew.RetryExitAt = Time.time + 1f;
        if (!crew.ExitInProgress)
            ReleaseDropOffBrake(crew);
    }

    private static void CancelPoliceDropOff(PoliceCar __instance)
    {
        int id = __instance.GetInstanceID();
        DismountedPatrols.Remove(id);
        if (__instance.carController != null)
            PoliceProgress.Remove(__instance.carController.GetInstanceID());
        PoliceCrewRetryAt.Remove(id);
        AssembledPoliceCars.Remove(id);
        if (PoliceTransports.TryGetValue(id, out var active))
        {
            PendingExits.RemoveAll(e => e.Crew == active);
            for (int i = PendingSeats.Count - 1; i >= 0; i--)
            {
                if (PendingSeats[i].Crew != active)
                    continue;
                var pending = PendingSeats[i].Officer;
                if (pending != null && !pending.IsInCar)
                    Object.Destroy(pending.gameObject);
                PendingSeats.RemoveAt(i);
            }
        }
        if (PoliceTransports.Remove(id, out var crew))
            ReleaseDropOffBrake(crew);
    }

    private static void EndPolicePursuit(PoliceCar __instance)
    {
        if (PoliceTransports.TryGetValue(__instance.GetInstanceID(), out var crew))
            ReleaseDropOffBrake(crew);
        if (__instance.carController != null)
            PoliceProgress.Remove(__instance.carController.GetInstanceID());
    }

    private static void ReleaseDropOffBrake(PoliceTransport crew)
    {
        if (!crew.BrakingForDropOff)
            return;

        crew.BrakingForDropOff = false;
        if (crew.Vehicle != null && crew.Vehicle.DriverAI != null)
            crew.Vehicle.ToggleDriverAIIsBraking(false);
    }

    private static bool DisembarkPoliceCrew(PoliceTransport crew, CharacterControllerBase driver,
        float distance, bool pursuing)
    {
        int exited = 0;
        foreach (var officer in crew.Officers)
        {
            if (officer == null || officer.IsDead || !crew.Car.Passengers.Contains(officer))
                continue;

            if (BeginExit(crew, officer, pursuing))
                exited++;
        }
        bool driverRequested = driver != null && crew.Car.Driver == driver && BeginExit(crew, driver, pursuing);
        log.LogInfo($"Policejní posily: žádost o výstup, řidič={driverRequested}, cestující={exited}, vzdálenost={distance:0} m.");
        return driverRequested;
    }

    private static void ActivateDismountedOfficer(CharacterControllerBase officer, bool pursuing)
    {
        if (!pursuing || officer == null || officer.IsDead)
            return;

        var brain = officer.AIBrain;
        if (brain == null)
            return;
        // Use the game's own wanted-state transition when available. Forcing an
        // AttackBehaviour over an already active Policeman breaks AI cleanup.
        var policeman = officer.GetComponentInChildren<Policeman>();
        if (policeman != null)
        {
            policeman.OnWantedLevelActivated();
            return;
        }
        // Fallback for police prefabs without a Policeman component.
        var target = GameManager.instance?.PlayerController;
        if (target != null && !target.IsDead)
            brain.SetBehaviour(Il2CppInterop.Runtime.Il2CppType.Of<AttackBehaviour>(), target, true);
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
        CombatPlugin.UpdateCrewTransitions();
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
            offset = offset.normalized * (Vector3.Distance(target.position, player.transform.position) / 130f * 0.47f);
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

        if (npc.Faction == Faction.Police && WantedSystem.WantedLevel == WantedLevel.Active)
            return !npc.IsInCar;

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
