"""Runs the real install script with a fake legendary core and a fake or real download manager.

Arguments: script_path case folder. InstallEngineScriptEdgeCaseTests.cs copies this file to the folder and checks its lines.
"""

import hashlib
import http.server
import importlib.util
import json
import os
import subprocess
import sys
import threading
import types

from legendary.core import LegendaryCore
from legendary.models.chunk import Chunk
from legendary.models.game import Game
from legendary.models.manifest import CDL, FML, ChunkInfo, ChunkPart, CustomFields, FileManifest, Manifest, ManifestMeta

spec = importlib.util.spec_from_file_location('install_engine', sys.argv[1])
engine = importlib.util.module_from_spec(spec)
spec.loader.exec_module(engine)

case = sys.argv[2]
base = sys.argv[3]
folder = os.path.join(base, 'app')
resume_path = os.path.join(base, 'app.resume')
manifest_path = os.path.join(base, 'app.manifest')

BUILD = '1.2.3-4'
DATA = b'manifest bytes'
SHA1 = hashlib.sha1(DATA).hexdigest()


def entry(name, content=b'', tags=(), chunks=True):
    parts = ['part'] if chunks else []
    return types.SimpleNamespace(filename=name, sha_hash=hashlib.sha1(content).digest(), install_tags=list(tags), chunk_parts=parts)


def write(name, content):
    path = os.path.join(folder, name)
    os.makedirs(os.path.dirname(path), exist_ok=True)

    with open(path, 'wb') as file:
        file.write(content)


class Manager:
    def __init__(self, chunks=(), updates=(), exitcode=0):
        self.chunks_to_dl = list(chunks)
        self.dl_dir = folder
        self.exitcode = exitcode
        self.logging_queue = None
        self.pid = 0
        self.polls = len(updates)
        self.resume_file = resume_path
        self.started = False
        self.status = None
        self.updates = updates

    def start(self):
        self.started = True

        for percent in self.updates:
            self.status.put(types.SimpleNamespace(progress=percent, download_speed=1024 * 1024))

    def is_alive(self):
        self.polls -= 1
        return self.polls >= 0

    def join(self):
        pass


class StalledManager(Manager):
    def start(self):
        self.started = True
        self.process = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(60)'])
        self.pid = self.process.pid

    def is_alive(self):
        return self.process.poll() is None

    def join(self):
        self.exitcode = self.process.wait()


class Core:
    def __init__(self, files=(), manager=None, login=True, listed=False, game=True, stored=False, data=DATA, build=BUILD):
        self.build = build
        self.data = data
        self.files = list(files)
        self.game = game
        self.listed = listed
        self.logged_in = login
        self.manager = manager or Manager()
        self.refreshed = False
        self.requests = 0
        self.stored = stored

    def login(self):
        if self.logged_in is None:
            raise ValueError('No saved credentials')

        return self.logged_in

    def is_installed(self, app):
        return self.listed

    def get_game_and_dlc_list(self, update_assets, platform, skip_ue):
        print(f'skip_ue: {skip_ue}')
        self.refreshed = True

    def get_game(self, app, platform):
        return types.SimpleNamespace(base_urls=None) if self.game and (self.stored or self.refreshed) else None

    def get_cdn_manifest(self, game, platform):
        self.requests += 1
        return self.data, ['https://example.invalid/base'], False, {}

    def load_manifest(self, data):
        meta = types.SimpleNamespace(build_version=self.build)
        return types.SimpleNamespace(meta=meta, file_manifest_list=types.SimpleNamespace(elements=self.files))

    def prepare_download(self, game, base_path, game_folder, status_q, override_manifest, file_install_tag):
        print('prepare_download')
        print(f'base_path: {base_path}')
        print(f'game_folder: {game_folder}')
        print(f'file_install_tag: {file_install_tag}')
        print(f'override_manifest: {override_manifest}')
        print(f'base_urls: {game.base_urls}')
        self.manager.status = status_q
        return self.manager, None, None


