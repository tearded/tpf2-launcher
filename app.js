const toggle=document.getElementById('theme-toggle');
function renderTheme(){const dark=window.launcherTheme.current==='dark';toggle.setAttribute('aria-pressed',String(dark));toggle.title=dark?'Helle Darstellung aktivieren':'Dunkle Darstellung aktivieren';toggle.querySelector('use').setAttribute('href',dark?'#sun':'#moon');}
toggle.addEventListener('click',()=>{window.launcherTheme.set(window.launcherTheme.current==='dark'?'light':'dark');renderTheme();});
renderTheme();
if(window.__TAURI_INTERNALS__){
  import('./desktop.js').then(module=>module.initializeDesktop()).catch(error=>{document.documentElement.dataset.ready='true';const message=document.querySelector('.connection-status');message.textContent='Launcher konnte nicht geladen werden: '+String(error);document.getElementById('main-action').disabled=true;});
}else{
  import('./preview.js');
}