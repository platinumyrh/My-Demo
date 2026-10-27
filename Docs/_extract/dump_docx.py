# -*- coding: utf-8 -*-
"""Read-only extraction of a DOCX into a structured text dump."""
import sys
from docx import Document
from docx.table import Table
from docx.text.paragraph import Paragraph
from docx.oxml.ns import qn


def iter_block_items(parent):
    from docx.document import Document as _Doc
    if isinstance(parent, _Doc):
        parent_elm = parent.element.body
    else:
        parent_elm = parent._element
    for child in parent_elm.iterchildren():
        if child.tag == qn('w:p'):
            yield Paragraph(child, parent)
        elif child.tag == qn('w:tbl'):
            yield Table(child, parent)


def main(path, out):
    doc = Document(path)
    lines = []
    lines.append("=== FILE: %s ===" % path)
    # core properties
    cp = doc.core_properties
    lines.append("[core] title=%r author=%r created=%s modified=%s last_modified_by=%r" % (
        cp.title, cp.author, cp.created, cp.modified, cp.last_modified_by))
    lines.append("[sections] count=%d" % len(doc.sections))
    for i, s in enumerate(doc.sections):
        lines.append("  section %d: page %sx%s orientation=%s" % (
            i, s.page_width, s.page_height, s.orientation))
    # headers/footers
    for i, s in enumerate(doc.sections):
        for name, part in (("header", s.header), ("footer", s.footer)):
            txt = " | ".join(p.text for p in part.paragraphs if p.text.strip())
            if txt:
                lines.append("  [%s of section %d] %s" % (name, i, txt))
    lines.append("[body]")
    p_idx = 0
    t_idx = 0
    for block in iter_block_items(doc):
        if isinstance(block, Paragraph):
            text = block.text
            style = block.style.name if block.style is not None else ""
            if text.strip() == "" and not style.startswith("Heading"):
                continue
            # detect page/section break markers
            brk = ""
            xml = block._p.xml
            if 'w:br' in xml and 'type="page"' in xml:
                brk = " [PAGE-BREAK]"
            lines.append("P%03d <%s>%s %s" % (p_idx, style, brk, text))
            p_idx += 1
        else:
            lines.append("--- TABLE %d (rows=%d, cols=%d) ---" % (
                t_idx, len(block.rows), len(block.columns)))
            for r_i, row in enumerate(block.rows):
                cells = []
                seen = set()
                for c in row.cells:
                    if id(c._tc) in seen:
                        continue
                    seen.add(id(c._tc))
                    cells.append(" ".join(c.text.split()))
                lines.append("  R%02d | %s" % (r_i, " || ".join(cells)))
            lines.append("--- END TABLE %d ---" % t_idx)
            t_idx += 1
    lines.append("[counts] paragraphs=%d tables=%d" % (p_idx, t_idx))
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("wrote", out, "lines", len(lines))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
