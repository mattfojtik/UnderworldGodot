using System;
using System.Data;
using System.Diagnostics;
using System.IO;
using System.Text;
using Godot;

namespace Underworld
{
    /// <summary>
    /// Class for loading and accessing shades.dat
    /// </summary>
    public class shade : ArtLoader
    {
        public int mapindex;
        int StartingLightLevel;
        int ViewingDistance;

        /// <summary>Viewing-distance band count from SHADES.DAT (0–15).</summary>
        public int ViewingDistanceUnits => ViewingDistance;

        int Shading;
        int StartOfShadingDistance;

        public static shade[] shadesdata;

        public short[] shadingbasedata;

        public byte[] ShadingArray_26EE = new byte[17 * 66];//This array is probably the light map that should be used for the shading but the existing effect looks right enough. possible structure is byte0 - is point visible, byte1 shading value to use at that point?

        /// <summary>
        /// A simplified image of the shading data. Used in the shader with the object info layer to determine what is in darkness.
        /// </summary>
        public ImageTexture simpleshade;

        public static float GetViewingDistance(int index)
        {
            // Match Hank: fixed 7 viewing bands (4.8f * 7 at design scale). Torch/ambient brightness
            // comes from the smoothpalette row (lightlevel), not a shorter cutoff distance.
            _ = index;
            const int viewingUnits = 7;
            return Mathf.Max(viewingUnits * tileMapRender.godotscale.Y, 0.01f);
        }

        /// <summary>World-space point used for shade/fog distance (simulated avatar eye, not HMD).</summary>
        public static Vector3 GetAvatarShadeOrigin()
        {
            var px = motion.playerMotionParams.x_0;
            var py = motion.playerMotionParams.y_2;
            var pz = motion.playerMotionParams.z_4 + 0xA4;
            return uwObject.XYZToVector3(px, py, pz);
        }

        public static void UpdateShaderShadeUniforms(int lightLevel)
        {
            RenderingServer.GlobalShaderParameterSet("avatar_shade_origin", GetAvatarShadeOrigin());
            RenderingServer.GlobalShaderParameterSet("cutoffdistance", GetViewingDistance(lightLevel));
        }

        static int _lightingDebugLogFrame;
        static int _lightingDebugRayFrame;
        static int _lastLoggedLightLevel = -1;

        /// <summary>Runtime lighting/shade distance diagnostics (HUD label + throttled log).</summary>
        public static string BuildLightingDebugText(int lightLevel)
        {
            try
            {
                return BuildLightingDebugTextCore(lightLevel, forceViewRay: false);
            }
            catch (Exception ex)
            {
                return $"=== Shade debug (error) ===\n{ex.GetType().Name}: {ex.Message}";
            }
        }

