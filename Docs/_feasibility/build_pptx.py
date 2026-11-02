# -*- coding: utf-8 -*-
"""
把 src/ppt.md 组装成排版好的汇报 PPT
输出：Docs/多人在线FPS对战系统_可行性分析汇报.pptx
"""
import os
import re
from pptx import Presentation
from pptx.util import Inches, Pt, Emu
from pptx.dml.color import RGBColor
from pptx.enum.text import PP_ALIGN, MSO_ANCHOR
from pptx.enum.shapes import MSO_SHAPE
from pptx.oxml.ns import qn
from lxml import etree

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src", "ppt.md")
FIG = os.path.join(HERE, "fig")
DOCS = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(DOCS, "多人在线FPS对战系统_可行性分析汇报.pptx")

EA = "微软雅黑"
LATIN = "Segoe UI"

ACCENT = RGBColor(0x1F, 0x4E, 0x79)
ACCENT2 = RGBColor(0x2E, 0x74, 0xB5)
LIGHT = RGBColor(0xEA, 0xF1, 0xF9)
ROW_ALT = RGBColor(0xF5, 0xF8, 0xFB)
TEXT = RGBColor(0x26, 0x26, 0x26)
MUTED = RGBColor(0x60, 0x6B, 0x7A)
WHITE = RGBColor(0xFF, 0xFF, 0xFF)

SW = Inches(13.333)
SH = Inches(7.5)

FIG_CAPTION = {
    "fig_arch": "系统总体架构",
    "fig_modules": "系统功能模块",
    "fig_flow": "用户操作流程",
    "fig_gantt": "项目建设里程碑甘特图",
    "fig_workload": "各阶段工作量分布",
    "fig_risk": "技术风险与对策矩阵",
}

FOOTER_LEFT = "多人在线FPS对战系统 · 可行性分析报告"


# ------------------------------------------------------------------ 基础工具
def set_run(run, text, size=18, bold=False, color=TEXT, ea=EA, latin=LATIN, italic=False):
    run.text = text
    f = run.font
    f.size = Pt(size)
    f.bold = bold
    f.italic = italic
    f.name = latin
    f.color.rgb = color
    rPr = run._r.get_or_add_rPr()
    for tag in ("a:ea", "a:cs"):
        el = rPr.find(qn(tag))
        if el is None:
            el = etree.SubElement(rPr, qn(tag))
        el.set("typeface", ea)
    return run


def textbox(slide, x, y, w, h, wrap=True):
    tb = slide.shapes.add_textbox(x, y, w, h)
    tf = tb.text_frame
    tf.word_wrap = wrap
    tf.margin_left = 0
    tf.margin_right = 0
    tf.margin_top = 0
    tf.margin_bottom = 0
    return tb, tf


def para(tf, first=False):
    return tf.paragraphs[0] if first else tf.add_paragraph()


def rect(slide, x, y, w, h, fill, line=None):
    sh = slide.shapes.add_shape(MSO_SHAPE.RECTANGLE, x, y, w, h)
    sh.fill.solid()
    sh.fill.fore_color.rgb = fill
    if line is None:
        sh.line.fill.background()
    else:
        sh.line.color.rgb = line
        sh.line.width = Pt(1)
    sh.shadow.inherit = False
    return sh


def rrect(slide, x, y, w, h, fill, line=None, radius=0.08):
    sh = slide.shapes.add_shape(MSO_SHAPE.ROUNDED_RECTANGLE, x, y, w, h)
    sh.fill.solid()
    sh.fill.fore_color.rgb = fill
    if line is None:
        sh.line.fill.background()
    else:
        sh.line.color.rgb = line
        sh.line.width = Pt(1)
    sh.shadow.inherit = False
    try:
        sh.adjustments[0] = radius
    except Exception:
        pass
    return sh


def fit_image(slide, path, bx, by, bw, bh):
    from PIL import Image
    with Image.open(path) as im:
        iw, ih = im.size
    a = iw / float(ih)
    if (bw / float(bh)) > a:
        h = bh
        w = int(bh * a)
    else:
        w = bw
        h = int(bw / a)
    x = bx + int((bw - w) / 2)
    y = by + int((bh - h) / 2)
    return slide.shapes.add_picture(path, x, y, width=w, height=h)


# ------------------------------------------------------------------ 版面
def new_slide(prs):
    return prs.slides.add_slide(prs.slide_layouts[6])


