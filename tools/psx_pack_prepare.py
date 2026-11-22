"""Prepare a local PS1 pack for TizenPSX. Never downloads games or executes pack files."""
from pathlib import Path, PureWindowsPath
import argparse, csv, hashlib, importlib.util, json, os, re, shutil, subprocess, tempfile, threading, uuid

ARCHIVES={'.7z','.rar','.zip'}
DISCS={'.bin','.cue','.iso','.img','.ccd','.sub','.mdf','.mds','.sbi'}
ALLOWED=DISCS|{'.jpg','.jpeg','.png','.txt','.nfo'}
SKIP=('emulator','plugin','bios','error fix')
VERSION=1

def cover_library():
    base=Path(__file__).resolve().parent
    path=base/'cover_import.py'
    if not path.exists():path=base.parent/'retro-tizen/tools/cover_import.py'
    spec=importlib.util.spec_from_file_location('psx_art',path)
    module=importlib.util.module_from_spec(spec);spec.loader.exec_module(module)
    module.SYSTEMS['psx']='Sony - PlayStation'
    return module

def member_path(name):
    p=PureWindowsPath(name)
    if p.is_absolute() or p.drive or p.root or not p.parts:raise ValueError('Absolute archive path: '+name)
    for part in p.parts:
        if part in ('.','..') or ':' in part or part.endswith((' ','.')) or any(ord(c)<32 for c in part):raise ValueError('Unsafe archive path: '+name)
        if re.fullmatch(r'(?i)(con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\..*)?',part):raise ValueError('Reserved filename: '+name)
    return Path(*p.parts)

def linked(path):
    return path.is_symlink() or bool(getattr(path.lstat(),'st_file_attributes',0)&0x400)

def manifest(archive,seven):
    result=subprocess.run([str(seven),'l','-slt','-sccUTF-8','-p','--',str(archive)],stdin=subprocess.DEVNULL,capture_output=True,encoding='utf-8',errors='replace',timeout=60)
    if result.returncode:raise ValueError('Cannot list archive: '+result.stderr.strip())
    if '----------' not in result.stdout:raise ValueError('Missing archive manifest')
    entries=[];seen=set()
    for block in re.split(r'\r?\n\s*\r?\n',result.stdout.split('----------',1)[1].strip()):
        f=dict(line.split(' = ',1) for line in block.splitlines() if ' = ' in line)
        if not f.get('Path'):continue
        p=member_path(f['Path']);directory=f.get('Folder')=='+' or f.get('Attributes','').startswith('D')
        if any(f.get(k)=='+' for k in ('Encrypted','Alternate Stream','Anti')) or any(f.get(k) not in (None,'','-') for k in ('Symbolic Link','Hard Link')) or 'l' in f.get('Attributes','').split(' ')[-1]:raise ValueError('Encrypted or linked archive member: '+str(p))
        if directory:continue
        if p.suffix.lower() not in ALLOWED:raise ValueError('Non-disc payload needs review: '+str(p))
        key=str(p).casefold()
        if key in seen:raise ValueError('Duplicate archive filename: '+str(p))
        seen.add(key);size=int(f.get('Size','0'))
        if size<0 or size>16*1024**3:raise ValueError('Unexpected member size')
        entries.append({'path':str(p),'size':size})
    if not any(Path(e['path']).suffix.lower() in DISCS for e in entries):raise ValueError('Archive has no PS1 disc files')
    return entries

def candidates(source):
    result=[]
    for p in sorted(source.iterdir(),key=lambda x:x.name.casefold()):
        if linked(p) or any(word in p.name.casefold() for word in SKIP):continue
        if p.is_file() and p.suffix.lower() in ARCHIVES:result.append(p)
        elif p.is_dir() and any(f.is_file() and f.suffix.lower() in DISCS for f in p.rglob('*')):result.append(p)
    return result

def directory_manifest(folder):
    entries=[]
    for p in sorted(folder.rglob('*')):
        if linked(p):raise ValueError('Linked source file/folder: '+str(p))
        if p.is_file():
            if p.suffix.lower() not in ALLOWED:raise ValueError('Non-disc file needs review: '+str(p))
            entries.append({'path':str(p.relative_to(folder)),'size':p.stat().st_size})
    return entries

