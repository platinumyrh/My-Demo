# -*- coding: utf-8 -*-
"""对生成的 docx / pptx 做结构化校验与文本容量测算"""
import os, re, sys
from docx import Document
from docx.shared import Emu
from pptx import Presentation
from pptx.util import Emu as PEmu
from PIL import ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.abspath(os.path.join(HERE, ".."))
DOCX = os.path.join(DOCS, "多人在线FPS对战系统_可行性分析报告.docx")
PPTX = os.path.join(DOCS, "多人在线FPS对战系统_可行性分析汇报.pptx")
FIGDIR = os.path.join(HERE, "fig")
YAHEI = "C:/Windows/Fonts/msyh.ttc"

FORBIDDEN = {
    "金额": [r"\d+\s*元", r"￥", r"¥", r"\d+\s*美元", r"费用", r"预算金额"],
    "完成时": [r"已经实现", r"已完成开发", r"已实现", r"实测得出", r"目前运行良好"],
}

_fc = {}


def font(sz):
    sz = max(int(round(sz)), 6)
    if sz not in _fc:
        _fc[sz] = ImageFont.truetype(YAHEI, sz)
    return _fc[sz]


def wpt(text, sz):
    f = font(sz)
    b = f.getbbox(text)
    return (b[2] - b[0])


def wrap_lines(text, sz, maxw_pt):
    lines, cur = 1, 0.0
    for ch in text:
        w = wpt(ch, sz)
        if cur + w > maxw_pt and cur > 0:
            lines += 1
            cur = w
        else:
            cur += w
    return lines


def check_text(name, text):
    hits = []
    for k, pats in FORBIDDEN.items():
        for p in pats:
            for m in re.finditer(p, text):
                s = max(0, m.start() - 18)
                hits.append((k, text[s:m.end() + 18].replace("\n", " ")))
    return hits


# ------------------------------------------------------------------ docx
def verify_docx():
    print("=" * 78)
    print("DOCX:", os.path.basename(DOCX), os.path.getsize(DOCX), "bytes")
    doc = Document(DOCX)
    paras = doc.paragraphs
    h1 = [p.text for p in paras if p.style.name == "Heading 1"]
    h2 = [p.text for p in paras if p.style.name == "Heading 2"]
    print("段落总数:", len(paras), "| 表格:", len(doc.tables), "| 内嵌图片:", len(doc.inline_shapes))
    print("节数:", len(doc.sections))
    print("\n一级标题 (%d):" % len(h1))
    for t in h1:
        print("   ", t)
    print("\n二级标题 (%d):" % len(h2))
    for t in h2:
        print("   ", t)

    # 表格规模
    print("\n表格明细:")
    for i, t in enumerate(doc.tables, 1):
        print(f"   表{i}: {len(t.rows)} 行 x {len(t.columns)} 列   首行: "
              + " | ".join(c.text.strip()[:10] for c in t.rows[0].cells))

    # 图片实际尺寸
    print("\n图片尺寸(cm):")
    for i, sh in enumerate(doc.inline_shapes, 1):
        print(f"   #{i}: {Emu(sh.width).cm:.2f} x {Emu(sh.height).cm:.2f}")

    # 全文文本检查
    alltext = "\n".join(p.text for p in paras)
    for t in doc.tables:
        for r in t.rows:
            for c in r.cells:
                alltext += "\n" + c.text
    hits = check_text("docx", alltext)
    print("\n禁用表述命中:", len(hits))
    for h in hits[:20]:
        print("   !", h)

    cjk = len(re.findall(r"[\u4e00-\u9fff]", alltext))
    print("中文字符数:", cjk)
    print("含占位符 ______ 次数:", alltext.count("______"))
    return len(h1), len(h2), len(doc.tables), len(doc.inline_shapes)


