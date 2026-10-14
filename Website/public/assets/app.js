document.addEventListener('DOMContentLoaded',()=>{
  document.querySelectorAll('img[data-avatar]').forEach(img=>{
    img.addEventListener('error',()=>{ img.hidden=true; });
  });

  document.querySelectorAll('[data-toggle]').forEach(button=>{
    button.addEventListener('click',()=>{
      const element=document.getElementById(button.dataset.toggle);
      if(element) element.classList.toggle('hidden');
    });
  });

  document.querySelectorAll('form[data-confirm]').forEach(form=>{
    form.addEventListener('submit',event=>{
      if(!confirm(window.LplkUx?.confirmation(form,form.dataset.confirm)||form.dataset.confirm)) event.preventDefault();
    });
  });

  document.querySelectorAll('button[data-global-confirm]').forEach(button=>{
    button.addEventListener('click',event=>{
      if(!confirm(window.LplkUx?.confirmation(button.form,button.dataset.globalConfirm)||button.dataset.globalConfirm)) event.preventDefault();
    });
  });

  document.querySelectorAll('#connector_type').forEach(type=>{
    const form=type.closest('form');
    if(!form) return;
    const update=()=>{
      form.querySelectorAll('[data-connector]').forEach(section=>{
        const active=section.dataset.connector===type.value;
        section.classList.toggle('hidden',!active);
        section.querySelectorAll('input,select,textarea,button').forEach(control=>{
          control.disabled=!active;
        });
      });
    };
    type.addEventListener('change',update);
    update();
  });

  // Community-Rolle direkt in der Gesamtübersicht ändern.
  document.querySelectorAll('select[data-community-role-select]').forEach(select=>{
    select.addEventListener('change',()=>{
      const oldValue=select.dataset.current||'';
      const newValue=select.value;
      if(newValue===oldValue) return;
      const label=select.options[select.selectedIndex]?.text||newValue;
      const player=select.dataset.player||'diese Person';
      const mirror=select.form?.querySelector('input[name="mirror_to_servers"]')?.checked;
      const extra=mirror?' und gleichzeitig als Serverrolle auf alle verwaltbaren Server übernehmen':'';
      if(!confirm(`Community-Rolle für ${player} wirklich auf „${label}“ ändern${extra} und speichern?`)){
        select.value=oldValue;
        return;
      }
      select.form?.submit();
    });
  });

  // Einzelne Serverrolle direkt in der Tabellenzelle ändern.
  document.querySelectorAll('select[data-server-role-select]').forEach(select=>{
    select.addEventListener('change',()=>{
      const oldValue=select.dataset.current||'';
      const newValue=select.value;
      if(newValue===oldValue) return;
      const label=select.options[select.selectedIndex]?.text||'Keine Gruppe';
      const player=select.dataset.player||'diese Person';
      const server=select.dataset.server||'diesem Server';
      if(!confirm(`Serverrolle für ${player} auf ${server} wirklich auf „${label}“ ändern und speichern?`)){
        select.value=oldValue;
        return;
      }
      select.form?.submit();
    });
  });

  // Massenverwaltung: Spieler markieren / alle sichtbaren markieren.
  const bulkForm=document.querySelector('form[data-bulk-players]');
  if(bulkForm){
    const itemSelector='input[data-select-item="bulk-player"]';
    const selectAll=document.querySelector('input[data-select-all="bulk-player"]');
    const countNode=bulkForm.querySelector('[data-selected-count]');
    const clearButton=bulkForm.querySelector('[data-clear-selection]');
    const mirror=bulkForm.querySelector('[data-mirror-community]');
    const communitySelect=bulkForm.querySelector('[data-bulk-community-role]');
    const serverRoleSelect=bulkForm.querySelector('[data-bulk-server-role]');

    const items=()=>Array.from(document.querySelectorAll(itemSelector));
    const updateCount=()=>{
      const list=items();
      const checked=list.filter(item=>item.checked).length;
      if(countNode) countNode.textContent=String(checked);
      if(selectAll){
        selectAll.checked=checked>0&&checked===list.length;
        selectAll.indeterminate=checked>0&&checked<list.length;
      }
    };

    if(selectAll){
      selectAll.addEventListener('change',()=>{
        items().forEach(item=>{item.checked=selectAll.checked;});
        updateCount();
      });
    }
    items().forEach(item=>item.addEventListener('change',updateCount));
    clearButton?.addEventListener('click',()=>{
      items().forEach(item=>{item.checked=false;});
      if(selectAll) selectAll.checked=false;
      updateCount();
    });

    const updateMirror=()=>{
      if(!mirror||!serverRoleSelect) return;
      serverRoleSelect.disabled=mirror.checked;
      serverRoleSelect.title=mirror.checked?'Die Serverrolle wird aus der gewählten Community-Rolle übernommen.':'';
    };
    mirror?.addEventListener('change',updateMirror);
    updateMirror();
    updateCount();

    bulkForm.addEventListener('submit',event=>{
      const selected=items().filter(item=>item.checked).length;
      if(selected===0){
        event.preventDefault();
        alert('Bitte mindestens einen Spieler auswählen.');
        return;
      }
      if(mirror?.checked && communitySelect?.value==='__unchanged'){
        event.preventDefault();
        alert('Bitte eine Community-Rolle auswählen, wenn sie gleichzeitig als Serverrolle verwendet werden soll.');
        return;
      }
      const communityLabel=communitySelect?.options[communitySelect.selectedIndex]?.text||'Nicht ändern';
      const serverLabel=mirror?.checked
        ? `wie Community-Rolle (${communityLabel})`
        : (serverRoleSelect?.options[serverRoleSelect.selectedIndex]?.text||'Nicht ändern');
      const scope=event.submitter?.value==='global'?'alle verwaltbaren Server':'ausgewählte Server';
      const message=`Änderungen für ${selected} ausgewählte Spieler wirklich speichern?\n\nCommunity-Rolle: ${communityLabel}\nServerrolle: ${serverLabel}\nServerbereich: ${scope}`;
      if(!confirm(message)) event.preventDefault();
    });
  }
});

