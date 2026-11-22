import importlib.util, tempfile, unittest, zipfile
from pathlib import Path
spec=importlib.util.spec_from_file_location('prep',Path(__file__).with_name('psx_pack_prepare.py'));p=importlib.util.module_from_spec(spec);spec.loader.exec_module(p)

class PrepareChecks(unittest.TestCase):
    def test_unsafe_members(self):
        for name in ['../escape.bin','a/../../b.bin','C:/disc.bin','/disc.bin','\\disc.bin','\\\\server\\share\\disc.bin','disc.bin:stream','NUL.bin','a/CON','a./disc.bin','a /disc.bin']:
            with self.subTest(name=name),self.assertRaises(ValueError):p.member_path(name)
        self.assertEqual(p.member_path('Game\\Track 01.bin'),Path('Game')/'Track 01.bin')
    def test_cue_case_and_companions(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'Track.BIN').write_bytes(bytes(50000));(root/'game.cue').write_text('FILE "track.bin" BINARY\n TRACK 01 MODE2/2352\n INDEX 01 00:00:00\n');(root/'cover.jpg').write_bytes(b'kept')
            launchers,notes=p.prepare_discs(root);self.assertEqual(launchers,[root/'game.cue']);self.assertIn('"Track.BIN"',(root/'game.cue').read_text());self.assertEqual((root/'cover.jpg').read_bytes(),b'kept')
    def test_cue_missing_track(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'bad.cue').write_text('FILE "missing.bin" BINARY\n TRACK 01 MODE2/2352\n')
            with self.assertRaises(ValueError):p.prepare_discs(root)
    def test_stale_cue_reference_keeps_audio_layout(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);(root/'Pandemonium.bin').write_bytes(bytes(50000));cue=root/'Pandemonium.cue';cue.write_text('FILE "SLUS-00232.BIN" BINARY\n TRACK 01 MODE2/2352\n INDEX 01 00:00:00\n TRACK 02 AUDIO\n INDEX 01 00:00:10\n')
            launchers,notes=p.prepare_discs(root);self.assertEqual(launchers,[cue]);self.assertIn('FILE "Pandemonium.bin"',cue.read_text());self.assertIn('TRACK 02 AUDIO',cue.read_text());self.assertTrue(notes)
    def test_mdf_keeps_metadata_and_bytes(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);data=bytes(50000);(root/'Game.mdf').write_bytes(data);(root/'Game.mds').write_bytes(b'MEDIA DESCRIPTOR'+bytes(100))
            launchers,_=p.prepare_discs(root);self.assertEqual(launchers,[root/'Game.bin']);self.assertEqual((root/'Game.bin').read_bytes(),data);self.assertTrue((root/'Game.mds').exists())
    def test_archive_prepare_resume_and_source_preserved(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);source=root/'source';source.mkdir();out=root/'PSX';archive=source/'A Game.zip'
            with zipfile.ZipFile(archive,'w') as z:
                z.writestr('nested/track.bin',bytes(50000));z.writestr('nested/disc.cue','FILE "track.bin" BINARY\n TRACK 01 MODE2/2352\n INDEX 01 00:00:00\n')
            original=archive.read_bytes();rows=p.run(source,out,p.find_seven(),True,False,lambda x:None);self.assertEqual(rows[0]['status'],'prepared');self.assertEqual(archive.read_bytes(),original)
            rows=p.run(source,out,p.find_seven(),True,False,lambda x:None);self.assertEqual(rows[0]['status'],'prepared')
            track=out/'A Game/nested/track.bin';track.write_bytes(b'changed');rows=p.run(source,out,p.find_seven(),True,False,lambda x:None);self.assertEqual(rows[0]['status'],'review');self.assertEqual(track.read_bytes(),b'changed')
    def test_pack_tools_excluded(self):
        with tempfile.TemporaryDirectory() as temp:
            root=Path(temp);tools=root/'Emulators,Plugins,BIOS,error fixes';tools.mkdir();(tools/'bios.bin').write_bytes(b'x');(root/'Game.7z').touch();self.assertEqual(p.candidates(root),[root/'Game.7z'])

if __name__=='__main__':unittest.main()
