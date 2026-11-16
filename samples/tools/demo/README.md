# Demo recording

Scripts that record the sample-app overview video embedded in the docs (`docs/static/video/kvbind-demo.mp4`).

`record.mjs` drives the UI with Playwright and captures Chrome's screencast frames at 1.5× scale (1920×1200). It also injects a visible cursor, captions and title cards into the page. Bob's concurrent commit goes through the API. `build.sh` turns the frames into a 30 fps H.264 mp4 with ffmpeg.

Requirements: Node 20+, Chromium (default `/usr/bin/chromium`; set `CHROMIUM` for another path), `ffmpeg` and `python3`. The sample app must be running (see `samples/README.md`).

```bash
cd samples/tools/demo
npm ci
npm run record       # records into out/, then replaces the docs video and its poster image
```

Each run creates a new underwriting application (`UW-xxxx`) in the sample database. `PACE` scales the pauses (default `0.82`). `UI` and `API` override the URLs. The UI on `:5200` is a container built when the AppHost starts. To record UI changes without restarting the AppHost, run `npx ng serve` in `x86cc.KVBind.Sample.UI` and record with `UI=http://localhost:4200 npm run record`.

The recording uses the UI's default dark theme and hides the custom-operations bar (`[data-operations]`). The storyline (steps, captions, timing) lives in `record.mjs`; edit it there and re-run.
