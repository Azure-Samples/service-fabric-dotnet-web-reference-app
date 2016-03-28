// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Inventory.Service
{
    using System;
    using System.Collections.Generic;
    using System.Fabric.Description;
    using System.IO;
    using System.IO.Compression;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Data;

    public class DiskBackupManager : IBackupStore
    {
        private string PartitionArchiveFolder;
        private string PartitionTempDirectory;
        private long backupFrequencyInSeconds;
        private int MaxBackupsToKeep;
        private long keyMin;
        private long keyMax;

        public DiskBackupManager(ConfigurationSection configSection, string partitionId, long keymin, long keymax, string codePackageTempDirectory)
        {
            this.keyMin = keymin;
            this.keyMax = keymax;

            string BackupArchivalPath = configSection.Parameters["BackupArchivalPath"].Value;
            this.backupFrequencyInSeconds = long.Parse(configSection.Parameters["BackupFrequencyInSeconds"].Value);
            this.MaxBackupsToKeep = int.Parse(configSection.Parameters["MaxBackupsToKeep"].Value);

            this.PartitionArchiveFolder = Path.Combine(BackupArchivalPath, "Backups", partitionId);
            this.PartitionTempDirectory = Path.Combine(codePackageTempDirectory, partitionId);

            ServiceEventSource.Current.Message(
                "DiskBackupManager constructed IntervalinSec:{0}, archivePath:{1}, tempPath:{2}, backupsToKeep:{3}",
                this.backupFrequencyInSeconds,
                this.PartitionArchiveFolder,
                this.PartitionTempDirectory,
                this.MaxBackupsToKeep);
        }

        long IBackupStore.backupFrequencyInSeconds
        {
            get { return this.backupFrequencyInSeconds; }
        }

        public Task ArchiveBackupAsync(BackupInfo backupInfo, CancellationToken cancellationToken)
        {
            string fullArchiveDirectory = Path.Combine(
                this.PartitionArchiveFolder,
                string.Format("{0}_{1}_{2}", Guid.NewGuid().ToString("N"), this.keyMin, this.keyMax));

            DirectoryInfo dirInfo = new DirectoryInfo(fullArchiveDirectory);
            dirInfo.Create();

            string fullArchivePath = Path.Combine(fullArchiveDirectory, "Backup.zip");

            ZipFile.CreateFromDirectory(backupInfo.Directory, fullArchivePath, CompressionLevel.Fastest, false);

            DirectoryInfo backupDirectory = new DirectoryInfo(backupInfo.Directory);
            backupDirectory.Delete(true);

            return Task.FromResult(true);
        }

        public Task<string> RestoreLatestBackupToTempLocation(CancellationToken cancellationToken)
        {
            ServiceEventSource.Current.Message("Restoring backup to temp source:{0} destination:{1}", this.PartitionArchiveFolder, this.PartitionTempDirectory);

            DirectoryInfo dirInfo = new DirectoryInfo(this.PartitionArchiveFolder);

            string backupZip = dirInfo.GetDirectories().OrderByDescending(x => x.LastWriteTime).First().FullName;

            string zipPath = Path.Combine(backupZip, "Backup.zip");

            ServiceEventSource.Current.Message("latest zip backup is {0}", zipPath);

            DirectoryInfo directoryInfo = new DirectoryInfo(this.PartitionTempDirectory);
            if (directoryInfo.Exists)
            {
                directoryInfo.Delete(true);
            }

            directoryInfo.Create();

            ZipFile.ExtractToDirectory(zipPath, this.PartitionTempDirectory);

            ServiceEventSource.Current.Message("Zip backup {0} extracted to {1}", zipPath, this.PartitionTempDirectory);

            return Task.FromResult<string>(this.PartitionTempDirectory);
        }

        public async Task DeleteBackupsAsync(CancellationToken cancellationToken)
        {
            await Task.Run(
                () =>
                {
                    ServiceEventSource.Current.Message("deleting old backups");

                    if (!Directory.Exists(this.PartitionArchiveFolder))
                    {
                        //Nothing to delete; Backups may not even have been created for the partition
                        return;
                    }

                    DirectoryInfo dirInfo = new DirectoryInfo(this.PartitionArchiveFolder);

                    IEnumerable<DirectoryInfo> oldBackups = dirInfo.GetDirectories().OrderByDescending(x => x.LastWriteTime).Skip(this.MaxBackupsToKeep);

                    foreach (DirectoryInfo oldBackup in oldBackups)
                    {
                        ServiceEventSource.Current.Message("Deleting old backup {0}", oldBackup.FullName);
                        oldBackup.Delete(true);
                    }

                    ServiceEventSource.Current.Message("Old backups deleted");

                    return;
                },
                cancellationToken);
        }
    }
}