"""Offline environment isolation, model profiles and actual CPU scheduler checks."""
from pathlib import Path
import ast
import io
import tempfile
import types
import unittest
from unittest.mock import patch

import test_manual_skin as manual

worker = manual.worker


class EnvironmentTests(unittest.TestCase):
    def test_environment_defaults_legacy_backend_and_invalid_selection(self):
        self.assertEqual(worker.inference_environment(), 'cuda')
        self.assertEqual(worker.inference_environment({'backend': 'cpu'}), 'cpu')
        self.assertEqual(worker.inference_environment({'environment': 'cuda', 'backend': 'cpu'}), 'cuda')
        with self.assertRaises(ValueError):
            worker.inference_environment({'environment': 'unknown'})

    def test_cpu_probe_ignores_nvidia_and_marks_cuda_wheel_for_repair(self):
        for cuda_build, expected in ((None, []), ('12.8', ['torch CPU'])):
            fake = types.SimpleNamespace(version=types.SimpleNamespace(cuda=cuda_build),
                                         cuda=types.SimpleNamespace(is_available=lambda: self.fail('CPU probe accessed CUDA')))
            events = []
            with patch.object(worker.importlib.metadata, 'version', side_effect=lambda name: worker.VERSIONS[name]), \
                    patch.dict('sys.modules', torch=fake), patch.object(worker, 'has_nvidia', side_effect=AssertionError('CPU probe enumerated GPUs')), \
                    patch.object(worker, 'emit', side_effect=lambda event, **values: events.append(values)):
                worker.probe({'environment': 'cpu'})
            self.assertEqual(events[-1]['missing'], expected)
            self.assertFalse(events[-1]['cuda'])
            self.assertEqual(events[-1]['device'], 'CPU')

    def test_bootstrap_uses_separate_runtimes_and_shared_weights(self):
        def packed(files):
            stream = io.BytesIO()
            with worker.zipfile.ZipFile(stream, 'w') as archive:
                for name, content in files.items():
                    archive.writestr(name, content)
            return stream.getvalue()
        python_zip = packed({'python.exe': b'python executable', 'python311.dll': b'python library',
                             'python311.zip': b'stdlib', 'python311._pth': b'old paths'})
        pip_zip = packed({'pip/__init__.py': b'pip', 'pip/__main__.py': b'launcher'})
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            weights = [root / 'base/model.safetensors', root / 'controlnet/model.safetensors']
            for target in weights:
                target.parent.mkdir(parents=True, exist_ok=True)
                target.write_bytes(b'shared verified weight')
            before = [(target.read_bytes(), target.stat().st_mtime_ns) for target in weights]
            cuda_runtime, cpu_runtime = root / 'runtime', root / 'runtime/cpu'
            transfers = []
            def downloaded(url, target, expected):
                transfers.append(Path(target))
                Path(target).write_bytes(python_zip if url == worker.PYTHON_URL else pip_zip)
            with patch.object(worker, 'download', side_effect=downloaded), patch.object(worker, 'dependency_mirror', return_value=True), \
                    patch.object(worker, 'has_nvidia', return_value=True), patch.object(worker, 'pip') as install, \
                    patch.object(worker.importlib.metadata, 'version', side_effect=lambda name: worker.VERSIONS[name]), patch.object(worker, 'emit'):
                worker.bootstrap({'runtime': str(cuda_runtime), 'environment': 'cuda', 'models': str(root)})
                cuda_executable = cuda_runtime / 'python/python.exe'
                cuda_time = cuda_executable.stat().st_mtime_ns
                worker.bootstrap({'runtime': str(cpu_runtime), 'environment': 'cpu', 'models': str(root)})
                self.assertEqual(cuda_executable.stat().st_mtime_ns, cuda_time)
                calls = install.call_args_list
                self.assertEqual(Path(calls[0].args[0]), cuda_executable)
                self.assertIn('torch==2.8.0+cu128', calls[0].args[1])
                self.assertIn('https://download.pytorch.org/whl/cu128', calls[0].args[1])
                self.assertEqual(Path(calls[2].args[0]), cpu_runtime / 'python/python.exe')
                self.assertIn('torch==2.8.0+cpu', calls[2].args[1])
                self.assertIn('https://download.pytorch.org/whl/cpu', calls[2].args[1])
                # Repeated preparations repair only this runtime's own files.
                with patch.object(worker.os, 'replace', side_effect=AssertionError('Valid runtime rewritten')):
                    worker.bootstrap({'runtime': str(cpu_runtime), 'environment': 'cpu', 'models': str(root)})
            self.assertEqual([(target.read_bytes(), target.stat().st_mtime_ns) for target in weights], before)
            self.assertEqual(len(list(root.rglob('*.safetensors'))), 2)
            self.assertTrue(all(target.name in ('python.zip', 'pip.whl') for target in transfers))
            self.assertTrue(all(target.parent in (cuda_runtime, cpu_runtime) for target in transfers))


