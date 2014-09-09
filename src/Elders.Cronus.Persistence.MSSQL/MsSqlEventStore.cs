﻿using Elders.Cronus.DomainModelling;
using Elders.Cronus.EventSourcing;

namespace Elders.Cronus.Persistence.MSSQL
{
    public class MsSqlEventStore : IEventStore
    {
        public MsSqlEventStore(IAggregateRepository aggregateRepository, IEventStorePersister persister, IEventStorePlayer player, IEventStoreStorageManager storageManager = null)
        {
            this.AggregateRepository = aggregateRepository;
            this.Persister = persister;
            this.Player = player;
            this.StorageManager = storageManager;
        }

        public IAggregateRepository AggregateRepository { get; private set; }

        public IEventStorePersister Persister { get; private set; }

        public IEventStorePlayer Player { get; private set; }

        public IEventStoreStorageManager StorageManager { get; private set; }
    }
}
