LoRA fine-tuning workspace

This folder contains a minimal LoRA fine-tuning setup targeting natural-language → GitHub-search-query mapping.

Files:
- `requirements.txt`: Python dependencies.
- `github_queries.jsonl`: sample dataset of instruction → query pairs (20 examples).
- `train.py`: training script (PEFT + Transformers + Trainer).
- `inference.py`: quick inference script to load the merged model on CPU.

Quick setup (recommended within a conda env):

```bash
conda create -n lora-env python=3.10 -y
conda activate lora-env
pip install -r requirements.txt
```

VS Code (Windows) terminal tip:

If your VS Code integrated terminal isn't running inside an activated conda prompt, you can still run commands reliably with `conda run`:

```powershell
./conda_run.ps1 lora-env python -V
./conda_run.ps1 lora-env python train.py --help
```

Train (example):

```bash
python train.py \
  --model_name "gpt2" \
  --data_file github_queries.jsonl \
  --output_dir ./lora-output \
  --num_train_epochs 3 \
  --per_device_train_batch_size 2

Train Phi-4-mini-instruct (example, CUDA + 8-bit):

`--load_in_8bit` still works, but it now explicitly requires CUDA (it will error on CPU).

```powershell
./conda_run.ps1 lora-env python train.py `
  --model_name microsoft/Phi-4-mini-instruct `
  --data_file github_queries.generated.jsonl `
  --output_dir ./lora-output/phi4-mini `
  --device cuda `
  --load_in_8bit `
  --num_train_epochs 3 `
  --per_device_train_batch_size 1
```

If LoRA module auto-detection fails for a model, run:

```powershell
./conda_run.ps1 lora-env python train.py --model_name microsoft/Phi-4-mini-instruct --list_modules
```

…then re-run training with `--target_modules module1,module2,...`.
```

This script will:
- Load a base causal LM from Hugging Face (`--model_name`).
- Wrap it with LoRA adapters configured via CLI args.
- Train using `transformers.Trainer` with a simple causal-LM data collator.
- Merge LoRA adapters and save the merged model under `--output_dir/merged`.

Inference (after training):

```bash
python inference.py --model_dir ./lora-output/merged --instruction "Find repos about machine learning in Python"
```

Generate more training data with Foundry Local (teacher model):

Prereqs:
- Install Foundry Local.
- Verify Foundry Local works and a model can run:

  ```powershell
  foundry model list
  foundry model run qwen2.5-0.5b
  ```

- Install Python deps (see `requirements.txt`).

Run:

This appends to a JSONL file of `{ "instruction": "...", "output": "..." }` examples using Foundry Local as the teacher model.

```powershell
./conda_run.ps1 lora-env python generate_dataset_foundry.py --help
```

Example:

```powershell
./conda_run.ps1 lora-env python generate_dataset_foundry.py --alias qwen2.5-0.5b --count 500 --out_file github_queries.generated.jsonl --log_rejects
```

`--count` is the number of new valid examples to add in this run.

Outputs:
- `github_queries.generated.jsonl` (valid examples only)
- `github_queries.generated.jsonl.rejects.jsonl` (optional; written when `--log_rejects` is set)

Tips:
- If you want more variety, bump `--temperature` slightly (e.g. `0.4`) and increase `--max_attempts`.
- If Foundry Local is still starting up or loading the model, transient errors can happen; the script retries and records failures in rejects when enabled.

Packaging model artifacts (recommended for app runtime download):

The app's download queue is single-artifact. The easiest distribution is a single `.zip` containing the Transformers folder contents (`config.json`, `tokenizer.json`, `*.safetensors`, etc).

```powershell
./conda_run.ps1 lora-env python pack_model_zip.py --model_dir ./lora-output/merged --out_zip ./lora-model.zip
```

This prints `Bytes` and `SHA256` which you can use as `ExpectedBytes` / `ExpectedSha256` when wiring a `DownloadUri` in the app.

Notes:
- `--load_in_8bit` is off by default. Enable it only when running with CUDA + bitsandbytes.
- `train.py` is conservative in token lengths; adjust `--max_length` if your prompts are longer.
