/* Serververwaltung v1.15.1: rein kosmetischer Pet-Begleiter mit neuen Leerlaufszenen. */
(()=>{
  'use strict';

  const IDLE_DELAY_MS=30000;
  const MIN_DOCK_ACTION_MS=6000;
  const MAX_DOCK_ACTION_MS=12000;
  const ESCAPE_STEPS=8;
  const VIEWPORT_MARGIN=18;
  const storageGet=key=>{try{return window.localStorage.getItem(key);}catch{return null;}};
  const storageSet=(key,value)=>{try{window.localStorage.setItem(key,value);}catch{}};
  const sessionGet=key=>{try{return window.sessionStorage.getItem(key);}catch{return null;}};
  const sessionSet=(key,value)=>{try{window.sessionStorage.setItem(key,value);}catch{}};
  const randomBetween=(min,max)=>Math.round(min+Math.random()*(max-min));
  const pick=list=>list[Math.floor(Math.random()*list.length)];
  const media=query=>typeof window.matchMedia==='function'?window.matchMedia(query):{matches:false};
  const listenMedia=(query,listener)=>{
    if(typeof query.addEventListener==='function')query.addEventListener('change',listener);
    else if(typeof query.addListener==='function')query.addListener(listener);
  };

  document.addEventListener('DOMContentLoaded',()=>{
    const home=document.querySelector('[data-sidebar-pet]');
    const dock=home?.querySelector('[data-sidebar-pet-character]');
    const mainPet=document.querySelector('[data-pet-main-character]');
    const toggle=home?.querySelector('[data-pet-motion-toggle]');
    const status=home?.querySelector('[data-pet-status]');
    const overlay=document.querySelector('[data-pet-escape-overlay]');
    const escapePet=overlay?.querySelector('[data-pet-escape-character]');
    const escapeFacing=overlay?.querySelector('[data-pet-escape-facing]');
    const escapeEmoji=overlay?.querySelector('[data-pet-escape-emoji]');
    const escapeBubble=overlay?.querySelector('[data-pet-escape-bubble]');
    const butterflies=[...(overlay?.querySelectorAll('[data-pet-butterfly]')||[])];
    if(!home||!dock||!toggle||!overlay||!escapePet||!escapeEmoji||!escapeBubble)return;

    const owner=String(home.dataset.petOwner||'0').replace(/[^0-9]/g,'')||'0';
    const storageKey=`serververwaltung.petMotion.v1.${owner}`;
    const piddleKey=`serververwaltung.petPiddle.v1.${owner}`;
    const reducedMotion=media('(prefers-reduced-motion: reduce)');
    const finePointer=media('(hover: hover) and (pointer: fine)');
    const playgroundActive=!!document.querySelector('[data-pet-playground]');
    const root=document.documentElement;
    const petName=String(home.dataset.petName||'Pet');
    const petEmoji=String(home.dataset.petEmoji||'🐾');
    const defaultStatus='Bereit für Unsinn';
    const behaviors=[
      {name:'sniff',className:'pet-behavior-sniff',status:'Schnüffelt herum',bubble:'Schnüff …'},
      {name:'sniff',className:'pet-behavior-sniff',status:'Sucht nach Snacks',bubble:'Riecht das nach Snacks?'},
      {name:'hop',className:'pet-behavior-hop',status:'Hüpft vor Freude',bubble:'Hui!'},
      {name:'sleep',className:'pet-behavior-sleep',status:'Macht ein Nickerchen',bubble:'Zzz …'},
      {name:'shake',className:'pet-behavior-shake',status:'Schüttelt sich',bubble:'Brrr!'},
      {name:'groom',className:'pet-behavior-groom',status:'Putzt sein Fell',bubble:'Erst mal fein machen.'},
      {name:'heal',className:'pet-behavior-heal',status:'Strahlt vor Gesundheit',bubble:'Alles wieder glänzend!'},
      {name:'train',className:'pet-behavior-train',status:'Übt fleißig',bubble:'Noch eine Wiederholung!'},
    ];
    const piddleBehavior={name:'piddle',className:'pet-behavior-piddle',status:'Tut ganz unschuldig',bubble:'Ähm … das war schon so.'};
    const behaviorClasses=[...new Set([...behaviors.map(behavior=>behavior.className),piddleBehavior.className])];

    let userEnabled=storageGet(storageKey)!=='disabled';
    let piddleAllowedAt=Math.max(0,Number(sessionGet(piddleKey))||0);
    let windowFocused=typeof document.hasFocus==='function'?document.hasFocus():true;
    let lastActivityAt=Date.now();
    let idleTimer=0;
    let behaviorTimer=0;
    let behaviorEndTimer=0;
    let runToken=0;
    let escapeRunning=false;
    let returning=false;
    let currentPosition={x:0,y:0};
    let lastPointer={x:Math.round(window.innerWidth*.68),y:Math.round(window.innerHeight*.48)};
    let pointerSeen=false;
    const animations=new Set();

    const setStatus=text=>{status.textContent=text;};
    const motionEnabled=()=>userEnabled&&!reducedMotion.matches&&root.dataset.uxMotion!=='quiet';
    const textEntryFocused=()=>{
      const active=document.activeElement;
      return !!active&&(active.matches('input,textarea,select,[contenteditable="true"]'));
    };
    const interfaceBlocked=()=>document.body.classList.contains('modal-open')||
      document.body.classList.contains('activity-digest-open')||
      document.body.classList.contains('pet-arena-replay-active')||
      !!document.querySelector('[data-panel-users-drawer].is-open,.player-action-overlay:not([hidden]),.quick-manage-overlay:not([hidden]),[data-activity-digest]:not([hidden]),dialog[open]');
    const careActionActive=()=>document.body.classList.contains('pet-care-action-active');
    const companionBlocked=()=>interfaceBlocked()||careActionActive();
    const canEscape=()=>motionEnabled()&&finePointer.matches&&windowFocused&&!document.hidden&&!playgroundActive&&!companionBlocked()&&!textEntryFocused()&&typeof escapePet.animate==='function';
    const clearTimer=id=>{if(id)window.clearTimeout(id);};
    const clearIdle=()=>{clearTimer(idleTimer);idleTimer=0;};
    const clearBehaviors=()=>{
      clearTimer(behaviorTimer);clearTimer(behaviorEndTimer);behaviorTimer=0;behaviorEndTimer=0;
      [dock,mainPet].filter(Boolean).forEach(node=>node.classList.remove(...behaviorClasses));
      setStatus(defaultStatus);
    };
    const cancelAnimations=()=>{
      animations.forEach(animation=>{try{animation.cancel();}catch{}});
      animations.clear();
    };
    const updateToggle=()=>{
      const effective=motionEnabled();
      root.classList.toggle('pet-motion-off',!effective);
      root.classList.toggle('pet-motion-paused',!windowFocused||document.hidden||interfaceBlocked());
      toggle.disabled=reducedMotion.matches||root.dataset.uxMotion==='quiet';
      toggle.setAttribute('aria-pressed',effective?'true':'false');
      toggle.textContent=root.dataset.uxMotion==='quiet'?'Animation: Ruhige Ansicht':reducedMotion.matches?'Animation: System aus':`Animation: ${userEnabled?'An':'Aus'}`;
      toggle.setAttribute('aria-label',reducedMotion.matches?'Pet-Animation ist durch die Systemeinstellung für reduzierte Bewegung ausgeschaltet':`Pet-Animation ${userEnabled?'ausschalten':'einschalten'}`);
    };

    const stopEscape=(rearm=false)=>{
      runToken+=1;
      escapeRunning=false;returning=false;
      cancelAnimations();
      overlay.hidden=true;
      overlay.classList.remove('is-cursor-chase','is-butterfly-chase',...behaviorClasses);
      home.classList.remove('is-away');
      setStatus(defaultStatus);
      if(rearm){lastActivityAt=Date.now();armIdle();armBehavior();}
    };

    const armIdle=()=>{
      clearIdle();
      if(!escapeRunning&&canEscape()){
        const remaining=Math.max(0,IDLE_DELAY_MS-(Date.now()-lastActivityAt));
        idleTimer=window.setTimeout(()=>{
          idleTimer=0;
          if(Date.now()-lastActivityAt<IDLE_DELAY_MS){armIdle();return;}
          startEscape();
        },remaining);
      }
    };
    const armBehavior=()=>{
      clearTimer(behaviorTimer);behaviorTimer=0;
      if(!motionEnabled()||!windowFocused||document.hidden||companionBlocked()||escapeRunning)return;
      behaviorTimer=window.setTimeout(runDockBehavior,randomBetween(MIN_DOCK_ACTION_MS,MAX_DOCK_ACTION_MS));
    };
    const runDockBehavior=()=>{
      behaviorTimer=0;
      if(!motionEnabled()||!windowFocused||document.hidden||companionBlocked()||escapeRunning)return;
      let behavior=pick(behaviors);
      if(Date.now()>=piddleAllowedAt&&Math.random()<.04){
        behavior=piddleBehavior;
        piddleAllowedAt=Date.now()+600000;
        sessionSet(piddleKey,String(piddleAllowedAt));
      }
      const targets=[dock,mainPet].filter(Boolean);
      const target=pick(targets);
      target.classList.remove(...behaviorClasses);
      target.classList.add(behavior.className);
      if(target===dock)setStatus(behavior.status);
      clearTimer(behaviorEndTimer);
      behaviorEndTimer=window.setTimeout(()=>{
        target.classList.remove(behavior.className);
        if(target===dock)setStatus(defaultStatus);
        behaviorEndTimer=0;
        armBehavior();
      },behavior.name==='sleep'?2800:behavior.name==='heal'?2400:behavior.name==='groom'?2200:behavior.name==='train'?2000:1500);
    };

    const clampPoint=(x,y,protectBubble=true)=>{
      const viewport=window.visualViewport;
      const width=Math.max(120,viewport?.width||window.innerWidth);
      const height=Math.max(120,viewport?.height||window.innerHeight);
      const offsetLeft=viewport?.offsetLeft||0;
      const offsetTop=viewport?.offsetTop||0;
      const sideMargin=Math.min(protectBubble?105:VIEWPORT_MARGIN,Math.max(8,(width-58)/2));
      const topMargin=Math.min(protectBubble?72:VIEWPORT_MARGIN,Math.max(8,(height-58)/2));
      return {
        x:Math.min(offsetLeft+width-sideMargin-58,Math.max(offsetLeft+sideMargin,Number.isFinite(x)?x:offsetLeft+width/2)),
        y:Math.min(offsetTop+height-VIEWPORT_MARGIN-58,Math.max(offsetTop+topMargin,Number.isFinite(y)?y:offsetTop+height/2)),
      };
    };
    const dockPoint=()=>{
      const rect=dock.getBoundingClientRect();
      return clampPoint(rect.left+rect.width/2-29,rect.top+rect.height/2-29,false);
    };
    const randomPoint=()=>clampPoint(randomBetween(50,Math.max(70,window.innerWidth-90)),randomBetween(70,Math.max(90,window.innerHeight-110)));
    const trackAnimation=animation=>{animations.add(animation);return animation;};
    const moveTo=async(target,duration,token,protectBubble=true)=>{
      if(token!==runToken||!escapeRunning)return false;
      target=clampPoint(target.x,target.y,protectBubble);
      escapeFacing.classList.toggle('is-facing-left',target.x<currentPosition.x);
      const animation=trackAnimation(escapePet.animate([
        {transform:`translate3d(${currentPosition.x}px,${currentPosition.y}px,0)`},
        {transform:`translate3d(${target.x}px,${target.y}px,0)`},
      ],{duration,easing:'cubic-bezier(.35,.05,.2,1)',fill:'forwards'}));
      try{await animation.finished;}catch{return false;}
      if(token!==runToken||!escapeRunning)return false;
      currentPosition=target;
      return true;
    };
    const showEscapeBehavior=behavior=>{
      overlay.classList.remove(...behaviorClasses);
      overlay.classList.add(behavior.className);
      escapeBubble.textContent=behavior.bubble;
      setStatus(behavior.status);
    };
    const startButterflies=()=>{
      butterflies.forEach((butterfly,index)=>{
        const p1=randomPoint(),p2=randomPoint(),p3=randomPoint();
        const animation=butterfly.animate([
          {transform:`translate3d(${p1.x}px,${p1.y}px,0) rotate(-8deg)`},
          {transform:`translate3d(${p2.x}px,${p2.y}px,0) rotate(10deg)`},
          {transform:`translate3d(${p3.x}px,${p3.y}px,0) rotate(-5deg)`},
        ],{duration:randomBetween(3600,5600)+index*250,iterations:Infinity,direction:'alternate',easing:'ease-in-out'});
        trackAnimation(animation);
      });
    };
    const finishEscape=async token=>{
      if(token!==runToken||!escapeRunning||returning)return;
      returning=true;
      overlay.classList.remove('is-cursor-chase','is-butterfly-chase',...behaviorClasses);
      escapeBubble.textContent=`${petName} ist gleich wieder da!`;
      setStatus('Kommt zurück');
      const arrived=await moveTo(dockPoint(),750,token,false);
      if(arrived&&token===runToken)stopEscape(true);
    };
    async function startEscape(){
      idleTimer=0;
      if(!canEscape()||escapeRunning)return;
      clearBehaviors();
      escapeRunning=true;returning=false;
      const token=++runToken;
      const mode=pointerSeen&&Math.random()<.62?'cursor':'butterfly';
      currentPosition=dockPoint();
      escapeEmoji.textContent=petEmoji;
      escapeBubble.textContent=mode==='cursor'?'Da ist die Maus!':'Schmetterlinge!';
      overlay.hidden=false;
      overlay.classList.add(mode==='cursor'?'is-cursor-chase':'is-butterfly-chase');
      home.classList.add('is-away');
      setStatus(mode==='cursor'?'Jagt den Mauszeiger':'Jagt Schmetterlinge');
      if(mode==='butterfly')startButterflies();

      for(let step=0;step<ESCAPE_STEPS;step+=1){
        if(token!==runToken||!escapeRunning||document.hidden||companionBlocked())return stopEscape(false);
        const behavior=pick(behaviors);
        showEscapeBehavior(behavior);
        let target=randomPoint();
        if(mode==='cursor'&&pointerSeen&&step%3!==2){
          const side=lastPointer.x>window.innerWidth/2?-1:1;
          target=clampPoint(lastPointer.x+side*randomBetween(38,64),lastPointer.y+randomBetween(-45,45));
        }else if(mode==='butterfly'&&butterflies.length){
          const butterfly=butterflies[step%butterflies.length].getBoundingClientRect();
          target=clampPoint(butterfly.left+butterfly.width/2-29,butterfly.top+butterfly.height/2-29);
        }
        if(!await moveTo(target,randomBetween(650,1050),token))return;
      }
      await finishEscape(token);
    }

    const resetFromActivity=()=>{
      lastActivityAt=Date.now();
      if(escapeRunning)stopEscape(true);
      else if(!idleTimer)armIdle();
      armBehavior();
    };
    const syncLifecycle=()=>{
      lastActivityAt=Date.now();
      updateToggle();
      if(escapeRunning&&!finePointer.matches)stopEscape(false);
      if(!motionEnabled()||!windowFocused||document.hidden||companionBlocked()){
        clearIdle();clearBehaviors();
        if(escapeRunning)stopEscape(false);
        return;
      }
      armIdle();armBehavior();
    };

    toggle.addEventListener('click',()=>{
      if(reducedMotion.matches)return;
      userEnabled=!userEnabled;
      storageSet(storageKey,userEnabled?'enabled':'disabled');
      syncLifecycle();
    });
    document.addEventListener('pointermove',event=>{
      if(event.pointerType&&event.pointerType!=='mouse'&&event.pointerType!=='pen')return;
      const x=Number(event.clientX),y=Number(event.clientY);
      if(Number.isFinite(x)&&Number.isFinite(y)){lastPointer=clampPoint(x,y,false);pointerSeen=true;}
      if(!escapeRunning){lastActivityAt=Date.now();if(!idleTimer)armIdle();}
    },{passive:true});
    document.addEventListener('pointerdown',resetFromActivity,{passive:true});
    document.addEventListener('touchstart',resetFromActivity,{passive:true});
    document.addEventListener('wheel',resetFromActivity,{passive:true});
    document.addEventListener('scroll',resetFromActivity,{passive:true,capture:true});
    document.addEventListener('keydown',resetFromActivity);
    document.addEventListener('focusin',resetFromActivity);
    document.addEventListener('focusout',()=>window.setTimeout(syncLifecycle,0));
    document.addEventListener('visibilitychange',syncLifecycle);
    document.addEventListener('lplk:motionchange',syncLifecycle);
    window.addEventListener('blur',()=>{windowFocused=false;clearIdle();clearBehaviors();if(escapeRunning)stopEscape(false);updateToggle();});
    window.addEventListener('focus',()=>{windowFocused=true;syncLifecycle();});
    window.addEventListener('resize',()=>{if(escapeRunning)stopEscape(true);else armIdle();},{passive:true});
    window.addEventListener('pagehide',()=>{clearIdle();clearBehaviors();stopEscape(false);});
    window.addEventListener('pageshow',syncLifecycle);
    listenMedia(reducedMotion,syncLifecycle);
    listenMedia(finePointer,syncLifecycle);

    if(typeof MutationObserver==='function'){
      const observer=new MutationObserver(syncLifecycle);
      observer.observe(document.body,{attributes:true,attributeFilter:['class']});
      document.querySelectorAll('[data-panel-users-drawer],.player-action-overlay,.quick-manage-overlay,[data-activity-digest]').forEach(node=>observer.observe(node,{attributes:true,attributeFilter:['class','hidden']}));
    }

    updateToggle();
    setStatus(defaultStatus);
    syncLifecycle();
  });
})();
