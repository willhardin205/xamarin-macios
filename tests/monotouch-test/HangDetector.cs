using System;
using System.Runtime.InteropServices;
using System.Threading;

using Foundation;
using ObjCRuntime;

namespace Xamarin.Utils {
	public static class HangDetector {

		static IntPtr str_format;
		static int pid;
		public static void Start ()
		{
			str_format = NSString.CreateNative ("%s");
			pid = System.Diagnostics.Process.GetCurrentProcess ().Id;
			var thread = new Thread (TriggerThread) {
				IsBackground = true,
				Name = "HangDetector Trigger Thread",
			};
			thread.Start ();

			var env = Environment.GetEnvironmentVariable ("KILL_TIMEOUT_SECONDS");
			if (double.TryParse (env, out var seconds)) {
				var killer_thread = new Thread (KillerThread) {
					IsBackground = true,
					Name = "HangDetector Killer Thread",
				};
				killer_thread.Start (TimeSpan.FromSeconds (seconds));
			}
		}

		[DllImport (Constants.FoundationLibrary)]
		extern static void NSLog (IntPtr format, [MarshalAs (UnmanagedType.LPStr)] string s);

		[DllImport (Constants.FoundationLibrary, EntryPoint = "NSLog")]
		extern static void NSLog_arm64 (IntPtr format, IntPtr p2, IntPtr p3, IntPtr p4, IntPtr p5, IntPtr p6, IntPtr p7, IntPtr p8, [MarshalAs (UnmanagedType.LPStr)] string s);

		[DllImport ("libc")]
		extern static int kill (int pid, int signal);

		[DllImport ("libc")]
		extern static int strlen (IntPtr str);

		static void LogEverywhere (string msg)
		{
			Console.WriteLine ($"[stdout] {msg}");
			Console.Error.WriteLine ($"[stderr] {msg}");
			if (Runtime.IsARM64CallingConvention) {
				NSLog_arm64 (str_format, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, $"[NSLog] {msg}");
			} else {
				NSLog (str_format, $"[NSLog] {msg}");
			}
			MonoTouchFixtures.AppDelegate.Runner.Writer.WriteLine (msg);
		}

		static void TriggerThread ()
		{
			try {
				while (true) {
					Thread.Sleep (TimeSpan.FromMinutes (1));
					LogEverywhere ("Triggering full thread dump...");
					kill (pid, 3 /* SIGQUIT */);
				}
			} catch (Exception e) {
				var msg = $"HangDetector's trigger thread died because of an exception: {e}";
				LogEverywhere (msg);
			} finally {
				NSString.ReleaseNative (str_format);
			}
		}

		struct sigaction_struct {
			public IntPtr handler;
			public int mask;
			public int flags;
		}

		[DllImport ("libc")]
		extern static int sigaction (int signal, ref sigaction_struct action, IntPtr zero);

		static void KillerThread (object argument)
		{
			int rv;
			sigaction_struct sa;
			sa.handler = IntPtr.Zero; // SIG_DFL
			sa.mask = 0;
			sa.flags = 0;

			var timeout = (TimeSpan) argument;
			Thread.Sleep (timeout);

			/* SIGABRT usually causes a crash report */
			rv = sigaction (6, ref sa, IntPtr.Zero);
			LogEverywhere ($"HangDetector will now kill(SIGABRT) the app after hitting a termination timeout of {timeout.TotalMinutes} minutes. sigaction rv: {rv}");
			rv = kill (pid, 6);
			LogEverywhere ($"Failed to SIGABRT? error code: {rv}");

			/* Let's try a bit harder, SIGSEGV now */
			rv = sigaction (11, ref sa, IntPtr.Zero);
			LogEverywhere ($"HangDetector will now kill(SIGSEGV) the app after hitting a termination timeout of {timeout.TotalMinutes} minutes. sigaction rv: {rv}");
			rv = kill (pid, 11);
			LogEverywhere ($"Failed to SIGSEGV?!? error code: {rv}");

			/* Still no luck, trigger a real SIGSEGV */
			strlen (IntPtr.Zero);
			LogEverywhere ($"Failed to strlen(NULL)... error code: {rv}");
			rv = kill (pid, 9 /* SIGKILL... */);
			LogEverywhere ($"Failed to kill. error code: {rv}. Giving up.");
		}
	}
}

