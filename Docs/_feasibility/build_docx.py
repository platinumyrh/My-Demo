# -*- coding: utf-8 -*-
"""
把 src/*.md 组装成排版好的 Word 可行性分析报告
输出：Docs/多人在线FPS对战系统_可行性分析报告.docx
"""
import os
import re
from docx import Document
from docx.shared import Pt, Cm, RGBColor
from docx.enum.text import WD_ALIGN_PARAGRAPH, WD_LINE_SPACING
from docx.enum.section import WD_SECTION
from docx.enum.table import WD_TABLE_ALIGNMENT
from docx.oxml.ns import qn
from docx.oxml import OxmlElement

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.join(HERE, "src")
FIG = os.path.join(HERE, "fig")
DOCS = os.path.abspath(os.path.join(HERE, ".."))
OUT = os.path.join(DOCS, "多人在线FPS对战系统_可行性分析报告.docx")

BODY_EA, BODY_ASCII = "宋体", "Times New Roman"
HEAD_EA, HEAD_ASCII = "黑体", "Times New Roman"
FIG_WIDTH_CM = 15.5
TEXT_WIDTH_CM = 16.0

FIG_CAPTION = {
    "fig_arch": "图 1  系统总体架构图",
    "fig_modules": "图 2  系统功能模块图",
    "fig_flow": "图 3  用户操作流程图",
    "fig_gantt": "图 4  项目建设里程碑甘特图",
    "fig_workload": "图 5  各阶段工作量分布图",
    "fig_risk": "图 6  技术风险与对策矩阵",
}

ABSTRACT = (
    "本报告针对拟开发的多人在线FPS对战系统课程设计项目，从技术、经济、社会与操作四个维度进行事前可行性论证。"
    "项目拟由 5 人小组在 16 周内完成，采用 Unity 2022.3 LTS 作为客户端引擎、.NET 8 控制台程序作为服务端，"
    "基于 UDP 与 Protocol Buffers 通信；核心采用 15Hz 确定性帧同步方案，以 Q16.16 定点数、环绕角查表与确定性随机数"
    "构筑确定性数值基座，客户端上报量化输入并执行本地预测与快照回滚重演，服务端不持有游戏状态、仅按座位封帧与广播，"
    "超时以空指令补齐以保证不卡全场。资源层拟采用 AssetBundle 与对比文件增量热更，并预留脚本热更能力。"
    "经济层面，本项目无资金投入与商业收入，故以人时作为统一度量单位，总投入约 800 人时，"
    "其中确定性核心与同步预测占总投入的 55%；沉淀出的可复用框架预计可为后续同类开发节省 200 人时以上。"
    "分析表明：该方案技术路线成熟，团队能力缺口可通过分阶段自测闸门化解，4 人规模下性能余量充足，"
    "法律许可与校园环境条件具备，操作与运维门槛低。结论为有条件可行，建议立项，并给出范围冻结与敏感性降级预案。"
)
KEYWORDS = "帧同步；确定性模拟；客户端预测与回滚；资源热更新；可行性分析"


# ------------------------------------------------------------------ 底层工具
def style_run(run, size=12, bold=False, ea=BODY_EA, ascii_f=BODY_ASCII, color=None, italic=False):
    run.font.size = Pt(size)
    run.font.bold = bold
    run.font.italic = italic
    run.font.name = ascii_f
    rPr = run._element.get_or_add_rPr()
    rFonts = rPr.find(qn("w:rFonts"))
    if rFonts is None:
        rFonts = OxmlElement("w:rFonts")
        rPr.insert(0, rFonts)
    rFonts.set(qn("w:ascii"), ascii_f)
    rFonts.set(qn("w:hAnsi"), ascii_f)
    rFonts.set(qn("w:eastAsia"), ea)
    if color is not None:
        run.font.color.rgb = color
    return run


