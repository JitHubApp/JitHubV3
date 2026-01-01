#!/usr/bin/env python3
"""
LoRA fine-tuning script for GitHub search query generation.
Trains a small model to map natural language → GitHub search queries.
"""
import argparse
import logging
import os
from datasets import load_dataset
from prompting import format_prompt, format_training_text
from transformers import (
    AutoTokenizer,
    AutoModelForCausalLM,
    Trainer,
    TrainingArguments,
    DataCollatorForLanguageModeling,
)
import torch
from peft import LoraConfig, get_peft_model, prepare_model_for_kbit_training

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


def tokenize_function(examples, tokenizer, max_length):
    """Tokenize examples for training.

    Uses label masking so loss is computed only on the response.
    """
    prompts = [format_prompt(i) for i in examples["instruction"]]
    full_texts = [format_training_text(i, o) for i, o in zip(examples["instruction"], examples["output"]) ]

    full = tokenizer(full_texts, padding="max_length", truncation=True, max_length=max_length)
    prompt_only = tokenizer(prompts, padding="max_length", truncation=True, max_length=max_length)

    labels = []
    for input_ids, attention_mask in zip(full["input_ids"], prompt_only["attention_mask"]):
        prefix_len = int(sum(attention_mask))
        lab = list(input_ids)
        for j in range(min(prefix_len, max_length)):
            lab[j] = -100
        labels.append(lab)

    full["labels"] = labels
    return full


