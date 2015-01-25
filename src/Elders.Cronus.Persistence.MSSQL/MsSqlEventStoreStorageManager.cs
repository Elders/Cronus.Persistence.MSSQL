using System;
using System.Data.SqlClient;
using System.Threading;
using Elders.Cronus.EventStore;

namespace Elders.Cronus.Persistence.MSSQL
{
    public class MsSqlEventStoreStorageManager : IEventStoreStorageManager
    {
        const string CreateEventsTableQuery = @"USE [{0}] SET ANSI_NULLS ON SET QUOTED_IDENTIFIER ON SET ANSI_PADDING ON CREATE TABLE [dbo].[{1}]([AggregateId] nvarchar(400) NOT NULL,[Revision] [int] NOT NULL,[Timestamp] [bigint] NOT NULL,[AggregateState] [varbinary](max) NOT NULL,CONSTRAINT [PK_{1}_Cronus] PRIMARY KEY CLUSTERED ([AggregateId],[Revision])WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON) ON [PRIMARY]) ON [PRIMARY] TEXTIMAGE_ON [PRIMARY] SET ANSI_PADDING OFF";

        static readonly log4net.ILog log = log4net.LogManager.GetLogger(typeof(MsSqlEventStoreStorageManager));

        private readonly string connectionString;

        private static object locker = new object();

        private readonly IMsSqlEventStoreTableNameStrategy tableNameStrategy;

        public MsSqlEventStoreStorageManager(IMsSqlEventStoreTableNameStrategy tableNameStrategy, string connectionString)
        {
            this.tableNameStrategy = tableNameStrategy;
            this.connectionString = connectionString;
        }

        public void CreateEventsStorage()
        {
            try
            {
                lock (locker)
                {
                    if (!DatabaseManager.DatabaseExists(connectionString))
                    {
                        DatabaseManager.CreateDatabase(connectionString, enableSnapshotIsolation: true);

                        for (int i = 0; i < 100; i++)
                        {
                            if (!DatabaseManager.DatabaseExists(connectionString))
                                Thread.Sleep(50);
                            else
                                break;
                        }

                        if (!DatabaseManager.DatabaseExists(connectionString))
                            throw new Exception(String.Format("Could not create a database '{0}'." + DatabaseManager.GetDatabaseName(connectionString)));
                    }

                    foreach (var tableName in tableNameStrategy.GetAllTableNames())
                    {
                        if (!DatabaseManager.TableExists(connectionString, tableName))
                        {
                            for (int i = 0; i < 100; i++)
                            {
                                CreateTable(CreateEventsTableQuery, tableName);
                                if (!DatabaseManager.TableExists(connectionString, tableName))
                                    Thread.Sleep(50);
                                else
                                    break;
                            }
                            if (!DatabaseManager.DatabaseExists(connectionString))
                                throw new Exception(String.Format("Could not create a table '{0}'." + tableName));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log.Error("Could not create EventStore.", ex);
            }
        }

        private void CreateTable(string queryTemplate, string tableName)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder(connectionString);

            SqlConnection conn = new SqlConnection(connectionString);
            try
            {
                conn.Open();

                var command = String.Format(queryTemplate, builder.InitialCatalog, tableName.Replace("dbo.", ""));
                SqlCommand cmd = new SqlCommand(command, conn);
                cmd.ExecuteNonQuery();

            }
            finally
            {
                conn.Close();
            }
        }

        public void CreateSnapshotsStorage()
        {
            throw new NotImplementedException();
        }

        public void CreateStorage()
        {
            CreateEventsStorage();
        }
    }
}