// v1.2.0: Live-Serverstatus. Nur Textwerte werden in den DOM geschrieben.
document.addEventListener('DOMContentLoaded',()=>{
  const root=document.querySelector('[data-live-server-id]');
  if(!root) return;
  const serverId=root.dataset.liveServerId;
  const body=document.querySelector('[data-live-player-body]');
  const auto=document.querySelector('[data-live-auto]');
  const refreshButton=document.querySelector('[data-live-refresh]');
  const pteroEnabled=root.dataset.pteroResourcesEnabled==='1';
  const liveCsrf=root.dataset.liveCsrf||'';
  const canBotKick=root.dataset.canBotKick==='1';
  const canBotSlay=root.dataset.canBotSlay==='1';
  const canBotTeam=root.dataset.canBotTeam==='1';
  let busy=false;
  let pteroUptimeBase=null;
  let pteroUptimeReceivedAt=0;
  let pteroCurrentState='unknown';

  const humanDuration=value=>{
    const seconds=Math.max(0,Number(value)||0);
    const h=Math.floor(seconds/3600),m=Math.floor((seconds%3600)/60),s=Math.floor(seconds%60);
    if(h>0) return `${h}h ${m}m`;
    if(m>0) return `${m}m ${s}s`;
    return `${s}s`;
  };
  const serverUptimeHuman=value=>{
    const seconds=Math.max(0,Math.floor(Number(value)||0));
    const d=Math.floor(seconds/86400),h=Math.floor((seconds%86400)/3600),m=Math.floor((seconds%3600)/60),sec=seconds%60;
    const parts=[];
    if(d>0)parts.push(`${d} T`);
    if(h>0||d>0)parts.push(`${h} Std`);
    if(m>0||h>0||d>0)parts.push(`${m} Min`);
    if(d===0&&h===0)parts.push(`${sec} Sek`);
    return parts.join(' ')||'0 Sek';
  };
  const updatePteroUptimeClock=()=>{
    const node=document.querySelector('[data-ptero-uptime]');if(!node)return;
    if(pteroCurrentState!=='running'||pteroUptimeBase===null){if(pteroCurrentState==='starting')node.textContent='startet gerade';return;}
    const elapsed=Math.max(0,Math.floor((Date.now()-pteroUptimeReceivedAt)/1000));
    node.textContent=serverUptimeHuman(Number(pteroUptimeBase)+elapsed);
  };
  const formatBytes=value=>{
    const bytes=Number(value);
    if(!Number.isFinite(bytes)||bytes<0)return '—';
    const units=['B','KiB','MiB','GiB','TiB'];
    let n=bytes,i=0;while(n>=1024&&i<units.length-1){n/=1024;i++;}
    return `${n.toLocaleString('de-DE',{maximumFractionDigits:i>=3?2:(i>=2?1:0)})} ${units[i]}`;
  };
  const clampPercent=value=>Math.max(0,Math.min(100,Number(value)||0));
  const dateKey=value=>{const d=new Date(value);if(Number.isNaN(d.getTime()))return String(value||'').slice(0,10);return `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;};
  const dateLabel=value=>{
    const d=new Date(value);if(Number.isNaN(d.getTime()))return String(value||'Unbekanntes Datum').slice(0,10);
    const today=new Date(),yesterday=new Date();yesterday.setDate(today.getDate()-1);
    const key=dateKey(d),todayKey=dateKey(today),yesterdayKey=dateKey(yesterday);
    const base=d.toLocaleDateString('de-DE',{day:'2-digit',month:'2-digit',year:'numeric'});
    return key===todayKey?`Heute · ${base}`:(key===yesterdayKey?`Gestern · ${base}`:base);
  };
  const td=(text,className='')=>{const e=document.createElement('td');e.textContent=text??'—';if(className)e.className=className;return e;};
  const livePlayerName=player=>String(player?.name||player?.display_name||player?.steam_id64||'').trim();
  const isSpectatorTeam=team=>['spectator','spectators','spec'].includes(String(team||'').trim().toLowerCase());
  const sortPlayersAlphabetically=players=>[...players].sort((a,b)=>{
    const byName=livePlayerName(a).localeCompare(livePlayerName(b),'de',{sensitivity:'base',numeric:true});
    if(byName!==0)return byName;
    return String(a?.steam_id64||'').localeCompare(String(b?.steam_id64||''));
  });
  const refreshPterodactyl=async()=>{
    if(!pteroEnabled)return;
    try{
      const r=await fetch(`/servers/${encodeURIComponent(serverId)}/live/pterodactyl`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});
      const data=await r.json().catch(()=>({available:false,error:`HTTP ${r.status}`}));
      const state=String(data.state||'unknown').toLowerCase();
      const stateLabel=String(data.state_label||state||'UNBEKANNT');
      pteroCurrentState=state;
      pteroUptimeBase=Number.isFinite(Number(data.uptime_seconds))?Number(data.uptime_seconds):null;
      pteroUptimeReceivedAt=Date.now();
      const pill=document.querySelector('[data-ptero-state]');
      if(pill){pill.textContent=stateLabel;pill.className=`status-pill ${state==='running'?'status-online':(state==='starting'||state==='stopping'?'status-warning':'status-offline')}`;}
      const stateText=document.querySelector('[data-ptero-state-text]');if(stateText)stateText.textContent=stateLabel;
      const stateNote=document.querySelector('[data-ptero-state-note]');if(stateNote)stateNote.textContent=data.suspended?'Server ist in Pterodactyl suspendiert':(data.available?'Pterodactyl Client API':'nicht verfügbar');
      const uptime=document.querySelector('[data-ptero-uptime]');if(uptime)uptime.textContent=data.uptime_human||'—';
      updatePteroUptimeClock();

      const cpu=document.querySelector('[data-ptero-cpu]');if(cpu)cpu.textContent=data.cpu_percent===null||data.cpu_percent===undefined?'—':`${Number(data.cpu_percent).toLocaleString('de-DE',{maximumFractionDigits:2})} %`;
      const cpuDetail=document.querySelector('[data-ptero-cpu-detail]');if(cpuDetail){
        if(data.cpu_limit_percent){const rel=data.cpu_of_limit_percent===null||data.cpu_of_limit_percent===undefined?'':` · ${Number(data.cpu_of_limit_percent).toLocaleString('de-DE',{maximumFractionDigits:1})}% vom Limit`;cpuDetail.textContent=`Limit ${Number(data.cpu_limit_percent).toLocaleString('de-DE',{maximumFractionDigits:0})}%${rel}`;}else cpuDetail.textContent='kein CPU-Limit gemeldet';
      }
      const cpuBar=document.querySelector('[data-ptero-cpu-bar]');if(cpuBar)cpuBar.style.width=`${clampPercent(data.cpu_of_limit_percent??data.cpu_percent)}%`;

      const memory=document.querySelector('[data-ptero-memory]');if(memory)memory.textContent=formatBytes(data.memory_bytes);
      const memoryDetail=document.querySelector('[data-ptero-memory-detail]');if(memoryDetail)memoryDetail.textContent=data.memory_limit_bytes?`${formatBytes(data.memory_bytes)} / ${formatBytes(data.memory_limit_bytes)} · ${Number(data.memory_percent||0).toLocaleString('de-DE',{maximumFractionDigits:1})}%`:`${formatBytes(data.memory_bytes)} · unbegrenzt`;
      const memoryBar=document.querySelector('[data-ptero-memory-bar]');if(memoryBar)memoryBar.style.width=`${clampPercent(data.memory_percent)}%`;

      const disk=document.querySelector('[data-ptero-disk]');if(disk)disk.textContent=formatBytes(data.disk_bytes);
      const diskDetail=document.querySelector('[data-ptero-disk-detail]');if(diskDetail)diskDetail.textContent=data.disk_limit_bytes?`${formatBytes(data.disk_bytes)} / ${formatBytes(data.disk_limit_bytes)} · ${Number(data.disk_percent||0).toLocaleString('de-DE',{maximumFractionDigits:1})}%`:`${formatBytes(data.disk_bytes)} · unbegrenzt`;
      const diskBar=document.querySelector('[data-ptero-disk-bar]');if(diskBar)diskBar.style.width=`${clampPercent(data.disk_percent)}%`;

      const network=document.querySelector('[data-ptero-network]');if(network)network.textContent=`Download ${formatBytes(data.network_rx_bytes)} · Upload ${formatBytes(data.network_tx_bytes)}`;
      const fetched=document.querySelector('[data-ptero-fetched]');if(fetched)fetched.textContent=data.fetched_at?`Pterodactyl: ${new Date(data.fetched_at).toLocaleTimeString('de-DE')}`:'Pterodactyl nicht aktualisiert';
      const stale=document.querySelector('[data-ptero-stale]');if(stale)stale.hidden=!data.stale;
      const error=document.querySelector('[data-ptero-error]');if(error)error.textContent=data.error?` · ${data.error}`:'';

      document.querySelectorAll('.ptero-power-action').forEach(form=>{
        const signal=form.querySelector('input[name="signal"]')?.value||'';
        const button=form.querySelector('button[type="submit"]');if(!button)return;
        let disabled=false;
        if(signal==='start')disabled=state==='running'||state==='starting';
        if(signal==='stop'||signal==='kill')disabled=state==='offline'||state==='stopping';
        button.disabled=disabled;
      });
    }catch(e){
      const error=document.querySelector('[data-ptero-error]');if(error)error.textContent=` · Ressourcenfehler: ${e?.message||e}`;
    }
  };

  const renderServerChat=async()=>{
    const list=document.querySelector('[data-live-server-chat]'); if(!list)return;
    try{
      const r=await fetch(`/servers/${encodeURIComponent(serverId)}/live/chat/messages?limit=30`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});
      if(!r.ok)return; const data=await r.json(); const messages=Array.isArray(data.messages)?data.messages:[];
      list.replaceChildren();
      if(messages.length===0){const e=document.createElement('div');e.className='muted server-chat-empty';e.textContent='Noch keine Chatnachrichten gespeichert.';list.appendChild(e);}
      let previousChatDay=null;
      messages.forEach(m=>{
        const currentChatDay=dateKey(m.sent_at);
        if(currentChatDay!==previousChatDay){const sep=document.createElement('div');sep.className='server-chat-day-separator';sep.dataset.chatDay=currentChatDay;const span=document.createElement('span');span.textContent=dateLabel(m.sent_at);sep.appendChild(span);list.appendChild(sep);previousChatDay=currentChatDay;}
        const role=String(m.community_role||'spieler').toLowerCase();
        const article=document.createElement('article');article.className=`server-chat-message game-chat-role-${role}`;
        const meta=document.createElement('div');meta.className='server-chat-meta';
        const time=document.createElement('time');try{time.textContent=new Date(m.sent_at).toLocaleTimeString('de-DE',{hour:'2-digit',minute:'2-digit',second:'2-digit'});}catch{time.textContent=m.sent_at||'—';}
        const name=document.createElement('strong');name.className=`game-role-text game-role-text-${role}`;name.textContent=m.player_name||'Unbekannt';
        const roleBadge=document.createElement('span');roleBadge.className=`game-role game-role-${role}`;roleBadge.textContent=role.charAt(0).toUpperCase()+role.slice(1);
        const team=document.createElement('span');team.className=`team-badge team-${String(m.team||'unassigned').toLowerCase()}`;team.textContent=m.team||'Unassigned';
        const scope=document.createElement('span');scope.className='status-pill';scope.textContent=m.team_only?'TEAM':'ALL';
        meta.append(time,name,roleBadge,team,scope);
        if(m.steam_id64 && document.querySelector('[data-player-action-modal]')){
          const actions=document.createElement('button');actions.type='button';actions.className='btn btn-secondary btn-small player-action-button';actions.textContent='Aktionen';
          actions.dataset.playerActions='';actions.dataset.playerSteamid=String(m.steam_id64);actions.dataset.playerName=String(m.player_name||'Unbekannt');actions.dataset.playerServerId=String(serverId);actions.dataset.playerChatId=String(m.id||0);meta.appendChild(actions);
        }
        const text=document.createElement('div');text.className='server-chat-text';text.textContent=m.message||'';
        article.append(meta,text);list.appendChild(article);
      });
      const total=document.querySelector('[data-server-chat-total]');if(total)total.textContent=`${Number(data.total||0)} gesamt`;
    }catch(e){console.warn('Serverchat konnte nicht aktualisiert werden.',e);}
  };
  const refresh=async()=>{
    if(busy) return;
    busy=true;
    try{
      const response=await fetch(`/servers/${encodeURIComponent(serverId)}/live/status`,{headers:{'Accept':'application/json'},credentials:'same-origin',cache:'no-store'});
      if(!response.ok) throw new Error(`HTTP ${response.status}`);
      const data=await response.json();
      const bridge=document.querySelector('[data-live-bridge]');
      if(bridge){bridge.textContent=data.bridge_online?'ONLINE':'OFFLINE';bridge.className=`status-pill ${data.bridge_online?'status-online':'status-offline'}`;}
      const map=document.querySelector('[data-live-map]'); if(map) map.textContent=data.map||'—';
      const count=document.querySelector('[data-live-count]'); if(count) count.textContent=`${Array.isArray(data.players)?data.players.length:0} / ${data.max_players??'—'}`;
      const teamScore=document.querySelector('[data-live-team-score]');if(teamScore)teamScore.textContent=`CT ${Number(data.ct_score??0)} : ${Number(data.t_score??0)} T`;
      const rounds=document.querySelector('[data-live-rounds]');if(rounds)rounds.textContent=`${Number(data.rounds_played??0)} Runden gespielt`;
      const updated=document.querySelector('[data-live-updated]'); if(updated) updated.textContent=data.updated_at||'—';
      if(body){
        body.replaceChildren();
        const players=sortPlayersAlphabetically(Array.isArray(data.players)?data.players:[]);
        const botCounts=players.reduce((acc,player)=>{if(!player?.is_bot)return acc;const team=String(player.team||'').toLowerCase();if(team==='t')acc.t++;if(team==='ct')acc.ct++;return acc;},{t:0,ct:0});
        document.querySelectorAll('[data-live-bot-count]').forEach(node=>{const prefix=node.dataset.liveBotPrefix||'';node.textContent=`${prefix}T ${botCounts.t} · CT ${botCounts.ct}`;});
        document.querySelectorAll('[data-live-bot-note]').forEach(node=>{node.textContent=(botCounts.t+botCounts.ct)>0?'Bots vorhanden':'Keine Bots';});
        if(players.length===0){const tr=document.createElement('tr');const empty=td('Aktuell keine Spielerdaten verfügbar.','muted');empty.colSpan=13;tr.appendChild(empty);body.appendChild(tr);}
        const hidden=(form,name,value)=>{const input=document.createElement('input');input.type='hidden';input.name=name;input.value=String(value??'');form.appendChild(input);};
        const botBaseForm=(player,operation)=>{const form=document.createElement('form');form.method='post';form.action=`/servers/${encodeURIComponent(serverId)}/live/bot-player`;hidden(form,'_csrf',liveCsrf);hidden(form,'operation',operation);hidden(form,'user_id',player.user_id??0);hidden(form,'player_id',player.player_id??-1);return form;};
        players.forEach(player=>{
          const isBot=Boolean(player.is_bot);
          const tr=document.createElement('tr');
          const liveRole=isBot?'bot':String(player.community_role||'spieler').toLowerCase();
          const spectator=isSpectatorTeam(player.team);
          const alive=!spectator&&Boolean(player.alive);
          const lifeClass=spectator?'is-player-spectator':(alive?'is-player-alive':'is-player-dead');
          tr.className=`live-player-row ${lifeClass} live-role-${liveRole}`;
          tr.dataset.playerLifeState=spectator?'spectator':(alive?'alive':'dead');
          const name=document.createElement('td');name.dataset.playerStatusHost='';name.dataset.playerSteamid=isBot?'':String(player.steam_id64||'');const strong=document.createElement('strong');strong.textContent=player.name||'Unbekannt';name.appendChild(strong);
          if(isBot){const note=document.createElement('div');note.className='small-muted';note.textContent='CS2-Bot';name.appendChild(note);}else{if(player.nickname_override){const note=document.createElement('div');note.className='small-muted';note.textContent='Server-Nickname aktiv';name.appendChild(note);}if(player.is_secondary_account){const note=document.createElement('div');note.className='small-muted';note.textContent='Zweitaccount';name.appendChild(note);}}tr.appendChild(name);
          const steam=document.createElement('td');steam.className='mono';if(isBot){const botId=document.createElement('span');botId.className='bot-id-label';botId.textContent='BOT';steam.appendChild(botId);}else{steam.textContent=player.steam_id64||'';}tr.appendChild(steam);
          const roleCell=document.createElement('td');
          if(isBot){const role=document.createElement('span');role.className='game-role game-role-bot';role.textContent='BOT';roleCell.appendChild(role);}else if(player.community_role){const role=document.createElement('span');role.className=`game-role game-role-${String(player.community_role).toLowerCase()}`;role.textContent=String(player.community_role).charAt(0).toUpperCase()+String(player.community_role).slice(1);roleCell.appendChild(role);}else{roleCell.textContent='nicht erfasst';roleCell.className='muted';}
          tr.appendChild(roleCell);
          const team=document.createElement('td');const badge=document.createElement('span');const teamName=String(player.team||'—');badge.className=`team-badge team-${teamName.toLowerCase()}`;badge.textContent=teamName;team.appendChild(badge);tr.appendChild(team);
          tr.appendChild(td(Number(player.kills??0),'numeric-stat'));
          tr.appendChild(td(Number(player.score??0),'numeric-stat'));
          const stateCell=document.createElement('td');
          const state=document.createElement('span');state.className=`player-life-state ${spectator?'player-life-spec':(alive?'player-life-alive':'player-life-dead')}`;state.textContent=spectator?'Zuschauer':(alive?'Lebend':'Tot');stateCell.appendChild(state);
          if(!isBot&&player.frozen){const note=document.createElement('span');note.className='status-note';note.textContent=' · Gefreezt';stateCell.appendChild(note);}if(!isBot&&player.noclip){const note=document.createElement('span');note.className='status-note';note.textContent=' · Noclip';stateCell.appendChild(note);}tr.appendChild(stateCell);
          tr.appendChild(td(player.health??'—'));tr.appendChild(td(player.armor??'—'));tr.appendChild(td(`${player.ping??'—'} ms`));
          if(isBot){tr.appendChild(td('—','muted'));}else{const comm=[];if(player.voice_muted)comm.push('Mute');if(player.gagged)comm.push('Gag');if(player.noclip)comm.push('Noclip');tr.appendChild(td(comm.length?comm.join(' · '):'—'));}
          tr.appendChild(td(player.connected_human||humanDuration(player.connected_seconds)));
          const action=document.createElement('td');
          if(isBot){
            const wrap=document.createElement('div');wrap.className='bot-player-actions';
            if(canBotKick){const form=botBaseForm(player,'kick');form.addEventListener('submit',event=>{if(!confirm('Diesen Bot wirklich kicken?'))event.preventDefault();});const button=document.createElement('button');button.type='submit';button.className='btn btn-danger btn-small';button.textContent='Kicken';form.appendChild(button);wrap.appendChild(form);}
            if(canBotSlay){const form=botBaseForm(player,'slay');form.addEventListener('submit',event=>{if(!confirm('Diesen Bot wirklich töten?'))event.preventDefault();});const button=document.createElement('button');button.type='submit';button.className='btn btn-secondary btn-small';button.textContent='Kill';form.appendChild(button);wrap.appendChild(form);}
            if(canBotTeam){const form=botBaseForm(player,'team');form.className='bot-team-form';const select=document.createElement('select');select.name='team';select.setAttribute('aria-label','Bot-Team');[['ct','CT'],['t','T'],['spectator','Spec']].forEach(([value,label])=>{const option=document.createElement('option');option.value=value;option.textContent=label;option.selected=(value==='spectator'?spectator:String(player.team||'').toLowerCase()===value);select.appendChild(option);});const button=document.createElement('button');button.type='submit';button.className='btn btn-secondary btn-small';button.textContent='Setzen';form.append(select,button);wrap.appendChild(form);}
            if(!canBotKick&&!canBotSlay&&!canBotTeam){const muted=document.createElement('span');muted.className='muted';muted.textContent='—';wrap.appendChild(muted);}action.appendChild(wrap);
          }else{
            const wrap=document.createElement('div');wrap.className='player-quick-actions';const link=document.createElement('a');link.className='btn btn-secondary btn-small';link.textContent='Verwalten';link.href=`/servers/${encodeURIComponent(serverId)}/live/player/${encodeURIComponent(player.steam_id64||'')}`;wrap.appendChild(link);
            if(player.steam_id64 && document.querySelector('[data-player-action-modal]')){const quick=document.createElement('button');quick.type='button';quick.className='btn btn-secondary btn-small player-action-button';quick.textContent='Aktionen';quick.dataset.playerActions='';quick.dataset.playerSteamid=String(player.steam_id64);quick.dataset.playerName=String(player.name||'Unbekannt');quick.dataset.playerServerId=String(serverId);wrap.appendChild(quick);}action.appendChild(wrap);
          }
          tr.appendChild(action);body.appendChild(tr);
        });
      }
      await Promise.all([renderServerChat(),refreshPterodactyl()]);
    }catch(error){console.warn('Live-Status konnte nicht aktualisiert werden.',error);}finally{busy=false;}
  };
  if(pteroEnabled){refreshPterodactyl();window.setInterval(updatePteroUptimeClock,1000);}
  refreshButton?.addEventListener('click',refresh);
  window.setInterval(()=>{if(auto?.checked) refresh();},5000);
});


// v1.8.3: Modulares Live-Dashboard. Kacheln auf-/zuklappen, frei anordnen
// und in einem 12-Spalten-Raster in der Breite anpassen. Alles wird je Panelkonto gespeichert.
document.addEventListener('DOMContentLoaded',()=>{
  const grid=document.querySelector('[data-live-layout-root]');
  const toolbar=document.querySelector('[data-live-layout-toolbar]');
  if(!grid||!toolbar)return;

  ['pterodactyl-control','active-rules'].forEach(id=>{const section=document.getElementById(id);if(section&&section.parentElement!==grid)grid.appendChild(section);});

  const storedOrder=String(toolbar.dataset.layoutOrder||'').split(',').map(v=>v.trim()).filter(Boolean);
  const storedCollapsed=new Set(String(toolbar.dataset.layoutCollapsed||'').split(',').map(v=>v.trim()).filter(Boolean));
  let storedSizes={};
  try{storedSizes=JSON.parse(String(toolbar.dataset.layoutSizes||'{}'))||{};}catch(_error){storedSizes={};}
  const endpoint=toolbar.dataset.layoutEndpoint||'';
  const csrf=toolbar.dataset.layoutCsrf||'';
  const editButton=toolbar.querySelector('[data-live-layout-edit]');
  const saveButton=toolbar.querySelector('[data-live-layout-save]');
  const resetButton=toolbar.querySelector('[data-live-layout-reset]');
  const expandAll=toolbar.querySelector('[data-live-expand-all]');
  const collapseAll=toolbar.querySelector('[data-live-collapse-all]');
  const allServers=toolbar.querySelector('[data-live-layout-all-servers]');
  const note=toolbar.querySelector('[data-live-layout-note]');
  const allowedSizes=[3,4,6,8,12];
  const sizeLabels={3:'1/4',4:'1/3',6:'1/2',8:'2/3',12:'Voll'};
  let editing=false;
  let dragged=null;

  const sections=()=>Array.from(grid.children).filter(node=>node instanceof HTMLElement&&node.matches('section.panel[id]'));
  const applyOrder=order=>{order.forEach(id=>{const node=document.getElementById(id);if(node&&node.parentElement===grid)grid.appendChild(node);});};
  applyOrder(storedOrder);

  const normalizeSize=value=>{const n=Number.parseInt(String(value),10);return allowedSizes.includes(n)?n:12;};
  const setSize=(section,value)=>{
    const span=normalizeSize(value);
    section.dataset.liveModuleSpan=String(span);
    section.style.setProperty('--live-module-span',String(span));
    const select=section.querySelector('[data-live-module-size]');
    if(select)select.value=String(span);
  };

  const setCollapsed=(section,collapsed)=>{
    section.classList.toggle('live-module-collapsed',collapsed);
    const toggle=section.querySelector('[data-live-module-toggle]');
    if(toggle){toggle.setAttribute('aria-expanded',collapsed?'false':'true');toggle.textContent=collapsed?'Aufklappen':'Einklappen';}
  };

  const moveSection=(section,direction)=>{
    const list=sections();const index=list.indexOf(section);if(index<0)return;
    if(direction<0&&index>0)grid.insertBefore(section,list[index-1]);
    if(direction>0&&index<list.length-1)grid.insertBefore(list[index+1],section);
  };

  sections().forEach(section=>{
    const header=section.querySelector(':scope > .panel-header');if(!header)return;
    const controls=document.createElement('div');controls.className='live-module-controls';
    const drag=document.createElement('button');drag.type='button';drag.className='live-module-drag';drag.textContent='Verschieben';drag.title='Kachel ziehen';drag.setAttribute('aria-label','Kachel verschieben');drag.draggable=true;
    const up=document.createElement('button');up.type='button';up.className='live-module-move';up.textContent='↑';up.title='Kachel nach oben';up.addEventListener('click',()=>moveSection(section,-1));
    const down=document.createElement('button');down.type='button';down.className='live-module-move';down.textContent='↓';down.title='Kachel nach unten';down.addEventListener('click',()=>moveSection(section,1));
    const size=document.createElement('select');size.className='live-module-size';size.dataset.liveModuleSize='';size.title='Kachelbreite';size.setAttribute('aria-label','Kachelbreite');
    allowedSizes.forEach(span=>{const option=document.createElement('option');option.value=String(span);option.textContent=`Breite ${sizeLabels[span]}`;size.appendChild(option);});
    size.addEventListener('change',()=>setSize(section,size.value));
    const toggle=document.createElement('button');toggle.type='button';toggle.className='live-module-toggle';toggle.dataset.liveModuleToggle='';toggle.addEventListener('click',()=>setCollapsed(section,!section.classList.contains('live-module-collapsed')));
    controls.append(drag,up,down,size,toggle);header.appendChild(controls);
    setSize(section,storedSizes[section.id]??12);
    setCollapsed(section,storedCollapsed.has(section.id));

    drag.addEventListener('dragstart',event=>{if(!editing){event.preventDefault();return;}dragged=section;section.classList.add('is-dragging');event.dataTransfer?.setData('text/plain',section.id);if(event.dataTransfer)event.dataTransfer.effectAllowed='move';});
    drag.addEventListener('dragend',()=>{section.classList.remove('is-dragging');dragged=null;});
  });

  grid.addEventListener('dragover',event=>{
    if(!editing||!dragged)return;event.preventDefault();
    const target=event.target instanceof Element?event.target.closest('section.panel[id]'):null;
    if(!target||target===dragged||target.parentElement!==grid)return;
    const rect=target.getBoundingClientRect();
    const before=event.clientY<rect.top+rect.height/2;
    grid.insertBefore(dragged,before?target:target.nextSibling);
  });

  const setEditing=value=>{
    editing=Boolean(value);grid.classList.toggle('is-layout-editing',editing);toolbar.classList.toggle('is-layout-editing',editing);
    if(editButton)editButton.textContent=editing?'Bearbeiten beenden':'Layout bearbeiten';
    if(note)note.textContent=editing?'Kacheln ziehen, mit ↑/↓ verschieben und die Breite wählen. Danach Layout speichern.':'Auf-/zuklappen, Breite wählen oder persönliche Anordnung bearbeiten.';
  };
  editButton?.addEventListener('click',()=>setEditing(!editing));
  expandAll?.addEventListener('click',()=>sections().forEach(section=>setCollapsed(section,false)));
  collapseAll?.addEventListener('click',()=>sections().forEach(section=>setCollapsed(section,true)));

  const postLayout=async(operation)=>{
    if(!endpoint)throw new Error('Layout-Endpunkt fehlt.');
    const params=new URLSearchParams();params.set('_csrf',csrf);params.set('operation',operation);params.set('all_servers',allServers?.checked?'1':'0');
    if(operation==='save'){
      const sizes={};sections().forEach(section=>{sizes[section.id]=normalizeSize(section.dataset.liveModuleSpan||12);});
      params.set('layout_json',JSON.stringify({order:sections().map(section=>section.id),collapsed:sections().filter(section=>section.classList.contains('live-module-collapsed')).map(section=>section.id),sizes}));
    }
    const response=await fetch(endpoint,{method:'POST',headers:{Accept:'application/json','Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},credentials:'same-origin',body:params.toString()});
    const data=await response.json().catch(()=>({ok:false,error:`HTTP ${response.status}`}));if(!response.ok||!data.ok)throw new Error(data.error||`HTTP ${response.status}`);return data;
  };

  saveButton?.addEventListener('click',async()=>{saveButton.disabled=true;try{await postLayout('save');setEditing(false);if(note)note.textContent=allServers?.checked?'Layout, Breiten und Kachelzustände wurden für alle Server gespeichert.':'Persönliches Layout inklusive Kachelbreiten wurde für diesen Server gespeichert.';}catch(error){if(note)note.textContent=`Speichern fehlgeschlagen: ${error?.message||error}`;}finally{saveButton.disabled=false;}});
  resetButton?.addEventListener('click',async()=>{if(!confirm(allServers?.checked?'Persönliches Live-Layout für alle Server zurücksetzen?':'Persönliches Live-Layout für diesen Server zurücksetzen?'))return;resetButton.disabled=true;try{await postLayout('reset');window.location.reload();}catch(error){if(note)note.textContent=`Zurücksetzen fehlgeschlagen: ${error?.message||error}`;resetButton.disabled=false;}});
});

// v1.2.4: Dashboard-Serverkacheln. Aktualisiert alle sichtbaren Server alle 5 Sekunden.
document.addEventListener('DOMContentLoaded',()=>{
  const root=document.querySelector('[data-dashboard-live-root]');
  if(!root) return;

  const refreshButton=document.querySelector('[data-dashboard-live-refresh]');
  const refreshedAt=document.querySelector('[data-dashboard-live-refresh-time]');
  let busy=false;

  const clear=node=>{while(node?.firstChild) node.removeChild(node.firstChild);};

  const playerChip=(serverId,player,team)=>{
    const wrap=document.createElement('div');wrap.className='dashboard-player-row';
    const a=document.createElement('a');
    const stateClass=team==='Spectator'?'is-spec':(player.alive?'is-alive':'is-dead');
    a.className=`dashboard-player-chip ${stateClass}`;
    a.href=`/servers/${encodeURIComponent(serverId)}/live/player/${encodeURIComponent(player.steam_id64||'')}`;

    const name=document.createElement('span');
    name.className='dashboard-player-chip-name';
    name.textContent=player.name||'Unbekannt';
    a.appendChild(name);

    const time=document.createElement('span');
    time.className='dashboard-player-chip-time';
    time.textContent=player.connected_human||'0s';
    a.appendChild(time);
    wrap.appendChild(a);

    if(player.steam_id64 && document.querySelector('[data-player-action-modal]')){
      const quick=document.createElement('button');quick.type='button';quick.className='btn btn-secondary btn-small player-action-button';quick.textContent='…';quick.title='Spieleraktionen';quick.dataset.playerActions='';quick.dataset.playerSteamid=String(player.steam_id64);quick.dataset.playerName=String(player.name||'Unbekannt');quick.dataset.playerServerId=String(serverId);wrap.appendChild(quick);
    }
    return wrap;
  };

  const updateTeam=(card,server,team)=>{
    const section=card.querySelector(`[data-dash-team="${team}"]`);
    if(!section) return;

    const list=section.querySelector('[data-dash-player-list]');
    const count=section.querySelector('.dashboard-team-strip-head span');
    const players=Array.isArray(server.teams?.[team])?server.teams[team]:[];

    if(count) count.textContent=String(players.length);
    if(!list) return;
    clear(list);

    if(players.length===0){
      const empty=document.createElement('span');
      empty.className='dashboard-team-empty';
      empty.textContent='keine';
      list.appendChild(empty);
      return;
    }

    players.slice(0,5).forEach(player=>list.appendChild(playerChip(server.id,player,team)));

    if(players.length>5){
      const more=document.createElement('span');
      more.className='dashboard-more-players';
      more.textContent=`+${players.length-5} weitere`;
      list.appendChild(more);
    }
  };

  const renderDashboardChat=(card,server)=>{
    const list=card.querySelector('[data-dash-chat-list]'); if(!list)return;
    clear(list);
    const messages=Array.isArray(server.recent_chat)?server.recent_chat:[];
    if(messages.length===0){const empty=document.createElement('div');empty.className='muted dashboard-chat-empty';empty.textContent='Noch keine Chatnachrichten gespeichert.';list.appendChild(empty);}
    messages.forEach(m=>{
      const role=String(m.community_role||'spieler').toLowerCase();
      const line=document.createElement('div');line.className=`dashboard-game-chat-line game-chat-role-${role}`;
      const time=document.createElement('span');time.className='dashboard-game-chat-time';try{time.textContent=new Date(m.sent_at).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit',second:'2-digit'});}catch{time.textContent='—';}
      const name=document.createElement('strong');name.className=`game-role-text game-role-text-${role}`;name.textContent=m.player_name||'Unbekannt';
      const text=document.createElement('span');text.className='dashboard-game-chat-text';text.textContent=m.message||'';
      line.append(time,name,text);
      if(m.steam_id64 && document.querySelector('[data-player-action-modal]')){const quick=document.createElement('button');quick.type='button';quick.className='btn btn-secondary btn-small player-action-button';quick.textContent='…';quick.title='Spieleraktionen';quick.dataset.playerActions='';quick.dataset.playerSteamid=String(m.steam_id64);quick.dataset.playerName=String(m.player_name||'Unbekannt');quick.dataset.playerServerId=String(server.id);quick.dataset.playerChatId=String(m.id||0);line.appendChild(quick);}list.appendChild(line);
    });
    const archive=document.createElement('a');archive.className='dashboard-chat-archive-link';archive.href=`/servers/${encodeURIComponent(server.id)}/chat`;archive.textContent='Chat-Archiv durchsuchen';list.appendChild(archive);
    const total=card.querySelector('[data-dash-chat-total]');if(total)total.textContent=String(Number(server.chat_total||0));
  };

  const setText=(card,selector,value)=>{
    const node=card.querySelector(selector);
    if(node) node.textContent=value;
  };

  const legacyRequests=new Map();
  const refreshLegacy=server=>{
    if(!server.can_live || !server.requires_live_refresh || legacyRequests.has(server.id)) return;
    const controller=new AbortController();
    const timeout=setTimeout(()=>controller.abort(),8000);
    legacyRequests.set(server.id,controller);
    fetch(`/dashboard/servers/${encodeURIComponent(server.id)}/live-status`,{
      headers:{Accept:'application/json'}, credentials:'same-origin',cache:'no-store',signal:controller.signal
    }).then(r=>{if(!r.ok)throw new Error('status');return r.json();}).then(updateCard)
      .catch(()=>{ /* Retain dated cache; the next normal refresh shows its age. */ })
      .finally(()=>{clearTimeout(timeout);legacyRequests.delete(server.id);});
  };

  const updateCard=server=>{
    const card=root.querySelector(`[data-dashboard-server="${CSS.escape(String(server.id))}"]`);
    if(!card) return;

    if(server.can_live){
      card.classList.toggle('is-online',Boolean(server.bridge_online));
      card.classList.toggle('is-offline',!server.bridge_online);

      const bridge=card.querySelector('[data-dash-bridge]');
      if(bridge){
        bridge.textContent=server.bridge_online?'ONLINE':(server.bridge_available?'STATUS VERALTET':'KEINE DATEN');
        bridge.className=`status-pill ${server.bridge_online?'status-online':'status-offline'}`;
      }

      setText(card,'[data-dash-framework]',server.framework||'Nicht gescannt');
      setText(card,'[data-dash-map]',server.map||'—');
      setText(card,'[data-dash-next-map]',server.next_map||'nicht geplant');
      setText(card,'[data-dash-player-count]',`${server.human_players??0}${server.max_players!==null&&server.max_players!==undefined?` / ${server.max_players}`:''}`);
      setText(card,'[data-dash-t-count]',String(server.t_count??0));
      setText(card,'[data-dash-ct-count]',String(server.ct_count??0));
      setText(card,'[data-dash-spec-count]',String(server.spectator_count??0));
      setText(card,'[data-dash-bot-count]',String(server.bots??0));
      setText(card,'[data-dash-updated]',server.updated_at||'—');

      const switchState=card.querySelector('[data-dash-switch-state]');
      if(switchState){
        switchState.classList.toggle('hidden',!server.map_switch_in_flight);
      }

      updateTeam(card,server,'T');
      updateTeam(card,server,'CT');
      updateTeam(card,server,'Spectator');
      renderDashboardChat(card,server);

      const reason=card.querySelector('[data-dash-reason]');
      if(reason){
        reason.textContent=server.reason||'Live-Bridge nicht erreichbar oder Status veraltet.';
        reason.classList.toggle('hidden',Boolean(server.bridge_online));
      }
    }
  };

  const refresh=async()=>{
    if(busy) return;
    busy=true;
    refreshButton?.setAttribute('disabled','disabled');

    try{
      const response=await fetch('/dashboard/live-status',{
        headers:{'Accept':'application/json'},
        credentials:'same-origin',
        cache:'no-store'
      });

      if(!response.ok) throw new Error(`HTTP ${response.status}`);
      const data=await response.json();
      (Array.isArray(data.servers)?data.servers:[]).forEach(server=>{updateCard(server);refreshLegacy(server);});

      const onlineRoot=document.querySelector('[data-panel-users-online]');
      if(onlineRoot && Array.isArray(data.panel_users)){
        clear(onlineRoot);
        if(data.panel_users.length===0){
          const empty=document.createElement('span');
          empty.className='muted';
          empty.textContent='Aktuell kein Webpanel-Nutzer aktiv.';
          onlineRoot.appendChild(empty);
        }else{
          data.panel_users.forEach(user=>{
            const chip=document.createElement('div');
            chip.className='panel-user-online-chip';
            if(user.steam_verified){
              const img=document.createElement('img');
              img.src=`/users/${encodeURIComponent(user.id)}/steam-avatar`;
              img.alt='';
              chip.appendChild(img);
            }
            const text=document.createElement('div');
            const strong=document.createElement('strong');
            strong.textContent=user.display_name||'Unbekannt';
            const span=document.createElement('span');
            span.textContent=(user.web_role_label||'')+(user.steam_verified?' · Steam':'');
            text.append(strong,span);
            chip.appendChild(text);
            onlineRoot.appendChild(chip);
          });
        }
      }

      const unread=Number(data.unread_announcements??0);
      document.querySelectorAll('[data-notification-count]').forEach(node=>node.textContent=String(unread));
      const dashboardUnread=document.querySelector('[data-announcement-unread]');
      if(dashboardUnread) dashboardUnread.textContent=String(unread);

      if(refreshedAt){
        const now=new Date();
        refreshedAt.textContent=`Aktualisiert ${now.toLocaleTimeString([], {hour:'2-digit',minute:'2-digit',second:'2-digit'})}`;
      }
    }catch(error){
      if(refreshedAt) refreshedAt.textContent='Live-Aktualisierung fehlgeschlagen';
    }finally{
      busy=false;
      refreshButton?.removeAttribute('disabled');
    }
  };

  refreshButton?.addEventListener('click',refresh);
  window.setInterval(refresh,5000);
});


// v1.4.2: Sichere Suche in der installierten Workshop-Mapliste.
// Enter im Suchfeld darf niemals einen Mapwechsel auslösen.
document.addEventListener('DOMContentLoaded',()=>{
  document.querySelectorAll('[data-map-search]').forEach(input=>{
    const form=input.closest('form');
    const select=form?.querySelector('[data-map-select]');
    const count=form?.querySelector('[data-map-search-count]');
    const resetButton=form?.querySelector('[data-map-search-reset]');
    if(!form || !select) return;

    const options=[...select.options].filter(option=>option.value!=='');

    const applyFilter=()=>{
      const q=(input.value||'').trim().toLocaleLowerCase();
      let visible=0;

      options.forEach(option=>{
        const haystack=`${option.textContent||''} ${option.value||''}`.toLocaleLowerCase();
        const match=!q || haystack.includes(q);
        option.hidden=!match;
        option.disabled=!match;
        if(match) visible++;
      });

      // Eine bereits gewählte Map, die durch den neuen Filter nicht mehr
      // sichtbar ist, wird sicherheitshalber abgewählt. So kann kein alter
      // Treffer versehentlich gestartet werden.
      if(select.selectedOptions[0]?.disabled) select.value='';

      if(count){
        count.textContent=q
          ? `${visible} von ${options.length} Maps`
          : `${options.length} Maps verfügbar`;
      }
    };

    const resetFilter=()=>{
      input.value='';
      options.forEach(option=>{
        option.hidden=false;
        option.disabled=false;
      });
      if(count) count.textContent=`${options.length} Maps verfügbar`;
      input.focus();
    };

    input.addEventListener('input',applyFilter);
    input.addEventListener('search',applyFilter);

    input.addEventListener('keydown',event=>{
      if(event.key==='Enter'){
        // Wichtig: Ein Suchfeld in einem <form> würde sonst den ersten
        // Submit-Button auslösen und damit den Mapwechsel starten.
        event.preventDefault();
        event.stopPropagation();
        applyFilter();
        return;
      }

      if(event.key==='Escape'){
        event.preventDefault();
        resetFilter();
      }
    });

    // Zusätzliche zweite Schutzschicht gegen Browser, die trotz keydown
    // einen impliziten Formular-Submit auslösen.
    form.addEventListener('submit',event=>{
      if(document.activeElement===input){
        event.preventDefault();
        event.stopPropagation();
      }
    });

    resetButton?.addEventListener('click',event=>{
      event.preventDefault();
      resetFilter();
    });

    applyFilter();
  });
});


// v1.4.1: Dashboard-Chat – letzte 30 Nachrichten, 5-Sekunden-Liveaktualisierung.
document.addEventListener('DOMContentLoaded',()=>{
  const list=document.querySelector('[data-chat-list]');
  const form=document.querySelector('[data-chat-form]');
  if(!list || !form) return;

  const status=document.querySelector('[data-chat-status]');
  const canModerate=list.dataset.canModerate==='1';

  const renderMessage=(m)=>{
    const article=document.createElement('article');
    article.className='chat-message';
    article.dataset.chatId=String(m.id);

    const avatarWrap=document.createElement('div');
    avatarWrap.className='chat-avatar-wrap';
    if(m.steam_verified){
      const img=document.createElement('img');
      img.className='chat-avatar';
      img.src=`/users/${encodeURIComponent(m.user_id)}/steam-avatar`;
      img.alt='';
      avatarWrap.appendChild(img);
    }else{
      const fallback=document.createElement('span');
      fallback.className='chat-avatar chat-avatar-fallback';
      fallback.textContent=(m.display_name||'?').trim().charAt(0).toUpperCase()||'?';
      avatarWrap.appendChild(fallback);
    }

    const body=document.createElement('div');
    body.className='chat-body';
    const meta=document.createElement('div');
    meta.className='chat-meta';
    const strong=document.createElement('strong');
    strong.textContent=m.display_name||'Unbekannt';
    const span=document.createElement('span');
    span.textContent=`${m.web_role_label||''} · ${m.created_at||''}`;
    meta.append(strong,span);
    const text=document.createElement('div');
    text.className='chat-text';
    text.textContent=m.message||'';
    body.append(meta,text);

    article.append(avatarWrap,body);

    if(canModerate){
      const deleteForm=document.createElement('form');
      deleteForm.className='chat-delete-form';
      deleteForm.action=`/dashboard/chat/${encodeURIComponent(m.id)}/delete`;
      deleteForm.method='post';
      deleteForm.dataset.chatDelete='';
      const csrf=form.querySelector('input[name="_csrf"]')?.cloneNode(true);
      if(csrf) deleteForm.appendChild(csrf);
      const button=document.createElement('button');
      button.className='link-button';
      button.type='submit';
      button.textContent='Löschen';
      deleteForm.appendChild(button);
      article.appendChild(deleteForm);
    }

    return article;
  };

  const replaceMessages=(messages)=>{
    const wasNearBottom=list.scrollHeight-list.scrollTop-list.clientHeight<80;
    list.replaceChildren();
    if(!messages.length){
      const empty=document.createElement('div');
      empty.className='chat-empty muted';
      empty.textContent='Noch keine Chatnachrichten.';
      list.appendChild(empty);
    }else{
      messages.forEach(m=>list.appendChild(renderMessage(m)));
    }
    if(wasNearBottom || !list.dataset.initialized){
      list.scrollTop=list.scrollHeight;
    }
    list.dataset.initialized='1';
  };

  const refresh=async()=>{
    try{
      const res=await fetch('/dashboard/chat/messages',{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});
      if(!res.ok) throw new Error(`HTTP ${res.status}`);
      const data=await res.json();
      replaceMessages(Array.isArray(data.messages)?data.messages:[]);
      if(status) status.textContent='Live';
    }catch{
      if(status) status.textContent='Verbindung unterbrochen';
    }
  };

  form.addEventListener('submit',async(e)=>{
    e.preventDefault();
    const button=form.querySelector('button[type="submit"]');
    button?.setAttribute('disabled','disabled');
    try{
      const res=await fetch(form.action,{
        method:'POST',
        body:new FormData(form),
        headers:{Accept:'application/json'},
        credentials:'same-origin'
      });
      const data=await res.json().catch(()=>({}));
      if(!res.ok || data.ok===false) throw new Error(data.error||'Nachricht konnte nicht gesendet werden.');
      form.reset();
      await refresh();
    }catch(error){
      if(status) status.textContent=error.message||'Senden fehlgeschlagen';
    }finally{
      button?.removeAttribute('disabled');
    }
  });

  list.addEventListener('submit',async(e)=>{
    const deleteForm=e.target.closest('[data-chat-delete]');
    if(!deleteForm) return;
    e.preventDefault();
    try{
      const res=await fetch(deleteForm.action,{
        method:'POST',
        body:new FormData(deleteForm),
        headers:{Accept:'application/json'},
        credentials:'same-origin'
      });
      if(!res.ok) throw new Error('Löschen fehlgeschlagen');
      await refresh();
    }catch{
      if(status) status.textContent='Löschen fehlgeschlagen';
    }
  });

  list.scrollTop=list.scrollHeight;
  window.setInterval(refresh,5000);
});


// v1.5.4: Whitelist Live Manager
document.addEventListener('DOMContentLoaded',()=>{
  const normalize=value=>(value||'').toLocaleLowerCase().trim();

  const bindFilter=(inputSelector,rowSelector)=>{
    document.querySelectorAll(inputSelector).forEach(input=>{
      const panel=input.closest('.whitelist-column')||document;
      const rows=[...panel.querySelectorAll(rowSelector)];
      const apply=()=>{
        const q=normalize(input.value);
        rows.forEach(row=>{
          const haystack=normalize(row.dataset.search||row.textContent);
          row.hidden=!!q&&!haystack.includes(q);
        });
      };
      input.addEventListener('input',apply);
      input.addEventListener('search',apply);
    });
  };

  bindFilter('[data-whitelist-current-search]','[data-whitelist-current]');
  bindFilter('[data-whitelist-player-search]','[data-whitelist-candidate]');

  document.querySelectorAll('[data-whitelist-select-visible]').forEach(button=>{
    button.addEventListener('click',()=>{
      const form=button.closest('form');
      if(!form)return;
      const boxes=[...form.querySelectorAll('[data-whitelist-candidate]:not([hidden]) input[type="checkbox"]')];
      const shouldCheck=boxes.some(box=>!box.checked);
      boxes.forEach(box=>box.checked=shouldCheck);
      button.textContent=shouldCheck?'Sichtbare abwählen':'Sichtbare markieren';
    });
  });
});

// v1.6.6: Whitelist echte Gameserver-Bestätigung
document.addEventListener('DOMContentLoaded',()=>{
  document.querySelectorAll('[data-whitelist-pending]').forEach(card=>{
    const serverId=Number(card.dataset.serverId||0);
    const token=String(card.dataset.token||'');
    const title=card.querySelector('[data-whitelist-pending-title]');
    const text=card.querySelector('[data-whitelist-pending-text]');
    const refresh=card.querySelector('[data-whitelist-pending-refresh]');
    const indicator=card.querySelector('[data-whitelist-pending-indicator]');
    if(!serverId||!token)return;

    let stopped=false;

    refresh?.addEventListener('click',()=>window.location.reload());

    const setState=(state,message)=>{
      card.classList.remove('is-confirmed','is-failed','is-timeout');
      if(state==='confirmed')card.classList.add('is-confirmed');
      if(state==='failed')card.classList.add('is-failed');
      if(state==='timeout')card.classList.add('is-timeout');

      if(title){
        title.textContent=state==='confirmed'
          ?'Vom Gameserver bestätigt'
          :state==='failed'
            ?'Änderung fehlgeschlagen'
            :state==='timeout'
              ?'Noch nicht bestätigt'
              :'Änderung wird auf dem Gameserver bestätigt …';
      }
      if(text&&message)text.textContent=message;
      if(indicator)indicator.setAttribute('data-state',state);
    };

    const verify=async()=>{
      if(stopped)return;

      try{
        const response=await fetch(
          `/servers/${serverId}/live/whitelist/verify?token=${encodeURIComponent(token)}`,
          {headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'}
        );
        const data=await response.json().catch(()=>({ok:false,error:'Ungültige Serverantwort.'}));

        if(!response.ok||data.ok===false){
          if(response.status===404){
            stopped=true;
            return;
          }
          throw new Error(data.error||'Bestätigung konnte nicht geprüft werden.');
        }

        const state=String(data.state||'pending');
        const progress=[];
        if(Number(data.added_total||0)>0){
          progress.push(`${Number(data.added_confirmed||0)}/${Number(data.added_total)} hinzugefügt`);
        }
        if(Number(data.removed_total||0)>0){
          progress.push(`${Number(data.removed_confirmed||0)}/${Number(data.removed_total)} entfernt`);
        }
        const message=[data.message,...progress].filter(Boolean).join(' · ');
        setState(state,message);

        if(state==='confirmed'){
          stopped=true;

          // Die echte Datei ist jetzt korrekt. Die lokale DOM-Liste kurz sichtbar
          // anpassen und anschließend den Serverzustand vollständig neu laden.
          document.querySelectorAll('.whitelist-row-pending-remove').forEach(row=>{
            row.classList.add('whitelist-row-confirmed-remove');
          });
          document.querySelectorAll('.whitelist-row-pending-add').forEach(row=>{
            row.classList.add('whitelist-row-confirmed-add');
          });

          window.setTimeout(()=>window.location.reload(),800);
          return;
        }

        if(state==='failed'||state==='timeout'){
          stopped=true;
          if(refresh)refresh.hidden=false;
          return;
        }

        window.setTimeout(verify,2000);
      }catch(error){
        setState('pending',error.message||'Bestätigung wird erneut versucht.');
        window.setTimeout(verify,2500);
      }
    };

    window.setTimeout(verify,900);
  });
});


// v1.5.9: Admin-Log Filter
// v1.6.2: Pagination mit 10 Einträgen pro Seite
document.addEventListener('DOMContentLoaded',()=>{
  document.querySelectorAll('.admin-log-panel').forEach(panel=>{
    const search=panel.querySelector('[data-admin-log-search]');
    const status=panel.querySelector('[data-admin-log-status]');
    const rows=[...panel.querySelectorAll('[data-admin-log-row]')];
    const empty=panel.querySelector('[data-admin-log-empty]');
    const pagination=panel.querySelector('[data-admin-log-pagination]');
    const newer=panel.querySelector('[data-admin-log-newer]');
    const older=panel.querySelector('[data-admin-log-older]');
    const pageInfo=panel.querySelector('[data-admin-log-page-info]');

    const pageSize=10;
    let currentPage=1;

    const filteredRows=()=>{
      const q=(search?.value||'').toLocaleLowerCase().trim();
      const filter=(status?.value||'all').toLocaleLowerCase();

      return rows.filter(row=>{
        const haystack=(row.dataset.search||row.textContent||'').toLocaleLowerCase();
        const rowStatus=(row.dataset.status||'').toLocaleLowerCase();
        return (!q||haystack.includes(q))
          && (filter==='all'||rowStatus===filter);
      });
    };

    const render=()=>{
      const matches=filteredRows();
      const totalPages=Math.max(1,Math.ceil(matches.length/pageSize));

      if(currentPage>totalPages)currentPage=totalPages;
      if(currentPage<1)currentPage=1;

      rows.forEach(row=>row.hidden=true);

      if(matches.length){
        const start=(currentPage-1)*pageSize;
        matches.slice(start,start+pageSize).forEach(row=>row.hidden=false);
      }

      if(empty)empty.hidden=matches.length>0||rows.length===0;
      if(pageInfo)pageInfo.textContent=`Seite ${matches.length?currentPage:0} / ${matches.length?totalPages:0}`;
      if(newer)newer.disabled=currentPage<=1||matches.length===0;
      if(older)older.disabled=currentPage>=totalPages||matches.length===0;
      if(pagination)pagination.hidden=matches.length<=pageSize;
    };

    const resetAndRender=()=>{
      currentPage=1;
      render();
    };

    search?.addEventListener('input',resetAndRender);
    search?.addEventListener('search',resetAndRender);
    status?.addEventListener('change',resetAndRender);

    newer?.addEventListener('click',()=>{
      if(currentPage>1){
        currentPage--;
        render();
      }
    });

    older?.addEventListener('click',()=>{
      const totalPages=Math.max(1,Math.ceil(filteredRows().length/pageSize));
      if(currentPage<totalPages){
        currentPage++;
        render();
      }
    });

    render();
  });
});

// v1.6.0: Webpanel user drawer
document.addEventListener('DOMContentLoaded',()=>{
  const drawer=document.querySelector('[data-panel-users-drawer]');
  if(!drawer)return;

  const toggle=drawer.querySelector('[data-panel-users-toggle]');
  const closeButtons=[...drawer.querySelectorAll('[data-panel-users-close]')];
  const back=drawer.querySelector('[data-panel-user-back]');
  const listView=drawer.querySelector('[data-panel-users-list-view]');
  const detailView=drawer.querySelector('[data-panel-user-detail-view]');
  const list=drawer.querySelector('[data-panel-users-list]');
  const detail=drawer.querySelector('[data-panel-user-detail]');
  const summary=drawer.querySelector('[data-panel-users-summary]');
  const onlineBadge=drawer.querySelector('[data-panel-users-online]');
  const unreadBadge=drawer.querySelector('[data-panel-users-unread]');
  const csrf=document.querySelector('meta[name="csrf-token"]')?.content||'';

  let selectedUserId=null;
  let refreshTimer=null;
  let detailRequestSeq=0;

  const esc=s=>String(s??'').replace(/[&<>"']/g,ch=>({
    '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'
  }[ch]));

  const roleLabel=role=>({founder:'Founder',admin:'Admin',member:'Member',vip:'VIP',spieler:'Spieler'}[role]||'Spieler');

  const formatDate=value=>{
    if(!value)return '—';
    const d=new Date(value);
    if(Number.isNaN(d.getTime()))return String(value);
    return new Intl.DateTimeFormat('de-DE',{
      day:'2-digit',month:'2-digit',year:'2-digit',
      hour:'2-digit',minute:'2-digit',second:'2-digit'
    }).format(d);
  };

  const initials=name=>{
    const parts=String(name||'?').trim().split(/\s+/).filter(Boolean);
    return (parts.slice(0,2).map(p=>p[0]?.toUpperCase()||'').join('')||'?');
  };

  const openDrawer=()=>{
    drawer.classList.add('is-open');
    toggle?.setAttribute('aria-expanded','true');
    localStorage.setItem('panelUsersDrawerOpen','1');
  };
  const closeDrawer=()=>{
    drawer.classList.remove('is-open');
    toggle?.setAttribute('aria-expanded','false');
    localStorage.setItem('panelUsersDrawerOpen','0');
  };
  const showList=()=>{
    selectedUserId=null;
    detailView.hidden=true;
    listView.hidden=false;
  };
  const showDetail=()=>{
    listView.hidden=true;
    detailView.hidden=false;
  };

  toggle?.addEventListener('click',()=>{
    drawer.classList.contains('is-open')?closeDrawer():openDrawer();
  });
  closeButtons.forEach(btn=>btn.addEventListener('click',closeDrawer));
  back?.addEventListener('click',showList);

  if(localStorage.getItem('panelUsersDrawerOpen')==='1')openDrawer();

  const post=async(url,data)=>{
    const body=new URLSearchParams();
    Object.entries(data).forEach(([key,value])=>{
      if(value!==undefined&&value!==null)body.set(key,String(value));
    });
    body.set('_csrf',csrf);

    const response=await fetch(url,{
      method:'POST',
      headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8','Accept':'application/json'},
      body:body.toString(),
      credentials:'same-origin'
    });
    const payload=await response.json().catch(()=>({ok:false,error:'Ungültige Serverantwort.'}));
    if(!response.ok||!payload.ok)throw new Error(payload.error||'Anfrage fehlgeschlagen.');
    return payload;
  };

  const renderUsers=users=>{
    const groups={founder:[],admin:[],member:[],vip:[],spieler:[]};
    users.forEach(user=>(groups[user.web_role]||groups.spieler).push(user));

    list.textContent='';
    ['founder','admin','member','vip','spieler'].forEach(role=>{
      const usersInRole=groups[role];
      if(!usersInRole.length)return;

      const section=document.createElement('section');
      section.className='panel-user-role-group';

      const head=document.createElement('div');
      head.className='panel-user-role-heading';
      const online=usersInRole.filter(u=>u.online).length;
      head.innerHTML=`<span>${roleLabel(role)}</span><span>${online}/${usersInRole.length} online</span>`;
      section.appendChild(head);

      usersInRole.forEach(user=>{
        const button=document.createElement('button');
        button.type='button';
        button.className=`panel-user-nav-item role-${user.web_role}`;
        button.dataset.userId=user.id;

        const avatar=document.createElement('span');
        avatar.className='panel-user-nav-avatar';
        if(user.avatar_url){
          const img=document.createElement('img');
          img.src=user.avatar_url;
          img.alt='';
          avatar.appendChild(img);
        }else{
          avatar.textContent=initials(user.display_name);
        }

        const main=document.createElement('span');
        main.className='panel-user-nav-main';
        const strong=document.createElement('strong');
        strong.textContent=user.display_name+(user.is_me?' (Du)':'');
        const sub=document.createElement('span');
        sub.textContent=user.active
          ? `${user.role_label} · ${user.online?'Online':'Offline'}`
          : `${user.role_label} · Gesperrt`;
        main.append(strong,sub);

        const state=document.createElement('span');
        state.className='panel-user-nav-state';
        const dot=document.createElement('span');
        dot.className='presence-dot '+(user.active?(user.online?'online':''):'locked');
        state.appendChild(dot);
        if(user.unread_count>0){
          const unread=document.createElement('span');
          unread.className='panel-user-unread';
          unread.textContent=user.unread_count>99?'99+':user.unread_count;
          state.appendChild(unread);
        }

        button.append(avatar,main,state);
        button.addEventListener('click',()=>{
          openDrawer();
          loadDetail(user.id);
        });
        section.appendChild(button);
      });

      list.appendChild(section);
    });

    if(!users.length){
      list.innerHTML='<div class="muted panel-users-loading">Keine Webpanel-Nutzer vorhanden.</div>';
    }
  };

  const loadSidebar=async()=>{
    try{
      const response=await fetch('/panel-users/sidebar',{headers:{'Accept':'application/json'},credentials:'same-origin',cache:'no-store'});
      if(!response.ok)return;
      const data=await response.json();
      if(!data.ok)return;

      renderUsers(data.users||[]);
      const active=(data.users||[]).filter(u=>u.active);
      const online=active.filter(u=>u.online).length;
      summary.textContent=`${online} online · ${active.length} aktiv`;
      onlineBadge.textContent=online;

      const unread=Number(data.unread_total||0);
      unreadBadge.textContent=unread>99?'99+':unread;
      unreadBadge.hidden=unread<=0;
      toggle?.classList.toggle('has-unread',unread>0);
    }catch{}
  };

  const messageHtml=message=>{
    return `<div class="panel-dm ${message.mine?'mine':''}">
      <div class="panel-dm-bubble">
        <div class="panel-dm-meta">
          <strong class="panel-user-detail-role role-${esc(message.sender_role)}">${esc(message.sender_display_name)}</strong>
          <span>${esc(formatDate(message.created_at))}</span>
        </div>
        <div class="panel-dm-text">${esc(message.message)}</div>
      </div>
    </div>`;
  };

  const renderDetail=(data,uiState=null)=>{
    const user=data.user;
    const messages=data.messages||[];
    const avatar=user.avatar_url
      ? `<img src="${esc(user.avatar_url)}" alt="">`
      : esc(initials(user.display_name));

    const email=user.email
      ? `<dt>E-Mail</dt><dd>${esc(user.email)}</dd>`
      : '';

    const steam=user.steam_id64
      ? `<dt>SteamID64</dt><dd class="mono">${esc(user.steam_id64)}</dd>`
      : `<dt>Steam</dt><dd>${user.steam_verified?'Verifiziert':'Nicht verbunden'}</dd>`;

    let manage='';
    if(user.can_manage){
      manage=`<section class="panel-user-manage">
        <h3>Rolle & Konto verwalten</h3>
        <form data-panel-user-account>
          <div class="panel-user-manage-grid">
            <label>Webrolle
              <select name="web_role">
                ${Object.entries(user.assignable_roles||{}).map(([value,label])=>`<option value="${esc(value)}" ${user.web_role===value?'selected':''}>${esc(label)}</option>`).join('')}
              </select>
            </label>
            <label class="checkbox-line">
              <input type="checkbox" name="active" value="1" ${user.active?'checked':''}> Konto aktiv
            </label>
          </div>
          <button class="btn btn-secondary btn-small top-gap" type="submit">Rolle speichern</button>
          <div class="small-muted" data-panel-user-account-status></div>
        </form>
      </section>`;
    }

    let conversation=messages.length
      ? messages.map(messageHtml).join('')
      : '<div class="muted panel-users-loading">Noch keine Direktnachrichten.</div>';

    let composer=user.can_message
      ? `<form class="panel-dm-form" data-panel-dm-form>
          <textarea name="message" maxlength="4000" rows="5" placeholder="Nachricht an ${esc(user.display_name)}…" required></textarea>
          <button class="btn btn-primary btn-small" type="submit">Senden</button>
         </form>
         <div class="small-muted" data-panel-dm-status></div>`
      : `<div class="panel-user-disabled-note">${user.is_me?'Eigenes Konto.':'Dieses Konto ist derzeit gesperrt; Nachrichten sind deaktiviert.'}</div>`;

    detail.innerHTML=`<div class="panel-user-detail-card">
      <div class="panel-user-detail-head">
        <div class="panel-user-detail-avatar">${avatar}</div>
        <div class="panel-user-detail-name">
          <strong>${esc(user.display_name)}${user.is_me?' (Du)':''}</strong>
          <span class="panel-user-detail-role role-${esc(user.web_role)}">${esc(user.role_label)}</span>
          <span>${user.active?(user.online?'Online':'Offline'):'Gesperrt'}</span>
        </div>
      </div>

      <dl class="panel-user-info-grid">
        <dt>Status</dt><dd>${user.active?(user.online?'Online':'Offline'):'Gesperrt'}</dd>
        <dt>Zuletzt gesehen</dt><dd>${esc(formatDate(user.last_seen_at))}</dd>
        <dt>Letzter Login</dt><dd>${esc(formatDate(user.last_login_at))}</dd>
        <dt>Konto seit</dt><dd>${esc(formatDate(user.created_at))}</dd>
        ${steam}
        ${email}
      </dl>

      ${manage}

      <section class="panel-user-messages">
        <h3>Direktnachrichten</h3>
        <div class="panel-user-conversation" data-panel-user-conversation>${conversation}</div>
        ${composer}
      </section>
    </div>`;

    const conv=detail.querySelector('[data-panel-user-conversation]');
    const composerTextarea=detail.querySelector('[data-panel-dm-form] textarea[name="message"]');
    const draftKey=`panelDmDraft:${user.id}`;

    // Entwürfe über Auto-Refresh hinweg erhalten. Zusätzlich bleibt ein Entwurf
    // innerhalb der Browser-Sitzung auch bei einem versehentlichen Drawer-Wechsel
    // erhalten, bis er erfolgreich gesendet oder manuell geleert wurde.
    const storedDraft=sessionStorage.getItem(draftKey)||'';
    const draft=uiState?.draft ?? storedDraft;
    if(composerTextarea&&draft)composerTextarea.value=draft;

    if(conv){
      if(uiState){
        const maxScroll=Math.max(0,conv.scrollHeight-conv.clientHeight);
        conv.scrollTop=uiState.convAtBottom?maxScroll:Math.min(uiState.convScroll,maxScroll);
      }else{
        conv.scrollTop=conv.scrollHeight;
      }
    }
    if(uiState){
      detail.scrollTop=uiState.detailScroll;
      if(composerTextarea&&uiState.composerFocused){
        composerTextarea.focus({preventScroll:true});
        const len=composerTextarea.value.length;
        const start=Math.min(Number(uiState.selectionStart??len),len);
        const end=Math.min(Number(uiState.selectionEnd??len),len);
        try{composerTextarea.setSelectionRange(start,end);}catch{}
      }
    }

    composerTextarea?.addEventListener('input',()=>{
      if(composerTextarea.value)sessionStorage.setItem(draftKey,composerTextarea.value);
      else sessionStorage.removeItem(draftKey);
    });

    detail.querySelector('[data-panel-dm-form]')?.addEventListener('submit',async event=>{
      event.preventDefault();
      const form=event.currentTarget;
      const textarea=form.querySelector('textarea[name="message"]');
      const statusNode=detail.querySelector('[data-panel-dm-status]');
      const text=textarea.value.trim();
      if(!text)return;

      try{
        form.querySelector('button').disabled=true;
        const payload=await post(`/panel-users/${user.id}/messages`,{message:text});
        textarea.value='';
        sessionStorage.removeItem(`panelDmDraft:${user.id}`);
        statusNode.textContent='Nachricht gesendet.';
        const conv=detail.querySelector('[data-panel-user-conversation]');
        if(conv){
          if(conv.querySelector('.panel-users-loading'))conv.textContent='';
          conv.insertAdjacentHTML('beforeend',messageHtml(payload.message));
          conv.scrollTop=conv.scrollHeight;
        }
        loadSidebar();
      }catch(error){
        statusNode.textContent=error.message;
      }finally{
        form.querySelector('button').disabled=false;
      }
    });

    detail.querySelector('[data-panel-user-account]')?.addEventListener('submit',async event=>{
      event.preventDefault();
      const form=event.currentTarget;
      const statusNode=form.querySelector('[data-panel-user-account-status]');
      const select=form.querySelector('select[name="web_role"]');
      const active=form.querySelector('input[name="active"]');

      try{
        form.querySelector('button').disabled=true;
        await post(`/panel-users/${user.id}/account`,{
          web_role:select.value,
          active:active.checked?'1':'0'
        });
        statusNode.textContent='Rolle/Kontostatus gespeichert.';
        await loadSidebar();
        await loadDetail(user.id,false,true);
      }catch(error){
        statusNode.textContent=error.message;
      }finally{
        form.querySelector('button').disabled=false;
      }
    });
  };

  const captureDetailUiState=()=>{
    const textarea=detail.querySelector('[data-panel-dm-form] textarea[name="message"]');
    const conv=detail.querySelector('[data-panel-user-conversation]');
    const threshold=24;
    return {
      draft:textarea?.value??'',
      composerFocused:document.activeElement===textarea,
      selectionStart:textarea?.selectionStart??null,
      selectionEnd:textarea?.selectionEnd??null,
      detailScroll:detail.scrollTop,
      convScroll:conv?.scrollTop??0,
      convAtBottom:conv?((conv.scrollHeight-conv.clientHeight-conv.scrollTop)<=threshold):true
    };
  };

  const loadDetail=async(userId,show=true,preserveUi=false)=>{
    const nextUserId=Number(userId);
    const sameUser=selectedUserId===nextUserId;
    const uiState=preserveUi&&sameUser?captureDetailUiState():null;
    selectedUserId=nextUserId;
    if(show)showDetail();
    if(!preserveUi)detail.innerHTML='<div class="muted panel-users-loading">Nutzer wird geladen…</div>';
    const requestSeq=++detailRequestSeq;
    try{
      const response=await fetch(`/panel-users/${selectedUserId}`,{
        headers:{'Accept':'application/json'},credentials:'same-origin',cache:'no-store'
      });
      const data=await response.json();
      if(requestSeq!==detailRequestSeq)return;
      if(!response.ok||!data.ok)throw new Error(data.error||'Nutzer konnte nicht geladen werden.');
      renderDetail(data,uiState);
      loadSidebar();
    }catch(error){
      if(requestSeq!==detailRequestSeq)return;
      if(!preserveUi){
        detail.innerHTML=`<div class="alert alert-error" style="margin:12px">${esc(error.message)}</div>`;
      }
    }
  };

  window.addEventListener('panel-users:open-user',event=>{
    const userId=Number(event.detail?.userId||0);
    if(!userId)return;
    openDrawer();
    loadDetail(userId);
  });

  loadSidebar();
  refreshTimer=setInterval(()=>{
    if(document.hidden)return;
    loadSidebar();
    if(selectedUserId&&drawer.classList.contains('is-open')){
      const composer=detail.querySelector('[data-panel-dm-form] textarea[name="message"]');
      const isComposing=!!composer&&(document.activeElement===composer||composer.value.length>0);
      // Während jemand schreibt, wird die Detailansicht absichtlich nicht neu
      // gerendert. So kann der 20-s-Poll niemals einen Tastenanschlag, Cursor
      // oder Entwurf verschlucken. Die Nutzer-/Online-Liste aktualisiert sich weiter.
      if(!isComposing)loadDetail(selectedUserId,false,true);
    }
  },20000);

  window.addEventListener('beforeunload',()=>refreshTimer&&clearInterval(refreshTimer));
});


// v1.6.4: Since-last-login activity digest
document.addEventListener('DOMContentLoaded',()=>{
  if(document.body.dataset.activityDigestPending!=='1')return;

  const overlay=document.querySelector('[data-activity-digest]');
  const body=overlay?.querySelector('[data-activity-digest-body]');
  const since=overlay?.querySelector('[data-activity-digest-since]');
  const closeButtons=[...(overlay?.querySelectorAll('[data-activity-digest-close]')||[])];
  if(!overlay||!body||!since)return;

  const esc=value=>String(value??'').replace(/[&<>"']/g,ch=>({
    '&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'
  }[ch]));

  const roleClass=role=>{
    const value=String(role||'').toLowerCase();
    return ['founder','admin','member','vip','spieler'].includes(value)?`role-${value}`:'';
  };

  const fmt=value=>{
    if(!value)return '—';
    const d=new Date(value);
    if(Number.isNaN(d.getTime()))return esc(value);
    return new Intl.DateTimeFormat('de-DE',{
      day:'2-digit',month:'2-digit',year:'2-digit',
      hour:'2-digit',minute:'2-digit'
    }).format(d);
  };

  const close=()=>{
    overlay.hidden=true;
    document.body.classList.remove('activity-digest-open');
  };

  closeButtons.forEach(button=>button.addEventListener('click',close));
  overlay.addEventListener('click',event=>{
    if(event.target===overlay)close();
  });
  document.addEventListener('keydown',event=>{
    if(event.key==='Escape'&&!overlay.hidden)close();
  });

  const countCard=(label,count,sub='')=>
    `<div class="activity-digest-stat"><strong>${Number(count||0)}</strong><span>${esc(label)}</span>${sub?`<small>${esc(sub)}</small>`:''}</div>`;

  const renderForum=forum=>{
    if(!forum?.post_count)return '';
    const items=(forum.threads||[]).map(thread=>
      `<a class="activity-digest-link-row" href="${esc(thread.url)}">
        <span><strong>${esc(thread.title)}</strong><small>${esc(thread.category)} · ${Number(thread.new_posts)} neu</small></span>
        <time>${fmt(thread.latest_at)}</time>
      </a>`
    ).join('');
    return `<section class="activity-digest-section">
      <div class="activity-digest-section-head"><h3>Forum</h3><a href="/forum">Forum öffnen</a></div>
      <p>${Number(forum.post_count)} neue Beiträge in ${Number(forum.thread_count)} neuen Themen bzw. aktualisierten Diskussionen.</p>
      <div class="activity-digest-list">${items}</div>
    </section>`;
  };

  const renderAdmin=admin=>{
    if(!admin?.count)return '';
    const groups=(admin.breakdown||[]).map(item=>
      `<a class="activity-digest-link-row" href="${esc(item.url)}">
        <span><strong class="${roleClass(item.actor_role)}">${esc(item.actor_name)}</strong><small>${esc(item.server_name)}</small></span>
        <b>${Number(item.count)} Aktionen</b>
      </a>`
    ).join('');

    const latest=(admin.latest||[]).map(item=>
      `<a class="activity-digest-admin-event" href="${esc(item.url)}">
        <span><strong class="${roleClass(item.actor_role)}">${esc(item.actor_name)}</strong> · ${esc(item.action_label)}${item.target_name?` → ${esc(item.target_name)}`:''}</span>
        <small>${esc(item.server_name)} · ${fmt(item.created_at)}</small>
      </a>`
    ).join('');

    return `<section class="activity-digest-section">
      <div class="activity-digest-section-head"><h3>Adminaktionen auf Servern</h3><span>${Number(admin.count)} insgesamt</span></div>
      <div class="activity-digest-list">${groups}</div>
      ${latest?`<details class="activity-digest-details"><summary>Letzte Aktionen anzeigen</summary><div>${latest}</div></details>`:''}
    </section>`;
  };

  const renderMessages=messages=>{
    if(!messages?.count)return '';
    const items=(messages.senders||[]).map(sender=>
      `<a href="#" class="activity-digest-link-row" data-digest-open-user="${Number(sender.user_id)}">
        <span><strong class="${roleClass(sender.role)}">${esc(sender.display_name)}</strong><small>${Number(sender.count)} Nachrichten · ${Number(sender.unread)} ungelesen</small></span>
        <time>${fmt(sender.latest_at)}</time>
      </a>`
    ).join('');

    return `<section class="activity-digest-section">
      <div class="activity-digest-section-head"><h3>Persönliche Nachrichten</h3><span>${Number(messages.count)} neu</span></div>
      <div class="activity-digest-list">${items}</div>
    </section>`;
  };

  const renderAnnouncements=announcements=>{
    if(!announcements?.count)return '';
    const items=(announcements.items||[]).map(item=>
      `<a class="activity-digest-link-row" href="${esc(item.url)}">
        <span><strong>${esc(item.title)}</strong><small>${esc(item.creator)} · ${esc(item.level)}</small></span>
        <time>${fmt(item.created_at)}</time>
      </a>`
    ).join('');

    return `<section class="activity-digest-section">
      <div class="activity-digest-section-head"><h3>Mitteilungen</h3><span>${Number(announcements.count)} neu</span></div>
      <div class="activity-digest-list">${items}</div>
    </section>`;
  };

  const renderChat=chat=>{
    if(!chat?.count)return '';
    const items=(chat.items||[]).map(item=>
      `<a class="activity-digest-chat-row" href="${esc(item.url)}">
        <strong class="${roleClass(item.role)}">${esc(item.author)}</strong>
        <span>${esc(item.message)}</span>
        <time>${fmt(item.created_at)}</time>
      </a>`
    ).join('');

    return `<section class="activity-digest-section">
      <div class="activity-digest-section-head"><h3>Webpanel-Chat</h3><a href="/dashboard#dashboard-chat">${Number(chat.count)} Nachrichten</a></div>
      <div class="activity-digest-list">${items}</div>
    </section>`;
  };

  const render=data=>{
    since.textContent=data.since_label||'Seit dem letzten Login';

    const stats=[
      countCard('Forum',data.forum?.post_count||0,`${data.forum?.thread_count||0} neue Themen`),
      countCard('Adminaktionen',data.admin_actions?.count||0),
      countCard('Nachrichten',data.messages?.count||0,`${data.messages?.unread_count||0} ungelesen`),
      countCard('Mitteilungen',data.announcements?.count||0),
      countCard('Chat',data.chat?.count||0)
    ].join('');

    const sections=[
      renderMessages(data.messages),
      renderForum(data.forum),
      renderAdmin(data.admin_actions),
      renderAnnouncements(data.announcements),
      renderChat(data.chat)
    ].filter(Boolean).join('');

    body.innerHTML=`<div class="activity-digest-stats">${stats}</div>
      ${sections||'<div class="activity-digest-empty"><strong>Keine neuen Aktivitäten.</strong><span>Seit dem letzten Login ist nichts Neues hinzugekommen.</span></div>'}`;

    body.querySelectorAll('[data-digest-open-user]').forEach(link=>{
      link.addEventListener('click',event=>{
        event.preventDefault();
        const userId=Number(link.dataset.digestOpenUser||0);
        if(!userId)return;
        close();
        window.dispatchEvent(new CustomEvent('panel-users:open-user',{detail:{userId}}));
      });
    });

    overlay.hidden=false;
    document.body.classList.add('activity-digest-open');
  };

  fetch('/activity-digest',{
    headers:{'Accept':'application/json'},
    credentials:'same-origin',
    cache:'no-store'
  })
    .then(async response=>{
      const data=await response.json();
      if(!response.ok||!data.ok)throw new Error(data.error||'Aktivitätsübersicht konnte nicht geladen werden.');
      return data;
    })
    .then(data=>{
      if(data.pending===false)return;
      render(data);
    })
    .catch(error=>{
      console.warn('Aktivitätsübersicht:',error);
    });
});


// v1.6.6: Plugin compact browser
document.addEventListener('DOMContentLoaded',()=>{
  const search=document.querySelector('[data-plugin-search]');
  const servers=[...document.querySelectorAll('[data-plugin-server]')];
  const cards=[...document.querySelectorAll('[data-plugin-card]')];
  const catalogCards=[...document.querySelectorAll('[data-plugin-catalog-card]')];
  const openUpdates=document.querySelector('[data-plugin-open-updates]');
  const collapseAll=document.querySelector('[data-plugin-collapse-all]');

  const normalize=value=>String(value||'').toLocaleLowerCase().trim();

  const applySearch=()=>{
    if(!search)return;
    const q=normalize(search.value);
    catalogCards.forEach(card=>{card.hidden=!!q&&!normalize(card.dataset.search).includes(q);});

    servers.forEach(server=>{
      const serverName=normalize(server.dataset.serverName);
      const pluginCards=[...server.querySelectorAll('[data-plugin-card]')];
      let visible=0;

      pluginCards.forEach(card=>{
        const match=!q
          || normalize(card.dataset.search).includes(q)
          || serverName.includes(q);
        card.hidden=!match;
        if(match)visible++;
      });

      server.hidden=visible===0&&!!q;
      if(q&&visible>0)server.open=true;
    });
  };

  search?.addEventListener('input',applySearch);
  search?.addEventListener('search',applySearch);
  document.querySelectorAll('.plugin-quick-repo').forEach(link=>link.addEventListener('click',event=>event.stopPropagation()));

  openUpdates?.addEventListener('click',()=>{
    let found=0;
    servers.forEach(server=>{
      let hasUpdate=false;
      server.querySelectorAll('[data-plugin-card]').forEach(card=>{
        const isUpdate=card.dataset.updateAvailable==='1';
        card.open=isUpdate;
        if(isUpdate){
          card.hidden=false;
          hasUpdate=true;
          found++;
        }
      });
      server.open=hasUpdate;
      server.hidden=false;
    });
    if(search)search.value='';
    if(!found)openUpdates.textContent='Keine Updates vorhanden';
    window.setTimeout(()=>{if(openUpdates)openUpdates.textContent='Updates öffnen';},1400);
  });

  collapseAll?.addEventListener('click',()=>{
    cards.forEach(card=>card.open=false);
    servers.forEach(server=>server.open=false);
    document.querySelectorAll('.plugin-runs-accordion,.plugin-source-config').forEach(details=>details.open=false);
  });
});


// v1.8.1: lokale 2FA-QR-Einrichtung, Copy-Helfer und Recovery-Codes
// Der QR-Code wird ausschließlich im Browser aus der otpauth:// URI erzeugt.
document.addEventListener('DOMContentLoaded',()=>{
  const pulseLabel=(button,label='Kopiert')=>{
    if(!button)return;
    const old=button.textContent;
    button.textContent=label;
    window.setTimeout(()=>button.textContent=old,1200);
  };
  const copyText=async(value,button,fallbackNode=null)=>{
    value=String(value||'').trim();if(!value)return;
    try{await navigator.clipboard.writeText(value);pulseLabel(button);return;}
    catch{}
    if(fallbackNode){const range=document.createRange();range.selectNodeContents(fallbackNode);const selection=window.getSelection();selection?.removeAllRanges();selection?.addRange(range);}
  };

  const secretButton=document.querySelector('[data-copy-totp-secret]');
  const secret=document.querySelector('[data-totp-secret]');
  secretButton?.addEventListener('click',()=>copyText((secret?.textContent||'').replace(/\s+/g,''),secretButton,secret));

  document.querySelectorAll('[data-copy-value]').forEach(button=>button.addEventListener('click',()=>copyText(button.dataset.copyValue||'',button)));

  const recoveryRoot=document.querySelector('[data-recovery-codes]');
  const recoveryButton=document.querySelector('[data-copy-recovery-codes]');
  recoveryButton?.addEventListener('click',()=>{
    const codes=Array.from(recoveryRoot?.querySelectorAll('code')||[]).map(node=>(node.textContent||'').trim()).filter(Boolean);
    copyText(codes.join('\n'),recoveryButton,recoveryRoot);
  });

  document.querySelectorAll('[data-totp-qr]').forEach(host=>{
    const uri=host.dataset.totpUri||'';
    try{
      if(!uri||!window.SerververwaltungQRCode?.createSvg)throw new Error('QR-Modul nicht verfügbar');
      host.innerHTML=window.SerververwaltungQRCode.createSvg(uri,{quietZone:4,moduleSize:6});
      host.classList.add('is-ready');
    }catch(error){
      host.textContent='QR-Code konnte lokal nicht erzeugt werden. Bitte direkten App-Link oder manuellen Schlüssel verwenden.';
      host.classList.add('is-error');
      console.warn('Lokaler 2FA-QR-Code konnte nicht erzeugt werden.',error);
    }
  });
});

// v1.7.0: globale Darstellungssteuerung und adaptive Tabellen
// "Kacheln" ist die Standardansicht. Die Wahl gilt panelweit im aktuellen Browser.
document.addEventListener('DOMContentLoaded',()=>{
  const storageKey='serververwaltung.ui.viewMode';
  const body=document.body;
  const switchRoot=document.querySelector('[data-ui-view-switch]');
  const buttons=Array.from(document.querySelectorAll('[data-ui-view-mode]'));

  const normalize=value=>value==='compact'?'compact':'cards';
  const saved=(()=>{try{return normalize(window.localStorage.getItem(storageKey));}catch{return 'cards';}})();

  const applyMode=(mode,persist=true)=>{
    mode=normalize(mode);
    body.dataset.uiMode=mode;
    body.classList.toggle('ui-mode-cards',mode==='cards');
    body.classList.toggle('ui-mode-compact',mode==='compact');
    buttons.forEach(button=>{
      const active=button.dataset.uiViewMode===mode;
      button.classList.toggle('is-active',active);
      button.setAttribute('aria-pressed',active?'true':'false');
    });
    if(persist){try{window.localStorage.setItem(storageKey,mode);}catch{}}
    window.dispatchEvent(new CustomEvent('serververwaltung:viewmode',{detail:{mode}}));
  };

  applyMode(saved,false);
  switchRoot?.addEventListener('click',event=>{
    const button=event.target.closest('[data-ui-view-mode]');
    if(!button)return;
    applyMode(button.dataset.uiViewMode||'cards');
  });

  const labelAdaptiveTable=table=>{
    if(!(table instanceof HTMLTableElement))return;
    const headers=Array.from(table.querySelectorAll('thead th')).map((th,index)=>{
      const text=(th.textContent||'').replace(/\s+/g,' ').trim();
      if(text)return text;
      return index===0?'Auswahl':'Aktion';
    });

    table.querySelectorAll('tbody tr').forEach(row=>{
      const cells=Array.from(row.children).filter(cell=>cell instanceof HTMLTableCellElement);
      cells.forEach((cell,index)=>{
        if(Number(cell.getAttribute('colspan')||'1')>1){
          cell.dataset.uiWide='1';
          cell.dataset.uiLabel='';
          return;
        }
        cell.dataset.uiLabel=headers[index]||'Details';
      });
    });
  };

  const labelAll=()=>document.querySelectorAll('table[data-ui-adaptive]').forEach(labelAdaptiveTable);
  labelAll();
  body.classList.add('ui-adaptive-ready');

  // Live-Ansichten ersetzen Tabellenzeilen zyklisch. Nur geänderte adaptive Tabellen
  // werden deshalb nachbeschriftet, statt die komplette Seite neu zu verarbeiten.
  const observer=new MutationObserver(mutations=>{
    const tables=new Set();
    mutations.forEach(mutation=>{
      const parent=mutation.target instanceof Element?mutation.target:null;
      const table=parent?.closest?.('table[data-ui-adaptive]');
      if(table)tables.add(table);
      mutation.addedNodes.forEach(node=>{
        if(!(node instanceof Element))return;
        if(node.matches?.('table[data-ui-adaptive]'))tables.add(node);
        node.querySelectorAll?.('table[data-ui-adaptive]').forEach(tables.add,tables);
      });
    });
    tables.forEach(labelAdaptiveTable);
  });
  observer.observe(document.body,{childList:true,subtree:true});
});

// v1.7.0: aktive Hauptnavigation sichtbar markieren
document.addEventListener('DOMContentLoaded',()=>{
  const path=window.location.pathname.replace(/\/+$/,'')||'/';
  const links=Array.from(document.querySelectorAll('.sidebar nav a'));
  let best=null;
  let bestLength=-1;
  links.forEach(link=>{
    const href=(link.getAttribute('href')||'').replace(/\/+$/,'')||'/';
    const matches=href==='/'?path==='/':(path===href||path.startsWith(href+'/'));
    if(matches&&href.length>bestLength){best=link;bestLength=href.length;}
  });
  if(best){best.classList.add('is-active');best.setAttribute('aria-current','page');}
});

// v1.7.0: Audit-Log lokal filtern, ohne zusätzliche Serveranfrage
document.addEventListener('DOMContentLoaded',()=>{
  const input=document.querySelector('[data-audit-filter]');
  const table=document.querySelector('[data-audit-table]');
  if(!input||!table)return;
  const rows=Array.from(table.querySelectorAll('tbody tr')).filter(row=>!row.querySelector('td[colspan]'));
  const count=document.querySelector('[data-audit-visible-count]');
  const empty=document.querySelector('[data-audit-filter-empty]');
  const apply=()=>{
    const q=(input.value||'').trim().toLocaleLowerCase('de');
    let visible=0;
    rows.forEach(row=>{
      const show=q===''||(row.textContent||'').toLocaleLowerCase('de').includes(q);
      row.hidden=!show;
      if(show)visible++;
    });
    if(count)count.textContent=`${visible} sichtbar`;
    if(empty)empty.hidden=visible!==0;
  };
  input.addEventListener('input',apply);
  apply();
});

// v1.7.2: Schnellpresets für Waffen-Teamlimits.
document.addEventListener('DOMContentLoaded',()=>{
  const form=document.querySelector('form[action$="/live/weapon-mode"]');
  if(!form)return;
  const limitInputs=()=>Array.from(form.querySelectorAll('[data-weapon-limit]'));
  const set=(weapon,value)=>{
    const input=form.querySelector(`[data-weapon-limit="${CSS.escape(weapon)}"]`);
    if(input)input.value=String(value);
  };
  form.addEventListener('click',event=>{
    const button=event.target.closest('[data-weapon-limit-preset]');
    if(!button)return;
    event.preventDefault();
    const preset=button.dataset.weaponLimitPreset||'';
    if(preset==='clear') limitInputs().forEach(input=>{input.value='0';});
    if(preset==='awp-one') set('weapon_awp',1);
    if(preset==='snipers-one'){
      ['weapon_awp','weapon_ssg08','weapon_scar20','weapon_g3sg1'].forEach(weapon=>set(weapon,1));
    }
  });
});

// v1.7.3: Globale Spieleraktions-Kachel. Funktioniert per Event-Delegation auch
// für Spieler, die durch Live-Polling dynamisch in den DOM eingefügt werden.
document.addEventListener('DOMContentLoaded',()=>{
  const modal=document.querySelector('[data-player-action-modal]');
  if(!modal) return;

  const body=modal.querySelector('[data-player-action-body]');
  const loading=modal.querySelector('[data-player-action-loading]');
  const nameNode=modal.querySelector('[data-player-action-name]');
  const steamNode=modal.querySelector('[data-player-action-steam]');
  const serverSelect=modal.querySelector('[data-player-action-server]');
  const serverNote=modal.querySelector('[data-player-action-server-note]');
  const liveArea=modal.querySelector('[data-player-action-live-area]');
  const offline=modal.querySelector('[data-player-action-offline]');
  const banLink=modal.querySelector('[data-player-action-ban-link]');
  const banFree=modal.querySelector('[data-player-action-ban-free]');
  const unbanForm=modal.querySelector('[data-player-action-unban]');
  const banStatus=modal.querySelector('[data-player-action-ban-status]');
  const whitelistForm=modal.querySelector('[data-player-whitelist-action]');
  const whitelistServerSelect=modal.querySelector('[data-player-whitelist-server]');
  const moderationLink=modal.querySelector('[data-player-moderation-link]');
  const restrictionBox=modal.querySelector('[data-player-action-restrictions]');
  let current=null;

  const currentReturn=()=>`${location.pathname}${location.search}${location.hash}`;
  const esc=value=>encodeURIComponent(String(value??''));

  const close=()=>{
    modal.hidden=true;
    document.body.classList.remove('modal-open');
    current=null;
  };

  const selectedServer=()=>{
    if(!current||!serverSelect) return null;
    const id=Number(serverSelect.value||0);
    return (current.servers||[]).find(s=>Number(s.id)===id)||null;
  };

  const syncForms=()=>{
    const server=selectedServer();
    const steam=String(current?.steam_id64||'');
    modal.querySelectorAll('[data-player-live-action]').forEach(form=>{
      const steamInput=form.querySelector('input[name="steam_id64"]');
      const serverInput=form.querySelector('input[name="server_id"]');
      const returnInput=form.querySelector('input[name="_return"]');
      if(steamInput) steamInput.value=steam;
      if(serverInput) serverInput.value=server?String(server.id):'';
      if(returnInput) returnInput.value=currentReturn();
      form.querySelectorAll('button,input,select,textarea').forEach(control=>{
        if(control.name==='_csrf'||control.name==='steam_id64'||control.name==='action'||control.name==='_return') return;
        control.disabled=!server;
      });
    });

    if(serverNote){
      if(!server){
        serverNote.textContent='Kein aktuell erreichbarer Server mit diesem Spieler.';
      }else{
        const states=[];
        if(server.team) states.push(server.team);
        if(server.voice_muted) states.push('Mute aktiv');
        if(server.gagged) states.push('Gag aktiv');
        serverNote.textContent=`Online auf ${server.name}${states.length?' · '+states.join(' · '):''}`;
      }
    }

    const muteForm=modal.querySelector('[data-player-live-action="mute"]');
    const unmute=modal.querySelector('[data-player-live-action="unmute"]');
    const gagForm=modal.querySelector('[data-player-live-action="gag"]');
    const ungag=modal.querySelector('[data-player-live-action="ungag"]');
    if(unmute) unmute.hidden=!(server?.voice_muted);
    if(muteForm) muteForm.classList.toggle('is-currently-active',Boolean(server?.voice_muted));
    if(ungag) ungag.hidden=!(server?.gagged);
    if(gagForm) gagForm.classList.toggle('is-currently-active',Boolean(server?.gagged));
  };

  const render=()=>{
    const servers=Array.isArray(current?.servers)?current.servers:[];
    if(serverSelect){
      serverSelect.replaceChildren();
      if(servers.length===0){
        const option=document.createElement('option');option.value='';option.textContent='Nicht online';serverSelect.appendChild(option);serverSelect.disabled=true;
      }else{
        servers.forEach(server=>{
          const option=document.createElement('option');
          option.value=String(server.id);
          option.textContent=`${server.name} · ${server.team||'Unassigned'}`;
          if(server.preferred) option.selected=true;
          serverSelect.appendChild(option);
        });
        serverSelect.disabled=false;
        if(!serverSelect.value) serverSelect.value=String(servers[0].id);
      }
    }
    if(liveArea) liveArea.hidden=servers.length===0;
    if(offline) offline.hidden=servers.length!==0;

    if(banLink){
      const params=new URLSearchParams();
      params.set('steamid',String(current?.steam_id64||''));
      params.set('name',String(current?.player_name||''));
      params.set('_return',currentReturn());
      if(Number(current?.chat_id||0)>0) params.set('chat_id',String(current.chat_id));
      banLink.href=`/bans/new?${params.toString()}`;
    }

    const whitelistServers=Array.isArray(current?.whitelist_servers)?current.whitelist_servers:[];
    if(whitelistForm&&whitelistServerSelect){
      whitelistServerSelect.replaceChildren();
      if(whitelistServers.length===0){
        const option=document.createElement('option');option.value='';option.textContent='Kein verwaltbarer Server';whitelistServerSelect.appendChild(option);whitelistServerSelect.disabled=true;whitelistForm.querySelector('button')?.setAttribute('disabled','disabled');
      }else{
        whitelistServers.forEach(server=>{const option=document.createElement('option');option.value=String(server.id);option.textContent=server.name;if(server.preferred)option.selected=true;whitelistServerSelect.appendChild(option);});
        whitelistServerSelect.disabled=false;if(!whitelistServerSelect.value)whitelistServerSelect.value=String(whitelistServers[0].id);whitelistForm.querySelector('button')?.removeAttribute('disabled');
      }
      const steamInput=whitelistForm.querySelector('input[name="steam_id64"]');if(steamInput)steamInput.value=String(current?.steam_id64||'');
      const returnInput=whitelistForm.querySelector('input[name="_return"]');if(returnInput)returnInput.value=currentReturn();
    }

    modal.querySelectorAll('[data-player-moderation-action]').forEach(form=>{
      const steamInput=form.querySelector('input[name="steam_id64"]');if(steamInput)steamInput.value=String(current?.steam_id64||'');
      const nameInput=form.querySelector('input[name="player_name"]');if(nameInput)nameInput.value=String(current?.player_name||current?.steam_id64||'');
      const chatInput=form.querySelector('input[name="chat_message_id"]');if(chatInput)chatInput.value=Number(current?.chat_id||0)>0?String(current.chat_id):'';
      const returnInput=form.querySelector('input[name="_return"]');if(returnInput)returnInput.value=currentReturn();
      if(form.matches('[data-chat-evidence-form]')) form.hidden=!(Number(current?.chat_id||0)>0);
    });
    if(moderationLink){const q=new URLSearchParams({q:String(current?.steam_id64||'')});moderationLink.href=`/moderation?${q.toString()}`;}

    if(restrictionBox){
      const ms=current?.moderation_status||null;
      restrictionBox.replaceChildren();
      if(ms?.restricted){
        const add=(text,cls,title='')=>{const e=document.createElement('span');e.className=`player-restriction-badge ${cls}`;e.textContent=text;if(title)e.title=title;restrictionBox.appendChild(e);};
        if(ms.banned)add('GLOBAL BAN','player-restriction-ban',ms.ban_reason||'Global gebannt');
        if(Number(ms.warnings||0)>0)add(`${ms.warnings} WARN`,'player-restriction-warning',`${ms.warnings} aktive Verwarnung(en)`);
        if(ms.silence)add('SILENCE','player-restriction-silence','Voice + Chat global gesperrt');
        else{
          if(ms.mute)add('MUTE','player-restriction-mute','Globaler Voice-Mute aktiv');
          if(ms.gag)add('GAG','player-restriction-gag','Globaler Chat-Gag aktiv');
        }
        const text=document.createElement('span');text.className='restriction-summary-text';text.textContent='Aktive Moderationsmaßnahmen – gelten unabhängig vom aktuell ausgewählten Server.';restrictionBox.appendChild(text);
        restrictionBox.hidden=false;
      }else restrictionBox.hidden=true;
    }

    const ban=current?.active_ban||null;
    if(banFree) banFree.hidden=Boolean(ban);
    if(unbanForm){
      unbanForm.hidden=!ban;
      if(ban){
        unbanForm.action=`/bans/${esc(ban.id)}/unban`;
        const ret=unbanForm.querySelector('[data-player-action-return]');if(ret)ret.value=currentReturn();
        if(banStatus){
          const until=Number(ban.permanent)===1||ban.expires_at===null?'Permanent':`bis ${ban.expires_at}`;
          banStatus.textContent=`Global gebannt #${ban.id} · ${ban.reason_label||ban.reason_code||'Ban'} · ${until}`;
        }
      }
    }
    syncForms();
  };

  const open=async(button)=>{
    const steam=String(button.dataset.playerSteamid||'').trim();
    if(!/^7656119\d{10}$/.test(steam)) return;
    const playerName=String(button.dataset.playerName||steam).trim();
    const serverId=Number(button.dataset.playerServerId||0);
    const chatId=Number(button.dataset.playerChatId||0);

    modal.hidden=false;
    document.body.classList.add('modal-open');
    if(nameNode) nameNode.textContent=playerName||steam;
    if(steamNode) steamNode.textContent=steam;
    if(loading){loading.hidden=false;loading.textContent='Live-Status wird geprüft…';}
    if(body) body.hidden=true;

    try{
      const params=new URLSearchParams({steamid:steam});
      if(serverId>0) params.set('server_id',String(serverId));
      const response=await fetch(`/player-actions/context?${params.toString()}`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});
      const data=await response.json().catch(()=>({}));
      if(!response.ok||data.ok===false) throw new Error(data.error||`HTTP ${response.status}`);
      current={...data,player_name:playerName||data.player_name||steam,chat_id:chatId};
      render();
      if(loading) loading.hidden=true;
      if(body) body.hidden=false;
    }catch(error){
      if(loading){loading.hidden=false;loading.textContent=error?.message||'Spielerstatus konnte nicht geladen werden.';}
      if(body) body.hidden=true;
    }
  };

  document.addEventListener('click',event=>{
    const button=event.target.closest('[data-player-actions]');
    if(button){event.preventDefault();open(button);return;}
    if(event.target.closest('[data-player-action-close]')){event.preventDefault();close();}
  });
  serverSelect?.addEventListener('change',syncForms);
  modal.addEventListener('click',event=>{if(event.target===modal)close();});
  document.addEventListener('keydown',event=>{if(event.key==='Escape'&&!modal.hidden)close();});
});

