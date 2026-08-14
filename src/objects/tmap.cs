using Godot;

namespace Underworld
{
    /// <summary>
    /// Wall texture map (tmap) — a full-tile textured quad mounted on a wall.
    /// Placement is ground-truth: mesh face at local Z=0, parent snapped so that face
    /// lies on the tilemap wall plane + <see cref="uwsettings.vr_tmap_wall_offset_m"/>.
    /// </summary>
    public class tmap : model3D
    {
        Node3D tmapnode;

        /// <summary>Half-tile width / tile height in Godot metres (matches Hank's 0.6 / 1.2 at scale 1).</summary>
        static float PanelHalfWidth => tileMapRender.HalfTileWidth;
        static float PanelHeight => tileMapRender.TileWidth;

        static float WallOffsetMetres =>
            Mathf.Max(0f, uwsettings.instance?.vr_tmap_wall_offset_m ?? 0.1f);

        static bool IsDiagonalTile(short tileType) =>
            tileType is UWTileMap.TILE_DIAG_SE or UWTileMap.TILE_DIAG_SW
                or UWTileMap.TILE_DIAG_NE or UWTileMap.TILE_DIAG_NW;

        public tmap(uwObject _uwobject)
        {
            uwobject = _uwobject;
        }

        public static tmap CreateInstance(Node3D parent, uwObject obj, UWTileMap a_tilemap, string name)
        {
            var t = new tmap(obj);
            t.tmapnode = t.Generate3DModel(parent, name);
            SetModelRotation(parent, t);

            var offsetM = WallOffsetMetres;
            var tileType = UWTileMap.ValidTile(obj.tileX, obj.tileY)
                ? a_tilemap.Tiles[obj.tileX, obj.tileY].tileType : (short)-1;
            string placement;
            if (IsDiagonalTile(tileType))
            {
                PlaceOnDiagonalWallPlane(parent, obj, t.FaceSampleLocals(), tileType, offsetM);
                placement = "diagonal-plane";
            }
            else
            {
                // Face verts are at local Z=0; snap that plane onto the tilemap wall + offset.
                PlaceWallMountedDepth(parent, obj, t.FaceSampleLocals(), offsetM);
                placement = "ortho-plane";
            }

            var wallDistM = t.ComputeFaceWallDistanceMetres(parent);
            var facing = -parent.GlobalTransform.Basis.Z;
            LogTmap(
                $"Create tmap#{obj.index} tile=({obj.tileX},{obj.tileY}) type={tileType} "
                + $"h={obj.heading} xy=({obj.xpos},{obj.ypos}) z={obj.zpos} owner={obj.owner} "
                + $"placement={placement} offset={offsetM:F4}m wallDist={wallDistM:F4}m "
                + $"pos=({parent.GlobalPosition.X:F2},{parent.GlobalPosition.Y:F2},{parent.GlobalPosition.Z:F2}) "
                + $"facing=({facing.X:F2},{facing.Y:F2},{facing.Z:F2}) wScale={tileMapRender.WorldScaleFactor:F2}");

            if (uwsettings.instance.vr_debug)
            {
                t.AttachDebugOverlay(parent, offsetM, wallDistM);
            }

            return t;
        }

        /// <summary>
        /// Place face samples on the diagonal wall plane + standoff along the inward normal.
        /// Same contract as <see cref="PlaceWallMountedDepth"/> for orthogonal walls.
        /// </summary>
        static void PlaceOnDiagonalWallPlane(Node3D parent, uwObject obj, Vector3[] faceSampleLocals, short tileType, float standoffMetres)
        {
            parent.Position = obj.GetCoordinate();
            const float s = 0.707106781f;
            var inward = new Vector3(
                tileType is UWTileMap.TILE_DIAG_SE or UWTileMap.TILE_DIAG_NE ? -s : s,
                0f,
                tileType is UWTileMap.TILE_DIAG_SE or UWTileMap.TILE_DIAG_SW ? -s : s).Normalized();

            // Diagonal runs through tile corners; plane passes through tile center in XZ at object Y.
            var half = PanelHalfWidth;
            var full = PanelHeight;
            var planePoint = new Vector3(-(obj.tileX * full + half), parent.Position.Y, obj.tileY * full + half);
            var basis = parent.Transform.Basis;

            var minDepth = float.MaxValue;
            foreach (var local in faceSampleLocals)
            {
                var depth = (parent.Position + basis * local - planePoint).Dot(inward);
                if (depth < minDepth)
                {
                    minDepth = depth;
                }
            }

            parent.Position += inward * (standoffMetres - minDepth);
        }

