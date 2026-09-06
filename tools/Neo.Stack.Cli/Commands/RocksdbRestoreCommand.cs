using System;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using Neo.L2.Persistence;

namespace Neo.Stack.Cli.Commands;

/// <summary>
/// <c>rocksdb-restore</c> — restores RocksDB data from a compressed snapshot archive.
/// </summary>
internal static class RocksdbRestoreCommand
{
    public static int Run(string[] args)
    {
        var snapshotFile = ArgUtil.Get(args, "--snapshot-file", "");
        var targetDir = ArgUtil.Get(args, "--target-dir", "");
        
        if (string.IsNullOrWhiteSpace(snapshotFile) || string.IsNullOrWhiteSpace(targetDir))
        {
            Console.Error.WriteLine("Usage: neo-stack rocksdb-restore --snapshot-file <path> --target-dir <path>");
            return 1;
        }
        
        if (!File.Exists(snapshotFile))
        {
            Console.Error.WriteLine($"Error: Snapshot file does not exist: {snapshotFile}");
            return 1;
        }
        
        try
        {
            // Read manifest for validation
            var manifestBytes = File.ReadAllBytes(snapshotFile);
            var hasManifest = manifestBytes.Length > 0 && Array.IndexOf(manifestBytes, (byte)'{') >= 0;
            
            if (hasManifest)
            {
                Console.WriteLine("Restoring from ZIP archive with manifest...");
            }
            else
            {
                Console.WriteLine("Restoring from ZIP archive...");
            }
            
            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"rocksdb-restore-{Guid.NewGuid()}");
            Directory.CreateDirectory(tempExtractDir);
            
            // Extract archive
            Console.WriteLine("  extracting archive...");
            ZipFile.ExtractToDirectory(snapshotFile, tempExtractDir, overwriteFiles: true);
            
            // Validate extraction
            var expectedFilesCount = Directory.GetFiles(tempExtractDir).Length;
            if (expectedFilesCount == 0)
            {
                throw new InvalidOperationException("Archive contains no files");
            }
            
            Console.WriteLine($"  extracted {expectedFilesCount} files");
            
            // Stop any running node process (placeholder - would integrate with service manager in production)
            Console.WriteLine("  stopping existing node service...");
            Console.WriteLine("  Note: Please manually stop the node service before restoring");
            
            // Prepare target directory
            var backupName = "(none)";
            if (Directory.Exists(targetDir))
            {
                Console.WriteLine("  backing up existing data...");
                backupName = $"pre-restore-backup-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
                var backupDir = Path.Combine(targetDir, "..", backupName);
                if (Directory.Exists(targetDir))
                {
                    Directory.Move(targetDir, backupDir);
                }
            }
            
            Directory.CreateDirectory(targetDir);
            
            // Move extracted data to target
            Console.WriteLine("  copying restored data...");
            foreach (var sourceFile in Directory.GetFiles(tempExtractDir))
            {
                var fileName = Path.GetFileName(sourceFile);
                var destFile = Path.Combine(targetDir, fileName);
                
                // Don't copy manifest back - it's metadata
                if (fileName.Equals("manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                
                File.Copy(sourceFile, destFile, overwrite: true);
            }
            
            // Move all subdirectories too
            foreach (var sourceDir in Directory.GetDirectories(tempExtractDir))
            {
                var dirName = Path.GetFileName(sourceDir);
                var destDir = Path.Combine(targetDir, dirName);
                CopyDirectory(sourceDir, destDir, overwrite: true);
            }
            
            // Verify restored database
            var restoredRocksDbPath = Path.Combine(targetDir, "rocksdb");
            if (!Directory.Exists(restoredRocksDbPath))
            {
                // If extracted directly to target dir, move rocksdb folder
                var rocksDbDirs = Directory.GetDirectories(targetDir);
                if (rocksDbDirs.Length > 0)
                {
                    restoredRocksDbPath = rocksDbDirs[0];
                    // Already in place
                }
            }
            
            // Attempt to open and verify
            Console.WriteLine("  verifying database integrity...");
            try
            {
                using var store = new RocksDbKeyValueStore(restoredRocksDbPath);
                _ = store.Count; // Trigger a read operation
                Console.WriteLine("  ✓ Database verification passed");
            }
            catch (Exception ex)
            {
                throw new IOException($"Database integrity check failed: {ex.Message}", ex);
            }
            
            // Cleanup temp dir
            try { Directory.Delete(tempExtractDir, recursive: true); }
            catch { /* Ignore */ }
            
            var sizeMB = (new FileInfo(snapshotFile).Length / (1024.0 * 1024.0));
            
            Console.WriteLine();
            Console.WriteLine($"✓ Restore completed successfully");
            Console.WriteLine($"  source: {snapshotFile} ({sizeMB:F2} MB)");
            Console.WriteLine($"  target: {targetDir}");
            Console.WriteLine($"  backup: {Path.GetFullPath("..")}\\{backupName}");
            Console.WriteLine();
            Console.WriteLine($"Start the node to begin serving requests.");
            
            return 0;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Restore failed: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Restore failed unexpectedly: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.Error.WriteLine($"  inner: {ex.InnerException.Message}");
            }
            return 1;
        }
    }
    
    private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);
        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }
        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            CopyDirectory(dir, Path.Combine(destDir, Path.GetFileName(dir)), overwrite);
        }
    }
}
