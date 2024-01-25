using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Management.Dns;
using Microsoft.Azure.Management.Dns.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Rest.Azure.Authentication;

namespace DynamicIp
{
    public class UpdateDNSFunction
    {
        private readonly ILogger _logger;
        private readonly IConfiguration _configuration;

        public UpdateDNSFunction(ILoggerFactory loggerFactory, IConfiguration configuration)
        {
            _logger = loggerFactory.CreateLogger<UpdateDNSFunction>();
            _configuration = configuration;
        }

        [Function("UpdateDNSFunction")]
        public async Task RunAsync([TimerTrigger("0 * * * * *")] MyInfo myTimer)
        {
            _logger.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            _logger.LogInformation($"Next timer schedule at: {myTimer.ScheduleStatus.Next}");

            var dynamicDnsDomain = _configuration["DynamicDnsDomain"];
            var resourceGroupName = _configuration["ResourceGroupName"];
            var dnsZoneName = _configuration["DnsZoneName"];
            var recordSetName = _configuration["RecordSetName"];
            var subscriptionId = _configuration["SubscriptoinId"];

            // Get IP address from dynamic dns domain.
            var ipAddress = await GetIpAddressForDomain(dynamicDnsDomain);
            if (string.IsNullOrEmpty(ipAddress))
            {
                _logger.LogError("Failed to resolve IP address for domain");
                return;
            }

            _logger.LogInformation($"Domain: {dynamicDnsDomain} IP Address: {ipAddress}");

            // Build the service credentials and DNS management client
            var tenantId = _configuration["TenantId"];
            var clientId = _configuration["ClientId"];
            var secret = _configuration["Secret"];

            var serviceCreds = await ApplicationTokenProvider.LoginSilentAsync(tenantId, clientId, secret);
            var dnsClient = new DnsManagementClient(serviceCreds)
            {
                SubscriptionId = subscriptionId
            };

            var recordSet = dnsClient.RecordSets.Get(resourceGroupName, dnsZoneName, recordSetName, RecordType.A);

            // Add a new record to the local object.  Note that records in a record set must be unique/distinct
            if(recordSet.ARecords.FirstOrDefault()?.Ipv4Address != ipAddress)
            {

                _logger.LogInformation("IP address change detected, updating DNS record.");

                recordSet.ARecords.Clear();
                recordSet.ARecords.Add(new ARecord(ipAddress));

                // Update the record set in Azure DNS
                // Note: ETAG check specified, update will be rejected if the record set has changed in the meantime
                _ = await dnsClient.RecordSets.CreateOrUpdateAsync(resourceGroupName, dnsZoneName, recordSetName, RecordType.A, recordSet, recordSet.Etag);
            }
            else
            {
                _logger.LogInformation("No IP address change detected, do nothing.");
            }

            //Sleep for one 1 Seconds.
            _logger.LogInformation("Sleeping for 30 seconds...");
            Thread.Sleep(1000);
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
                _logger.LogError(ex.Message);
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
