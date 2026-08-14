using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
namespace Underworld
{

    /// <summary>
    /// Class for rendering 3d Model objects
    /// </summary>
    public class model3D : objectInstance
    {
        protected const int CEILING_HEIGHT = 32;
        //public Material material;
        public static Shader textureshader;
        public static Shader transparentshader;
        static GRLoader tmObj; //3d model textures at 

        public static GRLoader GetTmObj
        {
            get
            {
                LoadTmObj();
                return tmObj;
            }
        }

        static GRLoader tmFlat; //button.

        public static GRLoader GetTmFlat
        {
            get
            {
                LoadTmFlat();
                return tmFlat;
            }
        }
        
        /// <summary>
        /// Sets the tmobj to null so that the next time a texture is requested for a model the tmobj is recreated with fresh images (respecting detail levels)
        /// </summary>
        public static void ClearTmObj()
        {
            tmObj = null;
        }


        /// <summary>
        /// Generates the defined 3d model and adds as a child to the parent node.
        /// </summary>
        /// <param name="parent"></param>
        /// <returns></returns>
        public Node3D Generate3DModel(Node3D parent, string name)
        {
            int[] mats = new int[NoOfMeshes()];
            var a_mesh = new ArrayMesh(); //= Mesh as ArrayMesh;
            var verts = ModelVertices();
            Vector2[] uvs = ModelUVs(verts);
            int MeshCount = NoOfMeshes();

            for (int i = 0; i < MeshCount; i++)
            {
                mats[i] = ModelColour(i); //index into the appropiate palette(default) or material list
            }

            var normals = new List<Vector3>();
            foreach (var vert in verts)
            {
                normals.Add(vert.Normalized());
            }

            for (int i = 0; i < MeshCount; i++)
            {
                AddSurfaceToMesh(
                    instance: this, 
                    verts: verts, 
                    uvs: uvs, 
                    MatsToUse: mats, 
                    FaceCounter: i, 
                    a_mesh: a_mesh, 
                    normals: normals, 
                    indices: ModelTriangles(i));
            }

            return CreateMeshInstance(parent, name, a_mesh);
        }

        public virtual int[] ModelTriangles(int meshNo)
        {
            return new int[] { 0, 0, 0 };
        }

        public virtual Vector3[] ModelVertices()
        {
            return new Vector3[] { Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero };
        }

        public virtual int NoOfMeshes()
        {
            return 1;
        }

        /// <summary>
        /// This is the indices of the texture or colour palette to render with.
        /// </summary>
        /// <param name="meshNo"></param>
        /// <returns></returns>
        public virtual int ModelColour(int meshNo)
        {
            return 127; // this colour will standout.
            //return Color.Color8(0, 0, 0, 0);  //.white;
        }

        public virtual bool isSolidModel()
        {
            return true;
        }

        public virtual Vector2[] ModelUVs(Vector3[] verts)
        {//This probably gives bad mappings
            Vector2[] customUVs = new Vector2[verts.Length];
            for (int i = 0; i < customUVs.Length; i++)
            {
                customUVs[i] = new Vector2(verts[i].X, verts[i].Y);
                customUVs[i] = customUVs[i] * TextureScaling();
            }
            return customUVs;
        }

        public virtual float TextureScaling()
        {
            return 1f;
        }

        protected static Node3D CreateMeshInstance(Node3D parent, string ModelName, ArrayMesh a_mesh, bool EnableCollision = false)
        {
            var final_mesh = new MeshInstance3D();
            parent.AddChild(final_mesh);
            final_mesh.Position = Vector3.Zero; // new Vector3(x * -1.2f, 0.0f, y * 1.2f);
            final_mesh.Name = ModelName;
            final_mesh.Mesh = a_mesh;
            final_mesh.Layers = main.LayerGeo | main.LayerObjectInfo | main.LayerXFER;
            if (EnableCollision)
            {
                final_mesh.CreateTrimeshCollision();
                // final_mesh.CreateConvexCollision();
            }
            return final_mesh;
        }


