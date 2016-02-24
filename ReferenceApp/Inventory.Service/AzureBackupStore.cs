// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Fabric;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Blob;
    using System.Fabric.Description;
    public class AzureBlobBackupManager /*: IBackupStore*/
    {
        private readonly CloudBlobClient cloudBlobClient;
        private CloudBlobContainer backupBlobContainer;
        private CloudBlockBlob lastBackupBlob;

        private string restoreTempFolder;
        private string targetZipFile;
        private string targetUnzippedFolder;

        private CodePackageActivationContext context;
        private string partitionId;

        public long backupFrequencyInMinutes;

        public AzureBlobBackupManager(CodePackageActivationContext context, ConfigurationSection configSection, string partitionId)
        {
            string backupAccountName = configSection.Parameters["BackupAccountName"].Value;
            string backupAccountKey = configSection.Parameters["PrimaryKeyForBackupTestAccount"].Value;
            string blobEndpointAddress = configSection.Parameters["BlobServiceEndpointAddress"].Value;

            this.backupFrequencyInMinutes = long.Parse(configSection.Parameters["BackupFrequencyInSeconds"].Value);

            StorageCredentials storageCredentials = new StorageCredentials(backupAccountName, backupAccountKey);

            this.cloudBlobClient = new CloudBlobClient(new Uri(blobEndpointAddress), storageCredentials);
            this.backupBlobContainer = this.cloudBlobClient.GetContainerReference(this.partitionId);
            this.backupBlobContainer.CreateIfNotExists();

            this.context = context;
            this.partitionId = partitionId;

        }

        public async Task DeleteBackupsAsync(CancellationToken cancellationToken, long maxToKeep)
        {
            if (this.backupBlobContainer.Exists())
            {
                foreach (IListBlobItem item in this.backupBlobContainer.ListBlobs(null, false))
                {
                    CloudBlockBlob theblob = (CloudBlockBlob)item;
                    if (theblob.Properties.LastModified >= DateTime.UtcNow.AddMinutes(-1 * 3))
                    {
                        await theblob.DeleteAsync();
                    }
                }
            }
        }

        public async Task<bool> CheckIfBackupExistsInShareAsync(CancellationToken cancellationToken)
        {
            bool exists = false;
            BlobResultSegment resultSegment = await this.backupBlobContainer.ListBlobsSegmentedAsync(new BlobContinuationToken());
            while (resultSegment.ContinuationToken != null)
            {
                if (resultSegment.Results.Count() > 0)
                {
                    exists = true;
                    break;
                }

                resultSegment = await this.backupBlobContainer.ListBlobsSegmentedAsync(resultSegment.ContinuationToken);
            }

            ServiceEventSource.Current.Message("BackupStore: CheckIfBackupExistsInShareAsync returned " + exists.ToString().ToLowerInvariant());
            return exists;
        }

        public async Task CopyBackupAsync(string backupFolder, string backupId, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: UploadBackupFolderAsync: called.");

            string pathToZippedFolder = Path.Combine(this.context.WorkDirectory, backupId + @".zip");

            ZipFile.CreateFromDirectory(backupFolder, pathToZippedFolder);

            CloudBlockBlob blob = this.backupBlobContainer.GetBlockBlobReference(backupId);
            await blob.UploadFromFileAsync(pathToZippedFolder, FileMode.Open, CancellationToken.None);

            ServiceEventSource.Current.Message("BackupStore: UploadBackupFolderAsync: success.");
        }

        public async Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: Download any backup async called.");

            cancellationToken.ThrowIfCancellationRequested();

            this.lastBackupBlob = this.GetAnyBackupBlob();

            using (FileStream stream = File.Open(this.targetZipFile, FileMode.CreateNew))
            {
                ServiceEventSource.Current.Message("BackupStore: Downloading {0}", this.lastBackupBlob.Name);

                await this.lastBackupBlob.DownloadToStreamAsync(stream, cancellationToken);

                stream.Position = 0;

                using (ZipArchive zipArchive = new ZipArchive(stream))
                {
                    zipArchive.ExtractToDirectory(this.targetUnzippedFolder);
                }

                ServiceEventSource.Current.Message("BackupStore: Downloaded {0} in to {1}", this.lastBackupBlob.Name, this.targetUnzippedFolder);

                return this.targetUnzippedFolder;
            }
        }

        public async Task DeleteLastDownloadedBackupAsync(System.Threading.CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: Deleting last downloaded backup.");

            Trace.Assert(this.lastBackupBlob != null);
            Trace.Assert(this.restoreTempFolder != null);
            Trace.Assert(true == await this.lastBackupBlob.ExistsAsync(cancellationToken));

            await this.lastBackupBlob.DeleteAsync(cancellationToken);

            this.lastBackupBlob = null;

            Directory.Delete(this.restoreTempFolder, true);

            ServiceEventSource.Current.Message("BackupStore: Deleted last downloaded backup.");
        }

        public Task DeleteStoreAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: Deleting backup store.");


            return this.backupBlobContainer.DeleteIfExistsAsync(cancellationToken);
        }

        private CloudBlockBlob GetAnyBackupBlob()
        {
            IEnumerable<IListBlobItem> blobs = this.backupBlobContainer.ListBlobs();

            foreach (IListBlobItem blobList in blobs)
            {
                CloudBlockBlob blob = blobList as CloudBlockBlob;

                Trace.Assert(blob != null, "Must be a cloud block blob.");
                Trace.Assert(false == blob.Name.Contains("$"), "No logging folder.");

                ServiceEventSource.Current.Message("BackupStore: GetAnyBackupBlob returns {0}", blob.Name);

                return blob;
            }

            Trace.Assert(false, "There should always be something to restore.");

            return null;
        }
    }
}