using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using App.Metrics.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Noobot.Core.Plugins;

namespace Noobot.Core.Extensions
{
    public static class NoobotHostBuilderExtensions
    {
        public static IHostBuilder AddNoobot(this IHostBuilder builder)
        {
            return builder.ConfigureServices((builderContext, services) =>
            {
                services.AddSingleton<NoobotCore>()
                    .AddSingleton<IHostedService>(provider => provider.GetRequiredService<NoobotCore>())
                    .AddSingleton<INoobotCore>(provider => provider.GetRequiredService<NoobotCore>());

                services.AddSingleton<IHostedService, PluginService>();

                services.AddSingleton<HealthCheck, NoobotHealthCheck>();
            });
        }
    }

    internal class PluginService : IHostedService
    {
        private readonly List<IPlugin> _plugins;

        public PluginService(IEnumerable<IPlugin> plugins)
        {
            _plugins = plugins?.ToList();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _plugins?.ForEach(plugin => plugin.Start());
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _plugins?.ForEach(plugin => plugin.Stop());
            return Task.CompletedTask;
        }
    }

    internal class NoobotHealthCheck : HealthCheck
    {
        private readonly INoobotCore _noobot;

        public NoobotHealthCheck(INoobotCore noobot)
            : base("Noobot Health")
        {
            _noobot = noobot ?? throw new ArgumentNullException(nameof(noobot));
        }

        protected override ValueTask<HealthCheckResult> CheckAsync(CancellationToken cancellationToken = new CancellationToken())
        {
            if (_noobot.IsConnected.HasValue)
            {
                return new ValueTask<HealthCheckResult>(_noobot.IsConnected.Value ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy());
            }

            return new ValueTask<HealthCheckResult>(HealthCheckResult.Unhealthy());
        }


    }
}