def slide_header(slide, title, idx=None, total=None):
    rect(slide, 0, 0, SW, Inches(0.13), ACCENT)
    tb, tf = textbox(slide, Inches(0.75), Inches(0.42), Inches(11.4), Inches(0.85))
    p = para(tf, True)
    set_run(p.add_run(), title, size=28, bold=True, color=ACCENT)
    p.space_after = Pt(0)
    # 标题下装饰线
    rect(slide, Inches(0.78), Inches(1.27), Inches(1.5), Pt(3.2), ACCENT2)
    add_footer(slide, idx, total)


def add_footer(slide, idx, total):
    if idx is None:
        return
    tb, tf = textbox(slide, Inches(0.75), Inches(7.03), Inches(8.0), Inches(0.32))
    p = para(tf, True)
    set_run(p.add_run(), FOOTER_LEFT, size=10, color=MUTED)
    tb2, tf2 = textbox(slide, Inches(11.6), Inches(7.03), Inches(1.0), Inches(0.32))
    p2 = para(tf2, True)
    p2.alignment = PP_ALIGN.RIGHT
    set_run(p2.add_run(), f"{idx} / {total}", size=10, bold=True, color=ACCENT2)


def strip_md(t):
    t = re.sub(r"\*\*(.+?)\*\*", r"\1", t)
    t = re.sub(r"(?<!\*)\*(?!\*)(.+?)(?<!\*)\*(?!\*)", r"\1", t)
    return t.replace("`", "")


def add_bullets(slide, items, x, y, w, h, size=17):
    tb, tf = textbox(slide, x, y, w, h)
    first = True
    for it in items:
        p = para(tf, first)
        first = False
        p.space_after = Pt(11)
        p.line_spacing = 1.22
        set_run(p.add_run(), "▪  ", size=size, bold=True, color=ACCENT2)
        set_run(p.add_run(), strip_md(it), size=size, color=TEXT)
    return tb


def add_table(slide, rows, x, y, w, h, size=13):
    nrow, ncol = len(rows), max(len(r) for r in rows)
    gt = slide.shapes.add_table(nrow, ncol, x, y, w, h)
    tbl = gt.table
    for i, row in enumerate(rows):
        for j in range(ncol):
            cell = tbl.cell(i, j)
            cell.margin_left = Inches(0.08)
            cell.margin_right = Inches(0.08)
            cell.margin_top = Inches(0.03)
            cell.margin_bottom = Inches(0.03)
            cell.vertical_anchor = MSO_ANCHOR.MIDDLE
            cell.fill.solid()
            if i == 0:
                cell.fill.fore_color.rgb = ACCENT
            else:
                cell.fill.fore_color.rgb = WHITE if i % 2 == 1 else ROW_ALT
            tf = cell.text_frame
            tf.word_wrap = True
            p = tf.paragraphs[0]
            p.alignment = PP_ALIGN.CENTER if (i == 0 or j > 0) else PP_ALIGN.LEFT
            val = strip_md(row[j]) if j < len(row) else ""
            set_run(p.add_run(), val, size=size,
                    bold=(i == 0), color=WHITE if i == 0 else TEXT)
    return tbl


def add_notes(slide, text):
    if not text:
        return
    slide.notes_slide.notes_text_frame.text = text


# ------------------------------------------------------------------ 解析 ppt.md
SLIDE_RE = re.compile(r"^##\s+第\s*(\d+)\s*页\s*(.*)$")
FIG_RE = re.compile(r"^\[\[FIG:([A-Za-z0-9_]+)\]\]$")
NOTE_RE = re.compile(r"^>\s*备注[：:]\s*(.*)$")


def parse_ppt(path):
    slides = []
    cur = None
    with open(path, "r", encoding="utf-8") as f:
        lines = f.read().split("\n")

    def flush():
        if cur:
            slides.append(cur)

    i = 0
    while i < len(lines):
        s = lines[i].strip()
        m = SLIDE_RE.match(s)
        if m:
            flush()
            cur = {"no": int(m.group(1)), "title": m.group(2).strip(),
                   "bullets": [], "table": None, "fig": None, "note": ""}
            i += 1
            continue
        if cur is None:
            i += 1
            continue
        if s.startswith("# "):
            cur["doc_title"] = s[2:].strip()
            i += 1
            continue
        m = FIG_RE.match(s)
        if m:
            cur["fig"] = m.group(1)
            i += 1
            continue
        m = NOTE_RE.match(s)
        if m:
            cur["note"] = m.group(1).strip()
            i += 1
            continue
        if s.startswith("|") and s.endswith("|"):
            block = []
            while i < len(lines) and lines[i].strip().startswith("|"):
                row = lines[i].strip()
                if not re.fullmatch(r"\|[\s:\-|]+\|", row):
                    block.append([c.strip() for c in row.split("|")[1:-1]])
                i += 1
            cur["table"] = block
            continue
        if s.startswith("- "):
            cur["bullets"].append(s[2:].strip())
            i += 1
            continue
        if s.startswith(">"):
            cur["note"] = (cur["note"] + " " + s[1:].strip()).strip()
            i += 1
            continue
        i += 1
    flush()
    return slides


