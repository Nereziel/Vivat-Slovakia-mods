using System;
using System.Globalization;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using Vivat.Characters;
using Vivat.Systems.WantedSystem;

namespace VivatPopulationMod;

[BepInPlugin(PluginId, "Vivat Trainer", "0.1.10")]
public sealed class TrainerPlugin : BasePlugin
{
    private const string PluginId = "local.vivat.trainer";
    private TrainerMenu menu;
    private Harmony harmony;

    public override void Load()
    {
        menu = AddComponent<TrainerMenu>();
        menu.Initialize(Log);
        harmony = new Harmony(PluginId);
        PatchCameraInput(typeof(CharacterCameraLook), "Look");
        PatchCameraInput(typeof(CinemachineCarInput), "Update");
        Log.LogInfo("Trainer připraven. Menu se otevírá klávesou F8.");
    }

    private void PatchCameraInput(Type type, string name)
    {
        var target = AccessTools.DeclaredMethod(type, name, Type.EmptyTypes);
        if (target == null)
        {
            Log.LogWarning($"Metoda vstupu kamery {type.Name}.{name} chybí.");
            return;
        }

        try
        {
            harmony.Patch(target, prefix: new HarmonyMethod(
                AccessTools.DeclaredMethod(typeof(TrainerPlugin), nameof(AllowCameraLook))));
            Log.LogInfo($"Zámek kamery: {type.Name}.{name}");
        }
        catch (Exception exception)
        {
            Log.LogWarning($"Zámek kamery {type.Name}.{name} se nezapojil: {exception}");
        }
    }

    public override bool Unload()
    {
        if (menu != null)
        {
            menu.Close();
            UnityEngine.Object.Destroy(menu);
        }
        harmony?.UnpatchSelf();
        return true;
    }

    private static bool AllowCameraLook() => !TrainerMenu.IsOpen;
}

public sealed class TrainerMenu : MonoBehaviour
{
    private enum Field { None, Money, X, Y, Z }

    private ManualLogSource log;
    internal static bool IsOpen { get; private set; }
    private GameManager menuGame;
    private CursorLockMode oldLock;
    private bool oldCursorVisibility;
    private string amount = "1000";
    private string x = "";
    private string y = "";
    private string z = "";
    private string status = "";
    private Field activeField;
    private GameManager summonGame;
    private float summonDeadline;
    private static readonly float[] SummonDistances = { 7f, 11f };
    public TrainerMenu(IntPtr pointer) : base(pointer) { }

    public void Initialize(ManualLogSource source) => log = source;

    private void Update()
    {
        if (summonGame != null)
            FinishSummon();

        var keyboard = Keyboard.current;
        bool toggle = keyboard != null && keyboard.f8Key.wasPressedThisFrame;

        if (IsOpen)
        {
            if (toggle || menuGame != GameManager.instance || !IsGameplay(menuGame))
                Close();
            return;
        }

        if (!toggle)
            return;

        var game = GameManager.instance;
        if (!IsGameplay(game))
            return;

        oldLock = Cursor.lockState;
        oldCursorVisibility = Cursor.visible;
        menuGame = game;
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        IsOpen = true;
        log?.LogInfo("Trainer otevřen.");
    }

    private void OnDisable() => Close();

    internal void Close()
    {
        if (!IsOpen)
            return;

        IsOpen = false;
        activeField = Field.None;
        menuGame = null;
        Cursor.lockState = oldLock;
        Cursor.visible = oldCursorVisibility;
    }

