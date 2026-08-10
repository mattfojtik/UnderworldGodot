using Godot;

namespace Underworld
{
	/// <summary>Primary-hand pullback / thrust gestures for native VR melee and ranged charging.</summary>
	public static class VrCombatMotion
	{
		// Torso-local frame (VrController.WorldToTorsoLocal): X+ right, Y+ up, Z+ forward.
		// Origin is ~chest height on the avatar. Tunable fixed planes — not guard-relative.

		/// <summary>Left/right divider. Slash when the hand crosses this plane.</summary>
		public const float CenterlineX = 0f;

		/// <summary>Neck height. Bash when Y crosses this before other gestures.</summary>
		public const float NeckPlaneY = 0.28f;

		/// <summary>Depth plane toward the body. Stab when Z stays behind this (lower Z).</summary>
		public const float StabPlaneZ = 0.12f;

		/// <summary>Frames hand must stay behind stab plane before stab can charge (lets slash register first).</summary>
		const int StabChargeDelayFrames = 5;

		const float ReleaseForwardThreshold = 0.10f;

		enum MotionState
		{
			Idle,
			Charging,
		}

		static MotionState _state = MotionState.Idle;
		static bool _attackHeldDown;
		static Vector3 _strokeStartLocal;
		static Vector3 _peakPullbackLocal;
		static int _chargeSwingType;
		static bool _crossedCenterline;
		static bool _reachedAboveNeck;
		static bool _wentBehindStabPlane;
		static int _behindStabPlaneFrames;
		static bool _strokeTrackingActive;
		static bool _hasPrevTrackX;
		static float _prevTrackX;
		static float _strokeMinX;
		static float _strokeMaxX;

		public static bool UseVrCombatInput()
		{
			return VrController.IsActive
				&& !uwsettings.instance.vr_mirror
				&& uimanager.InGame
				&& uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack
				&& playerdat.play_drawn == 1;
		}

		public static bool IsAttackHeldDown => UseVrCombatInput() && _attackHeldDown;

		public static void Reset()
		{
			_state = MotionState.Idle;
			_attackHeldDown = false;
			_chargeSwingType = -1;
			ResetStrokeFlags();
		}

		static void ResetStrokeFlags()
		{
			_crossedCenterline = false;
			_reachedAboveNeck = false;
			_wentBehindStabPlane = false;
			_behindStabPlaneFrames = 0;
			_strokeTrackingActive = false;
			_hasPrevTrackX = false;
			_prevTrackX = 0f;
			_strokeMinX = 0f;
			_strokeMaxX = 0f;
		}

		static bool CrossedPlane(float previous, float current, float plane)
		{
			return (previous - plane) * (current - plane) < 0f;
		}

		static void TrackStrokePlanes(Vector3 local)
		{
			if (!_strokeTrackingActive)
			{
				_strokeTrackingActive = true;
				_strokeMinX = local.X;
				_strokeMaxX = local.X;
			}
			else
			{
				_strokeMinX = Mathf.Min(_strokeMinX, local.X);
				_strokeMaxX = Mathf.Max(_strokeMaxX, local.X);

				if (_hasPrevTrackX && CrossedPlane(_prevTrackX, local.X, CenterlineX))
				{
					_crossedCenterline = true;
				}
			}

			_prevTrackX = local.X;
			_hasPrevTrackX = true;

			if (_strokeMinX < CenterlineX && _strokeMaxX > CenterlineX)
			{
				_crossedCenterline = true;
			}

			if (local.Y >= NeckPlaneY)
			{
				_reachedAboveNeck = true;
			}

			if (local.Z <= StabPlaneZ)
			{
				_behindStabPlaneFrames++;
			}
			else
			{
				_behindStabPlaneFrames = 0;
			}

			_wentBehindStabPlane = _behindStabPlaneFrames >= StabChargeDelayFrames;
		}

		static bool IsSlashGesture()
		{
			return _crossedCenterline;
		}

		static bool TryBeginCharge(out int swingType)
		{
			swingType = -1;

			if (IsSlashGesture())
			{
				swingType = 0;
				return true;
			}

			if (_reachedAboveNeck)
			{
				swingType = 1;
				return true;
			}

			if (_wentBehindStabPlane)
			{
				swingType = 2;
				return true;
			}

			return false;
		}

