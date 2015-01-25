using System;
using System.Configuration;
using Elders.Cronus.IocContainer;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.EventStore;
using Elders.Cronus.EventStore.Config;
using Elders.Cronus.Pipeline.Config;
using Elders.Cronus.Serializer;
using System.Reflection;

namespace Elders.Cronus.Persistence.MSSQL.Config
{
    public static class MsSqlEventStoreExtensions
    {
        public static T UseMsSqlEventStore<T>(this T self, Action<MsSqlEventStoreSettings> configure) where T : IConsumerSettings<ICommand>
        {
            MsSqlEventStoreSettings settings = new MsSqlEventStoreSettings(self);
            if (configure != null)
                configure(settings);
            (settings as ISettingsBuilder).Build();
            return self;
        }

        public static T SetConnectionStringName<T>(this T self, string connectionStringName) where T : IMsSqlEventStoreSettings
        {
            self.ConnectionString = ConfigurationManager.ConnectionStrings[connectionStringName].ConnectionString;
            return self;
        }

        public static T SetConnectionString<T>(this T self, string connectionString) where T : IMsSqlEventStoreSettings
        {
            self.ConnectionString = connectionString;
            return self;
        }

        public static T SetAggregateStatesAssembly<T>(this T self, Type aggregateStatesAssembly) where T : IMsSqlEventStoreSettings
        {
            return self.SetAggregateStatesAssembly(Assembly.GetAssembly(aggregateStatesAssembly));
        }

        public static T SetAggregateStatesAssembly<T>(this T self, Assembly aggregateStatesAssembly) where T : IMsSqlEventStoreSettings
        {
            self.BoundedContext = aggregateStatesAssembly.GetAssemblyAttribute<BoundedContextAttribute>().BoundedContextName;
            self.EventStoreTableNameStrategy = new TablePerAggregateIdGroup(aggregateStatesAssembly);
            return self;
        }

        public static T WithNewStorageIfNotExists<T>(this T self) where T : IMsSqlEventStoreSettings
        {
            var storageManager = new MsSqlEventStoreStorageManager(self.EventStoreTableNameStrategy, self.ConnectionString);
            storageManager.CreateStorage();
            return self;
        }
    }

    public interface IMsSqlEventStoreSettings : IEventStoreSettings
    {
        string ConnectionString { get; set; }
        IMsSqlEventStoreTableNameStrategy EventStoreTableNameStrategy { get; set; }
    }

    public class MsSqlEventStoreSettings : SettingsBuilder, IMsSqlEventStoreSettings
    {
        public MsSqlEventStoreSettings(ISettingsBuilder settingsBuilder) : base(settingsBuilder) { }

        string IEventStoreSettings.BoundedContext { get; set; }

        string IMsSqlEventStoreSettings.ConnectionString { get; set; }

        IMsSqlEventStoreTableNameStrategy IMsSqlEventStoreSettings.EventStoreTableNameStrategy { get; set; }

        public override void Build()
        {
            var builder = this as ISettingsBuilder;
            IMsSqlEventStoreSettings settings = this as IMsSqlEventStoreSettings;

            builder.Container.RegisterSingleton<IAggregateRevisionService>(() => new InMemoryAggregateRevisionService(), builder.Name);
            var eventStore = new MsSqlEventStore(settings.EventStoreTableNameStrategy, builder.Container.Resolve<ISerializer>(), settings.ConnectionString);
            var aggregateRepository = new AggregateRepository(eventStore, builder.Container.Resolve<IPublisher<IEvent>>(builder.Name), builder.Container.Resolve<IAggregateRevisionService>(builder.Name));
            //var player = new CassandraEventStorePlayer(settings.Session, settings.EventStoreTableNameStrategy, (this as IEventStoreSettings).BoundedContext, builder.Container.Resolve<ISerializer>());

            builder.Container.RegisterSingleton<IAggregateRepository>(() => aggregateRepository, builder.Name);
            builder.Container.RegisterSingleton<IEventStore>(() => eventStore, builder.Name);
        }
    }
}
