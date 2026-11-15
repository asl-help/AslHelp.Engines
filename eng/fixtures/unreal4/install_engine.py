"""Installs 1 pinned engine through legendary. Unreal4/UnrealEngine.cs writes this file to a temp folder and runs it through uv.

Arguments: app build sha1 base_path install_tags manifest_path, or --check-reader to test the reader patch offline.
Lines: every line is a step line of the tool, such as "download: 45%, 61.4 MiB/s".
Exit codes: 0 done, 10 not logged in, 11 manifest or build not pinned, 12 app in legendary's installed.json, 13 file hash check failed.
"""

import contextlib
import copy
import hashlib
import logging
import logging.handlers
import multiprocessing
import os
import queue
import subprocess
import sys
import time

from concurrent.futures import ThreadPoolExecutor

from legendary.core import LegendaryCore
from legendary.models import json_manifest

EXIT_NOT_LOGGED_IN = 10
EXIT_NOT_PINNED = 11
EXIT_LISTED_AS_INSTALLED = 12
EXIT_FILES_BAD = 13

# Print at most 9 paths, because the tool keeps the last 10 lines for its error message and the count comes last.
MAX_BAD_PATHS = 9

# Print a download line every 5 percent, because the build log keeps them all.
PROGRESS_STEP = 5

# Stop after 10 minutes without a finished chunk, because legendary retries a failed chunk forever.
STALL_SECONDS = 600


LEGENDARY_READ = json_manifest.JSONCDL.read.__func__


def read_chunk_list(cls, json_data, manifest_version=13):
    """Fills in the lists before JSONCDL.read, because Epic's manifests lack ChunkFilesizeList on 4.0 and ChunkShaList on 4.0 to 4.2.

    Legendary reads chunk sizes only for its dl_size number and chunk hashes only to write its manifest copy.
    The check after the download uses the file hashes, which come from FileManifestList.
    """
    sizes = json_data.setdefault('ChunkFilesizeList', {})
    shas = json_data.setdefault('ChunkShaList', {})

    for guid in json_data['ChunkHashList']:
        sizes.setdefault(guid, '000')
        shas.setdefault(guid, '')

    return LEGENDARY_READ(cls, json_data, manifest_version)


def check_reader():
    """Reads a chunk list with both lists, without the sha list and without both."""
    guid = '00000000000000000000000000000001'
    required = {'ChunkHashList': {guid: '001000000000000000000000'}, 'DataGroupList': {guid: '007'}}
    sizes = {'ChunkFilesizeList': {guid: '000016000000000000000000'}}
    shas = {'ChunkShaList': {guid: 'aa' * 20}}

    for lists, file_size, sha_hash in ((sizes | shas, 4096, b'\xaa' * 20), (sizes, 4096, b''), ({}, 0, b'')):
        # Copy the lists, because JSONCDL.read empties them.
        chunk = json_manifest.JSONCDL.read(copy.deepcopy(required | lists)).elements[0]

        # Read guid_num first, because group_num returns the stored value only after that.
        if (chunk.guid_num, chunk.group_num, chunk.hash, chunk.file_size, chunk.sha_hash) != (1, 7, 1, file_size, sha_hash):
            sys.exit(f'bad chunk: {chunk!r}')


def is_logged_in(core):
    """Returns whether legendary has a working login."""
    try:
        return core.login()
    except ValueError as error:
        # Catch ValueError, because login() raises it when legendary doesn't have a stored login.
        print(error)
        return False


def download(dlm, status):
    """Runs the download manager and returns whether it finished."""
    # Print errors only, because workers log a warning for each retry and the manager logs its progress every second.
    handler = logging.StreamHandler()
    handler.setLevel(logging.ERROR)
    logs = multiprocessing.Queue(-1)
    listener = logging.handlers.QueueListener(logs, handler, respect_handler_level=True)
    listener.start()

    # Set the queue, because the workers log to it without checking for one.
    dlm.logging_queue = logs
    dlm.start()

    progress = None
    printed_step = None
    changed_at = time.monotonic()

    while dlm.is_alive():
        with contextlib.suppress(queue.Empty):
            update = status.get(timeout=1)

            if update.progress != progress:
                progress = update.progress
                changed_at = time.monotonic()
                step = progress // PROGRESS_STEP

                # Print the percent of chunks and the speed as they are, because legendary's status doesn't have a byte count for a bar.
                if step != printed_step:
                    printed_step = step
                    print(f'download: {int(progress)}%, {update.download_speed / 2**20:.1f} MiB/s')

        if time.monotonic() - changed_at > STALL_SECONDS:
            print(f'download stalled: no finished chunk for {STALL_SECONDS} seconds')

            # Kill the tree, because terminate() only ends the manager and its workers would go on retrying.
            subprocess.run(['taskkill', '/F', '/T', '/PID', str(dlm.pid)], capture_output=True, check=False)
            break

    dlm.join()
    listener.stop()

    if dlm.exitcode != 0:
        print(f'download manager exit code: {dlm.exitcode}')
        return False

    return True