// v1.7.5: Live-Regelkacheln – Linksklick/Rechtsklick öffnet eine sichere Schnellwahl.
document.addEventListener('DOMContentLoaded',()=>{
  const cards=Array.from(document.querySelectorAll('[data-rule-card]'));
  if(cards.length===0)return;
  const closeAll=(except=null)=>cards.forEach(card=>{if(card===except)return;const menu=card.querySelector('[data-rule-menu]');if(menu)menu.hidden=true;card.classList.remove('menu-open');});
  const toggle=card=>{
    const menu=card.querySelector('[data-rule-menu]');
    if(!menu){const anchor=card.dataset.ruleAnchor;if(anchor)document.querySelector(anchor)?.scrollIntoView({behavior:'smooth',block:'start'});return;}
    const open=menu.hidden;
    closeAll(card);
    menu.hidden=!open;
    card.classList.toggle('menu-open',open);
  };
  cards.forEach(card=>{
    card.addEventListener('click',event=>{if(event.target.closest('form,a,button,input,select,textarea,label'))return;toggle(card);});
    card.addEventListener('contextmenu',event=>{if(event.target.closest('form,a,button,input,select,textarea,label'))return;event.preventDefault();toggle(card);});
    card.addEventListener('keydown',event=>{if(event.key==='Enter'||event.key===' '){event.preventDefault();toggle(card);}});
  });
  document.addEventListener('click',event=>{if(!event.target.closest('[data-rule-card]'))closeAll();});
  document.addEventListener('keydown',event=>{if(event.key==='Escape')closeAll();});
});

