using DynamicIp;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration((context, config) =>
    {
        // Add JSON configuration for local development
        var env = context.HostingEnvironment;

        // Add local.settings.json only in local development
        if (env.IsDevelopment())
        {
            config.AddJsonFile("local.settings.json", optional: true, reloadOnChange: true);
        }

        // Add environment variables (these will pick up Azure Application Settings)
        config.AddEnvironmentVariables();
    })
            .ConfigureServices(services =>
            {
                // Register the TimerScheduleService as a singleton service
                services.AddSingleton<TimerScheduleService>();
            })
    .Build();

host.Run();
