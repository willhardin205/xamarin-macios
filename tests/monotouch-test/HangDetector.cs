using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

using Foundation;
using ObjCRuntime;

namespace Xamarin.Utils {
	public static class HangDetector {

		public static TextWriter AdditionalStream;

		static IntPtr str_format;
		static int pid;
		public static void Start ()
		{
			str_format = NSString.CreateNative ("%s");
			ResetSignal (6); // SIGABRT
			ResetSignal (10); // SIGBUS
			ResetSignal (13); // SIGPIPE
			pid = System.Diagnostics.Process.GetCurrentProcess ().Id;

			if (!double.TryParse (Environment.GetEnvironmentVariable ("THREADDUMP_INTERVAL_SECONDS"), out var interval)) 
				interval = 60;
			var thread = new Thread (TriggerThread) {
				IsBackground = true,
				Name = "HangDetector Trigger Thread",
			};
			thread.Start (TimeSpan.FromSeconds (interval));

			if (!double.TryParse (Environment.GetEnvironmentVariable ("KILL_TIMEOUT_SECONDS"), out var timeout))
				timeout = 600;
			var killer_thread = new Thread (KillerThread) {
				IsBackground = true,
				Name = "HangDetector Killer Thread",
			};
			killer_thread.Start (TimeSpan.FromSeconds (timeout));
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
			Console.WriteLine ($"[HangDetector/stdout] {msg}");
			Console.Error.WriteLine ($"[HangDetector/stderr] {msg}");
			if (Runtime.IsARM64CallingConvention) {
				NSLog_arm64 (str_format, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, $"[HangDetector/NSLog] {msg}");
			} else {
				NSLog (str_format, $"[HangDetector/NSLog] {msg}");
			}
			AdditionalStream?.WriteLine (msg);
		}

		static void TriggerThread (object argument)
		{
			var interval = (TimeSpan) argument;
			LogEverywhere ($"Started trigger thread with interval {interval.TotalMinutes} minutes.");
			try {
				while (true) {
					Thread.Sleep (interval);
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

		static void ResetSignal (int signal)
		{
			int rv;
			sigaction_struct sa;
			sa.handler = IntPtr.Zero; // SIG_DFL
			sa.mask = 0;
			sa.flags = 0;

			rv = sigaction (signal, ref sa, IntPtr.Zero);
			LogEverywhere ($"HangDetector disabled custom handling for signal {signal}.");
		}

		static void KillerThread (object argument)
		{
			var timeout = (TimeSpan) argument;
			LogEverywhere ($"Started killer thread with timeout {timeout.TotalMinutes} minutes.");

			int rv;
			sigaction_struct sa;
			sa.handler = IntPtr.Zero; // SIG_DFL
			sa.mask = 0;
			sa.flags = 0;

			Thread.Sleep (timeout);

			/* SIGABRT usually causes a crash report */
			ResetSignal (6);
			LogEverywhere ($"HangDetector will now kill(SIGABRT) the app after hitting a termination timeout of {timeout.TotalMinutes} minutes.");
			rv = kill (pid, 6);
			LogEverywhere ($"Failed to SIGABRT? error code: {rv}");

			/* Let's try a bit harder, SIGSEGV now */
			ResetSignal (11);
			LogEverywhere ($"HangDetector will now kill(SIGSEGV) the app after hitting a termination timeout of {timeout.TotalMinutes} minutes.");
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

