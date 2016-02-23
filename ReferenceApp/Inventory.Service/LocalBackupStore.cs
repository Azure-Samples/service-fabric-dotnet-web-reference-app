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
    public class LocalBackupManager : IBackupStore
    {
        private CodePackageActivationContext context;
        private string partitionId;
        private string restoreTempFolder;
        private string targetZipFile;
        private string targetUnzippedFolder;
        private bool pathTooLongExceptionSeen;

        public long backupFrequencyInMinutes;

        public LocalBackupManager(CodePackageActivationContext context, ConfigurationSection configSection, string partitionId)
        {
            this.context = context;
            this.partitionId = partitionId;
            string backupSettingValue = configSection.Parameters["LocalBackupTempPath"].Value;
            this.backupFrequencyInMinutes = long.Parse(configSection.Parameters["BackupFrequencyInMinutes"].Value);
            string tmpFolderPath = this.context.TempDirectory;

            this.restoreTempFolder = Path.Combine(tmpFolderPath, "Restore", this.partitionId);

            this.targetZipFile = Path.Combine(this.restoreTempFolder, "Restore.zip");

            this.targetUnzippedFolder = Path.Combine(this.restoreTempFolder, "Extracted");

            ServiceEventSource.Current.Message("TmpFolder: {0} {1}", this.restoreTempFolder, this.restoreTempFolder.Count());
            ServiceEventSource.Current.Message("UnzipFolder: {0} {1}", this.targetUnzippedFolder, this.targetUnzippedFolder.Count());
        }

        public Task<string> GetLastBackup(CancellationToken cancellationToken)
        {
            string strSource = Path.Combine(this.localBackupStore, this.partitionId);


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

        public async Task CopyBackupAsync(string backupFolder, string backupId, CancellationToken cancellationToken)
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

            string pathToFolder = Path.Combine(this.localBackupStore, partitionId, backupId);
            ServiceEventSource.Current.ServiceMessage(this, "Backup Folder Path " + pathToFolder);

            await this.CopyDirectory(backupFolder, pathToFolder);

            //var blob = this.backupBlobContainer.GetBlockBlobReference(backupId);
            //await blob.UploadFromFileAsync(pathToZippedFolder, FileMode.Open, CancellationToken.None);
            //Service.WriteTrace("BackupStore: UploadBackupFolderAsync: success.");
        }

        public async Task DeleteBackupsAsync(CancellationToken cancellationToken, long maxToKeep)
        {
            if (!Directory.Exists(strSource))
            {
                //Nothing to delete; Backups may not even have been created for the partition
                return;
            }

            System.IO.DirectoryInfo dirInfo = new DirectoryInfo(strSource);

            foreach (DirectoryInfo tempDir in dirInfo.GetDirectories().OrderByDescending(x => x.LastWriteTime).Skip(keepBehind))
            {
                tempDir.Delete(true);
            }

            return;
        }
    }
}