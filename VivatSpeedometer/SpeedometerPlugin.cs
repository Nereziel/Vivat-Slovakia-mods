using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace VivatPopulationMod;

[BepInPlugin("local.vivat.speedometer", "Vivat Speedometer", "0.1.0")]
public sealed class SpeedometerPlugin : BasePlugin
{
    private SpeedometerHud hud;

    public override void Load()
    {
        bool verboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false,
            "Podrobne informacni vypisy tachometru. Chyby a varovani zustavaji zapnute. Restart hry.").Value;
        if (!Config.Bind("HUD", "Enabled", true, "Zobrazí tachometr při jízdě s kamerou zvenku.").Value)
            return;

        int margin = Config.Bind("HUD", "RightMargin", 180, new ConfigDescription(
            "Odsazení tachometru od pravého okraje v pixelech.",
            new AcceptableValueRange<int>(0, 800))).Value;
        hud = AddComponent<SpeedometerHud>();
        hud.Initialize(Log, margin, verboseLogging);
    }

    public override bool Unload()
    {
        if (hud != null)
            UnityEngine.Object.Destroy(hud);
        return true;
    }
}

public sealed class SpeedometerHud : MonoBehaviour
{
    private ManualLogSource log;
    private bool verboseLogging;
    private int rightMargin;
    private GUIStyle speedometerStyle;
    private int displayedSpeed = -1;
    private string speedometerText = "";
    private bool reportedSpeedometer;

    public SpeedometerHud(IntPtr pointer) : base(pointer) { }

    public void Initialize(ManualLogSource source, int margin, bool enableVerboseLogging)
    {
        log = source;
        verboseLogging = enableVerboseLogging;
        rightMargin = margin;
    }

    private void OnGUI()
    {
        if (Event.current?.type != EventType.Repaint)
            return;

        try
        {
            DrawSpeedometer();
        }
        catch (Exception exception)
        {
            enabled = false;
            log?.LogWarning($"Tachometr vypnut: {exception}");
        }
    }

    private void DrawSpeedometer()
    {
        var game = GameManager.instance;
        if (game == null || game.currentInputState != GameManager.InputState.Gameplay)
            return;

        var car = game.CurrentCarController;
        if (car == null)
            return;

        var cameras = CameraManager.instance;
        var camera = cameras == null ? Camera.main : cameras.Camera;
        if (camera == null || !IsExteriorCamera(car, camera.transform.position))
            return;

        var body = car.Rigidbody;
        if (body == null)
            return;

        float speed = body.linearVelocity.magnitude * 3.6f;
        if (!float.IsFinite(speed))
            return;

        int roundedSpeed = Mathf.RoundToInt(speed);
        if (roundedSpeed != displayedSpeed)
        {
            displayedSpeed = roundedSpeed;
            speedometerText = $"{displayedSpeed} km/h";
        }

        speedometerStyle ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontSize = 24
        };
        var panel = new Rect(Mathf.Max(0, Screen.width - rightMargin - 130), Screen.height - 68, 130, 44);
        GUI.Box(panel, "");
        GUI.Label(panel, speedometerText, speedometerStyle);
        if (verboseLogging && !reportedSpeedometer)
        {
            reportedSpeedometer = true;
            log?.LogInfo("Tachometr zobrazen.");
        }
    }

    private static bool IsExteriorCamera(CarController car, Vector3 cameraPosition)
    {
        var box = car.collider;
        if (box == null)
            return (cameraPosition - car.transform.position).sqrMagnitude > 4f;

        var offset = box.transform.InverseTransformPoint(cameraPosition) - box.center;
        var halfSize = box.size * 0.5f;
        return Mathf.Abs(offset.x) > halfSize.x || Mathf.Abs(offset.y) > halfSize.y ||
               Mathf.Abs(offset.z) > halfSize.z;
    }
}