def main():
    parser = argparse.ArgumentParser(description="LoRA fine-tuning for GitHub search queries")
    parser.add_argument("--model_name", type=str, default="gpt2", help="Base model name/path from Hugging Face")
    parser.add_argument("--data_file", type=str, default="github_queries.jsonl", help="JSONL training data file")
    parser.add_argument("--output_dir", type=str, default="./lora-output", help="Output directory for trained model")
    parser.add_argument("--num_train_epochs", type=int, default=3, help="Number of training epochs")
    parser.add_argument("--per_device_train_batch_size", type=int, default=2, help="Batch size per device")
    parser.add_argument("--learning_rate", type=float, default=2e-4, help="Learning rate")
    parser.add_argument("--max_length", type=int, default=128, help="Max token length")
    parser.add_argument("--lora_r", type=int, default=8, help="LoRA rank")
    parser.add_argument("--lora_alpha", type=int, default=16, help="LoRA alpha")
    parser.add_argument("--lora_dropout", type=float, default=0.05, help="LoRA dropout")
    parser.add_argument("--target_modules", type=str, default="auto", help="Comma-separated target modules or 'auto'")
    parser.add_argument("--list_modules", action="store_true", help="List model modules and exit")
    parser.add_argument("--load_in_8bit", action="store_true", help="Use 8-bit quantization (requires bitsandbytes)")
    parser.add_argument("--mixed_precision", type=str, default="fp16", choices=["fp16", "bf16", "fp32"], help="Precision")
    parser.add_argument("--device", type=str, default="auto", choices=["auto", "cuda", "cpu"], help="Training device")
    args = parser.parse_args()

    # Create output directory
    os.makedirs(args.output_dir, exist_ok=True)
    print(f"[*] Output directory: {args.output_dir}")

    # Load dataset
    print(f"[*] Loading dataset from {args.data_file}")
    if not os.path.exists(args.data_file):
        print(f"[!] ERROR: Data file not found: {args.data_file}")
        return
    dataset = load_dataset("json", data_files=args.data_file)
    train_ds = dataset["train"]
    print(f"[*] Loaded {len(train_ds)} training examples")

    # Load tokenizer
    print(f"[*] Loading tokenizer: {args.model_name}")
    tokenizer = AutoTokenizer.from_pretrained(args.model_name, use_fast=True)
    if tokenizer.pad_token_id is None:
        tokenizer.pad_token = tokenizer.eos_token
    print(f"[*] Tokenizer ready")

    # Load model
    print(f"[*] Loading base model: {args.model_name}")
    torch_dtype = torch.float16 if args.mixed_precision == "fp16" else (torch.bfloat16 if args.mixed_precision == "bf16" else torch.float32)
    want_cuda = args.device == "cuda" or (args.device == "auto" and torch.cuda.is_available())
    if args.load_in_8bit and not want_cuda:
        print("[!] ERROR: --load_in_8bit requires CUDA.")
        return

    load_kwargs = {}
    if args.load_in_8bit:
        load_kwargs["load_in_8bit"] = True
        load_kwargs["device_map"] = "auto"
    else:
        load_kwargs["torch_dtype"] = torch_dtype
        load_kwargs["device_map"] = "auto" if want_cuda else {"": "cpu"}

    model = AutoModelForCausalLM.from_pretrained(args.model_name, **load_kwargs)

    print(f"[*] Model loaded")

    # Get all module names for diagnostics
    module_names = [n for n, _ in model.named_modules()]

    # If user wants to list modules, print and exit
    if args.list_modules:
        print("\n" + "=" * 80)
        print("MODEL MODULES (filtered for attention/projection layers):")
        print("=" * 80)
        interesting = [n for n in module_names if any(tok in n for tok in ("attn", "q_proj", "v_proj", "k_proj", "c_attn", "qkv", "proj", "dense", "wq", "wo", "gate", "up_proj", "down_proj"))]
        for n in (interesting[:500] if interesting else module_names[:500]):
            print(f"  {n}")
        print(f"\nTotal: {len(module_names)} modules, {len(interesting)} attention/projection-related")
        print("\nTo use specific modules, run with: --target_modules module1,module2,module3")
        print("=" * 80)
        return

    # Prepare model for k-bit training if needed
    if args.load_in_8bit:
        try:
            model = prepare_model_for_kbit_training(model)
            print(f"[*] Model prepared for 8-bit training")
        except Exception as e:
            print(f"[!] Warning: prepare_model_for_kbit_training failed (may not be needed): {e}")

    # Auto-detect or parse target modules
    print(f"[*] Configuring LoRA target modules")
    
    # Helper function to check if any substring exists in model modules
    def contains_any_in_modules(substrs):
        for substr in substrs:
            for mod_name in module_names:
                if substr in mod_name:
                    return True
        return False
    
    if args.target_modules == "auto":
        # Try common patterns in order
        detected = None
        if contains_any_in_modules(["qkv_proj"]):
            detected = ["qkv_proj", "o_proj"]
        elif contains_any_in_modules(["q_proj", "v_proj"]):
            detected = ["q_proj", "k_proj", "v_proj"]
        elif contains_any_in_modules(["c_attn"]):
            detected = ["c_attn"]
        elif contains_any_in_modules(["gate_proj"]):
            detected = ["gate_proj", "up_proj", "down_proj"]
        else:
            print(f"[!] ERROR: Could not auto-detect target modules. Available modules:")
            for n in module_names[:20]:
                print(f"      {n}")
            print(f"      ... ({len(module_names)} total)")
            print(f"\nRun with --list_modules to see all modules, then specify --target_modules")
            return
        
        target_modules = detected
        print(f"[*] Auto-detected target modules: {target_modules}")
    else:
        # User provided explicit modules
        target_modules = [m.strip() for m in args.target_modules.split(",")]
        # Verify at least one matches
        found_any = contains_any_in_modules(target_modules)
        if not found_any:
            print(f"[!] ERROR: None of specified target modules found in model:")
            print(f"    Requested: {target_modules}")
            print(f"    Available: {module_names[:10]} ... ({len(module_names)} total)")
            return
        print(f"[*] Using specified target modules: {target_modules}")

    # Create LoRA config
    print(f"[*] Creating LoRA config (r={args.lora_r}, alpha={args.lora_alpha}, dropout={args.lora_dropout})")
    lora_config = LoraConfig(
        r=args.lora_r,
        lora_alpha=args.lora_alpha,
        target_modules=target_modules,
        lora_dropout=args.lora_dropout,
        bias="none",
        task_type="CAUSAL_LM",
    )

    # Wrap model with LoRA
    try:
        model = get_peft_model(model, lora_config)
        print(f"[*] LoRA adapters applied to model")
    except Exception as e:
        print(f"[!] ERROR: Failed to apply LoRA: {e}")
        return

    # Tokenize dataset
    print(f"[*] Tokenizing dataset (max_length={args.max_length})")
    tokenized = train_ds.map(
        lambda ex: tokenize_function(ex, tokenizer, args.max_length),
        batched=True,
        remove_columns=train_ds.column_names
    )
    tokenized.set_format(type="torch")
    print(f"[*] Tokenization complete")

    # Setup trainer
    data_collator = DataCollatorForLanguageModeling(tokenizer, mlm=False)
    training_args = TrainingArguments(
        output_dir=args.output_dir,
        num_train_epochs=args.num_train_epochs,
        per_device_train_batch_size=args.per_device_train_batch_size,
        learning_rate=args.learning_rate,
        fp16=(args.mixed_precision == "fp16"),
        bf16=(args.mixed_precision == "bf16"),
        logging_steps=10,
        save_total_limit=2,
        save_strategy="epoch",
        remove_unused_columns=False,
        push_to_hub=False,
    )

    trainer = Trainer(
        model=model,
        args=training_args,
        train_dataset=tokenized,
        data_collator=data_collator,
    )

    # Train
    print(f"[*] Starting training for {args.num_train_epochs} epoch(s)")
    print(f"[*] Batch size: {args.per_device_train_batch_size}, Learning rate: {args.learning_rate}")
    trainer.train()

    # Merge and save
    print(f"[*] Merging LoRA adapters and saving model")
    try:
        merged_model = model.merge_and_unload()
        merged_path = os.path.join(args.output_dir, "merged")
        merged_model.save_pretrained(merged_path)
        tokenizer.save_pretrained(merged_path)
        print(f"[✓] Merged model saved to: {merged_path}")
    except Exception as e:
        print(f"[!] Failed to merge: {e}")
        print(f"[*] Saving adapter separately instead")
        adapter_path = os.path.join(args.output_dir, "adapter")
        model.save_pretrained(adapter_path)
        tokenizer.save_pretrained(adapter_path)
        print(f"[✓] Adapter saved to: {adapter_path}")

    print(f"\n[✓] Training complete!")
    print(f"[*] Output directory: {args.output_dir}")


if __name__ == "__main__":
    main()
