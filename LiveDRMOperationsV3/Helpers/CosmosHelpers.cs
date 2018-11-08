﻿using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using LiveDrmOperationsV3.Models;
using Microsoft.Azure.Documents;
using Microsoft.Azure.Documents.Client;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace LiveDrmOperationsV3.Helpers
{
    internal partial class CosmosHelpers
    {
        private static readonly IConfigurationRoot Config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddEnvironmentVariables()
            .Build();

        private static readonly string EndpointUrl = Config["CosmosDBAccountEndpoint"];
        private static readonly string AuthorizationKey = Config["CosmosDBAccountKey"];
        private static readonly string Database = Config["CosmosDB"];

        private static readonly string CollectionOutputs = Config["CosmosCollectionLiveEventOutputInfo"];
        private static readonly string CollectionSettings = Config["CosmosCollectionLiveEventSettings"];

        private static readonly bool NotInit =
            string.IsNullOrEmpty(EndpointUrl) || string.IsNullOrEmpty(AuthorizationKey);

        private static readonly DocumentClient _client =
            NotInit ? null : new DocumentClient(new Uri(EndpointUrl), AuthorizationKey);



        private static async Task<bool> CreateOrUpdateDocument(object myObject) // true if success
        {
            if (NotInit) return false;

            string id;
            string collectionId;
            if (myObject.GetType() == typeof(LiveEventEntry))
            {
                id = ((LiveEventEntry)myObject).Id;
                collectionId = CollectionOutputs;

            }
            else if (myObject.GetType() == typeof(LiveEventSettingsInfo))
            {
                id = ((LiveEventSettingsInfo)myObject).Id;
                collectionId = CollectionSettings;
            }
            else
            {
                return false;
            }

            try
            {
                await _client.ReplaceDocumentAsync(
                    UriFactory.CreateDocumentUri(Database, collectionId, id), myObject);
                return true;
            }
            catch (DocumentClientException de)
            {
                if (de.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }
            }

            try // new document
            {
                await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(Database, collectionId), myObject);
                return true;

            }
            catch (DocumentClientException de)
            {
                if (de.StatusCode != HttpStatusCode.NotFound)
                {
                    throw;
                }
            }

            try // let create te db, collection, and document
            {
                await _client.CreateDatabaseIfNotExistsAsync(new Database { Id = Database });
                await _client.CreateDocumentCollectionIfNotExistsAsync(UriFactory.CreateDatabaseUri(Database), new DocumentCollection { Id = collectionId });
                await _client.CreateDocumentAsync(UriFactory.CreateDocumentCollectionUri(Database, collectionId), myObject);
                return true;
            }
            catch (DocumentClientException de)
            {

            }

            return false;
        }

        public static async Task<bool> CreateOrUpdateGeneralInfoDocument(LiveEventEntry liveEvent) // true if success
        {
            return await CreateOrUpdateDocument(liveEvent);
        }


        public static async Task<bool> DeleteGeneralInfoDocument(LiveEventEntry liveEvent)
        {
            if (NotInit) return false;

            try
            {
                await _client.DeleteDocumentAsync(UriFactory.CreateDocumentUri(Database, CollectionOutputs,
                    liveEvent.Id));
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<IQueryable<LiveEventEntry>> ReadGeneralInfoDocument(string liveEventName)
        {
            if (NotInit) return null;

            try
            {
                FeedOptions queryOptions = new FeedOptions { MaxItemCount = -1 };

                return  _client.CreateDocumentQuery<LiveEventEntry>(
            UriFactory.CreateDocumentCollectionUri(Database, CollectionOutputs), queryOptions)
            .Where(f => f.LiveEventName == liveEventName);

                               
            }
            catch
            {
                return null;
            }
        }

        public static async Task<LiveEventSettingsInfo> ReadSettingsDocument(string liveEventName)
        {
            if (NotInit) return null;

            try
            {
                var result =
                    await _client.ReadDocumentAsync(UriFactory.CreateDocumentUri(Database, CollectionSettings,
                        liveEventName));
                return (dynamic)result.Resource;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<bool> CreateOrUpdateSettingsDocument(LiveEventSettingsInfo liveEvenSettings)
        {
            return await CreateOrUpdateDocument(liveEvenSettings);
        }
    }
}