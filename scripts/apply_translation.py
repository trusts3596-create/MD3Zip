"""把中文界面文本替换为可翻译标记。
- XAML: Content="中文" -> Content="{loc:T 中文}"
- C#:   "中文" -> L10n.L("中文")，跳过插值字符串与注释行
只处理英文词典中存在的 key，避免误伤路径/正则/日志。
"""
import os, re, json

ROOT = r"D:\wb"
DICT = os.path.join(ROOT, "src", "MD3Zip.App", "Localization", "en-US.json")
with open(DICT, encoding="utf-8") as fh:
    keys = set(json.load(fh).keys())

# 含这些字符的字符串不做 C# 替换（插值 / 转义 / JSON 模板）
unsafe = re.compile(r"[{$}\\]")

# ---------------- XAML ----------------
xaml_attrs = r'(Text|Content|Header|Watermark|ToolTip\.Tip|Title|PlaceholderText|Description)="([^"]*)"'
xaml_re = re.compile(xaml_attrs)

def xaml_sub(m):
    attr, val = m.group(1), m.group(2)
    if not re.search(r"[\u4e00-\u9fff]", val):
        return m.group(0)
    if val.startswith("{") or val not in keys:
        return m.group(0)
    return f'{attr}="{{loc:T {val}}}"'

xaml_files = []
for dp, dn, fn in os.walk(os.path.join(ROOT, "src", "MD3Zip.App", "Views")):
    for f in fn:
        if f.endswith(".axaml"):
            xaml_files.append(os.path.join(dp, f))

changed_xaml = 0
for path in xaml_files:
    with open(path, encoding="utf-8-sig") as fh:
        text = fh.read()
    new = xaml_re.sub(xaml_sub, text)
    if new != text:
        with open(path, "w", encoding="utf-8") as fh:
            fh.write(new)
        changed_xaml += 1
        print(f"XAML: {os.path.basename(path)}")

# ---------------- C# ----------------
str_re = re.compile(r'(?P<interp>\$?)"(?P<val>(?:[^"\\\r\n]|\\.)*)"')

def cs_sub(m):
    if m.group("interp") == "$":
        return m.group(0)  # 插值串跳过
    val = m.group("val")
    if val not in keys or unsafe.search(val):
        return m.group(0)
    return f'L10n.L("{val}")'

cs_files = []
for base in ["src/MD3Zip.App", "src/MD3Zip.Setup", "src/MD3Zip.Core"]:
    for dp, dn, fn in os.walk(os.path.join(ROOT, base)):
        if "/obj" in dp.replace("\\", "/").lower() or "/bin" in dp.replace("\\", "/").lower():
            continue
        for f in fn:
            if f.endswith(".cs"):
                cs_files.append(os.path.join(dp, f))

changed_cs = 0
for path in cs_files:
    with open(path, encoding="utf-8-sig") as fh:
        lines = fh.readlines()
    out = []
    touched = False
    for line in lines:
        stripped = line.lstrip()
        if stripped.startswith("//") or stripped.startswith("*") or stripped.startswith("///"):
            out.append(line)
            continue
        new = str_re.sub(cs_sub, line)
        if new != line:
            touched = True
        out.append(new)
    if touched:
        with open(path, "w", encoding="utf-8") as fh:
            fh.writelines(out)
        changed_cs += 1
        print(f"C#:   {os.path.basename(path)}")

print(f"\n完成：{changed_xaml} 个 XAML，{changed_cs} 个 C# 文件")
