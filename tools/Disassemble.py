"""Decode a bounded, already acquired x64 code snippet; never opens a process."""
import argparse
from pathlib import Path

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--input', required=True)
parser.add_argument('--base', type=lambda v: int(v,0), required=True)
args = parser.parse_args()
try:
    from capstone import Cs, CS_ARCH_X86, CS_MODE_64
except ImportError:
    parser.error('Optional dependency missing: create a Python venv and install capstone==5.0.6; see docs/investigation.md.')
source = Path(args.input)
if source.stat().st_size > 16384:
    parser.error('Only bounded snippets up to 16384 bytes are accepted.')
for instruction in Cs(CS_ARCH_X86,CS_MODE_64).disasm(source.read_bytes(),args.base):
    print(f'{instruction.address:016x} {instruction.mnemonic:10} {instruction.op_str}')
