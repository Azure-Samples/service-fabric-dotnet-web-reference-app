// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using Microsoft.ServiceFabric.Data;
    using Microsoft.WindowsAzure.Storage.Auth;
    using Microsoft.WindowsAzure.Storage.Blob;
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    public class AzureBlobBackupManager : IBackupStore
    {
        private readonly CloudBlobClient cloudBlobClient;
        private CloudBlobContainer backupBlobContainer;
        private int MaxBackupsToKeep;

        private string PartitionTempDirectory;
        private string partitionId;

        private long backupFrequencyInSeconds;
        private long keyMin;
        private long keyMax;

        long IBackupStore.backupFrequencyInSeconds
        {
            get
            {
                return backupFrequencyInSeconds;
            }
        }

        public AzureBlobBackupManager(ConfigurationSection configSection, string partitionId, long keymin, long keymax, string codePackageTempDirectory)
        {
            this.keyMin = keymin;
            this.keyMax = keymax;

            string backupAccountName = configSection.Parameters["BackupAccountName"].Value;
            string backupAccountKey = configSection.Parameters["PrimaryKeyForBackupTestAccount"].Value;
            string blobEndpointAddress = configSection.Parameters["BlobServiceEndpointAddress"].Value;

            this.backupFrequencyInSeconds = long.Parse(configSection.Parameters["BackupFrequencyInSeconds"].Value);
            this.MaxBackupsToKeep = int.Parse(configSection.Parameters["MaxBackupsToKeep"].Value);
            this.partitionId = partitionId;
            this.PartitionTempDirectory = Path.Combine(codePackageTempDirectory, partitionId);

            StorageCredentials storageCredentials = new StorageCredentials(backupAccountName, backupAccountKey);
            this.cloudBlobClient = new CloudBlobClient(new Uri(blobEndpointAddress), storageCredentials);
            this.backupBlobContainer = this.cloudBlobClient.GetContainerReference(this.partitionId);
            this.backupBlobContainer.CreateIfNotExists();
        }

        public async Task ArchiveBackupAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("AzureBlobBackupManager: Archive Called.");

            string fullArchiveDirectory = Path.Combine(this.PartitionTempDirectory, Guid.NewGuid().ToString("N"));

            DirectoryInfo fullArchiveDirectoryInfo = new DirectoryInfo(fullArchiveDirectory);
            fullArchiveDirectoryInfo.Create();

            string blobName = string.Format("{0}_{1}_{2}_{3}", Guid.NewGuid().ToString("N"), this.keyMin, this.keyMax, "Backup.zip");
            string fullArchivePath = Path.Combine(fullArchiveDirectory, "Backup.zip");

            ZipFile.CreateFromDirectory(backupInfo.Directory, fullArchivePath, CompressionLevel.Fastest, false);

            DirectoryInfo backupDirectory = new DirectoryInfo(backupInfo.Directory);
            backupDirectory.Delete(true);

            CloudBlockBlob blob = this.backupBlobContainer.GetBlockBlobReference(blobName);
            await blob.UploadFromFileAsync(fullArchivePath, FileMode.Open, CancellationToken.None);

            DirectoryInfo tempDirectory = new DirectoryInfo(fullArchiveDirectory);
            tempDirectory.Delete(true);

            ServiceEventSource.Current.Message("AzureBlobBackupManager: UploadBackupFolderAsync: success.");
        }

        public async Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("AzureBlobBackupManager: Download backup async called.");

            var lastBackupBlob = (await this.GetBackupBlobs(true)).First();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloading {0}", lastBackupBlob.Name);

            string downloadId = Guid.NewGuid().ToString("N");

            string zipPath = Path.Combine(this.PartitionTempDirectory, string.Format("{0}_Backup.zip", downloadId));

            lastBackupBlob.DownloadToFile(zipPath, FileMode.CreateNew);

            var restorePath = Path.Combine(this.PartitionTempDirectory, downloadId);

            ZipFile.ExtractToDirectory(zipPath, restorePath);

            FileInfo zipInfo = new FileInfo(zipPath);
            zipInfo.Delete();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Downloaded {0} in to {1}", lastBackupBlob.Name, restorePath);

            return restorePath;
        }

        public async Task DeleteBackupsAsync(CancellationToken cancellationToken)
        {
            if (this.backupBlobContainer.Exists())
            {
                ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting old backups");

                var oldBackups = (await GetBackupBlobs(true)).Skip(this.MaxBackupsToKeep);

                foreach (var backup in oldBackups)
                {
                    ServiceEventSource.Current.Message("AzureBlobBackupManager: Deleting {0}", backup.Name);
                    await backup.DeleteAsync(cancellationToken);
                }
            }
        }

        private async Task<IEnumerable<CloudBlockBlob>> GetBackupBlobs(bool sorted)
        {
            IEnumerable<IListBlobItem> blobs = this.backupBlobContainer.ListBlobs();

            ServiceEventSource.Current.Message("AzureBlobBackupManager: Got {0} blobs", blobs.Count());

            List<CloudBlockBlob> itemizedBlobs = new List<CloudBlockBlob>();

            foreach (CloudBlockBlob cbb in blobs)
            {
                await cbb.FetchAttributesAsync();
                itemizedBlobs.Add(cbb);

            }

            if (sorted)
            {
                return itemizedBlobs.OrderByDescending(x => x.Properties.LastModified);
            }
            else
            {
                return itemizedBlobs;
            }
        }
    }
}