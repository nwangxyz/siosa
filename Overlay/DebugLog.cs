using System;
using System.IO;
using System.Text;

namespace Siosa
{
    // 보상 창 감지 과정을 debug\siosa.log 에 남긴다 (문제 분석용). 1MB 를 넘으면 새로 시작.
    public static class DebugLog
    {
        public static string Path;

        public static void Write(string msg)
        {
            if (Path == null) return;
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path));
                if (File.Exists(Path) && new FileInfo(Path).Length > 1024 * 1024) File.Delete(Path);
                File.AppendAllText(Path, $"{DateTime.Now:HH:mm:ss.fff} {msg}\r\n", Encoding.UTF8);
            }
            catch { }
        }
    }
}
