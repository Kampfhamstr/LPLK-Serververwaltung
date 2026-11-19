"""Command/alias ownership audit per actual server profile, including dynamic routes.

--source checks current sources; --installed checks downloaded assembly metadata.
No addon code is executed. Unknown dynamic registration sites fail the audit.
"""
import argparse, collections, functools, json, re
from pathlib import Path

ROOT=Path(__file__).resolve().parents[1]
ENGINE=set('help addons stats say say_team kick noclip rcon map restart quit exit status exec execifexists changelevel host_workshop_map connect disconnect bind unbind alias sv_password rcon_password'.split())
ADMIN_BLOCKED=ENGINE|{'sw_map'}
KNOWN_DYNAMIC={'LPLK_Admin':{'AdminCommandAliases.cs'},'LPLK_ThirdPerson':{'CameraCommands.cs'},'LPLK_Whitelist':{'AccessCommands.cs'},'LPLK_Surf':{'Plugin.cs'},'LPLK_Zombie':{'HanZombieRiotCommand.cs'},'LPLK_Workshop':{'Plugin.cs'}}
COMPILED_DYNAMIC={'LPLK_Admin':'LPLK_Admin.Plugin::ReconcileAdminCommands()','LPLK_ThirdPerson':'LPLK_ThirdPerson.Plugin::ReconcileCommand()','LPLK_Whitelist':'LPLK_Whitelist.Plugin::ReconcileCommands()','LPLK_Surf':'::<Add>b__1(System.String)','LPLK_Zombie':'LPLK_Zombie.HanZriotCommands::Command()','LPLK_Workshop':'LPLK_Workshop.Plugin::Load(System.Boolean)'}
def read_json(path):return json.loads(path.read_text(encoding='utf-8-sig'))
@functools.lru_cache(maxsize=128)
def source_files(plugin):
    folder=ROOT/'Addons'/plugin/'Rohskripte'
    return {str(p.relative_to(folder)).replace('\\','/'):p.read_text(encoding='utf-8-sig') for p in folder.rglob('*.cs') if not {'obj','bin'}&set(p.relative_to(folder).parts)}
def attrs(text):
    # Preserve string tokens, discard comments, and allow ]/) inside help text.
    text=re.sub(r'"(?:\\.|[^"\\])*"|//[^\n]*|/\*[\s\S]*?\*/',lambda m:m[0] if m[0].startswith('"') else ' ',text)
    for m in re.finditer(r'(?:\[|,)\s*(Command|CommandAlias)\(\s*"([^"\r\n]+)"((?:"(?:\\.|[^"\\])*"|[^"\)])*)\)',text):
        arguments=re.sub(r'"(?:\\.|[^"\\])*"','""',m[3])
        raw=bool(re.search(r'registerRaw\s*:\s*true',arguments) or re.match(r'\s*,\s*true\b',arguments))
        yield {'name':m[2] if raw else 'sw_'+m[2],'kind':'attribute','guarded':False}
def classify(rows):
    owners=collections.defaultdict(list)
    for row in rows:owners[row['name'].casefold()].append(row)
    collisions=[];protected=[]
    for name,entries in owners.items():
        strong=[x for x in entries if not x['guarded']]
        # Case-insensitive names must have one owner; even duplicate attributes
        # within one component can refer to two handlers and are reviewed.
        if name in ENGINE and strong:collisions.append({'name':name,'kind':'engine','owners':sorted({x['plugin'] for x in strong})})
        if len(strong)>1 or len({x['plugin'] for x in entries})>1:
            # A dynamic occupancy guard cannot protect an attribute registered
            # later by another plugin. Reject order-dependent ownership too.
            collisions.append({'name':name,'kind':'duplicate','owners':[x['plugin'] for x in entries]})
        if any(x['guarded'] for x in entries) and (name in ENGINE or len({x['plugin'] for x in entries})>1):
            protected.append({'name':name,'owners':sorted({x['plugin'] for x in entries})})
    return collisions,protected
