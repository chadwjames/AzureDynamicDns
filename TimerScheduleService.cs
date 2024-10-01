using Microsoft.Extensions.Configuration;

namespace DynamicIp
{
    public class TimerScheduleService(IConfiguration configuration)
    {
        private readonly IConfiguration _configuration = configuration;

        public string GetTimerSchedule()
        {
            // Retrieve schedule expression from the configuration
            return _configuration["ScheduleExpression"] ?? "*/30 * * * * *"; // Default to 30 seconds if not set
        }
    }
}