def title_for(path):return re.sub(r'^PSX\s*-\s*','',path.stem if path.is_file() else path.name,flags=re.I).strip()

def cover_title(title):
    title=re.sub(r'\[(U|E|J)\]',lambda m:'('+{'U':'USA','E':'Europe','J':'Japan'}[m[1]]+')',title)
    title=re.sub(r'\[(?:Disc[^]]*)\]|\((?:Both Discs|Allies Disc|Soviets Disc)\)','',title,flags=re.I)
    title=re.sub(r'\bspiderman\b','Spider-Man',title,flags=re.I)
    title=re.sub(r'\bDiciples\b','Disciples',title,flags=re.I)
    return title.strip()

def contained(root,path):
    try:path.resolve().relative_to(root.resolve());return True
    except ValueError:return False

def actual_path(root,relative):
    # Resolve case exactly before copying references to a case-sensitive TV filesystem.
    here=root
    for part in PureWindowsPath(relative).parts:
        if part=='..':here=here.parent
        elif part!='.':
            matches=[p for p in here.iterdir() if p.name.casefold()==part.casefold()] if here.is_dir() else []
            if len(matches)!=1:return None
            here=matches[0]
    return here

def prepare_discs(stage):
    notes=[]
    # The existing core finds .mds by replacing the selected image's extension.
    # Keep image bytes and MDS metadata; .bin makes it discoverable by the current UI.
    for mdf in list(stage.rglob('*')):
        if mdf.suffix.lower()!='.mdf':continue
        mds=actual_path(mdf.parent,mdf.stem+'.mds')
        if not mds or not mds.read_bytes().startswith(b'MEDIA DESCRIPTOR'):raise ValueError('MDF missing valid MDS: '+mdf.name)
        target=mdf.with_suffix('.bin')
        if target.exists():raise ValueError('MDF/BIN name collision')
        mdf.rename(target);notes.append('Copied MDF exposed as BIN; unchanged disc bytes and MDS retained: '+target.name)
    # Companion extensions are looked up in lowercase by the core.
    for p in list(stage.rglob('*')):
        if p.suffix.lower() in ('.ccd','.sub','.mds','.sbi') and p.suffix!=p.suffix.lower():
            p.rename(p.with_suffix(p.suffix.lower()))
    referenced=set();launchers=[]
    for cue in sorted(stage.rglob('*')):
        if cue.suffix.lower()!='.cue':continue
        if cue.stat().st_size>262144:raise ValueError('Oversized CUE')
        data=cue.read_bytes()
        try:text=data.decode('utf-8-sig')
        except UnicodeDecodeError:text=data.decode('cp1252')
        count=0
        def repair(m):
            nonlocal count
            name=m[1] or m[2];p=PureWindowsPath(name)
            target=None if p.drive or p.is_absolute() else actual_path(cue.parent,name)
            if target is None:
                matches=[x for x in stage.rglob('*') if x.is_file() and x.name.casefold()==p.name.casefold()]
                if len(matches)==1:target=matches[0]
            if target is None:
                # Common pack defect: the CUE was renamed with its image but the
                # FILE line still uses the old serial number. Only accept the
                # unambiguous image sharing this CUE's own basename.
                same=[x for x in cue.parent.iterdir() if x.is_file() and x.stem.casefold()==cue.stem.casefold() and x.suffix.lower() in ('.bin','.img','.iso')]
                if len(same)==1 and len(re.findall(r'(?im)^\s*FILE\s+',text))==1:
                    target=same[0];notes.append('Repaired stale CUE filename '+name+' -> '+target.name+'; track/index data preserved')
            if target is None or not target.is_file() or not contained(stage,target) or target.stat().st_size==0:raise ValueError('Missing/unsafe CUE track: '+name)
            referenced.add(target.resolve());count+=1
            rel=os.path.relpath(target,cue.parent).replace('\\','/')
            return 'FILE "'+rel+'" '+m[3]
        repaired=re.sub(r'(?im)^\s*FILE\s+(?:"([^"]+)"|(\S+))\s+(\S+)',repair,text)
        if not count or not re.search(r'(?im)^\s*TRACK\s+\d+\s+',repaired):raise ValueError('CUE has no tracks: '+cue.name)
        cue.write_text(repaired,encoding='utf-8');launchers.append(cue)
    for p in sorted(stage.rglob('*')):
        if p.is_file() and p.suffix.lower() in ('.bin','.iso','.img') and p.resolve() not in referenced:
            if p.stat().st_size<2352*16:raise ValueError('Unexpectedly small disc image: '+p.name)
            launchers.append(p)
    if not launchers:raise ValueError('No launchable discs')
    for p in launchers:
        for ext in ('.ccd','.sub','.mds','.sbi'):
            companion=actual_path(p.parent,p.stem+ext)
            if companion and companion.name!=p.stem+ext:companion.rename(p.with_suffix(ext))
    return launchers,notes

