//
// Azure Media Services REST API v3 Functions
//
// delete-live-event-output - this function deletes a live event, and associated live outputs
//
/*
```c#
Input :
{
    "liveEventName":"CH1",
    "deleteAsset" : false // optional, default is True
    "azureRegion": "euwe" or "we" or "euno" or "no"// optional. If this value is set, then the AMS account name and resource group are appended with this value. Resource name is not changed if "ResourceGroupFinalName" in app settings is to a value non empty. This feature is useful if you want to manage several AMS account in different regions. Note: the service principal must work with all this accounts
}

Output:
{
    "success": true,
    "errorMessage" : "",
    "operationsVersion": "1.0.0.5"
}

```
*/
//
//

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LiveDrmOperationsV3.Helpers;
using LiveDrmOperationsV3.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Management.Media;
using Microsoft.Azure.Management.Media.Models;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace LiveDrmOperationsV3
{
    public static class DeleteChannel
    {
        [FunctionName("delete-live-event-output")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)]
            HttpRequest req, ILogger log)
        {
            log.LogInformation("C# HTTP trigger function processed a request.");

            var requestBody = new StreamReader(req.Body).ReadToEnd();
            dynamic data = JsonConvert.DeserializeObject(requestBody);

            ConfigWrapper config = null;
            try
            {
                config = new ConfigWrapper(new ConfigurationBuilder()
                        .SetBasePath(Directory.GetCurrentDirectory())
                        .AddEnvironmentVariables()
                        .Build(),
                        (string)data.azureRegion
                );
            }
            catch (Exception ex)
            {
                return IrdetoHelpers.ReturnErrorException(log, ex);
            }

            log.LogInformation("config loaded.");
            log.LogInformation("connecting to AMS account : " + config.AccountName);

            var liveEventName = (string)data.liveEventName;
            if (liveEventName == null)
                return IrdetoHelpers.ReturnErrorException(log, "Error - please pass liveEventName in the JSON");

            var deleteAsset = true;
            if (data.deleteAsset != null) deleteAsset = (bool)data.deleteAsset;

            var client = await MediaServicesHelpers.CreateMediaServicesClientAsync(config);
            // Set the polling interval for long running operations to 2 seconds.
            // The default value is 30 seconds for the .NET client SDK
            client.LongRunningOperationRetryTimeout = 2;

            try
            {
                log.LogInformation("live event : " + liveEventName);
                var liveEvent = client.LiveEvents.Get(config.ResourceGroup, config.AccountName, liveEventName);

                if (liveEvent == null)
                    return IrdetoHelpers.ReturnErrorException(log, $"Live event {liveEventName}  does not exist.");

                // let's purge all live output for now

                var ps = client.LiveOutputs.List(config.ResourceGroup, config.AccountName, liveEventName);
                foreach (var p in ps)
                {
                    var assetName = p.AssetName;
                    var asset = client.Assets.Get(config.ResourceGroup, config.AccountName, assetName);

                    // let's store name of the streaming policy
                    string streamingPolicyName = null;
                    var streamingLocatorsNames = client.Assets.ListStreamingLocators(config.ResourceGroup, config.AccountName, assetName).StreamingLocators.Select(l => l.Name);

                    foreach (var locatorName in streamingLocatorsNames)
                        if (locatorName != null)
                        {
                            //StreamingLocator streamingLocator = await client.StreamingLocators.GetAsync(config.ResourceGroup, config.AccountName, streamingLocatorName);
                            var streamingLocator = await client.StreamingLocators.GetAsync(config.ResourceGroup,
                                config.AccountName, locatorName);

                            if (streamingLocator != null) streamingPolicyName = streamingLocator.StreamingPolicyName;
                        }

                    log.LogInformation("deleting live output : " + p.Name);
                    await client.LiveOutputs.DeleteAsync(config.ResourceGroup, config.AccountName, liveEvent.Name,
                        p.Name);
                    if (deleteAsset)
                    {
                        log.LogInformation("deleting asset : " + assetName);
                        client.Assets.DeleteAsync(config.ResourceGroup, config.AccountName, assetName);
                        if (streamingPolicyName != null && streamingPolicyName.StartsWith(liveEventName)
                        ) // let's delete the streaming policy if custom
                        {
                            log.LogInformation("deleting streaming policy : " + streamingPolicyName);
                            client.StreamingPolicies.DeleteAsync(config.ResourceGroup, config.AccountName,
                                streamingPolicyName);
                        }
                    }
                }

                if (liveEvent.ResourceState == LiveEventResourceState.Running)
                {
                    log.LogInformation("stopping live event : " + liveEvent.Name);
                    await client.LiveEvents.StopAsync(config.ResourceGroup, config.AccountName, liveEvent.Name);
                }
                else if (liveEvent.ResourceState == LiveEventResourceState.Stopping)
                {
                    var liveevt = liveEvent;
                    while (liveevt.ResourceState == LiveEventResourceState.Stopping)
                    {
                        Thread.Sleep(2000);
                        liveevt = client.LiveEvents.Get(config.ResourceGroup, config.AccountName, liveEvent.Name);
                    }
                }

                log.LogInformation("deleting live event : " + liveEvent.Name);
                await client.LiveEvents.DeleteAsync(config.ResourceGroup, config.AccountName, liveEvent.Name);
            }

            catch (Exception ex)
            {
                return IrdetoHelpers.ReturnErrorException(log, ex);
            }

            try
            {
                if (!await CosmosHelpers.DeleteGeneralInfoDocument(new LiveEventEntry
                { LiveEventName = liveEventName, AMSAccountName = config.AccountName }))
                    log.LogWarning("Cosmos access not configured.");
            }
            catch (Exception ex)
            {
                return IrdetoHelpers.ReturnErrorException(log, ex);
            }

            var response = new JObject
            {
                {"liveEventName", liveEventName},
                {"success", true},
                {
                    "operationsVersion",
                    AssemblyName.GetAssemblyName(Assembly.GetExecutingAssembly().Location).Version.ToString()
                }
            };

            return new OkObjectResult(
                response.ToString()
            );
        }
    }
}