def set_para(p, align=None, first_line=None, left=None, line=1.5, before=0, after=0):
    pf = p.paragraph_format
    if align is not None:
        p.alignment = align
    if first_line is not None:
        pf.first_line_indent = first_line
    if left is not None:
        pf.left_indent = left
    pf.line_spacing = line
    pf.space_before = Pt(before)
    pf.space_after = Pt(after)
    return p


TOKEN_RE = re.compile(r"(\*\*.+?\*\*|`[^`]+?`)")


def add_inline(p, text, size=12, ea=BODY_EA, ascii_f=BODY_ASCII, base_bold=False):
    """支持 **粗体** 与 `等宽` 的行内解析"""
    for part in TOKEN_RE.split(text):
        if not part:
            continue
        if part.startswith("**") and part.endswith("**") and len(part) > 4:
            style_run(p.add_run(part[2:-2]), size=size, bold=True, ea=ea, ascii_f=ascii_f)
        elif part.startswith("`") and part.endswith("`") and len(part) > 2:
            style_run(p.add_run(part[1:-1]), size=size - 0.5, bold=base_bold,
                      ea=ea, ascii_f="Consolas")
        else:
            style_run(p.add_run(part), size=size, bold=base_bold, ea=ea, ascii_f=ascii_f)


def shade_cell(cell, fill):
    tcPr = cell._tc.get_or_add_tcPr()
    shd = OxmlElement("w:shd")
    shd.set(qn("w:val"), "clear")
    shd.set(qn("w:color"), "auto")
    shd.set(qn("w:fill"), fill)
    tcPr.append(shd)


def cell_text(cell, text, size=10.5, bold=False, align=WD_ALIGN_PARAGRAPH.LEFT, ea=BODY_EA):
    cell.text = ""
    p = cell.paragraphs[0]
    set_para(p, align=align, line=1.15, before=1.5, after=1.5)
    add_inline(p, text, size=size, ea=ea, base_bold=bold)


def add_field(paragraph, instr, placeholder=""):
    r = paragraph.add_run()
    fc = OxmlElement("w:fldChar")
    fc.set(qn("w:fldCharType"), "begin")
    r._element.append(fc)

    r2 = paragraph.add_run()
    it = OxmlElement("w:instrText")
    it.set(qn("xml:space"), "preserve")
    it.text = instr
    r2._element.append(it)

    r3 = paragraph.add_run()
    fc2 = OxmlElement("w:fldChar")
    fc2.set(qn("w:fldCharType"), "separate")
    r3._element.append(fc2)

    r4 = paragraph.add_run(placeholder)

    r5 = paragraph.add_run()
    fc3 = OxmlElement("w:fldChar")
    fc3.set(qn("w:fldCharType"), "end")
    r5._element.append(fc3)
    return [r, r2, r3, r4, r5]


def enable_update_fields(doc):
    """让 Word 打开文档时提示更新目录域"""
    st = doc.settings.element
    if st.find(qn("w:updateFields")) is not None:
        return
    el = OxmlElement("w:updateFields")
    el.set(qn("w:val"), "true")
    anchor = st.find(qn("w:compat"))
    if anchor is not None:
        anchor.addprevious(el)
    else:
        st.append(el)


def sect_page_numbering(section, start=1):
    sectPr = section._sectPr
    for old in sectPr.findall(qn("w:pgNumType")):
        sectPr.remove(old)
    el = OxmlElement("w:pgNumType")
    el.set(qn("w:start"), str(start))
    cols = sectPr.find(qn("w:cols"))
    if cols is not None:
        cols.addprevious(el)
    else:
        sectPr.append(el)


def add_page_footer(section, ea=BODY_EA):
    section.footer.is_linked_to_previous = False
    p = section.footer.paragraphs[0]
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.0)
    style_run(p.add_run("第 "), size=10.5)
    for r in add_field(p, " PAGE ", "1"):
        style_run(r, size=10.5)
    style_run(p.add_run(" 页"), size=10.5)


# ------------------------------------------------------------------ Markdown 解析
def is_table_row(line):
    s = line.strip()
    return s.startswith("|") and s.endswith("|") and s.count("|") >= 2