def fingerprint(path,entries):
    if path.is_file():files=[(path.name,path.stat().st_size,path.stat().st_mtime_ns)]
    else:files=[(e['path'],e['size'],(path/e['path']).stat().st_mtime_ns) for e in entries]
    return hashlib.sha256(json.dumps([str(path.resolve()),files],sort_keys=True).encode()).hexdigest()

def write_json(path,value):
    temporary=path.with_name(path.name+'.tmp');temporary.write_text(json.dumps(value,indent=2),encoding='utf-8');os.replace(temporary,path)

def covers_for(folder,launchers,title,art,names,cache,notify):
    parents=sorted({p.parent for p in launchers});existing=[]
    for parent in parents:
        pictures=[p for p in parent.iterdir() if p.is_file() and p.suffix.lower() in ('.jpg','.jpeg','.png')]
        preferred=[p for p in pictures if p.stem.casefold()=='cover']
        front=[p for p in pictures if 'front cover' in p.stem.casefold()]
        if preferred:existing.append(parent)
        elif len(front)==1:
            shutil.copy2(front[0],parent/('cover'+front[0].suffix.lower()));existing.append(parent)
    if len(existing)==len(parents):return {'status':'existing','source':''}
    aliases_path=Path(__file__).with_name('psx-cover-aliases.json')
    aliases=json.loads(aliases_path.read_text(encoding='utf-8')) if aliases_path.exists() else {}
    chosen=next((v for k,v in aliases.items() if k.casefold()==title.casefold()),None)
    alias_note=chosen.get('note','') if isinstance(chosen,dict) else ''
    if isinstance(chosen,dict):chosen=chosen['cover']
    if chosen:
        if chosen not in names:raise ValueError('Reviewed cover is absent from current catalogue: '+chosen)
        cover,reason=chosen,'reviewed catalogue title'
    else:
        if not hasattr(art,'_psx_titles'):
            art._psx_titles={}
            for name in names:art._psx_titles.setdefault(art.normalized(Path(name).stem),[]).append(name)
        clean=cover_title(title)
        cover,reason=art.match(clean,art._psx_titles.get(art.normalized(clean),[]))
    if not cover:return {'status':'review','reason':reason,'title':cover_title(title)}
    if cover not in cache:
        data=art.fetch(art.art_url('psx',cover));art.validate_png(data);cache[cover]=data
    for parent in parents:
        if parent in existing:continue
        target=parent/'cover.png'
        with target.open('xb') as f:f.write(cache[cover])
    notify('  Cover: '+cover)
    return {'status':'downloaded','source':art.art_url('psx',cover),'match':cover,'reason':alias_note or reason}

