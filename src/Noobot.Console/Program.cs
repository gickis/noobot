using System.Linq;
using System.Threading.Tasks;
using App.Metrics;
using App.Metrics.Health;
using App.Metrics.Health.Builder;
using App.Metrics.Reporting.Console;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Noobot.Core.Configuration;
using Noobot.Core.Extensions;

namespace Noobot.Console
{
    public class Program
    {
        //private static INoobotCore _noobotCore;
        //private static readonly ManualResetEvent _quitEvent = new ManualResetEvent(false);

        private static async Task Main(string[] args)
        {
            await new HostBuilder()
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    config.AddEnvironmentVariables();
                    config.AddJsonFile("appsettings.json", true);
                    config.AddCommandLine(args);
                })
                .ConfigureLogging((hostingContext, logging) =>
                {
                    logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging"));
                    logging.AddConsole();
                })
                .ConfigureServices((builderContext, services) =>
                {
                    services
                        .Configure<NoobotOptions>(builderContext.Configuration.GetSection(nameof(NoobotOptions)))
                        .AddSingleton(provider => provider.GetRequiredService<IOptions<NoobotOptions>>().Value);

                    services
                        .Configure<MetricsOptions>(builderContext.Configuration.GetSection(nameof(MetricsOptions)))
                        .AddSingleton(provider => provider.GetRequiredService<IOptions<MetricsOptions>>().Value);

                    services
                        .Configure<MetricsReportingConsoleOptions>(
                            builderContext.Configuration.GetSection(nameof(MetricsReportingConsoleOptions)))
                        .AddSingleton(provider =>
                            provider.GetRequiredService<IOptions<MetricsReportingConsoleOptions>>().Value);

                    services
                        .AddSingleton(provider => new MetricsBuilder()
                            .Configuration
                            .Extend(provider.GetRequiredService<MetricsOptions>())
                            .Report
                            .ToConsole(provider.GetRequiredService<MetricsReportingConsoleOptions>())
                            .Build())
                        .AddSingleton<IMetrics>(provider => provider.GetRequiredService<IMetricsRoot>())
                        .AddSingleton(provider => provider.GetRequiredService<IMetricsRoot>().Reporters.AsEnumerable())
                        .AddSingleton<IHostedService, ReportSchedulerHostedService>();
                })
                .AddNoobot()
                .RunConsoleAsync();

            //System.Console.WriteLine("Starting Noobot...");
            //AppDomain.CurrentDomain.ProcessExit += ProcessExitHandler; // closing the window doesn't hit this in Windows
            //System.Console.CancelKeyPress += ConsoleOnCancelKeyPress;

            //RunNoobot()
            //    .GetAwaiter()
            //    .GetResult();

            //_quitEvent.WaitOne();
        }

        //private static async Task RunNoobot()
        //{
        //    var containerFactory = new ContainerFactory(
        //        new ConfigurationBase(),
        //        new JsonConfigReader(),
        //        GetLogger());

        //    var container = containerFactory.CreateContainer();
        //    _noobotCore = container.GetNoobotCore();

        //    await _noobotCore.Connect();
        //}

        //private static ConsoleOutLogger GetLogger()
        //{
        //    return new ConsoleOutLogger("Noobot", LogLevel.All, true, true, false, "yyyy/MM/dd HH:mm:ss:fff");
        //}

        //private static void ConsoleOnCancelKeyPress(object sender, ConsoleCancelEventArgs consoleCancelEventArgs)
        //{
        //    _quitEvent.Set();
        //    consoleCancelEventArgs.Cancel = true;
        //}

        //// not hit
        //private static void ProcessExitHandler(object sender, EventArgs e)
        //{
        //    System.Console.WriteLine("Disconnecting...");
        //    _noobotCore?.Disconnect();
        //}
    }
}