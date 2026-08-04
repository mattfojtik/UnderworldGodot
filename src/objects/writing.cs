using Godot;
namespace Underworld
{
    public class writing : model3D
    {
        public static writing CreateInstance(Node3D parent, uwObject obj, string name)
        {
            var b = new writing(obj);
            var modelNode = b.Generate3DModel(parent, name);
            SetModelRotation(parent, b);
            AlignToWall(parent, obj, nudgeFactor: 0.08f);
            if (uwsettings.instance.vr_debug)
            {
                b.AttachDebugOverlay(parent);
            }

            return b;
        }

        public writing(uwObject _uwobject)
        {
            uwobject = _uwobject;
            uwobject.instance = this;
        }


        public static bool LookAt(uwObject obj)
        {
            if (obj.classindex == 6)
            {
                uimanager.NextOutputPrependedString = GameStrings.GetString(0x1170 + obj.flags);//The TYPE of SIGN reads: message
            }
            
            if (obj.is_quant == 1)
            {
                uimanager.AddToMessageScroll(GameStrings.GetString(8, obj.link - 0x200));
            }
            return true;
        }

        public override Vector3[] ModelVertices()
        {
            var v = new Vector3[4];
            v[0] = new Vector3(-0.0625f, 0f, 0.0625f);
            v[1] = new Vector3(0.1875f, 0f, 0.0625f);
            v[2] = new Vector3(0.1875f, 0.25f, 0.0625f);
            v[3] = new Vector3(-0.0625f, 0.25f, 0.0625f);
            return v;
        }

        public override int[] ModelTriangles(int meshNo)
        {
            var tris = new int[6];
            tris[0] = 0;
            tris[1] = 3;
            tris[2] = 2;
            tris[3] = 2;
            tris[4] = 1;
            tris[5] = 0;
            return tris;
        }

        public override Vector2[] ModelUVs(Vector3[] verts)
        {
            var uv = new Vector2[4];
            uv[0] = new Vector2(0, 1);
            uv[1] = new Vector2(1, 1);
            uv[2] = new Vector2(1, 0);
            uv[3] = new Vector2(0, 0);
            return uv;
        }

        public override ShaderMaterial GetMaterial(int textureno, int surface)
        {
            //(20 + (flags & 0x07)           
            return GetTmObj.GetMaterialForObject(20 + (uwobject.flags & 0x07),uwobject);
        }

        void AttachDebugOverlay(Node3D parent)
        {
            var label = new Label3D
            {
                Name = "WritingDebug",
                Text = $"writing #{uwobject.index}\n" +
                       $"tile {uwobject.tileX},{uwobject.tileY}\n" +
                       $"h{uwobject.heading} xy({uwobject.xpos},{uwobject.ypos}) z{uwobject.zpos}\n" +
                       $"ci=0x{uwobject.classindex:X} flags={uwobject.flags}\n" +
                       $"wScale={tileMapRender.WorldScaleFactor:F2}",
                FontSize = 48,
                PixelSize = 0.0012f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                Modulate = new Color(0.4f, 1f, 0.5f),
                OutlineSize = 4,
                OutlineModulate = Colors.Black,
                Position = new Vector3(0f, 0.35f, 0.08f),
                Layers = main.LayerGeo | main.LayerXFER,
            };
            parent.AddChild(label);
        }

    }//end class
}//end namespace