def run(source,destination,seven,apply=False,download=True,notify=print):
    source=source.resolve();destination=destination.resolve()
    if not source.is_dir():raise ValueError('Source folder not found')
    if contained(source,destination) or contained(destination,source):raise ValueError('Source and destination must be separate folders')
    if destination.exists() and linked(destination):raise ValueError('Destination cannot be a link')
    inputs=candidates(source);plans=[];errors=[];used=set()
    for p in inputs:
        try:
            entries=manifest(p,seven) if p.is_file() else directory_manifest(p)
            name=title_for(p);key=name.casefold()
            if key in used:name+=' - '+p.suffix.lstrip('.');key=name.casefold()
            if key in used:raise ValueError('Duplicate output folder')
            used.add(key);plans.append((p,name,entries,fingerprint(p,entries)))
        except Exception as e:errors.append({'source':str(p),'status':'review','error':str(e)})
    total=sum(e['size'] for _,_,entries,_ in plans for e in entries)
    notify(f'{len(plans)} games/archives planned; {total/1e9:.1f} GB uncompressed; {len(errors)} need review.')
    if not apply:return {'planned':len(plans),'bytes':total,'review':errors}
    destination.mkdir(parents=True,exist_ok=True)
    remaining=sum(sum(e['size'] for e in entries) for _,name,entries,_ in plans if not (destination/name).exists())
    if shutil.disk_usage(destination).free<remaining+1024**3:raise ValueError('Insufficient free space (includes 1 GB reserve)')
    art=None;names=[];cache={}
    if download:
        try:art=cover_library();names=art.index('psx')
        except Exception as e:notify('Artwork index unavailable; preparing games anyway: '+str(e))
    previous={}
    report_path=destination/'TizenPSX-preparation-report.json'
    if report_path.exists():
        try:previous={r.get('folder'):r.get('artwork',{}) for r in json.loads(report_path.read_text(encoding='utf-8'))}
        except (ValueError,OSError):pass
    rows=list(errors)
    for number,(p,name,entries,identity) in enumerate(plans,1):
        target=destination/name;stage=None
        notify(f'[{number}/{len(plans)}] {name}')
        try:
            marker=target/'.tizenpsx-prepared.json'
            if target.exists():
                if linked(target) or not marker.is_file():raise ValueError('Existing unmanaged folder preserved; choose another destination')
                old=json.loads(marker.read_text(encoding='utf-8'))
                if old.get('source_fingerprint')!=identity:raise ValueError('Source changed; existing prepared folder preserved')
                launchers=[target/member_path(x) for x in old['launchers']]
                if not all(x.is_file() for x in launchers):raise ValueError('Prepared files missing; folder preserved for review')
                for entry in old.get('files',[]):
                    check=target/member_path(entry['path'])
                    if not check.is_file() or linked(check) or check.stat().st_size!=entry['size']:raise ValueError('Prepared file changed/missing: '+entry['path'])
                notes=old.get('notes',[]);notify('  Already prepared; checking artwork.')
            else:
                stage=destination/('.prepare-'+uuid.uuid4().hex);stage.mkdir()
                if p.is_file():
                    result=subprocess.run([str(seven),'x','-y','-bd','-bso0','-bsp0','-p','-o'+str(stage),'--',str(p)],stdin=subprocess.DEVNULL,capture_output=True,text=True,errors='replace')
                    if result.returncode:raise ValueError('Archive extraction/CRC failed: '+result.stderr[-1000:])
                else:
                    for e in entries:
                        output=stage/member_path(e['path']);output.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p/e['path'],output)
                for e in entries:
                    output=stage/member_path(e['path'])
                    if linked(output) or not output.is_file() or output.stat().st_size!=e['size']:raise ValueError('Extracted size/type mismatch: '+e['path'])
                launchers,notes=prepare_discs(stage)
                relative=[str(x.relative_to(stage)) for x in launchers]
                prepared_files=[{'path':str(x.relative_to(stage)),'size':x.stat().st_size} for x in stage.rglob('*') if x.is_file()]
                write_json(stage/'.tizenpsx-prepared.json',{'tool_version':VERSION,'source':str(p),'source_fingerprint':identity,'launchers':relative,'notes':notes,'files':prepared_files})
                stage.rename(target);stage=None;launchers=[target/x for x in relative]
            artwork={'status':'not requested'}
            if art:
                try:artwork=covers_for(target,launchers,name,art,names,cache,notify)
                except Exception as e:artwork={'status':'error','reason':str(e)}
            prior=previous.get(str(target),{})
            if artwork.get('status')=='existing' and prior.get('source'):artwork=dict(prior,status='existing')
            marker_data=json.loads((target/'.tizenpsx-prepared.json').read_text(encoding='utf-8'));marker_data['artwork']=artwork
            write_json(target/'.tizenpsx-prepared.json',marker_data)
            rows.append({'source':str(p),'folder':str(target),'title':name,'status':'prepared','launchers':[str(x) for x in launchers],'notes':notes,'artwork':artwork})
        except Exception as e:
            rows.append({'source':str(p),'status':'review','error':str(e)});notify('  REVIEW: '+str(e))
        finally:
            if stage is not None and stage.parent.resolve()==destination and stage.name.startswith('.prepare-') and not linked(stage):shutil.rmtree(stage)
        write_json(destination/'TizenPSX-preparation-report.json',rows)
    with (destination/'TizenPSX-preparation-report.csv').open('w',encoding='utf-8-sig',newline='') as f:
        writer=csv.writer(f);writer.writerow(['Source','Status','Folder','Cover','Details'])
        for r in rows:writer.writerow([r['source'],r['status'],r.get('folder',''),r.get('artwork',{}).get('status',''),r.get('error') or r.get('artwork',{}).get('reason','')])
    notify(f"Finished: {sum(r['status']=='prepared' for r in rows)} prepared; {sum(r['status']=='review' for r in rows)} require review.")
    return rows