// v1.7.5: Aktive Werte in den eigentlichen Einstellungsformularen farblich hervorheben.
document.addEventListener('DOMContentLoaded',()=>{
  const scope=document.querySelector('#bots-rules, #weapon-modes');
  if(!scope)return;
  const defaults={gravity:'800',global_speed:'1',infinite_ammo:'0',buy_anywhere:'0',airaccelerate:'12',fall_damage_scale:'1',damage_scale:'1',freeze_time:'15',round_time:'1.92',buy_time:'20',start_money:'800',max_money:'16000',max_rounds:'24',limitteams:'2',mode:'normal'};
  const normalize=v=>String(v??'').replace(',','.').replace(/\.0+$/,'');
  const paint=()=>{
    scope.querySelectorAll('input[type="checkbox"]').forEach(input=>input.closest('label')?.classList.toggle('setting-active',input.checked));
    scope.querySelectorAll('select[name],input[type="number"][name]').forEach(control=>{
      const name=control.name.replace(/^team_limits\[(.+)\]$/,'team_limit');
      let active=false,custom=false;
      if(name==='team_limit')active=Number(control.value||0)>0;
      else if(Object.hasOwn(defaults,name))custom=normalize(control.value)!==normalize(defaults[name]);
      control.classList.toggle('setting-active',active);
      control.classList.toggle('setting-custom',custom);
    });
    scope.querySelectorAll('.action-card').forEach(card=>{
      const has=card.querySelector('.setting-active,.setting-custom,.checkbox-line.setting-active');
      card.classList.toggle('setting-state-active',Boolean(has));
    });
  };
  scope.addEventListener('change',paint);
  scope.addEventListener('input',paint);
  paint();
});

