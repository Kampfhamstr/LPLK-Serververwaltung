(()=>{
 'use strict';
 const labels={uebersicht:'Übersicht',spieler:'Zugeordnete Spieler',addons:'Addons',rechte:'Rechte',einstellungen:'Einstellungen',sicherungen:'Sicherungen',verwaltung:'Verwaltung'};
 const text=(node)=>node?.textContent?.trim()||'';
 const safeStorage={get:(key,fallback)=>{try{return JSON.parse(localStorage.getItem(key))??fallback;}catch{return fallback;}},set:(key,value)=>{try{localStorage.setItem(key,JSON.stringify(value));}catch{}}};
 window.LplkUx={confirmation(form,message){
  if(!form)return message;
  const lines=[message],modal=form.closest('[data-player-action-modal]'),ctx=document.querySelector('[data-ux-context]');
  const player=text(modal?.querySelector('[data-player-action-name]'))||(ctx?.dataset.kind==='player'?text(ctx.querySelector('.ux-breadcrumb strong')):'');
  if(player)lines.push('Spieler: '+player);
  const global=/\/bans(?:\/|$)|\/moderation\//.test(new URL(form.action,location.href).pathname)||/global/i.test(message);
  const select=modal?.querySelector('[data-player-action-server]');
  const server=text(select?.selectedOptions?.[0])||(ctx?.dataset.kind==='server'?text(ctx.querySelector('.ux-breadcrumb strong')):'');
  if(global)lines.push('Gilt auf allen verbundenen Servern.');else if(server)lines.push('Server: '+server);
  for(const [name,label]of [['minutes','Dauer'],['reason','Grund'],['detail','Details'],['server_role','Neue Servergruppe']]){
   const control=form.elements.namedItem(name);if(!control||control.disabled)continue;
   const value=control.tagName==='SELECT'?text(control.selectedOptions[0]):String(control.value||'').trim();
   if(value)lines.push(label+': '+value.slice(0,300));
  }
  return lines.join('\n\n');
 }};
 document.addEventListener('DOMContentLoaded',()=>{
  document.documentElement.dataset.uxReady='true';
  const uid=document.body.dataset.uxUser;if(!uid||uid==='0')return;
  const key='lplk.ux.'+uid+'.',ctx=document.querySelector('[data-ux-context]');
  // Store IDs only; names and access are taken from the current authorized page.
  const ids=value=>Array.isArray(value)?value.filter(n=>Number.isSafeInteger(n)&&n>0):[];
  let favorites=ids(safeStorage.get(key+'favorites',[])).slice(0,100);
  let recent=ids(safeStorage.get(key+'recent',[])).slice(0,8);
  if(ctx?.dataset.kind==='server'){const id=Number(ctx.dataset.entityId);recent=[id,...recent.filter(n=>n!==id)].slice(0,8);safeStorage.set(key+'recent',recent);}
  function drawFavorites(){
   document.querySelectorAll('[data-ux-favorite]').forEach(button=>{const active=favorites.includes(Number(button.dataset.uxFavorite));button.setAttribute('aria-pressed',String(active));button.textContent=button.closest('[data-ux-server-list]')?(active?'★':'☆'):(active?'★ Favorit':'☆ Als Favorit merken');});
   document.querySelectorAll('[data-ux-server-list]').forEach(list=>{[...list.children].sort((a,b)=>{const score=el=>{const id=Number(el.dataset.uxServer);return favorites.includes(id)?-100+favorites.indexOf(id):recent.includes(id)?recent.indexOf(id):100;};return score(a)-score(b);}).forEach(el=>{el.querySelector('[data-ux-recent]').hidden=!recent.includes(Number(el.dataset.uxServer));list.append(el);});});
  }
  document.querySelectorAll('[data-ux-favorite]').forEach(button=>button.addEventListener('click',()=>{const id=Number(button.dataset.uxFavorite);favorites=favorites.includes(id)?favorites.filter(n=>n!==id):[...favorites,id].slice(-100);safeStorage.set(key+'favorites',favorites);drawFavorites();}));drawFavorites();
  const motion=document.querySelector('button[data-ux-motion]');
  function setMotion(quiet){document.documentElement.dataset.uxMotion=quiet?'quiet':'normal';motion?.setAttribute('aria-pressed',String(quiet));if(motion)motion.textContent=quiet?'Ruhige Ansicht: An':'Ruhige Ansicht';document.dispatchEvent(new Event('lplk:motionchange'));}
  setMotion(safeStorage.get(key+'quiet',matchMedia('(prefers-reduced-motion: reduce)').matches));motion?.addEventListener('click',()=>{const quiet=document.documentElement.dataset.uxMotion!=='quiet';safeStorage.set(key+'quiet',quiet);setMotion(quiet);});
  const menu=document.querySelector('[data-ux-menu]'),sidebar=document.querySelector('#panel-sidebar');
  const userDrawer=document.querySelector('[data-panel-users-drawer]'),userToggle=document.querySelector('[data-panel-users-toggle]'),mobile=matchMedia('(max-width:850px)');
  const placeUsers=()=>{if(!userToggle||!userDrawer)return;userToggle.classList.toggle('ux-users-inline',mobile.matches);if(mobile.matches)document.querySelector('.ux-topbar')?.append(userToggle);else userDrawer.prepend(userToggle);};placeUsers();mobile.addEventListener('change',placeUsers);
  function closeMenu(){sidebar?.classList.remove('ux-open');menu?.setAttribute('aria-expanded','false');}
  menu?.addEventListener('click',()=>{const open=sidebar.classList.toggle('ux-open');menu.setAttribute('aria-expanded',String(open));if(open)sidebar.querySelector('a,summary')?.focus();});
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&sidebar?.classList.contains('ux-open')){closeMenu();menu.focus();}if((event.ctrlKey||event.metaKey)&&event.key.toLowerCase()==='k'){const search=document.querySelector('#global-search');if(search){event.preventDefault();search.focus();}}});

  // Enhance existing complete markup. Without JS every authorized section remains accessible.
  const workspace=document.querySelector('[data-ux-workspace]');
  if(workspace){
   const groups=new Map();
   workspace.querySelectorAll('[data-ux-section]').forEach(panel=>{const name=panel.dataset.uxSection;if(!groups.has(name)){const group=document.createElement('div');group.className='ux-section-group';group.id='bereich-'+name;groups.set(name,group);}groups.get(name).append(panel);});
   workspace.querySelectorAll('.two-column,.server-config-grid').forEach(el=>{if(!el.children.length)el.remove();});
   const nav=document.createElement('nav');nav.className='ux-section-nav';nav.setAttribute('aria-label','Seitenabschnitte');
   for(const name of Object.keys(labels)){if(!groups.has(name))continue;const a=document.createElement('a');a.href='#'+name;a.textContent=labels[name];a.dataset.uxSectionLink=name;nav.append(a);workspace.append(groups.get(name));}
   // The entity navigation already contains links to the main groups.
   const contextNav=ctx?.querySelector('.ux-context-links');
   contextNav?.querySelectorAll('a').forEach(a=>{const u=new URL(a.href);if(u.pathname===location.pathname&&!u.hash)a.href=u.pathname+u.search+'#uebersicht';});
   if(contextNav){for(const a of nav.children){if(![...contextNav.querySelectorAll('a')].some(b=>{const u=new URL(b.href);return u.pathname===location.pathname&&(u.hash===a.hash||(!u.hash&&a.hash==='#uebersicht'));})){contextNav.append(a.cloneNode(true));}}}else workspace.prepend(nav);
   const selectGroup=()=>{
    let hash;try{hash=decodeURIComponent(location.hash.slice(1));}catch{hash='';}
    const target=document.getElementById(hash),targetGroup=target?.closest('.ux-section-group');
    const active=groups.has(hash)?hash:targetGroup?targetGroup.id.replace('bereich-',''):'uebersicht';
    for(const[name,group]of groups)group.hidden=name!==active;
    document.querySelectorAll('.ux-context-links a,.ux-section-nav a').forEach(a=>{const url=new URL(a.href);const selected=url.pathname===location.pathname&&(url.hash==='#'+active||(!url.hash&&active==='uebersicht'));if(selected)a.setAttribute('aria-current','page');else a.removeAttribute('aria-current');});
    if(target&&targetGroup){for(let p=target.parentElement;p;p=p.parentElement)if(p.tagName==='DETAILS')p.open=true;}
   };
   selectGroup();window.addEventListener('hashchange',selectGroup);
  }else ctx?.querySelectorAll('.ux-context-links a').forEach(a=>{const url=new URL(a.href);if(url.pathname===location.pathname&&!url.hash)a.setAttribute('aria-current','page');});

  // Back-to-results uses only a same-origin list URL; never a posted action or external referrer.
  const listPath=/^\/(players|servers|search)$/.test(location.pathname);
  if(listPath){document.addEventListener('click',event=>{const a=event.target.closest('a[href]');if(!a)return;const u=new URL(a.href);if(u.origin===location.origin&&/^\/(players|servers)\/[1-9]\d*(?:\/|$)/.test(u.pathname)){try{sessionStorage.setItem(key+'return',JSON.stringify({url:location.pathname+location.search,scroll:scrollY,at:Date.now()}));}catch{}}});}
  let back;try{back=JSON.parse(sessionStorage.getItem(key+'return'));}catch{}
  if(back&&Date.now()-back.at<3600000&&/^\/(players|servers|search)(?:\?[^#]*)?$/.test(back.url)){
   if(ctx){const a=document.createElement('a');a.className='ux-back-link';a.href=back.url;a.textContent='← Zurück zu deiner Liste';ctx.before(a);}
   if(location.pathname+location.search===back.url){requestAnimationFrame(()=>scrollTo(0,Math.max(0,Number(back.scroll)||0)));}
  }
  document.querySelectorAll('[data-plugin-server]').forEach(el=>{if('#'+el.id===location.hash)el.open=true;});

  // Contextual confirmation for communication restrictions, preserving existing POST contracts.
  document.querySelectorAll('form[data-player-live-action="mute"],form[data-player-live-action="gag"]').forEach(form=>form.addEventListener('submit',event=>{if(!confirm(window.LplkUx.confirmation(form,'Kommunikationssperre anwenden?')))event.preventDefault();}));
  document.addEventListener('invalid',event=>{const field=event.target;if(!field.form)return;field.setAttribute('aria-invalid','true');let note=field.parentElement.querySelector('.ux-field-error');if(!note){note=document.createElement('span');note.className='ux-field-error';note.id='ux-error-'+crypto.randomUUID();field.after(note);field.setAttribute('aria-describedby',((field.getAttribute('aria-describedby')||'')+' '+note.id).trim());}note.textContent=field.validationMessage;},true);
  document.addEventListener('input',event=>{const field=event.target;if(field.validity?.valid){field.removeAttribute('aria-invalid');const note=field.parentElement?.querySelector('.ux-field-error');if(note){field.setAttribute('aria-describedby',(field.getAttribute('aria-describedby')||'').replace(note.id,'').trim());note.remove();}}});
  const draftNode=document.querySelector('[data-ux-draft]');
  if(draftNode){try{const draft=JSON.parse(draftNode.dataset.uxDraft);const form=[...document.forms].find(f=>new URL(f.action,location.href).pathname===draft.path);if(form){for(const[name,value]of Object.entries(draft.fields)){const input=form.elements.namedItem(name);if(input&&!input.disabled&&!['hidden','password','file'].includes(input.type))input.value=value;}const note=document.createElement('p');note.className='ux-feedback';note.setAttribute('role','status');note.textContent='Deine Eingaben wurden wiederhergestellt. Bitte korrigiere den Fehler und prüfe die Angaben vor dem Speichern.';form.prepend(note);}}catch{}}
  const dirty=new Set();let leaving=false;
  document.addEventListener('input',event=>{const form=event.target.form;if(form?.method==='post'&&!form.matches('[data-chat-form],.dashboard-chat-form')&&!form.closest('[role=dialog]'))dirty.add(form);});
  document.addEventListener('submit',event=>{queueMicrotask(()=>{if(!event.defaultPrevented){leaving=true;dirty.delete(event.target);}});});
  window.addEventListener('beforeunload',event=>{if(!leaving&&dirty.size){event.preventDefault();event.returnValue='';}});
  window.addEventListener('pageshow',()=>{leaving=false;});

  const sourceSelect=document.querySelector('[data-showcase-server]');let featureGeneration=0;
  async function features(){
   const sid=Number(sourceSelect?.value||ctx?.dataset.featureServer||0);if(!sid||!ctx?.querySelector('[data-ux-feature]'))return;
   const generation=++featureGeneration,note=ctx.querySelector('[data-feature-note]');
   const links=[...ctx.querySelectorAll('[data-ux-feature]')];
   for(const a of links){a.hidden=false;a.removeAttribute('aria-disabled');const u=new URL(a.href);if(ctx.dataset.kind==='player'){u.searchParams.set('server',String(sid));a.href=u.pathname+u.search;}}
   if(note)note.textContent='Funktionsstatus wird geprüft …';
   try{
    const response=await fetch('/navigation/features/'+sid,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(15000)});
    if(!response.ok)throw Error('Status nicht erreichbar');const data=await response.json();if(generation!==featureGeneration)return;
    const notes=[];
    const allLinks=[...new Set([...links,...[...document.querySelectorAll('.main a[href]')].filter(a=>/^\/(?:players\/[1-9]\d*\/(?:statistics|economy)|servers\/[1-9]\d*\/ranking)$/.test(new URL(a.href).pathname))])];
    for(const a of allLinks){const feature=a.dataset.uxFeature||(new URL(a.href).pathname.endsWith('/economy')?'economy':'statistics'),state=data.features?.[feature]||'unknown';
     const unavailable=['disabled','not_installed','not_released'].includes(state);a.hidden=unavailable;
     const label=feature==='economy'?'Bank':'Statistiken';
     if(unavailable)notes.push(label+': '+({disabled:'deaktiviert',not_installed:'nicht installiert',not_released:'noch nicht freigegeben'}[state]));
     else if(state!=='active')notes.push(label+': Status momentan nicht bestätigt');
    }
    if(note)note.textContent=[...new Set(notes)].join(' · ');
   }catch{if(generation===featureGeneration&&note)note.textContent='Funktionsstatus momentan nicht abrufbar. Das bedeutet nicht, dass das Addon deaktiviert ist.';}
  }
  features();sourceSelect?.addEventListener('change',features);
 });
})();
