using LibBundle3.Records;
using LibBundledGGPK3;

// Siosa.Extractor — Content.ggpk 를 읽기 전용으로 열어 필요한 파일만 꺼낸다.
// 사용법:
//   Siosa.Extractor list <ggpk> <prefix>            경로 목록 출력
//   Siosa.Extractor dump <ggpk> <outDir> <path...>  지정 파일 추출

if (args.Length < 2)
{
    Console.WriteLine("usage: list <ggpk> <prefix> | dump <ggpk> <outDir> <path...>");
    return 1;
}

var mode = args[0];
var ggpkPath = args[1];

// FileAccess.Read 로 열어서 GGPK 를 절대 수정할 수 없게 한다.
using var fs = new FileStream(ggpkPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
using var ggpk = new BundledGGPK(fs, false, true);
var index = ggpk.Index;

if (mode == "list")
{
    var prefix = args.Length > 2 ? args[2].ToLowerInvariant() : "";
    foreach (var f in index.Files.Values)
        if (f.Path != null && f.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            Console.WriteLine($"{f.Size}\t{f.Path}");
    return 0;
}

if (mode == "dump")
{
    var outDir = args[2];
    foreach (var p in args.Skip(3))
    {
        if (!index.TryGetFile(p, out FileRecord rec))
        {
            Console.Error.WriteLine($"not found: {p}");
            continue;
        }
        var data = rec.Read();
        var dst = Path.Combine(outDir, p.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(dst));
        File.WriteAllBytes(dst, data.ToArray());
        Console.WriteLine($"{data.Length}\t{p}");
    }
    return 0;
}

Console.Error.WriteLine($"unknown mode: {mode}");
return 1;