// v1.7.5: Globale Moderationszustände überall dort markieren, wo eine SteamID sichtbar/aktionsfähig ist.
document.addEventListener('DOMContentLoaded',()=>{
  const cache=new Map();
  let requestInFlight=false;
  let timer=null;
  const valid=steam=>/^7656119\d{10}$/.test(steam);
  const hostFor=node=>node.closest('[data-player-status-host],.player-quick-actions,.server-chat-meta,.dashboard-player-row,.player-profile-actions,.assignment-player-actions,.whitelist-player-actions,.player-live-main,.profile-main,td')||node.parentElement;
  const badge=(text,cls,title='')=>{const el=document.createElement('span');el.className=`player-restriction-badge ${cls}`;el.textContent=text;if(title)el.title=title;return el;};
  const renderOne=(node,status)=>{
    const host=hostFor(node);if(!host)return;
    const steam=String(node.dataset.playerSteamid||'');
    let box=host.querySelector(`:scope > [data-player-restrictions-for="${CSS.escape(steam)}"]`);
    if(!status?.restricted){box?.remove();return;}
    if(!box){box=document.createElement('span');box.className='player-restriction-badges';box.dataset.playerRestrictionsFor=steam;host.appendChild(box);}else box.replaceChildren();
    if(status.banned)box.appendChild(badge('BAN','player-restriction-ban',status.ban_reason||'Global gebannt'));
    if(Number(status.warnings||0)>0)box.appendChild(badge(`${status.warnings} WARN`,'player-restriction-warning',`${status.warnings} aktive Verwarnung(en)`));
    if(status.silence)box.appendChild(badge('SILENCE','player-restriction-silence','Global: Voice + Chat gesperrt'));
    else{
      if(status.mute)box.appendChild(badge('MUTE','player-restriction-mute','Globaler Voice-Mute aktiv'));
      if(status.gag)box.appendChild(badge('GAG','player-restriction-gag','Globaler Chat-Gag aktiv'));
    }
  };
  const scan=async()=>{
    if(requestInFlight)return;
    const nodes=Array.from(document.querySelectorAll('[data-player-steamid]')).filter(n=>valid(String(n.dataset.playerSteamid||'')));
    const ids=[...new Set(nodes.map(n=>String(n.dataset.playerSteamid)))];
    if(ids.length===0)return;
    ids.forEach(id=>{const st=cache.get(id);if(st)nodes.filter(n=>n.dataset.playerSteamid===id).forEach(n=>renderOne(n,st));});
    requestInFlight=true;
    try{
      for(let i=0;i<ids.length;i+=100){
        const batch=ids.slice(i,i+100);
        const response=await fetch(`/moderation/statuses?steamids=${encodeURIComponent(batch.join(','))}`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});
        if(!response.ok)continue;
        const data=await response.json();
        const players=data?.players||{};
        Object.entries(players).forEach(([id,status])=>cache.set(id,status));
      }
      nodes.forEach(node=>renderOne(node,cache.get(String(node.dataset.playerSteamid))));
    }catch(_){/* Statusmarker dürfen die Seite nie blockieren. */}
    finally{requestInFlight=false;}
  };
  const schedule=()=>{clearTimeout(timer);timer=setTimeout(scan,180);};
  schedule();
  setInterval(scan,10000);
  const observer=new MutationObserver(schedule);
  observer.observe(document.body,{childList:true,subtree:true});
});

