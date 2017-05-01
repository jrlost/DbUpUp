using DbUp.Engine;
using DbUp.Engine.Output;
using NDesk.Options;
using System;
using System.Data.SqlClient;
using System.IO;
using System.Text;

namespace DbUp.Console {
	class Program {
		static void Main(string[] args) {
			var server = "";
			var database = "";
			var directory = "";
			var username = "";
			var password = "";
			var connectionString = "";
			var workingDir = "";
			var dryrun = false;
			var printAll = false;
			var headVersion = "";
			var promptUser = false;
			var branch = "";

			bool show_help = false;

			var optionSet = new OptionSet() {
                { "s|server=", "the SQL Server host", s => server = s },
                { "db|database=", "database to upgrade", d => database = d},
                { "d|directory=", "directory containing SQL Update files", dir => directory = dir },
                { "u|user=", "Database username", u => username = u},
                { "p|password=", "Database password", p => password = p},
                { "cs|connectionString=", "Full connection string", cs => connectionString = cs},
                { "w|workingDir=", "Working directory for existing Git repo", w => workingDir = @w },
                { "h|help",  "show this message and exit", v => show_help = v != null },
                { "dr|dryrun",  "Return a list of files that would have been executed", dr => dryrun = dr != null },
                { "pa|printAll", "Print the contents of the files returned in the dry run", pa => printAll = pa != null },
                { "prompt|promptUser", "Prompt user before exiting", prompt => promptUser = prompt != null },
                { "b|branch=", "Git branch to checkout and pull", b => branch = b }
            };

			optionSet.Parse(args);

			if (args.Length == 0) {
				show_help = true;
			}

			if (show_help) {
				optionSet.WriteOptionDescriptions(System.Console.Out);
				return;
			}

			Directory.SetCurrentDirectory(workingDir);

			if (String.IsNullOrEmpty(connectionString)) {
				connectionString = BuildConnectionString(server, database, username, password);
			}

			// Get the version hash of the database and repo@HEAD
			branch = !String.IsNullOrEmpty(branch) ? branch : null;
			DatabaseVersion databaseVersion = new DatabaseVersion(connectionString);
			Git aGit = new Git(workingDir, branch);
			aGit.UpdateLocalRepo();
			headVersion = aGit.HeadVersion();

			var dbup = DeployChanges.To
				.SqlDatabase(connectionString)
				.LogToConsole()
				.WithScriptsFromFileSystem(directory)
				.Build(branch);

			LogRunDetails(databaseVersion.Version, headVersion, connectionString, workingDir, dbup.Log);

			if (dryrun) {
				dbup.DryRun(databaseVersion.Version, headVersion, workingDir, printAll, connectionString, aGit);
			} else {
				if (dbup.IsUpgradeRequired(databaseVersion.Version, workingDir, aGit)) {
					if (dbup.PerformUpgrade(databaseVersion.Version, headVersion, workingDir, connectionString, aGit, printAll).Successful) {
						// Set the new database version
						databaseVersion.Version = headVersion;
						dbup.Log.WriteInformation("Database updated to {0}", headVersion);
					}
				} else {
					dbup.Log.WriteInformation("\r\n\r\nDatabase already at newest version. Upgrade is not required.");
				}
			}

			if (promptUser) {
				System.Console.WriteLine("\r\n\r\nPress any key to continue.");
				System.Console.ReadKey();
			}
		}

		private static string BuildConnectionString(string server, string database, string username, string password) {
			var conn = new SqlConnectionStringBuilder();
			conn.DataSource = server;
			conn.InitialCatalog = database;
			if (!String.IsNullOrEmpty(username)) {
				conn.UserID = username;
				conn.Password = password;
				conn.IntegratedSecurity = false;
			} else {
				conn.IntegratedSecurity = true;
			}

			return conn.ToString();
		}

		private static void LogRunDetails(string databaseVersion, string headVersion, string connectionString, string workingDir, IUpgradeLog log) {
			StringBuilder output = new StringBuilder();
			output.AppendLine("--------------------------------------------------------------------------------");
			output.AppendFormat("Connection: {0}\r\n", connectionString);
			output.AppendFormat("Working Directory: {0}\r\n", workingDir);
			output.AppendFormat("DB Version: {0}\r\n", databaseVersion);
			output.AppendFormat("HEAD Version: {0}\r\n", headVersion);
			output.AppendLine("--------------------------------------------------------------------------------");

			log.WriteInformation(output.ToString());
		}
	}
}