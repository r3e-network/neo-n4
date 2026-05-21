# Neo N4 中文规格书实施计划

> 对应英文计划：[docs/superpowers/plans/2026-05-20-neo-n4-chinese-specification-book.md](../../../../superpowers/plans/2026-05-20-neo-n4-chinese-specification-book.md)

**目标：** 新增一本面向读者的中文 Neo N4 规格书，让读者可以按章节理解设计、架构、实现、运维、测试与安全边界。

**架构：** 新书放在 `docs/zh/specification/`。它不是替代所有现有文档，而是把分散的母版设计、架构专题、运维说明、测试证据和代码入口组织成一条学习路径。

**技术栈：** mdBook Markdown、Mermaid、现有 SVG/PNG 图、NeoHub 可部署合约、r3e Neo core fork、NeoVM2/RISC-V、NeoFS DA、SP1 zkVM、.NET 10、Rust、TypeScript SDK。

---

## 任务

- [ ] 创建中文规格书目录和 8 个章节。
- [ ] 在章节中加入图片、表格、定义、协议对象、代码片段和实现路径。
- [ ] 将规格书接入 `docs/SUMMARY.md` 和 `docs/zh/SUMMARY.md`。
- [ ] 在中文架构导航中把规格书标为推荐长读路径。
- [ ] 运行 `mdbook build`、中文/文档完整性单测和 `git diff --check`。
