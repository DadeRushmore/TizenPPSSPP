"""Check the standalone identity and exact native/resource payload in the TPK."""
import hashlib, json, struct, xml.etree.ElementTree as ET, zipfile
from pathlib import Path

root=Path(__file__).resolve().parents[1]
version=ET.parse(root/'app/tizen-manifest.xml').getroot().attrib['version']
package=root/f'app/bin/Release/tizen50/org.roelof.tizenpsp-{version}.tpk'
ns={'t':'http://tizen.org/ns/packages'}
with zipfile.ZipFile(package) as z:
    names=set(z.namelist())
    manifest=ET.fromstring(z.read('tizen-manifest.xml'))
    assert manifest.attrib['package']=='org.roelof.tizenpsp'
    assert manifest.attrib['version']==version
    assert manifest.attrib['api-version']=='5.0'
    app=manifest.find('t:ui-application',ns)
    assert app.attrib['appid']=='org.roelof.tizenpsp'
    assert app.attrib['exec']=='TizenPSP.dll'
    assert app.find('t:label',ns).text=='TizenPPSSPP'
    assert manifest.find('t:profile',ns).attrib['name']=='tv'
    assert [p.text for p in manifest.findall('t:privileges/t:privilege',ns)]==['http://tizen.org/privilege/externalstorage','http://tizen.org/privilege/internet']
    native=z.read('res/libtizenpsp_triangle.so')
    assert native==(root/'app/res/libtizenpsp_triangle.so').read_bytes()
    digest=hashlib.sha256(native).hexdigest()
    assert digest==z.read('res/triangle.sha256').decode().strip()
    assert digest==json.loads((root/'docs/milestone2-validation.json').read_text())['native_sha256']
    assert native[:7]==b'\x7fELF\x01\x01\x01'
    assert struct.unpack_from('<HH',native,16)==(3,40)
    assert struct.unpack_from('<I',native,36)[0]&0x400==0
    baseline=z.read('res/libtizenpsp_triangle_v010.so')
    baseline_hash=hashlib.sha256(baseline).hexdigest()
    assert baseline_hash==json.loads((root/'docs/milestone1-baseline.json').read_text())['native_sha256']
    assert baseline_hash==z.read('res/triangle-v010.sha256').decode().strip()
    glview=z.read('res/libtizenpsp_glview.so')
    glview_hash=hashlib.sha256(glview).hexdigest()
    assert glview==(root/'app/res/libtizenpsp_glview.so').read_bytes()
    assert glview_hash==z.read('res/glview.sha256').decode().strip()
    assert glview[:7]==b'\x7fELF\x01\x01\x01'
    assert struct.unpack_from('<HH',glview,16)==(3,40)
    assert struct.unpack_from('<I',glview,36)[0]&0x400==0
    assert b'glReadPixels' not in glview and b'eglCreatePbufferSurface' not in glview
    assert b'tizenpsp_cpp_probe' in glview and b'CPP_STARTUP_OK' in glview
    bridge=z.read('res/libtizenpsp_corebridge.so')
    assert bridge==(root/'app/res/libtizenpsp_corebridge.so').read_bytes()
    assert hashlib.sha256(bridge).hexdigest()==z.read('res/corebridge.sha256').decode().strip()
    assert b'glReadPixels' not in bridge and b'eglSwapBuffers' not in bridge
    assert all(symbol in bridge for symbol in [b'psp_bridge_set_host_services',b'psp_bridge_sample_rate',b'psp_bridge_coalesced'])
    imports=z.read('res/core-runtime-imports.tsv')
    assert imports==(root/'app/res/core-runtime-imports.tsv').read_bytes()
    assert hashlib.sha256(imports).hexdigest()==z.read('res/core-runtime-imports.sha256').decode().strip()
    assert b'__cxa_thread_atexit_impl' in imports and b'GLIBC_' in imports
    core=z.read('res/ppsspp_libretro-arm32.so')
    core_hash=hashlib.sha256(core).hexdigest()
    assert core_hash=='43af35a686c48473549aa6d76f49831c41b317be9d6e5cb835577c20da018bd8'
    assert core_hash==z.read('res/core.sha256').decode().strip()
    assert core==(root/'app/res/ppsspp_libretro-arm32.so').read_bytes()
    jit=z.read('res/ppsspp_libretro-arm32-jit.so')
    jit_hash=hashlib.sha256(jit).hexdigest()
    assert jit_hash==z.read('res/core-jit.sha256').decode().strip()
    assert jit_hash==json.loads((root/'docs/ppsspp-arm-jit-build-result.json').read_text())['sha256']
    assert 'ExpectedJitHash="'+jit_hash+'"' in (root/'app/NativeJitExperiment.cs').read_text()
    assert jit==(root/'app/res/ppsspp_libretro-arm32-jit.so').read_bytes()
    jit_imports=z.read('res/core-jit-runtime-imports.tsv')
    assert jit_imports==(root/'app/res/core-jit-runtime-imports.tsv').read_bytes()
    assert hashlib.sha256(jit_imports).hexdigest()==z.read('res/core-jit-runtime-imports.sha256').decode().strip()
    assert b'tizenpsp_jit_snapshot' in jit and b'tizenpsp_jit_init' in jit
    assert b'tizenpsp_set_command_queue' in jit and b'tizenpsp_command_queue_status' in jit
    assert b'tizenpsp_set_vertex_binding' in jit and b'tizenpsp_vertex_binding_status' in jit
    assert not any(n.endswith(('tizenpsp-live.txt','live-enabled.txt')) for n in names), 'Private live configuration in package'
    assert b'ffmpeg=1' in jit
    assert z.read('res/core-licenses/LICENSE-FFmpeg-LGPLv2.1.txt')==(root/'vendor/ppsspp/ext/ffmpeg/COPYING.LGPLv2.1').read_bytes()
    assert z.read('res/core-licenses/FFmpeg-LICENSE.md')==(root/'vendor/ppsspp/ext/ffmpeg/LICENSE.md').read_bytes()
    assert {'res/PPSSPP/compat.ini','res/core-licenses/LICENSE-LLVM.txt','res/core-licenses/LICENSE-Zig.txt'}<=names
    atlas=z.read('res/PPSSPP/ppge_atlas.zim')
    assert atlas==(root/'vendor/ppsspp/assets/ppge_atlas.zim').read_bytes()
    assert atlas==(root/'app/res/PPSSPP/ppge_atlas.zim').read_bytes()
    assert {'bin/TizenPSP.dll','shared/res/TizenPSP.png','author-signature.xml','signature1.xml','res/LICENSE','res/THIRD-PARTY-NOTICES.md'}<=names
    icon=z.read('shared/res/TizenPSP.png')
    assert icon==(root/'app/shared/res/TizenPSP.png').read_bytes()
    assert icon[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',icon[16:24])==(117,117)
    theme_hashes={}
    for theme in ['vice-city','liberty-city','olympus','kombat','tekken','grand-tour','most-wanted','far-far-away']:
        name='res/themes/'+theme+'.png';art=z.read(name)
        assert art==(root/'app'/name).read_bytes() and 0<len(art)<=8*1024*1024
        assert art[:8]==b'\x89PNG\r\n\x1a\n'
        width,height=struct.unpack('>II',art[16:24]);assert width>=1280 and height>=720
        theme_hashes[theme]=hashlib.sha256(art).hexdigest()
    managed=z.read('bin/TizenPSP.dll')
    # The labels are read from this exact embedded package manifest.
    assert (root/'app/tizen-manifest.xml').read_bytes() in managed, 'Managed labels contain a stale manifest'
    for name in ('Program.cs', 'LibraryView.cs', 'FrontendOptions.cs'):
        text=(root/'app'/name).read_text(encoding='utf-8')
        assert not __import__('re').search(r'(?:TizenPPSSPP |app_version=|Text\(")0[.]\d+[.]\d+', text), 'Hard-coded app version: '+name

    assert managed==(root/'app/bin/Release/tizen50/TizenPSP.dll').read_bytes()
    assert z.read('res/THIRD-PARTY-NOTICES.md')==(root/'THIRD-PARTY-NOTICES.md').read_bytes()
    assert not any('pcsx' in n.lower() or 'psxnative' in n.lower() or n.endswith(('.p12','.pfx')) for n in names)
report={'package':package.name,'package_sha256':hashlib.sha256(package.read_bytes()).hexdigest(),'managed_sha256':hashlib.sha256(managed).hexdigest(),'core_sha256':core_hash,'core_bytes':len(core),
        'icon_sha256':hashlib.sha256(icon).hexdigest(),'theme_sha256':theme_hashes,
        'bridge_sha256':hashlib.sha256(bridge).hexdigest(),'bridge_bytes':len(bridge),
        'jit_sha256':jit_hash,'jit_bytes':len(jit),
        'native_sha256':digest,'native_bytes':len(native),'baseline_sha256':baseline_hash,'glview_sha256':glview_hash,'glview_bytes':len(glview),'checks':'identity, ARM32 soft-float, payload hashes, original baseline hash, GLView module has no readback, license, signature-file presence, no PS1 core or private keys',
        'limits':'No signature trust validation, TV installation or native execution performed.'}
(root/'build').mkdir(exist_ok=True)
(root/'build/package-validation.json').write_text(json.dumps(report,indent=2)+'\n')
print('PASS: standalone TizenPSP package identity and native payload; development signature files present.')