def create_empty_files(install_path, files):
    """Creates the files of the manifest that don't have any chunks."""
    for file in files:
        if not file.chunk_parts:
            path = os.path.join(install_path, file.filename)
            os.makedirs(os.path.dirname(path), exist_ok=True)
            open(path, 'a').close()


def file_matches(path, sha1):
    """Returns whether the file has the hash, and False for a file that is missing or can't be read."""
    try:
        with open(path, 'rb') as file:
            return hashlib.file_digest(file, 'sha1').hexdigest() == sha1
    except OSError:
        return False


def check_files(dlm, files):
    """Hashes every installed file and returns whether all match, because legendary doesn't check any hash while it downloads."""
    print(f'checking files: {len(files)}')

    hashes = {file.filename: file.sha_hash.hex() for file in files}
    good = []
    bad = []

    # Hash on threads, because file reads and hashlib let other threads run and an engine has up to 113,257 files.
    with ThreadPoolExecutor(os.cpu_count()) as pool:
        matches = pool.map(lambda item: file_matches(os.path.join(dlm.dl_dir, item[0]), item[1]), hashes.items())

        for path, match in zip(hashes, matches):
            (good if match else bad).append(path)

    if not bad:
        return True

    # List only the good files in the resume file, so the next run downloads the bad ones and nothing else.
    with open(dlm.resume_file, 'w', encoding='utf-8') as resume:
        resume.writelines(f'{hashes[path]}:{path}\n' for path in good)

    for path in bad[:MAX_BAD_PATHS]:
        print(path)

    print(f'bad files: {len(bad)}')
    return False


def install(app, build, sha1, base_path, tags, manifest_path):
    """Downloads the pinned manifest and its files and returns the exit code."""
    core = LegendaryCore()

    if not is_logged_in(core):
        return EXIT_NOT_LOGGED_IN

    # Refuse a listed app, because prepare_download then reuses its folder and ignores base_path.
    if core.is_installed(app):
        return EXIT_LISTED_AS_INSTALLED

    # Read the stored game first, because the manifest request only needs its ids and a list refresh sends requests for the whole library.
    game = core.get_game(app, platform='Windows')

    if game is None:
        # Pass skip_ue, because the list leaves engines out by default.
        core.get_game_and_dlc_list(update_assets=True, platform='Windows', skip_ue=False)
        game = core.get_game(app, platform='Windows')

    if game is None:
        print(f'{app} is not in the library of this Epic account')
        return 1

    data, base_urls, _, _ = core.get_cdn_manifest(game, platform='Windows')
    got_sha1 = hashlib.sha1(data).hexdigest()

    if got_sha1 != sha1:
        print(f'sha1: pinned {sha1} got {got_sha1}')
        return EXIT_NOT_PINNED

    manifest = core.load_manifest(data)

    if manifest.meta.build_version != build:
        print(f'build: pinned {build} got {manifest.meta.build_version}')
        return EXIT_NOT_PINNED

    with open(manifest_path, 'wb') as file:
        file.write(data)

    # Install from the file just checked, because prepare_download fetches the manifest again otherwise.
    game.base_urls = base_urls
    status = multiprocessing.Queue(-1)
    dlm, _, _ = core.prepare_download(
        game=game,
        base_path=base_path,
        game_folder=app,
        status_q=status,
        override_manifest=manifest_path,
        file_install_tag=tags)

    files = [file for file in manifest.file_manifest_list.elements if tags is None or not file.install_tags or '' in file.install_tags]

    if dlm.chunks_to_dl:
        if not download(dlm, status):
            return 1
    else:
        # Skip the manager when a resume finds every chunk done, because it divides by the chunk count.
        # Create the empty files here, because only the manager creates them otherwise.
        create_empty_files(dlm.dl_dir, files)

    if not check_files(dlm, files):
        return EXIT_FILES_BAD

    # Delete the resume file, because only a finished download manager deletes it.
    with contextlib.suppress(FileNotFoundError):
        os.remove(dlm.resume_file)

    return 0


def main(arguments):
    """Runs the command line and returns the exit code."""
    # Set UTF-8, because the tool decodes it and a piped stream uses the ANSI code page. Flush per line, because the bar reads while this runs.
    sys.stdout.reconfigure(encoding='utf-8', errors='replace', line_buffering=True)
    sys.stderr.reconfigure(encoding='utf-8', errors='replace', line_buffering=True)

    json_manifest.JSONCDL.read = classmethod(read_chunk_list)

    if arguments == ['--check-reader']:
        check_reader()
        return 0

    app, build, sha1, base_path, install_tags, manifest_path = arguments

    return install(app, build, sha1, base_path, [''] if install_tags == '1' else None, manifest_path)


# Guard the call, because each worker process imports this file again.
if __name__ == '__main__':
    sys.exit(main(sys.argv[1:]))
