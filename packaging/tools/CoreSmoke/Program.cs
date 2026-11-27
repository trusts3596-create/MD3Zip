using MD3Zip.Core;
using MD3Zip.Core.Models;

// MD3Zip.Core 冒烟测试：验证 Compress / Test / Update / Delete / Hash / 单文件格式。
var sevenZip = Path.Combine(@"D:\wb\Assets\7z", "7z.exe");
var service = new SevenZipService(sevenZip);
Console.WriteLine($"7z: {service.ExecutablePath} 可用={service.IsAvailable}");

if (!service.IsAvailable)
{
    Console.WriteLine("!! 找不到 7z.exe，终止");
    return 1;
}

var root = Path.Combine(Path.GetTempPath(), "md3zip-smoke-" + Guid.NewGuid().ToString("N")[..8]);
Directory.CreateDirectory(root);
var f1 = Path.Combine(root, "a.txt");
var f2 = Path.Combine(root, "b.bin");
var f3 = Path.Combine(root, "c.log");
await File.WriteAllTextAsync(f1, "hello md3zip " + new string('x', 1000));
await File.WriteAllBytesAsync(f2, new byte[2048]);
await File.WriteAllTextAsync(f3, "log-content");

var archive = Path.Combine(root, "test.7z");
var failures = 0;

void Check(string label, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {label}: {detail}");
    if (!ok) failures++;
}

// 1. 压缩
var compress = await service.CompressAsync(new CompressionRequest
{
    Sources = [f1, f2],
    OutputPath = archive,
    Format = ArchiveFormat.SevenZip,
    Level = CompressionLevel.Normal
});
Check("压缩", compress.Succeeded && File.Exists(archive), $"{compress.Status} / {compress.Message}");

// 2. 测试完整性
var test = await service.TestAsync(archive);
Check("测试", test.Succeeded, $"{test.Status} / {test.Message}");

// 3. 追加
var update = await service.UpdateAsync(new UpdateRequest { ArchivePath = archive, Sources = [f3] });
var afterUpdate = await service.ListAsync(archive);
Check("追加", update.Succeeded && afterUpdate.Listing?.Entries.Count >= 3,
    $"{update.Status} / 条目={afterUpdate.Listing?.Entries.Count}");

// 4. 删除
var delete = await service.DeleteEntriesAsync(archive, ["c.log"]);
var afterDelete = await service.ListAsync(archive);
var hasLog = afterDelete.Listing?.Entries.Any(e => e.Name.Equals("c.log", StringComparison.OrdinalIgnoreCase)) ?? true;
Check("删除", delete.Succeeded && !hasLog,
    $"{delete.Status} / 条目={afterDelete.Listing?.Entries.Count} / c.log 仍存在={hasLog}");

// 5. 校验和
var sha = await service.ComputeHashesAsync(archive, HashAlgorithmKind.Sha256);
Check("SHA-256", sha.Succeeded && sha.Hash?.Length == 64, $"{sha.Hash}");
var crc = await service.ComputeHashesAsync(archive, HashAlgorithmKind.Crc32);
Check("CRC32", crc.Succeeded && crc.Hash?.Length == 8, $"{crc.Hash}");

// 6. gzip 单文件
var gzPath = Path.Combine(root, "a.txt.gz");
var gzip = await service.CompressAsync(new CompressionRequest
{
    Sources = [f1],
    OutputPath = gzPath,
    Format = ArchiveFormat.GZip
});
Check("gzip 单文件", gzip.Succeeded && File.Exists(gzPath), $"{gzip.Status} / {gzip.Message}");

// 7. gzip 拒绝多文件
var gzipMulti = await service.CompressAsync(new CompressionRequest
{
    Sources = [f1, f2],
    OutputPath = Path.Combine(root, "multi.gz"),
    Format = ArchiveFormat.GZip
});
Check("gzip 拒绝多文件", !gzipMulti.Succeeded, gzipMulti.Message);

// 8. 覆盖策略：先解压一次，再以 Skip 模式解压
var outDir = Path.Combine(root, "out");
var firstExtract = await service.ExtractAsync(new ExtractionRequest
{
    ArchivePath = archive,
    TargetDirectory = outDir
});
var skipExtract = await service.ExtractAsync(new ExtractionRequest
{
    ArchivePath = archive,
    TargetDirectory = outDir,
    OverwriteMode = ExtractionOverwriteMode.Skip
});
Check("覆盖策略(-aos)", firstExtract.Succeeded && skipExtract.Succeeded,
    $"{firstExtract.Status} / {skipExtract.Status}");

try { Directory.Delete(root, true); } catch { }

Console.WriteLine(failures == 0 ? "=== 全部通过 ===" : $"=== {failures} 项失败 ===");
return failures == 0 ? 0 : 1;
