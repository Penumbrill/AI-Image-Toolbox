"""Build the native Android app with SDK 35 and JDK 17+, without Gradle."""
from pathlib import Path
import hashlib
import json
import os
import secrets
import shutil
import subprocess
import sys
import tempfile
import zipfile

ROOT = Path(__file__).resolve().parent
REPO = ROOT.parent
TEST = '--test' in sys.argv
SUFFIX = '.exe' if os.name == 'nt' else ''
if hasattr(sys.stdout, 'reconfigure'):
    sys.stdout.reconfigure(encoding='utf-8')


def configured_path(*names):
    for name in names:
        if os.environ.get(name):
            return Path(os.environ[name]).expanduser().resolve()
    raise RuntimeError('Set one of: ' + ', '.join(names))


def run(args):
    result = subprocess.run([str(x) for x in args], cwd=ROOT, capture_output=True,
                            text=True, encoding='utf-8', errors='replace')
    if result.returncode:
        raise RuntimeError('Build command failed: ' + str(args[0]) + '\n' + result.stdout + result.stderr)
    if result.stdout.strip():
        print(result.stdout.strip(), flush=True)


def main():
    sdk = configured_path('AIPT_SDK_ROOT', 'ANDROID_SDK_ROOT', 'ANDROID_HOME')
    java = configured_path('AIPT_JAVA_HOME', 'JAVA_HOME')
    native = configured_path('AIPT_NATIVE_LIB_DIR')
    ort = configured_path('AIPT_ONNXRT_DIR')
    tools = sdk / 'build-tools/35.0.0'
    platform = sdk / 'platforms/android-35/android.jar'
    for required in (platform, tools / ('aapt2' + SUFFIX), java / ('bin/javac' + SUFFIX)):
        if not required.is_file():
            raise RuntimeError('Required build dependency not found: ' + str(required))
    ort_jar = ort / 'classes.jar'
    if not ort_jar.is_file():
        raise RuntimeError('Verified ONNX Runtime classes.jar not found')
    ort_lock = json.loads((ROOT / 'native/onnxruntime.lock.json').read_text(encoding='utf-8'))
    for name, integrity in ort_lock['files'].items():
        dependency = ort / name
        if (not dependency.is_file() or dependency.stat().st_size != integrity['bytes'] or
                hashlib.sha256(dependency.read_bytes()).hexdigest() != integrity['sha256']):
            raise RuntimeError('ONNX Runtime dependency differs from the official pinned archive: ' + name)
    shared_libraries = []
    for abi in ('arm64-v8a', 'x86_64'):
        for filename in ('libaipt_diffusion.so', 'libonnxruntime.so', 'libonnxruntime4j_jni.so'):
            source = (native / abi / filename if filename == 'libaipt_diffusion.so'
                      else ort / 'jni' / abi / filename)
            if not source.is_file():
                raise RuntimeError('Required offline AI library not found: ' + str(source))
            shared_libraries.append((source, f'lib/{abi}/{filename}'))
    for abi in ('armeabi-v7a', 'x86'):
        source = native / abi / 'libaipt_compat.so'
        if not source.is_file():
            raise RuntimeError('Ordinary-tool installation compatibility library missing: ' + abi)
        shared_libraries.append((source, f'lib/{abi}/libaipt_compat.so'))
    base = REPO / 'build/android'
    base.mkdir(parents=True, exist_ok=True)
    # Inherit the workspace ACL, including restricted Windows execution tokens.
    # Python's Windows mkdir(0700) can exclude the calling sandbox token.
    out = base / (('test-' if TEST else 'release-') + secrets.token_hex(12))
    out.mkdir()
    assets = out / 'assets'
    assets.mkdir()
    for language in ('zh', 'en'):
        shutil.copyfile(REPO / f'assets/tutorial.{language}.json', assets / f'tutorial.{language}.json')
    shutil.copyfile(REPO / 'assets/android-ai-models.json', assets / 'android-ai-models.json')
    notice = ((REPO / 'LICENSE').read_text(encoding='utf-8') + '\n\n' +
              (REPO / 'THIRD_PARTY_NOTICES.md').read_text(encoding='utf-8') + '\n\n' +
              (ROOT / 'native/THIRD_PARTY_NOTICES.md').read_text(encoding='utf-8'))
    (assets / 'NOTICE.md').write_text(notice, encoding='utf-8')
    if TEST:
        with zipfile.ZipFile(ROOT / 'tests/fixtures.zip') as archive:
            for item in archive.infolist():
                destination = (assets / item.filename).resolve()
                if not destination.is_relative_to(assets.resolve()):
                    raise RuntimeError('Invalid test fixture path')
            archive.extractall(assets)
    run([tools / ('aapt2' + SUFFIX), 'compile', '--dir', ROOT / 'res', '-o', out / 'resources.zip'])
    run([tools / ('aapt2' + SUFFIX), 'link', '-o', out / 'unsigned.apk', '--manifest', ROOT / 'AndroidManifest.xml',
         '-I', platform, out / 'resources.zip', '-A', assets, '--java', out / 'generated',
         '--min-sdk-version', '21', '--target-sdk-version', '35'])
    classes = out / 'classes'
    classes.mkdir()
    sources = []
    source_roots = [ROOT / 'src'] + ([ROOT / 'tests'] if TEST else [])
    for source_root in source_roots:
        for source in sorted(source_root.rglob('*.java')):
            content = source.read_text(encoding='utf-8')
            if TEST and source.name == 'AndroidAiController.java':
                # Save genuine native diagnostics before calibration in test APKs only.
                raw_hooks = {
                    'if(generated==null)throw new IOException("Native generation produced no image");':
                        'if(generated==null)throw new IOException("Native generation produced no image");AiIntegrationTests.captureNative(a,number,frame,control,mask,generated);',
                    'merge(result,generated,mask,left,top,side,region,job);restoreProtected(result,source,materials,region,options.autoPlace,job);':
                        'merge(result,generated,mask,left,top,side,region,job);restoreProtected(result,source,materials,region,options.autoPlace,job);AiIntegrationTests.captureRaw(a,number,result);',
                }
                for anchor, replacement in raw_hooks.items():
                    if content.count(anchor) != 1:
                        raise RuntimeError('Test raw-image hook anchor changed: ' + anchor)
                    content = content.replace(anchor, replacement)
            if not TEST and source.name == 'MainActivity.java':
                content = content.replace('if(getIntent().getBooleanExtra("selftest",false))runSelfTest();', '')
                start = content.index('    void runSelfTest()')
                end = content.index('\n}', start)
                content = content[:start] + content[end:]
            target = out / 'sources' / source.relative_to(source_root)
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(content, encoding='utf-8')
            sources.append(target)
    sources += sorted((out / 'generated').rglob('*.java'))
    argfile = out / 'javac.args'
    for source in sources:
        relative = source.relative_to(out / 'sources') if source.is_relative_to(out / 'sources') else source.relative_to(out / 'generated')
        (classes / relative.parent).mkdir(parents=True, exist_ok=True)
    argfile.write_text('\n'.join('"' + str(p).replace('\\', '/') + '"' for p in sources), encoding='utf-8')
    run([java / ('bin/javac' + SUFFIX), '-encoding', 'UTF-8', '-source', '8', '-target', '8',
         '-classpath', os.pathsep.join(map(str, (platform, REPO / 'vendor/zxing/zxing-core.jar', ort_jar))), '-d', classes, '@' + str(argfile)])
    with zipfile.ZipFile(out / 'classes.jar', 'w') as archive:
        for path in sorted(classes.rglob('*.class')):
            archive.write(path, path.relative_to(classes).as_posix())
    run([java / ('bin/java' + SUFFIX), '-cp', tools / 'lib/d8.jar', 'com.android.tools.r8.D8',
         '--release', '--min-api', '21', '--lib', platform, '--output', out, out / 'classes.jar', REPO / 'vendor/zxing/zxing-core.jar', ort_jar])
    with zipfile.ZipFile(out / 'unsigned.apk', 'a') as archive:
        archive.write(out / 'classes.dex', 'classes.dex')
        for source, destination in shared_libraries:
            archive.write(source, destination, compress_type=zipfile.ZIP_DEFLATED)
    run([tools / ('zipalign' + SUFFIX), '-P', '16', '-f', '4', out / 'unsigned.apk', out / 'aligned.apk'])
    project_signing = Path.home() / '.ai-image-tools/signing/image-editing-tools'
    signing = project_signing if (project_signing / 'release.jks').is_file() else Path.home() / '.ai-image-tools/signing'
    key = Path(os.environ.get('AIPT_KEYSTORE', str(signing / 'release.jks'))).expanduser().resolve()
    password_file = Path(os.environ.get('AIPT_KEYSTORE_PASS_FILE', str(key.parent / 'password.txt'))).expanduser().resolve()
    if key.is_relative_to(REPO) or password_file.is_relative_to(REPO):
        raise RuntimeError('Keep the signing key and password outside the repository')
    if not key.exists():
        if password_file.exists():
            raise RuntimeError('Password exists but signing key is missing; check paths before creating a new key')
        key.parent.mkdir(parents=True, exist_ok=True)
        password_file.parent.mkdir(parents=True, exist_ok=True)
        password_file.write_text(secrets.token_hex(24), encoding='ascii')
        run([java / ('bin/keytool' + SUFFIX), '-genkeypair', '-keystore', key,
             '-storepass:file', password_file, '-keypass:file', password_file,
             '-alias', 'guangyingruoshui', '-keyalg', 'RSA', '-keysize', '3072', '-validity', '10000',
             '-dname', 'CN=Guangyingruoshui,OU=Image Tools,O=Independent,L=Local,ST=Local,C=CN'])
    if not password_file.is_file():
        raise RuntimeError('Signing password file not found')
    apk = base / 'test.apk' if TEST else REPO / 'releases/Android/AI-Image-Editing-Tools.apk'
    apk.parent.mkdir(parents=True, exist_ok=True)
    run([java / ('bin/java' + SUFFIX), '-jar', tools / 'lib/apksigner.jar', 'sign', '--ks', key,
         '--ks-key-alias', 'guangyingruoshui', '--ks-pass', 'file:' + str(password_file),
         '--v1-signing-enabled', 'true', '--v2-signing-enabled', 'true', '--v4-signing-enabled', 'false',
         '--out', out / 'signed.apk', out / 'aligned.apk'])
    run([java / ('bin/java' + SUFFIX), '-jar', tools / 'lib/apksigner.jar', 'verify', '--verbose', out / 'signed.apk'])
    run([tools / ('aapt2' + SUFFIX), 'dump', 'badging', out / 'signed.apk'])
    shutil.copyfile(out / 'signed.apk', apk)
    print('APK:', apk, 'bytes:', apk.stat().st_size, 'SHA256:', hashlib.sha256(apk.read_bytes()).hexdigest())


if __name__ == '__main__':
    main()
