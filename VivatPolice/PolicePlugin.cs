using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;
using UnityEngine.AI;
using Vivat.Characters;
using Vivat.Systems.WantedSystem;
using Object = UnityEngine.Object;

namespace VivatPopulationMod;

[BepInPlugin("local.vivat.police", "Vivat Police", "0.1.5")]
public sealed class PolicePlugin : BasePlugin
{
    private Harmony policeTransport;
    private Harmony policeBalance;
    private Harmony policeNavigation;
    private Harmony policeAim;
    private PoliceDispatch policeDispatch;
    private Harmony policeVoices;
    private PoliceVoicePlayback policePlayback;
    private static ManualLogSource log;
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
    private static Spawner pendingEmptySpawner;
    private static int pendingEmptyCar;
    private static int pendingEmptyCount;
    private static float footFallbackUntil;
    private static float nextStreetApproach;
    private static readonly List<PoliceCar> ApproachCandidates = new();
    private static readonly Dictionary<int, float> ArrivedUntil = new();
    private static CharacterControllerBase registeringPassenger;
    private static readonly HashSet<int> ReplacedEmptyCars = new();
    private static readonly Dictionary<int, float> EmptyCarRetryAt = new();
    private static readonly HashSet<int> EmptyCarsThisScan = new();
    private static WaveSpawner modifiedWave;
    private static int originalWaveLimit;
    private static float originalWaveInterval;
    private static bool originalUseCurve;
    private static AnimationCurve originalWaveCurve;
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
        internal bool DriverExitDue;
        internal float RetryExitAt;

