using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Linq;
using System.Drawing;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

public class Asset {
    public string name { get; set; }
    public string browser_download_url { get; set; }
    public string digest { get; set; }
    public long size { get; set; }
}
public class Release {
    [ScriptIgnore] public string Repository = "silver2127/tpf2-multiplayer";
    [ScriptIgnore] public string Channel { get { return Repository == "silver2127/tpf2-multiplayer" ? "official" : "fork"; } }
    public string tag_name { get; set; }
    public string body { get; set; }
    public bool draft { get; set; }
    public bool prerelease { get; set; }
    public Asset[] assets { get; set; }
    public Version Number { get { return Updater.ParseVersion(tag_name); } }
    public Asset Package {
        get {
            var matches = (assets ?? new Asset[0]).Where(a => a != null && a.name == "TpF2Multiplayer.msi").ToArray();
            if (matches.Length != 1) throw new InvalidDataException("Kein eindeutiger offizieller MSI-Installer vorhanden.");
            return matches[0];
        }
    }
    public void Validate() {
        if (draft || prerelease || Number == null || (Repository != "silver2127/tpf2-multiplayer" && Repository != "tearded/tpf2-multiplayer")) throw new InvalidDataException("Keine stabile Veröffentlichung.");
        var asset = Package;
        if (asset.browser_download_url != "https://github.com/" + Repository + "/releases/download/" + tag_name + "/TpF2Multiplayer.msi" ||
            asset.digest == null || !Regex.IsMatch(asset.digest, @"\Asha256:[0-9a-fA-F]{64}\z") || asset.size <= 0 || asset.size > 536870912)
            throw new InvalidDataException("Downloadadresse, Größe oder SHA-256-Prüfsumme fehlen oder sind ungültig.");
    }
}
public sealed class HttpDownload : WebClient {
    public HttpDownload() { Headers[HttpRequestHeader.UserAgent] = "TPF2-Personal-Launcher/1.0"; Headers[HttpRequestHeader.CacheControl] = "no-cache"; }
    protected override WebRequest GetWebRequest(Uri address) {
        var request = base.GetWebRequest(address);
        request.Timeout = 30000;
        var http = request as HttpWebRequest;
        if (http != null) http.ReadWriteTimeout = 30000;
        return request;
    }
}
public static class Updater {
    public const string Feed = "https://api.github.com/repos/silver2127/tpf2-multiplayer/releases/latest";
    public static readonly string Home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TPF2-MP", "OfficialLauncher");
    public static Version ParseVersion(string value) {
        Version parsed;
        if (value == null || !Regex.IsMatch(value, @"\Av?[0-9]+\.[0-9]+\.[0-9]+(?:\.[0-9]+)?\z") ||
            !Version.TryParse(value.TrimStart('v'), out parsed)) throw new InvalidDataException("Versionsnummer ist ungültig.");
        return parsed;
    }
    public static bool IsNewer(string installed, string offered) { return ParseVersion(offered) > ParseVersion(installed); }
    public static string RegistryValue(string name) {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            using (var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
            using (var key = root.OpenSubKey(@"SOFTWARE\silver2127\TpF2 Multiplayer"))
                if (key != null && key.GetValue(name) is string) return (string)key.GetValue(name);
        return null;
    }
    public static bool GameRunning() {
        var processes = Process.GetProcessesByName("TransportFever2");
        try { return processes.Length > 0; } finally { foreach (var process in processes) process.Dispose(); }
    }
    public static void RequireClosed() { if (GameRunning()) throw new InvalidOperationException("Bitte zuerst alle Transport-Fever-2-Fenster regulär schließen."); }
    public static string GameFolder() {
        string config=Path.Combine(Home,"game-folder.txt");
        var candidates=new System.Collections.Generic.List<string>();
        if(File.Exists(config))candidates.Add(File.ReadAllText(config).Trim());
        candidates.Add(RegistryValue("InstallFolder"));
        foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
            using(var root=RegistryKey.OpenBaseKey(RegistryHive.LocalMachine,view))
            using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Steam App 1066780"))
                if(key!=null)candidates.Add(key.GetValue("InstallLocation") as string);
        foreach(string folder in candidates)
            if(!String.IsNullOrWhiteSpace(folder)&&File.Exists(Path.Combine(folder,"TransportFever2.exe")))return Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
        throw new InvalidOperationException("Spielordner nicht gefunden. Bitte links „Spielordner wählen“ verwenden.");
    }
    public static async Task<Release> Fetch() {
        return await Fetch("official");
    }
    public static async Task<Release> Fetch(string channel) {
        if (channel != "official" && channel != "fork") throw new InvalidDataException("Unbekannter Kanal.");
        string repo = channel == "official" ? "silver2127/tpf2-multiplayer" : "tearded/tpf2-multiplayer";
        using (var client = new HttpDownload()) {
            var job = client.DownloadStringTaskAsync("https://api.github.com/repos/" + repo + "/releases/latest");
            if (await Task.WhenAny(job, Task.Delay(45000)) != job) { client.CancelAsync(); throw new TimeoutException("GitHub antwortet nicht. Bitte später erneut versuchen."); }
            string json;
            try { json = await job; }
            catch (WebException ex) {
                var response = ex.Response as HttpWebResponse;
                if (channel == "fork" && response != null && response.StatusCode == HttpStatusCode.NotFound) return null;
                throw;
            }
            var release = new JavaScriptSerializer().Deserialize<Release>(json);
            if (release == null) throw new InvalidDataException("Leere Antwort von GitHub.");
            release.Repository = repo;
            release.Validate(); return release;
        }
    }
    public static void Verify(Release release, string file) {
        release.Validate();
        if (new FileInfo(file).Length != release.Package.size) throw new InvalidDataException("Download ist unvollständig.");
        using (var input = File.OpenRead(file)) using (var sha = SHA256.Create()) {
            string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
            if (!String.Equals(actual, release.Package.digest.Substring(7), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256-Prüfung fehlgeschlagen. Installation gestoppt.");
        }
    }
    public static async Task<string> Download(Release release, Action<int> progress) {
        release.Validate(); Directory.CreateDirectory(Path.Combine(Home, "Downloads"));
        string file = Path.Combine(Home, "Downloads", "TpF2Multiplayer-" + (release.Channel == "fork" ? "fork-" : "") + release.Number + ".msi");
        if (File.Exists(file)) { try { Verify(release, file); return file; } catch (InvalidDataException) { } }
        string partial = file + "." + Guid.NewGuid().ToString("N") + ".part";
        try {
            using (var client = new HttpDownload()) {
                client.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                var job = client.DownloadFileTaskAsync(release.Package.browser_download_url, partial);
                if (await Task.WhenAny(job, Task.Delay(600000)) != job) { client.CancelAsync(); throw new TimeoutException("Download-Zeitlimit erreicht."); }
                await job;
            }
            await Task.Run(() => Verify(release, partial));
            if (File.Exists(file)) File.Delete(file);
            File.Move(partial, file); return file;
        } finally { try { if (File.Exists(partial)) File.Delete(partial); } catch { } }
    }
    public static string Backup(string game, string version) {
        RequireClosed();
        return BackupFiles(game, version);
    }
    internal static string BackupFiles(string game, string version) {
        string folder = Path.Combine(Home, "Backups", DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(folder);
        using (var zip = ZipFile.Open(Path.Combine(folder, "modstand.zip"), ZipArchiveMode.Create)) {
            foreach (string name in new[] { "alut.dll", "alut_real.dll", "tpf2_pluginhost.dll", "tpf2_bridge_mp.dll", "tpf2_slice.dll", "tpf2_menu.dll", "tpf2_slice.cfg", "tpf2mp.cfg", "netpunch/netpunch.exe" }) {
                string source = Path.Combine(game, name);
                if (File.Exists(source)) zip.CreateEntryFromFile(source, name, CompressionLevel.Optimal);
            }
            string mod = Path.Combine(game, "mods", "mp_lockstep_1");
            if (Directory.Exists(mod)) foreach (string file in Directory.GetFiles(mod, "*", SearchOption.AllDirectories))
                zip.CreateEntryFromFile(file, file.Substring(game.Length + 1).Replace('\\', '/'), CompressionLevel.Optimal);
        }
        File.WriteAllText(Path.Combine(folder, "Hinweis.txt"), "Moddateien vor dem offiziellen Update. Installiert: " + version + "\r\nSpiel: " + game + "\r\nEnthält auch lokale Lua-Änderungen. Kein vollständiger MSI-Rollback. Zum Wiederherstellen zuerst passende offizielle Version installieren, dann gesicherte Dateien bei geschlossenem Spiel zurückspielen. Spielstände werden nicht verändert.\r\n");
        return folder;
    }
}