def new_core():
    if case == 'login-false':
        return Core(login=False)

    if case == 'login-missing':
        return Core(login=None)

    if case == 'listed':
        return Core(listed=True)

    if case == 'no-game':
        return Core(game=False)

    if case == 'other-sha1':
        return Core(data=b'other manifest bytes')

    if case == 'other-build':
        return Core(build='1.2.3-5')

    if case == 'stored-game':
        write('good.bin', b'hello')
        return Core([entry('good.bin', b'hello')], stored=True)

    if case == 'resume-done':
        write('good.bin', b'hello')

        with open(resume_path, 'w', encoding='utf-8') as file:
            file.write('stale:line\n')

        return Core([entry('good.bin', b'hello'), entry('folder/sub/empty.bin', chunks=False)])

    if case == 'bad-files':
        write('good.bin', b'hello')
        write('bad.bin', b'other')
        return Core([entry('good.bin', b'hello'), entry('bad.bin', b'hello'), entry('missing.bin', b'hello')])

    if case == 'many-bad-files':
        os.makedirs(folder)
        return Core([entry(f'file{i}.bin', b'hello') for i in range(12)])

    if case in ('tags-on', 'tags-off'):
        write('plain.bin', b'hello')
        write('empty-tag.bin', b'hello')
        untagged = [entry('plain.bin', b'hello'), entry('empty-tag.bin', b'hello', tags=[''])]
        return Core([*untagged, entry('tagged.bin', b'hello', tags=['editor_symbols'])])

    if case == 'manager-fails':
        with open(resume_path, 'w', encoding='utf-8') as file:
            file.write('')

        return Core([entry('good.bin', b'hello')], Manager(['chunk'], [50.0], 3))

    if case == 'download':
        write('good.bin', b'hello')
        return Core([entry('good.bin', b'hello')], Manager(['chunk'], [0.0, 0.0, 3.0, 5.0, 7.5, 50.0, 100.0]))

    if case == 'stall':
        engine.STALL_SECONDS = 0
        return Core([entry('good.bin', b'hello')], StalledManager(['chunk']))

    if case == 'non-ascii':
        write('Ünï/ключ.bin', b'hello')
        return Core([entry('Ünï/ключ.bin', b'hello'), entry('Café/日本語.bin', b'hello')])

    raise ValueError(case)


def run_with_fake_core():
    core = new_core()
    engine.LegendaryCore = lambda: core
    tags = '0' if case == 'tags-off' else '1'
    code = engine.main(['app', BUILD, SHA1, base, tags, manifest_path])

    print(f'manifest requests: {core.requests}')
    print(f'manifest file: {os.path.exists(manifest_path)}')
    print(f'resume file: {os.path.exists(resume_path)}')
    print(f'manager started: {core.manager.started}')
    print(f'manager alive: {core.manager.is_alive()}')

    if os.path.exists(manifest_path):
        with open(manifest_path, 'rb') as file:
            print(f'manifest sha1: {hashlib.sha1(file.read()).hexdigest()}')

    if case in ('bad-files', 'non-ascii'):
        with open(resume_path, encoding='utf-8') as file:
            for line in file:
                print(f'resume: {line.strip()}')

    return code


def new_chunk(info, content):
    chunk = Chunk()
    chunk.guid = info.guid
    chunk.data = content
    return chunk


def publish(root, info, content):
    chunk = new_chunk(info, content)
    info.hash = chunk.hash
    info.sha_hash = chunk.sha_hash
    info.window_size = 1024 * 1024
    path = os.path.join(root, 'base', *info.path.split('/'))
    os.makedirs(os.path.dirname(path), exist_ok=True)

    with open(path, 'wb') as file:
        info.file_size = chunk.write(file)


def replace(root, info, content):
    with open(os.path.join(root, 'base', *info.path.split('/')), 'wb') as file:
        new_chunk(info, content).write(file)


def binary_manifest(root, files):
    manifest = Manifest()
    manifest.meta = ManifestMeta()
    manifest.meta.app_name = 'app'
    manifest.meta.build_version = BUILD
    manifest.chunk_data_list = CDL()
    manifest.file_manifest_list = FML()
    manifest.custom_fields = CustomFields()
    infos = {}

    for index, (name, content, tags) in enumerate(files):
        file = FileManifest()
        file.filename = name
        file.hash = hashlib.sha1(content).digest()
        file.install_tags = list(tags)

        if content:
            info = ChunkInfo(manifest_version=18)
            info.guid = (index + 1, 2, 3, 4)
            publish(root, info, content)
            infos[name] = info
            manifest.chunk_data_list.elements.append(info)
            file.chunk_parts.append(ChunkPart(info.guid, 0, len(content)))
            file.file_size = len(content)

        manifest.file_manifest_list.elements.append(file)

    return manifest.write(), infos


def blob(data):
    return ''.join(f'{byte:03d}' for byte in data)