        internal PoliceTransport(CarController vehicle, CarInteract car)
        {
            Vehicle = vehicle;
            Car = car;
        }
    }
    public override void Load()
    {
        log = Log;
        Log.LogInfo("Vivat Police 0.1.5: čtyři výjezdové hlídky od začátku pátrání.");
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
            "Dosah příjezdu v metrech. U hráče pěšky hlídka zpomalí podle rychlosti a zastaví blíž k němu.",
            new AcceptableValueRange<float>(30f, 70f))).Value;
        bool balanceWaves = Config.Bind("Police", "BalanceWaves", true,
            "S dopravou zapnutou mají přednost posily autem. Jinak počet pěších posil roste pozvolna.").Value;
        policeBalanceEnabled = customPoliceEnabled && balanceWaves;
        if (customPoliceEnabled)
        {
            policeAim = Install("police-aim", harmony =>
            {
                foreach (var name in new[] { nameof(Weapon.SecondaryAction), nameof(Weapon.CancelSecondaryAction) })
                {
                    var method = AccessTools.DeclaredMethod(typeof(Weapon), name, Type.EmptyTypes)
                        ?? throw new MissingMethodException(nameof(Weapon), name);
                    harmony.Patch(method, prefix: Callback(nameof(PreparePoliceAim)));
                    Log.LogInfo($"Harmony: Weapon.{name}");
                }
            });
        }
        if (policeBalanceEnabled)
        {
            policeBalance = Install("police-balance", harmony =>
            {
                var activate = AccessTools.DeclaredMethod(typeof(WaveSpawner), "Activate", Type.EmptyTypes)
                    ?? throw new MissingMethodException(nameof(WaveSpawner), "Activate");
                harmony.Patch(activate, prefix: Callback(nameof(BalancePoliceWave)));
                Patch(harmony, typeof(WaveSpawner), "Deactivate", Type.EmptyTypes, nameof(OnWaveDeactivated));
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
                var togglePolice = AccessTools.DeclaredMethod(typeof(DriverAI), nameof(DriverAI.TogglePolice),
                    new[] { typeof(bool), typeof(float), typeof(bool) })
                    ?? throw new MissingMethodException(nameof(DriverAI), nameof(DriverAI.TogglePolice));
                harmony.Patch(togglePolice, prefix: Callback(nameof(HasDriverAI)));
                Log.LogInfo("Harmony: DriverAI.TogglePolice");
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
                Log.LogInfo("Harmony: NavMeshAgent.CalculatePath");
            });
            if (policeTransport != null)
                policeDispatch = AddComponent<PoliceDispatch>();
            else
                policeTransportEnabled = false;
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
    }

    public override bool Unload()
    {
        policeTransport?.UnpatchSelf();
        policeBalance?.UnpatchSelf();
        policeNavigation?.UnpatchSelf();
        policeAim?.UnpatchSelf();
        policeVoices?.UnpatchSelf();
        if (policeDispatch != null)
            Object.Destroy(policeDispatch);
        if (policePlayback != null)
            Object.Destroy(policePlayback);
        foreach (var entry in PendingSeats)
            if (entry.Officer != null && !entry.Officer.IsInCar)
                Object.Destroy(entry.Officer.gameObject);
        PendingSeats.Clear();
        PendingExits.Clear();
        foreach (var crew in new List<PoliceTransport>(PoliceTransports.Values))
            ReleaseTransport(crew);
        PoliceTransports.Clear();
        PoliceCrewRetryAt.Clear();
        AssembledPoliceCars.Clear();
        RestorePoliceCarCapacity();
        RestoreWave();
        return true;
    }

    private Harmony Install(string feature, Action<Harmony> install)
    {
        var harmony = new Harmony($"local.vivat.police.{feature}");
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

    private static HarmonyMethod Callback(string method)
    {
        var type = method == nameof(PoliceVoicePlayback.Register) ||
            method == nameof(PoliceVoicePlayback.Unregister)
            ? typeof(PoliceVoicePlayback) : typeof(PolicePlugin);
        var callback = AccessTools.DeclaredMethod(type, method)
            ?? throw new MissingMethodException(type.FullName, method);
        return new HarmonyMethod(callback);
    }

    private static void BalancePoliceWave(WaveSpawner __instance)
    {
        var wanted = WantedSystem.instance;
        if (wanted == null || wanted.waveSpawner != __instance)
            return;

        RememberWave(__instance);
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

        RememberWave(wave);
        wave.useSpawnCurve = false;
        wave.spawnInterval = 5f;
        if (wave.maxSpawned == limit)
            return;

        wave.maxSpawned = limit;
        log.LogInfo($"Policejní posily: limit pěší zálohy {limit}.");
    }

    private static void RememberWave(WaveSpawner wave)
    {
        if (modifiedWave == wave)
            return;
        RestoreWave();
        modifiedWave = wave;
        originalWaveLimit = wave.maxSpawned;
        originalWaveInterval = wave.spawnInterval;
        originalUseCurve = wave.useSpawnCurve;
        originalWaveCurve = wave.spawnCurve;
    }

    private static void OnWaveDeactivated(WaveSpawner __instance)
    {
        if (modifiedWave == __instance)
            RestoreWave();
    }

    private static void RestoreWave()
    {
        if (modifiedWave != null)
        {
            modifiedWave.maxSpawned = originalWaveLimit;
            modifiedWave.spawnInterval = originalWaveInterval;
            modifiedWave.useSpawnCurve = originalUseCurve;
            modifiedWave.spawnCurve = originalWaveCurve;
        }
        modifiedWave = null;
        originalWaveCurve = null;
    }

    private static void PreparePoliceReinforcement(WaveSpawner __instance, CharacterControllerBase __0)
    {
        var wanted = WantedSystem.instance;
        if (wanted == null || WantedSystem.WantedLevel != WantedLevel.Active || wanted.waveSpawner != __instance ||
            __0 == null || __0.IsDead || __0.Faction != Faction.Police)
            return;

        if (policeLoadouts && !__0.IsInCar && __0 != registeringPassenger)
            ApplyPoliceLoadout(__0, false);
    }

    private static void PreparePoliceAim(Weapon __instance)
    {
        if (__instance.aimTokenNameStart != null && __instance.aimTokenNameCancel != null)
            return;
        var owner = __instance.owner;
        if (owner == null || owner.IsPlayer || owner.Faction != Faction.Police)
            return;
        int id = __instance.GetInstanceID();
        __instance.aimTokenNameStart ??= $"Aim_Start_{id}";
        __instance.aimTokenNameCancel ??= $"Aim_Cancel_{id}";
        log.LogInfo($"Policejní AI: obnoveny klíče míření pro {__instance.name} #{id}.");
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
            pendingEmptySpawner = null;
            footFallbackUntil = 0f;
            nextStreetApproach = 0f;
            ArrivedUntil.Clear();
            ApproachCandidates.Clear();
            EmptyCarRetryAt.Clear();
            nextLowStrengthDispatch = 0f;
            nextPoliceDispatchDebug = 0f;
            if (PoliceTransports.Count > 0)
                foreach (var crew in new List<PoliceTransport>(PoliceTransports.Values))
                    ReleaseTransport(crew);
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
            RestoreWave();
            return;
        }

        if (policeWantedSince == 0f)
            policeWantedSince = Time.time;
        var game = GameManager.instance;
        var player = game != null ? game.PlayerController : null;
        var world = WorldManager.instance;
        if (player == null || world == null)
            return;

        var playerPosition = player.transform.position;
        float wantedDuration = Time.time - policeWantedSince;
        const int maximumPatrols = 6;
        int targetPatrols = Math.Min(maximumPatrols, 4 + (int)(wantedDuration / 180f));

        var spawner = WorldManager.CarPoliceSpawner;
        if (spawner != null && !debugInitialDispatchSent)
        {
            debugInitialDispatchSent = true;
            int initialCars = Math.Max(targetPatrols, debugInitialPoliceCars);
            ReservePoliceCarCapacity(spawner, initialCars);
            int before = spawner.Spawned;
            int requested = Math.Max(0, initialCars - before);
            for (int i = 0; i < requested && spawner.Spawned < initialCars; i++)
                spawner.Spawn();
            log.LogInfo($"Police debug: start wanted, cíl {initialCars}, požadavky {requested}, " +
                        $"spawnuto {before}->{spawner.Spawned}, volné body {spawner.availableSpawnpoints}.");
        }

        bool reportDebug = debugInitialPoliceCars > 0 && Time.time >= nextPoliceDispatchDebug;
        if (reportDebug)
            nextPoliceDispatchDebug = Time.time + 5f;

        int nearbyPatrols = 0;
        float closestPatrol = float.PositiveInfinity;
        ApproachCandidates.Clear();
        var camera = Camera.main;
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
        // The wanted registry can be empty while enabled native patrols are pursuing the player.
        foreach (var patrol in Object.FindObjectsByType<PoliceCar>(FindObjectsSortMode.None))
        {
            if (patrol == null || !patrol.isActiveAndEnabled)
                continue;
            var vehicle = patrol.carController;
            if (vehicle == null || vehicle.IsPlayer)
                continue;
            bool nativePoliceVehicle = spawner != null && vehicle.Spawner == spawner;
            float distance = (vehicle.transform.position - playerPosition).sqrMagnitude;
            if (reportDebug)
                ReportPoliceVehicle(vehicle, patrol, Mathf.Sqrt(distance));
            if (!nativePoliceVehicle)
                continue;

            if (ReplaceStalledPatrol(vehicle, patrol, spawner, player))
                continue;

            var driver = vehicle.CarInteract?.Driver;
            if (patrol.IsActive && driver != null && !driver.IsDead && driver.Faction == Faction.Police)
            {
                closestPatrol = Mathf.Min(closestPatrol, Mathf.Sqrt(distance));
                if (distance <= policeDropOffDistance * policeDropOffDistance)
                    ArrivedUntil[vehicle.GetInstanceID()] = Time.time + 20f;
                else if (camera != null &&
                    vehicle.DriverAI != null && vehicle.Rigidbody != null && !IsPatrolVisible(camera, vehicle.transform.position) &&
                    vehicle.CarInteract.Passengers?.Contains(player) != true)
                    ApproachCandidates.Add(patrol);
            }
            if (patrol != null && distance <= 230f * 230f)
                RecoverStoppedPoliceCar(vehicle, patrol, spawner, playerPosition);

            if (distance > 130f * 130f)
                continue;

            var car = vehicle.CarInteract;
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
                Time.time - progress.StationarySince >= 9f &&
                Mathf.Abs(vehicle.SpeedPerSecond) < 1f;
            bool stoppingForCrew = patrol != null &&
                PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var transport) &&
                (transport.BrakingForDropOff || transport.ExitInProgress);
            if (patrol.IsActive && distance <= policeDropOffDistance * policeDropOffDistance && (!stalled || stoppingForCrew))
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
            string closest = float.IsPositiveInfinity(closestPatrol) ? "žádná" : $"{closestPatrol:0} m";
            log.LogInfo($"Police debug: {wantedDuration:0} s wanted, v dosahu {nearbyPatrols}/{targetPatrols}, nejbližší hlídka {closest}, " +
                        $"policejní spawner {population}.");
        }

        if (!player.IsInCar && !player.IsDead && Time.time >= nextStreetApproach &&
            ApproachCandidates.Count > 0 && spawner != null && spawner.worldPointsData != null)
        {
            int missing = targetPatrols;
            foreach (float until in ArrivedUntil.Values)
                if (Time.time < until)
                    missing--;
            nextStreetApproach = Time.time + 3f;
            foreach (var candidate in ApproachCandidates)
            {
                if (missing <= 0)
                    break;
                int id = candidate.carController.GetInstanceID();
                if (ArrivedUntil.TryGetValue(id, out float until) && Time.time < until)
                    continue;
                if (!TryStreetApproach(candidate, spawner.worldPointsData, playerPosition, camera))
                    break;
                ArrivedUntil[id] = Time.time + 20f;
                missing--;
                AssemblePoliceCrew(wanted.waveSpawner, candidate);
            }
        }

        if (policeBalanceEnabled)
        {
            // Keep the wave cap in place while cars are unavailable. Lowering it
            // after the first foot officers arrive can interfere with their lifecycle.
            if (nearbyPatrols == 0 && wantedDuration >= 15f)
                footFallbackUntil = Time.time + 6f;
            bool needsFootFallback = Time.time < footFallbackUntil;
            SetPoliceFootFallback(wanted.waveSpawner, needsFootFallback ? policeFootFallbackLimit : 0);
        }

        if (spawner != null)
        {
            ReservePoliceCarCapacity(spawner, targetPatrols);
            bool replacingStalled = replacementSpawner != null;
            RequestReplacementIfSlotIsFree(spawner, nearbyPatrols, targetPatrols);
            if (replacingStalled)
                nextLowStrengthDispatch = Time.time + 8f;
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
        // The pool can fill on a later frame. Observe the count before requesting again.
        if (pendingEmptySpawner != null)
        {
            if (pendingEmptySpawner == spawner && spawner.Spawned > pendingEmptyCount)
            {
                if (EmptyCarsThisScan.Contains(pendingEmptyCar))
                    ReplacedEmptyCars.Add(pendingEmptyCar);
                log.LogInfo($"Policejní posily: po žádosti za #{pendingEmptyCar} vzrostl počet aut {pendingEmptyCount}->{spawner.Spawned}.");
                pendingEmptySpawner = null;
            }
            else if (pendingEmptySpawner != spawner || Time.time >= nextLowStrengthDispatch)
            {
                log.LogDebug($"Policejní posily: po žádosti za #{pendingEmptyCar} bez nárůstu počtu aut.");
                pendingEmptySpawner = null;
            }
        }
        if (nearbyPatrols >= targetPatrols || Time.time < nextLowStrengthDispatch)
            return;

        if (spawner.Spawned < targetPatrols && spawner.Spawned < spawner.GetMaxSpawned())
        {
            nextLowStrengthDispatch = Time.time + 8f;
            spawner.Spawn();
            log.LogDebug("Policejní posily: vyžádána chybějící hlídka.");
            return;
        }

        foreach (int carId in EmptyCarsThisScan)
        {
            if (ReplacedEmptyCars.Contains(carId) ||
                (EmptyCarRetryAt.TryGetValue(carId, out var nextAttempt) && Time.time < nextAttempt))
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
            nextLowStrengthDispatch = Time.time + 8f;
            EmptyCarRetryAt[carId] = Time.time + 25f;
            int baseLimit = reservedPoliceSpawner == spawner ? originalPoliceCarCapacity : spawner.GetMaxSpawned();
            if (before >= Math.Max(baseLimit, targetPatrols) + targetPatrols)
                return;
            ReservePoliceCarCapacity(spawner, Math.Max(targetPatrols, before + 1));
            pendingEmptySpawner = spawner;
            pendingEmptyCar = carId;
            pendingEmptyCount = before;
            spawner.Spawn();
            log.LogDebug($"Policejní posily: žádost za prázdné auto #{carId}, živí policisté {liveOfficers}/{2 * targetPatrols}.");
            return;
        }
    }

    private static bool ReplaceStalledPatrol(CarController vehicle, PoliceCar patrol, Spawner spawner,
        CharacterControllerBase player)
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

        float now = Time.time;
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
            ReleaseTransport(crew);
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

        float elapsed = Time.time - replacementTime;
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

    // Deactivation may arrive after the native car has already destroyed its driver component.
    private static bool HasDriverAI(DriverAI __instance) => __instance != null;

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
            Mathf.Abs(vehicle.SpeedPerSecond) >= 1f ||
            (PoliceTransports.TryGetValue(patrol.GetInstanceID(), out var crew) && crew.BrakingForDropOff))
        {
            PoliceProgress.Remove(id);
            return;
        }

        var position = vehicle.transform.position;
        float now = Time.time;
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
        if (distanceSquared <= policeDropOffDistance * policeDropOffDistance)
        {
            progress.RepathRequested = stationaryFor >= 5f;
            return;
        }
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

    private static bool TryStreetApproach(PoliceCar patrol, WorldPointsData roads, Vector3 player, Camera camera)
    {
        var vehicle = patrol.carController;
        // Sample the actual lane segments. Their endpoints may both be well away from the player.
        var visited = new HashSet<IntPtr>();
        int nearLane = 0, outOfView = 0, grounded = 0;
        float best = float.PositiveInfinity;
        Vector3 bestPosition = default;
        Quaternion bestRotation = default;
        WorldPoint bestNext = null;
        for (int i = 0; i < 17; i++)
        {
            float angle = i * Mathf.PI * 0.25f;
            float radius = i == 0 ? 0f : i <= 8 ? 25f : 55f;
            var probe = player + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            var node = roads.FindNearestWorldPoint(probe);
            if (node == null)
                continue;
            InspectLane(node.PreviousWorldPoint);
            InspectLane(node);
        }
        if (bestNext == null)
        {
            if (debugInitialPoliceCars > 0)
                log.LogInfo($"Policejní příjezd: úseků {visited.Count}, kandidátů u hráče {nearLane}, " +
                            $"mimo dohled {outOfView}, s povrchem {grounded}, volných 0.");
            return false;
        }

        var body = vehicle.Rigidbody;
        if (!body.isKinematic)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
        float before = Vector3.Distance(vehicle.transform.position, player);
        body.position = bestPosition;
        body.rotation = bestRotation;
        Physics.SyncTransforms();
        vehicle.DriverAI.SetWorldPoint(bestNext);
        PoliceProgress.Remove(vehicle.GetInstanceID());
        PatrolApproaches.Remove(vehicle.GetInstanceID());
        log.LogInfo($"Policejní příjezd: auto #{vehicle.GetInstanceID()} přesunuto na příjezdovou ulici, " +
                    $"{before:0} -> {Vector3.Distance(bestPosition, player):0} m od hráče.");
        return true;

        void InspectLane(WorldPoint start)
        {
            if (start == null || !visited.Add(start.Pointer))
                return;
            var next = start.NextWorldPoint;
            if (next == null)
                return;
            Vector3 segment = next.Position - start.Position;
            float length = segment.magnitude;
            if (length < 4f)
                return;
            Vector3 forward = segment / length;
            var rotation = Quaternion.LookRotation(forward, Vector3.up);
            float along = Mathf.Clamp(Vector3.Dot(player - start.Position, forward), 0f, length);
            float miss = Vector3.Distance(start.Position + forward * along, player);
            if (miss > 36f)
                return;
            for (float lead = 32f; lead >= 0f; lead -= 8f)
            {
                Vector3 position = start.Position + forward * Mathf.Clamp(along - lead, 0f, length);
                float distance = Vector3.Distance(position, player);
                float score = miss + Mathf.Abs(distance - 32f);
                if (distance < 12f || distance > 45f || score >= best || Mathf.Abs(position.y - player.y) > 5f)
                    continue;
                nearLane++;
                if (IsPatrolVisible(camera, position))
                    continue;
                outOfView++;
                if (!Physics.Raycast(position + Vector3.up * 2f, Vector3.down, out RaycastHit ground, 4f,
                        vehicle.groundMask, QueryTriggerInteraction.Ignore) || ground.normal.y < 0.8f)
                    continue;
                grounded++;
                if (!IsFreePoliceRoadPoint(vehicle, ground.point, rotation))
                    continue;
                best = score;
                bestPosition = ground.point + Vector3.up * 0.35f;
                bestRotation = rotation;
                bestNext = next;
            }
        }
    }

    private static bool IsPatrolVisible(Camera camera, Vector3 position)
    {
        Vector3 roof = position + Vector3.up * 1.7f;
        if (!IsInsideCamera(camera, roof))
            return false;
        if (!Physics.Linecast(camera.transform.position, roof, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
            return true;
        return hit.collider == null || hit.collider.GetComponentInParent<CarController>() != null ||
               hit.collider.GetComponentInParent<CharacterControllerBase>() != null;
    }

    private static bool IsFreePoliceRoadPoint(CarController vehicle, WorldPoint point)
        => IsFreePoliceRoadPoint(vehicle, point.Position, point.Rotation);

    private static bool IsFreePoliceRoadPoint(CarController vehicle, Vector3 position, Quaternion rotation)
    {
        var hits = Physics.OverlapBox(position + Vector3.up * 1.05f,
            new Vector3(1.15f, 0.65f, 2.65f), rotation, ~0, QueryTriggerInteraction.Ignore);
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

    // Leave unrelated agents to the game. Only the patrol may use a static road query.
    private static bool CalculatePolicePath(NavMeshAgent __instance, Vector3 __0, NavMeshPath __1, ref bool __result)
    {
        if (__instance.isActiveAndEnabled && __instance.isOnNavMesh)
            return true;

        var officer = __instance.GetComponentInParent<CharacterControllerBase>();
        if (officer != null && officer.Faction == Faction.Police && !officer.IsPlayer && !officer.IsDead && !officer.IsInCar &&
            TryRecoverPedestrianAgent(__instance, officer))
            return true;

        var patrol = __instance.GetComponentInParent<PoliceCar>();
        if (patrol == null)
        {
            var car = __instance.GetComponentInParent<CarController>();
            patrol = car != null ? car.PoliceCar : null;
        }

        if ((officer == null || officer.IsPlayer || officer.Faction != Faction.Police) &&
            (patrol == null || patrol.agent != __instance))
            return true;

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

        float now = Time.time;
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
        if (recovered && Time.time >= nextPoliceFootNavigationReport)
        {
            nextPoliceFootNavigationReport = Time.time + 5f;
            log.LogInfo($"NavMesh: obnovené připojení NPC {officer.name}, oprava {delta.magnitude:0.0} m.");
        }
        return recovered;
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
        if (PoliceCrewRetryAt.TryGetValue(id, out var retryAt) && Time.time < retryAt)
            return;
        bool alreadyTracked = PoliceTransports.TryGetValue(id, out var crew);
        if (AssembledPoliceCars.Contains(id) ||
            (alreadyTracked && (crew.BrakingForDropOff || crew.ExitInProgress)))
            return;

        bool later = Time.time - policeWantedSince >= 60f;
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
                PoliceCrewRetryAt[id] = Time.time + 10f;
                log.LogWarning($"Policejní posily: posádka #{id} čeká na pěší NavMesh v okolí auta; další pokus za 10 s.");
                break;
            }

            var officer = Object.Instantiate(prefab, spawnPosition, car.transform.rotation);
            if (officer == null)
                continue;
            if (!officer.gameObject.activeSelf)
                officer.gameObject.SetActive(true);

            var spawnedAgent = officer.GetComponentInChildren<NavMeshAgent>(true);
            if (spawnedAgent != null && spawnedAgent.isActiveAndEnabled && !spawnedAgent.isOnNavMesh &&
                !TryRecoverPedestrianAgent(spawnedAgent, officer))
            {
                // Never register a broken agent with the wave spawner or place
                // it into a police car: its first attack path would be invalid.
                log.LogWarning($"Policejní posily: prefab {officer.name} se nepřipojil k NavMeshi " +
                    $"u hlídky #{id}; posádka zkusí vytvoření později.");
                Object.Destroy(officer.gameObject);
                PoliceCrewRetryAt[id] = Time.time + 10f;
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
    private sealed class PendingExit
    {
        internal PoliceTransport Crew;
        internal CharacterControllerBase Officer;
        internal float Deadline;
        internal bool TimedOut;
    }
    private static readonly List<PendingExit> PendingExits = new();

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
                if (WantedSystem.WantedLevel == WantedLevel.Active && !officer.IsDead &&
                    entry.Patrol != null && entry.Patrol.IsActive && entry.Car != null && entry.Wave != null &&
                    PoliceTransports.TryGetValue(entry.Patrol.GetInstanceID(), out var current) &&
                    current == entry.Crew && entry.Car.GetCarSeat(entry.Seat)?.Character == null)
                {
                    entry.Car.EnterVehicleInstant(officer, entry.Seat);
                    if (entry.Car.GetCarSeat(entry.Seat)?.Character == officer)
                    {
                        entry.Crew.CreatedOfficers.Add(officer);
                        if (policeLoadouts)
                            ApplyPoliceLoadout(officer, true);
                        log.LogInfo($"Policejní posily: auto #{entry.Crew.Vehicle.GetInstanceID()}, sedadlo {entry.Seat}, " +
                                    $"policista #{officer.GetInstanceID()}, aktivní={officer.gameObject.activeInHierarchy}, " +
                                    $"model={officer.ActiveModel?.activeInHierarchy}, stav={officer.StateMachine?.ActiveStateType}.");
                        continue;
                    }
                }
            }
            catch (Exception error)
            {
                log.LogWarning($"Policejní posily: usazení {entry.Seat}: {error.Message}");
            }
            entry.Crew.Officers.Remove(officer);
            entry.Crew.CreatedOfficers.Remove(officer);
            if (entry.Wave != null && entry.Wave.characters != null && entry.Wave.characters.Contains(officer))
                entry.Wave.UnregisterCharacter(officer);
            if (entry.Patrol != null)
            {
                int patrolId = entry.Patrol.GetInstanceID();
                AssembledPoliceCars.Remove(patrolId);
                PoliceCrewRetryAt[patrolId] = Time.time + 5f;
            }
            if (officer.IsInCar && entry.Car != null)
                entry.Car.ExitCarInstant(officer);
            Object.Destroy(officer.gameObject);
        }

        for (int i = PendingExits.Count - 1; i >= 0; i--)
        {
            var entry = PendingExits[i];
            bool seated = entry.Crew.Car != null && entry.Officer != null &&
                (entry.Crew.Car.Driver == entry.Officer || entry.Crew.Car.Passengers?.Contains(entry.Officer) == true);
            if (entry.Officer == null || entry.Officer.IsDead || (!seated && !entry.Officer.gameObject.activeInHierarchy))
            {
                PendingExits.RemoveAt(i);
                continue;
            }
            var state = entry.Officer.StateMachine?.ActiveStateType;
            bool finished = !entry.Officer.IsInCar && !seated && state.HasValue &&
                state != CharacterStateType.ExitCar && state != CharacterStateType.None;
            if (!finished)
            {
                if (Time.time < entry.Deadline)
                    continue;
                if (!entry.TimedOut)
                {
                    entry.TimedOut = true;
                    entry.Crew.DriverExitDue = true;
                    log.LogWarning($"Policejní výstup #{entry.Officer.GetInstanceID()} čeká: sedí={seated}, stav={state}.");
                    if (seated && entry.Crew.Car.Passengers?.Contains(entry.Officer) == true &&
                        entry.Crew.Vehicle != null && !entry.Crew.Vehicle.IsPlayer &&
                        Mathf.Abs(entry.Crew.Vehicle.SpeedPerSecond) <= 1.25f &&
                        WantedSystem.WantedLevel == WantedLevel.Active &&
                        GameManager.instance?.PlayerController?.IsInCar == false)
                    {
                        FinishPassengerExit(entry.Crew, entry.Officer);
                        entry.Deadline = Time.time + 5f;
                        continue;
                    }
                }
                // The native animation can finish after the seat is already free.
                if (state == CharacterStateType.ExitCar)
                    continue;
            }
            PendingExits.RemoveAt(i);
            log.LogInfo($"Policejní výstup #{entry.Officer.GetInstanceID()}: dokončen={finished}, sedí={seated}, stav={entry.Officer.StateMachine?.ActiveStateType}.");
            if (finished)
            {
                RegisterDismountedPassenger(entry.Crew, entry.Officer);
                ActivateDismountedOfficer(entry.Officer, WantedSystem.WantedLevel == WantedLevel.Active);
            }
        }
    }

    private static void RegisterDismountedPassenger(PoliceTransport crew, CharacterControllerBase officer)
    {
        if (!crew.CreatedOfficers.Contains(officer))
            return;
        var wave = WantedSystem.instance?.waveSpawner;
        if (wave == null || wave.characters == null)
            return;
        registeringPassenger = officer;
        try
        {
            if (!wave.characters.Contains(officer))
                wave.RegisterCharacter(officer);
            crew.CreatedOfficers.Remove(officer);
        }
        finally
        {
            registeringPassenger = null;
        }
    }

    private static bool BeginExit(PoliceTransport crew, CharacterControllerBase officer)
    {
        if (officer == null || officer.IsDead || !officer.IsInCar ||
            PendingExits.Exists(e => e.Officer == officer))
            return false;

        var brain = officer.AIBrain;
        if (brain == null || !officer.CanExitCar)
            return false;
        var states = officer.StateMachine;
        if (states == null)
            return false;
        if (!officer.gameObject.activeSelf && crew.Car.Passengers?.Contains(officer) == true)
        {
            ExitPassengerInstant(crew, officer);
        }
        else if (states.ActiveStateType != CharacterStateType.ExitCar)
        {
            brain.Enable();
            brain.ExitCar();
        }
        PendingExits.Add(new PendingExit { Crew = crew, Officer = officer, Deadline = Time.time + 8f });
        return true;
    }

    private static void ExitPassengerInstant(PoliceTransport crew, CharacterControllerBase officer)
    {
        if (!officer.gameObject.activeSelf)
            officer.gameObject.SetActive(true);
        crew.Car.ExitCarInstant(officer);
        log.LogInfo($"Policejní výstup #{officer.GetInstanceID()}: použit okamžitý výstup spolujezdce.");
    }

    private static void FinishPassengerExit(PoliceTransport crew, CharacterControllerBase officer)
    {
        var states = officer.StateMachine?.availableStates;
        if (states == null || !states.TryGetValue(CharacterStateType.ExitCar, out var state))
            return;
        var exit = state.TryCast<CharacterExitCarState>();
        if (exit == null)
            return;
        foreach (var type in PolicePassengerSeats)
        {
            var seat = crew.Car.GetCarSeat(type);
            if (seat == null || seat.Character != officer || seat.EnterSpot == null)
                continue;
            var agent = officer.Agent;
            var filter = new NavMeshQueryFilter { agentTypeID = agent != null ? agent.agentTypeID : 0,
                areaMask = agent != null ? agent.areaMask : NavMesh.AllAreas };
            if (!NavMesh.SamplePosition(seat.EnterSpot.position, out NavMeshHit hit, 2.5f, filter) ||
                Mathf.Abs(hit.position.y - seat.EnterSpot.position.y) > 1.5f)
                return;
            if (!officer.gameObject.activeSelf)
                officer.gameObject.SetActive(true);
            exit.carController = crew.Vehicle;
            exit.InstantCarExit(hit.position, seat, crew.Vehicle.transform);
            // The regular ExitCar sequence removes the passenger before selecting its instant branch.
            if (!officer.IsInCar && crew.Car.Passengers?.Contains(officer) == true)
                crew.Car.RemovePassenger(officer, type);
            if (!officer.IsInCar && seat.Character == officer)
                seat.character = null;
            log.LogInfo($"Policejní výstup #{officer.GetInstanceID()}: nouzový výstup u dveří {type}.");
            return;
        }
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
        var attack = brain.ActiveBehaviour?.TryCast<AttackBehaviour>();
        if (attack != null && attack.isActive)
            return;
        var pistol = inventory.SelectWeapon(WeaponType.Pistol);
        if (pistol == null)
        {
            log.LogWarning($"Policejní výzbroj: pistole není dostupná pro {officer.name}.");
            return;
        }

        // Never remove or swap an in-use weapon while AttackBehaviour is active:
        // vanilla AimStop/CancelSecondaryAction may still reference its action state.
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
        if (player == null || car == null || interact == null || car.IsPlayer ||
            car.Spawner == null || car.Spawner != WorldManager.CarPoliceSpawner)
        {
            if (crew != null)
                ReleaseDropOffBrake(crew);
            return;
        }

        if (crew != null && crew.ExitInProgress)
        {
            if (PendingExits.Exists(e => e.Crew == crew && !e.TimedOut))
                return;
            crew.ExitInProgress = false;
            bool passengerSeated = crew.Officers.Exists(o => o != null && !o.IsDead && interact.Passengers?.Contains(o) == true);
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
        float speed = Mathf.Abs(car.SpeedPerSecond);
        float range = blockedNearby ? policeDropOffDistance : Mathf.Min(policeDropOffDistance, 18f + speed * speed / 12f);
        if (crew != null && crew.BrakingForDropOff)
            range = policeDropOffDistance + 8f;

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
        crew.ExitInProgress = DisembarkPoliceCrew(crew, exitDriver ? driver : null, distance);
        crew.RetryExitAt = Time.time + 1f;
    }

    private static void CancelPoliceDropOff(PoliceCar __instance)
    {
        int id = __instance.GetInstanceID();
        DismountedPatrols.Remove(id);
        if (__instance.carController != null)
        {
            int vehicleId = __instance.carController.GetInstanceID();
            PoliceProgress.Remove(vehicleId);
            PatrolApproaches.Remove(vehicleId);
            ReplacedEmptyCars.Remove(vehicleId);
            EmptyCarRetryAt.Remove(vehicleId);
        }
        PoliceCrewRetryAt.Remove(id);
        AssembledPoliceCars.Remove(id);
        if (PoliceTransports.TryGetValue(id, out var active))
        {
            // Driver exit can disable PoliceCar synchronously. Keep observing detached officers.
            PendingExits.RemoveAll(e => e.Crew == active && (e.Officer == null || e.Officer.IsInCar &&
                e.Officer.StateMachine?.ActiveStateType != CharacterStateType.ExitCar));
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
            ReleaseTransport(crew, keepExiting: true);
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

    private static void ReleaseTransport(PoliceTransport crew, bool keepExiting = false)
    {
        ReleaseDropOffBrake(crew);
        for (int i = crew.CreatedOfficers.Count - 1; i >= 0; i--)
        {
            var officer = crew.CreatedOfficers[i];
            if (keepExiting && PendingExits.Exists(e => e.Crew == crew && e.Officer == officer))
                continue;
            crew.CreatedOfficers.RemoveAt(i);
            if (officer == null)
                continue;
            if (crew.Car != null)
                foreach (var type in PolicePassengerSeats)
                {
                    var seat = crew.Car.GetCarSeat(type);
                    if (seat == null || seat.Character != officer)
                        continue;
                    crew.Car.RemovePassenger(officer, type);
                    if (seat.Character == officer)
                        seat.character = null;
                    break;
                }
            Object.Destroy(officer.gameObject);
        }
    }

    private static bool DisembarkPoliceCrew(PoliceTransport crew, CharacterControllerBase driver,
        float distance)
    {
        if (crew.DriverExitDue && driver != null && crew.Car.Driver == driver && BeginExit(crew, driver))
        {
            log.LogInfo($"Policejní posily: výstup řidiče #{driver.GetInstanceID()} během obnovy výstupu posádky, {distance:0} m.");
            return true;
        }
        foreach (var officer in crew.Officers)
        {
            if (officer == null || officer.IsDead || crew.Car.Passengers?.Contains(officer) != true)
                continue;

            if (BeginExit(crew, officer))
            {
                log.LogInfo($"Policejní posily: výstup cestujícího #{officer.GetInstanceID()}, {distance:0} m.");
                return true;
            }
            // An unavailable seat must not hold the driver and the remaining passengers indefinitely.
            continue;
        }
        bool driverRequested = driver != null && crew.Car.Driver == driver && BeginExit(crew, driver);
        if (driverRequested)
            log.LogInfo($"Policejní posily: výstup řidiče #{driver.GetInstanceID()}, {distance:0} m.");
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
            if (!policeman.isEngaged && policeman.isActiveAndEnabled)
                policeman.OnWantedLevelActivated();
            return;
        }
        // Fallback for police prefabs without a Policeman component.
        var target = GameManager.instance?.PlayerController;
        if (target != null && !target.IsDead)
            brain.SetBehaviour(Il2CppInterop.Runtime.Il2CppType.Of<AttackBehaviour>(), target, true);
    }

}

public sealed class PoliceDispatch : MonoBehaviour
{
    private float nextCheck;

    public PoliceDispatch(IntPtr pointer) : base(pointer) { }

    private void Update()
    {
        PolicePlugin.UpdateCrewTransitions();
        if (Time.time < nextCheck)
            return;

        nextCheck = Time.time + 1f;
        PolicePlugin.DispatchPoliceCar();
    }
}

