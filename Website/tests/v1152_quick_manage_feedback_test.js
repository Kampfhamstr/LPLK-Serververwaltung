'use strict';
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const assert=require('node:assert/strict');
const source=fs.readFileSync(path.join(__dirname,'../public/assets/app.js'),'utf8');
const postSource=source.slice(source.indexOf('  const post=async(operation,data={})=>{'),source.indexOf('  const humanPlayers=()=>context.players'));

async function scenario(responses,interrupt){
  const messages=[],urls=[],button={disabled:false};
  let posts=0;
  const context=vm.createContext({URLSearchParams,Object,Array,String,encodeURIComponent,AbortSignal,
    messages,urls,button,flash:(message,ok)=>messages.push({message,ok}),
    body:{querySelectorAll:()=>[button]},overlay:{hidden:false},csrf:'test',currentServerId:5,requestGeneration:0,
    fetch:async(url,options)=>{urls.push(url);if(options.method==='POST'){posts++;assert.equal(button.disabled,true);}
      const response=responses.shift();if(response instanceof Error)throw response;
      assert.ok(response,'Unerwartete zusätzliche Anfrage');return {ok:response.httpOk!==false,status:response.httpOk===false?503:200,json:async()=>response};},
    setTimeout:callback=>{if(interrupt)interrupt(context);callback();},
  });
  await vm.runInContext(postSource+'\npost("respawn",{steam_id64:"76561198000000001"});',context);
  assert.equal(posts,1,'Befehl wird auch bei Fehler/Timeout nur einmal gesendet');
  assert.equal(button.disabled,false,'Schaltflächen werden wieder freigegeben');
  return {messages,urls};
}
const queued=()=>({ok:true,state:'queued',command_id:'command-1',message:'Warte auf Bridge.'});
(async()=>{
  let result=await scenario([queued(),{ok:true,state:'pending'},{ok:true,state:'confirmed',message:'Bestätigt'}]);
  assert.equal(result.messages.at(-1).ok,true);assert.ok(result.messages.slice(0,-1).every(m=>m.ok===null));
  assert.ok(result.urls.slice(1).every(url=>url.includes('/servers/5/quick-manage/result?command_id=command-1')));
  result=await scenario([queued(),{ok:true,state:'failed',message:'Spieler nicht online.'}]);
  assert.equal(result.messages.at(-1).ok,false);assert.match(result.messages.at(-1).message,/Spieler nicht online/);
  result=await scenario([queued(),...Array.from({length:15},()=>({ok:true,state:'pending'}))]);
  assert.equal(result.messages.at(-1).ok,null);assert.match(result.messages.at(-1).message,/keine Bestätigung/);
  result=await scenario([queued(),{ok:false,httpOk:false,error:'Verbindung unterbrochen'}]);
  assert.equal(result.messages.at(-1).ok,false);assert.match(result.messages.at(-1).message,/Verbindung unterbrochen/);
  result=await scenario([{ok:false,error:'Keine Berechtigung'}]);
  assert.equal(result.messages.at(-1).ok,false);assert.equal(result.urls.length,1);
  result=await scenario([queued()],context=>{context.requestGeneration++;context.currentServerId=6;});
  assert.equal(result.urls.length,1,'Alte Abfrage endet beim Wechsel des Servers');
  assert.ok(result.messages.every(m=>m.ok===null),'Alte Antwort erzeugt keine Erfolgsmeldung im neuen Dialog');
  console.log('OK Schnellverwaltung: Erfolg, Bridge-Fehler, Timeout, HTTP-Fehler, Berechtigung und Serverwechsel');
})().catch(error=>{console.error(error);process.exitCode=1;});
