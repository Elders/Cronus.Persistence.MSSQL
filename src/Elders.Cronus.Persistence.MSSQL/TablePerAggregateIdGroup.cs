using System;
using System.Collections.Generic;
using System.Reflection;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.EventStore;

namespace Elders.Cronus.Persistence.MSSQL
{
    public class TablePerAggregateIdGroup : IMsSqlEventStoreTableNameStrategy
    {
        private readonly int length = 2;
        private readonly Assembly aggregatesAssemblies;

        public TablePerAggregateIdGroup(Assembly aggregatesAssemblies)
        {
            this.aggregatesAssemblies = aggregatesAssemblies;
        }

        public string[] GetAllTableNames()
        {
            var boundedContext = aggregatesAssemblies.GetBoundedContext().BoundedContextName;

            var base64Elements = new char[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'J', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z', 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '+', '/' };
            if (base64Elements.Length != 64) throw new InvalidProgramException("Some HEAD changed the basic list of a base64 characters. http://en.wikipedia.org/wiki/Base64");
            var hresult = new List<string>();
            foreach (var first in base64Elements)
                foreach (var second in base64Elements)
                    hresult.Add(GetEventsTableName(boundedContext, String.Concat(first, second)));
            return hresult.ToArray();
        }

        public string GetEventsTableName(AggregateCommit aggregateCommit)
        {
            return GetEventsTableName(aggregateCommit.BoundedContext, aggregateCommit.AggregateRootId);
        }

        public string GetEventsTableName(string boundedContext, IAggregateRootId aggregateRootId)
        {
            return GetEventsTableName(boundedContext, aggregateRootId.RawId);
        }

        private string GetEventsTableName(string boundedContext, byte[] rawId)
        {
            var theBase = Convert.ToBase64String(rawId);
            var chunk = theBase.Substring(theBase.Length - 2, 2);
            return GetEventsTableName(boundedContext, chunk);
        }

        private string GetEventsTableName(string boundedContext, string chunk)
        {
            string tableName = boundedContext + "_es_" + chunk;
            return tableName;
        }
    }
}