using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SqlClient;
using System.IO;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.EventStore;
using Elders.Cronus.Serializer;

namespace Elders.Cronus.Persistence.MSSQL
{
    public class MsSqlEventStore : IEventStore
    {
        private const string LoadAggregateStateQueryTemplate = @"SELECT data FROM {0} WHERE AggregateId=@aggregateId";

        private readonly string connectionString;
        private readonly ISerializer serializer;
        private readonly IMsSqlEventStoreTableNameStrategy tableNameStrategy;

        public MsSqlEventStore(IMsSqlEventStoreTableNameStrategy tableNameStrategy, ISerializer serializer, string connectionString)
        {
            this.connectionString = connectionString;
            this.tableNameStrategy = tableNameStrategy;
            this.serializer = serializer;
        }

        public void Append(AggregateCommit aggregateCommit)
        {
            DataTable eventsTable = CreateInMemoryTableForEvents();
            try
            {
                byte[] data = SerializeEvent(aggregateCommit);
                var eventsRow = eventsTable.NewRow();
                eventsRow[0] = Convert.ToBase64String(aggregateCommit.AggregateRootId);
                eventsRow[1] = aggregateCommit.Revision;
                eventsRow[2] = aggregateCommit.Timestamp;
                eventsRow[3] = data;
                eventsTable.Rows.Add(eventsRow);
            }
            catch (Exception ex)
            {
                eventsTable.Clear();
                throw new AggregateStateFirstLevelConcurrencyException("", ex);
            }

            SqlConnection connection = new SqlConnection(connectionString);
            try
            {
                connection.Open();
                using (SqlBulkCopy bulkCopy = new SqlBulkCopy(connection))
                {
                    bulkCopy.DestinationTableName = "[" + tableNameStrategy.GetEventsTableName(aggregateCommit) + "]";
                    bulkCopy.WriteToServer(eventsTable, DataRowState.Added);
                }
            }
            catch (Exception ex)
            {
                throw new AggregateStateFirstLevelConcurrencyException("", ex);
            }
            finally
            {
                if (connection.State == ConnectionState.Open)
                    connection.Close();
                else
                {
                    throw new Exception("EventStore bulk persist connection is in strange state.");
                }
            }
        }

        public EventStream Load(IAggregateRootId aggregateId)
        {
            using (SqlConnection connection = new SqlConnection(connectionString))
            {
                connection.Open();
                string boundedContext = aggregateId.GetType().GetBoundedContext().BoundedContextName;
                string query = String.Format(LoadAggregateStateQueryTemplate, tableNameStrategy.GetEventsTableName(boundedContext, aggregateId));
                SqlCommand command = new SqlCommand(query, connection);
                command.Parameters.AddWithValue("@aggregateId", Convert.ToBase64String(aggregateId.RawId));
                List<AggregateCommit> aggregateCommits = new List<AggregateCommit>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var buffer = reader[0] as byte[];
                        using (var stream = new MemoryStream(buffer))
                        {
                            aggregateCommits.Add((AggregateCommit)serializer.Deserialize(stream));
                        }
                    }
                }
                return new EventStream(aggregateCommits);
            }
        }

        private byte[] SerializeEvent(AggregateCommit commit)
        {
            using (var stream = new MemoryStream())
            {
                serializer.Serialize(stream, commit);
                return stream.ToArray();
            }
        }

        private static DataTable CreateInMemoryTableForEvents()
        {
            DataTable aggregateCommit = new DataTable();

            DataColumn aggregateId = new DataColumn();
            aggregateId.DataType = typeof(string);
            aggregateId.ColumnName = "AggregateId";
            aggregateCommit.Columns.Add(aggregateId);

            DataColumn revision = new DataColumn();
            revision.DataType = typeof(int);
            revision.ColumnName = "Revision";
            aggregateCommit.Columns.Add(revision);

            DataColumn timestamp = new DataColumn();
            timestamp.DataType = typeof(long);
            timestamp.ColumnName = "Timestamp";
            aggregateCommit.Columns.Add(timestamp);

            DataColumn events = new DataColumn();
            events.DataType = typeof(byte[]);
            events.ColumnName = "Data";
            aggregateCommit.Columns.Add(events);



            DataColumn[] keys = new DataColumn[2];
            keys[0] = aggregateId;
            keys[1] = revision;
            aggregateCommit.PrimaryKey = keys;

            return aggregateCommit;
        }
    }
}