def fixture_checks():
    def row(p,n,guard=False):return {'plugin':p,'name':n,'guarded':guard}
    assert classify([row('A','help')])[0]
    assert classify([row('A','sw_map'),row('B','sw_map')])[0]
    assert classify([row('A','SW_MAP'),row('B','sw_map')])[0]
    assert classify([row('A','sw_map'),row('B','sw_map',True)])[0]
    assert classify([row('A','same'),row('A','same')])[0]
    assert not classify([row('A','sw_help')])[0]
    assert list(attrs('[Command("help", true)]'))[0]['name']=='help'
    assert list(attrs('[CommandAlias("help",registerRaw:true)]'))[0]['name']=='help'
    assert list(attrs('[Command("help")]'))[0]['name']=='sw_help'
    assert classify([row('Surf','stats')])[0]
    assert not classify([row('Surf','sw_stats'),row('Zombie','sw_zriot_hud')])[0]
    assert list(attrs('[Command("sounds",registerRaw:true,helpText:"sounds [page] (optional)")]'))[0]['name']=='sounds'
    assert len(list(attrs('[Command("a"), CommandAlias("b")]')))==2
    assert not list(attrs('// [Command("fake")]\n/* [Command("fake2")] */'))
    assert not list(attrs('[Command("x",helpText:"registerRaw:true")]'))[0]['name']=='x'
    return 15
