using System;
using System.IO;
public static class UnitTests {
    static int count;
    static void Check(bool value,string label){if(!value)throw new Exception(label);count++;Console.WriteLine("PASS "+label);}
    static void Reject(Action action,string label){try{action();}catch(InvalidDataException){Check(true,label);return;}throw new Exception("Accepted "+label);}
    public static void Main(){
        Check(Updater.IsNewer("0.6.1","v0.6.1.1"),"four-part hotfix");
        Check(!Updater.IsNewer("0.6.1.1","v0.6.1"),"no implicit downgrade");
        Check(Updater.IsNewer("0.6.1.9","v0.6.1.10"),"numeric comparison");
        foreach(string version in new[]{"v0.6.1.1-beta","1.0","1.0.0.0.1","0.6.1.999999999999999999","1.0.0\n"})Reject(()=>Updater.ParseVersion(version),"invalid version");
        foreach(string path in new[]{"../outside.dll","C:/Windows/file.dll","plugins/other.dll","alut_real.dll","mods/mp_lockstep_1/../other.lua","mods/mp_lockstep_1/test.lua:stream"})
            Reject(()=>ProfileStore.SafePath(Path.GetTempPath(),path),"path restriction "+path);
        Check(ProfileStore.Managed("mods/mp_lockstep_1/res/scripts/mp/net.lua"),"managed Lua");
        Check(ProfileStore.Managed("plugins/tpf2_workshop_register.dll"),"managed workshop plugin");
        var asset=new Asset{name="TpF2Multiplayer.msi",size=1,digest="sha256:"+new string('a',64),browser_download_url="https://github.com/silver2127/tpf2-multiplayer/releases/download/v0.6.1.1/TpF2Multiplayer.msi"};
        var release=new Release{tag_name="v0.6.1.1",assets=new[]{asset}};release.Validate();Check(true,"valid fixed source");
        release.prerelease=true;Reject(()=>release.Validate(),"prerelease blocked");release.prerelease=false;
        release.draft=true;Reject(()=>release.Validate(),"draft blocked");release.draft=false;
        asset.browser_download_url="https://example.com/TpF2Multiplayer.msi";Reject(()=>release.Validate(),"foreign source blocked");
        Console.WriteLine("PASS total="+count);
    }
}
