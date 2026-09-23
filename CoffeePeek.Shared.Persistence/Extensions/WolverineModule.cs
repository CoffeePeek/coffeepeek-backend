using System.Reflection;
using CoffeePeek.Shared.Kernel.Exceptions;
using CoffeePeek.Shared.Kernel.Extentions;
using JasperFx;
using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.ErrorHandling;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;
using Wolverine.Transports;

namespace CoffeePeek.Shared.Persistence.Extensions;

public static class WolverineModule
{
    extension(WebApplicationBuilder builder)
    {
        public void AddWolverine(
            Assembly applicationAssembly,
            Assembly[] handlerAssemblies,
            Type[] serviceLocationTypes)
        {
            var rabbitMqOptions = builder.Services.AddValidateOptions<RabbitMqOptions>();
            var postgresCpOptions = builder.Services.AddValidateOptions<PostgresCpOptions>();

            if (!builder.Environment.IsDevelopment())
            {
                builder.Services.CritterStackDefaults(options =>
                {
                    options.Production.AssertAllPreGeneratedTypesExist = true;
                });
            }
            
            builder.Host.UseWolverine(opts =>
            {
                // Use the service host assembly for generated handler adapters. Without this
                // Wolverine infers this shared project as the application assembly and cannot
                // find the code generated into the individual service projects.
                opts.ApplicationAssembly = applicationAssembly;
                opts.CodeGeneration.TypeLoadMode = builder.Environment.IsDevelopment()
                    ? TypeLoadMode.Auto
                    : TypeLoadMode.Static;

                foreach (var serviceType in serviceLocationTypes)
                {
                    opts.CodeGeneration.AlwaysUseServiceLocationFor(serviceType);
                }

                opts.UseRabbitMq(o =>
                    {
                        o.VirtualHost = string.IsNullOrWhiteSpace(rabbitMqOptions.VirtualHost)
                            ? "/"
                            : rabbitMqOptions.VirtualHost;
                        o.Password = rabbitMqOptions.Password;
                        o.UserName = rabbitMqOptions.Username;
                        o.HostName = rabbitMqOptions.HostName;
                        o.Port = rabbitMqOptions.Port;
                        o.RequestedConnectionTimeout = TimeSpan.FromSeconds(30);
                        o.AutomaticRecoveryEnabled = true;
                        o.NetworkRecoveryInterval = TimeSpan.FromSeconds(5);
                    })
                    .AutoProvision()
                    // Each handler gets its own queue (Account + Shops both listen to ModerationShopApprovedEvent).
                    .UseConventionalRouting(NamingSource.FromHandlerType);

                opts.PersistMessagesWithPostgresql(postgresCpOptions.ConnectionString);

                // Without durable endpoints the outbox only delays sending until commit and keeps messages in memory:
                // a crash or broker outage after commit silently drops events (e.g. a shop approved but never
                // published). Durable inbox also dedupes redelivered envelopes before handlers run.
                opts.Policies.UseDurableOutboxOnAllSendingEndpoints();
                opts.Policies.UseDurableInboxOnAllListeners();

                // Every CoffeePeek service is deployed as a single instance (no replicas in
                // deploy/docker-compose.yml or CoffePeek.AppHost). Wolverine's default
                // DurabilityMode.Balanced runs leader election and cross-node agent
                // coordination (AssignAgent/StartAgent) that assumes a multi-node cluster;
                // with only one node that handshake occasionally exceeds its ack timeout on
                // restart and throws a handled TimeoutException for no functional benefit.
                // Solo mode disables that cross-node coordination while still recovering the
                // transactional inbox/outbox on startup.
                opts.Durability.Mode = DurabilityMode.Solo;

                opts.UseEntityFrameworkCoreTransactions();
                foreach (var assembly in handlerAssemblies)
                {
                    opts.Discovery.IncludeAssembly(assembly);
                }

                opts.Policies.AutoApplyTransactions();

                // Unique violations and concurrency conflicts only (wrapped by UnitOfWork or raw from the EF
                // middleware's SaveChanges): a retry re-runs the handler on a fresh DbContext and its idempotency
                // check sees the competing commit. Everything else fails fast.
                opts.Policies
                    .OnException(TransientDbConflict.Matches)
                    .RetryWithCooldown(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400));
            });
        }
    }
}
