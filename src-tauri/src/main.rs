#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use std::io::{BufRead, BufReader};
use std::os::windows::process::CommandExt;
use std::process::{Command, Stdio};
use std::sync::{Arc, atomic::{AtomicBool, Ordering}};
use tauri::{Emitter, Manager};

#[derive(Clone, Default)]
struct Operation(Arc<AtomicBool>);
struct Reset(Arc<AtomicBool>);
impl Drop for Reset { fn drop(&mut self) { self.0.store(false, Ordering::SeqCst); } }

fn allowed(action: &str) -> bool {
    matches!(action, "status" | "fetch" | "install" | "recover" | "play" | "game-folder" | "choose-folder" | "backups" | "release-page")
}

#[tauri::command]
async fn native_action(app: tauri::AppHandle, state: tauri::State<'_, Operation>, action: String, channel: Option<String>, version: Option<String>) -> Result<serde_json::Value, String> {
    if !allowed(&action) { return Err("Unbekannte Launcher-Aktion.".into()); }
    let channel = channel.unwrap_or_else(|| "official".into());
    if channel != "official" && channel != "community" { return Err("Unbekannter Kanal.".into()); }
    if let Some(ref value) = version { if value.len()>32 || !value.chars().all(|c| c.is_ascii_digit() || c == '.') { return Err("Ungültige Versionsnummer.".into()); } }
    if state.0.swap(true, Ordering::SeqCst) { return Err("Eine Aktion läuft bereits. Bitte kurz warten.".into()); }
    let guard = Reset(state.0.clone());
    let helper = app.path().resource_dir().map_err(|e|e.to_string())?.join("TPF2Launcher.Native.exe");
    tauri::async_runtime::spawn_blocking(move || {
        let _guard = guard;
        let mut command = Command::new(helper);
        command.args([&action, &channel]);
        if let Some(value) = version { command.arg(value); }
        let mut child = command.creation_flags(0x08000000).stdin(Stdio::null()).stdout(Stdio::piped()).stderr(Stdio::null()).spawn().map_err(|e|format!("Launcher-Helfer startet nicht: {e}"))?;
        let reader = BufReader::new(child.stdout.take().ok_or("Launcher-Helfer ohne Ausgabe")?);
        let mut result = None;
        for line in reader.lines() {
            let line = line.map_err(|e|e.to_string())?;
            let value: serde_json::Value = serde_json::from_str(&line).map_err(|_|"Ungültige Antwort des Launcher-Helfers")?;
            if value["kind"] == "progress" { let _ = app.emit("native-progress", &value); }
            else { result = Some(value); }
        }
        let status = child.wait().map_err(|e|e.to_string())?;
        let value = result.ok_or("Launcher-Helfer wurde ohne Ergebnis beendet.")?;
        if !status.success() || value["ok"] != true { return Err(value["error"].as_str().unwrap_or("Aktion fehlgeschlagen.").to_owned()); }
        Ok(value["data"].clone())
    }).await.map_err(|e|e.to_string())?
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_single_instance::init(|app, _, _| { if let Some(window)=app.get_webview_window("main") { let _=window.unminimize(); let _=window.set_focus(); } }))
        .plugin(tauri_plugin_updater::Builder::new().build())
        .manage(Operation::default())
        .invoke_handler(tauri::generate_handler![native_action])
        .on_window_event(|window, event| {
            if let tauri::WindowEvent::CloseRequested { api, .. } = event {
                if window.state::<Operation>().0.load(Ordering::SeqCst) {
                    api.prevent_close(); let _=window.emit("operation-busy", ());
                }
            }
        })
        .run(tauri::generate_context!())
        .expect("TPF2 Launcher konnte nicht gestartet werden");
}

#[cfg(test)]
mod tests {
    #[test] fn only_fixed_operations() { assert!(super::allowed("install")); assert!(!super::allowed("cmd")); assert!(!super::allowed("../program.exe")); }
}
