using Godot;

namespace Underworld
{
    public class tmap:model3D
    {
        Node3D tmapnode;

        float tmapOffset
        {
            get
            {
                if (UWTileMap.ValidTile(uwobject.tileX, uwobject.tileY))
                {
                    var tile = UWTileMap.current_tilemap.Tiles[uwobject.tileX, uwobject.tileY];
                    var door = objectsearch.FindMatchInObjectChain(tile.indexObjectList, 5, 0, -1, UWTileMap.current_tilemap.LevelObjects);
                    if (door != null && door.xpos == uwobject.xpos && door.ypos == uwobject.ypos)
                    {
                        return 0.1f;
                    }
                }

                switch (uwobject.heading)
                {
                    case 0 when uwobject.ypos == 7:
                        return -0.13f;
                    case 2 when uwobject.xpos == 7:
                        return -0.13f;
                    case 4 when uwobject.ypos == 0:
                        return +0.13f;
                    case 6 when uwobject.xpos == 0:
                        return +0.13f;
                }

                return 0.07f;
            }
        }

        /// <summary>Half-tile width / tile height in Godot metres (matches Hank's 0.6 / 1.2 at scale 1).</summary>
        static float PanelHalfWidth => tileMapRender.HalfTileWidth;
        static float PanelHeight => tileMapRender.TileWidth;

        public tmap(uwObject _uwobject)
        {
            uwobject = _uwobject;
        }

        public static tmap CreateInstance(Node3D parent, uwObject obj, UWTileMap a_tilemap, string name)
        {
            var t = new tmap(obj);
            t.tmapnode = t.Generate3DModel(parent, name);
            SetModelRotation(parent, t);
            // AlignToWall multiplies nudge by WorldScaleFactor; counteract so wall offset stays ~0.1m.
            var nudge = 0.1f / Mathf.Max(1f, tileMapRender.WorldScaleFactor);
            model3D.AlignToWall(parent, obj, nudgeFactor: nudge);
            if (uwsettings.instance.vr_debug)
            {
                t.AttachDebugOverlay(parent, nudge);
            }
            return t;
        }

        public static bool LookAt(uwObject obj)
        {
            int textureindex = UWTileMap.current_tilemap.texture_map[obj.owner];
            uimanager.AddToMessageScroll(GameStrings.TextureDescription(textureindex));
            if ((textureindex == 142) && ((_RES != GAME_UW2)))
            {//This is a window into the abyss.
                uimanager.DisplayCutsImage(cutsfile: "cs400.n01", imageNo: playerdat.dungeon_level - 1, targetControl: uimanager.CutsSmall);
            }
            return true; //prevents the default you cannot use message
        }


        float ExtrudeForMesh()
        {
            var raw = tmapOffset;
            var scale = Mathf.Max(1f, tileMapRender.WorldScaleFactor);
            if (scale <= 1f)
            {
                return raw;
            }

            // Small positive offsets (default 0.07, door 0.1) sit too far out at VR scale.
            if (raw > 0f && raw <= 0.14f)
            {
                return raw / scale;
            }

            // Heading hacks (±0.13): negative values pull into the wall; descaling them
            // pushes the face into the room. Instead nudge further into the wall by ~wScale texels.
            if (raw < 0f && raw >= -0.14f)
            {
                return raw - (scale - 1f) * 2.2f * tileMapRender.WallTexelWorld;
            }

            return raw;
        }

        public override Vector3[] ModelVertices()
        {
            var offset = ExtrudeForMesh();
            Vector3[] v = new Vector3[4];
            v[0] = new Vector3(-PanelHalfWidth, 0f, offset);
            v[1] = new Vector3(PanelHalfWidth, 0f, offset);
            v[2] = new Vector3(PanelHalfWidth, PanelHeight, offset);
            v[3] = new Vector3(-PanelHalfWidth, PanelHeight, offset);
            return v;
        }

        public override int[] ModelTriangles(int meshNo)
        {
            int[] tris = new int[6];
            tris[0] = 1;
            tris[1] = 0;
            tris[2] = 3;
            tris[3] = 3;
            tris[4] = 2;
            tris[5] = 1;
            return tris;
        }

        public override Vector2[] ModelUVs(Vector3[] verts)
        {
            Vector2[] v = new Vector2[4];
            v[0] = new Vector2(0,1);
            v[1] = new Vector2(1,1);
            v[2] = new Vector2(1,0);
            v[3]  = new Vector2(0,0);
            return v;
        }


        public override ShaderMaterial GetMaterial(int textureno, int surface)
        {
            if (surface != 6)
            {
                return tileMapRender.mapTexturesWalls.GetMaterialForObject(
                    textureno: uwobject.owner,
                    texturemap: UWTileMap.current_tilemap.texture_map,
                    obj: uwobject);
            }

            return base.GetMaterial(0, 6);
        }

        void AttachDebugOverlay(Node3D parent, float alignNudge)
        {
            var rawOffset = tmapOffset;
            var extrude = ExtrudeForMesh();
            var wallTex = ComputeFaceWallDistanceTexels(parent, extrude);
            var edge = EdgeLabel(uwobject);
            var label = new Label3D
            {
                Name = "TmapDebug",
                Text = $"tmap #{uwobject.index}\n" +
                       $"tile {uwobject.tileX},{uwobject.tileY} {edge}\n" +
                       $"h{uwobject.heading} xy({uwobject.xpos},{uwobject.ypos}) z{uwobject.zpos}\n" +
                       $"ci=0x{uwobject.classindex:X} owner={uwobject.owner}\n" +
                       $"rawOff={rawOffset:F3} extrude={extrude:F4}\n" +
                       $"alignNudge={alignNudge:F4} wScale={tileMapRender.WorldScaleFactor:F2}\n" +
                       $"wallDist={wallTex:F2} texels",
                FontSize = 48,
                PixelSize = 0.0012f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.95f, 0.2f),
                OutlineSize = 4,
                OutlineModulate = Colors.Black,
                Position = new Vector3(0f, PanelHeight + 0.08f, extrude + 0.02f),
                Layers = main.LayerGeo | main.LayerXFER,
            };
            parent.AddChild(label);
        }

        static string EdgeLabel(uwObject obj)
        {
            if (obj.xpos == 0) return "west";
            if (obj.xpos == 7) return "east";
            if (obj.ypos == 0) return "north";
            if (obj.ypos == 7) return "south";
            return "interior";
        }

        float ComputeFaceWallDistanceTexels(Node3D parent, float extrudeLocal)
        {
            if (!model3D.TryGetWallMountFrame(uwobject, out var roomNormal, out var wallPoint, parent.GlobalPosition))
            {
                return -1f;
            }

            var faceCenterLocal = new Vector3(0f, PanelHeight * 0.5f, extrudeLocal);
            var faceWorld = parent.GlobalTransform * faceCenterLocal;
            var depthMeters = (faceWorld - wallPoint).Dot(roomNormal);
            return depthMeters / tileMapRender.WallTexelWorld;
        }
    }
}