def main():
    parser=argparse.ArgumentParser();parser.add_argument('--source',action='store_true');parser.add_argument('--installed',action='store_true');parser.add_argument('--candidate');args=parser.parse_args()
    fixtures=fixture_checks();policy=read_json(ROOT/'config/command-registration.json')
    inventory_path=ROOT/'state/command-fleet-inventory.json'
    if inventory_path.exists():inventory=json.loads(read_json(inventory_path)['output'])
    elif args.installed:raise SystemExit('Fresh installed assembly inventory required')
    else:inventory={'servers':[{'id':p['server'],'dlls':[{'file':'plugins/'+n+'/'+n+'.dll','sha256':'source'} for n in p['components']]} for p in policy['profiles']]}
    metadata={x['sha256']:x for x in read_json(ROOT/'state/command-fleet-assembly-audit.json')} if args.installed else {}
    config_path=ROOT/'state/command-fleet-config-names.json'
    configs=read_json(config_path) if config_path.exists() else policy['aliases'];results=[]
    known_candidate=any(f['file'].split('/')[1]==args.candidate for s in inventory['servers'] for f in s['dlls'])
    defaults=(ROOT/'Addons/LPLK_Admin/Rohskripte/AdminCommandAliases.cs').read_text(encoding='utf-8-sig').split('// These belong')[0]
    groups={m[1]:re.findall(r'"([a-z0-9_.-]+)"',m[2]) for m in re.finditer(r'\["([A-Za-z]+)"\]\s*=\s*\[([^\]]+)\]',defaults)}
    for server in inventory['servers']:
        installed={f['file'].split('/')[1]:f for f in server['dlls'] if f['file'].split('/')[2]==f['file'].split('/')[1]+'.dll'}
        if args.candidate and not known_candidate and args.candidate not in installed:
            installed[args.candidate]={'file':'plugins/'+args.candidate+'/'+args.candidate+'.dll','sha256':'source'}
        rows=[];unknown=[];versions={};guarded_skips=[]
        for plugin,file in installed.items():
            folder=ROOT/'Addons'/plugin/'Rohskripte'
            if not folder.exists():unknown.append({'plugin':plugin,'reason':'No reviewed source'});continue
            sources=source_files(plugin)
            if args.installed:
                assembly=metadata.get(file['sha256'])
                if not assembly:unknown.append({'plugin':plugin,'reason':'Missing compiled metadata'});continue
                versions[plugin]=assembly['metadata'].get('Version',assembly['version'])
                commands=[{'name':a['effective'],'kind':'attribute','guarded':False} for a in assembly['attributes']]
                dynamic=[d for d in assembly['dynamic'] if 'RoundFileStore::BeginLoad' not in d['method']]
                if any(plugin not in COMPILED_DYNAMIC or COMPILED_DYNAMIC[plugin] not in d['method'] or d['count']!=1 for d in dynamic):
                    unknown.append({'plugin':plugin,'reason':'Unknown compiled dynamic registrar'})
            else:
                commands=[a for text in sources.values() for a in attrs(text)]
                versions[plugin]='current-source'
            own={a['name'] for a in commands}
            for name,text in sources.items():
                sites=re.findall(r'\bRegisterCommand(?:Alias)?\s*\(',text)
                if sites and (name not in KNOWN_DYNAMIC.get(plugin,set()) or len(sites)!=1):
                    unknown.append({'plugin':plugin,'file':name,'reason':'Unmodeled dynamic registration'})
            settings={k:v for c in configs if c['server']==server['id'] and '/'+plugin+'/' in c['file'] for k,v in c['names'].items()}
            dynamic=[]
            def add(name,guard=True):
                if name not in own:dynamic.append({'name':name,'kind':'dynamic','guarded':guard})
            if plugin=='LPLK_Admin':
                names={n for values in groups.values() for n in values}
                for key,value in settings.items():
                    if key.startswith('Commands.'):names.update(n.removeprefix('sw_').lower() for n in value)
                cold_safe=not args.installed or versions[plugin]=='0.11.10'
                for n in sorted(names):
                    for exact in (n,'sw_'+n):
                        if cold_safe and exact in ADMIN_BLOCKED:guarded_skips.append({'plugin':plugin,'name':exact});continue
                        # Native occupancy guards prevent overwrite, but the old
                        # configured sw_map can precede Maps during cold load.
                        add(exact,False if exact=='sw_map' else True)
            elif plugin=='LPLK_Surf':
                names={n for m in re.finditer(r'\bAdd\(\[([^\]]+)\]',sources['Plugin.cs']) for n in re.findall(r'"([a-z0-9_]+)"',m[1])}
                for n in sorted(names):
                    add('sw_'+n)
                    if n not in ENGINE:add(n)
                    else:guarded_skips.append({'plugin':plugin,'name':n})
            elif plugin=='LPLK_Zombie':
                names=re.findall(r'\("([a-z]+)",\w+,(?:true|false)\)',sources['HanZombieRiotCommand.cs'])
                if len(names)!=7:unknown.append({'plugin':plugin,'reason':'Zombie command table changed'})
                for n in names:
                    for exact in ('zriot_'+n,'sw_zriot_'+n):add(exact,False)
            elif plugin=='LPLK_Workshop':
                for n in ('sw_addons','sw_workshop','sw_workshob'):add(n)
            elif plugin=='LPLK_ThirdPerson':
                n=settings.get('CustomTPCommand','tp').removeprefix('sw_').lower()
                if n not in ('','tp','thirdperson'):add('sw_'+n)
            elif plugin=='LPLK_Whitelist':
                for suffix,default,exempt in [('AddCommand','wl',('wl','lplk_wl_add')),('RemoveCommand','uwl',('uwl','lplk_wl_remove'))]:
                    values=[v for k,v in settings.items() if k.endswith(suffix)]
                    n=(values[-1] if values else default).removeprefix('sw_').lower()
                    if n not in exempt:add('sw_'+n)
            # Shared diagnostic command is namespaced by the actual assembly.
            if any('RoundFileStore.BeginLoad(' in text for text in sources.values()):add('sw_lplk_io_'+plugin.lower(),False)
            rows.extend(dict(c,plugin=plugin) for c in commands+dynamic)
        collisions,protected=classify(rows)
        results.append({'server':server['id'],'components':len(installed),'registrations':len(rows),'versions':versions,'collisions':collisions,'protectedOverlaps':protected,'reservedSkipped':guarded_skips,'unreviewed':unknown,'commands':rows})
    report={'scope':'installed-assembly-and-config' if args.installed else 'current-source-and-server-profiles','fixtureChecks':fixtures,'servers':results,'passed':all(not s['collisions'] and not s['unreviewed'] for s in results)}
    output=ROOT/'state'/('command-fleet-installed-audit.json' if args.installed else 'command-fleet-source-audit.json')
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
    print(json.dumps({'passed':report['passed'],'fixtureChecks':fixtures,'servers':[{k:s[k] for k in ('server','components','registrations','collisions','unreviewed')} for s in results]},ensure_ascii=False))
    if not report['passed']:raise SystemExit(1)
if __name__=='__main__':main()
