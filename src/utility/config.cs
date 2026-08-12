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
    /// <summary>
    /// VR boot path. "explore" loads straight into a level (default avatar when datafolder is DATA).
    /// "full" runs the flat intro, main menu, character creation, and save selection on a VR menu screen.
    /// </summary>
    public string vr_boot_mode { get; set; } = "full";
    /// <summary>Width of the front-menu TV quad in metres (vr_boot_mode "full").</summary>
    public float vr_menu_screen_width { get; set; } = 2.2f;
    /// <summary>Distance from the headset to the menu TV quad in metres.</summary>
    public float vr_menu_screen_distance { get; set; } = 2.5f;
    /// <summary>Vertical offset of the menu TV in metres (negative lowers the screen).</summary>
    public float vr_menu_screen_offset_y { get; set; } = -0.4f;
    /// <summary>Brightness multiplier for the intro/menu TV quad (1 = unchanged).</summary>
    public float vr_menu_screen_brightness { get; set; } = 1.45f;
    public bool VrBootExplore =>
        !vr_boot_mode.Equals("full", StringComparison.OrdinalIgnoreCase);
    public bool VrBootFull =>
        vr_boot_mode.Equals("full", StringComparison.OrdinalIgnoreCase);
    /// <summary>Flip thumbstick forward/back if walk direction feels reversed (common on Quest Link).</summary>
    public bool vr_invert_stick_y { get; set; } = false;
    /// <summary>DATA or SAVE0..SAVE4 — used when vr skips menus and loads straight into a level.</summary>
    public string datafolder { get; set; } = "DATA";
    /// <summary>Print VR diagnostics and draw bright debug geometry in the headset.</summary>
    public bool vr_debug { get; set; } = true;
    /// <summary>Throttled logs for intro/menu laser and body-marker visibility (also enabled when vr_debug is true).</summary>
    public bool vr_intro_debug { get; set; } = false;
    /// <summary>Append native VR diagnostics to user://vr_diag.log and project logs/vr_diag.log.</summary>
    public bool vr_diag_log { get; set; } = true;
    /// <summary>Append VR weapon-hand motion samples to user://vr_combat_motion.log for gesture tuning.</summary>
    public bool vr_combat_motion_log { get; set; } = true;
    /// <summary>Show torso-local slash/bash/stab planes and weapon-hand marker in combat mode.</summary>
    public bool vr_combat_gesture_planes { get; set; } = false;
    /// <summary>Overlay shade/cutoff distance debug on the HUD (F11 also toggles). Off by default.</summary>
    public bool vr_light_debug { get; set; } = false;
    /// <summary>
    /// World scale in VR (corridors, ceilings, doors). Applied before level load. Higher = feel shorter.
    /// Door opening ≈ 0.975 * scale meters. For ~7 ft doors use ~2.18.
    /// </summary>
    public float vr_world_scale { get; set; } = 2.18f;
    /// <summary>
    /// Sprite/NPC scale factor (independent of vr_world_scale). Applied before level load.
    /// 0 = match vr_world_scale. At ~2.54, a 48px UW1 NPC is about 6 ft tall.
    /// </summary>
    public float vr_sprite_scale { get; set; } = 2.54f;
    /// <summary>
    /// When true, show the flat SubViewport render on a screen in the headset (legacy fallback).
    /// When false (default), render the dungeon world directly in stereoscopic VR.
    /// </summary>
    public bool vr_mirror { get; set; } = false;
    /// <summary>Show a semi-transparent capsule at the simulated player body (collision position).</summary>
    public bool vr_show_body { get; set; } = true;
    /// <summary>
    /// Native VR: show the DOS-style HUD (inventory, flasks, actions) on a panel attached to the left controller.
    /// </summary>
    public bool vr_hud_panel { get; set; } = true;
    /// <summary>Width of the left-hand HUD quad in metres (height follows 1280×800 aspect).</summary>
    public float vr_hud_panel_width { get; set; } = 0.42f;
    /// <summary>Native VR: head-locked overlays (message scroll, flasks, compass, inventory, attack gem, eyes/gargoyle).</summary>
    public bool vr_status_panels { get; set; } = true;
    /// <summary>When true, head overlays stay visible instead of appearing on events and fading out.</summary>
    public bool vr_status_panels_always_visible { get; set; } = false;
    /// <summary>Seconds to show head overlays after activity before fading (when not always visible).</summary>
    public float vr_status_panels_display_seconds { get; set; } = 5f;
    /// <summary>Seconds for head overlay fade-out (when not always visible).</summary>
    public float vr_status_panels_fade_seconds { get; set; } = 0.75f;
    /// <summary>Virtual HUD screen width in metres for head-locked overlays (scroll, flask, gem, eyes).</summary>
    public float vr_status_screen_width { get; set; } = 2.2f;
    /// <summary>Distance in front of the headset for head-locked overlays.</summary>
    public float vr_status_screen_distance { get; set; } = 2f;
    /// <summary>Vertical offset applied to all head overlays in metres (negative lowers the whole group).</summary>
    public float vr_status_panels_offset_y { get; set; } = -0.4f;
    /// <summary>Head-locked message scroll offset in metres (X = right, Y = up).</summary>
    public float vr_message_scroll_offset_x { get; set; } = 0f;
    public float vr_message_scroll_offset_y { get; set; } = 0f;
    /// <summary>Head-locked health flask offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_health_flask_offset_x { get; set; } = 0f;
    public float vr_health_flask_offset_y { get; set; } = 0f;
    public float vr_health_flask_offset_z { get; set; } = 0f;
    /// <summary>Head-locked mana flask offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_mana_flask_offset_x { get; set; } = 0f;
    public float vr_mana_flask_offset_y { get; set; } = 0f;
    public float vr_mana_flask_offset_z { get; set; } = 0f;
    /// <summary>Head-locked compass offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_compass_offset_x { get; set; } = 0f;
    public float vr_compass_offset_y { get; set; } = 0f;
    public float vr_compass_offset_z { get; set; } = 0f;
    /// <summary>Head-locked inventory/paperdoll offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_inventory_offset_x { get; set; } = 0f;
    public float vr_inventory_offset_y { get; set; } = 0f;
    public float vr_inventory_offset_z { get; set; } = 0f;
    /// <summary>Head-locked rune bag/shelf offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_rune_bag_offset_x { get; set; } = 0f;
    public float vr_rune_bag_offset_y { get; set; } = 0f;
    public float vr_rune_bag_offset_z { get; set; } = 0f;
    /// <summary>Head-locked stats panel offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_stats_offset_x { get; set; } = 0f;
    public float vr_stats_offset_y { get; set; } = 0f;
    public float vr_stats_offset_z { get; set; } = 0f;
    /// <summary>Head-locked selected-rune shelf offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_rune_shelf_offset_x { get; set; } = 0f;
    public float vr_rune_shelf_offset_y { get; set; } = 0f;
    public float vr_rune_shelf_offset_z { get; set; } = 0f;
    /// <summary>Head-locked active spell icons offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_active_spells_offset_x { get; set; } = 0f;
    public float vr_active_spells_offset_y { get; set; } = 0f;
    public float vr_active_spells_offset_z { get; set; } = 0f;
    /// <summary>Head-locked paperdoll pull-chain offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_chain_offset_x { get; set; } = 0f;
    public float vr_chain_offset_y { get; set; } = 0f;
    public float vr_chain_offset_z { get; set; } = 0f;
    /// <summary>Head-locked conversation portrait/dialogue offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_conversation_offset_x { get; set; } = 0f;
    public float vr_conversation_offset_y { get; set; } = 0f;
    public float vr_conversation_offset_z { get; set; } = 0f;
    /// <summary>Head-locked weapon attack animation offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_weapon_anim_offset_x { get; set; } = 0f;
    public float vr_weapon_anim_offset_y { get; set; } = 0f;
    public float vr_weapon_anim_offset_z { get; set; } = 0f;
    /// <summary>Head-locked attack power gem offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_power_gem_offset_x { get; set; } = 0f;
    public float vr_power_gem_offset_y { get; set; } = 0f;
    public float vr_power_gem_offset_z { get; set; } = 0f;
    /// <summary>Head-locked enemy-health eyes/gargoyle offset in metres (X = right, Y = up, Z = farther from headset).</summary>
    public float vr_eyes_offset_x { get; set; } = 0f;
    public float vr_eyes_offset_y { get; set; } = 0f;
    public float vr_eyes_offset_z { get; set; } = 0f;
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
