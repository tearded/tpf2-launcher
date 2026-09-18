using System;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Forms;

public static class LauncherSetup {
    public static bool HandleElevatedAction(string[] args) {
        if(args.Length!=3||args[0]!="--activate")return false;
        Guid profileId,requestId;
        if(!Guid.TryParseExact(args[1],"N",out profileId)||!Guid.TryParseExact(args[2],"N",out requestId))throw new InvalidDataException("Ungültiger Wechselauftrag.");
        string result=Path.Combine(Updater.Home,"switch-result-"+args[2]+".txt");
        try{var store=new ProfileStore(Updater.Home,true);if(store.RecoveryPending)store.Recover();else store.Activate(store.Load(args[1]));File.WriteAllText(result,"OK");Environment.Exit(0);}
        catch(Exception ex){File.WriteAllText(result,ex.Message);Environment.Exit(1);}
        return true;
    }
    public static async Task Activate(ProfileStore store,Profile target) {
        bool needsElevation=false;
        string probe=Path.Combine(store.State.GameFolder,".tpf2-launcher-"+Guid.NewGuid().ToString("N")+".tmp");
        try{using(File.Create(probe)){}File.Delete(probe);}
        catch(UnauthorizedAccessException){needsElevation=true;}
        if(!needsElevation){await Task.Run(()=>{if(store.RecoveryPending)store.Recover();else store.Activate(target);});return;}
        string request=Guid.NewGuid().ToString("N"),result=Path.Combine(Updater.Home,"switch-result-"+request+".txt");
        var info=new ProcessStartInfo(Application.ExecutablePath,"--activate "+target.Id+" "+request){UseShellExecute=true,Verb="runas"};
        using(var process=Process.Start(info)){
            await Task.Run(()=>process.WaitForExit());
            if(process.ExitCode!=0)throw new InvalidOperationException(File.Exists(result)?File.ReadAllText(result):"Erhöhter Profilwechsel wurde abgebrochen.");
        }
        store.State=ProfileStore.Read<ProfileState>(Path.Combine(store.Root,"profiles.json"));
        if(store.State.Active!=target.Id||!store.Matches(target,store.State.GameFolder))throw new IOException("Erhöhter Profilwechsel konnte nicht bestätigt werden.");
        if(File.Exists(result))File.Delete(result);
    }
    public static void ValidateMsi(string package,Release release) {
        dynamic installer=Activator.CreateInstance(Type.GetTypeFromProgID("WindowsInstaller.Installer"));
        dynamic database=installer.OpenDatabase(package,0);
        dynamic view=database.OpenView("SELECT `Property`, `Value` FROM `Property`");view.Execute();
        string version=null,upgrade=null,name=null;
        for(dynamic row=view.Fetch();row!=null;row=view.Fetch()) {
            string property=row.StringData[1];string value=row.StringData[2];
            if(property=="ProductVersion")version=value;if(property=="UpgradeCode")upgrade=value;if(property=="ProductName")name=value;
        }
        view.Close();
        if(version!=release.Number.ToString()||upgrade!="{80DBF679-F058-410E-9BAD-87731AC96633}"||name!="TpF2 Multiplayer")throw new InvalidDataException("MSI-Produktdaten passen nicht zum ausgewählten Release.");
    }
    public static string StageMsi(Release release,string package,string directory) {
        // Windows Installer's service can fail to open the per-user download
        // cache (1619). Use a separate temporary input and verify the copy.
        Updater.Verify(release,package);
        Directory.CreateDirectory(directory);
        string staged=Path.Combine(directory,"TpF2Multiplayer.msi");
        File.Copy(package,staged,false);
        Updater.Verify(release,staged);
        return staged;
    }
    public static async Task InstallBase(Release release,Action<int> progress) {
        if(release.Channel!="official")throw new InvalidOperationException("Zuerst einmal die offizielle Multiplayer-Basis installieren.");
        Updater.RequireClosed();string game=Updater.GameFolder();
        string package=await Updater.Download(release,progress);ValidateMsi(package,release);
        await Task.Run(()=>Updater.Backup(game,Updater.RegistryValue("Version")??"Erstinstallation"));
        Updater.RequireClosed();
        package=StageMsi(release,package,Path.Combine(Path.GetTempPath(),"tpf2-base-"+Guid.NewGuid().ToString("N").Substring(0,12)));
        string log=Path.Combine(Updater.Home,"base-install-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".log");
        var info=new ProcessStartInfo(Path.Combine(Environment.SystemDirectory,"msiexec.exe"),"/i \""+package+"\" /passive /norestart MSIRESTARTMANAGERCONTROL=Disable INSTALLFOLDER=\""+game+"\" /L*v \""+log+"\""){UseShellExecute=true,Verb="runas"};
        using(var process=Process.Start(info)){
            await Task.Run(()=>process.WaitForExit());
            if(process.ExitCode==3010)throw new InvalidOperationException("Basis installiert. Windows bitte neu starten und den Launcher erneut öffnen.");
            if(process.ExitCode!=0)throw new InvalidOperationException("Basisinstallation nicht abgeschlossen ("+process.ExitCode+"). Protokoll: "+log);
        }
        if(Updater.RegistryValue("Version")!=release.Number.ToString()||!File.Exists(Path.Combine(game,"alut_real.dll")))throw new InvalidOperationException("Basisinstallation konnte nicht bestätigt werden.");
    }
}
