using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Siosa
{
    // PoE1 클라이언트 로그(KakaoClient.txt / Client.txt) 위치를 자동으로 찾는다.
    //   1) 실행 중인 게임 프로세스의 폴더
    //   2) 설치 정보(레지스트리: 카카오 언인스톨 키, GGG, Steam)
    //   3) 흔한 설치 경로
    // 여러 개가 나오면 가장 최근에 기록된 로그를 고른다.
    public static class LogDetect
    {
        static readonly string[] LogNames = { "KakaoClient.txt", "Client.txt" };

        //   (게임 프로세스 경로 조회는 가장 낮은 권한이고 메모리는 읽지 않지만, 보안 모듈이 있는 게임이라 필요할 때만 한다)
        public static string Find() => Newest(Candidates()) ?? Newest(RunningGameDirs());

        static string Newest(IEnumerable<string> dirs) =>
            dirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase)
                .SelectMany(dir => LogNames.Select(n => Path.Combine(dir, "logs", n)))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

        static IEnumerable<string> Candidates()
        {
            var dirs = new List<string>();
            dirs.AddRange(RegistryDirs());
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                var r = drive.RootDirectory.FullName;
                dirs.Add(Path.Combine(r, @"Kakaogames\Path of Exile"));
                dirs.Add(Path.Combine(r, @"Daum Games\Path of Exile"));
                dirs.Add(Path.Combine(r, @"Program Files (x86)\Grinding Gear Games\Path of Exile"));
                dirs.Add(Path.Combine(r, @"Program Files\Grinding Gear Games\Path of Exile"));
                dirs.Add(Path.Combine(r, @"Program Files (x86)\Steam\steamapps\common\Path of Exile"));
                dirs.Add(Path.Combine(r, @"SteamLibrary\steamapps\common\Path of Exile"));
            }
            return dirs.Where(d => !string.IsNullOrEmpty(d)).Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // PathOfExile_x64_KG.exe, PathOfExile_x64.exe, PathOfExile_x64Steam.exe … (PoE2 는 제외)
        static IEnumerable<string> RunningGameDirs()
        {
            foreach (var p in Process.GetProcesses())
            {
                string dir = null;
                try
                {
                    if (p.ProcessName.StartsWith("PathOfExile", StringComparison.OrdinalIgnoreCase) &&
                        !p.ProcessName.StartsWith("PathOfExile2", StringComparison.OrdinalIgnoreCase))
                        dir = Path.GetDirectoryName(ImagePath(p.Id));
                }
                catch { }
                finally { p.Dispose(); }
                if (dir != null) yield return dir;
            }
        }

        // 관리자 권한으로 실행된 게임이어도 읽을 수 있는 방식 (PROCESS_QUERY_LIMITED_INFORMATION)
        static string ImagePath(int pid)
        {
            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return Native.QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { Native.CloseHandle(h); }
        }

        static IEnumerable<string> RegistryDirs()
        {
            var list = new List<string>();
            void TryAdd(Func<string> f) { try { var v = f(); if (!string.IsNullOrEmpty(v)) list.Add(v); } catch { } }

            // 카카오 런처 설치 정보 (키 이름 "Path Of Exile", PoE2 는 "Path Of Exile2")
            foreach (var hive in new[] { Registry.LocalMachine, Registry.CurrentUser })
                foreach (var baseKey in new[] { @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall" })
                    TryAdd(() => hive.OpenSubKey(baseKey + @"\Path Of Exile")?.GetValue("InstallLocation") as string);

            // GGG 독립 실행형
            TryAdd(() => Registry.CurrentUser.OpenSubKey(@"Software\GrindingGearGames\Path of Exile")?.GetValue("InstallLocation") as string);

            // Steam 라이브러리
            try
            {
                var steam = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath") as string;
                if (steam != null)
                {
                    list.Add(Path.Combine(steam, @"steamapps\common\Path of Exile"));
                    var vdf = Path.Combine(steam, @"steamapps\libraryfolders.vdf");
                    if (File.Exists(vdf))
                        foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                            list.Add(Path.Combine(m.Groups[1].Value.Replace(@"\\", @"\"), @"steamapps\common\Path of Exile"));
                }
            }
            catch { }
            return list;
        }
    }
}
