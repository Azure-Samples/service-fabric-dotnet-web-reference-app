// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using System;
    using System.Collections.Generic;
    using System.Configuration;
    using System.Fabric;
    using System.Fabric.Testability;
    using System.IO;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Common;
    using Inventory.Domain;
    using Microsoft.ServiceFabric.Data;
    using Microsoft.ServiceFabric.Data.Collections;
    using Microsoft.ServiceFabric.Services.Communication.Runtime;
    using Microsoft.ServiceFabric.Services.Remoting.Client;
    using Microsoft.ServiceFabric.Services.Remoting.Runtime;
    using Microsoft.ServiceFabric.Services.Runtime;
    using Microsoft.WindowsAzure.Storage.Auth;
    using RestockRequest.Domain;
    using RestockRequestManager.Domain;

    internal class InventoryService : StatefulService, IInventoryService
    {
        private const string InventoryItemDictionaryName = "inventoryItems";
        private const string ActorMessageDictionaryName = "incomingMessages";
        private const string RestockRequestManagerServiceName = "RestockRequestManager";
        private const string RequestHistoryDictionaryName = "RequestHistory";

        private static string CurSetting; // this helper string is used to assign fallback strings when running unit tests (ConfigurationManager does not assign right values during unit tests)
        private static string BackupTestAccountName = ((CurSetting = ConfigurationManager.AppSettings["BackupTestAccountName"]) != null) ? CurSetting : "TBA=";  // this string needs to have length multiple of 4
        private static string PrimaryKeyForBackupTestAccount = ((CurSetting = ConfigurationManager.AppSettings["PrimaryKeyForBackupTestAccount"]) != null) ? CurSetting : "TBA="; // this string needs to have length multiple of 4
        private static string BlobServiceEndpointAddress = ((CurSetting = ConfigurationManager.AppSettings["BlobServiceEndPointAddress"]) != null) ? CurSetting : "https://TBA.net/"; // this string needs to have valid Uri syntax

        private readonly Uri blobContainerEndpoint = new Uri(BlobServiceEndpointAddress);
        private readonly Uri countDictionaryName = new Uri("fabric:/countDictionary");
        private readonly StorageCredentials storageCredentials = new StorageCredentials(BackupTestAccountName, PrimaryKeyForBackupTestAccount);
        private IReliableStateManager stateManager;

        private IBackupStore backupStore;
        //Set local backup folder
        private string localBackupStore = @"E:\Temp2\";


        //Set local or cloud backup
        private StorageTypes storageType = StorageTypes.Azure;

        public InventoryService()
        {
        }

        /// <summary>
        /// This constructor is used in unit tests to inject a different state manager for unit testing.
        /// </summary>
        /// <param name="stateManager"></param>
        public InventoryService(IReliableStateManager stateManager)
        {
            this.stateManager = stateManager;
        }

        /// <summary>
        /// Used internally to generate inventory items and adds them to the ReliableDict we have.
        /// </summary>
        /// <param name="item"></param>
        /// <returns></returns>
        public async Task<bool> CreateInventoryItemAsync(InventoryItem item)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await inventoryItems.AddAsync(tx, item.Id, item);
                await tx.CommitAsync();
                ServiceEventSource.Current.ServiceMessage(this, "Created inventory item: {0}", item);
            }

            return true;
        }

        /// <summary>
        /// Tries to add the given quantity to the inventory item with the given ID without going over the maximum quantity allowed for an item.
        /// </summary>
        /// <param name="itemId"></param>
        /// <param name="quantity"></param>
        /// <returns>The quantity actually added to the item.</returns>
        public async Task<int> AddStockAsync(InventoryItemId itemId, int quantity)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            int quantityAdded = 0;

            ServiceEventSource.Current.ServiceMessage(this, "Received add stock request. Item: {0}. Quantity: {1}.", itemId, quantity);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                // Try to get the InventoryItem for the ID in the request.
                ConditionalResult<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);

                // We can only update the stock for InventoryItems in the system - we are not adding new items here.
                if (item.HasValue)
                {
                    // Update the stock quantity of the item.
                    // This only updates the copy of the Inventory Item that's in local memory here;
                    // It's not yet saved in the dictionary.
                    quantityAdded = item.Value.AddStock(quantity);

                    // We have to store the item back in the dictionary in order to actually save it.
                    // This will then replicate the updated item for
                    await inventoryItems.SetAsync(tx, item.Value.Id, item.Value);
                }

                // nothing will happen unless we commit the transaction!
                await tx.CommitAsync();

                ServiceEventSource.Current.ServiceMessage(
                    this,
                    "Add stock complete. Item: {0}. Added: {1}. Total: {2}",
                    item.Value.Id,
                    quantityAdded,
                    item.Value.AvailableStock);
            }


            return quantityAdded;
        }

        /// <summary>
        /// Removes the given quantity of stock from an in item in the inventory.
        /// </summary>
        /// <param name="request"></param>
        /// <returns>int: Returns the quantity removed from stock.</returns>
        public async Task<int> RemoveStockAsync(InventoryItemId itemId, int quantity, CustomerOrderActorMessageId amId)
        {
            ServiceEventSource.Current.ServiceMessage(this, "inside remove stock {0}|{1}", amId.GetHashCode(), amId.GetHashCode());

            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, DateTime> recentRequests =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, DateTime>>(ActorMessageDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>> requestHistory =
                await
                    this.stateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>>>(RequestHistoryDictionaryName);

            int removed = 0;

            ServiceEventSource.Current.ServiceMessage(this, "Received remove stock request. Item: {0}. Quantity: {1}.", itemId, quantity);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                //first let's see if this is a duplicate request
                ConditionalResult<DateTime> previousRequest = await recentRequests.TryGetValueAsync(tx, amId);
                if (!previousRequest.HasValue)
                {
                    //first time we've seen the request or it was a dupe from so long ago we have forgotten

                    // Try to get the InventoryItem for the ID in the request.
                    ConditionalResult<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);

                    // We can only remove stock for InventoryItems in the system.
                    if (item.HasValue)
                    {
                        // Update the stock quantity of the item.
                        // This only updates the copy of the Inventory Item that's in local memory here;
                        // It's not yet saved in the dictionary.
                        removed = item.Value.RemoveStock(quantity);

                        // We have to store the item back in the dictionary in order to actually save it.
                        // This will then replicate the updated item
                        await inventoryItems.SetAsync(tx, itemId, item.Value);

                        //we also have to make a note that we have returned this result, so that we can protect
                        //ourselves from stale or duplicate requests that come back later
                        await requestHistory.SetAsync(tx, amId, new Tuple<InventoryItemId, int>(itemId, removed));

                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Removed stock complete. Item: {0}. Removed: {1}. Remaining: {2}",
                            item.Value.Id,
                            removed,
                            item.Value.AvailableStock);
                    }
                }
                else
                {
                    //this is a duplicate request. We need to send back the result we already came up with and hope they get it this time
                    //find the previous result and send it back
                    ConditionalResult<Tuple<InventoryItemId, int>> previousResponse = await requestHistory.TryGetValueAsync(tx, amId);

                    if (previousResponse.HasValue)
                    {
                        removed = previousResponse.Value.Item2;
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Retrieved previous response for request {0}, from {1}, for Item {2} and quantity {3}",
                            amId,
                            previousRequest.Value,
                            previousResponse.Value.Item1,
                            previousResponse.Value.Item2);
                    }
                    else
                    {
                        //we've seen the request before but we don't have a record for what we responded, inconsistent state
                        ServiceEventSource.Current.ServiceMessage(
                            this,
                            "Inconsistent State: recieved duplicate request {0} but don't have matching response in history",
                            amId);
                        this.ServicePartition.ReportFault(System.Fabric.FaultType.Transient);
                    }


                    //note about duplicate Requests: technically if a duplicate request comes in and we have 
                    //sufficient invintory to return more now that we did previously, we could return more of the order and decrement 
                    //the difference to reduce the total number of round trips. This optimization is not currently implemented
                }


                //always update the datetime for the given request
                await recentRequests.SetAsync(tx, amId, DateTime.UtcNow);

                // nothing will happen unless we commit the transaction!
                ServiceEventSource.Current.Message("Committing Changes in Inventory Service");
                await tx.CommitAsync();
                ServiceEventSource.Current.Message("Inventory Service Changes Committed");
            }

            ServiceEventSource.Current.Message("Removed {0} of item {1}", removed, itemId);
            return removed;
        }

        public async Task<bool> IsItemInInventoryAsync(InventoryItemId itemId)
        {
            ServiceEventSource.Current.Message("checking item {0} to see if it is in inventory", itemId);
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            PrintInventoryItems(inventoryItems);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                ConditionalResult<InventoryItem> item = await inventoryItems.TryGetValueAsync(tx, itemId);
                return item.HasValue;
            }
        }

        /// <summary>
        /// Retrieves a customer-specific view (defined in the InventoryItemView class in the Fabrikam Common namespace)
        /// af all items in the IReliableDictionary in InventoryService. Only items with a CustomerAvailableStock greater than
        /// zero are returned as a business logic constraint to reduce overordering. 
        /// </summary>
        /// <returns>IEnumerable of InventoryItemView</returns>
        public async Task<IEnumerable<InventoryItemView>> GetCustomerInventoryAsync()
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            ServiceEventSource.Current.Message("Called GetCustomerInventory to return InventoryItemView");

            PrintInventoryItems(inventoryItems);

            IEnumerable<InventoryItemView> results = inventoryItems.Select(x => (InventoryItemView) x.Value).Where(x => x.CustomerAvailableStock > 0);

            List<InventoryItemView> resultList = results.ToList<InventoryItemView>();

            foreach (InventoryItemView tempResult in resultList)
            {
                ServiceEventSource.Current.Message("{0}|{1}", tempResult.Id, tempResult.Description);
            }

            return results;
        }

        /// <summary>
        /// NOTE: This should not be used in published MVP code. 
        /// This function allows us to remove inventory items from inventory.
        /// </summary>
        /// <param name="itemId"></param>
        /// <returns></returns>
        public async Task DeleteInventoryItemAsync(InventoryItemId itemId)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);

            using (ITransaction tx = this.stateManager.CreateTransaction())
            {
                await inventoryItems.TryRemoveAsync(tx, itemId);
                await tx.CommitAsync();
            }
        }

        public async Task CopyBackupFolderAsync(string backupFolder, string partitionId, string backupId, CancellationToken cancellationToken)
        {
            //Storing local backups; set to E:\Temp2 by default.
            string pathToFolder = Path.Combine(this.localBackupStore, partitionId, backupId);
            ServiceEventSource.Current.ServiceMessage(this, "Backup Folder Path " + pathToFolder);

            await this.CopyDirectory(backupFolder, pathToFolder);

            //var blob = this.backupBlobContainer.GetBlockBlobReference(backupId);
            //await blob.UploadFromFileAsync(pathToZippedFolder, FileMode.Open, CancellationToken.None);
            //Service.WriteTrace("BackupStore: UploadBackupFolderAsync: success.");
        }

        protected override IReliableStateManager CreateReliableStateManager()
        {
            if (this.stateManager == null)
            {
                //this.stateManager = base.CreateReliableStateManager();
                this.stateManager = new ReliableStateManager(
                    new ReliableStateManagerConfiguration(
                        onDataLossEvent: this.OnDataLossAsync));
            }
            return this.stateManager;
        }

        /// <summary>
        /// Creates a new communication listener for protocol of our choice.
        /// </summary>
        /// <returns></returns>
        protected override IEnumerable<ServiceReplicaListener> CreateServiceReplicaListeners()
        {
            return new List<ServiceReplicaListener>()
            {
                new ServiceReplicaListener(
                    (initParams) =>
                        new ServiceRemotingListener<IInventoryService>(initParams, this))
            };
        }


        protected async Task<bool> OnDataLossAsync(CancellationToken cancellationToken)
        {
            //If restoring from Azure BlobStore
            if (this.storageType == StorageTypes.Azure)
            {
                await this.InitializeAsync(cancellationToken);
            }


            try
            {
                string backupFolder;

                //If restoring from local directory
                if (this.storageType == StorageTypes.Local)
                {
                    backupFolder = this.GetLastBackupFolder();
                }
                //If restoring from Azure BlobStore
                else
                {
                    backupFolder = await this.backupStore.DownloadAnyBackupAsync(cancellationToken);
                }


                ServiceEventSource.Current.ServiceMessage(this, "Restoration Folder Path " + backupFolder);
                await this.StateManager.RestoreAsync(backupFolder, RestorePolicy.Force, cancellationToken);

                ServiceEventSource.Current.ServiceMessage(this, "Restore completed");

                return true;
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Restoration failed: " + "{0} {1}" + e.GetType() + e.Message);

                throw;
            }
        }


        /// <summary>
        /// Populates the inventory with some dummy items.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        protected override Task RunAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("InventoryService ReliableDictionary successfully created");
            //If restoring from Azure BlobStore

            return Task.WhenAll(this.PeriodicInventoryCheck(cancellationToken), this.PeriodicOldMessageTrimming(cancellationToken));
        }


        private async Task<bool> BackupCallbackAzureAsync(BackupInfo backupInfo)
        {
            string backupId = Guid.NewGuid().ToString();
            CancellationToken cancellationToken = default(CancellationToken);

            long totalBackupCount;


            IReliableDictionary<int, long> countDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<int, long>>(this.countDictionaryName);
            using (ITransaction txn = this.StateManager.CreateTransaction())
            {
                long count = await countDictionary.AddOrUpdateAsync(txn, 0, 0, (key, oldValue) => { return oldValue + 1; });

                totalBackupCount = count;
                await txn.CommitAsync();
            }


            ServiceEventSource.Current.Message("Backup count dictionary updated: " + totalBackupCount);


            if ((totalBackupCount%10) == 0)
            {
                //Store no more than 10 backups at a time - the actual max might be a bit more than 10 since more backups could have been created when deletion was taking place. Keeps behind 5 backups.
                await this.backupStore.DeleteBackupsAzureAsync(cancellationToken);
            }

            if ((totalBackupCount > 10) && (DateTime.Now.Second%20) == 0)
            {
                //Let's simulate a data loss every time the time is a multiple of 20 seconds, and a backup just completed.
                ServiceEventSource.Current.ServiceMessage(this, "Restore Started");

                using (FabricClient fabricClient = new FabricClient())
                {
                    PartitionSelector partitionSelector = PartitionSelector.PartitionIdOf(
                        this.ServiceInitializationParameters.ServiceName,
                        this.ServiceInitializationParameters.PartitionId);

                    await fabricClient.ServiceManager.InvokeDataLossAsync(partitionSelector, DataLossMode.PartialDataLoss, cancellationToken);
                }
            }

            ServiceEventSource.Current.Message("Backing up from directory, ID  : " + backupInfo.Directory + " *** " + backupId);
            try
            {
                await this.backupStore.UploadBackupFolderAsync(backupInfo.Directory, backupId, CancellationToken.None);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Uploading to backup folder failed: " + "{0} {1}" + e.GetType() + e.Message);
            }

            return true;
        }


        private async Task CopyDirectory(string strSource, string strDestination)
        {
            if (!System.IO.Directory.Exists(strDestination))
            {
                Directory.CreateDirectory(strDestination);
            }

            DirectoryInfo dirInfo = new DirectoryInfo(strSource);
            FileInfo[] files = dirInfo.GetFiles();
            foreach (FileInfo tempfile in files)
            {
                tempfile.CopyTo(Path.Combine(strDestination, tempfile.Name));
            }

            DirectoryInfo[] directories = dirInfo.GetDirectories();
            foreach (DirectoryInfo tempdir in directories)
            {
                await this.CopyDirectory(Path.Combine(strSource, tempdir.Name), Path.Combine(strDestination, tempdir.Name));
            }
        }

        private string GetLastBackupFolder()
        {
            string strSource = Path.Combine(this.localBackupStore, this.ServicePartition.PartitionInfo.Id.ToString());


            DirectoryInfo dirInfo = new DirectoryInfo(strSource);
            {
                foreach (DirectoryInfo tempDir in dirInfo.GetDirectories().OrderByDescending(x => x.LastWriteTime))
                {
                    ServiceEventSource.Current.ServiceMessage(
                        this,
                        "tempDir is " + this.localBackupStore + this.ServicePartition.PartitionInfo.Id + @"\" + tempDir);
                    return (this.localBackupStore + this.ServicePartition.PartitionInfo.Id + @"\" + tempDir);
                }
            }

            return null;
        }

        private void DeleteBackups(string strSource, int keepBehind)
        {
            if (!Directory.Exists(strSource))
            {
                //Nothing to delete; Backups may not even have been created for the partition
                return;
            }

            System.IO.DirectoryInfo dirInfo = new DirectoryInfo(strSource);

            using (ITransaction txn = this.StateManager.CreateTransaction())
            {
                foreach (DirectoryInfo tempDir in dirInfo.GetDirectories().OrderByDescending(x => x.LastWriteTime).Skip(keepBehind))
                {
                    tempDir.Delete(true);
                }
            }

            return;
        }

        private async Task<bool> BackupCallbackAsync(BackupInfo backupInfo)
        {
            string backupId = Guid.NewGuid().ToString();


            long totalBackupCount;


            IReliableDictionary<int, long> countDictionary = await this.StateManager.GetOrAddAsync<IReliableDictionary<int, long>>(this.countDictionaryName);
            using (ITransaction txn = this.StateManager.CreateTransaction())
            {
                long count = await countDictionary.AddOrUpdateAsync(txn, 0, 0, (key, oldValue) => { return oldValue + 1; });

                totalBackupCount = count;
                await txn.CommitAsync();
            }

            ServiceEventSource.Current.ServiceMessage(this, "Backup count dictionary updated: " + totalBackupCount);
            ServiceEventSource.Current.Message("Backup count dictionary updated: " + totalBackupCount);

            if ((totalBackupCount%20) == 0)
            {
                //The following limits the number of backups stored to 20 per partition. The actual max might be more than 20 per partition since more backups 
                //could have been created when deletion was taking place. 
                //Also depending on the backup that was restored, the count of backups could be a lot larger.
                this.DeleteBackups(Path.Combine(this.localBackupStore, this.ServicePartition.PartitionInfo.Id.ToString()), 5);
            }

            //Simulate a restore/data loss event randomly. This assumes that all partitions have some state at this point. 
            //Five inventory items must be added for all five partitions to have state.
            if ((totalBackupCount > 19) && (DateTime.Now.Second%20) == 0)
            {
                CancellationToken cancellationToken = default(CancellationToken);

                ServiceEventSource.Current.ServiceMessage(this, "Restore Started");

                using (FabricClient fabricClient = new FabricClient())
                {
                    PartitionSelector partitionSelector = PartitionSelector.PartitionIdOf(
                        this.ServiceInitializationParameters.ServiceName,
                        this.ServiceInitializationParameters.PartitionId);

                    await fabricClient.ServiceManager.InvokeDataLossAsync(partitionSelector, DataLossMode.PartialDataLoss, cancellationToken);
                }
            }

            await
                this.CopyBackupFolderAsync(backupInfo.Directory, this.ServicePartition.PartitionInfo.Id.ToString(), backupId, CancellationToken.None);

            return true;
        }


        private async Task InitializeAsync(CancellationToken cancellationToken)
        {
            this.backupStore = new BackupStore(
                this.blobContainerEndpoint,
                this.storageCredentials,
                this.ServicePartition,
                this.ServiceInitializationParameters);
            try
            {
                await this.backupStore.InitializeAsync(cancellationToken);
            }
            catch (Exception e)
            {
                ServiceEventSource.Current.ServiceMessage(this, "{0} {1}" + e.GetType() + e.Message);
            }
        }

        private static void PrintInventoryItems(IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems)
        {
            ServiceEventSource.Current.Message("PRINTING INVENTORY");
            Dictionary<KeyValuePair<InventoryItemId, InventoryItem>, KeyValuePair<InventoryItemId, InventoryItem>> items = inventoryItems.ToDictionary(v => v);

            foreach (KeyValuePair<KeyValuePair<InventoryItemId, InventoryItem>, KeyValuePair<InventoryItemId, InventoryItem>> tempitem in items)
            {
                ServiceEventSource.Current.Message("ID:{0}|Item:{1}", tempitem.Key, tempitem.Value);
            }
        }

        private async Task PeriodicOldMessageTrimming(CancellationToken cancellationToken)
        {
            IReliableDictionary<CustomerOrderActorMessageId, DateTime> recentRequests =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, DateTime>>(ActorMessageDictionaryName);

            IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>> requestHistory =
                await
                    this.stateManager.GetOrAddAsync<IReliableDictionary<CustomerOrderActorMessageId, Tuple<InventoryItemId, int>>>(RequestHistoryDictionaryName);

            while (!cancellationToken.IsCancellationRequested)
            {
                using (ITransaction tx = this.stateManager.CreateTransaction())
                {
                    foreach (KeyValuePair<CustomerOrderActorMessageId, DateTime> request in recentRequests)
                    {
                        //if we have a record of a message that is older than 2 hours from current time, then remove that record
                        //from both of the stale message tracking dictionaries.
                        if (request.Value < (DateTime.UtcNow.AddHours(-2)))
                        {
                            await recentRequests.TryRemoveAsync(tx, request.Key);
                            await requestHistory.TryRemoveAsync(tx, request.Key);
                        }
                    }

                    await tx.CommitAsync();
                }

                //sleep for 5 minutes then scan again
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
        }

        private async Task PeriodicInventoryCheck(CancellationToken cancellationToken)
        {
            IReliableDictionary<InventoryItemId, InventoryItem> inventoryItems =
                await this.stateManager.GetOrAddAsync<IReliableDictionary<InventoryItemId, InventoryItem>>(InventoryItemDictionaryName);
            if (this.storageType == StorageTypes.Azure)
            {
                await this.InitializeAsync(cancellationToken);
            }

            while (!cancellationToken.IsCancellationRequested)
            {
                ServiceEventSource.Current.ServiceMessage(this, "Checking inventory stock for {0} items.", await inventoryItems.GetCountAsync());

                foreach (InventoryItem item in inventoryItems.Select(x => x.Value))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        //Check if stock is below restockThreshold and if the item is not already on reorder
                        if ((item.AvailableStock <= item.RestockThreshold) && !item.OnReorder)
                        {
                            ServiceUriBuilder builder = new ServiceUriBuilder(RestockRequestManagerServiceName);

                            IRestockRequestManager restockRequestManagerClient = ServiceProxy.Create<IRestockRequestManager>(0, builder.ToUri());

                            // we reduce the quantity passed in to RestockRequest to ensure we don't overorder   
                            RestockRequest newRequest = new RestockRequest(item.Id, (item.MaxStockThreshold - item.AvailableStock));

                            InventoryItem updatedItem = new InventoryItem(
                                item.Description,
                                item.Price,
                                item.AvailableStock,
                                item.RestockThreshold,
                                item.MaxStockThreshold,
                                item.Id,
                                true);

                            // TODO: this call needs to be idempotent in case we fail to update the InventoryItem after this completes.
                            await restockRequestManagerClient.AddRestockRequestAsync(newRequest);

                            // Write operations take an exclusive lock on an item, which means we can't do anything else with that item while the transaction is open.
                            // If something blocks before the transaction is committed, the open transaction on the item will prevent all operations on it, including reads.
                            // Once the transaction commits, the lock is released and other operations on the item can proceed.
                            // Operations on the transaction all have timeouts to prevent deadlocking an item, 
                            // but we should do as little work inside the transaction as possible that is not related to the transaction itself.
                            using (ITransaction tx = this.stateManager.CreateTransaction())
                            {
                                await inventoryItems.TryUpdateAsync(tx, item.Id, updatedItem, item);

                                await tx.CommitAsync();
                            }

                            ServiceEventSource.Current.ServiceMessage(
                                this,
                                "Restock order placed. Item ID: {0}. Quantity: {1}",
                                newRequest.ItemId,
                                newRequest.Quantity);
                        }
                    }
                    catch (Exception e)
                    {
                        ServiceEventSource.Current.ServiceMessage(this, "Failed to place restock order for item {0}. {1}", item.Id, e.ToString());
                    }

                    if (this.storageType == StorageTypes.Local)
                    {
                        await this.StateManager.BackupAsync(this.BackupCallbackAsync);
                    }
                    else
                    {
                        await this.StateManager.BackupAsync(this.BackupCallbackAzureAsync);
                    }
                }

                await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            }
        }

        private enum StorageTypes
        {
            Azure,
            Local
        };
    }
}