def gui():
    import tkinter as tk
    from tkinter import ttk,filedialog,messagebox
    import queue
    app=tk.Tk();app.title('TizenPSX · Prepare games & covers');app.geometry('850x560');events=queue.Queue()
    source=tk.StringVar();dest=tk.StringVar(value=r'D:\PSX')
    ttk.Label(app,text='Prepare your PS1 collection',font=('Segoe UI',20)).pack(anchor='w',padx=18,pady=16)
    ttk.Label(app,text='Extract game archives, keep disc tracks together, and match covers. Originals stay unchanged.').pack(anchor='w',padx=18)
    for title,var in [('Downloaded pack',source),('Prepared PSX folder',dest)]:
        row=ttk.Frame(app);row.pack(fill='x',padx=18,pady=8);ttk.Label(row,text=title,width=22).pack(side='left');ttk.Entry(row,textvariable=var).pack(side='left',fill='x',expand=True)
        def browse(v=var):
            selected=filedialog.askdirectory()
            if selected:v.set(selected)
        ttk.Button(row,text='Browse',command=browse).pack(side='left')
    log=tk.Text(app,height=18,wrap='word');log.pack(fill='both',expand=True,padx=18,pady=10)
    def start():
        if not source.get():messagebox.showerror('Choose a pack','Select the downloaded pack folder.');return
        button.config(state='disabled');src=source.get();dst=dest.get()
        def worker():
            try:run(Path(src),Path(dst),find_seven(),True,notify=lambda t:events.put(t))
            except Exception as e:events.put('ERROR: '+str(e))
            finally:events.put(None)
        threading.Thread(target=worker,daemon=True).start()
    button=ttk.Button(app,text='Prepare games & download covers',command=start);button.pack(pady=10)
    def tick():
        while not events.empty():
            item=events.get()
            if item is None:button.config(state='normal')
            else:log.insert('end',item+'\n');log.see('end')
        app.after(150,tick)
    def close():
        if str(button.cget('state'))=='disabled':messagebox.showinfo('Preparation running','Please leave this window open until preparation finishes.');return
        app.destroy()
    app.protocol('WM_DELETE_WINDOW',close);app.after(150,tick);app.mainloop()

def find_seven():
    path=Path(os.environ.get('ProgramFiles',r'C:\Program Files'))/'7-Zip/7z.exe'
    if not path.is_file():raise ValueError('Install 7-Zip for Windows first (7-zip.org).')
    return path

if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--source',type=Path);parser.add_argument('--destination',type=Path);parser.add_argument('--apply',action='store_true');parser.add_argument('--no-covers',action='store_true');args=parser.parse_args()
    if args.source:
        if not args.destination:parser.error('--destination is required')
        run(args.source,args.destination,find_seven(),args.apply,not args.no_covers,notify=lambda t:print(t,flush=True))
    else:gui()
