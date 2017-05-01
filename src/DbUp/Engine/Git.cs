using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;

namespace DbUp.Engine {
	/// <summary>
	/// Git commands to find migrations that need to be executed since the last release
	/// </summary>
	public class Git {
		// Location of the git repository on the local machine
		private string workingDir = "";
		// git.exe should be on the user's path
		private string gitExe = ""; //"git.exe";
		// The branch you want to use
		private string branch = ""; //"master";
		// Name of the remote to use
		private string remote = ""; //"origin";

		public Git(string workingDir, string branch) {
			this.workingDir = workingDir;

			Hashtable config = ConfigurationManager.GetSection("git") as Hashtable;
			try {
				gitExe = (string)config["gitExe"] ?? gitExe;
				remote = (string)config["remote"] ?? remote;
				this.branch = branch ?? (string)config["branch"] ?? this.branch;
			} catch (KeyNotFoundException Ex) {
				Console.WriteLine("Missing git config parameter in app.config");
				throw Ex;
			}
		}

		///<summary>
		/// Runs arbitrary git commands
		/// </summary>
		/// <returns>Git command result as a string</returns>
		public string ExecuteCommand(string command) {
			Process git = new Process();
			ProcessStartInfo gitStartInfo = new ProcessStartInfo();
			gitStartInfo.FileName = gitExe;
			gitStartInfo.Arguments = command;
			gitStartInfo.UseShellExecute = false;
			gitStartInfo.RedirectStandardOutput = true;
			gitStartInfo.RedirectStandardError = true;
			gitStartInfo.WorkingDirectory = workingDir;
			git.StartInfo = gitStartInfo;
			git.Start();
			string gitOutput = git.StandardOutput.ReadToEnd();
			gitOutput += git.StandardError.ReadToEnd();

			git.WaitForExit();

			return gitOutput;
		}

		/// <summary>
		/// Update the local repository with the lastest upstream changes
		/// </summary>
		public void UpdateLocalRepo() {
			PullChanges();
			RemoteUpdate();
			CheckoutBranch();
		}

		/// <summary>
		/// Make sure the local repository is on the correct branch. Assumes master will be used.
		/// </summary>
		private void CheckoutBranch() {
			Console.WriteLine("Checkout");
			// Checkout the desired branch
			Console.WriteLine(ExecuteCommand("checkout " + branch));
			// Update everything to match the remote branch exactly including removing untracked files if there are any
			Console.WriteLine(ExecuteCommand("reset --hard " + remote + "/" + branch));
		}

		/// <summary>
		/// Get the latest updates from the remote
		/// </summary>
		private void RemoteUpdate() {
			Console.WriteLine("Remote Update");
			Console.WriteLine(ExecuteCommand("remote update " + remote));
		}

		/// <summary>
		/// Pull the remote updates into the local repository (only from origin not upstream)
		/// </summary>
		private void PullChanges() {
			Console.WriteLine("Pull");
			Console.WriteLine(ExecuteCommand("pull " + remote));
		}

		/// <summary>
		/// Fetch the hash for the HEAD of the current branch
		/// </summary>
		/// <returns>Git hash as a string</returns>
		public string HeadVersion() {
			return ExecuteCommand("rev-parse HEAD").Trim();
		}

		/// <summary>
		/// Find files in the local directory that need to be migrated to the target database
		/// </summary>
		/// <param name="databaseVersionHash">The most recent git hash stored in the target database to be updated</param>
		/// <param name="repoVersionHash">The hash in the local repo that we want to compare the database hash to (typically HEAD)</param>
		/// <returns>List of files prefixed with relative paths to the local repo</returns>
		public string[] GetScripts(string databaseVersionHash, string repoVersionHash) {
			// Get all files that have been updated since the database was last updated
			// (M)odified, (A)dded, (C)opied, (R)enamed
			// (Renamed doesn't seem to do anything in testing but is here for consistency and claritys since -CM seem to catch renamed files)
			return ExecuteCommand(
				String.Format(
					"diff -O diff-order-migrations-first --name-only --diff-filter=MACR {0}..{1}",
					databaseVersionHash,
					repoVersionHash
				)
			).Split('\n');
		}
	}
}