/* v1.7.9 – Professional Permission Center */
(() => {
  const page = document.querySelector('[data-permissions-page]');
  if (!page) return;

  const norm = value => String(value || '').toLocaleLowerCase('de');
  const userSearch = page.querySelector('[data-user-permission-search]');
  const roleFilter = page.querySelector('[data-user-role-filter]');
  const onlyCustom = page.querySelector('[data-user-only-custom]');
  const userCards = [...page.querySelectorAll('[data-user-permission-card]')];
  const filterUsers = () => {
    const q = norm(userSearch?.value).trim();
    const role = String(roleFilter?.value || '');
    const custom = !!onlyCustom?.checked;
    userCards.forEach(card => {
      const show = (!q || norm(card.dataset.search).includes(q)) && (!role || card.dataset.role === role) && (!custom || card.dataset.custom === '1');
      card.hidden = !show;
    });
  };
  userSearch?.addEventListener('input', filterUsers);
  roleFilter?.addEventListener('change', filterUsers);
  onlyCustom?.addEventListener('change', filterUsers);

  page.querySelectorAll('.permission-editor, .permission-role-card, .permission-package-card').forEach(scope => {
    const search = scope.querySelector('[data-permission-search]');
    const dangerous = scope.querySelector('[data-permission-only-dangerous]');
    const overrides = scope.querySelector('[data-permission-only-overrides]');
    const items = [...scope.querySelectorAll('[data-permission-item]')];
    const apply = () => {
      const q = norm(search?.value).trim();
      items.forEach(item => {
        const showQ = !q || norm(item.dataset.permissionText).includes(q);
        const showDanger = !dangerous?.checked || item.dataset.dangerous === '1';
        const showOverride = !overrides?.checked || item.dataset.override === '1';
        item.hidden = !(showQ && showDanger && showOverride);
      });
    };
    search?.addEventListener('input', apply);
    dangerous?.addEventListener('change', apply);
    overrides?.addEventListener('change', apply);
  });

  let templates = {};
  try { templates = JSON.parse(page.dataset.roleTemplates || '{}'); } catch (_) {}
  const compareA = page.querySelector('[data-role-compare-a]');
  const compareB = page.querySelector('[data-role-compare-b]');
  const compareResult = page.querySelector('[data-role-compare-result]');
  const renderCompare = () => {
    if (!compareResult || !compareA || !compareB) return;
    const a = templates[compareA.value], b = templates[compareB.value];
    if (!a || !b) return;
    const allPerms = new Set([...(a.permissions || []), ...(b.permissions || [])]);
    if ((a.permissions || []).includes('*') || (b.permissions || []).includes('*')) {
      Object.keys(window.__unused || {}).forEach(p => allPerms.add(p));
    }
    const onlyA = [...allPerms].filter(p => (a.permissions || []).includes(p) && !(b.permissions || []).includes(p));
    const onlyB = [...allPerms].filter(p => (b.permissions || []).includes(p) && !(a.permissions || []).includes(p));
    const caps = ['view','players','permissions','sync','live'];
    const capDiff = caps.filter(c => !!a.server_caps?.[c] !== !!b.server_caps?.[c]);
    compareResult.innerHTML = `<strong>${a.label} ↔ ${b.label}</strong><br>`+
      `<span>${onlyA.length} Rechte nur in ${a.label}, ${onlyB.length} nur in ${b.label}, ${capDiff.length} unterschiedliche Serverfreigaben.</span>`+
      `<br><small>Max. CS2-Rolle: ${a.max_game_role} ↔ ${b.max_game_role} · 2FA: ${a.require_2fa?'JA':'NEIN'} ↔ ${b.require_2fa?'JA':'NEIN'}</small>`;
  };
  compareA?.addEventListener('change', renderCompare);
  compareB?.addEventListener('change', renderCompare);
  renderCompare();

  page.querySelectorAll('[data-role-template-form]').forEach(form => {
    form.addEventListener('change', () => form.dataset.dirty = '1');
    form.addEventListener('submit', e => {
      const affected = Number(form.dataset.affectedUsers || 0);
      if (!window.confirm(`Rollenvorlage ändern?\n\nBetroffen: ${affected} Nutzer.\nIndividuelle Abweichungen bleiben erhalten.`)) e.preventDefault();
    });
  });
})();