        /// <summary>
        /// Adds a surface built from the various uv, vertices and materials arrays to a mesh
        /// </summary>
        /// <param name="verts"></param>
        /// <param name="uvs"></param>
        /// <param name="MatsToUse"></param>
        /// <param name="FaceCounter"></param>
        /// <param name="a_mesh"></param>
        /// <param name="normals"></param>
        /// <param name="indices"></param>
        protected static void AddSurfaceToMesh(model3D instance, Vector3[] verts, Vector2[] uvs, int[] MatsToUse, int FaceCounter, ArrayMesh a_mesh, List<Vector3> normals, int[] indices, int faceCounterAdj = 0)
        {
            var surfaceArray = new Godot.Collections.Array();
            surfaceArray.Resize((int)Mesh.ArrayType.Max);
            surfaceArray[(int)Mesh.ArrayType.Vertex] = verts; //.ToArray();
            surfaceArray[(int)Mesh.ArrayType.TexUV] = uvs; //.ToArray();
            surfaceArray[(int)Mesh.ArrayType.Normal] = normals.ToArray();
            surfaceArray[(int)Mesh.ArrayType.Index] = indices.ToArray();
            //Add the new surface to the mesh
            a_mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, surfaceArray);
            a_mesh.SurfaceSetMaterial(
                surfIdx: FaceCounter + faceCounterAdj, 
                material: instance.GetMaterial(
                    textureno: MatsToUse[FaceCounter], 
                    surface: FaceCounter));

        }


        /// <summary>
        /// Get the material to display this 3d object with. Defaults to a flat colour from the palette.
        /// Override this to use textures from tmobj instead and replace the texture_albedo as needed.
        /// </summary>
        /// <param name="textureno"></param>
        /// <returns></returns>
        public virtual ShaderMaterial GetMaterial(int textureno, int surface)
        {
            if (textureshader == null)
            {
                textureshader = (Shader)ResourceLoader.Load("res://resources/shaders/uwshader_allred.gdshader");
            }
            var newmaterial = new ShaderMaterial();
            newmaterial.Shader = textureshader;
            newmaterial.SetShaderParameter("texture_albedo", (Texture)Palette.IndexToImage((byte)textureno));
            newmaterial.SetShaderParameter("albedo", new Color(1, 1, 1, 1));
            newmaterial.SetShaderParameter("uv1_scale", new Vector3(1, 1, 1));
            newmaterial.SetShaderParameter("uv2_scale", new Vector3(1, 1, 1));
            newmaterial.SetShaderParameter("UseAlpha", false);
            newmaterial.SetShaderParameter("objectindex_lowerbytes", uwobject.index & 0xFF);
            newmaterial.SetShaderParameter("objectindex_upperbytes", (uwobject.index>>8) & 0xFF);
            return newmaterial;
        }

        public ShaderMaterial GetMaterial_alphacolour(int textureno, int surface)
        {
            if (transparentshader == null)
            {
                transparentshader = (Shader)ResourceLoader.Load("res://resources/shaders/uwshader_alpha.gdshader");
            }
            var newmaterial = new ShaderMaterial();
            newmaterial.Shader = transparentshader;
            newmaterial.SetShaderParameter("texture_albedo", (Texture)Palette.IndexToImage((byte)textureno));
            newmaterial.SetShaderParameter("albedo", new Color(1, 1, 1, 1));
            newmaterial.SetShaderParameter("uv1_scale", new Vector3(1, 1, 1));
            newmaterial.SetShaderParameter("uv2_scale", new Vector3(1, 1, 1));
            newmaterial.SetShaderParameter("UseAlpha", false);
            return newmaterial;
        }

        public static void DisplayModelPoints(model3D m, Node3D n, int maxpoints = 20)
        {
            //render the points for debugging
            var vs = m.ModelVertices();
            int vindex = 0;
            Label3D obj_orign = new();
            obj_orign.Text = $".";
            obj_orign.Position = Vector3.Zero;
            obj_orign.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
            n.AddChild(obj_orign);

            foreach (var v in vs)
            {
                if (vindex < maxpoints)
                {
                    Label3D obj_lbl = new();
                    obj_lbl.Text = $".{vindex}";
                    obj_lbl.FontSize = 8;
                    obj_lbl.Position = new Vector3(v.X, v.Y, v.Z);
                    obj_lbl.Billboard = BaseMaterial3D.BillboardModeEnum.Enabled;
                    n.AddChild(obj_lbl);
                }
                vindex++;
            }
        }



