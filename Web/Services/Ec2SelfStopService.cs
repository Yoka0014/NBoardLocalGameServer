using System;
using System.Threading.Tasks;

using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Util;

using Microsoft.Extensions.Logging;

namespace NBoardLocalGameServer.Web.Services
{
    /// <summary>
    /// Stops the EC2 instance this process is running on, using the instance's own IAM instance
    /// profile credentials (no embedded keys). Used by QueueRunner's idle-timeout auto-stop feature.
    /// </summary>
    internal class Ec2SelfStopService(ILogger<Ec2SelfStopService> logger)
    {
        /// <summary>
        /// No-ops (and logs) when not running on an EC2 instance, so this is safe to call from local
        /// dev/test without an AWS environment.
        /// </summary>
        public async Task StopSelfAsync()
        {
            string? instanceId;
            try
            {
                instanceId = EC2InstanceMetadata.InstanceId;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not reach EC2 instance metadata service; skipping self-stop (not running on EC2?).");
                return;
            }

            if (string.IsNullOrEmpty(instanceId))
            {
                logger.LogInformation("Not running on an EC2 instance (no instance metadata available); skipping self-stop.");
                return;
            }

            try
            {
                using var client = new AmazonEC2Client();
                await client.StopInstancesAsync(new StopInstancesRequest { InstanceIds = [instanceId] });
                logger.LogInformation("Requested EC2 self-stop for instance {InstanceId}.", instanceId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to stop EC2 instance {InstanceId}. Check that the instance's IAM role grants ec2:StopInstances on itself.",
                    instanceId);
            }
        }
    }
}