(() => {
  const input = document.querySelector('[data-preview-search]');
  if (!input) return;
  const items = [...document.querySelectorAll('[data-preview-item]')];
  input.addEventListener('input', () => {
    const q = String(input.value || '').toLocaleLowerCase('de').trim();
    items.forEach(item => item.hidden = !!q && !String(item.dataset.search || '').includes(q));
  });
})();

/* v1.8.4 – persönliches Hell-/Dunkel-/System-Design */
document.addEventListener('DOMContentLoaded',()=>{
  const root=document.documentElement;
  const switcher=document.querySelector('[data-ui-theme-switch]');
  if(!switcher) return;

  const allowed=new Set(['light','dark','system']);
  const media=window.matchMedia?window.matchMedia('(prefers-color-scheme: light)'):null;
  const csrf=document.querySelector('meta[name="csrf-token"]')?.getAttribute('content')||'';
  let preference=String(root.dataset.themePreference||'system').toLowerCase();
  if(!allowed.has(preference)) preference='system';

  const resolveTheme=pref=>pref==='system'?(media?.matches?'light':'dark'):pref;
  const paint=pref=>{
    if(!allowed.has(pref)) pref='system';
    preference=pref;
    const resolved=resolveTheme(pref);
    root.dataset.themePreference=pref;
    root.dataset.themeResolved=resolved;
    root.style.colorScheme=resolved;
    switcher.querySelectorAll('[data-ui-theme-mode]').forEach(button=>{
      const active=button.dataset.uiThemeMode===pref;
      button.classList.toggle('is-active',active);
      button.setAttribute('aria-pressed',active?'true':'false');
    });
    try{localStorage.setItem('serververwaltung.uiTheme',pref);}catch(_){ }
  };

  const save=async pref=>{
    const previous=preference;
    paint(pref);
    if(!csrf) return;
    try{
      const body=new URLSearchParams();
      body.set('_csrf',csrf);
      body.set('theme',pref);
      const response=await fetch('/profile/theme',{
        method:'POST',
        headers:{'Content-Type':'application/x-www-form-urlencoded;charset=UTF-8','Accept':'application/json'},
        body:body.toString(),
        credentials:'same-origin'
      });
      const data=await response.json().catch(()=>({}));
      if(!response.ok||!data.ok) throw new Error(data.error||'Design konnte nicht gespeichert werden.');
    }catch(error){
      paint(previous);
      alert(error?.message||'Design konnte nicht gespeichert werden.');
    }
  };

  switcher.addEventListener('click',event=>{
    const button=event.target.closest('[data-ui-theme-mode]');
    if(!button) return;
    const next=String(button.dataset.uiThemeMode||'system').toLowerCase();
    if(!allowed.has(next)||next===preference) return;
    save(next);
  });

  if(media){
    const syncSystem=()=>{if(preference==='system') paint('system');};
    if(typeof media.addEventListener==='function') media.addEventListener('change',syncSystem);
    else if(typeof media.addListener==='function') media.addListener(syncSystem);
  }

  paint(preference);
});


// v1.8.9: Rollen-Dropdowns immer nach der aktuell ausgewählten Rolle einfärben.
document.addEventListener('DOMContentLoaded',()=>{
  const roleClasses=['role-select-founder','role-select-admin','role-select-member','role-select-vip','role-select-spieler'];
  const paint=select=>{
    roleClasses.forEach(c=>select.classList.remove(c));
    const value=String(select.value||'').toLowerCase();
    if(['founder','admin','member','vip','spieler'].includes(value)) select.classList.add(`role-select-${value}`);
  };
  document.querySelectorAll('select[data-community-role-select],select[data-server-role-select],select[data-role-color-select]').forEach(select=>{paint(select);select.addEventListener('change',()=>paint(select));});
});

