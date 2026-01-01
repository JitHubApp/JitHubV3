import argparse
import torch
from transformers import AutoTokenizer, AutoModelForCausalLM
from prompting import format_prompt, normalize_model_output


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model_dir", type=str, required=True)
    parser.add_argument("--instruction", type=str, default="Find repos about machine learning in Python")
    parser.add_argument("--prompt", type=str, default=None, help="Alias for --instruction")
    parser.add_argument("--max_new_tokens", type=int, default=64)
    parser.add_argument("--device", type=str, default="auto", choices=["auto", "cpu", "cuda"], help="Inference device")
    parser.add_argument("--temperature", type=float, default=0.2)
    parser.add_argument("--top_p", type=float, default=0.95)
    args = parser.parse_args()

    instruction = args.prompt if args.prompt is not None else args.instruction
    prompt = format_prompt(instruction)

    if args.device == "cuda" and not torch.cuda.is_available():
        raise SystemExit("--device cuda requested but CUDA is not available")
    use_cuda = args.device == "cuda" or (args.device == "auto" and torch.cuda.is_available())
    device_map = "auto" if use_cuda else {"": "cpu"}

    tokenizer = AutoTokenizer.from_pretrained(args.model_dir, use_fast=True)
    if tokenizer.pad_token_id is None:
        tokenizer.pad_token = tokenizer.eos_token

    model = AutoModelForCausalLM.from_pretrained(args.model_dir, device_map=device_map)

    first_param = next((p for p in model.parameters() if p is not None), None)
    device = first_param.device if first_param is not None else torch.device("cpu")

    inputs = tokenizer(prompt, return_tensors="pt")
    input_ids = inputs["input_ids"].to(device)
    attention_mask = inputs.get("attention_mask")
    if attention_mask is not None:
        attention_mask = attention_mask.to(device)

    with torch.no_grad():
        outputs = model.generate(
            input_ids=input_ids,
            attention_mask=attention_mask,
            max_new_tokens=args.max_new_tokens,
            do_sample=True,
            temperature=args.temperature,
            top_p=args.top_p,
            pad_token_id=tokenizer.eos_token_id,
        )

    gen_ids = outputs[0][input_ids.shape[1]:]
    text = tokenizer.decode(gen_ids, skip_special_tokens=True)
    print(normalize_model_output(text))


if __name__ == "__main__":
    main()
