"""Fetch pinned core dependencies and apply tracked patches. Requires Python 3.12+.
Does not install tools, compile, change the TV package or touch TizenPS.
"""
import configparser, hashlib, json, os, re, shutil, subprocess, tarfile, tempfile, urllib.request
from pathlib import Path
root=Path(__file__).resolve().parents[1]
source=root/'vendor/ppsspp';cache=root/'build/core-downloads';cache.mkdir(parents=True,exist_ok=True)
meta=json.loads((root/'docs/ppsspp-source.json').read_text())
def download(url,path,digest):
    if not path.exists():
        partial=path.with_suffix(path.suffix+'.partial')
        with urllib.request.urlopen(url,timeout=60) as r,partial.open('wb') as f:shutil.copyfileobj(r,f)
        if hashlib.sha256(partial.read_bytes()).hexdigest()!=digest:raise RuntimeError('Download hash mismatch: '+url)
        partial.replace(path)
    if hashlib.sha256(path.read_bytes()).hexdigest()!=digest:raise RuntimeError('Cached hash mismatch: '+str(path))
def extract(archive,dest):
    dest.mkdir(parents=True,exist_ok=True)
    with tarfile.open(archive) as t:
        for member in t:
            parts=Path(member.name).parts[1:]
            if not parts:continue
            member.name=str(Path(*parts));t.extract(member,dest,filter='data')
archive=root/'build/ppsspp-a7d80b6.tar.gz'
download(meta['url'],archive,meta['archive_sha256'])
if not (source/'CMakeLists.txt').exists():extract(archive,source)
cfg=configparser.ConfigParser();cfg.read(source/'.gitmodules')
urls={cfg[s]['path']:cfg[s]['url'].removesuffix('.git').removeprefix('https://github.com/') for s in cfg.sections()}
for dep in json.loads((root/'docs/ppsspp-dependencies.json').read_text()):
    path=cache/(dep['revision']+'.tar.gz')
    download('https://codeload.github.com/'+urls[dep['path']]+'/tar.gz/'+dep['revision'],path,dep['archive_sha256'])
    dest=source/dep['path'];marker=dest/'.tizenpsp-revision'
    if not marker.exists():extract(path,dest);marker.write_text(dep['revision'])
    if marker.read_text()!=dep['revision']:raise RuntimeError('Dependency revision mismatch')
patches=sorted((root/'native/ppsspp-patches').glob('*.patch'))
command=['git','apply','--ignore-space-change','--directory=vendor/ppsspp']
def complete_patch_stack_present():
    # Later conditional patches change earlier context. Reverse the whole stack
    # in a small scratch tree, leaving the user's actual sources untouched.
    scratch=Path(tempfile.mkdtemp(prefix='patch-check-',dir=root/'build')).resolve()
    assert scratch.is_relative_to((root/'build').resolve())
    try:
        names=set()
        for patch in patches:
            names.update(re.findall(r'^--- a/(.+)$',patch.read_text(),re.M))
        for name in names:
            target=scratch/'vendor/ppsspp'/name
            assert target.resolve().is_relative_to(scratch)
            target.parent.mkdir(parents=True,exist_ok=True)
            if not (source/name).is_file():return False
            target.write_bytes((source/name).read_bytes().replace(b'\r\n',b'\n'))
        patch_env=dict(os.environ,GIT_CEILING_DIRECTORIES=str(scratch.parent))
        patch_env.pop('GIT_DIR',None);patch_env.pop('GIT_WORK_TREE',None)
        scratch_command=['git','-c','core.autocrlf=false']+command[1:]
        for patch in reversed(patches):
            result=subprocess.run(scratch_command+['--reverse',str(patch)],cwd=scratch,env=patch_env,capture_output=True)
            if result.returncode!=0:
                print('Patch-stack reverse check:',patch.name,result.stderr.decode(errors='replace').strip());return False
        # Self-consistent reverse/reapply is insufficient: a patch accidentally
        # based on an earlier local edit can otherwise silently retain that edit.
        # Require the reversed original files to match the pinned source archive.
        with tarfile.open(archive) as original_archive:
            original_members={m.name.split('/',1)[1]:m for m in original_archive if '/' in m.name and m.isfile()}
            for name in names:
                if name not in original_members:
                    print('Patched original missing from pinned archive:',name);return False
                original=original_archive.extractfile(original_members[name]).read().replace(b'\r\n',b'\n')
                if (scratch/'vendor/ppsspp'/name).read_bytes().replace(b'\r\n',b'\n')!=original:
                    print('Reversed patch baseline differs from pinned source:',name);return False
        # Reapply and require exact content after normalizing scratch line endings.
        for patch in patches:
            result=subprocess.run(scratch_command+[str(patch)],cwd=scratch,env=patch_env,capture_output=True)
            if result.returncode!=0:
                print('Patch-stack forward check:',patch.name,result.stderr.decode(errors='replace').strip());return False
        for name in names:
            if (scratch/'vendor/ppsspp'/name).read_bytes().replace(b'\r\n',b'\n')!=(source/name).read_bytes().replace(b'\r\n',b'\n'):
                print('Patch-stack reconstruction differs:',name);return False
        return True
    finally:
        assert scratch.is_relative_to((root/'build').resolve())
        shutil.rmtree(scratch)
if not complete_patch_stack_present():
    for patch in patches:
        if subprocess.run(command+['--reverse','--check',str(patch)],cwd=root,capture_output=True).returncode==0:continue
        subprocess.run(command+['--check',str(patch)],cwd=root,check=True)
        names=re.findall(r'^--- a/(.+)$',patch.read_text(),re.M)
        endings={name:b'\r\n' if b'\r\n' in (source/name).read_bytes() else b'\n' for name in names}
        subprocess.run(command+[str(patch)],cwd=root,check=True)
        for name,ending in endings.items():
            path=source/name;before=path.read_bytes();after=before.replace(b'\r\n',b'\n').replace(b'\n',ending)
            if after!=before:path.write_bytes(after)
shutil.copytree(root/'native/core-build/headers',root/'build/core-headers',dirs_exist_ok=True)
shutil.copytree(root/'native/core-build/headers/openxr',source/'ext/OpenXR-SDK/include/openxr',dirs_exist_ok=True)
print('Pinned core, required dependencies, patches and compile headers ready.')