# ------------------------------------------------------------------ pptx
def verify_pptx():
    print("\n" + "=" * 78)
    print("PPTX:", os.path.basename(PPTX), os.path.getsize(PPTX), "bytes")
    prs = Presentation(PPTX)
    print("画布: %.2f x %.2f in | 幻灯片: %d"
          % (prs.slide_width / 914400, prs.slide_height / 914400, len(prs.slides)))
    problems = []
    alltext = ""
    for idx, slide in enumerate(prs.slides, 1):
        kinds = []
        for sh in slide.shapes:
            kinds.append(sh.shape_type)
        has_notes = slide.has_notes_slide and slide.notes_slide.notes_text_frame.text.strip() != ""
        n_pic = sum(1 for sh in slide.shapes if sh.shape_type == 13)
        n_tbl = sum(1 for sh in slide.shapes if getattr(sh, "has_table", False) and sh.has_table)
        print(f"\n--- 第 {idx} 页 | 形状 {len(slide.shapes)} | 图 {n_pic} | 表 {n_tbl} | 备注 {'有' if has_notes else '无'}")

        for sh in slide.shapes:
            if sh.has_text_frame and sh.text_frame.text.strip():
                txt = sh.text_frame.text
                alltext += "\n" + txt
                top_in = sh.top / 914400
                left_in = sh.left / 914400
                w_in = sh.width / 914400
                h_in = sh.height / 914400
                # 盒底是否越界
                if top_in + h_in > 7.5 + 0.01 or left_in + w_in > 13.34 + 0.01:
                    problems.append(f"第{idx}页 文本框越界: L{left_in:.2f} T{top_in:.2f} W{w_in:.2f} H{h_in:.2f}")
                # 容量测算
                need = 0.0
                for p in sh.text_frame.paragraphs:
                    t = "".join(r.text for r in p.runs)
                    if not t:
                        continue
                    sz = None
                    for r in p.runs:
                        if r.font.size:
                            sz = r.font.size.pt
                            break
                    sz = sz or 18
                    ls = p.line_spacing or 1.0
                    n = wrap_lines(t, sz, w_in * 72 - 2)
                    need += n * sz * ls * 1.02 + (p.space_after.pt if p.space_after else 0)
                if need > h_in * 72 + 1:
                    problems.append(
                        f"第{idx}页 文本可能溢出: 需要≈{need:.0f}pt 盒高={h_in*72:.0f}pt "
                        f"[{txt[:34].replace(chr(10),' / ')}]")

        # 打印要点摘要
        for sh in slide.shapes:
            if sh.has_text_frame and sh.text_frame.text.strip():
                first = sh.text_frame.paragraphs[0].runs
                if first and first[0].font.size and first[0].font.size.pt >= 26:
                    print("   标题:", sh.text_frame.text.strip()[:40])

    hits = check_text("pptx", alltext)
    print("\n禁用表述命中:", len(hits))
    for h in hits[:20]:
        print("   !", h)
    print("中文字符数:", len(re.findall(r"[\u4e00-\u9fff]", alltext)))

    print("\n布局问题:", len(problems))
    for p in problems:
        print("   !", p)
    return problems


def strip_md(t):
    t = re.sub(r"\*\*(.+?)\*\*", r"\1", t)
    return t.replace("`", "")


def verify_completeness():
    """逐页核对：ppt.md 里的每条要点、每个表格单元格都必须出现在成品中"""
    print("\n" + "=" * 78)
    print("完整性核对（ppt.md -> pptx）")
    src = open(os.path.join(HERE, "src", "ppt.md"), encoding="utf-8").read().split("\n")
    slides, cur = [], None
    for ln in src:
        s = ln.strip()
        m = re.match(r"^##\s+第\s*(\d+)\s*页\s*(.*)$", s)
        if m:
            cur = {"no": int(m.group(1)), "bullets": [], "cells": []}
            slides.append(cur)
            continue
        if cur is None:
            continue
        if s.startswith("- "):
            cur["bullets"].append(s[2:].strip())
        elif s.startswith("|") and not re.fullmatch(r"\|[\s:\-|]+\|", s):
            for c in s.split("|")[1:-1]:
                c = c.strip()
                if c:
                    cur["cells"].append(c)

    prs = Presentation(PPTX)
    missing, total_b, total_c = [], 0, 0
    for d in slides:
        s = prs.slides[d["no"] - 1]
        txt = "\n".join(sh.text_frame.text for sh in s.shapes if sh.has_text_frame)
        for t in s.shapes:
            if getattr(t, "has_table", False) and t.has_table:
                for r in t.table.rows:
                    for c in r.cells:
                        txt += "\n" + c.text
        for b in d["bullets"]:
            total_b += 1
            if strip_md(b) not in txt:
                missing.append((d["no"], "要点", b[:34]))
        for c in d["cells"]:
            total_c += 1
            if strip_md(c) not in txt:
                missing.append((d["no"], "单元格", c[:34]))
    print(f"核对要点 {total_b} 条、表格单元格 {total_c} 个")
    print("缺失:", len(missing))
    for m in missing[:30]:
        print("   ! 第%d页 %s: %s" % m)
    # 残留 Markdown 标记
    leftover = []
    for i, s in enumerate(prs.slides, 1):
        for sh in s.shapes:
            if sh.has_text_frame and ("**" in sh.text_frame.text or "`" in sh.text_frame.text):
                leftover.append((i, sh.text_frame.text[:40]))
            if getattr(sh, "has_table", False) and sh.has_table:
                for r in sh.table.rows:
                    for c in r.cells:
                        if "**" in c.text:
                            leftover.append((i, c.text[:40]))
    print("残留 Markdown 标记:", len(leftover))
    for l in leftover[:10]:
        print("   !", l)
    return missing, leftover


if __name__ == "__main__":
    report = os.path.join(HERE, "verify_report.txt")
    _real = sys.stdout
    sys.stdout = open(report, "w", encoding="utf-8")
    try:
        verify_docx()
        verify_pptx()
        verify_completeness()
        print("\nDONE")
    finally:
        sys.stdout.close()
        sys.stdout = _real
    print("report written:", report)
