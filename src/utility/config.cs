using System.IO;
using System.Text.Json;
using System;
using Godot;
using System.Diagnostics;

namespace Underworld;

public class uwsettings
{

	private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        IgnoreReadOnlyProperties = true,
        PropertyNameCaseInsensitive = true,
    };

	private static readonly string FilePath
		= ProjectSettings.GlobalizePath("user://settings.json");

    public static uwsettings instance;

    // This initialises our instance as soon as the class is loaded.
    static uwsettings() => LoadSettings();

    public static void LoadSettings()
    {

        if (File.Exists(FilePath))
        {
            Debug.Print($"Loading settings from {FilePath}");
            using var stream = File.OpenRead(FilePath);
            instance = JsonSerializer.Deserialize<uwsettings>(stream, JsonOpts);
        }
        else
        {
            Debug.Print($"No existing settings at {FilePath}. Loading defaults.");
            instance = new();
        }

        if (main.cameraPitchGimbal_world != null)
        {
            main.cameraPitchGimbal_world.Fov = Math.Max(50, instance.FOV);
            main.cameraPitchGimbal_sprites.Fov = main.cameraPitchGimbal_world.Fov;
        }

        switch (instance.gametoload.ToUpper())
        {
            case "UW2":
            case "2":
                UWClass._RES = UWClass.GAME_UW2;
                UWClass.BasePath = instance.pathuw2;
                break;
            case "UW1":
            case "1":
                UWClass._RES = UWClass.GAME_UW1;
                UWClass.BasePath = instance.pathuw1;
                break;
            case "UWDEMO":
            case "0":
                UWClass._RES = UWClass.GAME_UWDEMO;
                break;
            default:
                throw new InvalidOperationException("Invalid Game Selected");
        }

        // Backward compat: if legacy 'rompath' is set but new 'synthpath' isn't,
        // promote rompath to synthpath.
        if (string.IsNullOrEmpty(instance.synthpath) && !string.IsNullOrEmpty(instance.rompath))
        {
            instance.synthpath = instance.rompath;
            Debug.Print("Warning: 'rompath' setting is deprecated, use 'synthpath' instead.");
        }

    }

    public string pathuw1 { get; set; } = @"C:\Games\UW";
    public string pathuw2 { get; set; } = @"C:\Games\UW2";
    public string gametoload { get; set; } = "UW1";
    public int level { get; set; } = 0;
    /// <summary>When true, start in OpenXR VR mode with head tracking and thumbstick movement.</summary>
    public bool vr { get; set; } = false;
    /// <summary>Flip thumbstick forward/back if walk direction feels reversed (common on Quest Link).</summary>
    public bool vr_invert_stick_y { get; set; } = false;
    /// <summary>DATA or SAVE0..SAVE4 — used when vr skips menus and loads straight into a level.</summary>
    public string datafolder { get; set; } = "DATA";
    /// <summary>Print VR diagnostics and draw bright debug geometry in the headset.</summary>
    public bool vr_debug { get; set; } = true;
    /// <summary>World scale in VR (corridors, ceilings, sprites). Applied before level load. Higher = feel shorter.</summary>
    public float vr_world_scale { get; set; } = 2.7f;
    /// <summary>
    /// When true, show the flat SubViewport render on a screen in the headset (legacy fallback).
    /// When false (default), render the dungeon world directly in stereoscopic VR.
    /// </summary>
    public bool vr_mirror { get; set; } = false;
    /// <summary>Show a semi-transparent capsule at the simulated player body (collision position).</summary>
    public bool vr_show_body { get; set; } = true;
    public float FOV { get; set; } = 75;
    public bool showcolliders { get; set; }
    public int shaderbandsize { get; set; } = 8;
    public string synth { get; set; } = "soundfont";
    public string synthpath { get; set; } = "";
    // Legacy field, still read for backward compatibility. If set and synthpath is empty,
    // synthpath is populated from this in LoadSettings.
    public string rompath { get; set; } = "";

    public void Save()
    {
        Debug.Print($"Saving settings to {FilePath}");
        using var stream = File.OpenWrite(FilePath);
        JsonSerializer.Serialize(stream, this, JsonOpts);
    }

}
