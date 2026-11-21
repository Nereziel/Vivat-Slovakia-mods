using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using FMOD;
using FMODUnity;
using UnityEngine;
using Vivat.Characters;
using Vivat.Systems.WantedSystem;

namespace VivatPopulationMod;

public sealed class PoliceVoicePlayback : MonoBehaviour
{
    private static readonly Dictionary<int, Policeman> Officers = new();
    private readonly List<Sound[]> voices = new();
    private readonly List<CharacterControllerBase> nearbyPolice = new();
    private ManualLogSource log;
    private Channel channel;
    private bool hasChannel;
    private float nextCheck;
    private float nextLine;
    private float nextSearch;
    private float nextStatus;
    private int phrase;
    private bool reportedPlayback;

    public PoliceVoicePlayback(IntPtr pointer) : base(pointer) { }

    internal void Initialize(ManualLogSource source) => log = source;

    internal static void Register(Policeman __instance) =>
        Officers[__instance.GetInstanceID()] = __instance;

    internal static void Unregister(Policeman __instance) =>
        Officers.Remove(__instance.GetInstanceID());

    private void Update()
    {
        float now = Time.unscaledTime;
        if (now < nextCheck)
            return;
        nextCheck = now + 0.4f;

        try
        {
            var game = GameManager.instance;
            var player = game == null ? null : game.PlayerController;
            if (player == null || player.IsDead)
            {
                StopLine();
                return;
            }

            if (voices.Count == 0)
            {
                LoadVoices();
                if (voices.Count == 0)
                {
                    enabled = false;
                    return;
                }
            }

            if (WantedSystem.WantedLevel != WantedLevel.Active)
            {
                StopLine();
                Officers.Clear();
                nearbyPolice.Clear();
                nextSearch = 0f;
                return;
            }

            if (game.currentInputState != GameManager.InputState.Gameplay || now < nextLine)
                return;

            var origin = player.transform.position;
            CharacterControllerBase speaker = null;
            float closest = 65f * 65f;
            foreach (var pair in Officers)
            {
                var officer = pair.Value;
                if (officer == null || !officer.isActiveAndEnabled)
                    continue;

                var brain = officer.Brain;
                SelectSpeaker(brain == null ? null : brain.Controller, origin, ref closest, ref speaker);
            }

            if (speaker == null)
            {
                if (now >= nextSearch)
                {
                    nextSearch = now + 3f;
                    nearbyPolice.Clear();
                    foreach (var character in UnityEngine.Object.FindObjectsByType<CharacterControllerBase>(
                                 FindObjectsSortMode.None))
                    {
                        if (character != null && character.Faction == Faction.Police &&
                            (character.transform.position - origin).sqrMagnitude < 85f * 85f)
                            nearbyPolice.Add(character);
                    }
                }
                foreach (var character in nearbyPolice)
                    SelectSpeaker(character, origin, ref closest, ref speaker);
            }

            if (speaker == null)
            {
                if (now >= nextStatus)
                {
                    nextStatus = now + 12f;
                    log?.LogInfo($"Policejní pokřiky: žádný pěší policista v dosahu " +
                                 $"({Officers.Count} registrovaných, {nearbyPolice.Count} nalezených u hráče).");
                }
                return;
            }

            int speakerId = speaker.GetInstanceID();
            var clips = voices[(int)((uint)speakerId % (uint)voices.Count)];
            nextLine = now + UnityEngine.Random.Range(5f, 8f);
            PlayLine(clips[phrase++ % clips.Length], speaker.transform.position + Vector3.up * 1.5f);
        }
        catch (Exception exception)
        {
            StopLine();
            log?.LogWarning($"Policejní pokřiky vypnuty: {exception}");
            enabled = false;
        }
    }

    private static void SelectSpeaker(CharacterControllerBase character, Vector3 origin, ref float closest,
        ref CharacterControllerBase speaker)
    {
        if (character == null || !character.gameObject.activeInHierarchy ||
            character.IsPlayer || character.IsDead || character.IsInCar)
            return;

        float distance = (character.transform.position - origin).sqrMagnitude;
        if (distance < closest)
        {
            closest = distance;
            speaker = character;
        }
    }

    private void LoadVoices()
    {
        var system = RuntimeManager.CoreSystem;
        var root = Path.Combine(Paths.PluginPath, "VivatPoliceVoices");
        foreach (string voice in new[] { "charon", "fenrir" })
        {
            string directory = Path.Combine(root, voice);
            if (!Directory.Exists(directory))
                continue;

            var files = Directory.GetFiles(directory, "*.wav");
            Array.Sort(files, StringComparer.Ordinal);
            var clips = new List<Sound>();
            foreach (string path in files)
            {
                var mode = MODE._3D | MODE._3D_WORLDRELATIVE | MODE._3D_LINEARROLLOFF |
                           MODE.LOOP_OFF | MODE.CREATESAMPLE;
                var result = system.createSound(path, mode, out Sound sound);
                if (result == RESULT.OK)
                    clips.Add(sound);
                else
                    log?.LogWarning($"Policejní zvuk {Path.GetFileName(path)}: {result}");
            }
            if (clips.Count > 0)
                voices.Add(clips.ToArray());
        }
        log?.LogInfo($"Policejní pokřiky: načteno {voices.Count} hlasů.");
    }

    private void PlayLine(Sound clip, Vector3 position)
    {
        StopLine();
        var system = RuntimeManager.CoreSystem;
        var result = system.playSound(clip, default, true, out channel);
        if (result != RESULT.OK)
        {
            log?.LogWarning($"Policejní pokřik: FMOD playSound vrátil {result}.");
            return;
        }

        hasChannel = true;
        var source = new VECTOR { x = position.x, y = position.y, z = position.z };
        var velocity = new VECTOR();
        var spatial = channel.set3DAttributes(ref source, ref velocity);
        var range = channel.set3DMinMaxDistance(7f, 70f);
        var playback = spatial == RESULT.OK && range == RESULT.OK ? channel.setPaused(false) : RESULT.ERR_INTERNAL;
        if (playback != RESULT.OK)
        {
            log?.LogWarning($"Policejní pokřik: FMOD pozice={spatial}, dosah={range}, přehrání={playback}.");
            StopLine();
            return;
        }

        if (!reportedPlayback)
        {
            reportedPlayback = true;
            log?.LogInfo($"Přehrán první prostorový policejní pokřik. Posluchači: {StudioListener.ListenerCount}.");
        }
    }

    private void StopLine()
    {
        if (!hasChannel)
            return;
        channel.stop();
        hasChannel = false;
    }

    private void OnDestroy()
    {
        StopLine();
        foreach (var clips in voices)
            foreach (var clip in clips)
                clip.release();
        voices.Clear();
        Officers.Clear();
    }
}
