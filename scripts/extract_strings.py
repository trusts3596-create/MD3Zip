"""扫描源码中的中文字符串字面量，输出去重清单（供翻译字典使用）。"""
import os, re, json

ROOT = r"D:\wb"
BASES = ["src/MD3Zip.App", "src/MD3Zip.Core", "src/MD3Zip.Setup", "src/MD3Zip.ShellExtension"]

targets = []
for base in BASES:
    for dp, dn, fn in os.walk(os.path.join(ROOT, base)):
        parts = dp.replace("\\", "/").lower()
        if "/obj" in parts or "/bin" in parts:
            continue
        for f in fn:
            if f.endswith((".cs", ".axaml")):
                targets.append(os.path.join(dp, f))

# 匹配双引号字符串（含转义）
pat = re.compile(r'"((?:[^"\\\r\n]|\\.)*)"')
han = re.compile(r"[\u4e00-\u9fff]")

strings = set()
for path in targets:
    with open(path, encoding="utf-8-sig") as fh:
        text = fh.read()
    for m in pat.finditer(text):
        s = m.group(1)
        if han.search(s):
            strings.add(s)

items = sorted(strings)
out = os.path.join(ROOT, ".tools", "zh_strings.json")
with open(out, "w", encoding="utf-8") as fh:
    json.dump({s: "" for s in items}, fh, ensure_ascii=False, indent=2)

print(f"扫描 {len(targets)} 个文件，提取 {len(items)} 条唯一中文字符串 -> {out}")
for s in items[:40]:
    print("  " + s)
