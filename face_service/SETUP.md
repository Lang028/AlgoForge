# Setting up the face service on a new machine

Step by step, for the Python side. The web app needs nothing beyond the .NET SDK — this
guide is only for `face_service`, which does the face detection and clustering.

Everything here happens **inside the `face_service` folder**.

---

## 0. Before you start

You need **Python 3.11 or newer**. Check what you have:

```bash
python --version
```

If that prints `Python 3.11.x` or higher, skip to step 1.

If it says "not recognized", or prints 3.10 or lower, install Python from
<https://www.python.org/downloads/>. **On the installer's first screen, tick
"Add python.exe to PATH"** before clicking Install — without it every command below
fails with "python is not recognized". Close and reopen your terminal afterwards.

You also need about **4GB of free disk** and a decent internet connection. The downloads
are large and explained at each step.

---

## 1. Open a terminal in the right folder

From the repo root:

```bash
cd face_service
```

Everything below assumes you are in that folder. If a command fails with "file not found",
check you are in `face_service` and not the repo root.

---

## 2. Create the virtual environment

A venv keeps this project's packages separate from the rest of your machine, so nothing
here can break your other Python work.

```bash
python -m venv .venv
```

Takes a few seconds and prints nothing. You now have a `.venv` folder. It is gitignored —
it lives on your machine only, which is why you have to create it here rather than getting
it from the clone.

---

## 3. Activate it

**Windows (PowerShell):**

```bash
.venv\Scripts\Activate.ps1
```

**Windows (Command Prompt):**

```bash
.venv\Scripts\activate.bat
```

**macOS / Linux:**

```bash
source .venv/bin/activate
```

Your prompt should now start with `(.venv)`. **That prefix is how you know it worked.**

> If PowerShell refuses with "running scripts is disabled on this system", run this once
> and try again:
> ```bash
> Set-ExecutionPolicy -Scope CurrentUser RemoteSigned
> ```

You must activate the venv **every time you open a new terminal** to work on this. If you
ever see packages missing that you know you installed, check for the `(.venv)` prefix
first — nine times out of ten that is the answer.

---

## 4. Update pip

Old pip versions fail on some of these packages with confusing errors.

```bash
python -m pip install --upgrade pip
```

---

## 5. Install PyTorch — do this BEFORE the next step

**Order matters here.** `requirements.txt` lists `torch`, and if you let it install torch
by itself, pip downloads the CUDA (NVIDIA GPU) build: several extra gigabytes you do not
need, and it will not help unless the machine has a supported NVIDIA card.

Installing the CPU build first means the next step sees torch is already present and
leaves it alone.

```bash
pip install torch==2.5.1 --index-url https://download.pytorch.org/whl/cpu
```

**This downloads roughly 2GB and is the slowest step.** Several minutes on a good
connection, considerably longer on a poor one. It is normal for it to sit on
"Downloading torch-2.5.1..." for a long while.

---

## 6. Install everything else

```bash
pip install -r requirements.txt
```

A few hundred MB. This brings in FastAPI and uvicorn (the web server), InsightFace (face
detection and recognition), ONNX Runtime, OpenCV, and YOLO for person detection.

> **If this fails on `torchreid`**, that package is fussy and it is optional. Open
> `requirements.txt`, put a `#` in front of the `torchreid==0.2.5` line, remove the `#`
> from the `open_clip_torch==2.30.0` line near the bottom, and run the command again. The
> service detects which one is present and uses it — no code change needed.

---

## 7. Start the service

```bash
uvicorn main:app --port 8000
```

**The first start is slow — this is expected and only happens once.** InsightFace
downloads its `buffalo_l` models, about 300MB, into your home folder (`~/.insightface`).
You will see download progress, then lines about loading models, and finally:

```
INFO:     Uvicorn running on http://127.0.0.1:8000 (Press CTRL+C to quit)
```

That last line means it is ready. Every start after this is a few seconds, and works
offline.

**Leave this terminal open.** The service runs in the foreground; closing the window or
pressing Ctrl+C stops it.

---

## 8. Check what actually loaded

Open a **second** terminal (you do not need the venv active for this) and run:

```bash
curl http://127.0.0.1:8000/health
```

You want something like:

```json
{"status": "ok", "signals": ["face", "appearance", "head"]}
```

`"status": "ok"` means the service is up. **The `signals` list is the part worth reading**
— it tells you which models really loaded:

| Signal present | Meaning |
|---|---|
| `face` | **Required.** Without this nothing can be tagged at all. |
| `appearance`, `head` | Optional. Their absence is silent but noticeably worsens grouping of profiles and back-of-head shots. |

If `face` is missing, the service is running but useless — check the startup terminal for
an error and fix that before uploading anything.

---

## 9. Now run the web app

With the service still running in its terminal, start the web app from the repo root — in
Visual Studio, or:

```bash
dotnet run --project AlgoForge
```

> The README says `--project AlgoForge/AlgoForge`, which is wrong: the project file is at
> `AlgoForge/AlgoForge.csproj`, so there is no nested folder of that name. Use `AlgoForge`
> (the folder) or `AlgoForge/AlgoForge.csproj`.

**Start the face service before uploading photos.** The web app calls it during upload; if
it is not running the photos are still stored, but with nobody detected in them, and you
would have to re-cluster the event afterwards to fix it.

---

## Every time after this

Setup is once. Day to day it is two terminals:

**Terminal 1 — face service:**
```bash
cd face_service
.venv\Scripts\Activate.ps1
uvicorn main:app --port 8000
```

**Terminal 2 — web app**, or just press F5 in Visual Studio:
```bash
dotnet run --project AlgoForge
```

---

## When something is wrong

**"python is not recognized"** — Python is not on PATH. Reinstall it with "Add python.exe
to PATH" ticked, then open a new terminal.

**"uvicorn is not recognized"** — the venv is not active. Look for `(.venv)` in your
prompt and redo step 3.

**"Port 8000 is already in use"** — the service is already running in another window.
Either use that one, or start this on a different port with `--port 8001`, and change
`PersonPipeline:BaseUrl` in `AlgoForge/appsettings.json` to match.

**Uploads work but nobody is ever detected** — the web app cannot reach the service. Check
it is running, then check `/health` responds, then check `PersonPipeline:BaseUrl` points at
the right port.

**First upload is very slow** — the service loads models on first use. Subsequent uploads
are much quicker.

---

## What does not come from the clone, and why

- **`.venv/`** — machine-specific, so you build it in step 2.
- **`embeddings/`** — these are biometric face vectors. Deliberately never committed. They
  regenerate when you re-cluster an event.
- **InsightFace models** — downloaded to your home folder on first run, shared across
  projects rather than stored per-repo.

`yolo11s.pt` (19MB) **is** committed, so that one arrives with the clone.
