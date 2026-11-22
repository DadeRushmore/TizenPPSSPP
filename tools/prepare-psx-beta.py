"""Export the PSX beta without personal assets, build caches or signing material.

The output is intentionally separate from the user's working TV build.
Run once into a new directory; review and build the export before publication.
"""
from pathlib import Path
import shutil
import json

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'ports/TizenPS-0.3.34'
DEST = ROOT / 'build/psx-public-beta/source'
if DEST.exists():
    raise SystemExit('Export already exists; refusing to overwrite review work.')
DEST.mkdir(parents=True)
SKIP_DIRS = {'.git', '.cache', '__pycache__', 'bin', 'obj', 'node_modules', 'covers', 'outputs', 'preview-data', 'test-results'}
SKIP_EXT = {'.o', '.pdb', '.tpk', '.wgt', '.p12', '.pfx', '.crt', '.cer', '.pem', '.key', '.der', '.jks', '.keystore', '.bin', '.cue', '.iso', '.img', '.chd', '.pbp', '.mcr', '.srm', '.log'}
def copy_tree(folder):
    for src in (SOURCE / folder).rglob('*'):
        rel = src.relative_to(SOURCE)
        if not src.is_file() or any(p in SKIP_DIRS for p in rel.parts):
            continue
        if src.suffix.lower() in SKIP_EXT or src.name.startswith(('personal-', '.env')):
            continue
        if src.name in {'commands.json', 'author-signature.xml', 'profiles.xml'} or src.name.startswith('signature'):
            continue
        if src.suffix.lower() in {'.so', '.exe'} and str(rel).replace('\\', '/') not in {
            'roelofs-psx-native/res/libpcsx_tizen.so', 'roelofs-psx-native/res/libovprobe.so', 'roelofs-psx-native/res/homebrew.exe'}:
            continue
        target = DEST / rel
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(src, target)

for folder in ['roelofs-psx-native', 'pcsx-rearmed-native', 'toolchain-glibc-source', 'LICENSES',
               'library-ui-checks', 'library-scan-checks', 'psx-player-checks', 'native-elf-checks',
               'dualsense-checks', 'fullspec-checks', 'audio-checks', 'performance-checks', 'performance-elf-checks', 'tests']:
    copy_tree(folder)
for name in ['LICENSE', 'THIRD-PARTY-NOTICES.md', '.gitignore', 'catalog.json']:
    shutil.copy2(SOURCE/name, DEST/name)
for name in ['make-ui.ps1', 'render-public-art.cjs', 'render-tizenpsx-wordmark.ps1', 'render-native-scenes.ps1', 'check-release.py']:
    (DEST/'tools').mkdir(exist_ok=True)
    shutil.copy2(SOURCE/'tools'/name, DEST/'tools'/name)
(DEST/'docs').mkdir(exist_ok=True)
shutil.copy2(SOURCE/'docs/LIBRARY-DRIVE-GUIDE.md', DEST/'docs/LIBRARY-DRIVE-GUIDE.md')
(DEST/'preview/assets').mkdir(parents=True)
# Distinguish the new distribution package from the existing private build.
for name in ['tizen-manifest.xml', 'Program.cs', 'LibraryScenes.cs']:
    path = DEST/'roelofs-psx-native'/name
    path.write_text(path.read_text(encoding='utf-8').replace('0.3.37', '0.3.38'), encoding='utf-8')
# This helper must follow the same fallback order as the actual native UI.
path = DEST/'library-ui-checks/PreviewCanvas.cs'
text = path.read_text(encoding='utf-8')
text = text.replace('return File.Exists(p)?p:Path.Combine(res,"ui","missing.png");',
    'if(File.Exists(p))return p;p=Path.Combine(res,"demo-covers",game.Id+".png");return File.Exists(p)?p:Path.Combine(res,"ui","missing.png");')
text = text.replace('g.Id=="tekken3"', 'g.Id=="mktrilogy"')
path.write_text(text, encoding='utf-8')
print('Exported', sum(p.is_file() for p in DEST.rglob('*')), 'files to', DEST)