def is_sep_row(line):
    s = line.strip()
    return re.fullmatch(r"\|[\s:\-|]+\|", s) is not None


def split_cells(line):
    s = line.strip()
    parts = s.split("|")
    return [c.strip() for c in parts[1:-1]]


class Builder:
    def __init__(self, doc):
        self.doc = doc

    def h1(self, text):
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, before=18, after=12)
        style_run(p.add_run(text), size=16, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)
        # 让目录域能抓到：同时套用内置 Heading 1 大纲级别
        p.style = self.doc.styles["Heading 1"]
        for r in p.runs:
            style_run(r, size=16, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)
        set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, before=18, after=12)

    def h2(self, text):
        p = self.doc.add_paragraph()
        p.style = self.doc.styles["Heading 2"]
        style_run(p.add_run(text), size=14, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)
        set_para(p, align=WD_ALIGN_PARAGRAPH.LEFT, line=1.5, before=12, after=6)

    def para(self, text):
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.JUSTIFY, first_line=Cm(0.74), line=1.5, after=0)
        add_inline(p, text, size=12)

    def bullet(self, text):
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.JUSTIFY, left=Cm(0.74), line=1.5, after=0)
        p.paragraph_format.first_line_indent = Cm(-0.37)
        add_inline(p, "● " + text, size=12)

    def ordered(self, num, text):
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.JUSTIFY, left=Cm(0.74), line=1.5, after=0)
        p.paragraph_format.first_line_indent = Cm(-0.74)
        add_inline(p, f"（{num}）{text}", size=12)

    def quote(self, text):
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.LEFT, left=Cm(0.74), line=1.4, before=3, after=3)
        add_inline(p, text, size=10.5, ea="楷体")
        for r in p.runs:
            r.font.color.rgb = RGBColor(0x44, 0x44, 0x44)

    def table(self, rows):
        if not rows:
            return
        ncol = max(len(r) for r in rows)
        t = self.doc.add_table(rows=len(rows), cols=ncol)
        t.style = self.doc.styles["Table Grid"]
        t.alignment = WD_TABLE_ALIGNMENT.CENTER
        t.autofit = True
        for i, row in enumerate(rows):
            for j in range(ncol):
                val = row[j] if j < len(row) else ""
                cell = t.cell(i, j)
                if i == 0:
                    cell_text(cell, val, size=10.5, bold=True,
                              align=WD_ALIGN_PARAGRAPH.CENTER, ea=HEAD_EA)
                    shade_cell(cell, "DCE6F1")
                else:
                    al = WD_ALIGN_PARAGRAPH.LEFT
                    if len(val) <= 10 and j > 0:
                        al = WD_ALIGN_PARAGRAPH.CENTER
                    cell_text(cell, val, size=10.5, align=al)
        # 表后空一行
        sp = self.doc.add_paragraph()
        set_para(sp, line=1.0, after=6)

    def figure(self, fig_id, caption):
        path = os.path.join(FIG, fig_id + ".png")
        if not os.path.exists(path):
            return
        p = self.doc.add_paragraph()
        set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.0, before=6, after=3)
        p.add_run().add_picture(path, width=Cm(FIG_WIDTH_CM))
        cp = self.doc.add_paragraph()
        set_para(cp, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.2, before=0, after=10)
        style_run(cp.add_run(caption), size=10.5, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)


FIG_RE = re.compile(r"^\[\[FIG:([A-Za-z0-9_]+)(?:\|(.+?))?\]\]$")
ORD_RE = re.compile(r"^(\d+)[.、)]\s+(.*)$")