    private void OnGUI()
    {
        if (!IsOpen)
            return;

        GUI.Box(new Rect(16, 16, 412, 402), "Vivat Trainer");
        if (GUI.Button(new Rect(335, 42, 80, 26), "Zavřít F8"))
        {
            Close();
            return;
        }

        GUI.Label(new Rect(28, 43, 60, 24), "Peníze");
        NumberField(new Rect(88, 43, 88, 25), Field.Money, ref amount, 8);
        if (GUI.Button(new Rect(184, 43, 112, 25), "Přidat peníze"))
            Run(AddMoney);

        WeaponButton(28, 81, "Pistole", WeaponType.Pistol);
        WeaponButton(160, 81, "Samopal", WeaponType.SubmachineGun);
        WeaponButton(292, 81, "Brokovnice", WeaponType.Shotgun);
        WeaponButton(28, 112, "Útočná puška", WeaponType.AssaultRifle);
        WeaponButton(160, 112, "Puška", WeaponType.Rifle);
        WeaponButton(292, 112, "Těžká zbraň", WeaponType.Heavy);

        if (GUI.Button(new Rect(28, 152, 123, 26), "Doplnit náboje"))
            Run((_, player) => { Inventory(player).RefillAmmo(); return "Munice doplněna."; });
        if (GUI.Button(new Rect(160, 152, 123, 26), "Opravit auto"))
            Run(Repair);
        if (GUI.Button(new Rect(292, 152, 123, 26), "Vyléčit"))
            Run(Heal);
        if (GUI.Button(new Rect(28, 185, 180, 26), "Přivolat vlastní auto"))
            Run(SummonCar);
        if (GUI.Button(new Rect(222, 185, 193, 26), "Zrušit pátrání"))
            Run(ClearWantedLevel);

        GUI.Label(new Rect(28, 221, 380, 24), "Teleport podle souřadnic Unity");
        CoordinateField(28, "X", Field.X, ref x);
        CoordinateField(160, "Y", Field.Y, ref y);
        CoordinateField(292, "Z", Field.Z, ref z);
        if (GUI.Button(new Rect(28, 283, 180, 27), "Načíst aktuální polohu"))
            Run(ReadPosition);
        if (GUI.Button(new Rect(222, 283, 193, 27), "Teleportovat"))
            Run(Teleport);
        if (GUI.Button(new Rect(28, 316, 387, 27), "Teleport na waypoint z mapy"))
            Run(TeleportToWaypoint);

        GUI.Label(new Rect(28, 349, 385, 58), status);
    }

    private void CoordinateField(float left, string axis, Field field, ref string value)
    {
        GUI.Label(new Rect(left, 251, 19, 24), axis);
        NumberField(new Rect(left + 19, 251, 104, 24), field, ref value, 16);
    }

    private void NumberField(Rect rectangle, Field field, ref string value, int maxLength)
    {
        bool selected = activeField == field;
        if (GUI.Button(rectangle, selected ? value + "|" : value.Length > 0 ? value : "Zadat"))
            activeField = field;

        var input = Event.current;
        if (activeField != field || input == null || input.type != EventType.KeyDown)
            return;

        if (input.keyCode == KeyCode.Return || input.keyCode == KeyCode.KeypadEnter ||
            input.keyCode == KeyCode.Escape)
        {
            activeField = Field.None;
        }
        else if (input.keyCode == KeyCode.Backspace)
        {
            if (value.Length > 0)
                value = value[..^1];
        }
        else if (input.keyCode == KeyCode.Delete)
        {
            value = "";
        }
        else
        {
            char character = input.character;
            bool digit = character >= '0' && character <= '9';
            bool coordinateSymbol = field != Field.Money && character is '-' or '.' or ',';
            if ((!digit && !coordinateSymbol) || value.Length >= maxLength)
                return;

            value += character;
        }

        input.Use();
    }

    private void WeaponButton(float left, float top, string label, WeaponType type)
    {
        if (GUI.Button(new Rect(left, top, 123, 25), label))
        {
            Run((_, player) =>
            {
                Inventory(player).AddWeapon(type, 30, true);
                return $"Přidáno: {label}.";
            });
        }
    }