# ------------------------------------------------------------------ 各类页面
def cover_slide(prs, data):
    slide = new_slide(prs)
    rect(slide, 0, 0, SW, SH, RGBColor(0xF7, 0xFA, 0xFD))
    rect(slide, 0, 0, Inches(0.28), SH, ACCENT)
    rect(slide, Inches(0.28), 0, SW, Inches(0.09), ACCENT2)

    tb, tf = textbox(slide, Inches(1.1), Inches(1.45), Inches(11.2), Inches(1.2))
    p = para(tf, True)
    set_run(p.add_run(), "多人在线FPS对战系统", size=44, bold=True, color=ACCENT)

    tb, tf = textbox(slide, Inches(1.1), Inches(2.72), Inches(11.2), Inches(0.7))
    p = para(tf, True)
    set_run(p.add_run(), "课程设计可行性分析报告", size=24, color=ACCENT2)

    rect(slide, Inches(1.14), Inches(3.5), Inches(2.2), Pt(3.5), ACCENT2)

    lines = [b for b in (data.get("bullets") or []) if b]
    tb, tf = textbox(slide, Inches(1.1), Inches(3.9), Inches(11.2), Inches(2.6))
    first = True
    for ln in lines:
        p = para(tf, first)
        first = False
        p.space_after = Pt(8)
        set_run(p.add_run(), strip_md(ln), size=16, color=MUTED)
    add_notes(slide, data.get("note", ""))
    return slide


def content_slide(prs, data, idx, total):
    slide = new_slide(prs)
    title = data["title"]
    slide_header(slide, title, idx, total)

    bullets = [b for b in data["bullets"] if b]
    table = data["table"]
    fig = data["fig"]

    top = Inches(1.62)
    bottom_limit = Inches(6.85)
    avail = bottom_limit - top

    if fig and bullets and not table:
        # 左文右图
        add_bullets(slide, bullets, Inches(0.78), top + Inches(0.12), Inches(4.55), Inches(5.0), size=16)
        path = os.path.join(FIG, fig + ".png")
        if os.path.exists(path):
            fit_image(slide, path, Inches(5.6), top, Inches(7.0), avail)
    elif fig and not bullets and not table:
        path = os.path.join(FIG, fig + ".png")
        if os.path.exists(path):
            fit_image(slide, path, Inches(0.78), top, SW - Inches(1.56), avail)
    elif table and fig:
        # 左表右图
        add_table(slide, table, Inches(0.72), top + Inches(0.30), Inches(5.95), Inches(3.4), size=12)
        path = os.path.join(FIG, fig + ".png")
        if os.path.exists(path):
            fit_image(slide, path, Inches(6.95), top, Inches(5.7), avail)
    elif table and bullets:
        # 左表右文
        add_table(slide, table, Inches(0.72), top + Inches(0.25), Inches(7.15), Inches(4.6), size=12)
        add_bullets(slide, bullets, Inches(8.15), top + Inches(0.30), Inches(4.5),
                    avail - Inches(0.4), size=13)
    elif table:
        add_table(slide, table, Inches(0.9), top + Inches(0.35), SW - Inches(1.8), Inches(4.2), size=14)
    else:
        size = 19 if len(bullets) <= 4 else 17
        add_bullets(slide, bullets, Inches(1.0), top + Inches(0.55), Inches(11.3), Inches(4.6), size=size)

    add_notes(slide, data.get("note", ""))
    return slide


def main():
    data = parse_ppt(SRC)
    if not data:
        raise SystemExit("未解析到任何页面: " + SRC)

    prs = Presentation()
    prs.slide_width = SW
    prs.slide_height = SH
    total = len(data)

    for d in data:
        if d["no"] == 1:
            cover_slide(prs, d)
        else:
            content_slide(prs, d, d["no"], total)

    prs.save(OUT)
    print("saved:", OUT, "slides:", len(prs.slides.__iter__.__self__._sldIdLst))


if __name__ == "__main__":
    main()