        static string BuildLightingDebugTextCore(int lightLevel, bool forceViewRay)
        {
            var cutoff = GetViewingDistance(lightLevel);
            var origin = GetAvatarShadeOrigin();
            var tilemap = VrController.GetTilemapNode(main.instance);
            var tilemapScale = tilemap?.Scale ?? Vector3.One;

            var sb = new StringBuilder();
            sb.AppendLine("=== Shade / lighting debug ===");
            sb.AppendLine($"lightlevel={lightLevel}  cutoff={cutoff:F2}  (Hank flat ref=33.60)");
            sb.AppendLine($"godotscale.Y={tileMapRender.godotscale.Y:F3}  WorldScaleFactor={tileMapRender.WorldScaleFactor:F3}");
            sb.AppendLine($"tilemap.Scale={tilemapScale}  TileWidth={tileMapRender.TileWidth:F3}");
            sb.AppendLine($"palette={Palette.CurrentPalette}  ColourTone={Palette.ColourTone}  paletteCycle={PaletteLoader.NextPaletteCycle_GAME}");
            sb.AppendLine($"final_color_pass={(VrController.IsActive && !uwsettings.instance.vr_mirror)}  UseHdr2D={GetRootViewportUseHdr2D()}");
            AppendSmoothPaletteSample(sb, lightLevel);
            if (shadesdata != null && lightLevel >= 0 && lightLevel < shadesdata.Length)
            {
                sb.AppendLine($"SHADES[{lightLevel}].ViewDistUnits={shadesdata[lightLevel].ViewingDistanceUnits} (cutoff ignores this; Hank uses 7)");
            }

            sb.AppendLine($"avatar_shade_origin=({origin.X:F2}, {origin.Y:F2}, {origin.Z:F2})");

            var hasHmd = VrController.TryGetXrEyeWorldPosition(out var hmd);
            if (hasHmd)
            {
                var delta = hmd - origin;
                sb.AppendLine($"HMD world=({hmd.X:F2}, {hmd.Y:F2}, {hmd.Z:F2})");
                sb.AppendLine($"HMD-avatar dXYZ={delta.Length():F3}  dXZ={new Vector2(delta.X, delta.Z).Length():F3}");
            }

            if (main.cameraPitchGimbal_world != null)
            {
                var cam = main.cameraPitchGimbal_world.GlobalPosition;
                sb.AppendLine($"camera world=({cam.X:F2}, {cam.Y:F2}, {cam.Z:F2})");
            }

            _lightingDebugRayFrame++;
            var runRay = forceViewRay || _lightingDebugRayFrame % 30 == 0;
            if (runRay && VrController.TryRaycastFromView(out var hitPos, cutoff * 1.5f))
            {
                AppendDistanceProbe(sb, "view ray hit", origin, hasHmd, hmd, hitPos, cutoff);
            }
            else if (!runRay)
            {
                sb.AppendLine("view ray: (throttled; updates every 30 frames)");
            }
            else
            {
                sb.AppendLine("view ray: no hit");
            }

            var forward = VrController.GetViewForwardWorld();
            var probe7 = origin + forward * (7f * tileMapRender.TileWidth);
            AppendDistanceProbe(sb, "7-tile probe (walls=XYZ, sprites=XZ)", origin, hasHmd, hmd, probe7, cutoff);

            var forwardXZ = new Vector3(forward.X, 0f, forward.Z);
            if (forwardXZ.LengthSquared() > 0.0001f)
            {
                forwardXZ = forwardXZ.Normalized();
                var probe7xz = origin + forwardXZ * (7f * tileMapRender.TileWidth);
                AppendDistanceProbe(sb, "7-tile XZ-only probe", origin, hasHmd, hmd, probe7xz, cutoff);
            }

            sb.AppendLine("maptouse=dist/cutoff  (1.0=fully dark; Hank torch uses palette row)");
            return sb.ToString().TrimEnd();
        }

        static void AppendSmoothPaletteSample(StringBuilder sb, int lightLevel)
        {
            try
            {
                if (PaletteLoader.Palettes == null || Palette.CurrentPalette < 0
                    || Palette.CurrentPalette >= PaletteLoader.Palettes.Length)
                {
                    return;
                }

                var cycled = PaletteLoader.Palettes[Palette.CurrentPalette].cycledGamePalette;
                if (cycled == null)
                {
                    return;
                }

                int cycle = PaletteLoader.NextPaletteCycle_GAME;
                if (cycle < 0)
                {
                    cycle = 0;
                }
                else if (cycle > cycled.GetUpperBound(2))
                {
                    cycle = cycled.GetUpperBound(2);
                }

                int tone = Math.Clamp(Palette.ColourTone, 0, 1);
                int level = Math.Clamp(lightLevel, 0, 7);
                var tex = cycled[tone, level, cycle];
                var img = tex?.GetImage();
                if (img == null)
                {
                    return;
                }

                int py = (int)Math.Round(0.25f * (img.GetHeight() - 1));
                py = Math.Clamp(py, 0, img.GetHeight() - 1);
                var near = img.GetPixel(128, py);
                var far = img.GetPixel(128, img.GetHeight() - 1);
                sb.AppendLine(
                    $"smoothpalette[{tone},{level},{cycle}] px128 @maptouse0.25=({near.R8},{near.G8},{near.B8}) @1.0=({far.R8},{far.G8},{far.B8})");
            }
            catch (Exception ex)
            {
                sb.AppendLine($"smoothpalette sample: {ex.GetType().Name}");
            }
        }

