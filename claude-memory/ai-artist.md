---
name: ai-artist
description: "Sprite generator B (AI Artist) - local Stable Diffusion 1.5 via ONNX/DirectML in SandcastleRpgMaker.AI.dll, model at H:\\SandcastleAI\\sd15-fp16; b671"
metadata:
  node_type: memory
  type: project
  originSessionId: e02e316e-a1c8-42c0-a7f7-69326088b5ec
  modified: 2026-11-24T19:59:49.972Z
---

Sprite generator B done in b671 (2026-11-24): Tools > AI Artist.

- `AI/` project = SandcastleRpgMaker.AI.dll (Microsoft.ML.OnnxRuntime.DirectML 1.24.4): ClipTokenizer, StableDiffusion (Euler, CFG, img2img via vae_encoder), SpriteMaker (background flood fill, keep biggest piece, shrink, median cut / EGA16 with level stretch, outline).
- Model: sharpbai/stable-diffusion-v1-5-onnx-directml-fp16 (SD 1.5, CreativeML OpenRAIL-M) downloaded to H:\SandcastleAI\sd15-fp16 (NOT in git, 2.1 GB). The user approved the download ("50000000000% ok") and takes responsibility for copyright.
- DML-optimised model: it can NOT load on the CPU provider (BiasSplitGelu kernel) - only on DirectML.
- The RTX 3050 does 25 steps at 512x512 in about 6 s; the model loads in 5-20 s.
- DirectML.dll comes with Windows 11 (no copy in the output folder).
- Test harness: H:\_sandcastle_test_temp\sdtest (dotnet run -- out.bmp steps "prompt").
- Model folder setting: %AppData%\SandcastleRpgMaker\ai-artist.txt. Pictures are saved to Documents\Sandcastle AI Artist.
- Still to do: sprite generator A (skeleton + parts on bones, U6/U7/U8 libraries); A can then cut AI pictures into parts. Ideas: directions/poses, LoRA for pixel art.

Related: [[issues-odt-2026-11-23]]