    private void Run(Func<GameManager, CharacterControllerBase, string> action)
    {
        try
        {
            var game = menuGame;
            var player = game == GameManager.instance ? GetActivePlayer(game) : null;
            status = player == null
                ? "Nejdřív načti hru se živou postavou."
                : action(game, player);
        }
        catch (Exception exception)
        {
            status = "Akce selhala. Podrobnosti jsou v logu BepInEx.";
            log?.LogWarning(exception);
        }
    }

    private static CharacterControllerBase GetActivePlayer(GameManager game)
    {
        var player = game == null ? null : game.PlayerController;
        return player != null && player.gameObject.activeInHierarchy && player.IsPlayer && !player.IsDead
            ? player
            : null;
    }

    private static bool IsGameplay(GameManager game) =>
        GetActivePlayer(game) != null && game.currentInputState == GameManager.InputState.Gameplay;

    private static CharacterInventory Inventory(CharacterControllerBase player)
    {
        var inventory = player.GetComponentInChildren<CharacterInventory>(true);
        return inventory != null ? inventory : throw new InvalidOperationException("Inventář hráče chybí.");
    }

    private string AddMoney(GameManager _, CharacterControllerBase player)
    {
        if (!int.TryParse(amount, NumberStyles.None, CultureInfo.InvariantCulture, out int value) ||
            value < 1 || value > 1_000_000)
            return "Zadej částku od 1 do 1 000 000.";

        Inventory(player).AddMoney(value, true);
        return $"Přidáno {value} Kčs.";
    }

    private static string Repair(GameManager game, CharacterControllerBase _)
    {
        var car = game.CurrentCarController;
        if (car == null)
            car = game.PlayerCarController;
        if (car == null)
            return "Auto hráče není dostupné.";

        car.Repair();
        return "Auto opraveno.";
    }

    private static string ClearWantedLevel(GameManager _, CharacterControllerBase __)
    {
        if (WantedSystem.instance == null)
            return "Policejní pátrání není dostupné.";
        if (WantedSystem.WantedLevel == WantedLevel.Inactive)
            return "Policie po tobě nepátrá.";

        WantedSystem.RemoveWantedLevel();
        return WantedSystem.WantedLevel == WantedLevel.Inactive
            ? "Pátrání zrušeno."
            : "Hra zatím pátrání neukončila.";
    }

    private string SummonCar(GameManager game, CharacterControllerBase player)
    {
        if (game.CurrentCarController != null)
            return "Pro přivolání auta nejdřív vystup.";
        if (summonGame == game)
            return "Čekám na obnovení vlastního auta.";
        if (!TryFindSummonSpot(player, out var position, out var rotation))
            return "Poblíž není volné místo pro auto.";

        var car = game.PlayerCarController;
        if (IsReadyToSummon(car))
        {
            MoveSummonedCar(car, position, rotation);
            return "Přesun vlastního auta spuštěn.";
        }

        if (PlayerCarRespawnSystem.instance == null)
            return "Herní systém přivolání auta není dostupný.";

        PlayerCarRespawnSystem.RespawnPlayerCarImmediately();
        summonGame = game;
        summonDeadline = Time.unscaledTime + 10f;
        return "Čekám na obnovení vlastního auta.";
    }

    private void FinishSummon()
    {
        var game = summonGame;
        var player = game == GameManager.instance ? GetActivePlayer(game) : null;
        if (player == null || game.CurrentCarController != null)
        {
            summonGame = null;
            status = "Přivolání auta zrušeno.";
            return;
        }

        var car = game.PlayerCarController;
        if (!IsReadyToSummon(car))
        {
            if (Time.unscaledTime < summonDeadline)
                return;

            summonGame = null;
            status = "Hra neobnovila vlastní auto včas.";
            log?.LogWarning(status);
            return;
        }

        summonGame = null;
        try
        {
            if (!TryFindSummonSpot(player, out var position, out var rotation))
            {
                status = "Poblíž není volné místo pro obnovené auto.";
                return;
            }

            MoveSummonedCar(car, position, rotation);
            status = "Přesun obnoveného auta spuštěn.";
        }
        catch (Exception exception)
        {
            status = "Přesun obnoveného auta selhal. Podrobnosti jsou v logu BepInEx.";
            log?.LogWarning(exception);
        }
    }

