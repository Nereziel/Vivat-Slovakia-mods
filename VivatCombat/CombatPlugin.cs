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

[BepInPlugin("local.vivat.combat", "Vivat Combat", "0.1.35")]
public sealed class CombatPlugin : BasePlugin
{
    private Harmony markers;
    private Harmony diagnostics;
    private Harmony damageDiagnostics;
    private Harmony cameraRecoil;
    private Harmony npcAudio;
    private Harmony npcHitReactions;
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
    [ThreadStatic]
    private static CharacterControllerBase weaponHitNpc;
    [ThreadStatic]
    private static Weapon activeShotWeapon;
    [ThreadStatic]
    private static Weapon simulatedDamageWeapon;

    public override void Load()
    {
        log = Log;
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
        npcAudio?.UnpatchSelf();
        npcHitReactions?.UnpatchSelf();
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
        new(AccessTools.DeclaredMethod(typeof(CombatPlugin), method)
            ?? throw new MissingMethodException(nameof(CombatPlugin), method));

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
