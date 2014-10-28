﻿using System;
using System.Collections.Generic;
using System.Linq;
using DbUp.Builder;
using System.IO;
using System.Diagnostics;

namespace DbUp.Engine
{
    /// <summary>
    /// This class orchestrates the database upgrade process.
    /// </summary>
    public class UpgradeEngine
    {
        private readonly UpgradeConfiguration configuration;

        /// <summary>
        /// Initializes a new instance of the <see cref="UpgradeEngine"/> class.
        /// </summary>
        /// <param name="configuration">The configuration.</param>
        public UpgradeEngine(UpgradeConfiguration configuration)
        {
            this.configuration = configuration;
        }

        /// <summary>
        /// Determines whether the database is out of date and can be upgraded.
        /// </summary>
        public bool IsUpgradeRequired(string databaseVersionHash, string workingDir)
        {
            return GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash).Count() != 0;
        }

        /// <summary>
        /// Return a list of scripts that would be ran if this was not a dry run
        /// </summary>
        /// <param name="databaseVersionHash">The most recent database version</param>
        /// <param name="workingDir">The local clone of the migrations repository</param>
        /// <returns>List of all the files that would be executed on the target database</returns>
        public string DryRun(string databaseVersionHash, string workingDir)
        {
            return GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash).ToString();
        }

        /// <summary>
        /// Tries to connect to the database.
        /// </summary>
        /// <param name="errorMessage">Any error message encountered.</param>
        /// <returns></returns>
        public bool TryConnect(out string errorMessage)
        {
            try
            {
                errorMessage = "";
                configuration.ConnectionManager.ExecuteCommandsWithManagedConnection(dbCommandFactory =>
                {
                    using (var command = dbCommandFactory())
                    {
                        command.CommandText = "select 1";
                        command.ExecuteScalar();
                    }
                });
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Performs the database upgrade.
        /// </summary>
        public DatabaseUpgradeResult PerformUpgrade(string databaseVersionHash, string workingDir)
        {
            var executed = new List<SqlScript>();
            try
            {
                using (configuration.ConnectionManager.OperationStarting(configuration.Log, executed))
                {
                    configuration.Log.WriteInformation("Beginning database upgrade");

                    var scriptsToExecute = GetScriptsToExecuteInsideOperation(workingDir, databaseVersionHash);

                    if (scriptsToExecute.Count == 0)
                    {
                        configuration.Log.WriteInformation("No new scripts need to be executed - completing.");
                        return new DatabaseUpgradeResult(executed, true, null);
                    }

                    configuration.ScriptExecutor.VerifySchema();

                    // TODO: Update to execute one script so the updates can be easily rolled back
                    foreach (var script in scriptsToExecute)
                    {
                        configuration.ScriptExecutor.Execute(script, configuration.Variables);

                        configuration.Journal.StoreExecutedScript(script);

                        executed.Add(script);
                    }

                    configuration.Log.WriteInformation("Upgrade successful");
                    return new DatabaseUpgradeResult(executed, true, null);
                }
            }
            catch (Exception ex)
            {
                configuration.Log.WriteError("Upgrade failed due to an unexpected exception:\r\n{0}", ex.ToString());
                return new DatabaseUpgradeResult(executed, false, ex);
            }
        }

        /// <summary>
        /// Returns a list of scripts that will be executed when the upgrade is performed
        /// </summary>
        /// <returns>The scripts to be executed</returns>
        /*
        public List<SqlScript> GetScriptsToExecute()
        {
            using (configuration.ConnectionManager.OperationStarting(configuration.Log, new List<SqlScript>()))
            {
                return GetScriptsToExecuteInsideOperation();
            }
        }
        */
        private List<SqlScript> GetScriptsToExecuteInsideOperation(string workingDir, string databaseVersionHash, string repoVersionHash = "HEAD")
        {
            // Git repo must already be cloned into workspace
            try {
                var aGit = new Git(workingDir);
                aGit.UpdateLocalRepo();
                return aGit.GetMigrationFiles(databaseVersionHash,repoVersionHash).Select(s => SqlScript.FromFile(s)).ToList();
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
        public DatabaseUpgradeResult UpdateDatabaseVersion(DatabaseVersion dbVersion, string headHash)
        {
            var marked = new List<SqlScript>();
            using (configuration.ConnectionManager.OperationStarting(configuration.Log, marked))
            {
                try
                {
                    dbVersion.Version = headHash;
                    configuration.Log.WriteInformation("Database updated successfully to version: " + headHash);
                    return new DatabaseUpgradeResult(marked, true, null);
                }
                catch (Exception ex)
                {
                    configuration.Log.WriteError("Update failed due to an unexpected exception:\r\n{0}", ex.ToString());
                    return new DatabaseUpgradeResult(marked, false, ex);
                }
            }
        }
    }
}