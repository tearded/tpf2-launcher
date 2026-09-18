# TPF2 Multiplayer Launcher

Ein eigenständiger Windows-Launcher für Transport Fever 2 Multiplayer mit einer
HTML/CSS-Oberfläche, heller und dunkler Darstellung und zwei Updatekanälen:
**Original** ([silver2127](https://github.com/silver2127/tpf2-multiplayer)) und
**Community** ([tearded](https://github.com/tearded/tpf2-multiplayer)).

## Herunterladen und weitergeben

Die einzelne **Setup.exe** aus den [Releases](https://github.com/tearded/tpf2-launcher/releases/latest)
herunterladen und ausführen. Diese Datei kann auch an Mitspieler weitergegeben
werden. Installiert wird für den aktuellen Windows-Benutzer; WebView2 wird bei
Bedarf nachinstalliert. Voraussetzung: Windows 10/11 x64, Steam und Transport
Fever 2 Build 35924. Internet ist für Downloads erforderlich.

Den erkannten Spielordner prüfen oder in den Einstellungen auswählen. Bei einer
neuen Multiplayer-Installation zuerst Original installieren. Danach den
gewünschten Kanal wählen. Alle Mitspieler brauchen denselben Kanal und dieselbe
Version. Ein Kanalwechsel wird vor der Installation bestätigt; alle laufenden
Spiele und der bisherige Launcher müssen dabei geschlossen sein.

Der Launcher sichert den bisherigen Multiplayer-Stand und prüft Paketgröße,
SHA-256 und MSI-Metadaten. Vorhandene Profile des bisherigen Launchers werden
weiterverwendet. Spielstände und andere Mods werden nicht verteilt.

## Updates

Launcher und Multiplayer haben getrennte Releases. Der Launcher prüft beim
Öffnen sein eigenes Repository. Unter Einstellungen lässt sich ein angebotenes
**Launcher-Update per Klick installieren**. Der integrierte Updater prüft die
kryptografische Paketsignatur. Der Installer besitzt kein Authenticode-Zertifikat.

Multiplayer-Updates kommen weiterhin aus den beiden oben verlinkten Projekten.
Der öffentliche Kanal Community kann älter als Original sein. Die angezeigte
installierte Version bleibt unabhängig von der ausgewählten Zielversion.

## Entwicklung

Benötigt werden Node.js 24, Rust (Version in `rust-toolchain.toml`), die MSVC
Build Tools mit Windows SDK und .NET Framework 4.x. Die Oberfläche läuft in
Tauri 2 / WebView2; ein eng begrenzter nativer C#-Helfer verwaltet die Profile.

```powershell
npm ci
npm run native
npm run desktop
```

Für eine reine Designvorschau mit Beispieldaten: `node serve.mjs`, anschließend
http://127.0.0.1:4318. Nur die Desktop-App führt native Aktionen aus.

```powershell
npm run build
powershell -File scripts/test-unit.ps1
cd src-tauri
cargo test --locked
```

`npm run test:native` führt zusätzlich die Windows-Profilregression in isolierten
Dateikopien aus. Diese benötigt eine vorhandene kompatible Spielinstallation und
Internet; sie verändert die echte Installation nicht.

## Releases bauen

1. Die Versionen in `package.json`, `package-lock.json`, `src-tauri/Cargo.toml`,
   `src-tauri/Cargo.lock` und `src-tauri/tauri.conf.json` gemeinsam erhöhen.
2. Versionshinweise in `docs/releases/<Version>.md` ergänzen und prüfen.
3. Den Commit nach `main` und einen passenden `v<Version>`-Tag pushen.

Der Windows-Workflow baut und testet den Quellstand, signiert den Installer mit
GitHub Actions Secret `TAURI_SIGNING_PRIVATE_KEY`, erstellt die Update-Metadaten
und lädt zunächst einen Releaseentwurf hoch. Erst nach erneutem Download und
Hashvergleich wird er veröffentlicht. Bestehende Releases werden nicht ersetzt.
Der private Signierschlüssel gehört niemals ins Repository; der öffentliche
Prüfschlüssel steht in der Tauri-Konfiguration. Den privaten Schlüssel dauerhaft
sichern, da vorhandene Installationen nur damit signierte Updates akzeptieren.

Lokal: `npm run package` mit gesetztem `TAURI_SIGNING_PRIVATE_KEY`, danach
`scripts/prepare-release.ps1`. Die Dateien liegen unter `release/` (git-ignoriert).

## Lizenz

MIT. Kein offizielles Produkt von Urban Games. Die Multiplayer-Pakete werden aus
den verlinkten Projekten bezogen und unterliegen deren jeweiligen Lizenzen.
