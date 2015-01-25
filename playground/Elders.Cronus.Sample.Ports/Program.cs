using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using Elders.Cronus.DomainModeling;
using Elders.Cronus.IocContainer;
using Elders.Cronus.Pipeline.Config;
using Elders.Cronus.Pipeline.Hosts;
using Elders.Cronus.Pipeline.Transport.RabbitMQ.Config;
using Elders.Cronus.Sample.Collaboration;
using Elders.Cronus.Sample.Collaboration.Users.Commands;
using Elders.Cronus.Sample.Collaboration.Users.DTOs;
using Elders.Cronus.Sample.Collaboration.Users.Projections;
using Elders.Cronus.Sample.CommonFiles;
using Elders.Cronus.Sample.IdentityAndAccess.Accounts.Commands;
using Elders.Cronus.UnitOfWork;
using NHibernate;
using NHibernate.Mapping.ByCode;

namespace Elders.Cronus.Sample.Ports
{
    class Program
    {
        static CronusHost host;
        public static void Main(string[] args)
        {
            Thread.Sleep(9000);
            log4net.Config.XmlConfigurator.Configure();

            var sf = BuildSessionFactory();
            var container = new Container();
            container.RegisterScoped<IUnitOfWork>(() => new NoUnitOfWork());
            var COLL_POOOOORTHandlerFactory = new PortHandlerFactory(container, null);
            var cfg = new CronusSettings(container)
                .UseContractsFromAssemblies(new Assembly[] { Assembly.GetAssembly(typeof(RegisterAccount)), Assembly.GetAssembly(typeof(CreateUser)) })
                .UsePortConsumer(consumable => consumable
                    .WithDefaultPublishersWithRabbitMq()
                    .UseRabbitMqTransport()
                    .SetNumberOfConsumerThreads(2)
                    .UsePorts(c => c.RegisterAllHandlersInAssembly(Assembly.GetAssembly(typeof(UserProjection)), COLL_POOOOORTHandlerFactory.Create)));

            //{
            //    return FastActivator.CreateInstance(type)
            //        .AssignPropertySafely<IHaveNhibernateSession>(x => x.Session = context.BatchContext.Get<Lazy<ISession>>().Value)
            //        .AssignPropertySafely<IPort>(x => x.CommandPublisher = container.Resolve<IPublisher<ICommand>>());
            //}
            // )));

            (cfg as ISettingsBuilder).Build();
            host = container.Resolve<CronusHost>();
            host.Start();

            Console.WriteLine("Ports started");
            Console.ReadLine();

            host.Stop();
        }

        public class PortHandlerFactory
        {
            private readonly IContainer container;
            private readonly string namedInstance;

            public PortHandlerFactory(IContainer container, string namedInstance)
            {
                this.container = container;
                this.namedInstance = namedInstance;
            }

            public object Create(Type handlerType)
            {
                var handler = FastActivator
                    .CreateInstance(handlerType)
                    .AssignPropertySafely<IPort>(x => x.CommandPublisher = container.Resolve<IPublisher<ICommand>>(namedInstance));
                return handler;
            }
        }

        static ISessionFactory BuildSessionFactory()
        {
            var typesThatShouldBeMapped = Assembly.GetAssembly(typeof(UserProjection)).GetExportedTypes().Where(t => t.Namespace.EndsWith("DTOs"));
            var cfg = new NHibernate.Cfg.Configuration();
            Action<ModelMapper> customMappings = modelMapper =>
            {
                modelMapper.Class<User>(mapper =>
                {
                    mapper.Property(pr => pr.Email, prmap => prmap.Unique(true));
                });
            };

            cfg = cfg.AddAutoMappings(typesThatShouldBeMapped, customMappings);
            return cfg.BuildSessionFactory();
        }
    }
}