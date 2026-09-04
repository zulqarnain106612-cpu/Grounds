from __future__ import annotations
import argparse
import json
import sys

from .gateway import handle


def main() -> int:
    parser = argparse.ArgumentParser(description="JetFighter agent gateway CLI")
    parser.add_argument("request_file", nargs="?", help="path to a JSON request file; omit to read stdin")
    args = parser.parse_args()

    raw = open(args.request_file).read() if args.request_file else sys.stdin.read()
    try:
        request = json.loads(raw)
    except json.JSONDecodeError as e:
        print(json.dumps({"response": {"status": "error", "errors": [
            {"code": "invalid_json", "message": str(e), "fatal": True}
        ]}}))
        return 1

    response = handle(request)
    print(json.dumps(response, indent=2))
    return 0 if response.get("response", {}).get("status") == "ok" else 1


if __name__ == "__main__":
    raise SystemExit(main())
