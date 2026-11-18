# CropPilot：Claude Code 補充指引

共用的協作規則寫在 [AGENTS.md](AGENTS.md)，Claude Code 與 Codex 都依該檔執行。本檔只放 Claude Code 專用的設定。

## Subagent 模型

派 subagent 時依用途以 `model` 參數指定模型，不沿用主對話的模型。

| 用途 | 模型 |
| --- | --- |
| 一般差異審查，例如提交前審查 | `sonnet` |
| 登入、權限、資料庫遷移或跨模組行為變更的獨立審查（AGENTS.md 要求派全新上下文 subagent 的情況） | `opus` |
| 只搜尋或定位檔案，不做判斷 | `haiku`，或使用內建的 Explore agent |

使用者明確指定模型時，依使用者的指定。
