"""Render current app layouts for the public READMEs; these are not TV captures."""
import argparse
import os
import shutil
import subprocess
from pathlib import Path

root = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser()
parser.add_argument('--psx-game', default='mktrilogy', choices=[
    'mktrilogy', 'mk4', 'personal-mortalkombatmythologiessubzero'])
args = parser.parse_args()
work = root / 'build/readme-previews'
work.mkdir(parents=True, exist_ok=True)
port = root / 'ports/TizenPS-0.3.34'
project = work / 'psx'
project.mkdir(exist_ok=True)

# Compile the actual current layout with the existing host drawing adapter.
# Only the disposable fixture selects a different game; app code is untouched.
source = port / 'library-ui-checks'
csproj = (source / 'Check.csproj').read_text().replace(
    '../roelofs-psx-native/', (port / 'roelofs-psx-native').as_posix() + '/')
(project / 'Check.csproj').write_text(csproj, encoding='utf-8')
shutil.copy2(source / 'global.json', project / 'global.json')
program = (source / 'Program.cs').read_text().replace(
    'new PlayerApp().RunChecks();ScenePreview.Export();', 'ScenePreview.Export();')
(project / 'Program.cs').write_text(program, encoding='utf-8')
canvas = (source / 'PreviewCanvas.cs').read_text()
canvas = canvas.replace('"../roelofs-psx-native/res"',
                        '"' + (port / 'roelofs-psx-native/res').as_posix() + '"')
canvas = canvas.replace('g.Id=="tekken3"', 'g.Id=="' + args.psx_game + '"')
canvas = canvas.replace('"../outputs/v036-scenes.json"',
                        '"' + (work / 'psx-scenes.json').as_posix() + '"')
(project / 'PreviewCanvas.cs').write_text(canvas, encoding='utf-8')
subprocess.run(['dotnet', 'run', '--project', str(project / 'Check.csproj'),
                '-c', 'Release', '-v', 'quiet'], cwd=project, check=True)

shell = shutil.which('pwsh') or 'powershell'
subprocess.run([shell, '-NoProfile', '-File', str(port / 'tools/render-native-scenes.ps1'),
                '-InputJson', os.path.relpath(work / 'psx-scenes.json', port / 'tools'),
                '-OutputDirectory', os.path.relpath(work / 'psx-images', port / 'tools')], check=True)
subprocess.run(['python', str(root / 'tools/preview-library.py')], cwd=root, check=True)

for name, image in [
    ('TizenPSX', work / 'psx-images/covers.png'),
    ('TizenPPSSPP', root / 'build/library-preview/01-collection.png'),
]:
    dest = root / 'github-placeholder' / name / 'docs/images'
    dest.mkdir(parents=True, exist_ok=True)
    shutil.copy2(image, dest / 'library-preview.png')
    print(f'{name}: {dest / "library-preview.png"}')
print('1920x1080 host renders of current layout code; fonts/compositing may differ on TV.')
