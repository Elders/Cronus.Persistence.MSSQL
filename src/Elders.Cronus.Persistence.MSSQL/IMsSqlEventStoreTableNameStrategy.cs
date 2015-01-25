using Elders.Cronus.DomainModeling;
using Elders.Cronus.EventStore;

namespace Elders.Cronus.Persistence.MSSQL
{
    public interface IMsSqlEventStoreTableNameStrategy
    {
        string GetEventsTableName(AggregateCommit aggregateCommit);
        string GetEventsTableName(string boundedContext, IAggregateRootId aggregateRootId);
        string[] GetAllTableNames();
    }
}