using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Neo.L2.Persistence;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>rocksdb-backup</c> — creates compressed snapshot backups of RocksDB data directories.
/// </summary>
internal static class RocksdbBackupCommand
{
    private sealed class BackupManifest
    {
        public string Version => "1.0";
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
        public string SourceDirectory { get; set; } = null!;
        public long BlockHeight { get; set; }
        public string ArchivePath { get; set; } = null!;
        public int RetentionDays { get; set; } = 30;
    }

    public static int Run(string[] args)
    {
        var nodeDataDir = ArgUtil.Get(args, "--node-data-dir", "");
        var backupDir = ArgUtil.Get(args, "--backup-dir", "./backups");
        var retentionDaysArg = ArgUtil.Get(args, "--retention-days", "30");
        
        if (!uint.TryParse(retentionDaysArg, out var retentionDays))
        {
            Console.Error.WriteLine($"--retention-days must be a non-negative integer, got '{retentionDaysArg}'");
            return 1;
        }
        var retentionDaysInt = (int)Math.Min(retentionDays, int.MaxValue);
        
        if (string.IsNullOrWhiteSpace(nodeDataDir) || string.IsNullOrWhiteSpace(backupDir))
        {
            Console.Error.WriteLine("Usage: neo-stack rocksdb-backup --node-data-dir <path> --backup-dir <path> [--retention-days <N>]");
            return 1;
        }
        
        if (!Directory.Exists(nodeDataDir))
        {
            Console.Error.WriteLine($"Error: Data directory does not exist: {nodeDataDir}");
            return 1;
        }
        
        var rocksDbPath = Path.Combine(nodeDataDir, "rocksdb");
        if (!Directory.Exists(rocksDbPath))
        {
            Console.Error.WriteLine($"Error: RocksDB directory not found at {rocksDbPath}");
            return 1;
        }
        
        var tempSnapshotDir = Path.Combine(Path.GetTempPath(), $"rocksdb-snapshot-{Guid.NewGuid()}");
        try
        {
            Directory.CreateDirectory(backupDir);
            Directory.CreateDirectory(tempSnapshotDir);
            
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var archiveName = $"backup-{timestamp}.zip";
            var archivePath = Path.Combine(backupDir, archiveName);
            
            Console.WriteLine("Creating RocksDB backup...");
            Console.WriteLine($"  source: {rocksDbPath}");
            Console.WriteLine($"  destination: {archivePath}");
            
            // Create snapshot using RocksDbKeyValueStore
            using var store = new RocksDbKeyValueStore(rocksDbPath);
            
            // Step 1: Copy files manually (since RocksDbSharp doesn't expose checkpoint)
            Console.WriteLine("  creating snapshot copy...");
            foreach (var file in Directory.GetFiles(rocksDbPath))
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.StartsWith("CURRENT") && 
                    !fileName.StartsWith("LOCK") && 
                    !fileName.StartsWith("LOG") &&
                    !fileName.StartsWith("MANIFEST"))
                {
                    var destFile = Path.Combine(tempSnapshotDir, fileName);
                    File.Copy(file, destFile, overwrite: true);
                }
            }
            
            // Also copy manifest files
            var manifestFiles = Directory.GetFiles(rocksDbPath, "CURRENT*")
                .Concat(Directory.GetFiles(rocksDbPath, "MANIFEST*"))
                .Distinct();
            foreach (var file in manifestFiles)
            {
                File.Copy(file, Path.Combine(tempSnapshotDir, Path.GetFileName(file)), overwrite: true);
            }
            
            // Step 2: Trigger minor compaction
            Console.WriteLine("  triggering compaction...");
            store.CompactRangeAsync().GetAwaiter().GetResult();
            
            // Step 3: Verify consistency
            Console.WriteLine("  verifying integrity...");
            store.VerifyConsistencyAsync().GetAwaiter().GetResult();
            
            // Step 4: Compress to zip
            Console.WriteLine("  compressing archive...");
            ZipFile.CreateFromDirectory(tempSnapshotDir, archivePath, CompressionLevel.Optimal, false);
            
            // Create manifest
            var manifest = new BackupManifest
            {
                SourceDirectory = Path.GetFullPath(rocksDbPath),
                ArchivePath = archivePath,
                RetentionDays = retentionDaysInt
            };
            var manifestJson = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(backupDir, "manifest.json"), manifestJson);
            
            var size = new FileInfo(archivePath).Length / 1024.0;
            
            Console.WriteLine($"✓ Backup completed successfully");
            Console.WriteLine($"  archive: {archivePath} ({size:F2} KB)");
            Console.WriteLine($"  retention: {retentionDaysInt} days");
            Console.WriteLine();
            Console.WriteLine($"Restore with: neo-stack rocksdb-restore --snapshot-file {archivePath} --target-dir <path>");
            
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Backup failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"  inner: {ex.InnerException.Message}");
            }
            return 1;
        }
        finally
        {
            try { if (Directory.Exists(tempSnapshotDir)) Directory.Delete(tempSnapshotDir, recursive: true); }
            catch { /* Ignore cleanup */ }
        }
    }
}