class ProfileTests(unittest.TestCase):
    @staticmethod
    def arguments():
        return dict(prompt='blue and brown original woven fabric in an illustrated city', negative_prompt='face, new garment',
                    num_inference_steps=24, guidance_scale=5.5, strength=.42)

    def test_no_profile_and_sd15_builtins_preserve_automatic_values(self):
        arguments = self.arguments()
        self.assertIs(worker.profile_arguments(arguments, worker.generation_profile({}, 'sd15')), arguments)
        for identifier in ('sd15-standard', 'dreamshaper-8'):
            profile = worker.generation_profile({'generation_profile': {'id': identifier}}, 'sd15')
            self.assertEqual(worker.profile_arguments(arguments, profile), arguments)

    def test_sdxl_builtin_wraps_existing_material_semantics_once(self):
        for identifier in ('sdxl-standard', 'sdxl-base-1.0'):
            profile = worker.generation_profile({'generation_profile': {'id': identifier, 'prompt_template': '', 'negative_template': ''}}, 'sdxl')
            arguments = self.arguments()
            adapted = worker.profile_arguments(arguments, profile)
            self.assertTrue(adapted['prompt'].startswith(arguments['prompt']))
            self.assertEqual(adapted['prompt'].count(arguments['prompt']), 1)
            self.assertIn('clothing design', adapted['prompt'])
            self.assertEqual(adapted['negative_prompt'], arguments['negative_prompt'])
            self.assertEqual(adapted['strength'], arguments['strength'])

    def test_custom_text_only_for_auto_prompt_and_parameters_for_each_phase(self):
        profile = worker.generation_profile({'generation_profile': dict(id='custom', prompt_template='Existing texture: {prompt}',
            negative_template='{negative}, unnatural seams', steps=30, guidance=6.5, strength=.65, clip_skip=2)}, 'sd15')
        for phase in ('background', 'main', 'regional', 'two-stage', 'polish', 'natural-refine'):
            arguments = self.arguments()
            arguments['phase'] = phase
            actual = worker.profile_arguments(arguments, profile)
            self.assertEqual(actual['prompt'], 'Existing texture: ' + arguments['prompt'])
            self.assertEqual(actual['negative_prompt'], arguments['negative_prompt'] + ', unnatural seams')
            self.assertEqual((actual['num_inference_steps'], actual['guidance_scale'], actual['strength'], actual['clip_skip']), (30, 6.5, .65, 2))
            self.assertEqual(arguments['strength'], .42)
        manual_prompt = worker.profile_arguments(self.arguments(), profile, auto_prompt=False)
        self.assertEqual(manual_prompt['prompt'], self.arguments()['prompt'])
        self.assertEqual(manual_prompt['negative_prompt'], self.arguments()['negative_prompt'])
        self.assertEqual(manual_prompt['num_inference_steps'], 30)
        self.assertEqual(worker.profile_arguments(self.arguments(), profile, skin_pass=True)['strength'], .28)
        text_only = self.arguments(); del text_only['strength']
        self.assertNotIn('strength', worker.profile_arguments(text_only, profile))

    def test_parameter_validation_rejects_unsupported_and_nonfinite_values(self):
        invalid = [('steps', 7), ('steps', 61), ('steps', 10.5), ('guidance', .5), ('guidance', 13),
                   ('guidance', float('nan')), ('strength', .1), ('strength', .9), ('strength', float('inf')),
                   ('clip_skip', 3), ('clip_skip', -1), ('steps', True), ('steps', '30')]
        for key, value in invalid:
            with self.subTest(key=key, value=value), self.assertRaises(ValueError):
                worker.generation_profile({'generation_profile': {'id': 'custom', key: value}}, 'sd15')
        for configured, family in (({'id': 'dreamshaper-8'}, 'sdxl'), ({'id': 'turbo'}, 'sdxl'),
                ({'id': 'custom', 'clip_skip': 1}, 'sdxl'), ({'id': 'custom', 'prompt_template': 'Replace source with portrait'}, 'sd15'),
                ({'id': 'custom', 'negative_template': 'only new exclusions'}, 'sd15'),
                ({'id': 'custom', 'prompt_template': '{prompt} {unknown}'}, 'sd15')):
            with self.subTest(configured=configured), self.assertRaises(ValueError):
                worker.generation_profile({'generation_profile': configured}, family)
        for steps, guidance, strength, clip in ((8, 1, .15, 0), (60, 12, .85, 2)):
            worker.generation_profile({'generation_profile': dict(id='custom', steps=steps, guidance=guidance, strength=strength, clip_skip=clip)}, 'sd15')

    def test_generate_dispatches_profile_and_keeps_skin_limit(self):
        profile = dict(id='custom', prompt_template='Local existing material: {prompt}', negative_template='{negative}, visible pasted edges',
                       steps=32, guidance=6, strength=.8, clip_skip=1)
        helper = manual.ManualSkinTests()
        with patch.object(worker, 'caption', return_value='original illustrated skin texture'):
            calls, events, output, source, placements = helper.run_generate(dict(auto_place=False, manual_skin=True,
                auto_prompt=True, environment='cpu', generation_profile=profile))
        self.assertEqual(placements, 0)
        self.assertEqual(len(calls), 1)
        actual = calls[0]
        self.assertTrue(actual['prompt'].startswith('Local existing material: '))
        self.assertIn('original skin', actual['prompt'])
        self.assertEqual((actual['num_inference_steps'], actual['guidance_scale'], actual['clip_skip'], actual['strength']), (32, 6, 1, .28))
        self.assertEqual(output.size, source.size)
        caption_prompt = next(values['prompt'] for event, values in events if event == 'caption')
        result_prompt = next(values['prompt'] for event, values in events if event == 'result')
        self.assertTrue(caption_prompt.startswith('Local existing material: '))
        self.assertEqual(result_prompt, caption_prompt)

    def test_all_diffusion_invocations_dispatch_profiles(self):
        tree = ast.parse(Path(worker.__file__).read_text(encoding='utf-8'))
        generate = next(item for item in tree.body if isinstance(item, ast.FunctionDef) and item.name == 'generate')
        calls = [item for item in ast.walk(generate) if isinstance(item, ast.Call)]
        self.assertFalse(any(isinstance(item.func, ast.Name) and item.func.id == 'pipe' for item in calls))
        self.assertEqual(sum(isinstance(item.func, ast.Name) and item.func.id == 'infer' for item in calls), 6)


