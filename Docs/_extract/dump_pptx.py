# -*- coding: utf-8 -*-
"""Read-only extraction of a PPTX into a structured text dump."""
import sys
from pptx import Presentation
from pptx.util import Emu


def shape_dump(shape, indent, out):
    pad = "  " * indent
    st = shape.shape_type
    name = shape.name
    out.append("%s- shape name=%r type=%s" % (pad, name, st))
    if shape.has_text_frame:
        tf = shape.text_frame
        for p_i, p in enumerate(tf.paragraphs):
            txt = "".join(r.text for r in p.runs)
            lvl = p.level
            if txt.strip() == "":
                continue
            out.append("%s  p%d(lvl%d): %s" % (pad, p_i, lvl, txt))
    if shape.has_table:
        tbl = shape.table
        out.append("%s  TABLE rows=%d cols=%d" % (pad, len(tbl.rows), len(tbl.columns)))
        for r_i, row in enumerate(tbl.rows):
            cells = [" ".join(c.text.split()) for c in row.cells]
            out.append("%s    R%02d | %s" % (pad, r_i, " || ".join(cells)))
    if shape.shape_type == 6 or shape.__class__.__name__ == "GroupShape":
        try:
            for sub in shape.shapes:
                shape_dump(sub, indent + 1, out)
        except Exception as e:
            out.append("%s  (group dump failed: %s)" % (pad, e))
    if shape.has_chart:
        try:
            ch = shape.chart
            out.append("%s  CHART type=%s" % (pad, ch.chart_type))
            try:
                cats = list(ch.plots[0].categories)
                out.append("%s    categories=%s" % (pad, cats))
            except Exception as e:
                out.append("%s    (no categories: %s)" % (pad, e))
            for s in ch.series:
                try:
                    out.append("%s    series %r = %s" % (pad, s.name, list(s.values)))
                except Exception as e:
                    out.append("%s    (series err: %s)" % (pad, e))
        except Exception as e:
            out.append("%s  (chart dump failed: %s)" % (pad, e))


def main(path, out):
    prs = Presentation(path)
    lines = []
    lines.append("=== FILE: %s ===" % path)
    lines.append("[size] width=%s height=%s slides=%d" % (
        prs.slide_width, prs.slide_height, len(prs.slides)))
    cp = prs.core_properties
    lines.append("[core] title=%r author=%r created=%s modified=%s" % (
        cp.title, cp.author, cp.created, cp.modified))
    for i, slide in enumerate(prs.slides, 1):
        layout = slide.slide_layout
        lines.append("")
        lines.append("=== SLIDE %d === (layout=%r)" % (i, layout.name))
        for shape in slide.shapes:
            shape_dump(shape, 0, lines)
        if slide.has_notes_slide:
            notes = slide.notes_slide.notes_text_frame.text
            if notes.strip():
                lines.append("[NOTES] %s" % notes.replace("\n", " / "))
    with open(out, "w", encoding="utf-8") as f:
        f.write("\n".join(lines))
    print("wrote", out, "lines", len(lines))


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2])
