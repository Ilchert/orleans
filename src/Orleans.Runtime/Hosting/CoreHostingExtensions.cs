using System;
using System.Diagnostics;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Orleans.Configuration;
using Orleans.Configuration.Internal;
using Orleans.Runtime;
using Orleans.Runtime.MembershipService;

namespace Orleans.Hosting
{
    /// <summary>
    /// Extensions for <see cref="ISiloBuilder"/> instances.
    /// </summary>
    public static class CoreHostingExtensions
    {
        /// <summary>
        /// Configure the container to use Orleans.
        /// </summary>


        public static ISiloBuilder AddActivityPropagation(this ISiloBuilder builder) =>
            builder.ConfigureServices(services =>
                {
                    var listener = new DiagnosticListener("Orleans");
                    services.TryAddSingleton(listener);
                    services.TryAddSingleton<DiagnosticSource>(listener);
                })
                .AddOutgoingGrainCallFilter<ActivityPropagationGrainCallFilter.ActivityPropagationOutgoingGrainCallFilter>()
                .AddIncomingGrainCallFilter<ActivityPropagationGrainCallFilter.ActivityPropagationIncomingGrainCallFilter>();

        /// <param name="builder">The silo builder.</param>
        /// <returns>The silo builder.</returns>
        public static ISiloBuilder ConfigureDefaults(this ISiloBuilder builder)
        {
            return builder.ConfigureServices((context, services) =>
            {
                if (!context.Properties.ContainsKey("OrleansServicesAdded"))
                {
                    services.PostConfigure<SiloOptions>(
                        options => options.SiloName =
                            options.SiloName ?? $"Silo_{Guid.NewGuid().ToString("N").Substring(0, 5)}");

                    services.TryAddSingleton<Silo>();
                    DefaultSiloServices.AddDefaultServices(services);

                    context.Properties.Add("OrleansServicesAdded", true);
                }
            });
        }

        /// <summary>
        /// Configures the silo to use development-only clustering and listen on localhost.
        /// </summary>
        /// <param name="builder">The silo builder.</param>
        /// <param name="siloPort">The silo port.</param>
        /// <param name="gatewayPort">The gateway port.</param>
        /// <param name="primarySiloEndpoint">
        /// The endpoint of the primary silo, or <see langword="null"/> to use this silo as the primary.
        /// </param>
        /// <param name="serviceId">The service id.</param>
        /// <param name="clusterId">The cluster id.</param>
        /// <returns>The silo builder.</returns>
        public static ISiloBuilder UseLocalhostClustering(
            this ISiloBuilder builder,
            int siloPort = EndpointOptions.DEFAULT_SILO_PORT,
            int gatewayPort = EndpointOptions.DEFAULT_GATEWAY_PORT,
            IPEndPoint primarySiloEndpoint = null,
            string serviceId = ClusterOptions.DevelopmentServiceId,
            string clusterId = ClusterOptions.DevelopmentClusterId)
        {
            builder.Configure<EndpointOptions>(options =>
            {
                options.AdvertisedIPAddress = IPAddress.Loopback;
                options.SiloPort = siloPort;
                options.GatewayPort = gatewayPort;
            });

            builder.UseDevelopmentClustering(optionsBuilder => ConfigurePrimarySiloEndpoint(optionsBuilder, primarySiloEndpoint));
            builder.ConfigureServices(services =>
            {
                // If the caller did not override service id or cluster id, configure default values as a fallback.
                if (string.Equals(serviceId, ClusterOptions.DevelopmentServiceId) && string.Equals(clusterId, ClusterOptions.DevelopmentClusterId))
                {
                    services.PostConfigure<ClusterOptions>(options =>
                    {
                        if (string.IsNullOrWhiteSpace(options.ClusterId)) options.ClusterId = ClusterOptions.DevelopmentClusterId;
                        if (string.IsNullOrWhiteSpace(options.ServiceId)) options.ServiceId = ClusterOptions.DevelopmentServiceId;
                    });
                }
                else
                {
                    services.Configure<ClusterOptions>(options =>
                    {
                        options.ServiceId = serviceId;
                        options.ClusterId = clusterId;
                    });
                }
            });

            return builder;
        }

        /// <summary>
        /// Configures the silo to use development-only clustering.
        /// </summary>
        /// <param name="builder"></param>
        /// <param name="primarySiloEndpoint">
        /// The endpoint of the primary silo, or <see langword="null"/> to use this silo as the primary.
        /// </param>
        /// <returns>The silo builder.</returns>
        public static ISiloBuilder UseDevelopmentClustering(this ISiloBuilder builder, IPEndPoint primarySiloEndpoint)
        {
            return builder.UseDevelopmentClustering(optionsBuilder => ConfigurePrimarySiloEndpoint(optionsBuilder, primarySiloEndpoint));
        }

        /// <summary>
        /// Configures the silo to use development-only clustering.
        /// </summary>
        public static ISiloBuilder UseDevelopmentClustering(
            this ISiloBuilder builder,
            Action<DevelopmentClusterMembershipOptions> configureOptions)
        {
            return builder.UseDevelopmentClustering(options => options.Configure(configureOptions));
        }

        /// <summary>
        /// Configures the silo to use development-only clustering.
        /// </summary>
        public static ISiloBuilder UseDevelopmentClustering(
            this ISiloBuilder builder,
            Action<OptionsBuilder<DevelopmentClusterMembershipOptions>> configureOptions)
        {
            return builder.ConfigureServices(
                services =>
                {
                    configureOptions?.Invoke(services.AddOptions<DevelopmentClusterMembershipOptions>());
                    services.ConfigureFormatter<DevelopmentClusterMembershipOptions>();
                    services
                        .AddSingleton<SystemTargetBasedMembershipTable>()
                        .AddFromExisting<IMembershipTable, SystemTargetBasedMembershipTable>();
                });
        }

        private static void ConfigurePrimarySiloEndpoint(OptionsBuilder<DevelopmentClusterMembershipOptions> optionsBuilder, IPEndPoint primarySiloEndpoint)
        {
            optionsBuilder.Configure((DevelopmentClusterMembershipOptions options, IOptions<EndpointOptions> endpointOptions) =>
            {
                if (primarySiloEndpoint is null)
                {
                    primarySiloEndpoint = endpointOptions.Value.GetPublicSiloEndpoint();
                }

                options.PrimarySiloEndpoint = primarySiloEndpoint;
            });
        }
    }
}