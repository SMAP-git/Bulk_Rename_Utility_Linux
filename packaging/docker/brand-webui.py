from pathlib import Path
import re
import shutil


root = Path("/usr/share/novnc")
title = "Bulk Rename Utility"
icon = "app/images/bulk-rename-utility.png"
shutil.copyfile(
    "/usr/share/icons/hicolor/512x512/apps/bulk-rename-utility.png",
    root / icon,
)

for name in ("vnc.html", "vnc_lite.html"):
    path = root / name
    html = path.read_text()
    html, count = re.subn(r"<title>[^<]*</title>", f"<title>{title}</title>", html)
    if count != 1:
        raise RuntimeError(f"Expected one page title in {path}, found {count}")
    html = re.sub(
        r'\s*<link\b[^>]*\brel=["\'](?:icon|apple-touch-icon)["\'][^>]*>',
        "",
        html,
    )
    html = html.replace(
        "</head>",
        f'<link rel="icon" type="image/png" href="{icon}">\n'
        f'<link rel="apple-touch-icon" href="{icon}">\n</head>',
    )
    path.write_text(html)

path = root / "app/ui.js"
javascript = path.read_text()
for old, new in (
    ('const PAGE_TITLE = "noVNC";', f'const PAGE_TITLE = "{title}";'),
    ('document.title = e.detail.name + " - " + PAGE_TITLE;', "document.title = PAGE_TITLE;"),
):
    if javascript.count(old) != 1:
        raise RuntimeError(f"Expected one occurrence of {old!r} in {path}")
    javascript = javascript.replace(old, new)
path.write_text(javascript)