def render_markdown(b, text):
    lines = text.split("\n")
    i = 0
    while i < len(lines):
        raw = lines[i]
        line = raw.rstrip()
        s = line.strip()

        if not s:
            i += 1
            continue

        m = FIG_RE.match(s)
        if m:
            fid, cap = m.group(1), (m.group(2) or FIG_CAPTION.get(fid, ""))
            b.figure(fid, cap)
            i += 1
            continue

        if s.startswith("# "):
            b.h1(s[2:].strip())
            i += 1
            continue
        if s.startswith("## "):
            b.h2(s[3:].strip())
            i += 1
            continue

        if s.startswith("|") and is_table_row(s):
            block = []
            while i < len(lines) and is_table_row(lines[i].strip()):
                if not is_sep_row(lines[i].strip()):
                    block.append(split_cells(lines[i]))
                i += 1
            b.table(block)
            continue

        if s.startswith("- ") or s.startswith("* "):
            b.bullet(s[2:].strip())
            i += 1
            continue

        mo = ORD_RE.match(s)
        if mo:
            b.ordered(mo.group(1), mo.group(2).strip())
            i += 1
            continue

        if s.startswith("> "):
            b.quote(s[2:].strip())
            i += 1
            continue
        if s.startswith(">"):
            b.quote(s[1:].strip())
            i += 1
            continue

        # 普通段落：合并连续非空行
        buf = [s]
        i += 1
        while i < len(lines):
            nxt = lines[i].strip()
            if (not nxt or nxt.startswith("#") or nxt.startswith("|")
                    or nxt.startswith("- ") or nxt.startswith("* ")
                    or nxt.startswith(">") or FIG_RE.match(nxt) or ORD_RE.match(nxt)):
                break
            buf.append(nxt)
            i += 1
        b.para("".join(buf) if len(buf) > 1 else buf[0])


# ------------------------------------------------------------------ 文档骨架
def setup_styles(doc):
    normal = doc.styles["Normal"]
    normal.font.name = BODY_ASCII
    normal.font.size = Pt(12)
    rPr = normal.element.get_or_add_rPr()
    rFonts = rPr.find(qn("w:rFonts"))
    if rFonts is None:
        rFonts = OxmlElement("w:rFonts")
        rPr.insert(0, rFonts)
    rFonts.set(qn("w:ascii"), BODY_ASCII)
    rFonts.set(qn("w:hAnsi"), BODY_ASCII)
    rFonts.set(qn("w:eastAsia"), BODY_EA)
    normal.paragraph_format.line_spacing = 1.5
    normal.paragraph_format.space_after = Pt(0)

    for name, size in (("Heading 1", 16), ("Heading 2", 14)):
        st = doc.styles[name]
        st.font.name = HEAD_ASCII
        st.font.size = Pt(size)
        st.font.bold = True
        st.font.color.rgb = RGBColor(0, 0, 0)
        rPr = st.element.get_or_add_rPr()
        rFonts = rPr.find(qn("w:rFonts"))
        if rFonts is None:
            rFonts = OxmlElement("w:rFonts")
            rPr.insert(0, rFonts)
        rFonts.set(qn("w:ascii"), HEAD_ASCII)
        rFonts.set(qn("w:hAnsi"), HEAD_ASCII)
        rFonts.set(qn("w:eastAsia"), HEAD_EA)

    for sec in doc.sections:
        sec.page_width = Cm(21.0)
        sec.page_height = Cm(29.7)
        sec.top_margin = Cm(2.54)
        sec.bottom_margin = Cm(2.54)
        sec.left_margin = Cm(2.5)
        sec.right_margin = Cm(2.5)