        static void AppendDistanceProbe(StringBuilder sb, string label, Vector3 avatarOrigin, bool hasHmd, Vector3 hmd, Vector3 target, float cutoff)
        {
            var dAvatarXyz = avatarOrigin.DistanceTo(target);
            var dAvatarXz = DistanceXZ(avatarOrigin, target);
            sb.AppendLine($"{label}:");
            sb.AppendLine($"  avatar dXYZ={dAvatarXyz:F2} maptouseXYZ={dAvatarXyz / cutoff:F3}");
            sb.AppendLine($"  avatar dXZ={dAvatarXz:F2} maptouseXZ={dAvatarXz / cutoff:F3}  (sprites/NPCs)");
            if (hasHmd)
            {
                var dHmdXyz = hmd.DistanceTo(target);
                var dHmdXz = DistanceXZ(hmd, target);
                sb.AppendLine($"  HMD    dXYZ={dHmdXyz:F2} maptouseXYZ={dHmdXyz / cutoff:F3}");
                sb.AppendLine($"  HMD    dXZ={dHmdXz:F2} maptouseXZ={dHmdXz / cutoff:F3}");
            }
        }

        static float DistanceXZ(Vector3 a, Vector3 b)
        {
            var dx = a.X - b.X;
            var dz = a.Z - b.Z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        static bool GetRootViewportUseHdr2D()
        {
            return main.instance?.GetTree()?.Root?.GetViewport()?.UseHdr2D ?? false;
        }

        static bool _lightingLogPathAnnounced;

        public static void MaybeLogLightingDebug(int lightLevel)
        {
            if (!uwsettings.instance.vr_light_debug || !VrController.IsActive || uwsettings.instance.vr_mirror)
            {
                return;
            }

            _lightingDebugLogFrame++;
            var lightLevelChanged = lightLevel != _lastLoggedLightLevel;
            if (!lightLevelChanged && _lightingDebugLogFrame % 120 != 0)
            {
                return;
            }

            _lastLoggedLightLevel = lightLevel;

            string text;
            try
            {
                text = BuildLightingDebugTextCore(lightLevel, forceViewRay: lightLevelChanged);
            }
            catch (Exception ex)
            {
                text = $"=== Shade debug (error) ===\n{ex.GetType().Name}: {ex.Message}";
            }

            WriteLightingDebugLog(text, append: lightLevelChanged);
            GD.Print(text);
        }

        static void WriteLightingDebugLog(string body, bool append = false)
        {
            try
            {
                var projectDir = ProjectSettings.GlobalizePath("res://");
                var logDir = Path.Combine(projectDir, "logs");
                Directory.CreateDirectory(logDir);
                var logPath = Path.Combine(logDir, "vr_lighting_debug.log");
                var stamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                var frame = Engine.GetFramesDrawn();
                var header = $"=== {stamp}  frame={frame} ===";
                var entry = header + "\n" + body + "\n";
                if (append && File.Exists(logPath))
                {
                    File.AppendAllText(logPath, entry);
                }
                else
                {
                    File.WriteAllText(logPath, entry);
                }

                if (!_lightingLogPathAnnounced)
                {
                    _lightingLogPathAnnounced = true;
                    VrDiagLog.Print($"[VR lighting] Debug log file: {logPath}");
                }
            }
            catch (Exception ex)
            {
                VrDiagLog.Warn($"[VR lighting] Could not write debug log: {ex.Message}");
            }
        }

        /// <summary>
        /// Creates a banded light map image for the uwshader that lerps shade bands to allow smoother shading.
        /// </summary>
        /// <param name="pal"></param>
        /// <param name="maps"></param>
        /// <param name="index"></param>
        /// <returns></returns>
        public static Godot.ImageTexture GetFullShadingImage(Palette pal, lightmap[] maps, int index, short[] shadingdata)
        {
            int BandSize = Math.Max(uwsettings.instance.shaderbandsize, 1);
            var img = Godot.Image.CreateEmpty(256, BandSize * 15, false, Godot.Image.Format.Rgba8);

            lightmap basemap = maps[0];
            lightmap nextmap = maps[1];

            for (int i = 0; i < shadingdata.GetUpperBound(0); i++)
            {
                bool doLerp = shadingdata[i] != shadingdata[i + 1];//Only lerp the shader bands when the shading is different
                for (int y = 0; y < BandSize; y++)
                {
                    if (y % BandSize == 0)
                    {
                        //At a band that contains colours specified by the light map.
                        //Apply primary colour band
                        basemap = maps[shadingdata[i]];
                        if (i + 1 < maps.GetUpperBound(0))
                        {
                            nextmap = maps[shadingdata[i + 1]];
                        }
                        else
                        {
                            //on last band. finish here.
                            //Debug.Print("LastBand");
                        }
                        for (int x = 0; x < 256; x++)
                        {
                            Color colour;
                            int pixel = basemap.red[x];
                            colour = pal.ColorAtIndex((byte)pixel, true, false);
                            img.SetPixel(x, y + i * BandSize, colour);
                        }
                    }
                    else
                    {//in betweeen lightmap bands. Lerp from the first band to this.
                        for (int x = 0; x < 256; x++)
                        { //apply a lerped colour band from the last to the next
                            //var basepixel = basemap.red[x];
                            //var nextpixel = nextmap.red[x];
                            var basecolour = pal.ColorAtIndex((byte)basemap.red[x], true, false);

                            if (doLerp)
                            {
                                //Color lerpedcolour;
                                var nextcolour = pal.ColorAtIndex((byte)nextmap.red[x], true, false);
                                //lerpedcolour = basecolour.Lerp(nextcolour, (float)(y % BandSize) / (float)BandSize);
                                img.SetPixel(x, y + i * BandSize, basecolour.Lerp(nextcolour, (float)(y % BandSize) / (float)BandSize));
                            }
                            else
                            {
                                //skip lerp if the shading has not changed.
                                //lerpedcolour = basecolour;
                                img.SetPixel(x, y + i * BandSize, basecolour);
                            }   
                        }
                    }
                }
            }
            //img.SavePng($"c:\\temp\\colourmap {index}.png");

            var tex = new ImageTexture();
            tex.SetImage(img);
            return tex;
        }


        /// <summary>
        /// Returns an array of the light maps to be used in this shade. Likely I am not returning the correct shading values but the current effect looks okay enough.
        /// </summary>
        /// <param name="shadesArray"></param>
        public short[] ExtractShadeArray()
        {
            short[] shadesArray = new short[16];
            if (ViewingDistance >= 16)
            {   //return all zeros.
                return shadesArray;
            }
            for (short si = 0; si < 16; si++)
            {
                if (si <= ViewingDistance)
                {
                    short ax = si;
                    ax = (short)Math.Pow(ax * 8, 2);
                    //int var6 = ax;
                    ax = (short)(ax << 1);
                    //int var4 = ax;
                    ax = (short)UnderWorldSqrt.sqrt_vanilla((ushort)ax);  //(short)Math.Sqrt(ax);
                    short var6 = ax;
                    int var4 = (short)(var6 * Shading / 64);
                    var4 += StartOfShadingDistance;
                    if (var4 < 0)
                    {
                        var4 = 0;
                    }
                    var6 = (short)(var4 + StartingLightLevel);
                    if (var6 > 14)
                    {
                        var6 = 14;
                    }
                    shadesArray[si] = var6;
                }
                else
                {
                    shadesArray[si] = 0xF; //darkness
                }
            } //loop si 1

            var di = 0;
            while (di < 0x11)
            {
                var si = 0;
                while (si < 0x21)
                {
                    //seg32_54C
                    //var var2 = (int)Math.Round(Math.Sqrt((0x10 - si) * (0x10 - si) + di * di), 0); // 
                    //vanilla underworld sqrt is used here because it slightly different values are returned compared to .NET sqrt. 
                    // This has later impacts on tile visibility calcs for the automap
                    // .eg when di = 0x2 and si = 0xE the (int)sqrt() will return 2 but vanilla game will return 3
                    var var2 = (short)UnderWorldSqrt.sqrt_vanilla((ushort)((0x10 - si) * (0x10 - si) + (di * di)));

                    if (var2 <= ViewingDistance)
                    {
                        //seg32_58B
                        ShadingArray_26EE[di * 66 + (si << 1) + 1] = (byte)shadesArray[var2];//33 used to be 66 
                    }
                    else
                    {
                        //Seg32_577
                        ShadingArray_26EE[di * 66 + (si << 1) + 1] = 0xF;
                    }
                    si++;
                }
                di++;
            }

            // File.WriteAllBytes($"c:\\temp\\shade_{mapindex}.dat", ShadingArray_26EF);
            return shadesArray;
        }

        public shade(int _index, int _Shading, int _StartingLightLevel, int _StartOfShadingDistance, int _ViewingDistance)
        {
            mapindex = _index;
            Shading = _Shading; //I had this as near dist. UnderworldAdventures calls it shading?
            StartingLightLevel = _StartingLightLevel & 0xF;
            StartOfShadingDistance = _StartOfShadingDistance;
            ViewingDistance = _ViewingDistance & 0xF;
            shadingbasedata = ExtractShadeArray();
            simpleshade = CreateSimpleShade(shadingbasedata);
        }

        static shade()
        {
            var path = System.IO.Path.Combine(BasePath, "DATA", "SHADES.DAT");
            if (System.IO.File.Exists(path))
            {
                if (ReadStreamFile(path, out byte[] buffer))
                {
                    shadesdata = new shade[8];
                    for (int i = 0; i < 8; i++)
                    {
                        try
                        {
                            shadesdata[i] = new shade(
                                _index: i,
                                _Shading: (int)(Int16)getAt(buffer, 0 + (i * 12), 16),
                                _StartingLightLevel: (int)getAt(buffer, 2 + (i * 12), 16),
                                _StartOfShadingDistance: (int)(Int16)getAt(buffer, 4 + (i * 12), 16),
                                _ViewingDistance: (int)getAt(buffer, 6 + (i * 12), 16)
                            );
                        }
                        catch
                        {
                            CreateEmptyShades();
                            return;
                        }
                    }
                }
            }
            else
            {
                CreateEmptyShades();
            }
        }

        private static void CreateEmptyShades()
        {
            shadesdata = new shade[8];
            Debug.Print("Defaulting to fullbright shades.");
            //initial an array of empty shades that provide full bright
            for (int i = 0; i < 8; i++)
            {
                shadesdata[i] = new shade(
                    _index: i,
                    _Shading: 0,
                    _StartingLightLevel: 0,
                    _StartOfShadingDistance: 0,
                    _ViewingDistance: 20
                 );
            }
        }
    
        public static ImageTexture CreateSimpleShade(short[] data)
        {
            var img = Godot.Image.CreateEmpty(data.GetUpperBound(0), 1, false, Godot.Image.Format.R8);
            var color = new Color();
            for (var x = 0; x< data.GetUpperBound(0);x++)
            {
                var colorindex = Math.Min((int)data[x], 0xF);                
                color.R8 = colorindex * 8;
                img.SetPixel(x, 0, color);
            }
            var tex = new ImageTexture();
            tex.SetImage(img);
            //img.SavePng($"c:\\temp\\simple.png");
            return tex;
        }
    }//end class    
}//end namespace