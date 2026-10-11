"""Build pinned native offline diffusion; does not download or run models.

Set AIPT_NDK_ROOT, AIPT_CMAKE_BIN, AIPT_SD_SOURCE and AIPT_NATIVE_OUTPUT.
The source checkout and dependencies stay outside the public repository.
"""
from pathlib import Path
import hashlib
import json
import os
import shutil
import subprocess
import sys

ROOT = Path(__file__).resolve().parent
LOCK = json.loads((ROOT / 'source-lock.json').read_text(encoding='utf-8'))

def setting(name):
    value = os.environ.get(name)
    if not value:
        raise RuntimeError('Set ' + name)
    return Path(value).expanduser().resolve()

def run(args, cwd=None):
    subprocess.run([str(value) for value in args], check=True, cwd=cwd)

def commit(source):
    return subprocess.check_output(['git', '-C', str(source), 'rev-parse', 'HEAD'], text=True).strip()

def main():
    source = setting('AIPT_SD_SOURCE')
    ndk = setting('AIPT_NDK_ROOT')
    cmake = setting('AIPT_CMAKE_BIN')
    out = setting('AIPT_NATIVE_OUTPUT')
    suffix = '.exe' if os.name == 'nt' else ''
    ninja = cmake.parent / ('ninja' + suffix)
    for path in (source / 'include/stable-diffusion.h', source / 'ggml/CMakeLists.txt',
                 ndk / 'build/cmake/android.toolchain.cmake', cmake, ninja):
        if not path.is_file():
            raise RuntimeError('Missing native build dependency: ' + str(path))
    if commit(source) != LOCK['stable_diffusion']['commit'] or commit(source / 'ggml') != LOCK['ggml']['commit']:
        raise RuntimeError('Native source revision differs from source-lock.json; review before updating the lock')
    if ('Pkg.Revision = ' + LOCK['ndk']) not in (ndk / 'source.properties').read_text():
        raise RuntimeError('NDK version differs from source-lock.json')
    out.mkdir(parents=True, exist_ok=True)
    report = {'source': LOCK, 'libraries': {}, 'compatibility_libraries': {}}
    for abi in LOCK['abis']:
        build = out / ('build-arm64' if abi == 'arm64-v8a' else 'build-' + abi)
        run([cmake, '-S', ROOT, '-B', build, '-G', 'Ninja', '-DCMAKE_MAKE_PROGRAM=' + str(ninja),
             '-DCMAKE_TOOLCHAIN_FILE=' + str(ndk / 'build/cmake/android.toolchain.cmake'),
             '-DAIPT_SD_SOURCE=' + str(source), '-DANDROID_ABI=' + abi,
             '-DANDROID_PLATFORM=android-' + str(LOCK['minimum_android_api']), '-DANDROID_STL=c++_static',
             '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_OBJECT_PATH_MAX=240',
             '-DSDCPP_BUILD_COMMIT=' + LOCK['stable_diffusion']['commit'][:7]])
        run([cmake, '--build', build, '--target', 'aipt_diffusion', '-j', os.environ.get('AIPT_NATIVE_JOBS', '4')])
        library = build / 'jni' / LOCK['shared_library']
        target = out / 'jni' / abi / library.name
        target.parent.mkdir(parents=True, exist_ok=True)
        shutil.copyfile(library, target)
        host = 'windows-x86_64' if os.name == 'nt' else ('darwin-x86_64' if sys.platform == 'darwin' else 'linux-x86_64')
        strip = ndk / 'toolchains/llvm/prebuilt' / host / 'bin' / ('llvm-strip' + suffix)
        run([strip, '--strip-unneeded', target])
        report['libraries'][abi] = {'bytes': target.stat().st_size,
                                   'sha256': hashlib.file_digest(target.open('rb'), 'sha256').hexdigest()}
    # A universal APK needs a native entry for 32-bit devices to install.
    # These ABI entries carry ordinary tools only, never an AI placeholder API.
    compatibility = LOCK['compatibility']
    targets = {'armeabi-v7a': 'armv7a-linux-androideabi', 'x86': 'i686-linux-android'}
    compiler = strip.parent / ('clang' + suffix)
    for abi in compatibility['abis']:
        target = out / 'jni' / abi / compatibility['shared_library']
        target.parent.mkdir(parents=True, exist_ok=True)
        run([compiler, '--target=' + targets[abi] + str(compatibility['minimum_android_api']),
             '-Oz', '-fPIC', '-fvisibility=hidden', '-shared', '-Wl,--no-undefined',
             '-Wl,-z,max-page-size=16384', '-Wl,-soname,' + target.name,
             ROOT / 'compat.c', '-o', target])
        run([strip, '--strip-unneeded', target])
        report['compatibility_libraries'][abi] = {'bytes': target.stat().st_size,
                                                 'sha256': hashlib.file_digest(target.open('rb'), 'sha256').hexdigest(),
                                                 'minimum_android_api': compatibility['minimum_android_api'],
                                                 'offline_ai': False}
    (out / 'build-report.json').write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print('Native APK library directory: ' + str(out / 'jni'))

if __name__ == '__main__':
    main()
