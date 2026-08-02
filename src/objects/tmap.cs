using System.Diagnostics;
using Godot;

namespace Underworld
{
    public class tmap:model3D
    {
        int texture;
        Node3D tmapnode;
        float tmapOffset;

        float ExtrudeOffset => tmapOffset * tileMapRender.WorldScaleFactor;

        static float WallStandoffUnscaled()
        {
            return tileMapRender.WallFaceStandoffWorld / tileMapRender.WorldScaleFactor;
        }

        public tmap(uwObject _uwobject)
        {
            uwobject = _uwobject;
            tmapOffset = WallStandoffUnscaled();
        }

        Vector3[] FaceSampleLocals()
        {
            var halfW = tileMapRender.HalfTileWidth;
            var tileW = tileMapRender.TileWidth;
            var extrude = ExtrudeOffset;
            return new[]
            {
                new Vector3(-halfW, 0f, extrude),
                new Vector3(halfW, 0f, extrude),
                new Vector3(halfW, tileW, extrude),
                new Vector3(-halfW, tileW, extrude),
            };
        }

        public void ApplyWallPlacement(Node3D parent)
        {
            tmapOffset = WallStandoffUnscaled();
            PlaceWallMountedDepth(parent, uwobject, FaceSampleLocals(), tileMapRender.WallFaceStandoffTexels);
        }

        public static tmap CreateInstance(Node3D parent, uwObject obj, UWTileMap a_tilemap, string name)
        {
            var t = new tmap(obj);

            //check if tmap shares space with a door, this deals with a tmap that is over a door in level 4 of UW1
            if (UWTileMap.ValidTile(obj.tileX, obj.tileY))
            {
                var tile = UWTileMap.current_tilemap.Tiles[obj.tileX, obj.tileY];

                var door = objectsearch.FindMatchInObjectChain(tile.indexObjectList, 5, 0, -1, UWTileMap.current_tilemap.LevelObjects);
                if (door!=null)
                {
                    if ((door.xpos == obj.xpos) && (door.ypos == obj.ypos))
                    {
                        Debug.Print($"Tmap {obj.index} shares space with door {door.index}");
                    }
                }
            }
            
            t.texture = obj.owner; //a_tilemap.texture_map[obj.owner];    
            t.tmapnode = t.Generate3DModel(parent, name);
           
            SetModelRotation(parent,t);
            t.ApplyWallPlacement(parent);
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


        public override Vector3[] ModelVertices()
        {
            var halfW = tileMapRender.HalfTileWidth;
            var tileW = tileMapRender.TileWidth;
            var extrude = ExtrudeOffset;
            Vector3[] v = new Vector3[4];
            v[0] = new Vector3(-halfW, 0f, extrude);
            v[1] = new Vector3(halfW, 0f, extrude);
            v[2] = new Vector3(halfW, tileW, extrude);
            v[3] = new Vector3(-halfW, tileW, extrude);
            return v;
        }

        public override int[] ModelTriangles(int meshNo)
        {
            //face
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
        {//Get the material texture from tmobj   
            if (surface != 6)
            {
                return tileMapRender.mapTexturesWalls.GetMaterial(texture, UWTileMap.current_tilemap.texture_map);
            }
            else
            {
                return base.GetMaterial(0, 6);
            }
        }
    } //end class
}//end namespace
