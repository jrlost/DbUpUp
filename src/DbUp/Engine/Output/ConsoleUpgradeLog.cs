using System;

namespace DbUp.Engine.Output {
	/// <summary>
	/// A log that writes to the console in a colorful way.
	/// </summary>
	public class ConsoleUpgradeLog : IUpgradeLog {
		/// <summary>
		/// Writes an informational message to the log.
		/// </summary>
		/// <param name="format">The format.</param>
		/// <param name="args">The args.</param>
		public void WriteInformation(string format, params object[] args) {
			Write(ConsoleColor.White, format, args);
		}

		/// <summary>
		/// Writes an error message to the log.
		/// </summary>
		/// <param name="format">The format.</param>
		/// <param name="args">The args.</param>
		public void WriteError(string format, params object[] args) {
			Error(ConsoleColor.Red, format, args);
		}

		/// <summary>
		/// Writes a warning message to the log.
		/// </summary>
		/// <param name="format">The format.</param>
		/// <param name="args">The args.</param>
		public void WriteWarning(string format, params object[] args) {
			Write(ConsoleColor.Yellow, format, args);
		}

		private static void Write(ConsoleColor color, string format, object[] args) {
			Console.ForegroundColor = color;
			if (args.Length > 0) { 
				Console.WriteLine(format, args);
			} else {
				Console.WriteLine(format);
			}
			Console.ResetColor();
		}

		private static void Error(ConsoleColor color, string format, object[] args) {
			Console.ForegroundColor = color;
			if (args.Length > 0) { 
				Console.Error.WriteLine(format, args);
			} else {
				Console.Error.WriteLine(format);
			}
			Console.ResetColor();
		}
	}
}