		static void ApplySlashUpgradeIfNeeded()
		{
			if (!IsSlashGesture() || _chargeSwingType == 0)
			{
				return;
			}

			_chargeSwingType = 0;
			combat.WeaponSwingTypePlayer = 0;
		}

		static string FormatPlaneExtra(Vector3 local)
		{
			return string.Format(
				System.Globalization.CultureInfo.InvariantCulture,
				"local_x={0:0.####},local_y={1:0.####},local_z={2:0.####},"
				+ "cross_lr={3},span_x={4:0.####},above_neck={5},behind_z={6},behind_frames={7},"
				+ "plane_neck_y={8:0.####},plane_stab_z={9:0.####}",
				local.X,
				local.Y,
				local.Z,
				_crossedCenterline ? 1 : 0,
				_strokeMaxX - _strokeMinX,
				_reachedAboveNeck ? 1 : 0,
				_wentBehindStabPlane ? 1 : 0,
				_behindStabPlaneFrames,
				NeckPlaneY,
				StabPlaneZ);
		}

		public static void Tick()
		{
			if (!UseVrCombatInput())
			{
				Reset();
				return;
			}

			if (combat.stage != combat.CombatStages.Ready
				&& combat.stage != combat.CombatStages.Charging)
			{
				_state = MotionState.Idle;
				_attackHeldDown = false;
				return;
			}

			var controller = VrController.GetWeaponHandController();
			if (controller == null)
			{
				return;
			}

			if (combat.isWeapon(playerdat.PrimaryHandObject) == 2
				&& (_state == MotionState.Charging || _attackHeldDown))
			{
				VrController.UpdateViewPortMouseFromHeadAim();
			}

			var world = controller.GlobalPosition;
			var local = VrController.WorldToTorsoLocal(world);
			LogMotionSample("sample", world, local, -1);

			switch (_state)
			{
				case MotionState.Idle:
					TrackStrokePlanes(local);

					if (TryBeginCharge(out var swingType))
					{
						_state = MotionState.Charging;
						_attackHeldDown = true;
						_chargeSwingType = swingType;
						_strokeStartLocal = local;
						_peakPullbackLocal = local;
						combat.WeaponSwingTypePlayer = _chargeSwingType;
						LogMotionSample("charge_start", world, local, combat.WeaponSwingTypePlayer);
					}
					break;

				case MotionState.Charging:
					TrackStrokePlanes(local);
					ApplySlashUpgradeIfNeeded();

					if (local.Z < _peakPullbackLocal.Z)
					{
						_peakPullbackLocal = local;
					}

					if (local.Z > _peakPullbackLocal.Z + ReleaseForwardThreshold)
					{
						var thrust = local - _peakPullbackLocal;
						combat.WeaponSwingTypePlayer = _chargeSwingType;
						LogMotionSample("release", world, local, combat.WeaponSwingTypePlayer, thrust);
						_attackHeldDown = false;
						_state = MotionState.Idle;
						ResetStrokeFlags();
					}
					break;
			}

			VrCombatMotionLog.Flush();
		}

		public static bool ShouldShowGesturePlanes()
		{
			return VrController.IsActive
				&& !uwsettings.instance.vr_mirror
				&& uimanager.InGame
				&& uimanager.InteractionMode == uimanager.InteractionModes.ModeAttack;
		}

		/// <summary>Torso-local weapon-hand position for gesture debug overlays.</summary>
		public static Vector3 GetDebugWeaponHandLocal()
		{
			var controller = VrController.GetWeaponHandController();
			return controller != null
				? VrController.WorldToTorsoLocal(controller.GlobalPosition)
				: Vector3.Zero;
		}

		static void LogMotionSample(string eventName, Vector3 world, Vector3 local, int classifierSwing, Vector3 thrust = default)
		{
			var extra = classifierSwing >= 0 ? FormatPlaneExtra(local) : string.Empty;
			VrCombatMotionLog.LogSample(
				eventName: eventName,
				motionState: _state.ToString(),
				world: world,
				local: local,
				strokeStart: _strokeStartLocal,
				peak: _peakPullbackLocal,
				thrust: thrust,
				peakDepth: 0f,
				classifierSwing: classifierSwing,
				extra: extra);
		}
	}
}
