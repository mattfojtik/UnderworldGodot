using Godot;

namespace Underworld
{
	/// <summary>Primary-hand pullback / thrust gestures for native VR melee and ranged charging.</summary>
	public static class VrCombatMotion
	{
		const float PullBackDetect = 0.05f;
		const float PullBackCharge = 0.075f;
		const float ReleaseForwardThreshold = 0.08f;
		// Tuned from vr_combat_motion.log calibration passes.
		const float WindUpMinMetric = 0.04f;
		const float BashNegativeUpMax = -0.23f;
		const float BashMinBack = 0.16f;
		const float StabPocketMaxBack = 0.14f;
		const float StabPocketMaxSide = 0.12f;
		const float StabPocketMinUp = -0.20f;
		const float SlashMinSide = 0.08f;
		const float SlashLateralSide = 0.075f;
		const float SlashMinBack = 0.10f;
		const float DegenerateSlashDepth = 0.20f;
		const float SlashThrustSideMin = 0.10f;
		const float SlashThrustSideMinShallow = 0.035f;
		const float SlashThrustSideOverForward = 0.30f;
		const float SlashThrustMaxBack = 0.14f;

		enum MotionState
		{
			Idle,
			PullingBack,
			Charging,
		}

		static MotionState _state = MotionState.Idle;
		static bool _attackHeldDown;
		static Vector3 _strokeStartLocal;
		static Vector3 _peakPullbackLocal;
		static float _peakPullbackDepth;

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
			_peakPullbackDepth = 0f;
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
					if (local.Z < -PullBackDetect)
					{
						_state = MotionState.PullingBack;
						_strokeStartLocal = local;
						_peakPullbackLocal = local;
						_peakPullbackDepth = -local.Z;
						LogMotionSample("pull_start", world, local, -1);
					}
					break;

				case MotionState.PullingBack:
					if (-local.Z > _peakPullbackDepth)
					{
						_peakPullbackDepth = -local.Z;
						_peakPullbackLocal = local;
					}

					if (_peakPullbackDepth >= PullBackCharge)
					{
						_state = MotionState.Charging;
						_attackHeldDown = true;
						combat.WeaponSwingTypePlayer = ClassifyWindUp(_strokeStartLocal, _peakPullbackLocal, _peakPullbackDepth);
						LogMotionSample("charge_start", world, local, combat.WeaponSwingTypePlayer);
					}
					else if (local.Z > -PullBackDetect * 0.35f)
					{
						LogMotionSample("pull_cancel", world, local, -1);
						_state = MotionState.Idle;
					}
					break;

				case MotionState.Charging:
					if (-local.Z > _peakPullbackDepth)
					{
						_peakPullbackDepth = -local.Z;
						_peakPullbackLocal = local;
						combat.WeaponSwingTypePlayer = ClassifyWindUp(_strokeStartLocal, _peakPullbackLocal, _peakPullbackDepth);
					}

					if (local.Z > _peakPullbackLocal.Z + ReleaseForwardThreshold)
					{
						var thrust = local - _peakPullbackLocal;
						combat.WeaponSwingTypePlayer = ClassifyReleaseStroke(
							_strokeStartLocal,
							_peakPullbackLocal,
							thrust,
							_peakPullbackDepth);
						LogMotionSample("release", world, local, combat.WeaponSwingTypePlayer, thrust);
						_attackHeldDown = false;
						_state = MotionState.Idle;
						_peakPullbackDepth = 0f;
					}
					break;
			}

			VrCombatMotionLog.Flush();
		}

		static void LogMotionSample(string eventName, Vector3 world, Vector3 local, int classifierSwing, Vector3 thrust = default)
		{
			VrCombatMotionLog.LogSample(
				eventName: eventName,
				motionState: _state.ToString(),
				world: world,
				local: local,
				strokeStart: _strokeStartLocal,
				peak: _peakPullbackLocal,
				thrust: thrust,
				peakDepth: _peakPullbackDepth,
				classifierSwing: classifierSwing);
		}

		static int ClassifyWindUp(Vector3 startLocal, Vector3 peakLocal, float peakDepth)
		{
			return ClassifySwing(startLocal, peakLocal, peakDepth);
		}

		static int ClassifyReleaseStroke(
			Vector3 startLocal,
			Vector3 peakLocal,
			Vector3 thrust,
			float peakDepth)
		{
			var endLocal = peakLocal + thrust;
			var windUp = peakLocal - startLocal;
			var back = -windUp.Z;
			var up = windUp.Y;
			var slashThrustSide = Mathf.Abs(GetSlashSideComponent(thrust.X, playerdat.isLefty));

			if (IsDegenerateWindUp(back, up))
			{
				if (slashThrustSide >= SlashThrustSideMin
					&& slashThrustSide > Mathf.Abs(thrust.Z) * SlashThrustSideOverForward)
				{
					return 0;
				}

				if (peakDepth >= DegenerateSlashDepth)
				{
					return 0;
				}

				return 2;
			}

			if (slashThrustSide >= SlashThrustSideMinShallow
				&& slashThrustSide > Mathf.Abs(thrust.Z) * SlashThrustSideOverForward
				&& back < SlashThrustMaxBack)
			{
				return 0;
			}

			return ClassifySwing(startLocal, peakLocal, peakDepth, endLocal);
		}

		static int ClassifySwing(Vector3 startLocal, Vector3 peakLocal, float peakDepth, Vector3? endLocal = null)
		{
			var windUp = peakLocal - startLocal;
			if (IsDegenerateWindUp(-windUp.Z, windUp.Y) && endLocal.HasValue)
			{
				windUp = endLocal.Value - startLocal;
			}

			var up = windUp.Y;
			var back = -windUp.Z;
			var slashSide = GetSlashSideComponent(windUp.X, playerdat.isLefty);
			var absSide = Mathf.Abs(slashSide);

			if (IsDegenerateWindUp(back, up))
			{
				return peakDepth >= DegenerateSlashDepth ? 0 : 2;
			}

			if (up <= BashNegativeUpMax && back >= BashMinBack)
			{
				return 1;
			}

			if (back < StabPocketMaxBack
				&& absSide < StabPocketMaxSide
				&& up > StabPocketMinUp)
			{
				return 2;
			}

			if (IsLateralSlashWindUp(slashSide)
				&& absSide >= SlashMinSide
				&& back >= SlashMinBack)
			{
				return 0;
			}

			return 2;
		}

		static bool IsDegenerateWindUp(float back, float up)
		{
			return back < WindUpMinMetric && up < WindUpMinMetric;
		}

		static bool IsLateralSlashWindUp(float slashSide)
		{
			return playerdat.isLefty
				? slashSide >= SlashLateralSide
				: slashSide <= -SlashLateralSide;
		}

		static float GetSlashSideComponent(float deltaX, bool lefty)
		{
			return lefty ? deltaX : -deltaX;
		}
	}
}