        /// <summary>
        /// Rotates the model along it's axis
        /// </summary>
        /// <param name="parent"></param>
        /// <param name="tileX"></param>
        /// <param name="tileY"></param>
        /// <param name="n"></param>
        public static void SetModelRotation(Node3D parent, model3D n)
        {
            switch (n.uwobject.heading * 45)
            {
                case tileMapRender.heading0:
                    parent.Rotate(Vector3.Up, (float)Math.PI);
                    break;
                case tileMapRender.heading1:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 3f / 4f); break;
                case tileMapRender.heading2: //90 works
                    parent.Rotate(Vector3.Up, (float)Math.PI / 2f);
                    break;
                case tileMapRender.heading3:
                    parent.Rotate(Vector3.Up, (float)Math.PI / 4f);
                    break;
                case tileMapRender.heading4:
                    //default. no rotation
                    break;
                case tileMapRender.heading5:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 7f / 4f);
                    break;
                case tileMapRender.Heading6:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 1.5f);
                    break;
                case tileMapRender.Heading7:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 5f / 4f);
                    break;
                default:
                    System.Diagnostics.Debug.Print($"Unhandled model heading. {n.uwobject.item_id} h:{n.uwobject.heading}");
                    break;
            }
        }

        public static void SetObjectRotation(Node3D parent, uwObject obj)
        {
            switch (obj.heading * 45)
            {
                case tileMapRender.heading0:
                    parent.Rotate(Vector3.Up, (float)Math.PI);
                    break;
                case tileMapRender.heading1:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 3f / 4f); break;
                case tileMapRender.heading2: //90 works
                    parent.Rotate(Vector3.Up, (float)Math.PI / 2f);
                    break;
                case tileMapRender.heading3:
                    parent.Rotate(Vector3.Up, (float)Math.PI / 4f);
                    break;
                case tileMapRender.heading4:
                    //default. no rotation
                    break;
                case tileMapRender.heading5:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 7f / 4f);
                    break;
                case tileMapRender.Heading6:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 1.5f);
                    break;
                case tileMapRender.Heading7:
                    parent.Rotate(Vector3.Up, (float)Math.PI * 5f / 4f);
                    break;
                default:
                    System.Diagnostics.Debug.Print($"Unhandled model heading. {obj.item_id} h:{obj.heading}");
                    break;
            }
        }

        /// <summary>
        /// Checks if tmObj is loaded and if not load. Call before returning a new material shader using tmObjl
        /// </summary>
        static void LoadTmObj()
        {
            if (tmObj == null)
            {
                tmObj = new GRLoader(GRLoader.TMOBJ_GR, GRLoader.GRShaderMode.TextureShader);
                tmObj.UseRedChannel = true;
            }
        }

        /// <summary>
        /// Checks if tmFlat is loaded and if not load. Call before returning a new material shader using tmObjl
        /// </summary>
        static void LoadTmFlat()
        {
            if (tmFlat == null)
            {
                tmFlat = new GRLoader(GRLoader.TMFLAT_GR, GRLoader.GRShaderMode.TextureShader);
                tmFlat.UseRedChannel = true;
            }
        }


        //Center the model in the tile it is in along it's heading
        public static void centreAlongAxis(Node3D ModelParentNode, model3D modelObj)
        {
            AlignToWall(ModelParentNode, modelObj.uwobject);
        }

        /// <summary>Snap wall-mounted objects to the tile grid and nudge flush to the wall.</summary>
        public static void AlignToWall(Node3D parent, uwObject obj, float nudgeFactor = 0.1f)
        {
            var onWest = obj.xpos == 0;
            var onEast = obj.xpos == 7;
            var onNorth = obj.ypos == 0;
            var onSouth = obj.ypos == 7;
            var onEdge = onWest || onEast || onNorth || onSouth;

            if (!onEdge)
            {
                SnapToTileCenterAlongHeading(parent, obj);
                return;
            }

            // Edge-mounted: keep GetCoordinate sub-tile position; nudge toward wall surface.
            var nudge = nudgeFactor * tileMapRender.WorldScaleFactor;
            if (onWest)
            {
                parent.Position += new Vector3(+nudge, 0f, 0f);
            }
            if (onEast)
            {
                parent.Position += new Vector3(-nudge, 0f, 0f);
            }
            if (onNorth)
            {
                parent.Position += new Vector3(0f, 0f, -nudge);
            }
            if (onSouth)
            {
                parent.Position += new Vector3(0f, 0f, +nudge);
            }
        }

        /// <summary>
        /// Tilemap wall plane and unit normal pointing into the open tile (matches RenderTile verts).
        /// West/east: X = -tileX*W / -(tileX+1)*W. North/south: Z = tileY*W / (tileY+1)*W.
        /// </summary>
        public static bool TryGetWallMountFrame(uwObject obj, out Vector3 roomNormal, out Vector3 wallPoint, Vector3 referencePosition)
        {
            roomNormal = Vector3.Zero;
            wallPoint = referencePosition;
            var tileWidth = tileMapRender.TileWidth;
            var tileX = obj.tileX;
            var tileY = obj.tileY;

            if (obj.xpos == 0)
            {
                // West face at X=0 local → world -tileX*W; interior is -X.
                roomNormal = Vector3.Left;
                wallPoint = new Vector3(-tileX * tileWidth, referencePosition.Y, referencePosition.Z);
                return true;
            }

            if (obj.xpos == 7)
            {
                // East face at X=-1.2 local → world -(tileX+1)*W; interior is +X.
                roomNormal = Vector3.Right;
                wallPoint = new Vector3(-(tileX + 1) * tileWidth, referencePosition.Y, referencePosition.Z);
                return true;
            }

            if (obj.ypos == 0)
            {
                // North face at Z=0 local → world tileY*W; interior is +Z (Vector3.Back).
                roomNormal = Vector3.Back;
                wallPoint = new Vector3(referencePosition.X, referencePosition.Y, tileY * tileWidth);
                return true;
            }

            if (obj.ypos == 7)
            {
                // South face at Z=1.2 local → world (tileY+1)*W; interior is -Z (Vector3.Forward).
                roomNormal = Vector3.Forward;
                wallPoint = new Vector3(referencePosition.X, referencePosition.Y, (tileY + 1) * tileWidth);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Snap so every face sample sits <paramref name="standoffMetres"/> into the room from the
        /// tilemap wall plane. Keeps GetCoordinate() tangential/height; only slides along roomNormal.
        /// </summary>
        public static void PlaceWallMountedDepth(Node3D parent, uwObject obj, Vector3[] faceSampleLocals, float standoffMetres)
        {
            var coord = obj.GetCoordinate();
            if (!TryGetWallMountFrame(obj, out var roomNormal, out var wallPoint, coord))
            {
                return;
            }

            parent.Position = coord;
            var basis = parent.Transform.Basis;

            var minDepth = float.MaxValue;
            foreach (var local in faceSampleLocals)
            {
                var depth = (parent.Position + basis * local - wallPoint).Dot(roomNormal);
                if (depth < minDepth)
                {
                    minDepth = depth;
                }
            }

            parent.Position += roomNormal * (standoffMetres - minDepth);
        }

        static void SnapToTileCenterAlongHeading(Node3D parent, uwObject obj)
        {
            int x = obj.tileX;
            int y = obj.tileY;
            switch (obj.heading * 45)
            {
                case tileMapRender.heading0:
                case tileMapRender.heading4:
                    parent.Position = new Vector3(-(x * tileMapRender.TileWidth + tileMapRender.HalfTileWidth), parent.Position.Y, parent.Position.Z);
                    break;
                case tileMapRender.heading2:
                case tileMapRender.Heading6:
                    parent.Position = new Vector3(parent.Position.X, parent.Position.Y, y * tileMapRender.TileWidth + tileMapRender.HalfTileWidth);
                    break;
            }
        }

        //Center the model in the tile in it's tile
        public static void centreInTile(Node3D modelParentNode, model3D modelObj)
        {
            int x = modelObj.uwobject.tileX;
            int y = modelObj.uwobject.tileY;

            modelParentNode.Position = new Vector3(-(x * tileMapRender.TileWidth + tileMapRender.HalfTileWidth), modelParentNode.Position.Y, y * tileMapRender.TileWidth + tileMapRender.HalfTileWidth);
        }

    }//end class
}//end namespace