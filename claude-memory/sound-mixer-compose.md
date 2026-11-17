---
name: sound-mixer-compose
description: b381 Sound Mixer is a "station for sound" - Compose renders a new internal sound and clears the mixer, irreversibly
metadata:
  type: project
---

2026-10-20 build 381 (user's design): SoundMixerWindow Compose -> RenderMix (NAudio, 44100 stereo 16-bit WAV, volume x column volume, per-sound Starts after, loops fill the length of the longest non-looping sound, 10 min cap) into %TEMP%/SandcastleRpgMaker/mixes, a new ResourceEntry (IsMix, shown "Composed in the Sound Mixer"), embedded into audios.sdbx at the next Save. The mixer is then emptied; source sounds stay in the list; a mix can't be put back. Preview Mix renders preview.wav and plays it.

**Why:** user sees the mixer like a crafting station - sounds in, a new sound out, never an external file.
**How to apply:** keep mixes internal-only (no export path in the UI). Related: [[work-schedules]].