    private static bool IsReadyToSummon(CarController car) =>
        car != null && car.gameObject.activeInHierarchy && !car.IsDestroyed;

    private void MoveSummonedCar(CarController car, Vector3 position, Quaternion rotation)
    {
        _ = car.Warp(position, rotation);
        log?.LogInfo($"Přesun vlastního auta {car.name} na {position}.");
    }

    private static bool TryFindSummonSpot(
        CharacterControllerBase player, out Vector3 position, out Quaternion rotation)
    {
        var origin = player.transform.position;
        var forward = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.5f)
            forward = Vector3.forward;
        var right = Vector3.Cross(Vector3.up, forward);
        rotation = Quaternion.LookRotation(forward);

        foreach (float distance in SummonDistances)
        {
            foreach (var direction in new[] { forward, right, -right, -forward })
            {
                var probe = origin + direction * distance + Vector3.up * 2f;
                if (!Physics.Raycast(probe, Vector3.down, out var ground, 4f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    ground.normal.y < 0.9f)
                    continue;

                var center = ground.point + Vector3.up * 1.35f;
                if (Physics.CheckBox(center, new Vector3(1.15f, 1.15f, 2.7f), rotation,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                    continue;

                position = ground.point + Vector3.up * 0.6f;
                return true;
            }
        }

        position = default;
        return false;
    }

    private static string Heal(GameManager _, CharacterControllerBase player)
    {
        var health = player.GetComponentInChildren<CharacterHealth>(true);
        if (health == null || health.originalHealth <= 0)
            return "Zdraví hráče není dostupné.";

        health.Heal(Math.Max(0, health.originalHealth - health.Health));
        return "Zdraví doplněno.";
    }

    private string ReadPosition(GameManager game, CharacterControllerBase player)
    {
        var car = game.CurrentCarController;
        var position = car != null ? car.transform.position : player.transform.position;
        x = position.x.ToString("G9", CultureInfo.InvariantCulture);
        y = position.y.ToString("G9", CultureInfo.InvariantCulture);
        z = position.z.ToString("G9", CultureInfo.InvariantCulture);
        return "Souřadnice načteny.";
    }

    private string Teleport(GameManager game, CharacterControllerBase player)
    {
        if (!ParseCoordinate(x, out float px) || !ParseCoordinate(y, out float py) ||
            !ParseCoordinate(z, out float pz))
            return "Zadej tři platné souřadnice.";

        Warp(game, player, new Vector3(px, py, pz));
        return "Teleport spuštěn.";
    }

    private static string TeleportToWaypoint(GameManager game, CharacterControllerBase player)
    {
        var gps = GPS.instance;
        var navigator = gps == null ? null : gps.customGPS;
        var waypoint = navigator == null ? null : navigator.target;
        if (waypoint == null)
            return "Nejdřív nastav vlastní waypoint na mapě.";

        var destination = waypoint.position;
        if (!float.IsFinite(destination.x) || !float.IsFinite(destination.y) ||
            !float.IsFinite(destination.z))
            return "Waypoint nemá platné souřadnice.";

        Warp(game, player, destination);
        return "Teleport na waypoint spuštěn.";
    }

    private static void Warp(GameManager game, CharacterControllerBase player, Vector3 destination)
    {
        var car = game.CurrentCarController;
        if (car != null)
            _ = car.Warp(destination, car.transform.rotation);
        else
            player.Warp(destination, player.transform.rotation);
    }

    private static bool ParseCoordinate(string input, out float value) =>
        float.TryParse(input.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
        float.IsFinite(value) && Math.Abs(value) <= 100_000f;
}
