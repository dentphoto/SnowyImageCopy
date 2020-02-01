﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SnowyImageCopy.Models
{
	/// <summary>
	/// Command line options
	/// </summary>
	internal static class CommandLine
	{
		#region Win32

		[DllImport("Kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool AttachConsole(uint dwProcessId);

		private const uint ATTACH_PARENT_PROCESS = uint.MaxValue;

		[DllImport("Kernel32.dll", SetLastError = true)]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool FreeConsole();

		#endregion

		/// <summary>
		/// Whether to show command line usage
		/// </summary>
		public static bool ShowsUsage => CheckArgs("/?", "-?");

		/// <summary>
		/// Whether to start auto check at startup of this application
		/// </summary>
		public static bool StartsAutoCheck => CheckArgs(StartsAutoCheckOptions);
		private static string[] StartsAutoCheckOptions => new[] { "/autocheck", "-autocheck", "/a", "-a" };

		/// <summary>
		/// Whether to make window state minimized at startup of this application
		/// </summary>
		public static bool MakesWindowStateMinimized => CheckArgs(MakesWindowStateMinimizedOptions);
		private static string[] MakesWindowStateMinimizedOptions => new[] { "/minimized", "-minimized", "/m", "-m" };

		/// <summary>
		/// Whether to record download log
		/// </summary>
		public static bool RecordsDownloadLog => CheckArgs(RecordsDownloadLogOptions);
		private static string[] RecordsDownloadLogOptions => new[] { "/recordlog", "-recordlog", "/r", "-r" };

		public static void ShowUsage()
		{
			if (!AttachConsole(ATTACH_PARENT_PROCESS))
				return;

			Console.WriteLine(
				"\n" +
				"Usage: SnowyImageCopy [{0}] [{1}] [{2}]\n" +
				"{0}: Start auto check at startup\n" +
				"{1}: Make window state minimized at startup\n" +
				"{2}: Record download log",
				StartsAutoCheckOptions[0],
				MakesWindowStateMinimizedOptions[0],
				RecordsDownloadLogOptions[0]);

			FreeConsole();
		}

		#region Helper

		private static string[] _args;

		private static bool CheckArgs(params string[] options)
		{
			_args ??= Environment.GetCommandLineArgs()
				.Skip(1) // The first arg is always executable file path.
				.Select(x => x.ToLower())
				.ToArray();

			return (options != null) && _args.Intersect(options).Any();
		}

		#endregion
	}
}