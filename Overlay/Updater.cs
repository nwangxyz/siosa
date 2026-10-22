using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace Siosa
{
    // 자동 업데이트 (포터블 exe 하나): GitHub 의 최신 릴리스(태그 v<버전>, 첨부 Siosa-v<버전>.zip)와 지금 버전을 비교하고,
    // 사용자가 '업데이트'를 누르면 받아서 Siosa.exe(와 README.txt)만 바꾼 뒤 다시 시작한다.
    //   - 게임 데이터는 exe 안에 있으므로 exe 만 바꾸면 된다
    //   - 사용자 파일(SiosaData\: 설정·프리셋·진행 기록, 직접 고친 regex.json)은 건드리지 않는다
    //   - 실행 중인 exe 는 덮어쓸 수 없어 Siosa.exe.old 로 이름을 바꾸고 새 exe 를 넣는다 (다음 실행 때 .old 를 지운다)
    // 개발 실행(저장소의 data 폴더를 쓰는 실행)에서는 확인하지 않는다.
    public static class Updater
    {
        public class Info
        {
            public Version Version;
            public string Tag, ZipUrl, PageUrl, Notes;
            public long Size;
        }

        public static Version Current => new Version(AppInfo.Version);

        // 업데이트할 수 있는 실행인지: 배포(포터블 exe)일 때만
        public static bool Supported => AppPaths.Portable;

        // 한 번 확인한 결과 (설정창을 다시 열 때마다 GitHub 에 묻지 않도록. GitHub API 는 시간당 60번 제한)
        static Task<Info> checking;
        public static Task<Info> CheckAsync() => checking ?? (checking = Task.Run(() => Check()));

        // 최신 릴리스. 새 버전이 아니거나 첨부 파일이 없으면 Version 만 채워서 돌려준다. 실패하면 예외
        static Info Check()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create($"https://api.github.com/repos/{AppInfo.Repo}/releases/latest");
            req.UserAgent = "Siosa/" + AppInfo.Version;
            req.Accept = "application/vnd.github+json";
            req.Timeout = req.ReadWriteTimeout = 8000;
            string body;
            try
            {
                using (var res = req.GetResponse())
                using (var sr = new StreamReader(res.GetResponseStream()))
                    body = sr.ReadToEnd();
            }
            catch (WebException ex) when ((ex.Response as HttpWebResponse)?.StatusCode == HttpStatusCode.NotFound)
            {
                return new Info { Version = Current, Tag = "v" + AppInfo.Version };   // 아직 릴리스가 없음 → 지금이 최신
            }
            var js = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            var d = js.Deserialize<Dictionary<string, object>>(body);
            var tag = d.TryGetValue("tag_name", out var t) ? t as string : null;
            if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var v)) throw new InvalidDataException("릴리스 태그를 읽지 못했습니다: " + tag);
            var info = new Info
            {
                Version = v, Tag = tag,
                PageUrl = d.TryGetValue("html_url", out var h) ? h as string : AppInfo.RepoUrl + "/releases",
                Notes = d.TryGetValue("body", out var b) ? b as string ?? "" : "",
            };
            if (d.TryGetValue("assets", out var a) && a is System.Collections.ArrayList list)
                foreach (Dictionary<string, object> asset in list)
                {
                    var name = asset["name"] as string ?? "";
                    if (!name.StartsWith("Siosa-", StringComparison.OrdinalIgnoreCase) || !name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    info.ZipUrl = asset["browser_download_url"] as string;
                    info.Size = Convert.ToInt64(asset["size"]);
                    break;
                }
            return info;
        }

        public static bool IsNewer(Info i) => i != null && i.ZipUrl != null && i.Version > Current;

        // 받아서 바꾸기. progress 는 다른 스레드에서 불린다
        public static void Apply(Info info, Action<string, int> progress)
        {
            if (!IsNewer(info)) throw new InvalidOperationException("새 버전이 없습니다");
            var uri = new Uri(info.ZipUrl);
            bool gh = uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".github.com", StringComparison.OrdinalIgnoreCase);
            if (uri.Scheme != "https" || !gh) throw new InvalidDataException("GitHub 주소가 아닙니다: " + info.ZipUrl);

            var tmp = Path.Combine(Path.GetTempPath(), "siosa_update");
            if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
            Directory.CreateDirectory(tmp);
            var zip = Path.Combine(tmp, "update.zip");

            // 1) 받기
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var req = (HttpWebRequest)WebRequest.Create(info.ZipUrl);
            req.UserAgent = "Siosa/" + AppInfo.Version;
            req.Timeout = req.ReadWriteTimeout = 30000;
            using (var res = req.GetResponse())
            using (var src = res.GetResponseStream())
            using (var dst = File.Create(zip))
            {
                long total = res.ContentLength > 0 ? res.ContentLength : info.Size, done = 0;
                var buf = new byte[81920];
                int n;
                while ((n = src.Read(buf, 0, buf.Length)) > 0)
                {
                    dst.Write(buf, 0, n);
                    done += n;
                    int pct = total > 0 ? (int)(done * 100 / total) : -1;
                    progress?.Invoke(total > 0 ? $"받는 중… {pct}%  ({done / 1024:N0} / {total / 1024:N0} KB)" : $"받는 중… {done / 1024:N0} KB", pct < 0 ? -1 : pct * 90 / 100);   // 받기 = 0~90%
                }
            }
            Install(zip, Application.ExecutablePath, tmp, progress);
            try { Directory.Delete(tmp, true); } catch { }
        }

        // 받은 zip 으로 exe 바꾸기 (개발용 --installtest 에서도 씀). exe = 바꿀 실행 파일, tmp = 풀 곳
        public static void Install(string zip, string exe, string tmp, Action<string, int> progress)
        {
            // 2) 풀기 (압축 밖으로 나가는 경로는 거부)
            progress?.Invoke("압축 푸는 중…", 93);
            var ext = Path.GetFullPath(Path.Combine(tmp, "x")) + "\\";
            using (var z = ZipFile.OpenRead(zip))
                foreach (var e in z.Entries)
                {
                    var dest = Path.GetFullPath(Path.Combine(ext, e.FullName));
                    if (!dest.StartsWith(ext, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("잘못된 압축 파일입니다");
                    if (e.FullName.EndsWith("/") || e.FullName.EndsWith("\\")) { Directory.CreateDirectory(dest); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    e.ExtractToFile(dest, true);
                }
            var pkg = File.Exists(Path.Combine(ext, "Siosa", "Siosa.exe")) ? Path.Combine(ext, "Siosa") : ext;
            if (!File.Exists(Path.Combine(pkg, "Siosa.exe"))) throw new InvalidDataException("압축 안에 Siosa.exe 가 없습니다");

            // 3) 바꾸기 (실행 중인 exe 는 이름만 바꿀 수 있다)
            progress?.Invoke("바꾸는 중…", 97);
            var old = exe + ".old";
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Copy(Path.Combine(pkg, "Siosa.exe"), exe, true); }
            catch { if (!File.Exists(exe)) File.Move(old, exe); throw; }
            var readme = Path.Combine(pkg, "README.txt");
            if (File.Exists(readme)) File.Copy(readme, Path.Combine(Path.GetDirectoryName(exe), "README.txt"), true);
            progress?.Invoke("완료 · 다시 시작합니다", 100);
        }

        // 업데이트 뒤 다시 시작했을 때: 예전 프로세스가 끝나기를 잠깐 기다리고 .old 를 지운다
        public static void CleanupAfterUpdate(string[] args)
        {
            int i = Array.IndexOf(args, "--after-update");
            if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out var pid))
                try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(10000); } catch { }
            var old = Application.ExecutablePath + ".old";
            for (int k = 0; k < 5 && File.Exists(old); k++)
            {
                try { File.Delete(old); } catch { System.Threading.Thread.Sleep(300); }
            }
        }
    }
}
