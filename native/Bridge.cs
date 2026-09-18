using System;
using System.IO;
using System.Net;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

public static class NativeBridge {
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = 2097152 };
    static readonly object OutputLock = new object();
    [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] static extern int GetCurrentPackageFullName(ref uint length, StringBuilder name);
    static void Write(object value) { lock(OutputLock)Console.WriteLine(Json.Serialize(value)); }
    static void Progress(string text, int percent=0) { Write(new {kind="progress",text=text,percent=percent}); }
    static void Open(string target) { Process.Start(new ProcessStartInfo(target){UseShellExecute=true}); }
    static string Channel(string value) { if(value=="official")return "official";if(value=="community")return "fork";throw new InvalidDataException("Unbekannter Kanal."); }
    static string PublicChannel(string value) { return value=="fork"?"community":value; }
    public static void EnsureNativeContext() {
        uint length=0;
        if(GetCurrentPackageFullName(ref length,null)!=15700)
            throw new InvalidOperationException("Bitte den Launcher über das Windows-Startmenü öffnen. Der aktuelle App-Kontext verwendet umgeleitete Benutzerdaten.");
    }
    static void RequireOldLauncherClosed() {
        try { using(var gate=Mutex.OpenExisting(@"Local\TPF2OfficialPersonalLauncher")) {
            bool acquired=false;try{acquired=gate.WaitOne(0);}catch(AbandonedMutexException){acquired=true;}
            if(!acquired)throw new InvalidOperationException("Bitte zuerst den bisherigen Launcher schließen.");
            gate.ReleaseMutex();
        }}catch(WaitHandleCannotBeOpenedException){}
    }
    static object Status(ProfileStore store) {
        string folder=null;try{folder=Updater.GameFolder();}catch(InvalidOperationException){}
        var active=store.Active;
        return new {gameFolder=folder,installed=active==null?null:new {version=active.Version,channel=PublicChannel(active.Channel)},
            running=Updater.GameRunning(),recovery=store.RecoveryPending,initialized=store.State!=null};
    }
    static void InitializeIfPresent(ProfileStore store) {
        if(store.State!=null||Updater.GameRunning())return;
        string version=Updater.RegistryValue("Version");
        if(version==null)return;
        RequireOldLauncherClosed();
        store.Initialize(Updater.GameFolder(),version);
    }
    static async Task<object> Run(string action,string channel,string expectedVersion) {
        var store=new ProfileStore(Updater.Home,true);
        switch(action) {
            case "status":
                InitializeIfPresent(store);
                return Status(store);
            case "fetch": {
                var release=await Updater.Fetch(Channel(channel));
                if(release==null)return null;
                return new {version=release.Number.ToString(),notes=release.body??"Keine Versionshinweise vorhanden.",channel=channel};
            }
            case "choose-folder": {
                Updater.RequireClosed();RequireOldLauncherClosed();
                using(var dialog=new FolderBrowserDialog{Description="Transport-Fever-2-Ordner mit TransportFever2.exe auswählen"}) {
                    if(dialog.ShowDialog()!=DialogResult.OK)return Status(store);
                    string folder=Path.GetFullPath(dialog.SelectedPath);
                    if(!File.Exists(Path.Combine(folder,"TransportFever2.exe")))throw new InvalidDataException("In diesem Ordner fehlt TransportFever2.exe.");
                    if(store.State!=null&&!String.Equals(folder,store.State.GameFolder,StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Für die vorhandenen Sicherungen ist bereits ein anderer Spielordner eingerichtet.");
                    Directory.CreateDirectory(Updater.Home);File.WriteAllText(Path.Combine(Updater.Home,"game-folder.txt"),folder);
                }
                InitializeIfPresent(store);return Status(store);
            }
            case "install": {
                Updater.RequireClosed();RequireOldLauncherClosed();InitializeIfPresent(store);
                if(store.RecoveryPending)throw new InvalidOperationException("Bitte zuerst den unterbrochenen Wechsel wiederherstellen.");
                if(expectedVersion==null)throw new InvalidDataException("Bitte zuerst die verfügbare Version prüfen.");
                var release=await Updater.Fetch(Channel(channel));
                if(release==null||release.Number.ToString()!=expectedVersion)throw new InvalidOperationException("Das Releaseangebot hat sich geändert. Bitte Updates erneut prüfen.");
                if(store.State==null) {
                    if(channel!="official")throw new InvalidOperationException("Bitte zuerst die Original-Version installieren. Danach ist Community verfügbar.");
                    Progress("Die Multiplayer-Basis wird eingerichtet …");
                    await LauncherSetup.InstallBase(release,p=>Progress("Multiplayer-Basis herunterladen …",p));
                    store.Initialize(Updater.GameFolder(),Updater.RegistryValue("Version"));
                }
                Progress("Release herunterladen und prüfen …");
                var target=await store.PrepareRelease(release,p=>Progress("Release herunterladen …",p));
                Updater.RequireClosed();RequireOldLauncherClosed();
                Progress("Bisherigen Stand sichern und Version aktivieren …");
                await LauncherSetup.Activate(store,target);
                return Status(store);
            }
            case "recover": {
                Updater.RequireClosed();RequireOldLauncherClosed();
                if(!store.RecoveryPending)return Status(store);
                var journal=ProfileStore.Read<SwitchJournal>(Path.Combine(store.Root,"switch-pending.json"));
                await LauncherSetup.Activate(store,store.Load(journal.Restore));return Status(store);
            }
            case "play":
                if(store.RecoveryPending)throw new InvalidOperationException("Bitte zuerst den unterbrochenen Wechsel wiederherstellen.");
                Updater.RequireClosed();Updater.GameFolder();Open("steam://rungameid/1066780");return new {started=true};
            case "game-folder": Open(Updater.GameFolder());return new {opened=true};
            case "backups":
                string profiles=Path.Combine(store.Root,"Profiles");Directory.CreateDirectory(profiles);Open(profiles);return new {opened=true};
            case "release-page":
                Channel(channel);Open("https://github.com/"+(channel=="official"?"silver2127":"tearded")+"/tpf2-multiplayer/releases");return new {opened=true};
            default: throw new InvalidDataException("Unbekannte Launcher-Aktion.");
        }
    }
    [STAThread] public static int Main(string[] args) {
        Console.OutputEncoding=new UTF8Encoding(false);
        ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;
        try {
            EnsureNativeContext();
            if(LauncherSetup.HandleElevatedAction(args))return 0;
            if(args.Length<2||args.Length>3)throw new InvalidDataException("Ungültiger Launcher-Aufruf.");
            var result=Run(args[0],args[1],args.Length==3?args[2]:null).GetAwaiter().GetResult();
            Write(new {ok=true,data=result});return 0;
        }catch(Exception ex){Write(new {ok=false,error=ex.Message});return 1;}
    }
}
