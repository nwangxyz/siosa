using System.Reflection;

// 버전은 여기 한 곳에서만 바꾼다 (release.ps1 이 이 값을 읽어 Siosa-v<버전>.zip 을 만든다).
// GitHub 릴리스 태그도 같은 값으로 (예: v1.0.0) — 자동 업데이트가 태그와 이 값을 비교한다.
[assembly: AssemblyTitle("Siosa")]
[assembly: AssemblyProduct("Siosa")]
[assembly: AssemblyCopyright("Copyright (c) 2026 nwangxyz · MIT")]
[assembly: AssemblyVersion(Siosa.AppInfo.Version + ".0")]
[assembly: AssemblyFileVersion(Siosa.AppInfo.Version + ".0")]
[assembly: AssemblyInformationalVersion(Siosa.AppInfo.Version)]

namespace Siosa
{
    // 파일 위치 (Program.Main 에서 정한다)
    //   배포(포터블): exe 하나. 게임 데이터는 exe 안, 사용자 파일은 exe 옆 SiosaData\ (처음 실행할 때 생김)
    //                 SiosaData 에 같은 이름의 데이터 파일(예: regex.json)을 두면 exe 안의 것 대신 쓴다
    //   개발: exe 위쪽에서 data\rewards.json 이 있는 폴더(저장소)를 찾으면 그 폴더가 Root, data\ 가 Data
    public static class AppPaths
    {
        public static bool Portable;
        public static string Root;   // settings.json · profiles · progress · debug
        public static string Data;   // 디스크의 데이터 폴더 (없는 파일은 exe 안의 것)
    }

    public static class AppInfo
    {
        public const string Version = "1.0.0";
        public const string Repo = "nwangxyz/siosa";   // GitHub 저장소 (릴리스에서 업데이트를 받는다)
        public const string RepoUrl = "https://github.com/" + Repo;
    }
}
