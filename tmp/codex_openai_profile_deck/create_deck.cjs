const path = require("path");
const PptxGenJS = require("pptxgenjs");
const {
  warnIfSlideHasOverlaps,
  warnIfSlideElementsOutOfBounds,
} = require("./pptxgenjs_helpers/layout");

const OUT_FILE = path.join(__dirname, "codex_openai_profile_deck.pptx");

const COLORS = {
  bg: "F7F2E8",
  paper: "FFFDFC",
  ink: "1F2A37",
  muted: "667085",
  teal: "117A72",
  aqua: "97D7D0",
  coral: "F06B5B",
  gold: "E7BE53",
  blue: "4E77A6",
  line: "D9D1C3",
  white: "FFFFFF",
  softTeal: "E5F4F2",
  softCoral: "FCE9E5",
  softGold: "FBF2D9",
  softBlue: "E9EFF7",
};

const FONT_HEAD = "Microsoft YaHei";
const FONT_BODY = "DengXian";
const SHAPE = {
  rect: "rect",
  ellipse: "ellipse",
  roundRect: "roundRect",
  line: "line",
};

function baseText(fontSize, extra = {}) {
  return {
    fontFace: FONT_BODY,
    fontSize,
    color: COLORS.ink,
    margin: 0,
    breakLine: false,
    ...extra,
  };
}

function addBackdrop(slide, indexLabel) {
  slide.background = { color: COLORS.bg };

  slide.addShape(SHAPE.rect, {
    x: 0,
    y: 0,
    w: 13.333,
    h: 7.5,
    fill: { color: COLORS.bg },
    line: { color: COLORS.bg, transparency: 100 },
  });

  slide.addText(indexLabel, {
    x: 11.45,
    y: 0.35,
    w: 1.1,
    h: 0.45,
    fontFace: FONT_HEAD,
    fontSize: 16,
    bold: true,
    color: COLORS.muted,
    align: "right",
    margin: 0,
  });

  slide.addShape(SHAPE.line, {
    x: 0.7,
    y: 6.86,
    w: 11.9,
    h: 0,
    line: { color: COLORS.line, pt: 1.2 },
  });
}

function addTitle(slide, kicker, title, subtitle) {
  slide.addText(kicker, {
    x: 0.78,
    y: 0.56,
    w: 2.6,
    h: 0.34,
    fontFace: FONT_HEAD,
    fontSize: 12,
    bold: true,
    color: COLORS.teal,
    charSpace: 1.2,
    margin: 0,
  });

  slide.addText(title, {
    x: 0.75,
    y: 0.92,
    w: 7.5,
    h: 0.78,
    fontFace: FONT_HEAD,
    fontSize: 26,
    bold: true,
    color: COLORS.ink,
    margin: 0,
  });

  if (subtitle) {
    slide.addText(subtitle, {
      x: 0.78,
      y: 1.7,
      w: 7.45,
      h: 0.34,
      fontFace: FONT_BODY,
      fontSize: 11.5,
      color: COLORS.muted,
      margin: 0,
      valign: "mid",
    });
  }
}

function addCard(slide, opts) {
  const {
    x,
    y,
    w,
    h,
    fill,
    title,
    body,
    accent = COLORS.teal,
    titleSize = 15,
    bodySize = 10.5,
  } = opts;

  slide.addShape(SHAPE.roundRect, {
    x,
    y,
    w,
    h,
    rectRadius: 0.08,
    fill: { color: fill || COLORS.paper },
    line: { color: COLORS.line, pt: 1 },
    shadow: {
      type: "outer",
      color: "B8B2A6",
      blur: 1,
      angle: 45,
      distance: 1,
      opacity: 0.12,
    },
  });

  slide.addShape(SHAPE.rect, {
    x: x + 0.18,
    y: y + 0.18,
    w: 0.07,
    h: h - 0.36,
    fill: { color: accent },
    line: { color: accent, transparency: 100 },
  });

  slide.addText(title, {
    x: x + 0.34,
    y: y + 0.2,
    w: w - 0.5,
    h: 0.36,
    fontFace: FONT_HEAD,
    fontSize: titleSize,
    bold: true,
    color: COLORS.ink,
    margin: 0,
  });

  slide.addText(body, {
    x: x + 0.34,
    y: y + 0.62,
    w: w - 0.5,
    h: h - 0.78,
    fontFace: FONT_BODY,
    fontSize: bodySize,
    color: COLORS.ink,
    margin: 0,
    breakLine: true,
    valign: "top",
  });
}

