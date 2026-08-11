using System;
using System.IO;
using Godot;

namespace Underworld
{
	/// <summary>Session log for native VR diagnostics (intro laser, setup, runtime state).</summary>
	public static class VrDiagLog
	{
		const string LogUserPath = "user://vr_diag.log";

		static Godot.FileAccess _userFile;
		static StreamWriter _workspaceWriter;
		static bool _sessionOpen;
		static double _sessionStartSec;
		static string _workspaceLogPath;

		public static bool IsEnabled =>
			uwsettings.instance.vr
			&& !uwsettings.instance.vr_mirror
			&& (uwsettings.instance.vr_diag_log
				|| uwsettings.instance.vr_debug
				|| uwsettings.instance.vr_intro_debug);

		public static string UserLogFilePath => ProjectSettings.GlobalizePath(LogUserPath);

		public static string WorkspaceLogFilePath => _workspaceLogPath ?? string.Empty;

		public static void EnsureSession()
		{
			if (!IsEnabled || _sessionOpen)
			{
				return;
			}

			TryOpenUserLog();
			TryOpenWorkspaceLog();

			if (_userFile == null && _workspaceWriter == null)
			{
				GD.PushWarning("[VR diag] Could not open vr_diag.log for writing.");
				return;
			}

			_sessionStartSec = Time.GetTicksMsec() / 1000.0;
			_sessionOpen = true;
			WriteRaw($"========== VR diag session {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
			WriteRaw($"user:// -> {UserLogFilePath}");
			if (!string.IsNullOrEmpty(_workspaceLogPath))
			{
				WriteRaw($"workspace -> {_workspaceLogPath}");
			}

			Flush();
			GD.Print($"[VR diag] Writing to {UserLogFilePath}"
				+ (string.IsNullOrEmpty(_workspaceLogPath) ? string.Empty : $" and {_workspaceLogPath}"));
		}

		public static void CloseSession()
		{
			if (!_sessionOpen)
			{
				return;
			}

			WriteRaw($"========== VR diag session end {DateTime.Now:yyyy-MM-dd HH:mm:ss} ==========");
			Flush();
			_userFile?.Close();
			_userFile = null;
			_workspaceWriter?.Dispose();
			_workspaceWriter = null;
			_sessionOpen = false;
		}

		/// <summary>Mirror to Godot output and append to vr_diag.log.</summary>
		public static void Print(string message)
		{
			GD.Print(message);
			if (!IsEnabled)
			{
				return;
			}

			EnsureSession();
			WriteRaw(FormatLine(message));
			Flush();
		}

		public static void Warn(string message)
		{
			GD.PushWarning(message);
			if (!IsEnabled)
			{
				return;
			}

			EnsureSession();
			WriteRaw(FormatLine($"WARN {message}"));
			Flush();
		}

		public static void Flush()
		{
			_userFile?.Flush();
			_workspaceWriter?.Flush();
		}

		static void TryOpenUserLog()
		{
			if (_userFile != null)
			{
				return;
			}

			if (Godot.FileAccess.FileExists(LogUserPath))
			{
				_userFile = Godot.FileAccess.Open(LogUserPath, Godot.FileAccess.ModeFlags.ReadWrite);
				_userFile?.SeekEnd();
				_userFile?.StoreLine(string.Empty);
			}
			else
			{
				_userFile = Godot.FileAccess.Open(LogUserPath, Godot.FileAccess.ModeFlags.Write);
			}
		}

		static void TryOpenWorkspaceLog()
		{
			if (_workspaceWriter != null)
			{
				return;
			}

			try
			{
				var projectRoot = ProjectSettings.GlobalizePath("res://");
				if (string.IsNullOrEmpty(projectRoot))
				{
					return;
				}

				var logsDir = Path.Combine(projectRoot, "logs");
				Directory.CreateDirectory(logsDir);
				_workspaceLogPath = Path.Combine(logsDir, "vr_diag.log");
				_workspaceWriter = new StreamWriter(_workspaceLogPath, append: true)
				{
					AutoFlush = false,
				};
				_workspaceWriter.WriteLine();
			}
			catch (Exception ex)
			{
				GD.PushWarning($"[VR diag] Workspace log unavailable: {ex.Message}");
				_workspaceLogPath = string.Empty;
			}
		}

		static string FormatLine(string message)
		{
			var t = Time.GetTicksMsec() / 1000.0 - _sessionStartSec;
			return $"[{t,8:F3}s f{Engine.GetProcessFrames(),6}] {message}";
		}

		static void WriteRaw(string line)
		{
			_userFile?.StoreLine(line);
			_workspaceWriter?.WriteLine(line);
		}
	}
}
