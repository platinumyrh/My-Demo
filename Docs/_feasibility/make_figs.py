# -*- coding: utf-8 -*-
"""
可行性分析报告图表生成器（Pillow 手工绘制，环境无 matplotlib）
输出：Docs/_feasibility/fig/*.png
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "fig")
os.makedirs(OUT, exist_ok=True)

YAHEI = "C:/Windows/Fonts/msyh.ttc"
YAHEI_BD = "C:/Windows/Fonts/msyhbd.ttc"

_font_cache = {}


def F(size, bold=False):
    key = (size, bold)
    if key not in _font_cache:
        _font_cache[key] = ImageFont.truetype(YAHEI_BD if bold else YAHEI, size)
    return _font_cache[key]


INK = (30, 41, 59)
SUB = (100, 116, 139)
LINE = (203, 213, 225)
BLUE = (37, 99, 235)
BLUE_L = (219, 234, 254)
BLUE_M = (147, 197, 253)
TEAL = (13, 148, 136)
TEAL_L = (204, 251, 241)
GREEN = (21, 128, 61)
GREEN_L = (220, 252, 231)
AMBER = (180, 83, 9)
AMBER_L = (254, 243, 199)
RED = (185, 28, 28)
RED_L = (254, 226, 226)
GRAY_L = (241, 245, 249)
GRAY_M = (226, 232, 240)
WHITE = (255, 255, 255)


def canvas(w, h, bg=WHITE):
    img = Image.new("RGB", (w, h), bg)
    return img, ImageDraw.Draw(img)


def tw(d, text, font):
    b = d.textbbox((0, 0), text, font=font)
    return b[2] - b[0], b[3] - b[1]


def rrect(d, box, fill, outline=None, radius=12, width=2):
    d.rounded_rectangle(box, radius=radius, fill=fill, outline=outline, width=width)


def ctext(d, box, text, font, fill=INK):
    x0, y0, x1, y1 = box
    w, h = tw(d, text, font)
    d.text((x0 + (x1 - x0 - w) / 2, y0 + (y1 - y0 - h) / 2 - 2), text, font=font, fill=fill)


def wrap(d, text, font, max_w):
    lines, cur = [], ""
    for ch in text:
        if ch == "\n":
            lines.append(cur)
            cur = ""
            continue
        if tw(d, cur + ch, font)[0] > max_w and cur:
            lines.append(cur)
            cur = ch
        else:
            cur += ch
    if cur:
        lines.append(cur)
    return lines


def para(d, box, text, font, fill=INK, max_w=None, line_gap=8, align="left"):
    x0, y0, x1, y1 = box
    max_w = max_w or (x1 - x0)
    lines = []
    for seg in text.split("\n"):
        lines.extend(wrap(d, seg, font, max_w))
    y = y0
    for ln in lines:
        w = tw(d, ln, font)[0]
        x = x0 if align == "left" else (x0 + (x1 - x0 - w) / 2 if align == "center" else x1 - w)
        d.text((x, y), ln, font=font, fill=fill)
        y += tw(d, ln, font)[1] + line_gap
    return y


def arrow_h(d, x0, x1, y, color=BLUE, width=3, head=11):
    d.line([(x0, y), (x1 - head, y)], fill=color, width=width)
    d.polygon([(x1, y), (x1 - head, y - head * 0.62), (x1 - head, y + head * 0.62)], fill=color)


def arrow_v(d, y0, y1, x, color=BLUE, width=3, head=11):
    d.line([(x, y0), (x, y1 - head)], fill=color, width=width)
    d.polygon([(x, y1), (x - head * 0.62, y1 - head), (x + head * 0.62, y1 - head)], fill=color)


def title(img, d, text, sub=None):
    w = img.width
    d.rectangle([0, 0, w, 74], fill=(248, 250, 252))
    d.line([(0, 74), (w, 74)], fill=LINE, width=2)
    d.text((36, 20), text, font=F(30, True), fill=INK)
    if sub:
        sw = tw(d, sub, F(17))[0]
        d.text((w - 36 - sw, 30), sub, font=F(17), fill=SUB)


def save(img, name):
    path = os.path.join(OUT, name)
    img.save(path, "PNG")
    print("saved:", path, img.size)


# ---------------------------------------------------------------- 图1 系统总体架构
def fig_arch():
    img, d = canvas(1680, 1020)
    title(img, d, "图 1  系统总体架构图", "客户端 Unity · 服务端 .NET 8 · UDP + Protocol Buffers")

    # 客户端大框
    rrect(d, (40, 110, 1000, 700), (248, 250, 252), LINE, 18, 2)
    d.text((70, 130), "客户端（Unity 2022.3 LTS / C#）", font=F(24, True), fill=BLUE)

    layers = [
        ("表现层", "帧间插值渲染 · 角色动画 · 特效与弹道 · 准星与名牌 · 登录/大厅/房间 UI", BLUE_L, BLUE),
        ("逻辑层（纯 C# 确定性模拟）", "逻辑世界 Tick · Q16.16 定点数数学库 · 查表三角函数 · 确定性随机 · 指令与快照 · 逻辑事件", TEAL_L, TEAL),
        ("传输层", "网络管理器（UDP 收发 · 心跳） · 消息分发 · Protobuf 编解码 · 事件总线", (237, 233, 254), (109, 40, 217)),
    ]
    y = 180
    for name, desc, fill, edge in layers:
        rrect(d, (70, y, 970, y + 130), fill, edge, 12, 2)
        d.text((92, y + 16), name, font=F(21, True), fill=edge)
        para(d, (92, y + 54, 950, y + 120), desc, F(16), SUB, max_w=858, line_gap=6)
        y += 158

    # 服务端大框
    rrect(d, (1080, 110, 1640, 700), (248, 250, 252), LINE, 18, 2)
    d.text((1110, 130), "服务端（.NET 8 控制台）", font=F(24, True), fill=GREEN)

    slayers = [
        ("接入层", "UDP 监听 · 会话管理\n心跳 · 超时扫描"),
        ("业务层", "登录认证 · 房间管理\n房间列表 · 玩家进出"),
        ("中继层", "按加入顺序分配座位\n封帧泵（齐了就封 / 200ms 补空）\n帧广播（可多帧合包）"),
    ]
    y = 180
    heights = [130, 130, 190]
    for (name, desc), hh in zip(slayers, heights):
        rrect(d, (1110, y, 1610, y + hh), GREEN_L, GREEN, 12, 2)
        d.text((1132, y + 14), name, font=F(21, True), fill=GREEN)
        para(d, (1132, y + 52, 1590, y + hh - 10), desc, F(16), SUB, max_w=458, line_gap=6)
        y += hh + 20

    # 中间链路
    d.rectangle([1000, 280, 1080, 560], fill=WHITE)
    arrow_h(d, 1005, 1078, 350, BLUE, 4, 12)
    arrow_h(d, 1078, 1005, 412, GREEN, 4, 12)
    d.text((1040 - tw(d, "UDP : 7777", F(17, True))[0] / 2, 252), "UDP : 7777", font=F(17, True), fill=INK)
    d.text((1040 - tw(d, "Protocol Buffers", F(14))[0] / 2, 284), "Protocol Buffers", font=F(14), fill=SUB)
    d.text((1040 - tw(d, "指令上行", F(14))[0] / 2, 476), "指令上行", font=F(14), fill=BLUE)
    d.text((1040 - tw(d, "帧广播下行", F(14))[0] / 2, 500), "帧广播下行", font=F(14), fill=GREEN)

    # 支撑层
    rrect(d, (40, 740, 1640, 900), (255, 251, 235), (217, 119, 6), 16, 2)
    d.text((70, 758), "支撑与工具链", font=F(23, True), fill=AMBER)
    support = [
        ("资源热更新", "AssetBundle 打包\n对比文件增量更新"),
        ("编辑器工具", "打包工具 · 对比文件生成\n资源上传 · 事件总线检视"),
        ("配置导出", "角色/载具配置 → JSON\n供服务端读取"),
        ("确定性自测套件", "基础库 · 逻辑世界 · 预测回滚\n战斗 · 协议与全链路回环"),
    ]
    x = 70
    for name, desc in support:
        rrect(d, (x, 800, x + 370, 888), WHITE, (253, 230, 138), 10, 2)
        d.text((x + 16, 810), name, font=F(18, True), fill=AMBER)
        para(d, (x + 16, 842, x + 356, 884), desc, F(14), SUB, max_w=340, line_gap=4)
        x += 388

    # 底部说明
    para(d, (40, 918, 1640, 1000),
         "说明：逻辑层为不含引擎依赖的纯 C# 源码，客户端与服务端复用同一份实现；"
         "服务端不持有游戏状态，仅承担封帧与广播，比赛结果由各端本地确定性模拟演算得出。",
         F(16), SUB, max_w=1600, line_gap=6)
    save(img, "fig_arch.png")


# ---------------------------------------------------------------- 图2 功能模块图
def fig_modules():
    img, d = canvas(1800, 900)
    title(img, d, "图 2  系统功能模块图", "7 大功能模块")

    root = (700, 110, 1100, 175)
    rrect(d, root, BLUE, None, 14)
    ctext(d, root, "多人在线FPS对战系统", F(24, True), WHITE)

    mods = [
        ("登录认证", BLUE_L, BLUE, ["账号登录", "会话令牌", "重复登录顶号"]),
        ("大厅与房间", BLUE_L, BLUE, ["房间列表", "创建/加入/离开", "房主开局"]),
        ("对战核心", TEAL_L, TEAL, ["15Hz 逻辑帧", "指令上报", "封帧广播"]),
        ("战斗系统", TEAL_L, TEAL, ["命中判定", "伤害与死亡", "弹药与换弹"]),
        ("表现层", (237, 233, 254), (109, 40, 217), ["插值渲染", "动画与特效", "界面与名牌"]),
        ("资源层", (255, 251, 235), AMBER, ["AssetBundle", "增量热更", "脚本热更预留"]),
        ("工具链", (255, 251, 235), AMBER, ["编辑器工具", "配置导出", "确定性自测"]),
    ]

    # 水平总线
    bus_y = 232
    d.line([(150, bus_y), (1650, bus_y)], fill=LINE, width=3)
    d.line([(900, 176), (900, bus_y)], fill=LINE, width=3)

    bw, gap = 208, 24
    x = (1800 - (bw * 7 + gap * 6)) / 2
    for name, fill, edge, items in mods:
        cx = x + bw / 2
        d.line([(cx, bus_y), (cx, 296)], fill=LINE, width=3)
        box = (x, 296, x + bw, 362)
        rrect(d, box, fill, edge, 12, 2)
        ctext(d, box, name, F(20, True), edge)
        y = 380
        for it in items:
            rrect(d, (x + 12, y, x + bw - 12, y + 52), WHITE, LINE, 8, 1)
            ctext(d, (x + 12, y, x + bw - 12, y + 52), it, F(15), SUB)
            y += 62
        x += bw + gap

    para(d, (60, 800, 1740, 880),
         "说明：模块划分遵循“表现与逻辑分离”原则——战斗系统与对战核心运行在无引擎依赖的纯 C# 逻辑层，"
         "表现层只消费逻辑事件并做插值显示，因此同一份逻辑可在客户端与服务端复用。",
         F(16), SUB, max_w=1680, line_gap=6)
    save(img, "fig_modules.png")


# ---------------------------------------------------------------- 图3 用户操作流程
def fig_flow():
    img, d = canvas(1680, 900)
    title(img, d, "图 3  用户操作流程图", "登录 → 大厅 → 房间 → 对战")

    stages = [
        ("① 登录", BLUE, BLUE_L, ["启动客户端", "输入账号与口令", "连接服务器并校验", "成功后进入大厅"]),
        ("② 大厅", TEAL, TEAL_L, ["创建房间", "或拉取房间列表", "选择房间并加入", "展示房间号与状态"]),
        ("③ 房间", (109, 40, 217), (237, 233, 254), ["展示成员列表", "房主点击开始", "成员可离开房间", "等待全员就绪"]),
        ("④ 对战", AMBER, (255, 251, 235), ["下发种子与座位表", "按座位确定性出生", "15Hz 逻辑帧推进", "渲染帧插值显示"]),
    ]

    bw, gap = 350, 70
    x = (1680 - (bw * 4 + gap * 3)) / 2
    for i, (name, edge, fill, items) in enumerate(stages):
        head = (x, 150, x + bw, 218)
        rrect(d, head, edge, None, 14)
        ctext(d, head, name, F(24, True), WHITE)

        body_h = 46 + len(items) * 52
        body = (x, 218, x + bw, 218 + body_h)
        rrect(d, body, fill, edge, 14, 2)
        y = 232
        for it in items:
            rrect(d, (x + 16, y, x + bw - 16, y + 44), WHITE, LINE, 8, 1)
            ctext(d, (x + 16, y, x + bw - 16, y + 44), it, F(16), SUB)
            y += 52

        if i < 3:
            arrow_h(d, x + bw + 8, x + bw + gap - 8, 184, SUB, 4, 14)
        x += bw + gap

    # 底部横条：数据流
    rrect(d, (100, 660, 1580, 800), (248, 250, 252), LINE, 14, 2)
    d.text((130, 676), "对战时序", font=F(20, True), fill=INK)
    steps = ["采集输入并量化", "本地立即预测", "打包上行", "服务端封帧", "广播全员指令", "校正与回滚重演", "插值渲染"]
    sx = 130
    for i, s in enumerate(steps):
        w = tw(d, s, F(16))[0] + 34
        rrect(d, (sx, 712, sx + w, 772), WHITE, BLUE_M, 10, 2)
        ctext(d, (sx, 712, sx + w, 772), s, F(16), INK)
        sx += w
        if i < len(steps) - 1:
            arrow_h(d, sx + 4, sx + 26, 742, BLUE, 3, 8)
            sx += 30

    para(d, (100, 820, 1580, 890),
         "说明：本地操作在按下瞬间即被预测执行，无需等待服务器往返；服务器封帧广播到达后，"
         "若本地预测与权威指令不一致则回滚到分歧帧之前并重演，因此既保证手感又保证各端结果一致。",
         F(16), SUB, max_w=1480, line_gap=6)
    save(img, "fig_flow.png")


# ---------------------------------------------------------------- 图4 甘特图
def fig_gantt():
    img, d = canvas(1680, 940)
    title(img, d, "图 4  项目建设里程碑甘特图", "总工期 16 周 · 12 个阶段 · 5 人小组")

    tasks = [
        ("M0  需求分析与技术选型", 1, 1, BLUE),
        ("M1  联机骨架", 2, 1, BLUE),
        ("M2  确定性基础库", 3, 1, TEAL),
        ("M3  逻辑世界", 4, 2, TEAL),
        ("M4  战斗结算", 6, 1, TEAL),
        ("M5  协议编解码", 7, 1, TEAL),
        ("M6  服务端中继", 8, 1, (109, 40, 217)),
        ("M7  客户端预测与回滚", 9, 2, (109, 40, 217)),
        ("M8  表现层与动画", 11, 2, AMBER),
        ("M9  大厅与房间闭环", 13, 1, AMBER),
        ("M10 资源热更新与工具", 14, 1, GREEN),
        ("M11 集成测试与交付", 15, 2, GREEN),
    ]

    left, top = 420, 150
    row_h = 52
    grid_w = 1180
    week_w = grid_w / 16.0

    # 表头
    d.rectangle([left - 260, top - 44, left + grid_w, top], fill=GRAY_L)
    d.text((left - 246, top - 36), "阶段", font=F(17, True), fill=INK)
    for wk in range(1, 17):
        x = left + (wk - 1) * week_w
        if wk % 2 == 1 or week_w > 60:
            w = tw(d, str(wk), F(14))[0]
            d.text((x + (week_w - w) / 2, top - 34), str(wk), font=F(14), fill=SUB)
    hw = tw(d, "周次", F(15))[0]
    d.text((left + (grid_w - hw) / 2, top - 62), "周次", font=F(15, True), fill=SUB)

    # 网格 + 行
    for wk in range(0, 17):
        x = left + wk * week_w
        d.line([(x, top), (x, top + len(tasks) * row_h)], fill=GRAY_M, width=1)
    # 里程碑分隔（每 4 周加粗）
    for wk in (4, 8, 12, 16):
        x = left + wk * week_w
        d.line([(x, top), (x, top + len(tasks) * row_h)], fill=LINE, width=2)

    for i, (name, start, dur, color) in enumerate(tasks):
        y = top + i * row_h
        if i % 2 == 1:
            d.rectangle([left - 260, y, left + grid_w, y + row_h], fill=(252, 253, 255))
        d.text((left - 246, y + 14), name, font=F(17), fill=INK)
        bx0 = left + (start - 1) * week_w + 3
        bx1 = left + (start - 1 + dur) * week_w - 3
        rrect(d, (bx0, y + 10, bx1, y + row_h - 10), color, None, 8)
        label = f"{dur} 周"
        if tw(d, label, F(14))[0] < (bx1 - bx0) - 10:
            ctext(d, (bx0, y + 10, bx1, y + row_h - 10), label, F(14), WHITE)
    d.line([(left, top + len(tasks) * row_h), (left + grid_w, top + len(tasks) * row_h)], fill=LINE, width=2)

    # 阶段分组说明
    legend = [("选型与骨架（M0–M1）", BLUE), ("确定性核心（M2–M5）", TEAL),
              ("同步与预测（M6–M7）", (109, 40, 217)), ("表现与闭环（M8–M9）", AMBER),
              ("工具与交付（M10–M11）", GREEN)]
    lx = 420
    for text, color in legend:
        rrect(d, (lx, 830, lx + 22, 852), color, None, 5)
        d.text((lx + 32, 830), text, font=F(16), fill=SUB)
        lx += tw(d, text, F(16))[0] + 90

    para(d, (60, 880, 1620, 930),
         "说明：每个阶段结束时须通过对应的确定性自测（全绿）方可进入下一阶段；M3、M7、M8、M11 为工期最长或风险最高的阶段，"
         "已预留缓冲周。", F(16), SUB, max_w=1560, line_gap=6)
    save(img, "fig_gantt.png")


# ---------------------------------------------------------------- 图5 工作量分布
def fig_workload():
    img, d = canvas(1680, 940)
    title(img, d, "图 5  各阶段工作量分布图", "总投入 800 人时（5 人 × 16 周 × 10 小时/周）")

    data = [
        ("M0  需求分析与技术选型", 40, BLUE),
        ("M1  联机骨架", 60, BLUE),
        ("M2  确定性基础库", 60, TEAL),
        ("M3  逻辑世界", 100, TEAL),
        ("M4  战斗结算", 60, TEAL),
        ("M5  协议编解码", 40, TEAL),
        ("M6  服务端中继", 80, (109, 40, 217)),
        ("M7  客户端预测与回滚", 100, (109, 40, 217)),
        ("M8  表现层与动画", 100, AMBER),
        ("M9  大厅与房间闭环", 60, AMBER),
        ("M10 资源热更新与工具", 60, GREEN),
        ("M11 集成测试与交付", 40, GREEN),
    ]
    total = sum(v for _, v, _ in data)
    assert total == 800, total

    left, right = 430, 1480
    top, row_h = 150, 56
    maxv = 100
    scale = (right - left) / maxv

    # 轴
    for tick in range(0, 101, 20):
        x = left + tick * scale
        d.line([(x, top - 10), (x, top + len(data) * row_h)], fill=GRAY_M, width=1)
        w = tw(d, str(tick), F(15))[0]
        d.text((x - w / 2, top + len(data) * row_h + 10), str(tick), font=F(15), fill=SUB)
    d.text((left + (right - left) / 2 - 30, top + len(data) * row_h + 42), "人时", font=F(15, True), fill=SUB)

    for i, (name, v, color) in enumerate(data):
        y = top + i * row_h
        if i % 2 == 1:
            d.rectangle([left, y, right, y + row_h], fill=(252, 253, 255))
        d.text((left - 400, y + 16), name, font=F(17), fill=INK)
        rrect(d, (left, y + 9, left + v * scale, y + row_h - 9), color, None, 7)
        label = f"{v}"
        d.text((left + v * scale + 12, y + 16), label, font=F(16, True), fill=color)

    # 合计
    ty = top + len(data) * row_h + 80
    rrect(d, (left - 400, ty, right, ty + 64), GRAY_L, LINE, 12, 2)
    d.text((left - 370, ty + 18), "合计", font=F(20, True), fill=INK)
    d.text((left - 250, ty + 18), f"{total} 人时", font=F(20, True), fill=BLUE)
    d.text((left + 60, ty + 22), "其中确定性核心（M2–M5）与同步预测（M6–M7）合计 440 人时，占总投入 55%，是技术风险与工作量的集中区。",
           font=F(16), fill=SUB)
    save(img, "fig_workload.png")


# ---------------------------------------------------------------- 图6 风险矩阵
def fig_risk():
    img, d = canvas(1560, 1310)
    title(img, d, "图 6  技术风险与对策矩阵", "横轴＝发生概率　纵轴＝影响程度")

    gx, gy = 310, 200          # 矩阵左上角
    cw, ch = 330, 175          # 单元格尺寸
    cols = ["低", "中", "高"]
    rows = ["高", "中", "低"]

    cell_fill = {
        (0, 0): GREEN_L, (0, 1): GREEN_L, (0, 2): AMBER_L,
        (1, 0): GREEN_L, (1, 1): AMBER_L, (1, 2): RED_L,
        (2, 0): AMBER_L, (2, 1): AMBER_L, (2, 2): RED_L,
    }
    cell_edge = {
        (0, 0): GREEN, (0, 1): GREEN, (0, 2): AMBER,
        (1, 0): GREEN, (1, 1): AMBER, (1, 2): RED,
        (2, 0): AMBER, (2, 1): AMBER, (2, 2): RED,
    }

    # 单元格（cy: 0=高影响在最上）
    for ci in range(3):      # 影响（行）
        for cj in range(3):  # 概率（列）
            x0 = gx + cj * cw
            y0 = gy + ci * ch
            rrect(d, (x0, y0, x0 + cw, y0 + ch), cell_fill[(ci, cj)], cell_edge[(ci, cj)], 10, 2)

    # 风险落点
    placement = {
        (1, 2): ["1 跨平台浮点差异", "4 网络与确定性经验缺口", "7 进度延期"],
        (1, 1): ["5 美术素材不足"],
        (2, 2): ["2 丢包与乱序", "6 无公网服务器"],
        (2, 1): ["3 客户端预测落空"],
    }
    for (ci, cj), items in placement.items():
        x0 = gx + cj * cw
        y0 = gy + ci * ch
        y = y0 + 22
        for it in items:
            rrect(d, (x0 + 14, y, x0 + cw - 14, y + 42), WHITE, cell_edge[(ci, cj)], 8, 1)
            d.text((x0 + 26, y + 11), it, font=F(15), fill=INK)
            y += 50

    # 轴标签
    for cj, name in enumerate(cols):
        w = tw(d, name, F(20, True))[0]
        d.text((gx + cj * cw + (cw - w) / 2, gy - 56), name, font=F(20, True), fill=INK)
    for ci, name in enumerate(rows):
        w = tw(d, name, F(20, True))[0]
        d.text((gx - 34 - w, gy + ci * ch + ch / 2 - 14), name, font=F(20, True), fill=INK)

    d.text((gx - 268, gy - 56), "影响程度", font=F(18, True), fill=SUB)
    lap = "发生概率 →"
    d.text((gx + (3 * cw - tw(d, lap, F(18, True))[0]) / 2, gy + 3 * ch + 16), lap, font=F(18, True), fill=SUB)

    # 色带说明
    legend = [("可接受", GREEN_L, GREEN), ("需关注", AMBER_L, AMBER), ("优先处置", RED_L, RED)]
    lx = gx
    ly = gy + 3 * ch + 58
    for text, fill, edge in legend:
        rrect(d, (lx, ly, lx + 26, ly + 24), fill, edge, 6, 2)
        d.text((lx + 36, ly), text, font=F(16), fill=SUB)
        lx += tw(d, text, F(16))[0] + 104

    # 主要对策（整幅横条，双列排布）
    py0 = ly + 62
    rrect(d, (60, py0, 1500, py0 + 330), (248, 250, 252), LINE, 14, 2)
    d.text((88, py0 + 16), "主要对策", font=F(21, True), fill=INK)
    counter = [
        "全逻辑层使用定点数运算、查表三角函数与确定性随机，禁用浮点",
        "权威帧缓冲容忍乱序与重复；超时补空指令推进，不卡全场",
        "快照回滚重演，将预测落空限制在视觉层面，不影响判定一致性",
        "分阶段学习并以自测闸门验收，把能力风险切成小块",
        "采用开放授权素材，优先保证功能完整",
        "以内网演示为主，服务端为单文件控制台程序，降低部署依赖",
        "范围冻结 + 敏感性预案（削减房间人数、关闭热更、缩减自测）",
    ]
    col_x = [88, 812]
    col_w = 620
    for i, c in enumerate(counter):
        ci = 0 if i < 4 else 1
        ri = i if i < 4 else i - 4
        x = col_x[ci]
        y = py0 + 62 + ri * 62
        d.text((x, y), f"{i + 1}.", font=F(15, True), fill=BLUE)
        for k, ln in enumerate(wrap(d, c, F(15), col_w)):
            d.text((x + 28, y + k * 24), ln, font=F(15), fill=SUB)

    para(d, (60, py0 + 348, 1500, py0 + 410),
         "说明：红色区域为“高影响且需优先处置”的风险；本方案通过确定性数值基座、封帧超时补空与快照回滚三项机制，"
         "将前两项最高风险转化为可控的工程实现问题。", F(16), SUB, max_w=1440, line_gap=6)
    save(img, "fig_risk.png")


if __name__ == "__main__":
    fig_arch()
    fig_modules()
    fig_flow()
    fig_gantt()
    fig_workload()
    fig_risk()
    print("\nALL FIGURES DONE ->", OUT)