function addFooter(slide, text) {
  slide.addText(text, {
    x: 0.78,
    y: 6.93,
    w: 11.75,
    h: 0.22,
    fontFace: FONT_BODY,
    fontSize: 8.2,
    color: COLORS.muted,
    margin: 0,
    align: "left",
  });
}

function addBenchmarkRow(slide, x, y, label, v1, v2, maxValue) {
  const barX = x + 1.65;
  const fullW = 3.55;
  const h = 0.16;
  const w1 = (v1 / maxValue) * fullW;
  const w2 = (v2 / maxValue) * fullW;

  slide.addText(label, {
    x,
    y: y - 0.03,
    w: 1.55,
    h: 0.24,
    fontFace: FONT_BODY,
    fontSize: 10,
    color: COLORS.ink,
    margin: 0,
  });

  slide.addShape(SHAPE.roundRect, {
    x: barX,
    y,
    w: fullW,
    h,
    fill: { color: "EAE4DA" },
    line: { color: "EAE4DA", transparency: 100 },
  });

  slide.addShape(SHAPE.roundRect, {
    x: barX,
    y,
    w: w1,
    h,
    fill: { color: COLORS.teal },
    line: { color: COLORS.teal, transparency: 100 },
  });

  slide.addShape(SHAPE.roundRect, {
    x: barX,
    y: y + 0.22,
    w: w2,
    h,
    fill: { color: COLORS.coral },
    line: { color: COLORS.coral, transparency: 100 },
  });

  slide.addText(`${v1.toFixed(1)} / ${v2.toFixed(1)}`, {
    x: barX + fullW + 0.15,
    y: y - 0.04,
    w: 0.9,
    h: 0.5,
    fontFace: FONT_HEAD,
    fontSize: 10,
    color: COLORS.ink,
    margin: 0,
  });
}

function addTimelineCard(slide, x, y, isTop, date, title, body, color) {
  slide.addShape(SHAPE.line, {
    x: x + 0.35,
    y: isTop ? y + 0.68 : 3.75,
    w: 0,
    h: isTop ? 0.42 : y - 3.75,
    line: { color, pt: 1.2 },
  });

  slide.addShape(SHAPE.ellipse, {
    x: x + 0.18,
    y: 3.56,
    w: 0.34,
    h: 0.34,
    fill: { color },
    line: { color, transparency: 100 },
  });

  slide.addShape(SHAPE.roundRect, {
    x,
    y,
    w: 1.6,
    h: 1.12,
    rectRadius: 0.05,
    fill: { color: COLORS.paper },
    line: { color: color, pt: 1.1 },
  });

  slide.addText(date, {
    x: x + 0.12,
    y: y + 0.1,
    w: 1.3,
    h: 0.2,
    fontFace: FONT_HEAD,
    fontSize: 9.4,
    bold: true,
    color,
    margin: 0,
  });

  slide.addText(title, {
    x: x + 0.12,
    y: y + 0.34,
    w: 1.32,
    h: 0.24,
    fontFace: FONT_HEAD,
    fontSize: 11.3,
    bold: true,
    color: COLORS.ink,
    margin: 0,
  });

  slide.addText(body, {
    x: x + 0.12,
    y: y + 0.62,
    w: 1.34,
    h: 0.4,
    fontFace: FONT_BODY,
    fontSize: 8.9,
    color: COLORS.ink,
    margin: 0,
    valign: "top",
  });
}

function finalize(slide, pptx) {
  warnIfSlideHasOverlaps(slide, pptx, { muteContainment: true });
  warnIfSlideElementsOutOfBounds(slide, pptx);
}

