using DbUp.Builder;
using DbUp.Engine.Output;
using System;
using System.Collections.Generic;
using System.Data.SqlClient;
using System.Linq;
using System.Text;
using Microsoft.SqlServer.Management.Smo;
using Microsoft.SqlServer.Management.Common;

namespace DbUp.Engine {
	/// <summary>
	/// This class orchestrates the database upgrade process.
	/// </summary>
	public class UpgradeEngine {
		private readonly UpgradeConfiguration configuration;
		private readonly string branch;

		/// <summary>
		/// Initializes a new instance of the <see cref="UpgradeEngine"/> class.
		/// </summary>
		/// <param name="configuration">The configuration.</param>
		public UpgradeEngine(UpgradeConfiguration configuration, string branch) {
			this.configuration = configuration;
			this.branch = branch;
		}

		/// <summary>
		/// Determines whether the database is out of date and can be upgraded.
		/// </summary>
		public bool IsUpgradeRequired(string databaseVersionHash, string workingDir, Git aGit) {
			return GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash, aGit).Count() != 0;
		}

		/// <summary>
		/// Return a list of scripts that would be ran if this was not a dry run
		/// </summary>
		/// <param name="databaseVersionHash">The most recent database version</param>
		/// <param name="workingDir">The local clone of the migrations repository</param>
		/// <param name="printAll">Print the contents of each of the scripts that will be run</param>
		public void DryRun(string databaseVersionHash, string headVersion, string workingDir, bool printAll, string connectionString, Git aGit) {
			if (printAll) {
				GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash, aGit).ForEach(i => configuration.Log.WriteInformation("--{0}\r\n{1}\r\n\r\n", i.Name, i.Contents));
			} else {
				GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash, aGit).ForEach(i => configuration.Log.WriteInformation("{0}", i.Name));
			}

			PerformUpgrade(databaseVersionHash, headVersion, workingDir, connectionString, aGit, printAll, true);
		}

		/// <summary>
		/// Tries to connect to the database.
		/// </summary>
		/// <param name="errorMessage">Any error message encountered.</param>
		/// <returns></returns>
		public bool TryConnect(out string errorMessage) {
			try {
				errorMessage = "";
				configuration.ConnectionManager.ExecuteCommandsWithManagedConnection(dbCommandFactory => {
					using (var command = dbCommandFactory()) {
						command.CommandText = "select 1";
						command.ExecuteScalar();
					}
				});
				return true;
			} catch (Exception ex) {
				errorMessage = ex.Message;
				return false;
			}
		}

		/// <summary>
		/// Performs the database upgrade.
		/// </summary>
		public DatabaseUpgradeResult PerformUpgrade(string databaseVersionHash, string headVersion, string workingDir, string connectionString, Git aGit, bool printAll = false, bool dryRun = false) {
			var executed = new List<SqlScript>();
			try {
				using (configuration.ConnectionManager.OperationStarting(configuration.Log, executed)) {
					configuration.Log.WriteInformation("Beginning database upgrade");

					var scriptsToExecute = GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash, aGit);

					if (scriptsToExecute.Count == 0) {
						configuration.Log.WriteInformation("No new scripts need to be executed - completing.");
						return new DatabaseUpgradeResult(executed, true, null);
					}

					configuration.ScriptExecutor.VerifySchema();

					StringBuilder combinedContents = new StringBuilder();
					combinedContents.Append("SET XACT_ABORT ON\r\nGO\r\nBEGIN TRANSACTION EndeavorRelease\r\nGO\r\n");
					foreach (var script in scriptsToExecute) {
						if (script.IsValid()) {
							combinedContents.AppendFormat("\r\n\r\n{0}", script.Contents);
						}
					}

					// If it's a dry run rollback the transaction at the end so nothing is committed to the database
					combinedContents.AppendFormat("\r\n{0} TRANSACTION\r\nGO\r\n", dryRun ? "ROLLBACK" : "COMMIT");
					if (!dryRun && printAll) {
						configuration.Log.WriteInformation("--------------------------------------------------------------------------------");
						configuration.Log.WriteInformation(combinedContents.ToString());
						configuration.Log.WriteInformation("--------------------------------------------------------------------------------");
					}
					SqlScript combinedScript = new SqlScript(headVersion + ".sql", combinedContents.ToString());
					try {
						using (SqlConnection conn = new SqlConnection(connectionString)) {
							Server db = new Server(new ServerConnection(conn));
							db.ConnectionContext.ExecuteNonQuery(combinedContents.ToString());

							executed.Add(combinedScript);
						}
					} catch (Exception ex) {
						// Just throw the error for now until we know whether the rollback will work
						throw ex;
					}
					return new DatabaseUpgradeResult(executed, true, null);
				}
			} catch (Exception ex) {
				configuration.Log.WriteError("Upgrade failed: {0}", ex.InnerException != null ? ex.InnerException.Message : ex.Message);
				return new DatabaseUpgradeResult(executed, false, ex);
			}
		}

		/// <summary>
		/// Retrieve all the SQL scripts that need to be executed
		/// </summary>
		/// <param name="workingDir">The directory containing the scripts to be run on the database</param>
		/// <param name="databaseVersionHash">The version hash the database was last upgraded to</param>
		/// <param name="repoVersionHash">The version hash the database will be upgraded to</param>
		/// <returns>List of SQL scripts including their names and contents</returns>
		private List<SqlScript> GetScriptsToExecuteInsideOperation(string workingDir, string databaseVersionHash, Git aGit, string repoVersionHash = "HEAD") {
			// Git repo must already be cloned into workspace
			try {
				return aGit.GetScripts(databaseVersionHash, repoVersionHash).Where(s => !String.IsNullOrEmpty(s)).Select(s => SqlScript.FromFile(s)).ToList();
			} catch (Exception ex) {
				configuration.Log.WriteError("Git commands failed to run: \r\n{0}", ex.ToString());
				return new List<SqlScript>();
			}
		}

		///<summary>
		/// Creates version record for any new migration scripts without executing them.
		/// Useful for bringing development environments into sync with automated environments
		///</summary>
		///<returns></returns>
		public DatabaseUpgradeResult UpdateDatabaseVersion(DatabaseVersion dbVersion, string headHash) {
			var marked = new List<SqlScript>();
			using (configuration.ConnectionManager.OperationStarting(configuration.Log, marked)) {
				try {
					dbVersion.Version = headHash;
					configuration.Log.WriteInformation("Database updated successfully to version: {0}", headHash);
					return new DatabaseUpgradeResult(marked, true, null);
				} catch (Exception ex) {
					configuration.Log.WriteError("Update failed due to an unexpected exception:\r\n{0}", ex.ToString());
					return new DatabaseUpgradeResult(marked, false, ex);
				}
			}
		}

		/// <summary>
		/// Returns the current logger
		/// </summary>
		public IUpgradeLog Log {
			get {
				return configuration.Log;
			}
		}
	}
}