def build_cover(doc):
    def blank(n=1, size=12):
        for _ in range(n):
            p = doc.add_paragraph()
            set_para(p, line=1.0, after=0)
            style_run(p.add_run(""), size=size)

    blank(3)
    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, after=6)
    style_run(p.add_run("______ 大学"), size=16, bold=True, ea=HEAD_EA)

    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, after=0)
    style_run(p.add_run("课 程 设 计 报 告"), size=26, bold=True, ea=HEAD_EA)

    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, before=4, after=0)
    style_run(p.add_run("（可 行 性 分 析）"), size=15, ea=HEAD_EA)

    blank(3)
    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.6, after=4)
    style_run(p.add_run("项目名称：多人在线FPS对战系统"), size=16, bold=True, ea=HEAD_EA)

    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.6, after=0)
    style_run(p.add_run("报告类型：可行性分析报告"), size=14, ea=HEAD_EA)

    blank(3)
    rows = [
        ("学　　院", "______"),
        ("专　　业", "______"),
        ("班　　级", "______"),
        ("小组成员", "______　______　______　______　______"),
        ("学　　号", "______　______　______　______　______"),
        ("指导教师", "______"),
        ("完成日期", "______ 年 ______ 月 ______ 日"),
    ]
    t = doc.add_table(rows=len(rows), cols=2)
    t.alignment = WD_TABLE_ALIGNMENT.CENTER
    t.autofit = False
    for i, (k, v) in enumerate(rows):
        c0 = t.cell(i, 0)
        c0.width = Cm(3.6)
        cell_text(c0, k, size=13, bold=True, align=WD_ALIGN_PARAGRAPH.RIGHT, ea=HEAD_EA)
        c1 = t.cell(i, 1)
        c1.width = Cm(9.4)
        cell_text(c1, v, size=13, align=WD_ALIGN_PARAGRAPH.LEFT)
        # 下划线效果
        for p in c1.paragraphs:
            for r in p.runs:
                r.font.underline = True


def build_abstract(doc):
    b = Builder(doc)
    p = doc.add_paragraph()
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, before=0, after=12)
    p.style = doc.styles["Heading 1"]
    style_run(p.add_run("摘　要"), size=16, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, after=12)

    pp = doc.add_paragraph()
    set_para(pp, align=WD_ALIGN_PARAGRAPH.JUSTIFY, first_line=Cm(0.74), line=1.5, after=6)
    add_inline(pp, ABSTRACT, size=12)

    kp = doc.add_paragraph()
    set_para(kp, align=WD_ALIGN_PARAGRAPH.JUSTIFY, first_line=Cm(0.74), line=1.5, after=0)
    add_inline(kp, "关键词：" + KEYWORDS, size=12)
    for r in kp.runs:
        r.font.bold = True


def build_toc(doc):
    p = doc.add_paragraph()
    p.style = doc.styles["Heading 1"]
    style_run(p.add_run("目　录"), size=16, bold=True, ea=HEAD_EA, ascii_f=HEAD_ASCII)
    set_para(p, align=WD_ALIGN_PARAGRAPH.CENTER, line=1.5, before=0, after=12)

    tp = doc.add_paragraph()
    set_para(tp, line=1.5, after=0)
    runs = add_field(tp, ' TOC \\o "1-2" \\h \\z \\u ', "（打开后按 Ctrl+A 再按 F9 可更新目录）")
    for r in runs:
        style_run(r, size=12)


def main():
    doc = Document()
    setup_styles(doc)

    # 第 1 节：封面
    build_cover(doc)

    # 第 2 节：摘要 + 目录 + 正文
    doc.add_section(WD_SECTION.NEW_PAGE)
    sec2 = doc.sections[1]
    sec2.page_width = Cm(21.0)
    sec2.page_height = Cm(29.7)
    sec2.top_margin = Cm(2.54)
    sec2.bottom_margin = Cm(2.54)
    sec2.left_margin = Cm(2.5)
    sec2.right_margin = Cm(2.5)
    sect_page_numbering(sec2, 1)
    add_page_footer(sec2)

    build_abstract(doc)
    doc.add_page_break()
    build_toc(doc)
    doc.add_page_break()

    b = Builder(doc)
    order = ["ch1_2.md", "ch3.md", "ch4_5.md", "ch6_7.md"]
    for idx, fn in enumerate(order):
        path = os.path.join(SRC, fn)
        if not os.path.exists(path):
            raise SystemExit("缺少章节文件: " + path)
        with open(path, "r", encoding="utf-8") as f:
            render_markdown(b, f.read())
        if idx < len(order) - 1:
            doc.add_page_break()

    enable_update_fields(doc)
    doc.save(OUT)
    print("saved:", OUT)


if __name__ == "__main__":
    main()