// v1.8.9: Schnellverwaltungs-Button (plus optional Rechtsklick) -> hierarchische Verwaltung im selben Fenster.
document.addEventListener('DOMContentLoaded',()=>{
  const overlay=document.querySelector('[data-quick-manage-overlay]'); if(!overlay)return;
  const body=overlay.querySelector('[data-qm-body]'),title=overlay.querySelector('[data-qm-title]'),statusNode=overlay.querySelector('[data-qm-status]'),crumb=overlay.querySelector('[data-qm-crumb]'),feedback=overlay.querySelector('[data-qm-feedback]'),full=overlay.querySelector('[data-qm-full]');
  const csrf=document.querySelector('meta[name="csrf-token"]')?.content||'';
  let context=null,currentServerId=0,mapPage=1,mapQuery='',requestGeneration=0;
  const esc=s=>String(s??'').replace(/[&<>"']/g,m=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#039;'}[m]));
  const close=()=>{requestGeneration++;overlay.hidden=true;document.body.classList.remove('modal-open');};
  overlay.querySelector('[data-qm-close]')?.addEventListener('click',close);overlay.addEventListener('click',e=>{if(e.target===overlay)close();});
  document.addEventListener('keydown',e=>{if(e.key==='Escape'&&!overlay.hidden)close();});
  const setCrumb=text=>{crumb.textContent=text?`› ${text}`:'';};
  const flash=(message,ok=true)=>{feedback.textContent=message;feedback.className=ok===null?'muted':ok?'text-good':'status-danger';};
  const post=async(operation,data={})=>{
    const serverId=currentServerId,generation=++requestGeneration;
    const isCurrent=()=>generation===requestGeneration&&serverId===currentServerId&&!overlay.hidden;
    const buttons=Array.from(body.querySelectorAll('button')).filter(b=>!b.disabled);
    buttons.forEach(b=>{b.disabled=true;});
    const show=(message,ok)=>{if(isCurrent())flash(message,ok);};
    try{
    show('Befehl wird gesendet …',null);
    const form=new URLSearchParams({_csrf:csrf,operation,...Object.fromEntries(Object.entries(data).map(([k,v])=>[k,String(v)]))});
    const r=await fetch(`/servers/${serverId}/quick-manage`,{method:'POST',headers:{Accept:'application/json','Content-Type':'application/x-www-form-urlencoded;charset=UTF-8'},credentials:'same-origin',body:form});
    const out=await r.json().catch(()=>({ok:false,error:`HTTP ${r.status}`}));
    if(!r.ok||!out.ok)throw new Error(out.error||'Aktion fehlgeschlagen.');
    if(out.state!=='queued'||!out.command_id){show(out.message||'Aktion wurde gespeichert.',true);return out;}
    show(out.message,null);
    for(let attempt=0;attempt<15;attempt++){
      await new Promise(resolve=>setTimeout(resolve,2000));
      if(!isCurrent())return out;
      const reply=await fetch(`/servers/${serverId}/quick-manage/result?command_id=${encodeURIComponent(out.command_id)}`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store',signal:AbortSignal.timeout(10000)});
      const result=await reply.json().catch(()=>({ok:false,error:`HTTP ${reply.status}`}));
      if(!reply.ok||!result.ok)throw new Error(`Bestätigung konnte nicht gelesen werden: ${result.error||'Unbekannter Fehler'}`);
      if(result.state==='confirmed'){show(result.message,true);return result;}
      if(result.state==='failed'){show(`Nicht bestätigt: ${result.message}`,false);return result;}
    }
    show('Noch keine Bestätigung der Bridge. Status in der Liveverwaltung prüfen; den Befehl nicht mehrfach senden.',null);
    return out;
    }catch(e){show(e.message||'Keine Bestätigung verfügbar. Status in der Liveverwaltung prüfen.',false);return null;}
    finally{buttons.forEach(b=>{b.disabled=false;});}
  };
  const humanPlayers=()=>context.players.filter(p=>!p.is_bot&&/^7656119\d{10}$/.test(String(p.steam_id64||'')));
  const playerOptions=()=>humanPlayers().map(p=>`<option value="${esc(p.steam_id64)}" data-name="${esc(p.name)}">${esc(p.name)} · ${esc(p.team)} · ${Number(p.kills||0)} K · ${Number(p.score||0)} P</option>`).join('');
  const goHome=()=>{setCrumb('');const groups=[];
    if(context.can.map||context.can.power_start||context.can.power_restart||context.can.power_stop||context.can.rules||context.can.bots) groups.push('<button class="quick-manage-group" data-qm-go="server"><strong>Serververwaltung</strong><span>Mapwechsel, Power, Runden, Warmup, Teams und Bots</span></button>');
    if(context.can.kick||context.can.ban||context.can.warn||context.can.gag||context.can.mute||context.can.slay||context.can.respawn||context.can.team) groups.push('<button class="quick-manage-group" data-qm-go="players"><strong>Spieler & Moderation</strong><span>Kick, Ban, Warn, Gag, Mute, Slay, Respawn und Teamwechsel</span></button>');
    if(context.can.rules||context.can.weapon_mode) groups.push('<button class="quick-manage-group" data-qm-go="rules"><strong>Gameplay & Regeln</strong><span>Friendly Fire, Headshot Only und schneller Zugriff auf weitere Live-Regeln</span></button>');
    if(context.can.message) groups.push('<button class="quick-manage-group" data-qm-go="communication"><strong>Kommunikation</strong><span>Servernachricht und direkter Zugriff auf Spielerkommunikation</span></button>');
    groups.push('<button class="quick-manage-group" data-qm-go="advanced"><strong>Erweitert</strong><span>Vollständige Live-Verwaltung und weitere Spezialfunktionen</span></button>');
    body.innerHTML=`<div class="quick-manage-grid">${groups.join('')}</div>`;bindNav();};
  const bindNav=()=>body.querySelectorAll('[data-qm-go]').forEach(b=>b.addEventListener('click',()=>renderSection(b.dataset.qmGo)));
  const renderServer=()=>{setCrumb('Serververwaltung');const groups=[];
    if(context.can.map) groups.push('<button class="quick-manage-group" data-qm-go="maps"><strong>Map wechseln</strong><span>Installierte Workshopmaps, Suche und 10 Maps je Seite</span></button>');
    if(context.can.power_start||context.can.power_restart||context.can.power_stop) groups.push('<button class="quick-manage-group" data-qm-go="power"><strong>Power</strong><span>Start, Restart und Stop nach Berechtigung</span></button>');
    if(context.can.map||context.can.rules) groups.push('<button class="quick-manage-group" data-qm-go="round"><strong>Runde & Teams</strong><span>Rundenrestart, Warmup, Teams tauschen oder mischen</span></button>');
    if(context.can.bots) groups.push('<button class="quick-manage-group" data-qm-go="bots"><strong>Bots</strong><span>T-/CT-Bot hinzufügen oder alle Bots entfernen</span></button>');
    body.innerHTML=`<h3 class="quick-manage-section-title">Serververwaltung</h3><div class="quick-manage-grid">${groups.join('')||'<div class="muted">Keine Server-Schnellaktionen für deine Rolle freigegeben.</div>'}</div>`;bindNav();};
  const renderMaps=()=>{if(!context.can.map){body.innerHTML='<div class="quick-manage-result error">Keine Berechtigung für Mapwechsel.</div>';return;}setCrumb('Serververwaltung › Map wechseln');const maps=context.maps.filter(m=>!mapQuery||`${m.name} ${m.id} ${m.source_folder}`.toLowerCase().includes(mapQuery.toLowerCase()));const pages=Math.max(1,Math.ceil(maps.length/10));mapPage=Math.min(mapPage,pages);const rows=maps.slice((mapPage-1)*10,mapPage*10).map(m=>`<div class="quick-manage-row"><div class="quick-manage-row-main"><strong>${esc(m.name)}</strong><small>Workshop ${esc(m.id)}${m.source_folder?` · ${esc(m.source_folder)}`:''}</small></div><button class="btn btn-primary btn-small" data-qm-map="${esc(m.id)}">Wechseln</button></div>`).join('')||'<div class="muted">Keine passende Map gefunden.</div>';body.innerHTML=`<div class="quick-manage-toolbar"><button class="btn btn-secondary btn-small" data-qm-go="server">← Serververwaltung</button></div><label>Map suchen<input type="search" data-qm-map-search placeholder="Mapname oder Workshop-ID" value="${esc(mapQuery)}"></label><div class="quick-manage-list" style="margin-top:12px">${rows}</div><div class="quick-manage-pagination"><button class="btn btn-secondary btn-small" data-qm-prev ${mapPage<=1?'disabled':''}>←</button><span>Seite ${mapPage} / ${pages} · ${maps.length} Maps</span><button class="btn btn-secondary btn-small" data-qm-next ${mapPage>=pages?'disabled':''}>→</button></div>`;bindNav();body.querySelector('[data-qm-map-search]')?.addEventListener('input',e=>{mapQuery=e.target.value;mapPage=1;renderMaps();});body.querySelector('[data-qm-prev]')?.addEventListener('click',()=>{mapPage--;renderMaps();});body.querySelector('[data-qm-next]')?.addEventListener('click',()=>{mapPage++;renderMaps();});body.querySelectorAll('[data-qm-map]').forEach(b=>b.addEventListener('click',async()=>{if(!confirm(`Map wirklich wechseln?`))return;try{await post('map_change',{map:b.dataset.qmMap,seconds:5});}catch(e){flash(e.message,false);}}));};
  const renderPower=()=>{setCrumb('Serververwaltung › Power');body.innerHTML=`<div class="quick-manage-toolbar"><button class="btn btn-secondary btn-small" data-qm-go="server">← Serververwaltung</button></div><div class="quick-manage-actions">${context.can.power_start?'<button class="btn btn-success" data-qm-op="power_start">Start</button>':''}${context.can.power_restart?'<button class="btn btn-secondary" data-qm-op="power_restart">Restart</button>':''}${context.can.power_stop?'<button class="btn btn-danger" data-qm-op="power_stop">Stop</button>':''}</div>`;bindNav();bindSimpleOps();};
  const renderRound=()=>{setCrumb('Serververwaltung › Runde & Teams');const actions=[];if(context.can.map)actions.push('<button class="btn btn-secondary" data-qm-op="round_restart">Runde neu starten</button>');if(context.can.rules){actions.push('<button class="btn btn-secondary" data-qm-op="warmup_start">Warmup starten</button>','<button class="btn btn-secondary" data-qm-op="warmup_end">Warmup beenden</button>','<button class="btn btn-secondary" data-qm-op="swap_teams">Teams tauschen</button>','<button class="btn btn-secondary" data-qm-op="scramble_teams">Teams mischen</button>');}body.innerHTML=`<div class="quick-manage-toolbar"><button class="btn btn-secondary btn-small" data-qm-go="server">← Serververwaltung</button></div><div class="quick-manage-actions">${actions.join('')||'<span class="muted">Keine Runden-/Teamaktion freigegeben.</span>'}</div>`;bindNav();bindSimpleOps();};
  const renderBots=()=>{if(!context.can.bots){body.innerHTML='<div class="quick-manage-result error">Keine Berechtigung für Bot-Steuerung.</div>';return;}setCrumb('Serververwaltung › Bots');body.innerHTML=`<div class="quick-manage-toolbar"><button class="btn btn-secondary btn-small" data-qm-go="server">← Serververwaltung</button></div><div class="quick-manage-actions"><button class="btn btn-secondary" data-qm-op="bot_add_t">T-Bot hinzufügen</button><button class="btn btn-secondary" data-qm-op="bot_add_ct">CT-Bot hinzufügen</button><button class="btn btn-danger" data-qm-op="bot_kick">Alle Bots entfernen</button></div>`;bindNav();bindSimpleOps();};
  const renderPlayers=()=>{setCrumb('Spieler & Moderation');const opts=playerOptions();body.innerHTML=`<div class="quick-manage-form"><label>Online-Spieler<select class="quick-manage-player-select" data-qm-player>${opts}</select></label><div class="quick-manage-form-grid"><label>Dauer (Minuten)<input type="number" min="0" max="5256000" value="60" data-qm-minutes></label><label>Grund<input type="text" maxlength="160" value="Schnellverwaltung" data-qm-reason></label></div><div class="quick-manage-actions">${context.can.kick?'<button class="btn btn-danger" data-qm-player-op="kick">Kick</button>':''}${context.can.ban?'<button class="btn btn-danger" data-qm-player-op="ban">Ban</button>':''}${context.can.warn?'<button class="btn btn-secondary" data-qm-player-op="warn">Warn</button>':''}${context.can.gag?'<button class="btn btn-secondary" data-qm-player-op="gag">Gag</button><button class="btn btn-secondary" data-qm-player-op="ungag">Ungag</button>':''}${context.can.mute?'<button class="btn btn-secondary" data-qm-player-op="mute">Mute</button><button class="btn btn-secondary" data-qm-player-op="unmute">Unmute</button>':''}${context.can.slay?'<button class="btn btn-secondary" data-qm-player-op="slay">Kill/Slay</button>':''}${context.can.respawn?'<button class="btn btn-secondary" data-qm-player-op="respawn">Respawn</button>':''}</div>${context.can.team?'<div class="quick-manage-actions"><button class="btn btn-secondary" data-qm-team="t">→ T</button><button class="btn btn-secondary" data-qm-team="ct">→ CT</button><button class="btn btn-secondary" data-qm-team="spectator">→ Zuschauer</button></div>':''}</div>`;body.querySelectorAll('[data-qm-player-op]').forEach(b=>b.addEventListener('click',async()=>{const sel=body.querySelector('[data-qm-player]');if(!sel?.value)return flash('Kein menschlicher Spieler ausgewählt.',false);const name=sel.options[sel.selectedIndex]?.dataset.name||'';if(['kick','ban'].includes(b.dataset.qmPlayerOp)&&!confirm(`${b.dataset.qmPlayerOp.toUpperCase()} für ${name} wirklich ausführen?`))return;try{await post(b.dataset.qmPlayerOp,{steam_id64:sel.value,player_name:name,minutes:body.querySelector('[data-qm-minutes]')?.value||60,reason:body.querySelector('[data-qm-reason]')?.value||''});}catch(e){flash(e.message,false);}}));body.querySelectorAll('[data-qm-team]').forEach(b=>b.addEventListener('click',async()=>{const sel=body.querySelector('[data-qm-player]');if(!sel?.value)return;try{await post('team',{steam_id64:sel.value,team:b.dataset.qmTeam});}catch(e){flash(e.message,false);}}));};
  const renderRules=()=>{setCrumb('Gameplay & Regeln');const actions=[];if(context.can.rules)actions.push('<button class="btn btn-secondary" data-qm-op="friendly_fire_on">Friendly Fire AN</button>','<button class="btn btn-secondary" data-qm-op="friendly_fire_off">Friendly Fire AUS</button>');if(context.can.rules&&context.can.weapon_mode)actions.push('<button class="btn btn-secondary" data-qm-op="headshot_on">Headshot Only AN</button>','<button class="btn btn-secondary" data-qm-op="headshot_off">Headshot Only AUS</button>');body.innerHTML=`<div class="quick-manage-actions">${actions.join('')||'<span class="muted">Keine Gameplay-Schnellregeln freigegeben.</span>'}</div><p class="muted" style="margin-top:16px">Weitere Regeln bleiben in der vollständigen Liveverwaltung verfügbar.</p>`;bindSimpleOps();};
  const renderCommunication=()=>{if(!context.can.message){body.innerHTML='<div class="quick-manage-result error">Keine Berechtigung für Servernachrichten.</div>';return;}setCrumb('Kommunikation');body.innerHTML=`<div class="quick-manage-form"><label>Servernachricht<textarea rows="5" maxlength="500" data-qm-message placeholder="Nachricht an alle Spieler"></textarea></label><button class="btn btn-primary" data-qm-send>Nachricht senden</button></div>`;body.querySelector('[data-qm-send]')?.addEventListener('click',async()=>{try{await post('broadcast',{message:body.querySelector('[data-qm-message]')?.value||''});body.querySelector('[data-qm-message]').value='';}catch(e){flash(e.message,false);}});};
  const renderAdvanced=()=>{setCrumb('Erweitert');body.innerHTML=`<p>Konsole, ConVars und seltene Spezialaktionen bleiben bewusst in der vollständigen Liveverwaltung, damit kritische Befehle nicht versehentlich per Schnellklick ausgelöst werden.</p><a class="btn btn-primary" href="/servers/${currentServerId}/live#advanced">Erweiterte Liveverwaltung öffnen</a>`;};
  const bindSimpleOps=()=>body.querySelectorAll('[data-qm-op]').forEach(b=>b.addEventListener('click',async()=>{if(b.dataset.qmOp.startsWith('power_')&&!confirm(`${b.textContent} wirklich ausführen?`))return;try{await post(b.dataset.qmOp);}catch(e){flash(e.message,false);}}));
  const renderSection=key=>({server:renderServer,maps:renderMaps,power:renderPower,round:renderRound,bots:renderBots,players:renderPlayers,rules:renderRules,communication:renderCommunication,advanced:renderAdvanced}[key]||goHome)();
  overlay.querySelector('[data-qm-home]')?.addEventListener('click',goHome);
  const open=async(row)=>{requestGeneration++;currentServerId=Number(row.dataset.serverId||0);if(!currentServerId)return;overlay.hidden=false;document.body.classList.add('modal-open');title.textContent=row.dataset.serverName||'Server';statusNode.textContent='Live-Status wird geladen …';body.innerHTML='<div class="quick-manage-loading">Schnellverwaltung wird geladen …</div>';feedback.textContent='Lade Daten …';full.href=`/servers/${currentServerId}/live`;try{const r=await fetch(`/servers/${currentServerId}/quick-manage`,{headers:{Accept:'application/json'},credentials:'same-origin',cache:'no-store'});context=await r.json();if(!context.ok)throw new Error(context.error||'Schnellverwaltung konnte nicht geladen werden.');const s=context.status;statusNode.textContent=`${s.online?'ONLINE':'OFFLINE'} · ${s.map||'—'} · ${s.players}${s.max_players?'/'+s.max_players:''} Spieler · T ${s.t_score}:${s.ct_score} CT`;feedback.textContent='Bereit';goHome();}catch(e){body.innerHTML=`<div class="quick-manage-result error">${esc(e.message)}</div>`;flash(e.message,false);}};
  document.querySelectorAll('[data-server-quick]').forEach(row=>{
    row.addEventListener('contextmenu',e=>{
      if(e.target.closest('a,button,input,select,textarea,label,form'))return;
      e.preventDefault();
      open(row);
    });
    row.querySelector('[data-server-quick-open]')?.addEventListener('click',e=>{
      e.preventDefault();
      e.stopPropagation();
      open(row);
    });
  });
});


// v1.9.2: Passkey/WebAuthn Registrierung und 2FA.
(()=>{
  const b64ToBytes=v=>{v=(v||'').replace(/-/g,'+').replace(/_/g,'/');while(v.length%4)v+='=';const raw=atob(v);return Uint8Array.from(raw,c=>c.charCodeAt(0));};
  const bytesToB64=v=>{const bytes=new Uint8Array(v);let s='';bytes.forEach(b=>s+=String.fromCharCode(b));return btoa(s).replace(/\+/g,'-').replace(/\//g,'_').replace(/=+$/,'');};
  const decodeCreation=o=>{const x=structuredClone(o);x.challenge=b64ToBytes(x.challenge);x.user.id=b64ToBytes(x.user.id);(x.excludeCredentials||[]).forEach(c=>c.id=b64ToBytes(c.id));return x;};
  const decodeRequest=o=>{const x=structuredClone(o);x.challenge=b64ToBytes(x.challenge);(x.allowCredentials||[]).forEach(c=>c.id=b64ToBytes(c.id));return x;};
  const serializeCredential=c=>({id:c.id,rawId:bytesToB64(c.rawId),type:c.type,response:{clientDataJSON:bytesToB64(c.response.clientDataJSON),...(c.response.attestationObject?{attestationObject:bytesToB64(c.response.attestationObject),transports:typeof c.response.getTransports==='function'?c.response.getTransports():[]}:{authenticatorData:bytesToB64(c.response.authenticatorData),signature:bytesToB64(c.response.signature),userHandle:c.response.userHandle?bytesToB64(c.response.userHandle):null})}});
  const json=async(r)=>{let d={};try{d=await r.json();}catch{}if(!r.ok||!d.ok)throw new Error(d.error||`HTTP ${r.status}`);return d;};
  const profile=document.querySelector('[data-passkey-profile]');
  if(profile){
    profile.querySelector('[data-passkey-register]')?.addEventListener('click',async()=>{
      const status=profile.querySelector('[data-passkey-status]'), password=profile.querySelector('[data-passkey-password]'), label=profile.querySelector('[data-passkey-label]');
      try{
        if(!window.PublicKeyCredential)throw new Error('Dieser Browser unterstützt keine Passkeys/WebAuthn.');
        status.textContent='Passkey-Anfrage wird vorbereitet …';
        const fd=new FormData();fd.set('_csrf',profile.dataset.csrf||'');fd.set('current_password',password?.value||'');fd.set('label',label?.value||'Passkey');
        const opts=await json(await fetch('/profile/passkeys/options',{method:'POST',body:fd,credentials:'same-origin'}));
        const cred=await navigator.credentials.create({publicKey:decodeCreation(opts.public_key)});if(!cred)throw new Error('Passkey-Registrierung wurde abgebrochen.');
        status.textContent='Passkey wird geprüft …';
        const done=await json(await fetch('/profile/passkeys/register',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':profile.dataset.csrf||''},body:JSON.stringify({credential:serializeCredential(cred)}),credentials:'same-origin'}));
        status.textContent='Passkey erfolgreich registriert.';setTimeout(()=>location.reload(),500);
      }catch(e){status.textContent=e?.message||'Passkey-Registrierung fehlgeschlagen.';}
    });
  }
  const login=document.querySelector('[data-passkey-login]');
  if(login){
    login.querySelector('[data-passkey-login-button]')?.addEventListener('click',async()=>{
      const status=login.querySelector('[data-passkey-login-status]');
      try{
        if(!window.PublicKeyCredential)throw new Error('Dieser Browser unterstützt keine Passkeys/WebAuthn.');
        status.textContent='Passkey wird angefordert …';
        const fd=new FormData();fd.set('_csrf',login.dataset.csrf||'');
        const opts=await json(await fetch('/login/2fa/passkey/options',{method:'POST',body:fd,credentials:'same-origin'}));
        const cred=await navigator.credentials.get({publicKey:decodeRequest(opts.public_key)});if(!cred)throw new Error('Passkey-Anmeldung wurde abgebrochen.');
        const done=await json(await fetch('/login/2fa/passkey/finish',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':login.dataset.csrf||''},body:JSON.stringify({credential:serializeCredential(cred)}),credentials:'same-origin'}));
        location.href=done.redirect||'/dashboard';
      }catch(e){status.textContent=e?.message||'Passkey-Anmeldung fehlgeschlagen.';}
    });
  }
})();
