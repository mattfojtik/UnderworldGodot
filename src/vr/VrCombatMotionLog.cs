using System;
using Godot;

namespace Underworld
{
	/// <summary>CSV motion-capture log for tuning VR melee gesture classification.</summary>
	public static class VrCombatMotionLog
	{
		const string LogUserPath = "user://vr_combat_motion.log";

		static FileAccess _file;
		static bool _sessionOpen;
		static double _sessionStartSec;

		public static bool IsEnabled =>
			VrController.IsActive
			&& !uwsettings.instance.vr_mirror
			&& (uwsettings.instance.vr_combat_motion_log || uwsettings.instance.vr_debug);

		public static string LogFilePath => ProjectSettings.GlobalizePath(LogUserPath);

		public static void EnsureSession()
		{
			if (!IsEnabled || _sessionOpen)
			{
				return;
			}

			var exists = FileAccess.FileExists(LogUserPath);
			if (exists)
			{
				_file = FileAccess.Open(LogUserPath, FileAccess.ModeFlags.ReadWrite);
				_file?.SeekEnd();
			}
			else
			{
				_file = FileAccess.Open(LogUserPath, FileAccess.ModeFlags.Write);
				WriteHeader();
			}

			if (_file == null)
			{
				GD.PushWarning("[VR combat log] Could not open vr_combat_motion.log for writing.");
				return;
			}

			if (exists)
			{
				_file.StoreLine(string.Empty);
			}

			_sessionStartSec = Time.GetTicksMsec() / 1000.0;
			_sessionOpen = true;
			WriteRow(
				eventName: "session_start",
				combatStage: combat.stage.ToString(),
				motionState: "n/a",
				classifierSwing: -1,
				world: Vector3.Zero,
				local: Vector3.Zero,
				strokeStart: Vector3.Zero,
				peak: Vector3.Zero,
				windUp: Vector3.Zero,
				thrust: Vector3.Zero,
				peakDepth: 0f,
				charge: combat.PlayerAttackCharge,
				extra: $"lefty={playerdat.isLefty},path={LogFilePath}");
			_file.Flush();
			GD.Print($"[VR combat log] Writing to {LogFilePath}");
		}

		public static void CloseSession()
		{
			if (!_sessionOpen || _file == null)
			{
				return;
			}

			WriteRow("session_end", combat.stage.ToString(), "n/a", -1,
				Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
				Vector3.Zero, Vector3.Zero, 0f, combat.PlayerAttackCharge, string.Empty);
			_file.Flush();
			_file.Close();
			_file = null;
			_sessionOpen = false;
		}

		public static void LogCombatMode(bool entering)
		{
			if (!IsEnabled)
			{
				return;
			}

			EnsureSession();
			WriteRow(
				entering ? "combat_mode_on" : "combat_mode_off",
				combat.stage.ToString(), "n/a", -1,
				Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero,
				Vector3.Zero, Vector3.Zero, 0f, combat.PlayerAttackCharge, string.Empty);
			Flush();
		}

		public static void LogJumpMarker()
		{
			if (!IsEnabled)
			{
				return;
			}

			EnsureSession();
			var controller = VrController.GetWeaponHandController();
			var world = controller?.GlobalPosition ?? Vector3.Zero;
			var local = controller != null ? VrController.WorldToTorsoLocal(world) : Vector3.Zero;
			WriteRow(
				"jump_marker",
				combat.stage.ToString(), "n/a", -1,
				world, local, Vector3.Zero, Vector3.Zero,
				Vector3.Zero, Vector3.Zero, 0f, combat.PlayerAttackCharge,
				"user_set_separator");
			Flush();
		}

		public static void LogSample(
			string eventName,
			string motionState,
			Vector3 world,
			Vector3 local,
			Vector3 strokeStart,
			Vector3 peak,
			Vector3 thrust,
			float peakDepth,
			int classifierSwing = -1)
		{
			if (!IsEnabled)
			{
				return;
			}

			EnsureSession();
			var windUp = peak - strokeStart;
			WriteRow(
				eventName,
				combat.stage.ToString(),
				motionState,
				classifierSwing,
				world,
				local,
				strokeStart,
				peak,
				windUp,
				thrust,
				peakDepth,
				combat.PlayerAttackCharge,
				string.Empty);
		}

		public static void Flush()
		{
			_file?.Flush();
		}

		static void WriteHeader()
		{
			_file.StoreLine(
				"time_sec,frame,event,combat_stage,motion_state,lefty,classifier_swing,"
				+ "world_x,world_y,world_z,local_x,local_y,local_z,"
				+ "stroke_start_x,stroke_start_y,stroke_start_z,"
				+ "peak_x,peak_y,peak_z,"
				+ "windup_x,windup_y,windup_z,"
				+ "thrust_x,thrust_y,thrust_z,"
				+ "slash_side,up,back,side_over_up,up_over_side,peak_depth,charge,extra");
		}

		static void WriteRow(
			string eventName,
			string combatStage,
			string motionState,
			int classifierSwing,
			Vector3 world,
			Vector3 local,
			Vector3 strokeStart,
			Vector3 peak,
			Vector3 windUp,
			Vector3 thrust,
			float peakDepth,
			int charge,
			string extra)
		{
			if (_file == null)
			{
				return;
			}

			var slashSide = playerdat.isLefty ? windUp.X : -windUp.X;
			var up = windUp.Y;
			var back = -windUp.Z;
			var sideOverUp = up > 0.02f ? slashSide / up : 0f;
			var upOverSide = slashSide > 0.02f ? up / slashSide : 0f;
			var t = Time.GetTicksMsec() / 1000.0 - _sessionStartSec;

			_file.StoreLine(string.Join(",",
				F((float)t),
				Engine.GetProcessFrames().ToString(),
				Csv(eventName),
				Csv(combatStage),
				Csv(motionState),
				playerdat.isLefty ? "1" : "0",
				classifierSwing.ToString(),
				F(world.X), F(world.Y), F(world.Z),
				F(local.X), F(local.Y), F(local.Z),
				F(strokeStart.X), F(strokeStart.Y), F(strokeStart.Z),
				F(peak.X), F(peak.Y), F(peak.Z),
				F(windUp.X), F(windUp.Y), F(windUp.Z),
				F(thrust.X), F(thrust.Y), F(thrust.Z),
				F(slashSide), F(up), F(back), F(sideOverUp), F(upOverSide),
				F(peakDepth),
				charge.ToString(),
				Csv(extra)));
		}

		static string F(float v) => v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);

		static string Csv(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			if (value.Contains(',') || value.Contains('"'))
			{
				return $"\"{value.Replace("\"", "\"\"")}\"";
			}

			return value;
		}
	}
}
