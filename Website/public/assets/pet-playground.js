/* Serververwaltung v1.15.1: sichere, synchronisierte Pet-Spielwiesen- und Leerlaufanimationen. */
(()=>{
  'use strict';

  const POLL_INTERVAL_MS=15000;
  const POLL_TIMEOUT_MS=8000;
  const AMBIENT_MIN_MS=5200;
  const AMBIENT_MAX_MS=9000;
  const MAX_PETS=12;
  const VALID_INTERACTIONS=new Set(['greet','sniff','chase','ball']);
  const VALID_TIERS=new Set(['classic','cool','epic']);
  const randomBetween=(min,max)=>Math.round(min+Math.random()*(max-min));
  const wait=ms=>new Promise(resolve=>window.setTimeout(resolve,ms));
  const asText=(value,fallback,max=80)=>{
    const text=typeof value==='string'?value.trim():'';
    return (text||fallback).slice(0,max);
  };
  const asId=value=>{
    const id=Number(value);
    return Number.isSafeInteger(id)&&id>0?id:0;
  };
  const asLevel=value=>Math.max(1,Math.min(20,Number.isFinite(Number(value))?Math.trunc(Number(value)):1));

  document.addEventListener('DOMContentLoaded',()=>{
    const root=document.querySelector('[data-pet-playground]');
    if(!root)return;
    const stage=root.querySelector('[data-playground-stage]');
    const roster=root.querySelector('[data-playground-roster]');
    const caption=root.querySelector('[data-playground-caption]');
    const live=root.querySelector('[data-playground-live]');
    const ball=root.querySelector('[data-playground-ball]');
    const chaseCloud=root.querySelector('[data-playground-chase-cloud]');
    const targetSelect=document.querySelector('[data-playground-target]');
    const petCount=root.querySelector('[data-playground-pet-count]');
    const mateCount=root.querySelector('[data-playground-mate-count]');
    const startsLeftOutput=root.querySelector('[data-playground-starts-left-output]');
    const empty=root.querySelector('[data-playground-empty]');
    const submitButton=document.querySelector('[data-playground-submit]');
    const cooldownBox=document.querySelector('[data-playground-cooldown]');
    const cooldownText=document.querySelector('[data-playground-cooldown-text]');
    const transientBlocker=document.querySelector('[data-playground-transient-blocker]');
    if(!stage||!roster||!caption||!ball||!chaseCloud)return;

    const reducedMotion=typeof window.matchMedia==='function'?window.matchMedia('(prefers-reduced-motion: reduce)'):{matches:false};
    const selfId=asId(root.dataset.playgroundSelfId);
    let startsLeft=Math.max(0,Number(root.dataset.playgroundStartsLeft)||0);
    let hardBlocked=root.dataset.playgroundHardBlocked==='1';
    let blockedReason=asText(root.dataset.playgroundBlockedReason,'',220);
    let globalCooldownEpoch=Math.max(0,Number(root.dataset.playgroundGlobalCooldownEpoch)||0);
    const initialServerNow=Math.max(0,Number(root.dataset.playgroundServerNowEpoch)||0);
    let serverOffsetSeconds=initialServerNow>0?initialServerNow-Date.now()/1000:0;
    let lastEventId=Math.max(0,Number(root.dataset.playgroundLastEventId)||0);
    let lifecycleToken=0;
    let sceneRunning=false;
    let ambientTimer=0;
    let pollTimer=0;
    let availabilityTimer=0;
    let pollController=null;
    let ambientIndex=0;
    const animations=new Set();
    const eventQueue=[];
    const seenEvents=new Set();
    const targetPairCooldowns=new Map();
    const targetServerAllowed=new Map();
    const targetLockedReasons=new Map();
    if(targetSelect)[...targetSelect.options].forEach(option=>{
      const id=asId(option.value);if(!id)return;
      const pairUntil=Math.max(0,Number(option.dataset.pairCooldownEpoch)||0);
      const referenceNow=initialServerNow||Math.floor(Date.now()/1000);
      targetPairCooldowns.set(id,pairUntil);
      targetLockedReasons.set(id,asText(option.dataset.lockedReason,'',160));
      const temporalBlock=pairUntil>referenceNow||globalCooldownEpoch>referenceNow||hardBlocked||startsLeft<=0;
      targetServerAllowed.set(id,option.dataset.targetResting!=='1'&&(option.dataset.serverAllowed!=='0'||temporalBlock));
    });

    const actors=()=>[...roster.querySelectorAll('[data-playground-actor]')];
    const actorById=id=>roster.querySelector(`[data-playground-actor][data-pet-id="${id}"]`);
    const motionAllowed=()=>!reducedMotion.matches&&!document.hidden&&!document.documentElement.classList.contains('pet-motion-off')&&!document.documentElement.classList.contains('pet-motion-paused');
    const actorName=actor=>asText(actor?.dataset.petName,'Pet',60);
    const setCaption=text=>{caption.textContent=text;};
    const announce=text=>{if(live)live.textContent=text;};
    const animationFor=(node,keyframes,options)=>{
      if(!node||typeof node.animate!=='function')return null;
      const animation=node.animate(keyframes,options);
      animations.add(animation);
      const release=()=>animations.delete(animation);
      animation.addEventListener('finish',release,{once:true});
      animation.addEventListener('cancel',release,{once:true});
      return animation;
    };
    const animationDone=async animation=>{
      if(!animation)return false;
      try{await animation.finished;return true;}catch{return false;}
    };
    const clearActorState=actor=>{
      if(actor&&typeof actor.getAnimations==='function')actor.getAnimations().forEach(animation=>{try{animation.cancel();}catch{}});
      actor?.classList.remove('is-performing');
      const figure=actor?.querySelector('[data-playground-figure]');
      const effect=actor?.querySelector('[data-playground-effect]');
      figure?.classList.remove('is-facing-left','is-sniffing','is-hopping','is-running','is-playing','is-napping','is-grooming','is-healing','is-training','is-snacking');
      if(effect){effect.textContent='';effect.removeAttribute('data-effect');}
    };
    const cancelScenes=()=>{
      lifecycleToken+=1;
      animations.forEach(animation=>{try{animation.cancel();}catch{}});
      animations.clear();
      actors().forEach(clearActorState);
      ball.hidden=true;
      chaseCloud.hidden=true;
      sceneRunning=false;
    };
    const clampActorDelta=(actor,x,y)=>{
      const stageRect=stage.getBoundingClientRect();
      const actorRect=actor.getBoundingClientRect();
      return {
        x:Math.max(stageRect.left+6-actorRect.left,Math.min(stageRect.right-6-actorRect.right,x)),
        y:Math.max(stageRect.top+48-actorRect.top,Math.min(stageRect.bottom-8-actorRect.bottom,y)),
      };
    };

    const pairGeometry=(first,second)=>{
      const stageRect=stage.getBoundingClientRect();
      const firstRect=first.getBoundingClientRect();
      const secondRect=second.getBoundingClientRect();
      const actorHalfWidth=Math.max(54,Math.min(100,Math.max(firstRect.width,secondRect.width)/2));
      const actorHalfHeight=Math.max(68,Math.min(104,Math.max(firstRect.height,secondRect.height)/2));
      const maximumSeparation=Math.max(46,(stageRect.width-actorHalfWidth*2-16)/2);
      const separation=Math.max(46,Math.min(88,maximumSeparation));
      const minimumCenter=actorHalfWidth+separation+8;
      const maximumCenter=stageRect.width-actorHalfWidth-separation-8;
      const desiredCenter=((firstRect.left+firstRect.width/2)+(secondRect.left+secondRect.width/2))/2-stageRect.left;
      const centerX=maximumCenter>=minimumCenter?Math.max(minimumCenter,Math.min(maximumCenter,desiredCenter)):stageRect.width/2;
      const desiredY=((firstRect.top+firstRect.height/2)+(secondRect.top+secondRect.height/2))/2-stageRect.top;
      const centerY=Math.max(actorHalfHeight+58,Math.min(stageRect.height-actorHalfHeight-16,desiredY));
      const firstDelta=clampActorDelta(first,centerX-separation-(firstRect.left+firstRect.width/2-stageRect.left),centerY-(firstRect.top+firstRect.height/2-stageRect.top));
      const secondDelta=clampActorDelta(second,centerX+separation-(secondRect.left+secondRect.width/2-stageRect.left),centerY-(secondRect.top+secondRect.height/2-stageRect.top));
      return {
        stageRect,firstMotion:first,secondMotion:second,centerX,centerY,
        first:firstDelta,
        second:secondDelta,
      };
    };
    const preparePair=(first,second,effectText)=>{
      [first,second].forEach(actor=>actor.classList.add('is-performing'));
      const firstFigure=first.querySelector('[data-playground-figure]');
      const secondFigure=second.querySelector('[data-playground-figure]');
      const firstEffect=first.querySelector('[data-playground-effect]');
      const secondEffect=second.querySelector('[data-playground-effect]');
      const firstIsLeft=first.getBoundingClientRect().left<=second.getBoundingClientRect().left;
      firstFigure?.classList.toggle('is-facing-left',!firstIsLeft);
      secondFigure?.classList.toggle('is-facing-left',firstIsLeft);
      [firstFigure,secondFigure].forEach(figure=>figure?.classList.add('is-playing'));
      [firstEffect,secondEffect].forEach(effect=>{if(effect)effect.textContent=effectText;});
    };
    const movePairTogether=(geometry,duration=1050)=>{
      const common={duration,easing:'cubic-bezier(.32,.03,.2,1)',fill:'forwards'};
      const first=animationFor(geometry.firstMotion,[{transform:'translate3d(0,0,0)'},{transform:`translate3d(${geometry.first.x}px,${geometry.first.y}px,0)`}],common);
      const second=animationFor(geometry.secondMotion,[{transform:'translate3d(0,0,0)'},{transform:`translate3d(${geometry.second.x}px,${geometry.second.y}px,0)`}],common);
      return [first,second];
    };

    const greetScene=async(first,second,token)=>{
      const geometry=pairGeometry(first,second);if(!geometry)return;
      preparePair(first,second,'💞');
      const firstFigure=first.querySelector('[data-playground-figure]');
      const secondFigure=second.querySelector('[data-playground-figure]');
      setCaption(`${actorName(first)} und ${actorName(second)} begrüßen sich.`);
      const [a,b]=movePairTogether(geometry,950);
      if(!(await Promise.all([animationDone(a),animationDone(b)])).every(Boolean)||token!==lifecycleToken)return;
      firstFigure?.classList.add('is-hopping');secondFigure?.classList.add('is-hopping');
      await wait(1250);
      try{a?.cancel();b?.cancel();}catch{}
    };
    const sniffScene=async(first,second,token)=>{
      const geometry=pairGeometry(first,second);if(!geometry)return;
      preparePair(first,second,'✨');
      setCaption(`${actorName(first)} und ${actorName(second)} schnüffeln neugierig zusammen.`);
      const [a,b]=movePairTogether(geometry,1050);
      if(!(await Promise.all([animationDone(a),animationDone(b)])).every(Boolean)||token!==lifecycleToken)return;
      first.querySelector('[data-playground-figure]')?.classList.add('is-sniffing');
      second.querySelector('[data-playground-figure]')?.classList.add('is-sniffing');
      await wait(1500);
      try{a?.cancel();b?.cancel();}catch{}
    };
    const ballScene=async(first,second,token)=>{
      const geometry=pairGeometry(first,second);if(!geometry)return;
      preparePair(first,second,'!');
      setCaption(`${actorName(first)} und ${actorName(second)} spielen sich den Ball zu.`);
      const [a,b]=movePairTogether(geometry,920);
      await Promise.all([animationDone(a),animationDone(b)]);
      if(token!==lifecycleToken)return;
      ball.hidden=false;
      const ballRect=ball.getBoundingClientRect();
      const baseX=ballRect.left+ballRect.width/2-geometry.stageRect.left;
      const baseY=ballRect.top+ballRect.height/2-geometry.stageRect.top;
      const leftX=geometry.centerX-47-baseX,rightX=geometry.centerX+47-baseX,y=geometry.centerY-13-baseY;
      const ballAnimation=animationFor(ball,[
        {transform:`translate3d(${leftX}px,${y}px,0) rotate(0deg)`},
        {transform:`translate3d(${rightX}px,${y-32}px,0) rotate(180deg)`,offset:.5},
        {transform:`translate3d(${leftX}px,${y}px,0) rotate(360deg)`},
      ],{duration:1800,iterations:2,easing:'ease-in-out'});
      await animationDone(ballAnimation);
      try{a?.cancel();b?.cancel();}catch{}
    };
    const chaseScene=async(first,second,token)=>{
      const geometry=pairGeometry(first,second);if(!geometry)return;
      preparePair(first,second,'💨');
      first.querySelector('[data-playground-figure]')?.classList.add('is-running');
      second.querySelector('[data-playground-figure]')?.classList.add('is-running');
      setCaption(`${actorName(first)} und ${actorName(second)} liefern sich ein Fangspiel.`);
      chaseCloud.hidden=false;
      const width=Math.max(70,Math.min(180,geometry.stageRect.width*.2));
      const firstOne=clampActorDelta(first,geometry.first.x-width,geometry.first.y-25);
      const firstTwo=clampActorDelta(first,geometry.first.x+width,geometry.first.y+22);
      const firstThree=clampActorDelta(first,geometry.first.x-width*.55,geometry.first.y+36);
      const secondOne=clampActorDelta(second,geometry.second.x-width*.82,geometry.second.y-18);
      const secondTwo=clampActorDelta(second,geometry.second.x+width*.82,geometry.second.y+28);
      const secondThree=clampActorDelta(second,geometry.second.x-width*.35,geometry.second.y+40);
      const firstFrames=[
        {transform:'translate3d(0,0,0)'},
        {transform:`translate3d(${firstOne.x}px,${firstOne.y}px,0)`},
        {transform:`translate3d(${firstTwo.x}px,${firstTwo.y}px,0)`},
        {transform:`translate3d(${firstThree.x}px,${firstThree.y}px,0)`},
        {transform:'translate3d(0,0,0)'},
      ];
      const secondFrames=[
        {transform:'translate3d(0,0,0)'},
        {transform:`translate3d(${secondOne.x}px,${secondOne.y}px,0)`},
        {transform:`translate3d(${secondTwo.x}px,${secondTwo.y}px,0)`},
        {transform:`translate3d(${secondThree.x}px,${secondThree.y}px,0)`},
        {transform:'translate3d(0,0,0)'},
      ];
      const a=animationFor(geometry.firstMotion,firstFrames,{duration:3200,easing:'ease-in-out'});
      await wait(140);if(token!==lifecycleToken)return;
      const b=animationFor(geometry.secondMotion,secondFrames,{duration:3200,easing:'ease-in-out'});
      await Promise.all([animationDone(a),animationDone(b)]);
    };
    const runPairScene=async(type,first,second,serverEvent=false)=>{
      if(sceneRunning||!motionAllowed()||!first||!second||first===second||!first.isConnected||!second.isConnected)return;
      sceneRunning=true;
      const token=lifecycleToken;
      try{
        if(type==='ball')await ballScene(first,second,token);
        else if(type==='chase')await chaseScene(first,second,token);
        else if(type==='sniff')await sniffScene(first,second,token);
        else await greetScene(first,second,token);
        if(serverEvent&&token===lifecycleToken)announce(`${actorName(first)} und ${actorName(second)}: ${type==='ball'?'Ballspiel':type==='chase'?'Fangspiel':type==='sniff'?'gemeinsames Schnüffeln':'Begrüßung'}.`);
      }finally{
        clearActorState(first);clearActorState(second);ball.hidden=true;chaseCloud.hidden=true;sceneRunning=false;
      }
    };
    const wanderScene=async actor=>{
      if(!actor||sceneRunning||!motionAllowed())return;
      sceneRunning=true;const token=lifecycleToken;
      const figure=actor.querySelector('[data-playground-figure]');
      actor.classList.add('is-performing');figure?.classList.add('is-running');
      setCaption(`${actorName(actor)} wandert über die Wiese und schaut sich um.`);
      const direction=Math.random()<.5?-1:1;
      const firstStep=clampActorDelta(actor,direction*randomBetween(22,46),-8);
      const secondStep=clampActorDelta(actor,direction*randomBetween(10,28),8);
      const animation=animationFor(actor,[{transform:'translate3d(0,0,0)'},{transform:`translate3d(${firstStep.x}px,${firstStep.y}px,0)`},{transform:`translate3d(${secondStep.x}px,${secondStep.y}px,0)`},{transform:'translate3d(0,0,0)'}],{duration:2500,easing:'ease-in-out'});
      await animationDone(animation);if(token===lifecycleToken)clearActorState(actor);sceneRunning=false;
    };
    const idleScene=async(actor,type)=>{
      const scenes={
        nap:{className:'is-napping',effect:'💤',duration:3000,caption:name=>`${name} macht ein kleines Nickerchen im Gras.`},
        groom:{className:'is-grooming',effect:'🪮',duration:2400,caption:name=>`${name} putzt sorgfältig sein Fell.`},
        heal:{className:'is-healing',effect:'✨',duration:2600,caption:name=>`${name} strahlt nach einer kleinen Pflegerunde.`},
        train:{className:'is-training',effect:'🏋️',duration:2300,caption:name=>`${name} übt fleißig für das nächste Abenteuer.`},
        snack:{className:'is-snacking',effect:'🥣',duration:2200,caption:name=>`${name} knuspert einen kleinen Wiesensnack.`},
      };
      const scene=scenes[type];
      if(!scene||!actor||sceneRunning||!motionAllowed())return;
      sceneRunning=true;const token=lifecycleToken;
      const figure=actor.querySelector('[data-playground-figure]');
      const effect=actor.querySelector('[data-playground-effect]');
      actor.classList.add('is-performing');figure?.classList.add(scene.className);
      if(effect){effect.textContent=scene.effect;effect.dataset.effect=type;}
      setCaption(scene.caption(actorName(actor)));
      await wait(scene.duration);
      if(token===lifecycleToken)clearActorState(actor);
      sceneRunning=false;
    };

    const pickPair=()=>{
      const list=actors().filter(actor=>!actor.hidden);
      if(list.length<2)return null;
      const first=list[Math.floor(Math.random()*list.length)];
      const remaining=list.filter(actor=>actor!==first);
      return [first,remaining[Math.floor(Math.random()*remaining.length)]];
    };
    const scheduleAmbient=()=>{
      window.clearTimeout(ambientTimer);
      if(!motionAllowed())return;
      ambientTimer=window.setTimeout(async()=>{
        if(!motionAllowed()){scheduleAmbient();return;}
        const queued=eventQueue.shift();
        if(queued){
          const first=actorById(queued.actorPetId),second=actorById(queued.targetPetId);
          await runPairScene(queued.type,first,second,true);
        }else{
          const list=actors();
          if(list.length){
            const idleTypes=['nap','groom','heal','train','snack'];
            if(ambientIndex%4===0)await idleScene(list[Math.floor(Math.random()*list.length)],idleTypes[Math.floor(ambientIndex/4)%idleTypes.length]);
            else if(list.length===1)await wanderScene(list[0]);
            else{
            const pair=pickPair();
            if(pair){
              const types=['greet','sniff','ball','chase'];
              if(ambientIndex%3===1)await wanderScene(pair[0]);
              else await runPairScene(types[ambientIndex%types.length],pair[0],pair[1],false);
            }
            }
            ambientIndex+=1;
          }
        }
        scheduleAmbient();
      },randomBetween(AMBIENT_MIN_MS,AMBIENT_MAX_MS));
    };

    const createActor=pet=>{
      const item=document.createElement('li');
      item.className='pet-playground-actor';
      item.setAttribute('data-playground-actor','');
      const motion=document.createElement('span');motion.className='pet-playground-motion';motion.dataset.playgroundMotion='';motion.setAttribute('aria-hidden','true');
      const figure=document.createElement('span');figure.className='pet-playground-figure';figure.dataset.playgroundFigure='';
      const emoji=document.createElement('span');emoji.className='pet-playground-emoji';emoji.dataset.playgroundEmoji='';
      const effect=document.createElement('span');effect.className='pet-playground-effect';effect.dataset.playgroundEffect='';
      figure.append(emoji,effect);motion.append(figure);
      const plate=document.createElement('span');plate.className='pet-playground-nameplate';
      const nameRow=document.createElement('span');nameRow.className='pet-playground-name-row';
      const name=document.createElement('strong');name.dataset.playgroundName='';nameRow.append(name);
      const detail=document.createElement('span');
      const level=document.createElement('b');level.dataset.playgroundLevel='';
      const separator=document.createTextNode(' · ');
      const species=document.createElement('span');species.dataset.playgroundSpecies='';detail.append(level,separator,species);
      const ownerLine=document.createElement('small');ownerLine.append(document.createTextNode('Besitzer: '));
      const owner=document.createElement('span');owner.dataset.playgroundOwner='';ownerLine.append(owner);
      const mood=document.createElement('small');mood.className='pet-playground-mood';mood.dataset.playgroundMood='';
      plate.append(nameRow,detail,ownerLine,mood);item.append(motion,plate);
      updateActor(item,pet);
      return item;
    };
    const normalizePet=raw=>{
      if(!raw||typeof raw!=='object'||Array.isArray(raw))return null;
      const id=asId(raw.pet_id??raw.id);if(!id)return null;
      const tier=VALID_TIERS.has(raw.tier)?raw.tier:'classic';
      const hasInteractionState=Object.prototype.hasOwnProperty.call(raw,'can_interact');
      return {id,name:asText(raw.name??raw.pet_name,'Pet',60),level:asLevel(raw.level),owner:asText(raw.owner_name??raw.owner,id===selfId?'Du':'Community',80),emoji:asText(raw.emoji,'🐾',16),species:asText(raw.species_label,'Pet',50),tier,isSelf:Boolean(raw.is_self)||id===selfId,ownerOnline:Boolean(raw.owner_online),mood:asText(raw.mood_label,'Spielbereit',50),canInteract:hasInteractionState?Boolean(raw.can_interact):null,pairCooldownUntil:Math.max(0,Number(raw.pair_cooldown_until)||0),targetResting:Boolean(raw.target_resting),lockedReason:asText(raw.locked_reason,'',160)};
    };
    function updateActor(actor,pet){
      actor.dataset.petId=String(pet.id);actor.dataset.petName=pet.name;actor.dataset.isSelf=pet.isSelf?'1':'0';
      actor.classList.remove('pet-playground-tier-classic','pet-playground-tier-cool','pet-playground-tier-epic','is-self');
      actor.classList.add(`pet-playground-tier-${pet.tier}`);actor.classList.toggle('is-self',pet.isSelf);
      actor.querySelector('[data-playground-emoji]').textContent=pet.emoji;
      actor.querySelector('[data-playground-name]').textContent=pet.name;
      actor.querySelector('[data-playground-level]').textContent=`Level ${pet.level}`;
      actor.querySelector('[data-playground-species]').textContent=pet.species;
      actor.querySelector('[data-playground-owner]').textContent=pet.owner;
      actor.querySelector('[data-playground-mood]').textContent=pet.mood;
      const ownerLine=actor.querySelector('[data-playground-owner]')?.parentElement;
      let online=ownerLine?.querySelector('.pet-playground-online');
      if(pet.ownerOnline&&!online&&ownerLine){ownerLine.append(document.createTextNode(' · '));online=document.createElement('span');online.className='pet-playground-online';online.textContent='online';ownerLine.append(online);}
      if(!pet.ownerOnline&&online){const separator=online.previousSibling;if(separator?.nodeType===Node.TEXT_NODE)separator.remove();online.remove();}
      let selfBadge=actor.querySelector('.pet-playground-name-row em');
      if(pet.isSelf&&!selfBadge){selfBadge=document.createElement('em');selfBadge.textContent='Deins';actor.querySelector('.pet-playground-name-row').append(selfBadge);}
      if(!pet.isSelf&&selfBadge)selfBadge.remove();
      actor.setAttribute('aria-label',`${pet.name}, Level ${pet.level}, ${pet.species}, Besitzer ${pet.owner}${pet.isSelf?', dein Pet':''}`);
    }
    const syncTargetOptions=pets=>{
      if(!targetSelect)return;
      const previous=asId(targetSelect.value);
      const candidates=pets.filter(pet=>!pet.isSelf);
      const candidateIds=new Set(candidates.map(pet=>pet.id));
      [targetPairCooldowns,targetServerAllowed,targetLockedReasons].forEach(map=>{for(const id of map.keys())if(!candidateIds.has(id))map.delete(id);});
      targetSelect.replaceChildren();
      if(!candidates.length){const option=document.createElement('option');option.value='';option.textContent='Gerade ist kein anderes Pet verfügbar';targetSelect.append(option);targetSelect.disabled=true;updatePlayAvailability();return;}
      candidates.forEach(pet=>{
        targetPairCooldowns.set(pet.id,pet.pairCooldownUntil||0);
        targetLockedReasons.set(pet.id,pet.lockedReason);
        if(!targetServerAllowed.has(pet.id))targetServerAllowed.set(pet.id,true);
        const temporalBlock=globalCooldownEpoch>serverNow()||pet.pairCooldownUntil>serverNow()||hardBlocked||startsLeft<=0;
        if(pet.targetResting)targetServerAllowed.set(pet.id,false);
        else if(temporalBlock)targetServerAllowed.set(pet.id,true);
        else if(pet.canInteract!==null)targetServerAllowed.set(pet.id,pet.canInteract);
        const option=document.createElement('option');option.value=String(pet.id);option.dataset.pairCooldownEpoch=String(targetPairCooldowns.get(pet.id)||0);option.dataset.targetResting=pet.targetResting?'1':'0';option.dataset.lockedReason=pet.lockedReason;option.title=pet.lockedReason;option.textContent=`${pet.emoji} ${pet.name} · Level ${pet.level} · ${pet.owner}`;targetSelect.append(option);
      });
      if(candidates.some(pet=>pet.id===previous))targetSelect.value=String(previous);
      updatePlayAvailability();
    };
    const serverNow=()=>Math.floor(Date.now()/1000+serverOffsetSeconds);
    function updatePlayAvailability(){
      window.clearTimeout(availabilityTimer);availabilityTimer=0;
      if(!targetSelect){if(submitButton)submitButton.disabled=true;return;}
      const now=serverNow();
      const globalRemaining=Math.max(0,globalCooldownEpoch-now);
      let nextDeadline=globalRemaining>0?globalCooldownEpoch:0;
      const available=[];
      const targetOptions=[...targetSelect.options].filter(option=>asId(option.value)>0);
      targetOptions.forEach(option=>{
        const id=asId(option.value);
        const pairUntil=Math.max(0,targetPairCooldowns.get(id)||Number(option.dataset.pairCooldownEpoch)||0);
        const pairRemaining=Math.max(0,pairUntil-now);
        option.disabled=hardBlocked||startsLeft<=0||globalRemaining>0||pairRemaining>0||targetServerAllowed.get(id)===false;
        option.title=option.disabled?(targetLockedReasons.get(id)||blockedReason||'Dieses Pet ist gerade nicht verfügbar.'):'';
        if(!option.disabled)available.push(option);
        if(pairRemaining>0&&(nextDeadline===0||pairUntil<nextDeadline))nextDeadline=pairUntil;
      });
      if(available.length&&!available.includes(targetSelect.selectedOptions[0]))available[0].selected=true;
      targetSelect.disabled=available.length===0;
      const canSubmit=!hardBlocked&&startsLeft>0&&globalRemaining===0&&available.length>0;
      if(startsLeftOutput)startsLeftOutput.textContent=String(startsLeft);
      if(submitButton){submitButton.disabled=!canSubmit;submitButton.textContent=`Gemeinsam spielen · ${startsLeft} frei`;}
      if(cooldownBox&&cooldownText){
        cooldownText.textContent=globalRemaining>0?`${globalRemaining} Sek.`:'jetzt möglich';
        cooldownBox.hidden=globalRemaining===0;
      }
      if(transientBlocker){
        let status='';
        if(hardBlocked)status=blockedReason||'Gemeinsames Spielen ist gerade nicht verfügbar.';
        else if(startsLeft<=0)status='Für heute sind alle gemeinsamen Spielrunden verbraucht.';
        else if(targetOptions.length===0)status='Für eine gemeinsame Runde muss mindestens ein anderes Pet auf der Spielwiese sein.';
        else if(globalRemaining>0)status=`Kurze Pfotenpause: In ${globalRemaining} Sekunden ist der nächste Spielmoment möglich.`;
        else if(!canSubmit&&nextDeadline>now)status=`Alle aktuellen Spielpaare machen noch ${Math.max(1,nextDeadline-now)} Sekunden Pfotenpause.`;
        else if(!canSubmit)status=targetLockedReasons.get(asId(targetSelect.value))||[...targetLockedReasons.values()].find(Boolean)||'Die verfügbaren Pets ruhen sich gerade aus.';
        transientBlocker.textContent=status;
        transientBlocker.hidden=status==='';
      }
      if(nextDeadline>now)availabilityTimer=window.setTimeout(updatePlayAvailability,1000);
    }
    const syncRoster=rawPets=>{
      if(!Array.isArray(rawPets))return;
      const pets=[];const ids=new Set();
      rawPets.slice(0,MAX_PETS).forEach(raw=>{const pet=normalizePet(raw);if(!pet||ids.has(pet.id))return;ids.add(pet.id);pets.push(pet);});
      if(sceneRunning)cancelScenes();
      pets.forEach(pet=>{
        let actor=actorById(pet.id);
        if(!actor){actor=createActor(pet);roster.append(actor);}else updateActor(actor,pet);
      });
      actors().forEach(actor=>{if(!ids.has(asId(actor.dataset.petId)))actor.remove();});
      pets.forEach(pet=>{const actor=actorById(pet.id);if(actor)roster.append(actor);});
      if(petCount)petCount.textContent=String(pets.length);
      if(mateCount)mateCount.textContent=String(pets.filter(pet=>!pet.isSelf).length);
      empty.hidden=pets.length!==0;
      syncTargetOptions(pets);
      scheduleAmbient();
    };
    const enqueueEvents=rawEvents=>{
      if(!Array.isArray(rawEvents))return;
      rawEvents
        .map(raw=>({id:asId(raw?.id),actorPetId:asId(raw?.actor_pet_id),targetPetId:asId(raw?.target_pet_id),type:VALID_INTERACTIONS.has(raw?.interaction_type)?raw.interaction_type:''}))
        .filter(event=>event.id&&event.actorPetId&&event.targetPetId&&event.actorPetId!==event.targetPetId&&event.type&&!seenEvents.has(event.id)&&event.id>lastEventId)
        .sort((a,b)=>a.id-b.id)
        .forEach(event=>{seenEvents.add(event.id);lastEventId=Math.max(lastEventId,event.id);eventQueue.push(event);});
      while(seenEvents.size>100)seenEvents.delete(seenEvents.values().next().value);
      if(eventQueue.length&&!sceneRunning){window.clearTimeout(ambientTimer);ambientTimer=window.setTimeout(scheduleAmbient,150);}
    };

    const stateUrl=()=>{
      try{
        const url=new URL(String(root.dataset.playgroundStateUrl||'/pets/playground/state'),window.location.origin);
        if(url.origin!==window.location.origin)return null;
        url.searchParams.set('after',String(lastEventId));
        return url;
      }catch{return null;}
    };
    const pollState=async()=>{
      window.clearTimeout(pollTimer);pollTimer=0;
      if(document.hidden){schedulePoll();return;}
      const url=stateUrl();if(!url)return;
      pollController?.abort();pollController=new AbortController();
      const abortTimer=window.setTimeout(()=>pollController?.abort(),POLL_TIMEOUT_MS);
      try{
        const response=await window.fetch(url,{method:'GET',credentials:'same-origin',cache:'no-store',headers:{Accept:'application/json'},signal:pollController.signal});
        if(!response.ok)return;
        const contentType=String(response.headers.get('content-type')||'').toLowerCase();
        if(!contentType.includes('application/json'))return;
        const payload=await response.json();
        if(!payload||typeof payload!=='object'||Array.isArray(payload))return;
        const rosterPayload=payload.playgroundPets??payload.pets??payload.roster;
        const eventsPayload=payload.events??payload.playEvents??payload.playSessions;
        const stateServerTime=Math.max(0,Number(payload.server_time)||0);
        if(stateServerTime>0)serverOffsetSeconds=stateServerTime-Date.now()/1000;
        if(Object.prototype.hasOwnProperty.call(payload,'playStartsLeft'))startsLeft=Math.max(0,Number(payload.playStartsLeft)||0);
        if(Object.prototype.hasOwnProperty.call(payload,'playCooldownUntil'))globalCooldownEpoch=Math.max(0,Number(payload.playCooldownUntil)||0);
        if(Object.prototype.hasOwnProperty.call(payload,'playBlockedReason')){
          const reason=asText(payload.playBlockedReason,'',220);
          blockedReason=reason;
          const cooldownOnly=globalCooldownEpoch>serverNow()&&reason.startsWith('Kurze Pfotenpause:');
          const rosterOnly=reason.includes('kein anderes Community-Pet');
          hardBlocked=reason!==''&&!cooldownOnly&&!rosterOnly;
        }
        if(Array.isArray(rosterPayload))syncRoster(rosterPayload);
        enqueueEvents(eventsPayload);
        lastEventId=Math.max(lastEventId,asId(payload.cursor));
        updatePlayAvailability();
      }catch(error){if(error?.name!=='AbortError')root.dataset.playgroundSync='retry';}
      finally{window.clearTimeout(abortTimer);pollController=null;schedulePoll();}
    };
    const schedulePoll=()=>{
      window.clearTimeout(pollTimer);
      if(document.hidden)return;
      pollTimer=window.setTimeout(pollState,POLL_INTERVAL_MS);
    };
    const syncMotionState=()=>{
      root.classList.toggle('is-motion-off',!motionAllowed());
      if(!motionAllowed()){window.clearTimeout(ambientTimer);cancelScenes();setCaption(reducedMotion.matches?'Animationen sind wegen „Bewegung reduzieren“ ausgeschaltet.':'Die Pet-Animationen sind momentan pausiert.');}
      else scheduleAmbient();
    };
    const syncVisibility=()=>{
      if(document.hidden){window.clearTimeout(ambientTimer);window.clearTimeout(pollTimer);window.clearTimeout(availabilityTimer);pollController?.abort();cancelScenes();}
      else{syncMotionState();pollState();}
    };

    document.addEventListener('visibilitychange',syncVisibility);
    window.addEventListener('pagehide',()=>{window.clearTimeout(ambientTimer);window.clearTimeout(pollTimer);window.clearTimeout(availabilityTimer);pollController?.abort();cancelScenes();});
    window.addEventListener('resize',()=>{if(sceneRunning)cancelScenes();scheduleAmbient();},{passive:true});
    if(typeof MutationObserver==='function')new MutationObserver(syncMotionState).observe(document.documentElement,{attributes:true,attributeFilter:['class']});
    if(typeof reducedMotion.addEventListener==='function')reducedMotion.addEventListener('change',syncMotionState);
    else if(typeof reducedMotion.addListener==='function')reducedMotion.addListener(syncMotionState);

    const featuredType=VALID_INTERACTIONS.has(root.dataset.playgroundFeaturedType)?root.dataset.playgroundFeaturedType:'';
    const featuredActorId=asId(root.dataset.playgroundFeaturedActorId),featuredTargetId=asId(root.dataset.playgroundFeaturedTargetId);
    if(featuredType&&featuredActorId&&featuredTargetId)eventQueue.push({id:lastEventId,actorPetId:featuredActorId,targetPetId:featuredTargetId,type:featuredType});
    updatePlayAvailability();syncMotionState();schedulePoll();
  });
})();