        static void LogTmap(string message) => VrDiagLog.Print($"[TMAP] {message}");

        public static bool LookAt(uwObject obj)
        {
            int textureindex = UWTileMap.current_tilemap.texture_map[obj.owner];
            uimanager.AddToMessageScroll(GameStrings.TextureDescription(textureindex));
            if ((textureindex == 142) && ((_RES != GAME_UW2)))
            {
                //This is a window into the abyss.
                uimanager.DisplayCutsImage(cutsfile: "cs400.n01", imageNo: playerdat.dungeon_level - 1, targetControl: uimanager.CutsSmall);
            }
            return true; //prevents the default you cannot use message
        }

        /// <summary>Face samples in local space — Z=0 is the textured plane (ground truth).</summary>
        Vector3[] FaceSampleLocals()
        {
            return new[]
            {
                new Vector3(-PanelHalfWidth, 0f, 0f),
                new Vector3(PanelHalfWidth, 0f, 0f),
                new Vector3(PanelHalfWidth, PanelHeight, 0f),
                new Vector3(-PanelHalfWidth, PanelHeight, 0f),
            };
        }

        public override Vector3[] ModelVertices() => FaceSampleLocals();

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
            v[0] = new Vector2(0, 1);
            v[1] = new Vector2(1, 1);
            v[2] = new Vector2(1, 0);
            v[3] = new Vector2(0, 0);
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

        void AttachDebugOverlay(Node3D parent, float offsetM, float wallDistM)
        {
            var edge = EdgeLabel(uwobject);
            var label = new Label3D
            {
                Name = "TmapDebug",
                Text = $"tmap #{uwobject.index}\n" +
                       $"tile {uwobject.tileX},{uwobject.tileY} {edge}\n" +
                       $"h{uwobject.heading} xy({uwobject.xpos},{uwobject.ypos}) z{uwobject.zpos}\n" +
                       $"ci=0x{uwobject.classindex:X} owner={uwobject.owner}\n" +
                       $"offset={offsetM:F4}m\n" +
                       $"wScale={tileMapRender.WorldScaleFactor:F2}\n" +
                       $"wallDist={wallDistM:F4}m",
                FontSize = 48,
                PixelSize = 0.0012f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(1f, 0.95f, 0.2f),
                OutlineSize = 4,
                OutlineModulate = Colors.Black,
                Position = new Vector3(0f, PanelHeight + 0.08f, offsetM + 0.02f),
                Layers = main.LayerGeo | main.LayerXFER,
            };
            parent.AddChild(label);
            LogTmap($"debug label tmap#{uwobject.index} wallDist={wallDistM:F4}m offset={offsetM:F4}m");
        }

        static string EdgeLabel(uwObject obj)
        {
            if (obj.xpos == 0) return "west";
            if (obj.xpos == 7) return "east";
            if (obj.ypos == 0) return "north";
            if (obj.ypos == 7) return "south";
            return "interior";
        }

        /// <summary>Signed metres from wall plane into the room (face center). Negative = behind wall.</summary>
        float ComputeFaceWallDistanceMetres(Node3D parent)
        {
            if (!TryGetWallMountFrame(uwobject, out var roomNormal, out var wallPoint, parent.GlobalPosition))
            {
                return float.NaN;
            }

            var faceCenterLocal = new Vector3(0f, PanelHeight * 0.5f, 0f);
            var faceWorld = parent.GlobalTransform * faceCenterLocal;
            return (faceWorld - wallPoint).Dot(roomNormal);
        }
    }
}
