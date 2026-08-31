# 貢獻指南 (Contributing Guide)

感謝您對 **CursorMaster (鼠標大師)** 的關注與支持！

---

## 💡 如何參與貢獻

### 1. 提交問題或回報 Bug (Issue)
- 在提交 Issue 前，請先搜尋現有的 Issue 是否已有相關討論。
- 描述問題時請附上：
  - Windows 系統版本（如 Win 10 22H2 / Win 11 23H2 等）
  - 問題發生的具體重現步驟
  - 游標主題檔案類型（.ani / .cur / .inf）
  - 截圖或錯誤訊息記錄

### 2. 功能建議 (Feature Request)
- 歡迎提出新功能的構想或 UI/UX 優化建議。
- 請詳細說明該功能的使用場景與預期效果。

### 3. 提交代碼修改 (Pull Request)
1. Fork 本專案倉庫至您的個人 GitHub 帳號。
2. 建立您的特性分支：`git checkout -b feature/AmazingFeature`。
3. 提交您的變更：`git commit -m "feat: Add some AmazingFeature"`。
4. 推送至遠端分支：`git push origin feature/AmazingFeature`。
5. 在 GitHub 上建立一個 Pull Request。

---

## 🛠️ 代碼風格與規範
- 遵守 C# / .NET 8.0 標準代碼命名規範。
- 盡量保持 Win32 API 呼叫的安全防護與資源釋放（`DestroyIcon`, `IDisposable`）。
- 確保修改後的代碼可在 Windows x64 環境下正常單檔發布與運行。