class CpuSchedulerTests(unittest.TestCase):
    def test_real_cpu_dpm_preserves_each_prediction_mode_and_finite_output(self):
        import torch
        from diffusers import DDIMScheduler, DPMSolverMultistepScheduler
        with patch.object(torch.cuda, 'mem_get_info', side_effect=AssertionError('CPU scheduler accessed GPU memory')):
            for prediction in ('epsilon', 'v_prediction', 'sample'):
                with self.subTest(prediction=prediction):
                    scheduler = DPMSolverMultistepScheduler.from_config(DDIMScheduler(prediction_type=prediction).config,
                                                                       algorithm_type='dpmsolver++', use_karras_sigmas=True)
                    scheduler.set_timesteps(4, device='cpu')
                    sample = torch.zeros((1, 4, 8, 8), device='cpu')
                    for timestep in scheduler.timesteps:
                        sample = scheduler.step(torch.zeros_like(sample), timestep, sample).prev_sample
                    self.assertEqual(scheduler.config.prediction_type, prediction)
                    self.assertEqual(sample.device.type, 'cpu')
                    self.assertTrue(torch.isfinite(sample).all().item())
                    self.assertEqual(worker.supports_scanning_guidance(scheduler), prediction == 'epsilon')
                    if prediction != 'epsilon':
                        # No QR parsing/VAE decoding is permitted for these modes.
                        self.assertIs(worker.scanning_scheduler(types.SimpleNamespace(scheduler=scheduler), None, None), scheduler)

    def test_actual_epsilon_guided_step_runs_entirely_on_cpu(self):
        import numpy as np
        import torch
        import torch.nn.functional as functional
        from PIL import Image
        from diffusers import DDIMScheduler
        bits = np.indices((21, 21)).sum(0) % 2
        for row, column in ((0, 0), (0, 14), (14, 0)):
            bits[row:row+7, column:column+7] = 0
            bits[row+1:row+6, column+1:column+6] = 1
            bits[row+2:row+5, column+2:column+5] = 0
        grid = np.full((200, 200), 128, dtype=np.uint8)
        grid[16:184, 16:184] = np.repeat(np.repeat(bits * 255, 8, axis=0), 8, axis=1)
        condition = Image.fromarray(grid).convert('RGB')
        vae = types.SimpleNamespace(parameters=lambda: (), config=types.SimpleNamespace(scaling_factor=1.))
        vae.decode = lambda latent, **kwargs: (functional.interpolate(latent[:, :3], size=(64, 64), mode='bilinear', align_corners=False),)
        pipe = types.SimpleNamespace(scheduler=DDIMScheduler(prediction_type='epsilon'), vae=vae)
        with patch.object(worker, 'emit'), patch.object(torch.cuda, 'mem_get_info', side_effect=AssertionError('CPU guidance accessed GPU memory')):
            guided = worker.scanning_scheduler(pipe, condition, Image.new('RGB', condition.size, (150, 160, 175)), scale=1., edge=256)
            self.assertIsNot(guided, pipe.scheduler)
            guided.set_timesteps(4, device='cpu')
            sample = torch.zeros((1, 4, 8, 8), device='cpu')
            result = guided.step(torch.zeros_like(sample), guided.timesteps[0], sample)
        self.assertEqual(result.prev_sample.device.type, 'cpu')
        self.assertTrue(torch.isfinite(result.prev_sample).all().item())


if __name__ == '__main__':
    unittest.main()
