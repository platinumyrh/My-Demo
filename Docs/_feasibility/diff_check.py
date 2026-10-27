# -*- coding: utf-8 -*-
"""对比源 Markdown 与成品 docx 的中文字符量，确认没有整段丢失"""
import os, re, sys
from docx import Document

HERE = os.path.dirname(os.path.abspath(__file__))
DOCS = os.path.abspath(os.path.join(HERE, ".."))
DOCX = os.path.join(DOCS, "多人在线FPS对战系统_可行性分析报告.docx")

out = open(os.path.join(HERE, "diff_check.txt"), "w", encoding="utf-8")


def cjk(s):
    return len(re.findall(r"[\u4e00-\u9fff]", s))


total_src = 0
out.write("每章源文件 vs docx 中文字符\n" + "-" * 60 + "\n")
for fn in ["ch1_2.md", "ch3.md", "ch4_5.md", "ch6_7.md"]:
    txt = open(os.path.join(HERE, "src", fn), encoding="utf-8").read()
    # 剔除标记行与表格分隔行
    body = "\n".join(l for l in txt.split("\n")
                     if not l.strip().startswith("[[FIG") and not re.fullmatch(r"\|[\s:\-|]+\|", l.strip()))
    n = cjk(body)
    total_src += n
    out.write(f"{fn:12s} 源 {n:6d}\n")

doc = Document(DOCX)
alltext = "\n".join(p.text for p in doc.paragraphs)
for t in doc.tables:
    for r in t.rows:
        for c in r.cells:
            alltext += "\n" + c.text
n_doc = cjk(alltext)
out.write("-" * 60 + "\n")
out.write(f"源合计(不含标记行)   {total_src:6d}\n")
out.write(f"docx 合计            {n_doc:6d}\n")
out.write(f"差额(docx-源，含封面/摘要/目录等新增内容) {n_doc - total_src:6d}\n")

# 逐章抓唯一句验证
probes = [
    "编写目的", "现行组织系统概况", "拟建立的信息系统", "经济可行性分析",
    "技术可行性分析", "社会可行性分析", "结论的解释", "术语表",
    "确定性帧同步", "Q16.16", "封帧", "回滚重演",
]
out.write("\n关键术语出现次数\n" + "-" * 60 + "\n")
for p in probes:
    out.write(f"{p:16s} {alltext.count(p)}\n")

out.write("\n图片数: %d   表格数: %d   段落数: %d\n"
          % (len(doc.inline_shapes), len(doc.tables), len(doc.paragraphs)))
out.close()
print("written")
