using Godot;

namespace Underworld
{
	/// <summary>Primary-hand pullback / thrust gestures for native VR melee and ranged charging.</summary>
	public static class VrCombatMotion
	{
		const float PullBackThreshold = 0.14f;
		const float ReleaseForwardThreshold = 0.11f;
		const float StabLowY = 0.05f;
		const float BashHighY = 0.32f;
		const float SlashCrossX = 0.14f;
		const float SameSideMinX = 0.06f;

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

			var local = VrController.WorldToTorsoLocal(controller.GlobalPosition);

			switch (_state)
			{
				case MotionState.Idle:
					if (local.Z < -PullBackThreshold)
					{
						_state = MotionState.PullingBack;
						_strokeStartLocal = local;
						_peakPullbackLocal = local;
						_peakPullbackDepth = -local.Z;
					}
					break;

				case MotionState.PullingBack:
					if (-local.Z > _peakPullbackDepth)
					{
						_peakPullbackDepth = -local.Z;
						_peakPullbackLocal = local;
					}

					if (_peakPullbackDepth >= PullBackThreshold)
					{
						_state = MotionState.Charging;
						_attackHeldDown = true;
						combat.WeaponSwingTypePlayer = ClassifySwingAtPeak(_peakPullbackLocal);
					}
					else if (local.Z > -PullBackThreshold * 0.35f)
					{
						_state = MotionState.Idle;
					}
					break;

				case MotionState.Charging:
					if (-local.Z > _peakPullbackDepth)
					{
						_peakPullbackDepth = -local.Z;
						_peakPullbackLocal = local;
						combat.WeaponSwingTypePlayer = ClassifySwingAtPeak(_peakPullbackLocal);
					}

					if (local.Z > _peakPullbackLocal.Z + ReleaseForwardThreshold)
					{
						combat.WeaponSwingTypePlayer = ClassifyReleaseStroke(_peakPullbackLocal, local);
						_attackHeldDown = false;
						_state = MotionState.Idle;
						_peakPullbackDepth = 0f;
					}
					break;
			}
		}

		static int ClassifySwingAtPeak(Vector3 peakLocal)
		{
			return ClassifyReleaseStroke(peakLocal, peakLocal);
		}

		static int ClassifyReleaseStroke(Vector3 startLocal, Vector3 endLocal)
		{
			var lefty = playerdat.isLefty;
			var delta = endLocal - startLocal;

			if (endLocal.Y >= BashHighY)
			{
				return 1; // bash
			}

			if (endLocal.Y <= StabLowY && IsSameSide(endLocal.X, lefty))
			{
				return 2; // stab
			}

			if (Mathf.Abs(delta.X) >= SlashCrossX || CrossesTorso(startLocal.X, endLocal.X))
			{
				return 0; // slash
			}

			if (endLocal.Y > 0.18f)
			{
				return 1;
			}

			if (endLocal.Y <= StabLowY && Mathf.Abs(endLocal.X) >= SameSideMinX)
			{
				return 2;
			}

			return 0;
		}

		static bool IsSameSide(float localX, bool lefty)
		{
			return lefty ? localX <= -SameSideMinX : localX >= SameSideMinX;
		}

		static bool CrossesTorso(float startX, float endX)
		{
			if (Mathf.Sign(startX) == Mathf.Sign(endX))
			{
				return false;
			}

			return Mathf.Abs(startX) >= SameSideMinX && Mathf.Abs(endX) >= SameSideMinX;
		}
	}
}
