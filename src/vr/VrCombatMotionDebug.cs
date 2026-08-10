using Godot;

namespace Underworld
{
	/// <summary>Semi-transparent overlays for VR melee gesture planes (combat mode).</summary>
	static class VrCombatMotionDebug
	{
		const float PlaneAlpha = 0.25f;
		const float PlaneHalfExtent = 0.05f;
		const float PlaneCenterYOffset = 0.12f;
		const float PlaneCenterZOffset = 0.22f;
		const float HandMarkerRadius = 0.015f;

		static Node3D _root;
		static MeshInstance3D _centerlinePlane;
		static MeshInstance3D _neckPlane;
		static MeshInstance3D _stabPlane;
		static MeshInstance3D _weaponHandMarker;

		public static void Update(bool visible, Vector3 weaponHandLocal)
		{
			if (!visible)
			{
				if (_root != null && GodotObject.IsInstanceValid(_root))
				{
					_root.Visible = false;
				}

				return;
			}

			if (!EnsureCreated())
			{
				return;
			}

			_root.Visible = true;
			_root.GlobalTransform = VrController.GetTorsoTransform();
			_weaponHandMarker.Position = weaponHandLocal;
		}

		static bool EnsureCreated()
		{
			if (_root != null && GodotObject.IsInstanceValid(_root))
			{
				return true;
			}

			var underworld = VrController.GetUnderworldNode();
			if (underworld == null)
			{
				return false;
			}

			_root = new Node3D { Name = "VrCombatGesturePlanes" };
			underworld.AddChild(_root);

			var quad = new QuadMesh
			{
				Size = new Vector2(PlaneHalfExtent * 2f, PlaneHalfExtent * 2f),
			};

			_centerlinePlane = CreatePlane(
				"SlashCenterlinePlane",
				quad,
				new Color(1f, 0.25f, 0.25f, PlaneAlpha),
				new Vector3(VrCombatMotion.CenterlineX, PlaneCenterYOffset, PlaneCenterZOffset),
				new Vector3(0f, Mathf.Pi * 0.5f, 0f));

			_neckPlane = CreatePlane(
				"BashNeckPlane",
				quad,
				new Color(1f, 0.9f, 0.2f, PlaneAlpha),
				new Vector3(0f, VrCombatMotion.NeckPlaneY, PlaneCenterZOffset),
				new Vector3(-Mathf.Pi * 0.5f, 0f, 0f));

			_stabPlane = CreatePlane(
				"StabDepthPlane",
				quad,
				new Color(0.25f, 0.75f, 1f, PlaneAlpha),
				new Vector3(0f, PlaneCenterYOffset, VrCombatMotion.StabPlaneZ),
				Vector3.Zero);

			_weaponHandMarker = new MeshInstance3D
			{
				Name = "WeaponHandMarker",
				Mesh = new SphereMesh { Radius = HandMarkerRadius, Height = HandMarkerRadius * 2f },
				Layers = main.LayerGeo | main.LayerXFER,
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = new Color(1f, 1f, 1f, 0.9f),
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
				},
			};

			_root.AddChild(_centerlinePlane);
			_root.AddChild(_neckPlane);
			_root.AddChild(_stabPlane);
			_root.AddChild(_weaponHandMarker);
			GD.Print("[VR combat] Gesture plane overlays created (0.1m, 75% transparent).");
			return true;
		}

		static MeshInstance3D CreatePlane(
			string name,
			QuadMesh mesh,
			Color color,
			Vector3 localPosition,
			Vector3 localRotation)
		{
			return new MeshInstance3D
			{
				Name = name,
				Mesh = mesh,
				Position = localPosition,
				Rotation = localRotation,
				Layers = main.LayerGeo | main.LayerXFER,
				MaterialOverride = new StandardMaterial3D
				{
					AlbedoColor = color,
					Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
					ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
					CullMode = BaseMaterial3D.CullModeEnum.Disabled,
				},
			};
		}
	}
}
