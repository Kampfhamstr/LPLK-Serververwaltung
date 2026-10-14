/* Serververwaltung v1.15.1: progressive UI-Hilfen und bestätigte Pflegeanimationen. */
(()=>{
  'use strict';

  const CARE_ACTIONS={
    feed:{label:'Futterzeit',prop:'🥣',particles:['🍖','🥕','✨'],success:'Lecker!',message:name=>`${name} knuspert zufrieden sein Futter.`},
    pet:{label:'Fellpflege',prop:'🪮',particles:['💛','✨','💛'],success:'genießt die Aufmerksamkeit',message:name=>`${name} genießt Streicheleinheiten und eine kleine Fellpflege.`},
    play:{label:'Spielzeit',prop:'🎾',particles:['⭐','💨','⭐'],success:'Was für ein Spaß',message:name=>`${name} jagt begeistert dem Ball hinterher.`},
    sleep:{label:'Ruhezeit',prop:'🛏️',particles:['💤','🌙','💤'],success:'gut ausgeruht',message:name=>`${name} rollt sich ein und träumt eine Runde.`},
    heal:{label:'Heilpflege',prop:'🩹',particles:['💚','✨','💚'],success:'Grundpflege',message:name=>`${name} wird sanft gepflegt und leuchtet wieder gesund.`},
    train:{label:'Training',prop:'🏋️',particles:['💪','⚡','💪'],success:'Training geschafft',message:name=>`${name} absolviert konzentriert seine Trainingsrunde.`},
  };
  const storageGet=key=>{try{return window.sessionStorage.getItem(key);}catch{return null;}};
  const storageSet=(key,value)=>{try{window.sessionStorage.setItem(key,value);}catch{}};
  const storageRemove=key=>{try{window.sessionStorage.removeItem(key);}catch{}};

  const initPetCareAnimations=()=>{
    const stage=document.querySelector('[data-pet-action-stage]');
    if(!stage)return;
    const prop=stage.querySelector('[data-pet-action-prop]');
    const particles=[...stage.querySelectorAll('[data-pet-action-particles] i')];
    const feedback=stage.querySelector('[data-pet-action-feedback]');
    const forms=[...document.querySelectorAll('[data-pet-action-form]')];
    const petId=String(stage.dataset.petId||'active').replace(/[^0-9]/g,'')||'active';
    const petName=String(stage.dataset.petName||'Dein Pet').trim().slice(0,60)||'Dein Pet';
    const storageKey=`serververwaltung.petCareAction.${petId}`;
    const actionClasses=Object.keys(CARE_ACTIONS).map(action=>`pet-action-${action}`);
    let finishTimer=0;
    let finishStartedAt=0;
    let finishRemainingMs=0;
    let actionRunning=false;
    let pauseObserver=null;
    const clockNow=()=>typeof performance!=='undefined'&&typeof performance.now==='function'?performance.now():Date.now();

    forms.forEach(form=>form.addEventListener('submit',()=>{
      const action=String(form.dataset.petAction||'');
      if(!CARE_ACTIONS[action])return;
      storageSet(storageKey,JSON.stringify({action,submittedAt:Date.now()}));
    }));

    const raw=storageGet(storageKey);
    if(!raw)return;
    storageRemove(storageKey);
    let pending=null;
    try{pending=JSON.parse(raw);}catch{return;}
    const action=typeof pending?.action==='string'?pending.action:'';
    const submittedAt=Number(pending?.submittedAt)||0;
    const config=CARE_ACTIONS[action];
    if(!config||submittedAt<=0||Date.now()-submittedAt>45000)return;
    const successful=[...document.querySelectorAll('.alert-success')].some(alert=>String(alert.textContent||'').includes(config.success));
    if(!successful)return;

    const actionPaused=()=>document.hidden||document.documentElement.classList.contains('pet-motion-paused');
    const pauseFinishClock=()=>{
      if(!finishTimer)return;
      window.clearTimeout(finishTimer);finishTimer=0;
      finishRemainingMs=Math.max(0,finishRemainingMs-(clockNow()-finishStartedAt));
    };
    const clear=()=>{
      actionRunning=false;
      window.clearTimeout(finishTimer);finishTimer=0;
      finishStartedAt=0;finishRemainingMs=0;
      stage.classList.remove('is-pet-action-active',...actionClasses);
      stage.removeAttribute('data-active-pet-action');
      document.body.classList.remove('pet-care-action-active');
      if(prop)prop.textContent='';
      particles.forEach(particle=>{particle.textContent='';});
      if(feedback){feedback.hidden=true;feedback.textContent='';}
    };
    const scheduleFinish=()=>{
      if(!actionRunning||finishTimer||actionPaused())return;
      if(finishRemainingMs<=0){clear();return;}
      finishStartedAt=clockNow();
      finishTimer=window.setTimeout(()=>{finishTimer=0;finishRemainingMs=0;clear();},finishRemainingMs);
    };
    const syncFinishClock=()=>{
      if(!actionRunning)return;
      if(actionPaused())pauseFinishClock();else scheduleFinish();
    };
    const play=()=>{
      if(document.hidden){document.addEventListener('visibilitychange',()=>{if(!document.hidden)play();},{once:true});return;}
      clear();
      stage.classList.add(`pet-action-${action}`,'is-pet-action-active');
      stage.dataset.activePetAction=action;
      document.body.classList.add('pet-care-action-active');
      if(prop)prop.textContent=config.prop;
      particles.forEach((particle,index)=>{particle.textContent=config.particles[index]||'✨';});
      if(feedback){feedback.hidden=false;feedback.textContent=`${config.label}: ${config.message(petName)}`;}
      actionRunning=true;finishRemainingMs=action==='sleep'?4000:3400;scheduleFinish();
    };
    document.addEventListener('visibilitychange',syncFinishClock);
    if(typeof MutationObserver==='function'){
      pauseObserver=new MutationObserver(syncFinishClock);
      pauseObserver.observe(document.documentElement,{attributes:true,attributeFilter:['class']});
    }
    window.addEventListener('pagehide',()=>{pauseObserver?.disconnect();clear();},{once:true});
    play();
  };

  const formatDuration=seconds=>{
    const safe=Math.max(0,Math.ceil(seconds));
    const hours=Math.floor(safe/3600);
    const minutes=Math.floor((safe%3600)/60);
    const rest=safe%60;
    if(hours>0)return `${hours} Std. ${String(minutes).padStart(2,'0')} Min.`;
    return `${minutes}:${String(rest).padStart(2,'0')} Min.`;
  };

  const initAdventureCountdown=()=>{
    const root=document.querySelector('[data-ready-at-epoch]');
    if(!root)return;
    const readyAt=Number(root.dataset.readyAtEpoch||0);
    const startedAt=Number(root.dataset.startedAtEpoch||0);
    const countdown=root.querySelector('[data-adventure-countdown]');
    const progress=root.querySelector('[data-adventure-progress]');
    const state=root.querySelector('[data-adventure-state]');
    const waitingNote=root.querySelector('[data-adventure-wait-note]');
    const buttons=[...root.querySelectorAll('[data-enable-when-ready]')];
    if(!Number.isFinite(readyAt)||readyAt<=0)return;
    let timer=0;
    const update=()=>{
      const now=Math.floor(Date.now()/1000);
      const remaining=Math.max(0,readyAt-now);
      const ready=remaining<=0;
      if(countdown)countdown.textContent=ready?'Bereit zum Abholen':formatDuration(remaining);
      if(progress){
        const full=Math.max(1,readyAt-startedAt);
        const value=ready?100:Math.max(0,Math.min(99,Math.round(((now-startedAt)/full)*100)));
        progress.value=value;
        progress.setAttribute('aria-label',ready?'Abenteuer abgeschlossen':`Abenteuer zu ${value} Prozent abgeschlossen`);
      }
      if(state)state.textContent=ready?'Beute bereit':'Unterwegs';
      buttons.forEach(button=>{
        button.disabled=!ready;
        if(ready)button.textContent='Beute bergen';
        else if(button.dataset.waitingLabel)button.textContent=button.dataset.waitingLabel;
      });
      if(waitingNote)waitingNote.hidden=ready;
      if(ready&&timer){window.clearInterval(timer);timer=0;}
    };
    update();
    if(readyAt>Math.floor(Date.now()/1000))timer=window.setInterval(update,1000);
    document.addEventListener('visibilitychange',()=>{if(!document.hidden)update();});
  };

  const initItemFilter=()=>{
    const filter=document.querySelector('[data-pet-item-filter]');
    const grid=document.querySelector('[data-pet-item-grid]');
    if(!filter||!grid)return;
    const cards=[...grid.querySelectorAll('[data-pet-item-card]')];
    const empty=document.querySelector('[data-pet-item-filter-empty]');
    const apply=()=>{
      const value=String(filter.value||'all');
      let visible=0;
      cards.forEach(card=>{
        const show=value==='all'||card.dataset.itemSlot===value;
        card.hidden=!show;
        if(show)visible+=1;
      });
      if(empty)empty.hidden=visible!==0;
    };
    filter.addEventListener('change',apply);
  };

  const initArenaCooldown=()=>{
    const root=document.querySelector('[data-arena-cooldown-epoch]');
    if(!root)return;
    const end=Number(root.dataset.arenaCooldownEpoch||0);
    const text=root.querySelector('[data-arena-cooldown-text]');
    if(!Number.isFinite(end)||end<=0||!text)return;
    let timer=0;
    const update=()=>{
      const remaining=Math.max(0,end-Math.floor(Date.now()/1000));
      text.textContent=remaining>0?formatDuration(remaining):'jetzt möglich';
      if(remaining<=0&&timer){window.clearInterval(timer);timer=0;window.location.reload();}
    };
    update();
    if(end>Math.floor(Date.now()/1000))timer=window.setInterval(update,1000);
  };

  const initBattleDetails=()=>{
    const details=[...document.querySelectorAll('[data-pet-battle-details]')];
    if(!details.length)return;
    const sync=()=>document.body.classList.toggle('pet-arena-replay-active',details.some(item=>item.open));
    details.forEach(item=>item.addEventListener('toggle',sync));
    sync();
  };

  document.addEventListener('DOMContentLoaded',()=>{
    initPetCareAnimations();
    initAdventureCountdown();
    initItemFilter();
    initArenaCooldown();
    initBattleDetails();
  });
})();
