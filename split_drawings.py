"""
split_drawings.py

Makes an "unbound" copy of every drawing (layout tab) in a master DWG.

For each layout in the master file, going down the list in tab order, it:
  1. Copies (copy/paste) the master DWG file.
  2. Opens the copy in AutoCAD and deletes every layout except that one.
  3. Saves the copy with the name of the drawing it kept (e.g. "A-101.dwg").

The master file is never modified.

Requirements:
  - Windows with AutoCAD installed (it is driven through COM).
  - Python 3 with pywin32:  pip install pywin32

Usage:
  python split_drawings.py "C:\\Jobs\\Master.dwg"
  python split_drawings.py "C:\\Jobs\\Master.dwg" --out "C:\\Jobs\\Sheets"
  python split_drawings.py "C:\\Jobs\\Master.dwg" --dry-run
"""

import argparse
import os
import re
import shutil
import sys
import time

try:
    import pythoncom
    import win32com.client
except ImportError:
    sys.exit("pywin32 is required: pip install pywin32")


# AutoCAD rejects COM calls while it is busy; retry instead of failing.
RETRY_ERRORS = (-2147418111, -2147417846)  # RPC_E_CALL_REJECTED, RPC_E_SERVERCALL_RETRYLATER


def retry(func, *args, attempts=20, delay=0.5):
    for i in range(attempts):
        try:
            return func(*args)
        except pythoncom.com_error as e:
            if e.hresult in RETRY_ERRORS and i < attempts - 1:
                time.sleep(delay)
                continue
            raise


def safe_filename(name):
    """Strip characters Windows does not allow in file names."""
    cleaned = re.sub(r'[<>:"/\\|?*]', "_", name).strip().rstrip(".")
    return cleaned or "Unnamed"


def get_layout_names(acad, master_path):
    """Return the paper space layout names in tab order (skips Model)."""
    doc = retry(acad.Documents.Open, master_path, True)  # read-only
    try:
        layouts = [
            (layout.TabOrder, layout.Name)
            for layout in doc.Layouts
            if layout.Name.lower() != "model"
        ]
    finally:
        retry(doc.Close, False)
    return [name for _, name in sorted(layouts)]


def keep_only_layout(doc, keep_name):
    """Delete every paper space layout in doc except keep_name."""
    # The active layout cannot be deleted, so switch to the one being kept.
    retry(setattr, doc, "ActiveLayout", doc.Layouts.Item(keep_name))

    to_delete = [
        layout.Name
        for layout in doc.Layouts
        if layout.Name.lower() not in ("model", keep_name.lower())
    ]
    for name in to_delete:
        retry(doc.Layouts.Item(name).Delete)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("master", help="Path to the master DWG containing all the drawings")
    parser.add_argument("--out", help="Output folder (default: a 'Split' folder next to the master)")
    parser.add_argument("--overwrite", action="store_true", help="Replace output files that already exist")
    parser.add_argument("--dry-run", action="store_true", help="List what would be created without doing it")
    args = parser.parse_args()

    master = os.path.abspath(args.master)
    if not os.path.isfile(master):
        sys.exit(f"File not found: {master}")

    out_dir = os.path.abspath(args.out or os.path.join(os.path.dirname(master), "Split"))
    os.makedirs(out_dir, exist_ok=True)

    acad = win32com.client.Dispatch("AutoCAD.Application")
    acad.Visible = True

    names = get_layout_names(acad, master)
    if not names:
        sys.exit("No layouts found in the master drawing.")

    print(f"Found {len(names)} drawing(s) in {os.path.basename(master)}")

    made, skipped, failed = 0, 0, []
    for i, name in enumerate(names, 1):
        target = os.path.join(out_dir, safe_filename(name) + ".dwg")
        print(f"[{i}/{len(names)}] {name} -> {target}")

        if args.dry_run:
            continue
        if os.path.exists(target) and not args.overwrite:
            print("    exists, skipping (use --overwrite to replace)")
            skipped += 1
            continue

        try:
            # 1. Copy/paste the master file.
            shutil.copy2(master, target)

            # 2. Open the copy and delete every drawing but this one.
            doc = retry(acad.Documents.Open, target, False)
            try:
                keep_only_layout(doc, name)
                # 3. Save it; the file already carries the drawing's name.
                retry(doc.Save)
            finally:
                retry(doc.Close, False)
            made += 1
        except Exception as e:  # keep going down the list
            print(f"    FAILED: {e}")
            failed.append(name)
            if os.path.exists(target):
                os.remove(target)

    print(f"\nDone. Created {made}, skipped {skipped}, failed {len(failed)}.")
    for name in failed:
        print(f"  failed: {name}")


if __name__ == "__main__":
    main()