def json_manifest(root, files):
    hashes = {}
    groups = {}
    entries = []
    infos = {}

    for index, (name, content, _) in enumerate(files):
        parts = []

        if content:
            info = ChunkInfo(manifest_version=7)
            info.guid = (index + 1, 2, 3, 4)
            publish(root, info, content)
            infos[name] = info
            guid = ''.join(f'{part:08X}' for part in info.guid)
            hashes[guid] = blob(info.hash.to_bytes(8, 'little'))
            groups[guid] = blob(info.group_num.to_bytes(1, 'little'))
            parts.append({'Guid': guid, 'Offset': blob((0).to_bytes(4, 'little')), 'Size': blob(len(content).to_bytes(4, 'little'))})

        entries.append({'Filename': name, 'FileHash': blob(hashlib.sha1(content).digest()), 'FileChunkParts': parts})

    data = {
        'ManifestFileVersion': blob((7).to_bytes(4, 'little')),
        'bIsFileData': False,
        'AppID': blob((0).to_bytes(4, 'little')),
        'AppNameString': 'app',
        'BuildVersionString': BUILD,
        'LaunchExeString': '',
        'LaunchCommand': '',
        'PrereqIds': [],
        'PrereqName': '',
        'PrereqPath': '',
        'PrereqArgs': '',
        'FileManifestList': entries,
        'ChunkHashList': hashes,
        'DataGroupList': groups,
    }

    return json.dumps(data).encode(), infos


def serve(root):
    requests = {}

    class Handler(http.server.SimpleHTTPRequestHandler):
        def __init__(self, *arguments, **options):
            super().__init__(*arguments, directory=root, **options)

        def do_GET(self):
            requests[self.path] = requests.get(self.path, 0) + 1
            super().do_GET()

        def log_message(self, *arguments):
            pass

    server = http.server.ThreadingHTTPServer(('127.0.0.1', 0), Handler)
    threading.Thread(target=server.serve_forever, daemon=True).start()
    return f'http://127.0.0.1:{server.server_address[1]}/base', requests


def run_with_server():
    config = os.path.join(base, 'config')
    os.makedirs(config)
    os.environ['LEGENDARY_CONFIG_PATH'] = config
    os.environ['NO_PROXY'] = '127.0.0.1'

    with open(os.path.join(config, 'config.ini'), 'w', encoding='utf-8') as file:
        file.write('[Legendary]\nmax_workers = 2\nmax_memory = 64\n')

    uses_json = 'json' in case
    bad_chunk = 'bad-chunk' in case
    root = os.path.join(base, 'server')
    a = b'a' * 1000
    b = b'b' * 2000
    files = [('folder/a.bin', a, ()), ('b.bin', b, ()), ('folder/empty.bin', b'', ())]

    if not uses_json:
        files.append(('tagged.bin', b'tagged', ('editor_symbols',)))

    data, infos = json_manifest(root, files) if uses_json else binary_manifest(root, files)
    url, requests = serve(root)

    if bad_chunk:
        replace(root, infos['b.bin'], b'wrong bytes')

    if 'resume-done' in case:
        write('folder/a.bin', a)
        write('b.bin', b)

        # Write the resume file where legendary keeps it, tmp/<app>.resume under its config folder.
        os.makedirs(os.path.join(config, 'tmp'))

        with open(os.path.join(config, 'tmp', 'app.resume'), 'w', encoding='utf-8') as file:
            file.write(f'{hashlib.sha1(a).hexdigest()}:folder/a.bin\n{hashlib.sha1(b).hexdigest()}:b.bin\n')

    class LocalCore(LegendaryCore):
        def login(self, force_refresh=False):
            return True

        def get_game(self, app_name, update_meta=False, platform='Windows'):
            return Game(app_name=app_name, app_title=app_name, metadata={}, asset_infos={})

        def get_cdn_manifest(self, game, platform='Windows', disable_https=False):
            return data, [url], False, {}

    # Stop a run after 1 minute without a chunk, because legendary retries a missing chunk forever.
    engine.STALL_SECONDS = 60
    engine.LegendaryCore = LocalCore
    tags = '0' if uses_json else '1'
    arguments = ['app', BUILD, hashlib.sha1(data).hexdigest(), base, tags, manifest_path]
    code = engine.main(arguments)

    if bad_chunk:
        print(f'first exit: {code}')
        replace(root, infos['b.bin'], b)
        code = engine.main(arguments)

    for name, info in infos.items():
        print(f'chunk requests of {name}: {requests.get("/base/" + info.path, 0)}')

    print(f'resume file: {os.path.exists(os.path.join(config, "tmp", "app.resume"))}')
    return code


if __name__ == '__main__':
    sys.exit(run_with_server() if case.startswith('server-') else run_with_fake_core())
