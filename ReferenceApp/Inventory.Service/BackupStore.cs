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

    public class BackupStore : IBackupStore
    {
        private readonly CloudBlobClient cloudBlobClient;
        private readonly IStatefulServicePartition servicePartition;
        private readonly StatefulServiceInitializationParameters statefulServiceInitializationParameters;
        private CloudBlobContainer backupBlobContainer;
        private CloudBlockBlob lastBackupBlob;

        private string restoreTempFolder;
        private string targetZipFile;
        private string targetUnzippedFolder;
        private bool pathTooLongExceptionSeen;

        public BackupStore(
            Uri endpoint,
            StorageCredentials credentials,
            IStatefulServicePartition servicePartition,
            StatefulServiceInitializationParameters statefulServiceInitializationParameters)
        {
            this.cloudBlobClient = new CloudBlobClient(endpoint, credentials);
            this.servicePartition = servicePartition;
            this.statefulServiceInitializationParameters = statefulServiceInitializationParameters;

            this.backupBlobContainer = this.GetBackupBlobContainer();
        }


        public async Task DeleteBackupsAzureAsync(CancellationToken cancellationToken)
        {
            if (this.backupBlobContainer.Exists())
            {
                foreach (IListBlobItem item in this.backupBlobContainer.ListBlobs(null, false))
                {
                    CloudBlockBlob theblob = (CloudBlockBlob) item;
                    if (theblob.Properties.LastModified >= DateTime.UtcNow.AddMinutes(-1*3))
                    {
                        await theblob.DeleteAsync();
                    }
                }
            }
        }


        public async Task InitializeAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: InitializeAsync");

            await this.backupBlobContainer.CreateIfNotExistsAsync(cancellationToken);

            ServiceEventSource.Current.Message("BackupStore: InitializeAsync: success.");
        }

        public async Task<bool> CheckIfBackupExistsInShareAsync(CancellationToken cancellationToken)
        {
            bool exists = false;
            BlobResultSegment resultSegment = await this.backupBlobContainer.ListBlobsSegmentedAsync(new BlobContinuationToken());
            while(resultSegment.ContinuationToken != null)
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

        public async Task UploadBackupFolderAsync(string backupFolder, string backupId, CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: UploadBackupFolderAsync: called.");

            string pathToZippedFolder = Path.Combine(
                this.statefulServiceInitializationParameters.CodePackageActivationContext.WorkDirectory,
                backupId + @".zip");
            ZipFile.CreateFromDirectory(backupFolder, pathToZippedFolder);


            CloudBlockBlob blob = this.backupBlobContainer.GetBlockBlobReference(backupId);
            await blob.UploadFromFileAsync(pathToZippedFolder, FileMode.Open, CancellationToken.None);

            ServiceEventSource.Current.Message("BackupStore: UploadBackupFolderAsync: success.");
        }

        public async Task<string> DownloadAnyBackupAsync(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("BackupStore: Download any backup async called.");

            while (true)
            {
                try
                {
                    this.InitializeRestoreFolderStructure(this.pathTooLongExceptionSeen);


                    if (Directory.Exists(this.restoreTempFolder))
                    {
                        Directory.Delete(this.restoreTempFolder);
                    }


                    Directory.CreateDirectory(this.restoreTempFolder);
                    Directory.CreateDirectory(this.targetUnzippedFolder);

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
                catch (PathTooLongException e)
                {
                    this.pathTooLongExceptionSeen = true;

                    ServiceEventSource.Current.Message("BackupStore: DownloadAnyBackupAsync: Exception {0} {1}", e.GetType(), e.Message);
                }
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

        private CloudBlobContainer GetBackupBlobContainer()
        {
            string identifier = this.GetTestPartitionIdentifier();
            return this.cloudBlobClient.GetContainerReference(identifier);
            //return this.cloudBlobClient.GetContainerReference(testId + "-p" + identifier);
        }

        private string GetTestPartitionIdentifier()
        {
            ServicePartitionInformation partitionInfo = this.servicePartition.PartitionInfo;

            return partitionInfo.Id.ToString();
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

        private void InitializeRestoreFolderStructure(bool pathTooLong)
        {
            string tmpFolderPath = null;

            if (false == pathTooLong)
            {
                tmpFolderPath = this.statefulServiceInitializationParameters.CodePackageActivationContext.TempDirectory;
            }
            else
            {
                string[] drives = Environment.GetLogicalDrives();

                if (drives.Contains(@"E:\"))
                {
                    tmpFolderPath = @"E:\temp";
                }
                else if (drives.Contains(@"C:\"))
                {
                    tmpFolderPath = @"C:\temp";
                }
            }

            this.restoreTempFolder = Path.Combine(tmpFolderPath, "R", this.statefulServiceInitializationParameters.PartitionId.ToString("N"));

            this.targetZipFile = Path.Combine(this.restoreTempFolder, "R.zip");

            this.targetUnzippedFolder = Path.Combine(this.restoreTempFolder, "E");

            ServiceEventSource.Current.Message("TmpFolder: {0} {1}", this.restoreTempFolder, this.restoreTempFolder.Count());
            ServiceEventSource.Current.Message("UnzipFolder: {0} {1}", this.targetUnzippedFolder, this.targetUnzippedFolder.Count());
        }
    }
}