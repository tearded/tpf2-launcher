using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

public class ProfileFile {
    public string Path { get; set; }
    public string Sha256 { get; set; }
    public long Size { get; set; }
}
public class Profile {
    public string Id { get; set; }
    public string Channel { get; set; }
    public string Version { get; set; }
    public string Title { get; set; }
    public string Notes { get; set; }
    public string CreatedUtc { get; set; }
    public string PackageHash { get; set; }
    public ProfileFile[] Files { get; set; }
}
public class ProfileState {
    public string GameFolder { get; set; }
    public string Active { get; set; }
    public string Official { get; set; }
    public string Fork { get; set; }
}
public class SwitchJournal {
    public ProfileState Previous { get; set; }
    public string Restore { get; set; }
}
public sealed class ProfileStore {
    public readonly string Root;
    public ProfileState State;
    readonly bool live;
    public const string ForkRepository = "https://github.com/tearded/tpf2-multiplayer";
    static readonly string[] Core = { "alut.dll", "tpf2_pluginhost.dll", "tpf2_bridge_mp.dll", "tpf2_slice.dll", "tpf2_menu.dll", "tpf2_slice.cfg", "tpf2mp_version.txt", "netpunch/netpunch.exe", "plugins/tpf2_previews.dll", "plugins/tpf2_workshop_register.dll" };
    static readonly string[] Required = { "alut.dll", "tpf2_pluginhost.dll", "tpf2_bridge_mp.dll", "tpf2_slice.dll", "tpf2_menu.dll", "netpunch/netpunch.exe", "mods/mp_lockstep_1/mod.lua", "mods/mp_lockstep_1/res/config/game_script/lockstep.lua", "mods/mp_lockstep_1/res/scripts/mp/net.lua" };
    string StateFile { get { return System.IO.Path.Combine(Root, "profiles.json"); } }
    string JournalFile { get { return System.IO.Path.Combine(Root, "switch-pending.json"); } }
    public bool RecoveryPending { get { return File.Exists(JournalFile); } }
    public ProfileStore(string root, bool liveEnvironment) {
        Root = System.IO.Path.GetFullPath(root); live = liveEnvironment;
        Directory.CreateDirectory(Root);
        if (File.Exists(StateFile)) State = Read<ProfileState>(StateFile);
    }
    public static T Read<T>(string file) { return new JavaScriptSerializer().Deserialize<T>(File.ReadAllText(file)); }
    public static void Write(string file, object value) {
        string temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(temp, new JavaScriptSerializer().Serialize(value));
        if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
    }
    public void Save() { Write(StateFile, State); }
    public static string Hash(string file) {
        using (var stream = File.OpenRead(file)) using (var hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
    }
    public static bool Managed(string name) {
        if (String.IsNullOrEmpty(name) || name.Contains("\\") || name.Contains(":") || name.StartsWith("/") || name.Split('/').Any(p => p == ".." || p == "." || p.Length == 0 || p.EndsWith(".") || p.EndsWith(" "))) return false;
        return Core.Contains(name, StringComparer.OrdinalIgnoreCase) || name.StartsWith("mods/mp_lockstep_1/", StringComparison.OrdinalIgnoreCase);
    }
    public static string SafePath(string root, string relative) {
        if (!Managed(relative)) throw new InvalidDataException("Datei liegt außerhalb des Multiplayer-Profils: " + relative);
        string prefix = System.IO.Path.GetFullPath(root).TrimEnd('\\') + "\\";
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(prefix, relative.Replace('/', '\\')));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Ungültiger Dateipfad.");
        string probe = path;
        while (probe != null && probe.Length >= prefix.Length - 1) {
            if ((File.Exists(probe) || Directory.Exists(probe)) && (File.GetAttributes(probe) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Verknüpfte Profilpfade werden nicht verändert: " + probe);
            probe = System.IO.Path.GetDirectoryName(probe);
        }
        return path;
    }
    public static string[] Inventory(string game) {
        var result = Core.Where(name => File.Exists(SafePath(game, name))).ToList();
        string mod = SafePath(game, "mods/mp_lockstep_1/mod.lua");
        mod = System.IO.Path.GetDirectoryName(mod);
        if (Directory.Exists(mod)) {
            var directories = new Stack<string>(); directories.Push(mod);
            while (directories.Count > 0) {
                string directory = directories.Pop();
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Modordner enthält eine Verzeichnisverknüpfung.");
                foreach (string file in Directory.GetFiles(directory)) {
                    string name = file.Substring(System.IO.Path.GetFullPath(game).TrimEnd('\\').Length + 1).Replace('\\', '/');
                    SafePath(game, name); result.Add(name);
                }
                foreach (string child in Directory.GetDirectories(directory)) directories.Push(child);
            }
        }
        return result.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray();
    }
    string ProfileDirectory(string id) {
        if (id == null || !Regex.IsMatch(id, @"\A[0-9a-f]{32}\z")) throw new InvalidDataException("Ungültige Profil-ID.");
        return System.IO.Path.Combine(Root, "Profiles", id);
    }
    public Profile Load(string id) {
        var profile = Read<Profile>(System.IO.Path.Combine(ProfileDirectory(id), "profile.json"));
        if (profile == null || profile.Id != id || (profile.Channel != "official" && profile.Channel != "fork" && profile.Channel != "local") || profile.Files == null || profile.Files.Length == 0 || profile.Files.Length > 10000)
            throw new InvalidDataException("Ungültiges Profil.");
        Updater.ParseVersion(profile.Version);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in profile.Files) {
            if (file == null || !Managed(file.Path) || !names.Add(file.Path) || file.Sha256 == null || !Regex.IsMatch(file.Sha256, @"\A[0-9A-F]{64}\z") || file.Size < 0)
                throw new InvalidDataException("Ungültiger Profileintrag.");
        }
        if (Required.Any(name => !names.Contains(name))) throw new InvalidDataException("Das Profil ist unvollständig.");
        return profile;
    }
    public Profile Active { get { return State == null ? null : Load(State.Active); } }
    public void Verify(Profile profile) {
        // Re-read the manifest as well, so edited/deleted profile data cannot bypass validation.
        profile = Load(profile.Id);
        string files = System.IO.Path.Combine(ProfileDirectory(profile.Id), "files");
        foreach (var entry in profile.Files) {
            string file = SafePath(files, entry.Path);
            if (!File.Exists(file) || new FileInfo(file).Length != entry.Size || Hash(file) != entry.Sha256) throw new InvalidDataException("Profil beschädigt: " + entry.Path);
        }
    }
    public Profile Capture(string game, string channel, string version, string title, string notes) {
        Updater.ParseVersion(version);
        var profile = new Profile { Id = Guid.NewGuid().ToString("N"), Channel = channel, Version = version, Title = title, Notes = notes, CreatedUtc = DateTime.UtcNow.ToString("o") };
        string folder = ProfileDirectory(profile.Id); Directory.CreateDirectory(folder);
        var entries = new List<ProfileFile>();
        foreach (string relative in Inventory(game)) {
            string source = SafePath(game, relative), target = SafePath(System.IO.Path.Combine(folder, "files"), relative);
            string before = Hash(source); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(target)); File.Copy(source, target);
            if (Hash(target) != before || Hash(source) != before) throw new IOException("Datei wurde während der Sicherung geändert: " + relative);
            entries.Add(new ProfileFile { Path = relative, Sha256 = before, Size = new FileInfo(target).Length });
        }
        profile.Files = entries.ToArray(); Write(System.IO.Path.Combine(folder, "profile.json"), profile); Verify(profile);
        if(!Matches(profile,game))throw new IOException("Der Modstand hat sich während der Sicherung geändert. Bitte erneut versuchen.");
        return profile;
    }
    void Guard(bool requiresClosed=true) {
        if (!live) return;
        if(requiresClosed)Updater.RequireClosed();
        if (State != null && !String.Equals(Updater.GameFolder(), State.GameFolder, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Spielordner hat sich geändert. Profilwechsel gestoppt.");
        ValidateGame(Updater.GameFolder());
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string sandboxConfig=System.IO.Path.Combine(Root,"sandbox-roots.json");
        string[] sandboxes=File.Exists(sandboxConfig)?Read<string[]>(sandboxConfig):new string[0];
        var priorityRoots=new[]{System.IO.Path.Combine(local,"tpf2mp")}.Concat(sandboxes.Select(box=>System.IO.Path.Combine(box,@"user\current\AppData\Local\tpf2mp")));
        foreach (string root in priorityRoots) {
            foreach (string name in Core.Where(n => n.EndsWith(".dll") || n == "netpunch/netpunch.exe").Concat(new[] { "tpf2_previews.dll", "data/plugins/tpf2_previews.dll", "tpf2_workshop_register.dll", "data/plugins/tpf2_workshop_register.dll" }))
                if (File.Exists(System.IO.Path.Combine(root, name))) throw new InvalidOperationException("Eine vorrangige Laufzeitkopie blockiert den Wechsel: " + name);
        }
        if (State != null) foreach(string sandbox in sandboxes) {
            string drive = System.IO.Path.GetPathRoot(State.GameFolder).Substring(0, 1);
            string overlay = System.IO.Path.Combine(sandbox, "drive", drive, State.GameFolder.Substring(3));
            if (Inventory(overlay).Length != 0) throw new InvalidOperationException("Die Test-Sandbox enthält eigene Moddateien. Profilwechsel gestoppt.");
        }
    }
    public static void ValidateGame(string game) {
        using(var reader=new BinaryReader(File.OpenRead(System.IO.Path.Combine(game,"TransportFever2.exe")))) {
            if(reader.ReadUInt16()!=0x5a4d)throw new InvalidDataException("Spiel-EXE ist ungültig.");
            reader.BaseStream.Position=0x3c;int pe=reader.ReadInt32();
            if(pe<64||pe>reader.BaseStream.Length-256)throw new InvalidDataException("Spiel-EXE ist ungültig.");
            reader.BaseStream.Position=pe;if(reader.ReadUInt32()!=0x4550)throw new InvalidDataException("Spiel-EXE ist ungültig.");
            reader.BaseStream.Position=pe+8;uint timestamp=reader.ReadUInt32();
            reader.BaseStream.Position=pe+24;if(reader.ReadUInt16()!=0x20b)throw new InvalidDataException("Kein unterstütztes 64-Bit-Spiel.");
            reader.BaseStream.Position=pe+24+56;uint imageSize=reader.ReadUInt32();
            if(timestamp!=0x675abcc6||imageSize!=0x046ce000)throw new InvalidOperationException("Dieser Launcher unterstützt Transport Fever 2 Steam-Build 35924. Spielversion stimmt nicht überein.");
        }
        string original=System.IO.Path.Combine(game,"alut_real.dll");
        if(!File.Exists(original)||Hash(original)!="3DF103AE3D94A6B90C4D2A6D75DCB388CD835F5E3AF9962B22C20D4473CFC035")throw new InvalidOperationException("Originale Audio-DLL fehlt oder wurde verändert. Bitte offizielle Basisinstallation reparieren.");
    }
    public void Initialize(string game, string version) {
        if (State != null) return;
        Guard(false);
        var current = Capture(game, "local", version, "Lokaler Entwicklungsstand", "Lokale Änderungen; kein veröffentlichtes Fork-Release. Wird beim Wechsel vollständig gesichert.");
        State = new ProfileState { GameFolder = game, Active = current.Id }; Save();
    }
    public bool Matches(Profile profile, string game) {
        var paths = Inventory(game);
        return paths.Length == profile.Files.Length && profile.Files.All(f => File.Exists(SafePath(game, f.Path)) && Hash(SafePath(game, f.Path)) == f.Sha256);
    }
    public Profile SaveLocalChanges() {
        Guard(); if (RecoveryPending) throw new InvalidOperationException("Zuerst den unterbrochenen Wechsel wiederherstellen.");
        var current = Capture(State.GameFolder, "local", Active.Version, "Lokaler Entwicklungsstand", "Gesicherte lokale Installation; kein veröffentlichtes Fork-Release.");
        State.Active = current.Id; Save(); return current;
    }
    public void DetectLocalChanges() {
        if (State == null || RecoveryPending || Matches(Active, State.GameFolder)) return;
        SaveLocalChanges();
    }
    public async Task<Profile> PrepareRelease(Release release, Action<int> progress) {
        release.Validate();
        string cached = release.Channel == "official" ? State.Official : State.Fork;
        if (cached != null) {
            var existing = Load(cached);
            if (existing.Version == release.Number.ToString() && existing.PackageHash == release.Package.digest) { Verify(existing); return existing; }
        }
        string package = await Updater.Download(release, progress);
        LauncherSetup.ValidateMsi(package,release);
        // MSI still has legacy MAX_PATH limits, so keep administrative staging short.
        string stage = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "tpf2-" + Guid.NewGuid().ToString("N").Substring(0,12)); Directory.CreateDirectory(stage);
        package = LauncherSetup.StageMsi(release, package, System.IO.Path.Combine(stage, "input"));
        var info = new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory, "msiexec.exe"), "/a \"" + package + "\" /qn /norestart TARGETDIR=\"" + stage + "\" /L*v \"" + System.IO.Path.Combine(stage,"extract.log") + "\"") { UseShellExecute = false, CreateNoWindow = true };
        using (var process = Process.Start(info)) {
            await Task.Run(() => process.WaitForExit());
            if (process.ExitCode != 0) throw new InvalidOperationException("Installer konnte nicht entpackt werden (" + process.ExitCode + ").");
        }
        string source = System.IO.Path.Combine(stage, @"PFiles\Steam\steamapps\common\Transport Fever 2");
        foreach(string extracted in Directory.GetFiles(source,"*",SearchOption.AllDirectories)) {
            string relative=extracted.Substring(source.Length+1).Replace('\\','/');
            if(!Managed(relative))throw new InvalidDataException("Dieses Release enthält eine noch nicht unterstützte Komponente: "+relative);
        }
        var profile = await Task.Run(() => Capture(source, release.Channel, release.Number.ToString(), release.Channel == "official" ? "Original" : "Community", release.body ?? ""));
        profile.PackageHash = release.Package.digest; Write(System.IO.Path.Combine(ProfileDirectory(profile.Id),"profile.json"),profile);
        if (release.Channel == "official") State.Official = profile.Id; else State.Fork = profile.Id;
        Save(); return profile;
    }
    // Switch only files owned by this launcher. Saves, other mods, other plugins,
    // original alut_real.dll and runtime data stay outside this inventory.
    internal void ApplyFiles(Profile target, Action<int> afterWrite) {
        Verify(target);
        var targetPaths = new HashSet<string>(target.Files.Select(f => f.Path), StringComparer.OrdinalIgnoreCase);
        string sourceRoot = System.IO.Path.Combine(ProfileDirectory(target.Id), "files");
        int count = 0;
        foreach (var entry in target.Files) {
            if (live) Updater.RequireClosed();
            string destination = SafePath(State.GameFolder, entry.Path);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(destination));
            string temp = destination + ".launcher-" + Guid.NewGuid().ToString("N") + ".tmp";
            try {
                File.Copy(SafePath(sourceRoot, entry.Path), temp);
                if (Hash(temp) != entry.Sha256) throw new IOException("Dateiprüfung beim Wechsel fehlgeschlagen.");
                if (File.Exists(destination)) File.Replace(temp, destination, null); else File.Move(temp, destination);
            } finally { if (File.Exists(temp)) File.Delete(temp); }
            if (afterWrite != null) afterWrite(++count);
        }
        foreach (string relative in Inventory(State.GameFolder).Where(p => !targetPaths.Contains(p))) {
            if (live) Updater.RequireClosed();
            File.Delete(SafePath(State.GameFolder, relative));
            if (afterWrite != null) afterWrite(++count);
        }
        if (!Matches(target, State.GameFolder)) throw new IOException("Profilprüfung nach dem Wechsel fehlgeschlagen.");
    }
    public void Activate(Profile target) {
        using(var gate=new System.Threading.Mutex(false,@"Local\TPF2ReleaseProfileMutation")) {
            if(!gate.WaitOne(0))throw new IOException("Ein anderer Profilwechsel läuft bereits.");
            try{Activate(target,null);}finally{gate.ReleaseMutex();}
        }
    }
    internal void Activate(Profile target, Action<int> testFault) {
        Guard(); if (RecoveryPending) throw new InvalidOperationException("Ein unterbrochener Wechsel muss zuerst wiederhergestellt werden.");
        Verify(target);
        if (Active.Id == target.Id && Matches(target, State.GameFolder)) return;
        var previousActive = Active;
        bool changed = !Matches(previousActive, State.GameFolder);
        var before = Capture(State.GameFolder, changed ? "local" : previousActive.Channel, previousActive.Version, previousActive.Title, previousActive.Notes);
        before.PackageHash = changed ? null : previousActive.PackageHash; Write(System.IO.Path.Combine(ProfileDirectory(before.Id),"profile.json"),before);
        State.Active = before.Id;
        if (before.Channel == "fork") State.Fork = before.Id; else if (before.Channel == "official") State.Official = before.Id;
        Save();
        var previous = Read<ProfileState>(StateFile);
        Write(JournalFile, new SwitchJournal { Previous = previous, Restore = before.Id });
        try {
            Guard(); ApplyFiles(target, testFault);
            State.Active = target.Id;
            if (target.Channel == "fork") State.Fork = target.Id; else if (target.Channel == "official") State.Official = target.Id;
            Save(); File.Delete(JournalFile);
        } catch {
            // If a game was started meanwhile, keep the recovery journal and wait
            // for normal game shutdown instead of writing into loaded files.
            try { Guard(); ApplyFiles(before, null); State = previous; Save(); File.Delete(JournalFile); } catch { }
            throw;
        }
    }
    public void Recover() {
        using(var gate=new System.Threading.Mutex(false,@"Local\TPF2ReleaseProfileMutation")) {
            try{if(!gate.WaitOne(0))throw new IOException("Ein anderer Profilwechsel läuft bereits.");}catch(System.Threading.AbandonedMutexException){}
            try{RecoverCore();}finally{gate.ReleaseMutex();}
        }
    }
    void RecoverCore() {
        Guard();
        var journal = Read<SwitchJournal>(JournalFile);
        if (!String.Equals(journal.Previous.GameFolder, State.GameFolder, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Wiederherstellung gehört zu einem anderen Spielordner.");
        var profile = Load(journal.Restore); Verify(profile); ApplyFiles(profile, null);
        State = journal.Previous; Save(); File.Delete(JournalFile);
    }
}