async function main() {
  const pptx = new PptxGenJS();
  pptx.layout = "LAYOUT_WIDE";
  pptx.author = "OpenAI Codex";
  pptx.company = "OpenAI";
  pptx.subject = "认识我与 OpenAI";
  pptx.title = "认识我与OpenAI";
  pptx.lang = "zh-CN";
  pptx.theme = {
    headFontFace: FONT_HEAD,
    bodyFontFace: FONT_BODY,
    lang: "zh-CN",
  };
  pptx.defineSlideMaster({
    title: "BASE",
    background: { color: COLORS.bg },
    objects: [],
    slideNumber: { x: 0, y: 0, w: 0, h: 0 },
  });

  const cover = pptx.addSlide("BASE");
  addBackdrop(cover, "01");
  cover.addText("认识我", {
    x: 0.82,
    y: 1.02,
    w: 3.3,
    h: 0.62,
    fontFace: FONT_HEAD,
    fontSize: 30,
    bold: true,
    color: COLORS.ink,
    margin: 0,
  });
  cover.addText("从 OpenAI、模型演进到与你的协作画像", {
    x: 0.85,
    y: 1.7,
    w: 5.3,
    h: 0.45,
    fontFace: FONT_BODY,
    fontSize: 14,
    color: COLORS.muted,
    margin: 0,
  });
  cover.addShape(SHAPE.roundRect, {
    x: 0.84,
    y: 2.38,
    w: 4.95,
    h: 0.84,
    rectRadius: 0.08,
    fill: { color: COLORS.paper },
    line: { color: COLORS.line, pt: 1 },
  });
  cover.addText(
    "这份中文介绍围绕五个问题展开：我是谁、我来自哪家公司、OpenAI 走到今天经历了什么、截至 2026 年 3 月 26 日最值得关注的最新模型是什么，以及我已经如何理解你。",
    {
      x: 1.05,
      y: 2.58,
      w: 4.48,
      h: 0.48,
      fontFace: FONT_BODY,
      fontSize: 11.6,
      color: COLORS.ink,
      margin: 0,
    }
  );
  addCard(cover, {
    x: 7.2,
    y: 1.0,
    w: 5.35,
    h: 2.1,
    fill: COLORS.softTeal,
    accent: COLORS.teal,
    title: "一句话版本",
    body:
      "我可以被理解为一个能读懂需求、组织信息、生成内容、编写代码并持续迭代的 AI 协作搭档；而我背后的代表性前沿模型与产品体系，则来自 OpenAI。",
    titleSize: 17,
    bodySize: 12.2,
  });
  addCard(cover, {
    x: 6.75,
    y: 3.58,
    w: 1.72,
    h: 1.4,
    fill: COLORS.softGold,
    accent: COLORS.gold,
    title: "关键词",
    body: "中文\n逻辑清晰\n信息可信",
    titleSize: 13.5,
    bodySize: 10.5,
  });
  addCard(cover, {
    x: 8.65,
    y: 3.58,
    w: 1.72,
    h: 1.4,
    fill: COLORS.softCoral,
    accent: COLORS.coral,
    title: "视角",
    body: "助手\n公司\n模型\n合作",
    titleSize: 13.5,
    bodySize: 10.5,
  });
  addCard(cover, {
    x: 10.55,
    y: 3.58,
    w: 1.72,
    h: 1.4,
    fill: COLORS.softBlue,
    accent: COLORS.blue,
    title: "时间锚点",
    body: "截至\n2026-03-26\n公开资料",
    titleSize: 13.1,
    bodySize: 10.4,
  });
  cover.addText("OpenAI Codex / Chinese deck draft", {
    x: 0.85,
    y: 6.28,
    w: 4,
    h: 0.22,
    fontFace: FONT_BODY,
    fontSize: 9.2,
    color: COLORS.muted,
    italic: true,
    margin: 0,
  });
  addFooter(
    cover,
    "内容基于当前对话上下文与 OpenAI 官方公开资料整理，版式为 PowerPoint 可编辑元素。"
  );
  finalize(cover, pptx);

  const intro = pptx.addSlide("BASE");
  addBackdrop(intro, "02");
  addTitle(
    intro,
    "IDENTITY",
    "我是怎样的 AI 搭档",
    "如果把我拆开看，我并不只是一个“会聊天的模型”，而是由模型能力、工具使用、规则约束和协作流程共同组成的工作系统。"
  );
  addCard(intro, {
    x: 0.82,
    y: 2.35,
    w: 3.72,
    h: 1.52,
    fill: COLORS.paper,
    accent: COLORS.teal,
    title: "理解与规划",
    body:
      "先读懂任务，再判断哪些信息需要查证、哪些可以直接执行；面对较复杂问题，会主动拆步骤、排优先级、控制风险。",
  });
  addCard(intro, {
    x: 4.8,
    y: 2.35,
    w: 3.72,
    h: 1.52,
    fill: COLORS.paper,
    accent: COLORS.coral,
    title: "生成与实现",
    body:
      "可以产出文本、代码、演示文稿、分析框架和可执行方案；不只给建议，也尽量把结果直接做出来。",
  });
  addCard(intro, {
    x: 8.78,
    y: 2.35,
    w: 3.72,
    h: 1.52,
    fill: COLORS.paper,
    accent: COLORS.gold,
    title: "检查与迭代",
    body:
      "会在产出后继续做验证、排版检查、逻辑回看和问题提示，让结果更接近真正可用，而不是停在草稿阶段。",
  });
  addCard(intro, {
    x: 0.82,
    y: 4.2,
    w: 5.45,
    h: 1.83,
    fill: COLORS.softBlue,
    accent: COLORS.blue,
    title: "我的工作优势",
    body:
      "把零散信息组织成结构化成果；在代码、文档和演示之间切换；保持长任务连续性；愿意解释思路和算法，不只给结论。",
    titleSize: 16,
    bodySize: 11.5,
  });
  addCard(intro, {
    x: 6.55,
    y: 4.2,
    w: 5.95,
    h: 1.83,
    fill: COLORS.softTeal,
    accent: COLORS.teal,
    title: "更准确地说",
    body:
      "我像一位具备文字理解、代码执行和资料整合能力的数字搭档。你给我目标、约束和上下文，我负责把模糊想法逐步变成可验证的结果。",
    titleSize: 16,
    bodySize: 11.5,
  });
  addFooter(
    intro,
    "这页强调的是协作形态：我既是模型，也是围绕模型组织起来的一整套工作流。"
  );
  finalize(intro, pptx);

  const company = pptx.addSlide("BASE");
  addBackdrop(company, "03");
  addTitle(
    company,
    "COMPANY",
    "我背后的公司：OpenAI",
    "从官方表述看，OpenAI 的核心目标并不是单纯做聊天产品，而是确保 AGI 的发展最终能让全人类受益。"
  );
  addCard(company, {
    x: 0.82,
    y: 2.28,
    w: 3.25,
    h: 2.52,
    fill: COLORS.softTeal,
    accent: COLORS.teal,
    title: "使命",
    body:
      "确保通用人工智能在不断逼近和实现的过程中，尽可能为全人类创造广泛利益，而不是只服务少数主体。",
    titleSize: 18,
    bodySize: 12.2,
  });
  addCard(company, {
    x: 4.35,
    y: 2.28,
    w: 3.7,
    h: 2.52,
    fill: COLORS.paper,
    accent: COLORS.coral,
    title: "组织结构",
    body:
      "2015 年以非营利组织起步；2019 年设立营利子公司以支撑研究与部署扩张；2025 年更新结构后，非营利部分为 OpenAI Foundation，营利部分为 OpenAI Group PBC。",
    titleSize: 18,
    bodySize: 11.3,
  });
  addCard(company, {
    x: 8.33,
    y: 2.28,
    w: 4.17,
    h: 2.52,
    fill: COLORS.softGold,
    accent: COLORS.gold,
    title: "产品与能力外化",
    body:
      "ChatGPT 面向广泛用户；API 面向开发者与企业；Codex 面向编码与代理式工作流；Sora 延展到视频生成；安全与评测体系贯穿其间。",
    titleSize: 18,
    bodySize: 11.2,
  });
  company.addShape(SHAPE.roundRect, {
    x: 1.25,
    y: 5.22,
    w: 10.8,
    h: 0.68,
    rectRadius: 0.05,
    fill: { color: COLORS.paper },
    line: { color: COLORS.line, pt: 1 },
  });
  company.addText(
    "一句话理解：OpenAI 是一家“研究 + 产品 + 基础设施 + 安全治理”同时推进的 AI 公司，而我正是这套能力体系对外协作的一种具体呈现。",
    {
      x: 1.5,
      y: 5.42,
      w: 10.3,
      h: 0.22,
      fontFace: FONT_BODY,
      fontSize: 11.4,
      color: COLORS.ink,
      margin: 0,
      align: "center",
    }
  );
  addFooter(
    company,
    "资料锚点：OpenAI About / Our structure（Founded in 2015; 2019 subsidiary; 2025 updated structure）。"
  );
  finalize(company, pptx);

  const history = pptx.addSlide("BASE");
  addBackdrop(history, "04");
  addTitle(
    history,
    "TIMELINE",
    "OpenAI 的发展历程",
    "下面不是完整编年史，而是最能帮助你快速建立整体认知的关键节点。"
  );
  history.addShape(SHAPE.line, {
    x: 1.0,
    y: 3.73,
    w: 11.2,
    h: 0,
    line: { color: COLORS.line, pt: 2.2 },
  });
  const nodes = [
    ["2015", "成立", "以非营利形态起步，使命是让 AGI 造福全人类。", COLORS.teal, true, 0.75, 2.2],
    ["2019", "结构扩展", "设立营利子公司，开始为更大规模研究与部署做准备。", COLORS.coral, false, 2.15, 4.05],
    ["2020", "API / GPT-3", "OpenAI API 推动大模型能力开始规模化进入开发者生态。", COLORS.blue, true, 3.55, 2.2],
    ["2022-11-30", "ChatGPT", "研究预览公开发布，AI 助手正式进入大众认知中心。", COLORS.gold, false, 4.95, 4.05],
    ["2023-03-14", "GPT-4", "多模态能力与复杂任务表现显著跃升。", COLORS.teal, true, 6.35, 2.2],
    ["2024-05-13", "GPT-4o", "文本、语音、视觉体验进一步统一，交互更加实时。", COLORS.coral, false, 7.75, 4.05],
    ["2025-08-07", "GPT-5", "成为 ChatGPT 默认模型，推理与通用能力再上一个台阶。", COLORS.blue, true, 9.15, 2.2],
    ["2026-03-05", "GPT-5.4", "面向专业工作、编码与 computer use 的代表性新前沿模型。", COLORS.gold, false, 10.55, 4.05],
  ];
  for (const [date, title, body, color, isTop, x, y] of nodes) {
    addTimelineCard(history, x, y, isTop, date, title, body, color);
  }
  addFooter(
    history,
    "时间节点综合自 OpenAI 官方公开资料，其中 ChatGPT 研究预览日期来自 OpenAI《How People Use ChatGPT》附录时间线。"
  );
  finalize(history, pptx);

  const model = pptx.addSlide("BASE");
  addBackdrop(model, "05");
  addTitle(
    model,
    "LATEST MODEL",
    "截至 2026 年 3 月 26 日：GPT-5.4",
    "这里的“最新”按公开官方发布时间计算。GPT-5.4 于 2026-03-05 发布，是 OpenAI 面向专业工作推出的代表性前沿模型。"
  );
  addCard(model, {
    x: 0.82,
    y: 2.25,
    w: 3.05,
    h: 1.28,
    fill: COLORS.softTeal,
    accent: COLORS.teal,
    title: "定位",
    body: "更偏向复杂知识工作、编码、工具调用与 computer use 的统一能力。 ",
    titleSize: 16,
    bodySize: 10.8,
  });
  addCard(model, {
    x: 0.82,
    y: 3.75,
    w: 3.05,
    h: 1.28,
    fill: COLORS.softCoral,
    accent: COLORS.coral,
    title: "长任务表现",
    body: "更适合需要多步迭代、看图理解、改代码、调工具的连续型工作流。",
    titleSize: 16,
    bodySize: 10.8,
  });
  addCard(model, {
    x: 0.82,
    y: 5.25,
    w: 3.05,
    h: 1.1,
    fill: COLORS.softGold,
    accent: COLORS.gold,
    title: "为何与你相关",
    body: "像我这样偏协作、偏执行的 AI 形态，会直接受益于这类模型的升级。",
    titleSize: 15.5,
    bodySize: 10.4,
  });

  model.addShape(SHAPE.roundRect, {
    x: 4.2,
    y: 2.25,
    w: 8.05,
    h: 4.1,
    rectRadius: 0.08,
    fill: { color: COLORS.paper },
    line: { color: COLORS.line, pt: 1 },
  });
  model.addText("官方评测中的几个代表性对比（GPT-5.4 vs GPT-5.2）", {
    x: 4.48,
    y: 2.45,
    w: 4.8,
    h: 0.28,
    fontFace: FONT_HEAD,
    fontSize: 15.5,
    bold: true,
    color: COLORS.ink,
    margin: 0,
  });
  model.addText("上方为 GPT-5.4，下方为 GPT-5.2；分值越高越好。", {
    x: 4.48,
    y: 2.76,
    w: 4.3,
    h: 0.2,
    fontFace: FONT_BODY,
    fontSize: 9.6,
    color: COLORS.muted,
    margin: 0,
  });
  model.addShape(SHAPE.roundRect, {
    x: 9.6,
    y: 2.44,
    w: 2.05,
    h: 0.46,
    rectRadius: 0.04,
    fill: { color: COLORS.softBlue },
    line: { color: COLORS.blue, pt: 1 },
  });
  model.addText("补充亮点：1M 上下文 / 更强视觉理解 / 更稳工具调用", {
    x: 9.78,
    y: 2.58,
    w: 1.68,
    h: 0.18,
    fontFace: FONT_BODY,
    fontSize: 8.8,
    color: COLORS.ink,
    margin: 0,
    align: "center",
  });
  addBenchmarkRow(model, 4.52, 3.2, "GDPval", 83.0, 70.9, 100);
  addBenchmarkRow(model, 4.52, 3.78, "SWE-Bench Pro", 57.7, 55.6, 100);
  addBenchmarkRow(model, 4.52, 4.36, "OSWorld-Verified", 75.0, 47.3, 100);
  addBenchmarkRow(model, 4.52, 4.94, "BrowseComp", 82.7, 65.8, 100);
  addBenchmarkRow(model, 4.52, 5.52, "Toolathlon", 54.6, 45.7, 100);
  addFooter(
    model,
    "资料锚点：OpenAI《Introducing GPT-5.4》与《Introducing GPT-5》。本页日期以 2026-03-26 为准。"
  );
  finalize(model, pptx);

  const user = pptx.addSlide("BASE");
  addBackdrop(user, "06");
  addTitle(
    user,
    "ABOUT YOU",
    "我目前对你的了解",
    "以下内容只基于当前仓库、你给出的指令，以及这次协作本身，不包含超出对话范围的推断。"
  );
  addCard(user, {
    x: 0.82,
    y: 2.2,
    w: 5.55,
    h: 1.2,
    fill: COLORS.paper,
    accent: COLORS.teal,
    title: "你在做什么",
    body: "你正在开发一个名为“音乐魔盒”的中文软件项目，当前环境以 Windows / PowerShell / Git 协作为主。",
    titleSize: 15.5,
    bodySize: 11.2,
  });
  addCard(user, {
    x: 0.82,
    y: 3.62,
    w: 5.55,
    h: 1.2,
    fill: COLORS.paper,
    accent: COLORS.coral,
    title: "你重视什么",
    body: "重大修改前先做 Git 备份；提交信息用英文；发现 bug、逻辑、性能或结构问题时，希望我主动指出。",
    titleSize: 15.5,
    bodySize: 11.2,
  });
  addCard(user, {
    x: 0.82,
    y: 5.04,
    w: 5.55,
    h: 1.2,
    fill: COLORS.paper,
    accent: COLORS.gold,
    title: "你希望我的工作方式",
    body: "节省额度、不做无谓操作；遇到卡死或闪退可以尝试消息弹窗定位；不仅做结果，也把原理和算法说明白。",
    titleSize: 15.5,
    bodySize: 11.1,
  });
  addCard(user, {
    x: 6.72,
    y: 2.2,
    w: 5.58,
    h: 4.04,
    fill: COLORS.softBlue,
    accent: COLORS.blue,
    title: "这意味着我应该怎样配合你",
    body:
      "1. 先保留可回滚点，再做较大改动。\n2. 尽量直接落地实现，而不是只给空泛建议。\n3. 发现潜在问题要顺手指出，不等你追问。\n4. 重要结论给出处或验证方式。\n5. 在解释方案时，把原理、权衡和算法讲清楚。\n6. 最终目标是做一个会写、会查、会改、会解释的协作搭档。",
    titleSize: 17,
    bodySize: 11.1,
  });
  addFooter(
    user,
    "这页是“当前已知画像”，后续随着合作增多，我对你的理解还会继续细化。"
  );
  finalize(user, pptx);

  await pptx.writeFile({ fileName: OUT_FILE });
  console.log(`Deck written to ${OUT_FILE}`);
}

main().catch((error) => {
  console.error(error);
  process.exitCode = 1;
});
