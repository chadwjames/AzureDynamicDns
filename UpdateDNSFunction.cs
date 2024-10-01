using System.Net;
using Azure.Core;
using Azure.Identity;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Azure.ResourceManager.Dns;
using Azure.ResourceManager;
using Azure.ResourceManager.Dns.Models;

namespace DynamicIp
{
    public class UpdateDNSFunction(ILoggerFactory loggerFactory, IConfiguration configuration, TimerScheduleService timerScheduleService)
    {
        private readonly ILogger _logger = loggerFactory.CreateLogger<UpdateDNSFunction>();

        [Function("UpdateDNSFunction")]
        public async Task RunAsync([TimerTrigger("0 * * * * *")] MyInfo myTimer)
        {
            // Retrieve the actual timer schedule from the configuration
            var schedule = timerScheduleService.GetTimerSchedule();
            _logger.LogInformation("Running with configured schedule: {schedule}", schedule);

            _logger.LogInformation("Timer trigger function executed at: {Time}", DateTime.Now);
            _logger.LogInformation("Next timer schedule at: {Next}", myTimer.ScheduleStatus.Next);


            // Get IP address from dynamic dns domain.
            var dynamicDnsDomain = configuration["DynamicDnsDomain"];
            var dynamicDnsIpAddress = await GetIpAddressForDomain(dynamicDnsDomain);
            if (string.IsNullOrEmpty(dynamicDnsIpAddress))
            {
                _logger.LogError("Failed to resolve IP address for domain");
                return;
            }

            _logger.LogInformation("Domain: {dynamicDnsDomain} IP Address: {ipAddress}", dynamicDnsDomain, dynamicDnsIpAddress);

            // Build the service credentials and DNS management client
            var resourceGroupName = configuration["ResourceGroupName"];
            var dnsZoneNames = configuration["DnsZoneNames"];
            var recordSetName = configuration["RecordSetName"];
            var subscriptionId = configuration["SubscriptoinId"];            
            var tenantId = configuration["TenantId"];
            var clientId = configuration["ClientId"];
            var clientSecret = configuration["Secret"];

            var credential = new ClientSecretCredential(tenantId, clientId, clientSecret);
            var armClient = new ArmClient(credential);

            var subscription = armClient.GetSubscriptionResource(new ResourceIdentifier($"/subscriptions/{subscriptionId}"));
            var resourceGroup = await subscription.GetResourceGroupAsync(resourceGroupName);

            if (string.IsNullOrEmpty(dnsZoneNames))
            {
                _logger.LogError("No DNS zone names found in the configuration.");
                return;
            }

            foreach (var dnsZoneName in dnsZoneNames.Split(','))
            {

                var dnsZone = await resourceGroup.Value.GetDnsZoneAsync(dnsZoneName);

                var dnsARecordSetResource = (await dnsZone.Value.GetDnsARecordAsync(recordSetName)).Value;

                // Verify record exists
                if (dnsARecordSetResource.Data.DnsARecords.Count == 0)
                {
                    _logger.LogError("No IP address found in the record set.");
                    return;
                }

                var ipAddress = IPAddress.Parse(dynamicDnsIpAddress);

                // Check if the IP address is different from the current record set
                if (dnsARecordSetResource.Data.DnsARecords.FirstOrDefault()?.IPv4Address.ToString() != ipAddress.ToString())
                {

                    _logger.LogInformation("IP address change detected, updating {dnsZoneName} DNS record.", dnsZoneName);
                    var recordSet = dnsARecordSetResource.Data;

                    recordSet.DnsARecords.Clear();
                    var dnsARecordInfo = new DnsARecordInfo
                    {
                        IPv4Address = ipAddress,
                    };

                    recordSet.DnsARecords.Add(dnsARecordInfo);

                    // Update the record set in Azure DNS
                    await dnsARecordSetResource.UpdateAsync(recordSet, recordSet.ETag);

                }
                else
                {
                    _logger.LogInformation("No IP address change detected for {dnsZonename}, do nothing.", dnsZoneName);
                }
            }
        }

        private async Task<string?> GetIpAddressForDomain(string dynamicDnsDomain)
        {
            try
            {
                // Get host addresses
                IPAddress[] addresses = await Dns.GetHostAddressesAsync(dynamicDnsDomain);
                return addresses.FirstOrDefault()?.ToString();
            }
            catch (Exception ex)
            {
                // Handle exceptions (e.g., domain not found)
                _logger.LogError("{message}", ex.Message);
                return null;
            }
        }
    }

    public class MyInfo
    {
        public required MyScheduleStatus ScheduleStatus { get; set; }

        public bool IsPastDue { get; set; }
    }

    public class MyScheduleStatus
    {
        public DateTime Last { get; set; }

        public DateTime Next { get; set; }

        public DateTime LastUpdated { get; set; }
    }
}
